using System.Globalization;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsQueryBuilderTests
{
    private static readonly GrampsOptions Options = new()
    {
        ApiUrl = "https://tree.example.com",
        Username = "svc",
        Password = "secret",
        MaxPageSize = 50,
        DefaultProfile = "self,families,events",
    };

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Build_CoercesPageToAtLeastOne(int requestedPage, int expectedPage)
    {
        var query = GrampsQueryBuilder.Build(new GrampsListRequest { Page = requestedPage }, Options);

        Assert.Equal(expectedPage.ToString(CultureInfo.InvariantCulture), Single(query, "page"));
    }

    [Fact]
    public void Build_WhenPageSizeExceedsMax_CapsAtMax()
    {
        var query = GrampsQueryBuilder.Build(new GrampsListRequest { PageSize = 500 }, Options);

        Assert.Equal("50", Single(query, "pagesize"));
    }

    [Fact]
    public void Build_WhenPageSizeUnspecified_DefaultsToMax()
    {
        var query = GrampsQueryBuilder.Build(new GrampsListRequest(), Options);

        Assert.Equal("50", Single(query, "pagesize"));
    }

    [Fact]
    public void Build_WhenPageSizeIsZeroOrNegative_CoercesToOne()
    {
        var query = GrampsQueryBuilder.Build(new GrampsListRequest { PageSize = 0 }, Options);

        Assert.Equal("1", Single(query, "pagesize"));
    }

    [Fact]
    public void Build_WhenProfileUnspecified_AppliesDefaultProfile()
    {
        var query = GrampsQueryBuilder.Build(new GrampsListRequest(), Options);

        Assert.Equal("self,families,events", Single(query, "profile"));
    }

    [Fact]
    public void Build_WhenProfileIsNone_OmitsProfileParameter()
    {
        var query = GrampsQueryBuilder.Build(new GrampsListRequest { Profile = "none" }, Options);

        Assert.DoesNotContain(query, kv => kv.Key == "profile");
    }

    [Fact]
    public void Build_CommaJoinsKeysAndSkipKeys()
    {
        // Gramps Web parses these as a single delimited value; repeating the query key instead
        // silently drops all but the last one.
        var query = GrampsQueryBuilder.Build(
            new GrampsListRequest { Keys = ["gramps_id", "handle"], SkipKeys = ["media_list"] },
            Options);

        Assert.Equal("gramps_id,handle", Single(query, "keys"));
        Assert.Equal("media_list", Single(query, "skipkeys"));
    }

    [Fact]
    public void Build_WhenSortSpecified_PassesThrough()
    {
        var query = GrampsQueryBuilder.Build(new GrampsListRequest { Sort = "surname,-birth" }, Options);

        Assert.Equal("surname,-birth", Single(query, "sort"));
    }

    [Fact]
    public void Build_WhenSortUnspecified_OmitsSortParameter()
    {
        var query = GrampsQueryBuilder.Build(new GrampsListRequest(), Options);

        Assert.DoesNotContain(query, kv => kv.Key == "sort");
    }

    [Fact]
    public void BuildForGet_OmitsPageAndPagesize()
    {
        var query = GrampsQueryBuilder.BuildForGet(new GrampsGetRequest(), Options);

        Assert.DoesNotContain(query, kv => kv.Key is "page" or "pagesize");
    }

    [Fact]
    public void BuildForGet_AppliesDefaultProfileAndCommaJoinsExtend()
    {
        var query = GrampsQueryBuilder.BuildForGet(
            new GrampsGetRequest { Extend = ["family_list", "event_ref_list"] },
            Options);

        Assert.Equal("self,families,events", Single(query, "profile"));
        Assert.Equal("family_list,event_ref_list", Single(query, "extend"));
    }

    [Fact]
    public void BuildForIdLookup_ForcesPageOneAndPagesizeOne()
    {
        // Unlike BuildForGet, this hits a collection endpoint filtered by gramps_id, not a genuine
        // single-object endpoint — so it must carry the same forced-pagination guardrail as any
        // other collection call, to guard against the gramps_id filter ever being silently ignored
        // upstream (as measured for /api/search/'s profile param).
        var query = GrampsQueryBuilder.BuildForIdLookup(new GrampsGetRequest(), Options);

        Assert.Equal("1", Single(query, "page"));
        Assert.Equal("1", Single(query, "pagesize"));
    }

    [Fact]
    public void BuildForIdLookup_AppliesDefaultProfileAndCommaJoinsExtend()
    {
        var query = GrampsQueryBuilder.BuildForIdLookup(
            new GrampsGetRequest { Extend = ["family_list", "event_ref_list"] },
            Options);

        Assert.Equal("self,families,events", Single(query, "profile"));
        Assert.Equal("family_list,event_ref_list", Single(query, "extend"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(4, 4)]
    public void BuildForTimeline_CoercesPageToAtLeastOne(int requestedPage, int expectedPage)
    {
        var query = GrampsQueryBuilder.BuildForTimeline(new GrampsTimelineRequest { Page = requestedPage }, Options);

        Assert.Equal(expectedPage.ToString(CultureInfo.InvariantCulture), Single(query, "page"));
    }

    [Fact]
    public void BuildForTimeline_WhenPageSizeExceedsMax_CapsAtMax()
    {
        var query = GrampsQueryBuilder.BuildForTimeline(new GrampsTimelineRequest { PageSize = 500 }, Options);

        Assert.Equal("50", Single(query, "pagesize"));
    }

    [Fact]
    public void BuildForTimeline_CommaJoinsEventClassesAndEvents()
    {
        var query = GrampsQueryBuilder.BuildForTimeline(
            new GrampsTimelineRequest { EventClasses = ["vital", "family"], Events = ["Birth"] },
            Options);

        Assert.Equal("vital,family", Single(query, "event_classes"));
        Assert.Equal("Birth", Single(query, "events"));
    }

    [Fact]
    public void BuildForTimeline_PassesThroughDatesAncestorsAndOffspring()
    {
        var query = GrampsQueryBuilder.BuildForTimeline(
            new GrampsTimelineRequest { Dates = "1900/1/1-", Ancestors = 2, Offspring = 3 },
            Options);

        Assert.Equal("1900/1/1-", Single(query, "dates"));
        Assert.Equal("2", Single(query, "ancestors"));
        Assert.Equal("3", Single(query, "offspring"));
    }

    [Fact]
    public void BuildForTimeline_WhenAncestorsAndOffspringUnspecified_OmitsThem()
    {
        var query = GrampsQueryBuilder.BuildForTimeline(new GrampsTimelineRequest(), Options);

        Assert.DoesNotContain(query, kv => kv.Key is "ancestors" or "offspring" or "dates");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-2, 1)]
    [InlineData(3, 3)]
    public void BuildForGql_CoercesPageToAtLeastOne(int requestedPage, int expectedPage)
    {
        var query = GrampsQueryBuilder.BuildForGql(new GrampsGqlRequest { Gql = "surname = X", Page = requestedPage }, Options);

        Assert.Equal(expectedPage.ToString(CultureInfo.InvariantCulture), Single(query, "page"));
    }

    [Fact]
    public void BuildForGql_WhenPageSizeExceedsMax_CapsAtMax()
    {
        var query = GrampsQueryBuilder.BuildForGql(new GrampsGqlRequest { Gql = "surname = X", PageSize = 500 }, Options);

        Assert.Equal("50", Single(query, "pagesize"));
    }

    [Fact]
    public void BuildForGql_CommaJoinsKeys()
    {
        var query = GrampsQueryBuilder.BuildForGql(
            new GrampsGqlRequest { Gql = "surname = X", Keys = ["gramps_id", "handle"] }, Options);

        Assert.Equal("gramps_id,handle", Single(query, "keys"));
    }

    [Fact]
    public void BuildForGql_CommaJoinsExtendAndSkipKeys()
    {
        var query = GrampsQueryBuilder.BuildForGql(
            new GrampsGqlRequest
            {
                Gql = "surname = X",
                Extend = ["family_list", "event_ref_list"],
                SkipKeys = ["citation_list", "note_list"],
            },
            Options);

        Assert.Equal("family_list,event_ref_list", Single(query, "extend"));
        Assert.Equal("citation_list,note_list", Single(query, "skipkeys"));
    }

    [Fact]
    public void BuildForGql_DoesNotApplySort()
    {
        var query = GrampsQueryBuilder.BuildForGql(new GrampsGqlRequest { Gql = "surname = X" }, Options);

        Assert.DoesNotContain(query, kv => kv.Key == "sort");
    }

    [Fact]
    public void BuildForGql_WhenProfileUnspecified_AppliesDefaultProfile()
    {
        var query = GrampsQueryBuilder.BuildForGql(new GrampsGqlRequest { Gql = "surname = X" }, Options);

        Assert.Equal("self,families,events", Single(query, "profile"));
    }

    [Fact]
    public void BuildForGql_WhenProfileIsNone_OmitsProfile()
    {
        var query = GrampsQueryBuilder.BuildForGql(new GrampsGqlRequest { Gql = "surname = X", Profile = "none" }, Options);

        Assert.DoesNotContain(query, kv => kv.Key == "profile");
    }

    [Fact]
    public void BuildForGql_WhenPageSizeUnspecified_DefaultsToGqlDefaultPageSize()
    {
        var query = GrampsQueryBuilder.BuildForGql(new GrampsGqlRequest { Gql = "surname = X" }, Options);

        Assert.Equal(Options.GqlDefaultPageSize.ToString(CultureInfo.InvariantCulture), Single(query, "pagesize"));
    }

    private static string? Single(IReadOnlyList<KeyValuePair<string, string?>> query, string key) =>
        query.Single(kv => kv.Key == key).Value;
}
