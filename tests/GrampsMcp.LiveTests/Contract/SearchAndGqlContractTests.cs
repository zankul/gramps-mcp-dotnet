using System.Net;
using System.Text.Json;
using GrampsMcp.LiveTests.LiveStack;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;

namespace GrampsMcp.LiveTests.Contract;

/// <summary>
/// Verifies search's <c>type</c> filter and <c>semantic</c> flag, plus a small number of GQL
/// cases, against a real server. Search's total hit counts below were read back from the live
/// stack for the query <see cref="ExampleTree.CommonSearchSurname"/> ("Ball") before being written
/// as assertions: 21 person hits, 11 family hits, 36 event hits (person+family+event = 68, matching
/// <see cref="ExampleTree.CommonSearchSurnameHitCount"/>, which is an untyped/unfiltered count).
/// </summary>
[Trait("Tier", "Live")]
[Collection(LiveGrampsCollection.Name)]
public class SearchAndGqlContractTests(LiveGrampsFixture fixture)
{
    private static readonly string[] PersonAndFamilyTypes = ["person", "family"];

    // Read back from the live stack for ExampleTree.CommonSearchSurname (see class-level doc
    // comment) — reused across two tests below so a future dataset re-capture updates one spot.
    private const int CommonSearchSurnamePersonHitCount = 21;


    [Fact]
    public async Task SearchAsync_TypeRestrictedToPerson_ReturnsOnlyPersonHits()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsSearchRequest { Query = ExampleTree.CommonSearchSurname, PageSize = 100, Types = ["person"] };
        var result = await fixture.ApiClient.SearchAsync(request, CancellationToken.None);

        Assert.Equal(CommonSearchSurnamePersonHitCount, result.Total);
        Assert.All(result.Items, hit => Assert.Equal("person", hit.ObjectType));
    }

    [Fact]
    public async Task SearchAsync_TypeRestrictedToPersonAndFamily_ExcludesEventHits()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsSearchRequest
        {
            Query = ExampleTree.CommonSearchSurname,
            PageSize = 100,
            Types = ["person", "family"],
        };
        var result = await fixture.ApiClient.SearchAsync(request, CancellationToken.None);

        Assert.Equal(32, result.Total);
        Assert.All(result.Items, hit => Assert.Contains(hit.ObjectType, PersonAndFamilyTypes));
        Assert.DoesNotContain(result.Items, hit => hit.ObjectType == "event");
    }

    [Fact]
    public async Task SearchAsync_TypeOmitted_IncludesEventHitsAlongsidePersonAndFamily()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsSearchRequest { Query = ExampleTree.CommonSearchSurname, PageSize = 100 };
        var result = await fixture.ApiClient.SearchAsync(request, CancellationToken.None);

        Assert.Equal(ExampleTree.CommonSearchSurnameHitCount, result.Total);
        Assert.Contains(result.Items, hit => hit.ObjectType == "event");
    }

    /// <summary>
    /// <c>docker-compose.test.yml</c> (and <c>docker-compose.dev.yml</c>) set no
    /// <c>VECTOR_EMBEDDING_MODEL</c>, so on this stack <c>semantic=true</c> is not silently
    /// accepted or silently identical to non-semantic search — it 503s with a specific upstream
    /// message. This contradicts an assumption that semantic search might just work or degrade
    /// gracefully; worth flagging (see PR description), since a model calling
    /// <c>gramps_search(semantic: true)</c> against this deployment shape gets a real error, not a
    /// silently-different result set. The assertion here is that our client surfaces it as a
    /// translatable <see cref="GrampsApiException"/> rather than an unhandled exception.
    /// </summary>
    [Fact]
    public async Task SearchAsync_Semantic_FailsWithVectorEmbeddingModelNotSetOnThisStack()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsSearchRequest { Query = ExampleTree.CommonSearchSurname, PageSize = 5, Semantic = true };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => fixture.ApiClient.SearchAsync(request, CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("VECTOR_EMBEDDING_MODEL", exception.Message);
    }

    [Fact]
    public async Task GrampsSearchTool_TypeRestrictedToPerson_ReturnsOnlyPersonHits()
    {
        LiveStackGate.Require(fixture);

        var tool = new GrampsSearchTool(fixture.ApiClient);
        var result = await tool.SearchAsync(
            ExampleTree.CommonSearchSurname, types: ["person"], page: 1, pagesize: 100, semantic: false, CancellationToken.None);

        Assert.Equal(CommonSearchSurnamePersonHitCount, result.Total);
        Assert.All(result.Items, hit => Assert.Equal("person", hit.ObjectType));
    }

    /// <summary>
    /// A different field and comparison operator than <c>GuardrailPremiseTests</c>'s
    /// <c>gender = 1</c> (an equality check on an int field): <c>&gt;=</c> on a computed list-length
    /// field. Verified against the real server first — the anchor person (3 families) is one of
    /// exactly two people in the tree with <c>family_list.length &gt;= 3</c>, the other being
    /// I0368/HI0KQCG9TGT5AAIPU.
    /// </summary>
    [Fact]
    public async Task Gql_FamilyListLengthGreaterThanOrEqualThree_ReturnsExactlyTwoPeopleIncludingAnchor()
    {
        LiveStackGate.Require(fixture);

        var tool = new GrampsQueryTool(fixture.ApiClient);
        var result = await tool.QueryAsync("person", "family_list.length >= 3", page: 1, pagesize: 10, keys: null, cancellationToken: CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Contains(result.Items, item => item.GetProperty("handle").GetString() == ExampleTree.AnchorPersonHandle);
    }

    [Fact]
    public async Task Gql_GrampsIdEqualsAnchorId_ReturnsExactlyOneMatchingHandle()
    {
        LiveStackGate.Require(fixture);

        var request = new GrampsGqlRequest { Gql = $"gramps_id = \"{ExampleTree.AnchorPersonGrampsId}\"", PageSize = 10 };
        var result = await fixture.ApiClient.QueryAsync<JsonElement>("/api/people/", request, CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal(ExampleTree.AnchorPersonHandle, result.Items[0].GetProperty("handle").GetString());
    }
}
