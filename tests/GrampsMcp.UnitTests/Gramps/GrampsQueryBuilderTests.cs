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
    public void Build_PassesThroughKeysAndSkipKeys()
    {
        var query = GrampsQueryBuilder.Build(
            new GrampsListRequest { Keys = ["gramps_id", "handle"], SkipKeys = ["media_list"] },
            Options);

        Assert.Equal(["gramps_id", "handle"], query.Where(kv => kv.Key == "keys").Select(kv => kv.Value));
        Assert.Equal(["media_list"], query.Where(kv => kv.Key == "skipkeys").Select(kv => kv.Value));
    }

    private static string? Single(IReadOnlyList<KeyValuePair<string, string?>> query, string key) =>
        query.Single(kv => kv.Key == key).Value;
}
