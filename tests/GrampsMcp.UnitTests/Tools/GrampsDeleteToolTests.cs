using System.Net;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsDeleteToolTests
{
    [Fact]
    public async Task DeleteAsync_SendsDeleteToObjectEndpoint()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var tool = new GrampsDeleteTool(CreateClient(handler));

        await tool.DeleteAsync("person", "h1", CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/api/people/h1", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DeleteAsync_WithInvalidHandle_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsDeleteTool(CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(
            () => tool.DeleteAsync("person", "../escape", CancellationToken.None));
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler) =>
        GrampsTestClientFactory.CreateClient(handler);
}
