using System.Net;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsUndoToolTests
{
    [Fact]
    public async Task UndoAsync_PostsToTransactionUndoEndpoint()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var tool = new GrampsUndoTool(CreateClient(handler));

        await tool.UndoAsync(7, force: true, message: "mistake", CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/transactions/history/7/undo", request.RequestUri!.AbsolutePath);
        Assert.Contains("force=true", request.RequestUri.Query);
        Assert.Contains("message=mistake", request.RequestUri.Query);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler) =>
        GrampsTestClientFactory.CreateClient(handler);
}
