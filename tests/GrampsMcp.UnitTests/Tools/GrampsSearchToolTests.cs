using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsSearchToolTests
{
    [Fact]
    public async Task SearchAsync_PassesQueryTypesAndSemanticThrough()
    {
        var handler = new TestHttpMessageHandler(_ => TestResponses.JsonList("[]", 0));
        var tool = new GrampsSearchTool(GrampsTestClientFactory.CreateClient(handler));

        await tool.SearchAsync("Sutherland", types: ["person"], semantic: true);

        var request = handler.Requests.Single();
        Assert.Equal("/api/search/", request.RequestUri!.AbsolutePath);
        Assert.Contains("query=Sutherland", request.RequestUri.Query);
        Assert.Contains("type=person", request.RequestUri.Query);
        Assert.Contains("semantic=true", request.RequestUri.Query);
    }
}
