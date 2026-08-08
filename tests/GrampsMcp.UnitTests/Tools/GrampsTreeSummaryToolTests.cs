using System.Net;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsTreeSummaryToolTests
{
    [Theory]
    [InlineData(false, "surnames=false")]
    [InlineData(true, "surnames=true")]
    public async Task GetAsync_PassesIncludeSurnamesThrough(bool includeSurnames, string expectedQueryFragment)
    {
        var handler = new TestHttpMessageHandler(_ => TestResponses.Json(HttpStatusCode.OK, """{"object_counts":{"people":1}}"""));
        var tool = new GrampsTreeSummaryTool(GrampsTestClientFactory.CreateClient(handler));

        await tool.GetAsync(includeSurnames);

        var request = handler.Requests.Single();
        Assert.Equal("/api/metadata/", request.RequestUri!.AbsolutePath);
        Assert.Contains(expectedQueryFragment, request.RequestUri.Query);
    }
}
