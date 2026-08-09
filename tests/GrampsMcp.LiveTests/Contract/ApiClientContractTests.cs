using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrampsMcp.LiveTests.LiveStack;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;

namespace GrampsMcp.LiveTests.Contract;

/// <summary>
/// Verifies <see cref="GrampsApiClient"/>'s own request-building and response-parsing behavior
/// against a real Gramps Web server, as opposed to <see cref="GuardrailPremiseTests"/> (which
/// deliberately bypasses our code via <see cref="LiveGrampsFixture.RawClient"/> to prove upstream
/// premises in isolation). A bug in <c>GrampsQueryBuilder</c> or the client's response mapping
/// would sail through the unit tests, which only assert against a stub handler; these tests assert
/// that our code produces correct behavior against the real server.
/// </summary>
[Trait("Tier", "Live")]
[Collection(LiveGrampsCollection.Name)]
public class ApiClientContractTests(LiveGrampsFixture fixture)
{
    [Fact]
    public async Task ListAsync_WithCommaJoinedKeys_ReturnsObjectsContainingBothKeys()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsListRequest { PageSize = 5, Keys = ["handle", "gramps_id"] };
        var result = await fixture.ApiClient.ListAsync<JsonElement>(
            "/api/people/", request, CancellationToken.None);

        Assert.NotEmpty(result.Items);
        foreach (var person in result.Items)
        {
            Assert.True(person.TryGetProperty("handle", out _), "Row is missing 'handle' — comma-joined keys param dropped a value.");
            Assert.True(person.TryGetProperty("gramps_id", out _), "Row is missing 'gramps_id' — comma-joined keys param dropped a value.");
        }
    }

    [Fact]
    public async Task GetAsync_WithDefaultProfile_ResolvesReadableProfileSections()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsGetRequest { Profile = "self,families,events" };
        var person = await fixture.ApiClient.GetAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}", request, CancellationToken.None);

        Assert.True(person.TryGetProperty("profile", out var profile), "Expected a 'profile' section with profile=self,families,events.");
        Assert.True(profile.TryGetProperty("name_given", out var givenName), "Expected profile.name_given to be resolved.");
        Assert.False(string.IsNullOrWhiteSpace(givenName.GetString()), "profile.name_given was resolved but blank.");
        Assert.True(profile.TryGetProperty("families", out var families), "Expected profile.families to be present with profile=self,families,events.");
        Assert.True(families.GetArrayLength() > 0, "Anchor person has families in ExampleTree, but profile.families came back empty.");
    }

    [Fact]
    public async Task GetAsync_WithProfileNone_OmitsProfileSection()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsGetRequest { Profile = "none" };
        var person = await fixture.ApiClient.GetAsync<JsonElement>(
            $"/api/people/{ExampleTree.AnchorPersonHandle}", request, CancellationToken.None);

        Assert.False(person.TryGetProperty("profile", out _), "Expected no 'profile' section with profile=none.");
    }

    /// <summary>
    /// <c>GrampsQueryBuilder.BuildForSearch</c> only ever calls <c>BuildPagination</c> — it has no
    /// code path that can emit a <c>profile</c> parameter, so there is no way to trigger this
    /// through <see cref="GrampsApiClient.SearchAsync"/>'s public surface; the guarantee is
    /// structural, not runtime-observable via our client. This one assertion goes through
    /// <see cref="LiveGrampsFixture.RawClient"/> instead, to check the premise motivating that
    /// design (<c>CLAUDE.md</c>'s "/api/search/ ... rejects the profile/extend/keys/skipkeys
    /// params the other list endpoints accept"). Measured against the real server, that premise is
    /// only half right for <c>profile</c>: <c>/api/search/</c> does not 422 on it — it silently
    /// ignores it and returns 200 with an unshaped response, same as omitting it entirely. That's a
    /// documentation correction worth flagging (see the PR description), not a bug: it doesn't
    /// change that <c>BuildForSearch</c> is right to never send <c>profile</c>, since sending it
    /// would be dead weight rather than useful shaping either way.
    /// </summary>
    [Fact]
    public async Task Search_SilentlyIgnoresProfileParameter_RatherThanRejectingIt()
    {
        LiveStackGate.Require(fixture);

        var query = Uri.EscapeDataString(ExampleTree.CommonSearchSurname);
        using var withProfile = await fixture.RawClient.GetAsync(
            $"/api/search/?query={query}&profile=self&pagesize=1", CancellationToken.None);
        using var withoutProfile = await fixture.RawClient.GetAsync(
            $"/api/search/?query={query}&pagesize=1", CancellationToken.None);

        withProfile.EnsureSuccessStatusCode();
        withoutProfile.EnsureSuccessStatusCode();

        var withProfileBody = await withProfile.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var withoutProfileBody = await withoutProfile.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.False(
            withProfileBody[0].TryGetProperty("profile", out _),
            "Expected profile=self on /api/search/ to be silently ignored (no 'profile' section added), " +
            "not honored as shaping.");

        // Compares total hit counts rather than the top hit's identity — the search index's
        // tie-breaking between equally-ranked hits isn't guaranteed stable across requests, so
        // asserting on a specific hit would risk an unrelated flake.
        Assert.Equal(ReadTotalCountHeader(withoutProfile), ReadTotalCountHeader(withProfile));
    }

    [Fact]
    public async Task FindByGrampsIdAsync_ReturnsExactlyOneMatchingRow()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsGetRequest { Profile = "none", Keys = ["handle", "gramps_id"] };
        var found = await fixture.ApiClient.FindByGrampsIdAsync<JsonElement>(
            "/api/people/", ExampleTree.AnchorPersonGrampsId, request, CancellationToken.None);

        Assert.True(found.ValueKind != JsonValueKind.Undefined, "Expected a match for the anchor person's Gramps ID.");
        Assert.Equal(ExampleTree.AnchorPersonHandle, found.GetProperty("handle").GetString());
        Assert.Equal(ExampleTree.AnchorPersonGrampsId, found.GetProperty("gramps_id").GetString());

        // Confirms "exactly one" isn't just an artifact of FindByGrampsIdAsync always taking
        // items[0] — the gramps_id filter itself yields a single-row collection upstream.
        using var rawResponse = await fixture.RawClient.GetAsync(
            $"/api/people/?gramps_id={ExampleTree.AnchorPersonGrampsId}", CancellationToken.None);
        rawResponse.EnsureSuccessStatusCode();
        var rawMatches = await rawResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(1, rawMatches.GetArrayLength());
    }

    [Fact]
    public async Task GrampsGetTool_ResolvesAnchorPerson_ByGrampsId()
    {
        LiveStackGate.Require(fixture);

        var tool = new GrampsGetTool(fixture.ApiClient);
        var person = await tool.GetAsync("person", ExampleTree.AnchorPersonGrampsId, extend: null, profile: "none", CancellationToken.None);

        Assert.Equal(ExampleTree.AnchorPersonHandle, person.GetProperty("handle").GetString());
    }

    [Fact]
    public async Task GrampsGetTool_ResolvesAnchorPerson_ByHandle()
    {
        LiveStackGate.Require(fixture);

        var tool = new GrampsGetTool(fixture.ApiClient);
        var person = await tool.GetAsync("person", ExampleTree.AnchorPersonHandle, extend: null, profile: "none", CancellationToken.None);

        Assert.Equal(ExampleTree.AnchorPersonGrampsId, person.GetProperty("gramps_id").GetString());
    }

    [Fact]
    public async Task SearchAsync_DeserializesHitsWithNonNullRankAndScore()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsSearchRequest { Query = ExampleTree.CommonSearchSurname, PageSize = 5 };
        var result = await fixture.ApiClient.SearchAsync(request, CancellationToken.None);

        Assert.Equal(ExampleTree.CommonSearchSurnameHitCount, result.Total);
        Assert.NotEmpty(result.Items);
        foreach (var hit in result.Items)
        {
            Assert.False(double.IsNaN(hit.Rank));
            Assert.False(double.IsNaN(hit.Score));
            Assert.False(string.IsNullOrEmpty(hit.Handle));
            Assert.False(string.IsNullOrEmpty(hit.ObjectType));
        }
    }

    [Fact]
    public async Task ListAsync_PageSizeAboveMaxPageSize_IsClampedByOurClient()
    {
        LiveStackGate.Require(fixture);

        // docker-compose.test.yml sets no GRAMPS_MAX_PAGESIZE, so GrampsOptions.MaxPageSize's
        // default of 100 (see GrampsServiceCollectionExtensions/GrampsOptions) is what the live
        // stack's DI-built ApiClient enforces.
        const int expectedMaxPageSize = 100;
        const int requestedPageSize = 500;
        var request = new GrampsListRequest { PageSize = requestedPageSize };
        var result = await fixture.ApiClient.ListAsync<JsonElement>(
            "/api/people/", request, CancellationToken.None);

        Assert.True(
            result.Items.Count <= expectedMaxPageSize,
            $"Requested pagesize={requestedPageSize}, but GrampsOptions.MaxPageSize defaults to " +
            $"{expectedMaxPageSize} and neither docker-compose.test.yml nor this fixture overrides " +
            $"GRAMPS_MAX_PAGESIZE — expected our own client to clamp the row count to at most " +
            $"{expectedMaxPageSize}, got {result.Items.Count}.");
        Assert.Equal(expectedMaxPageSize, result.PageSize);

        using var rawResponse = await fixture.RawClient.GetAsync(
            $"/api/people/?page=1&pagesize={requestedPageSize}", CancellationToken.None);
        rawResponse.EnsureSuccessStatusCode();
        var rawPeople = await rawResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(
            requestedPageSize,
            rawPeople.GetArrayLength());
    }

    private static int? ReadTotalCountHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-total-count", out var values) && int.TryParse(values.FirstOrDefault(), out var count)
            ? count
            : null;
}
