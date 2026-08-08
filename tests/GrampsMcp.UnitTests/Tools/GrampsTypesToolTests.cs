using System.Net;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsTypesToolTests
{
    [Fact]
    public async Task GetAsync_FetchesTypesEndpoint()
    {
        var handler = new TestHttpMessageHandler(_ => TestResponses.Json(HttpStatusCode.OK, """{"default":{},"custom":{}}"""));
        var tool = new GrampsTypesTool(GrampsTestClientFactory.CreateClient(handler));

        var result = await tool.GetAsync();

        Assert.Equal("/api/types/", handler.Requests.Single().RequestUri!.AbsolutePath);
        Assert.True(result.TryGetProperty("default", out _));
    }
}
