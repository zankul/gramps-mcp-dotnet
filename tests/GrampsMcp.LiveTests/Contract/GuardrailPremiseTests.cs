using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrampsMcp.LiveTests.LiveStack;

namespace GrampsMcp.LiveTests.Contract;

/// <summary>
/// Verifies, against a real Gramps Web server via <see cref="LiveGrampsFixture.RawClient"/> — a
/// raw <see cref="HttpClient"/> that bypasses <c>GrampsApiClient</c>/<c>GrampsQueryBuilder</c>
/// entirely — the four upstream-API premises those guardrails assume (see <c>CLAUDE.md</c>'s
/// "Guardrails" section and <c>GrampsQueryBuilder</c>'s own comments). A bug that merely encoded
/// the same wrong assumption into <c>GrampsQueryBuilder</c> would sail through the unit tests,
/// since those only assert against a stub handler; these tests assert against the real server's
/// actual behavior instead.
/// </summary>
[Trait("Tier", "Live")]
[Collection(LiveGrampsCollection.Name)]
public class GuardrailPremiseTests(LiveGrampsFixture fixture)
{
    [Fact]
    public async Task OmittedPage_ReturnsWholeCollection_NotLimitedByPageSize()
    {
        LiveStackGate.Require(fixture);

        using var response = await fixture.RawClient.GetAsync("/api/people/?pagesize=2", CancellationToken.None);
        response.EnsureSuccessStatusCode();

        var people = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var totalCount = ReadTotalCountHeader(response);

        Assert.True(
            people.GetArrayLength() > 2,
            $"Expected an omitted 'page' to return the whole collection (~{ExampleTree.ObjectCounts.People} " +
            $"people), but got only {people.GetArrayLength()} rows — as many as 'pagesize' alone would allow " +
            "on a real page. This is the exact premise GrampsQueryBuilder coerces page<1 to 1 to defend against.");
        Assert.Equal(totalCount, (int?)people.GetArrayLength());
    }

    [Fact]
    public async Task ExplicitPageOne_ReturnsExactlyPageSize()
    {
        LiveStackGate.Require(fixture);

        using var response = await fixture.RawClient.GetAsync("/api/people/?page=1&pagesize=2", CancellationToken.None);
        response.EnsureSuccessStatusCode();

        var people = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(2, people.GetArrayLength());
    }

    /// <summary>
    /// Correction to the informally-stated premise ("page=0 means return the whole collection"):
    /// against a real server, an explicit <c>page=0</c> is rejected outright with a 422 ("Must be
    /// greater than or equal to 1"), not treated as "unpaginated". Only an <em>omitted</em> page
    /// param triggers the whole-collection behavior (see
    /// <see cref="OmittedPage_ReturnsWholeCollection_NotLimitedByPageSize"/>). This matters because
    /// GrampsQueryBuilder's <c>Math.Max(1, page)</c> coercion means our own code never sends an
    /// explicit 0 anyway, so this divergence from the documented assumption is currently harmless
    /// in practice — but the assumption as written is not what the server does.
    /// </summary>
    [Fact]
    public async Task ExplicitPageZero_IsRejected_NotTreatedAsWholeCollection()
    {
        LiveStackGate.Require(fixture);

        using var response = await fixture.RawClient.GetAsync("/api/people/?page=0&pagesize=2", CancellationToken.None);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task TotalCountHeader_OnPaginatedPeopleList_ReflectsTrueTotalNotPageSize()
    {
        LiveStackGate.Require(fixture);

        using var response = await fixture.RawClient.GetAsync("/api/people/?page=1&pagesize=2", CancellationToken.None);
        response.EnsureSuccessStatusCode();

        var totalCount = ReadTotalCountHeader(response);
        var people = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.NotNull(totalCount);
        Assert.NotEqual(people.GetArrayLength(), totalCount!.Value);
        Assert.True(
            totalCount.Value > 1000,
            $"Expected x-total-count near {ExampleTree.ObjectCounts.People}, got {totalCount} — this is exactly " +
            "the corruption GrampsApiClient.ReadTotalCount's items.Count fallback would produce if the header " +
            "were silently missing.");
    }

    [Fact]
    public async Task TotalCountHeader_OnSearch_ReflectsTrueHitCountNotPageSize()
    {
        LiveStackGate.Require(fixture);

        using var response = await fixture.RawClient.GetAsync(
            $"/api/search/?query={Uri.EscapeDataString(ExampleTree.CommonSearchSurname)}&pagesize=1", CancellationToken.None);
        response.EnsureSuccessStatusCode();

        var totalCount = ReadTotalCountHeader(response);
        var hits = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.NotNull(totalCount);
        Assert.NotEqual(hits.GetArrayLength(), totalCount!.Value);
        Assert.Equal(ExampleTree.CommonSearchSurnameHitCount, totalCount.Value);
    }

    [Fact]
    public async Task TotalCountHeader_OnGqlQuery_ReflectsTrueMatchCountNotPageSize()
    {
        LiveStackGate.Require(fixture);

        var gql = Uri.EscapeDataString("gender = 1");
        using var response = await fixture.RawClient.GetAsync(
            $"/api/people/?gql={gql}&page=1&pagesize=1", CancellationToken.None);
        response.EnsureSuccessStatusCode();

        var totalCount = ReadTotalCountHeader(response);
        var matches = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.NotNull(totalCount);
        Assert.NotEqual(matches.GetArrayLength(), totalCount!.Value);
        Assert.True(totalCount.Value > 100, $"Expected many gender=1 matches, got x-total-count={totalCount}.");
    }

    [Fact]
    public async Task TotalCountHeader_OnTransactionHistory_IsPresent()
    {
        LiveStackGate.Require(fixture);

        using var response = await fixture.RawClient.GetAsync("/api/transactions/history/?pagesize=1", CancellationToken.None);
        response.EnsureSuccessStatusCode();

        var totalCount = ReadTotalCountHeader(response);

        Assert.NotNull(totalCount);
        Assert.True(totalCount!.Value >= 1);
    }

    [Fact]
    public async Task SingleObjectGet_RejectsListStyleQueryParam()
    {
        LiveStackGate.Require(fixture);

        using var response = await fixture.RawClient.GetAsync(
            $"/api/people/{ExampleTree.AnchorPersonHandle}?page=1", CancellationToken.None);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>
    /// Uses a genuinely malformed GQL expression discovered empirically against the real server
    /// (an unclosed group: <c>gramps_id = (</c>) rather than one invented for a mock — a plausible
    /// guess (e.g. an unsupported <c>~</c> operator) turned out to parse fine upstream and return
    /// an empty result set instead of erroring, so it would have made this test pass for the wrong
    /// reason.
    /// </summary>
    [Fact]
    public async Task MalformedGql_Returns422WithParsePositionDetailVerbatim()
    {
        LiveStackGate.Require(fixture);

        var gql = Uri.EscapeDataString("gramps_id = (");
        using var response = await fixture.RawClient.GetAsync($"/api/people/?gql={gql}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.Contains("char", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pins docs/PLAN.md fact #14: GQL endpoints honor <c>profile</c>, unlike <c>/api/search/</c>
    /// (see <see cref="ApiClientContractTests.Search_SilentlyIgnoresProfileParameter_RatherThanRejectingIt"/>,
    /// which silently ignores it). This corrected a stale comment in
    /// <c>GrampsQueryBuilder.BuildForGql</c> that cited fact #5 for a claim fact #5 never made.
    /// </summary>
    [Fact]
    public async Task Gql_HonorsProfileParameter_AddingResolvedProfileSection()
    {
        LiveStackGate.Require(fixture);

        var gql = Uri.EscapeDataString($"gramps_id = \"{ExampleTree.AnchorPersonGrampsId}\"");
        using var response = await fixture.RawClient.GetAsync(
            $"/api/people/?gql={gql}&profile=self", CancellationToken.None);
        response.EnsureSuccessStatusCode();

        var people = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(1, people.GetArrayLength());
        Assert.True(
            people[0].TryGetProperty("profile", out var profile),
            "Expected profile=self on a GQL query to add a resolved 'profile' section, same as the " +
            "plain list endpoint — GQL does not reject or ignore it.");
        Assert.True(profile.TryGetProperty("name_display", out _), "Expected profile.name_display to be resolved.");
    }

    /// <summary>Pins the <c>extend</c> half of fact #14.</summary>
    [Fact]
    public async Task Gql_HonorsExtendParameter_AddingExtendedSection()
    {
        LiveStackGate.Require(fixture);

        var gql = Uri.EscapeDataString($"gramps_id = \"{ExampleTree.AnchorPersonGrampsId}\"");
        using var response = await fixture.RawClient.GetAsync(
            $"/api/people/?gql={gql}&extend=family_list", CancellationToken.None);
        response.EnsureSuccessStatusCode();

        var people = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(1, people.GetArrayLength());
        Assert.True(
            people[0].TryGetProperty("extended", out _),
            "Expected extend=family_list on a GQL query to add an 'extended' section — GQL does not " +
            "reject or ignore it.");
    }

    /// <summary>Pins the <c>skipkeys</c> half of fact #14.</summary>
    [Fact]
    public async Task Gql_HonorsSkipkeysParameter_OmittingNamedKeys()
    {
        LiveStackGate.Require(fixture);

        var gql = Uri.EscapeDataString($"gramps_id = \"{ExampleTree.AnchorPersonGrampsId}\"");
        using var response = await fixture.RawClient.GetAsync(
            $"/api/people/?gql={gql}&skipkeys=citation_list,note_list", CancellationToken.None);
        response.EnsureSuccessStatusCode();

        var people = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(1, people.GetArrayLength());
        Assert.False(people[0].TryGetProperty("citation_list", out _), "Expected skipkeys to drop 'citation_list'.");
        Assert.False(people[0].TryGetProperty("note_list", out _), "Expected skipkeys to drop 'note_list'.");
    }

    private static int? ReadTotalCountHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-total-count", out var values) &&
        int.TryParse(values.FirstOrDefault(), out var total)
            ? total
            : null;
}
