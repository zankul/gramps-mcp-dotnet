using System.Text.Json;
using GrampsMcp.LiveTests.LiveStack;
using GrampsMcp.Server.Gramps;
using Xunit.Sdk;

namespace GrampsMcp.LiveTests;

/// <summary>
/// Guards that the seeded <c>example.gramps</c> tree still matches what <see cref="ExampleTree"/>
/// captured. These exist to catch drift early and diagnostically — an upstream image refresh that
/// silently changes the bundled dataset should fail here with a clear "which field, expected vs
/// actual, re-capture like this" message, not surface as a baffling failure three tests later.
/// </summary>
[Trait("Tier", "Live")]
[Collection(LiveGrampsCollection.Name)]
public class SeededTreeTests(LiveGrampsFixture fixture)
{
    private const string RecaptureHint =
        "See docs/testing.md's \"Capturing ExampleTree\" section for the re-capture procedure.";

    /// <summary>±10% band: exact re-import can shift record counts slightly (id ordering,
    /// generator nondeterminism) without meaning the dataset actually changed shape.</summary>
    private const double ToleranceFraction = 0.10;

    [Fact]
    public async Task SeededTree_ObjectCounts_MatchCapturedFingerprint()
    {
        LiveStackGate.Require(fixture);

        var metadata = await fixture.ApiClient.GetAsync<JsonElement>("/api/metadata/", CancellationToken.None);
        Assert.True(metadata.TryGetProperty("object_counts", out var counts));

        AssertWithinTolerance(counts, "people", ExampleTree.ObjectCounts.People);
        AssertWithinTolerance(counts, "families", ExampleTree.ObjectCounts.Families);
        AssertWithinTolerance(counts, "events", ExampleTree.ObjectCounts.Events);
        AssertWithinTolerance(counts, "places", ExampleTree.ObjectCounts.Places);
        AssertWithinTolerance(counts, "citations", ExampleTree.ObjectCounts.Citations);
        AssertWithinTolerance(counts, "sources", ExampleTree.ObjectCounts.Sources);
        AssertWithinTolerance(counts, "repositories", ExampleTree.ObjectCounts.Repositories);
        AssertWithinTolerance(counts, "media", ExampleTree.ObjectCounts.Media);
        AssertWithinTolerance(counts, "notes", ExampleTree.ObjectCounts.Notes);
        AssertWithinTolerance(counts, "tags", ExampleTree.ObjectCounts.Tags);
    }

    public static TheoryData<string, string, string> Anchors => new()
    {
        { "person", ExampleTree.AnchorPersonGrampsId, ExampleTree.AnchorPersonHandle },
        { "family", ExampleTree.AnchorFamilyGrampsId, ExampleTree.AnchorFamilyHandle },
        { "place", ExampleTree.AnchorPlaceGrampsId, ExampleTree.AnchorPlaceHandle },
        { "event", ExampleTree.AnchorEventGrampsId, ExampleTree.AnchorEventHandle },
    };

    [Theory]
    [MemberData(nameof(Anchors))]
    public async Task SeededTree_AnchorId_Resolves(string type, string grampsId, string expectedHandle)
    {
        LiveStackGate.Require(fixture);

        await AssertAnchorResolves(type, grampsId, expectedHandle);
    }

    private async Task AssertAnchorResolves(string type, string grampsId, string expectedHandle)
    {
        // Reuses the same Gramps-ID-to-handle resolution the tools rely on (GrampsHandleResolver),
        // rather than hand-rolling a parallel FindByGrampsIdAsync call — and, since it returns the
        // resolved handle, lets this test cross-check it against the handle ExampleTree captured
        // live, catching drift the Gramps ID alone wouldn't reveal (e.g. a re-seed that reassigns
        // handles but keeps IDs stable).
        var resolvedHandle = await GrampsHandleResolver.ResolveAsync(
            fixture.ApiClient, type, grampsId, CancellationToken.None);

        Assert.True(
            resolvedHandle == expectedHandle,
            $"Anchor {type} '{grampsId}' resolved to handle '{resolvedHandle}', but ExampleTree recorded " +
            $"'{expectedHandle}' — the seeded tree's handles have drifted even though the Gramps ID still " +
            $"resolves. {RecaptureHint}");
    }

    private static void AssertWithinTolerance(JsonElement counts, string field, int expected)
    {
        Assert.True(
            counts.TryGetProperty(field, out var actualProperty),
            $"object_counts is missing field '{field}' (expected ~{expected} from ExampleTree). {RecaptureHint}");

        var actual = actualProperty.GetInt32();
        var lowerBound = (int)Math.Floor(expected * (1 - ToleranceFraction));
        var upperBound = (int)Math.Ceiling(expected * (1 + ToleranceFraction));

        try
        {
            Assert.InRange(actual, lowerBound, upperBound);
        }
        catch (InRangeException)
        {
            Assert.Fail(
                $"object_counts.{field} drifted outside the ±{ToleranceFraction:P0} band around the " +
                $"ExampleTree fingerprint: expected {expected} (allowed [{lowerBound}, {upperBound}]), " +
                $"got {actual}. {RecaptureHint}");
        }
    }
}
