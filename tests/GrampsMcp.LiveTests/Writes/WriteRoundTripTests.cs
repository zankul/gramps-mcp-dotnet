using System.Net;
using System.Text.Json;
using GrampsMcp.LiveTests.LiveStack;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;

namespace GrampsMcp.LiveTests.Writes;

/// <summary>
/// Full write round-trips against the live stack — Phase 5's done-criterion
/// ("create → update → verify → undo round-trips against a local container", <c>docs/PLAN.md</c>).
/// Every object these tests create lives in the reserved <see cref="ZzTestNamespace"/>, never
/// touches the seeded <c>example.gramps</c> data, and is removed by one of three layers: a
/// per-test <see cref="TestObjectScope"/>, an opening <see cref="ZzTestSweep"/> for leftovers from
/// a killed prior run, and a closing sweep inside <see cref="ExampleTreeUnchanged_AfterWriteSuite"/>
/// (forced to run last by <see cref="WriteRoundTripOrderer"/>). <see cref="LoopbackGuard"/> refuses
/// to run any of this against a non-local stack.
/// </summary>
[Trait("Tier", "Live")]
[Collection(LiveGrampsCollection.Name)]
[TestCaseOrderer("GrampsMcp.LiveTests.Writes.WriteRoundTripOrderer", "GrampsMcp.LiveTests")]
public class WriteRoundTripTests(LiveGrampsFixture fixture)
{
    // xunit constructs a fresh WriteRoundTripTests instance per [Fact], so "run the opening sweep
    // exactly once" needs state that survives that — a static, lazily-started Task shared by every
    // instance in this process. Guards against a killed previous run's leftovers without paying
    // the sweep's ~8-query cost more than once per test run.
    private static Task<int>? _openingSweep;
    private static readonly object OpeningSweepGate = new();

    private static Task<int> EnsureOpeningSweepAsync(GrampsApiClient apiClient)
    {
        lock (OpeningSweepGate)
        {
            _openingSweep ??= ZzTestSweep.RunAsync(apiClient, CancellationToken.None);
        }

        return _openingSweep;
    }

    [Fact]
    public async Task Create_Get_Update_Verify_Delete_RoundTrips()
    {
        LiveStackGate.Require(fixture);
        LoopbackGuard.RequireLocal(fixture.Options.ApiUrl);
        await EnsureOpeningSweepAsync(fixture.ApiClient);

        var createTool = new GrampsCreateTool(fixture.ApiClient);
        var getTool = new GrampsGetTool(fixture.ApiClient);
        var updateTool = new GrampsUpdateTool(fixture.ApiClient);
        var deleteTool = new GrampsDeleteTool(fixture.ApiClient);

        await using var scope = new TestObjectScope(fixture.ApiClient);

        var grampsId = ZzTestNamespace.NextGrampsId();
        var payload = JsonSerializer.SerializeToElement(new
        {
            gramps_id = grampsId,
            primary_name = new
            {
                first_name = "WriteRoundTrip",
                surname_list = new[] { new { surname = "ZzTestSurname" } },
            },
            gender = 2,
        });

        var createResult = await createTool.CreateAsync("person", payload, CancellationToken.None);
        var handle = createResult[0].GetProperty("handle").GetString()!;
        scope.Track("person", handle);

        var created = await getTool.GetAsync("person", handle, extend: null, profile: "none", CancellationToken.None);
        Assert.Equal(grampsId, created.GetProperty("gramps_id").GetString());
        Assert.Equal("WriteRoundTrip", created.GetProperty("primary_name").GetProperty("first_name").GetString());
        Assert.Equal(
            "ZzTestSurname",
            created.GetProperty("primary_name").GetProperty("surname_list")[0].GetProperty("surname").GetString());

        // The update step is the real point of this test: GrampsUpdateTool reads the full current
        // object, merges the patch client-side, then PUTs the whole thing back, because Gramps
        // Web's PUT replaces the entire object. Proving that round-trip means asserting the
        // patched field changed *and* an unrelated field (the surname, untouched by the patch)
        // survived — a naive "just PUT the patch" implementation would have wiped it.
        var patch = JsonSerializer.SerializeToElement(new
        {
            primary_name = new { first_name = "WriteRoundTripUpdated" },
        });
        var updateResult = await updateTool.UpdateAsync("person", handle, patch, CancellationToken.None);

        Assert.Equal(
            "WriteRoundTripUpdated",
            updateResult.GetProperty("primary_name").GetProperty("first_name").GetString());
        Assert.Equal(
            "ZzTestSurname",
            updateResult.GetProperty("primary_name").GetProperty("surname_list")[0].GetProperty("surname").GetString());
        Assert.Equal(grampsId, updateResult.GetProperty("gramps_id").GetString());

        // Verify against the server too, not just the tool's own optimistic merged return value.
        var verified = await getTool.GetAsync("person", handle, extend: null, profile: "none", CancellationToken.None);
        Assert.Equal("WriteRoundTripUpdated", verified.GetProperty("primary_name").GetProperty("first_name").GetString());
        Assert.Equal(
            "ZzTestSurname",
            verified.GetProperty("primary_name").GetProperty("surname_list")[0].GetProperty("surname").GetString());

        await deleteTool.DeleteAsync("person", handle, CancellationToken.None);

        var afterDelete = await Assert.ThrowsAsync<GrampsApiException>(() =>
            fixture.ApiClient.GetAsync<JsonElement>(
                $"/api/people/{handle}", new GrampsGetRequest { Profile = "none" }, CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Merge_KeepsTarget_DiscardsSource()
    {
        LiveStackGate.Require(fixture);
        LoopbackGuard.RequireLocal(fixture.Options.ApiUrl);
        await EnsureOpeningSweepAsync(fixture.ApiClient);

        var createTool = new GrampsCreateTool(fixture.ApiClient);
        var mergeTool = new GrampsMergeTool(fixture.ApiClient);

        await using var scope = new TestObjectScope(fixture.ApiClient);

        var (keepHandle, keepGrampsId) = await CreateTestPersonAsync(createTool, scope, "MergeKeep");
        var (discardHandle, _) = await CreateTestPersonAsync(createTool, scope, "MergeDiscard");

        await mergeTool.MergeAsync("person", keepHandle, discardHandle, options: null, CancellationToken.None);

        var survivor = await fixture.ApiClient.GetAsync<JsonElement>(
            $"/api/people/{keepHandle}", new GrampsGetRequest { Profile = "none" }, CancellationToken.None);
        Assert.Equal(keepGrampsId, survivor.GetProperty("gramps_id").GetString());
        Assert.Equal("MergeKeep", survivor.GetProperty("primary_name").GetProperty("first_name").GetString());

        var discardedGone = await Assert.ThrowsAsync<GrampsApiException>(() =>
            fixture.ApiClient.GetAsync<JsonElement>(
                $"/api/people/{discardHandle}", new GrampsGetRequest { Profile = "none" }, CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound, discardedGone.StatusCode);
    }

    [Fact]
    public async Task History_ThenUndo_ReversesTheTransaction()
    {
        LiveStackGate.Require(fixture);
        LoopbackGuard.RequireLocal(fixture.Options.ApiUrl);
        await EnsureOpeningSweepAsync(fixture.ApiClient);

        var createTool = new GrampsCreateTool(fixture.ApiClient);
        var historyTool = new GrampsHistoryTool(fixture.ApiClient);
        var undoTool = new GrampsUndoTool(fixture.ApiClient);
        var mergeTool = new GrampsMergeTool(fixture.ApiClient);

        await using var scope = new TestObjectScope(fixture.ApiClient);

        // --- Part A: the unforced path, since that's what gramps_undo exposes to a model by
        // default (force defaults to false). Undoing the most recent transaction (this create)
        // with nothing else touching the same object afterward is exactly the case an unforced
        // undo is meant to handle.
        var (handle, _) = await CreateTestPersonAsync(createTool, scope, "UndoRoundTrip");

        var createTransactionId = await LatestTransactionIdAsync(historyTool);
        var unforcedTask = await RunUndoAsync(undoTool, createTransactionId, force: false);
        Assert.Equal("SUCCESS", unforcedTask.GetProperty("state").GetString());

        var afterUndo = await Assert.ThrowsAsync<GrampsApiException>(() =>
            fixture.ApiClient.GetAsync<JsonElement>(
                $"/api/people/{handle}", new GrampsGetRequest { Profile = "none" }, CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound, afterUndo.StatusCode);

        // --- Part B: force=true. What actually gates an unforced undo turned out not to be
        // "is this the latest transaction" — undoing an *earlier*, non-conflicting transaction
        // (an independent object's own create) succeeds unforced too, verified by hand against
        // this stack. It's specific to merges: undoing a merge transaction unforced deterministically
        // fails with "Object has changed", even when it *is* the latest transaction and nothing
        // else touched either object afterward. force=true is what a model needs to reverse one.
        var (keepHandle, _) = await CreateTestPersonAsync(createTool, scope, "UndoMergeKeep");
        var (discardHandle, discardGrampsId) = await CreateTestPersonAsync(createTool, scope, "UndoMergeDiscard");

        await mergeTool.MergeAsync("person", keepHandle, discardHandle, options: null, CancellationToken.None);
        var mergeTransactionId = await LatestTransactionIdAsync(historyTool);

        var unforcedMergeUndo = await RunUndoAsync(undoTool, mergeTransactionId, force: false);
        Assert.Equal(
            "FAILURE",
            unforcedMergeUndo.GetProperty("state").GetString());

        var forcedMergeUndo = await RunUndoAsync(undoTool, mergeTransactionId, force: true);
        Assert.Equal("SUCCESS", forcedMergeUndo.GetProperty("state").GetString());

        var restoredDiscard = await fixture.ApiClient.GetAsync<JsonElement>(
            $"/api/people/{discardHandle}", new GrampsGetRequest { Profile = "none" }, CancellationToken.None);
        Assert.Equal(discardGrampsId, restoredDiscard.GetProperty("gramps_id").GetString());
    }

    /// <summary>
    /// Re-runs the <c>object_counts</c> fingerprint <see cref="SeededTreeTests"/> guards, after a
    /// closing <see cref="ZzTestSweep"/> pass, asserting an *exact* match against
    /// <see cref="ExampleTree.ObjectCounts"/> — not the ±10% drift-tolerance band
    /// <c>SeededTreeTests</c> uses for cross-import variance, since nothing else writes to this
    /// stack. Any difference here means a write test above leaked an object that none of the three
    /// cleanup layers caught. <see cref="WriteRoundTripOrderer"/> forces this to run last among
    /// this class's own tests so it observes their cleanup rather than racing it.
    /// </summary>
    [Fact]
    public async Task ExampleTreeUnchanged_AfterWriteSuite()
    {
        LiveStackGate.Require(fixture);
        LoopbackGuard.RequireLocal(fixture.Options.ApiUrl);

        // In case this somehow ran before any other test in this class needed the opening sweep
        // (e.g. this class is ever selected in isolation via --filter).
        await EnsureOpeningSweepAsync(fixture.ApiClient);
        await ZzTestSweep.RunAsync(fixture.ApiClient, CancellationToken.None);

        var metadata = await fixture.ApiClient.GetAsync<JsonElement>("/api/metadata/", CancellationToken.None);
        Assert.True(metadata.TryGetProperty("object_counts", out var counts));

        AssertExactCount(counts, "people", ExampleTree.ObjectCounts.People);
        AssertExactCount(counts, "families", ExampleTree.ObjectCounts.Families);
        AssertExactCount(counts, "events", ExampleTree.ObjectCounts.Events);
        AssertExactCount(counts, "places", ExampleTree.ObjectCounts.Places);
        AssertExactCount(counts, "citations", ExampleTree.ObjectCounts.Citations);
        AssertExactCount(counts, "sources", ExampleTree.ObjectCounts.Sources);
        AssertExactCount(counts, "repositories", ExampleTree.ObjectCounts.Repositories);
        AssertExactCount(counts, "media", ExampleTree.ObjectCounts.Media);
        AssertExactCount(counts, "notes", ExampleTree.ObjectCounts.Notes);
        AssertExactCount(counts, "tags", ExampleTree.ObjectCounts.Tags);
    }

    private static async Task<(string Handle, string GrampsId)> CreateTestPersonAsync(
        GrampsCreateTool createTool, TestObjectScope scope, string firstName)
    {
        var grampsId = ZzTestNamespace.NextGrampsId();
        var payload = JsonSerializer.SerializeToElement(new
        {
            gramps_id = grampsId,
            primary_name = new { first_name = firstName, surname_list = new[] { new { surname = "ZzTestSurname" } } },
            gender = 1,
        });

        var createResult = await createTool.CreateAsync("person", payload, CancellationToken.None);
        var handle = createResult[0].GetProperty("handle").GetString()!;
        scope.Track("person", handle);

        return (handle, grampsId);
    }

    private static async Task<int> LatestTransactionIdAsync(GrampsHistoryTool historyTool)
    {
        var history = await historyTool.ListAsync(page: 1, pagesize: 1, sort: "-id", CancellationToken.None);
        Assert.NotEmpty(history.Items);
        return history.Items[0].GetProperty("id").GetInt32();
    }

    private async Task<JsonElement> RunUndoAsync(GrampsUndoTool undoTool, int transactionId, bool force)
    {
        var undoResponse = await undoTool.UndoAsync(transactionId, force, message: null, CancellationToken.None);
        var taskId = GrampsUndoTaskPoller.ExtractTaskId(undoResponse);
        return await GrampsUndoTaskPoller.WaitForCompletionAsync(fixture.ApiClient, taskId, CancellationToken.None);
    }

    private static void AssertExactCount(JsonElement counts, string field, int expected)
    {
        Assert.True(counts.TryGetProperty(field, out var actualProperty), $"object_counts is missing field '{field}'.");
        var actual = actualProperty.GetInt32();
        Assert.True(
            actual == expected,
            $"object_counts.{field} was {actual}, expected exactly {expected} (ExampleTree's captured " +
            "fingerprint) after the write suite's cleanup — a write test above leaked an object.");
    }
}
