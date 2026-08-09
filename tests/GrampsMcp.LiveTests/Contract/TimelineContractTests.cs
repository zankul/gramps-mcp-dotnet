using System.Text.Json;
using GrampsMcp.LiveTests.LiveStack;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;

namespace GrampsMcp.LiveTests.Contract;

/// <summary>
/// Verifies the timeline endpoint's param spellings (<c>event_classes</c>, <c>events</c>,
/// <c>dates</c>, <c>ancestors</c>, <c>offspring</c>) against a real server — nothing else in the
/// suite checks these against anything but a stub handler, and <c>GrampsQueryBuilder.BuildForTimeline</c>
/// is exactly the kind of param-spelling guardrail a wrong guess would sail through unit tests on.
/// All counts/compositions below were read back from the live stack (not guessed) against the
/// anchor person (<see cref="ExampleTree.AnchorPersonGrampsId"/>, a well-connected individual with
/// 3 families and 3 events) before being written as assertions.
/// </summary>
[Trait("Tier", "Live")]
[Collection(LiveGrampsCollection.Name)]
public class TimelineContractTests(LiveGrampsFixture fixture)
{
    // Read back from the live stack against the anchor person (see the class-level doc comment) —
    // reused across several tests below so a future dataset re-capture only needs updating once.
    private const int AnchorPersonDefaultTimelineEventCount = 15;
    private const int AnchorPersonOffspringFiveTimelineEventCount = 21;

    [Fact]
    public async Task Timeline_NoFilters_ReturnsAnchorPersonsFifteenEventsAcrossFourTypes()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsTimelineRequest { PageSize = 100 };
        var result = await fixture.ApiClient.ListTimelineAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}/timeline", request, CancellationToken.None);

        Assert.Equal(AnchorPersonDefaultTimelineEventCount, result.Items.Count);
        var typeCounts = CountByType(result.Items);
        Assert.Equal(6, typeCounts.GetValueOrDefault("Birth"));
        Assert.Equal(4, typeCounts.GetValueOrDefault("Marriage"));
        Assert.Equal(4, typeCounts.GetValueOrDefault("Death"));
        Assert.Equal(1, typeCounts.GetValueOrDefault("Burial"));
    }

    /// <summary>
    /// <c>event_classes=vital</c> keeps the one Burial event (its class is "vital"); a non-vital
    /// class (e.g. "family") narrows it out while leaving Birth/Marriage/Death untouched — those
    /// three appear to always be included regardless of the requested class on this endpoint, so
    /// the narrowing this test can observe is specifically the Burial event's disappearance.
    /// </summary>
    [Fact]
    public async Task Timeline_EventClasses_NonVitalClass_NarrowsOutBurialButKeepsVitalTrio()
    {
        LiveStackGate.Require(fixture);

        var vitalRequest = new GrampsTimelineRequest { PageSize = 100, EventClasses = ["vital"] };
        var vitalResult = await fixture.ApiClient.ListTimelineAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}/timeline", vitalRequest, CancellationToken.None);

        var familyRequest = new GrampsTimelineRequest { PageSize = 100, EventClasses = ["family"] };
        var familyResult = await fixture.ApiClient.ListTimelineAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}/timeline", familyRequest, CancellationToken.None);

        Assert.Equal(AnchorPersonDefaultTimelineEventCount, vitalResult.Items.Count);
        Assert.Contains("Burial", CountByType(vitalResult.Items).Keys);

        Assert.Equal(14, familyResult.Items.Count);
        Assert.DoesNotContain("Burial", CountByType(familyResult.Items).Keys);
        var familyTypeCounts = CountByType(familyResult.Items);
        Assert.Equal(6, familyTypeCounts.GetValueOrDefault("Birth"));
        Assert.Equal(4, familyTypeCounts.GetValueOrDefault("Marriage"));
        Assert.Equal(4, familyTypeCounts.GetValueOrDefault("Death"));
    }

    /// <summary>
    /// A correctly-spelled but nonexistent event type name (as opposed to an event class) is
    /// rejected with a 422, confirming <c>events</c> is validated against a real enum upstream —
    /// the actual risk this test guards against, since a typo in <c>GrampsQueryBuilder</c>'s param
    /// name (not the value) would otherwise sail through unit tests silently.
    /// </summary>
    [Fact]
    public async Task Timeline_Events_UnknownEventTypeName_Rejected()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsTimelineRequest { PageSize = 100, Events = ["NotARealGrampsEventType"] };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(() => fixture.ApiClient.ListTimelineAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}/timeline", request, CancellationToken.None));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, exception.StatusCode);
    }

    /// <summary>
    /// <c>events=["Birth"]</c> does not, contrary to a plausible first guess, narrow the timeline
    /// down to only Birth events — verified against the real server, it returns the same 14-event
    /// set (Birth 6, Marriage 4, Death 4) that <c>event_classes=family</c> produces, i.e. it
    /// excludes Burial but leaves Marriage/Death untouched despite neither being "Birth". The
    /// param's real narrowing behavior on this endpoint is not what its name suggests; asserting
    /// the actual observed count (rather than the originally-assumed 6) still proves it reaches the
    /// upstream filter rather than being silently dropped, which is the guardrail this test exists
    /// to check.
    /// </summary>
    [Fact]
    public async Task Timeline_Events_ValidEventTypeName_AcceptedAndAppliesNarrowing()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsTimelineRequest { PageSize = 100, Events = ["Birth"] };
        var result = await fixture.ApiClient.ListTimelineAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}/timeline", request, CancellationToken.None);

        Assert.Equal(14, result.Items.Count);
        Assert.DoesNotContain("Burial", CountByType(result.Items).Keys);
    }

    /// <summary>
    /// Ancestors/offspring must each be between 1 and 5 (a 0 is rejected with a 422) — worth
    /// documenting since neither the tool description nor <c>GrampsQueryBuilder</c>'s comments say
    /// so; our code never sends 0 today (the tool's params are nullable/omit-if-absent), so this is
    /// currently harmless in practice, same shape of finding as
    /// <c>GuardrailPremiseTests.ExplicitPageZero_IsRejected_NotTreatedAsWholeCollection</c>.
    /// </summary>
    [Fact]
    public async Task Timeline_AncestorsZero_IsRejected()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsTimelineRequest { PageSize = 100, Ancestors = 0, Offspring = 0 };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(() => fixture.ApiClient.ListTimelineAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}/timeline", request, CancellationToken.None));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, exception.StatusCode);
    }

    /// <summary>
    /// The anchor person's own ancestors have no recorded events in this dataset, so
    /// <c>ancestors</c> alone doesn't move the count here — but <c>offspring</c> does: his son
    /// (<see cref="ExampleTree.RelatedChildGrampsId"/>, kinship "son") has his own births/marriages/
    /// deaths, so widening offspring from 1 to 5 generations pulls in more descendants' events.
    /// Measured against the real server: offspring=1 gives the same 15 events as the default;
    /// offspring=5 grows to 21 (Birth 6→11, Death 4→5), confirming the param actually reaches the
    /// upstream generation-walk rather than being silently dropped.
    /// </summary>
    [Fact]
    public async Task Timeline_Offspring_WideningGenerations_GrowsEventSet()
    {
        LiveStackGate.Require(fixture);

        var narrowRequest = new GrampsTimelineRequest { PageSize = 100, Ancestors = 1, Offspring = 1 };
        var narrowResult = await fixture.ApiClient.ListTimelineAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}/timeline", narrowRequest, CancellationToken.None);

        var wideRequest = new GrampsTimelineRequest { PageSize = 100, Ancestors = 1, Offspring = 5 };
        var wideResult = await fixture.ApiClient.ListTimelineAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}/timeline", wideRequest, CancellationToken.None);

        Assert.Equal(AnchorPersonDefaultTimelineEventCount, narrowResult.Items.Count);
        Assert.Equal(AnchorPersonOffspringFiveTimelineEventCount, wideResult.Items.Count);
        Assert.True(
            wideResult.Items.Count > narrowResult.Items.Count,
            "Expected offspring=5 to pull in more descendant events than offspring=1.");
    }

    /// <summary>
    /// Correction to the tool's own <c>[Description]</c> for <c>dates</c> ("'-y/m/d' (before)"):
    /// measured against the real server, a zero-padded month/day (e.g. <c>1850/01/01-</c>) is
    /// rejected with a 422 ("String does not match expected pattern"), while the unpadded form
    /// (<c>1850/1/1-</c>) is accepted. The description's example doesn't specify padding either
    /// way, so a model following it literally with padding (a natural reading of "y/m/d") would hit
    /// a 422 on a well-formed-looking request — worth flagging for a possible description tweak,
    /// not fixed here since it's a wording/doc call rather than a code bug.
    /// </summary>
    [Fact]
    public async Task Timeline_Dates_RejectsZeroPaddedButAcceptsUnpaddedMonthAndDay()
    {
        LiveStackGate.Require(fixture);

        var paddedRequest = new GrampsTimelineRequest { PageSize = 100, Dates = "1850/01/01-" };
        var paddedException = await Assert.ThrowsAsync<GrampsApiException>(() => fixture.ApiClient.ListTimelineAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}/timeline", paddedRequest, CancellationToken.None));
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, paddedException.StatusCode);

        var unpaddedRequest = new GrampsTimelineRequest { PageSize = 100, Dates = "1850/1/1-" };
        var unpaddedResult = await fixture.ApiClient.ListTimelineAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}/timeline", unpaddedRequest, CancellationToken.None);

        Assert.Equal(7, unpaddedResult.Items.Count);
    }

    /// <summary>
    /// Goes through <see cref="GrampsTimelineTool"/> itself (not just <see cref="GrampsApiClient"/>)
    /// with every filter param this step targets set at once — <c>eventClasses</c>, <c>events</c>,
    /// <c>dates</c>, <c>ancestors</c>, <c>offspring</c> — resolving the anchor person by Gramps ID
    /// through <c>GrampsHandleResolver</c> on the way in.
    /// </summary>
    [Fact]
    public async Task GrampsTimelineTool_WithAllFilterParamsSet_SucceedsAgainstRealServer()
    {
        LiveStackGate.Require(fixture);

        var tool = new GrampsTimelineTool(fixture.ApiClient);
        var result = await tool.TimelineAsync(
            "person",
            ExampleTree.AnchorPersonGrampsId,
            ancestors: 2,
            offspring: 2,
            eventClasses: ["vital", "family"],
            events: ["Birth", "Marriage"],
            dates: "1800/1/1-1900/1/1",
            page: 1,
            pagesize: 50,
            keys: null,
            CancellationToken.None);

        Assert.NotEmpty(result.Items);
    }

    private static Dictionary<string, int> CountByType(IEnumerable<JsonElement> events)
    {
        var counts = new Dictionary<string, int>();
        foreach (var eventElement in events)
        {
            var type = eventElement.GetProperty("type").GetString()!;
            counts[type] = counts.GetValueOrDefault(type) + 1;
        }

        return counts;
    }
}
