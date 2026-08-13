using System.Net;
using System.Text;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsMergeToolTests
{
    [Fact]
    public async Task MergeAsync_PostsToPhoenixTitanicPath()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"handle":"phoenix"}""", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsMergeTool(CreateClient(handler));

        await tool.MergeAsync("person", "phoenix", "titanic", options: null, CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/people/phoenix/merge/titanic", request.RequestUri!.AbsolutePath);
        Assert.Equal("{}", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task MergeAsync_WithoutOptions_SendsEmptyObjectBody()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var tool = new GrampsMergeTool(CreateClient(handler));

        await tool.MergeAsync("family", "phoenix", "titanic", options: null, CancellationToken.None);

        Assert.Equal("{}", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task MergeAsync_WithInvalidHandle_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsMergeTool(CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(
            () => tool.MergeAsync("person", "../escape", "titanic", options: null, CancellationToken.None));
    }

    [Fact]
    public async Task MergeAsync_WithSameHandle_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsMergeTool(CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(
            () => tool.MergeAsync("person", "same", "same", options: null, CancellationToken.None));
    }

    [Fact]
    public async Task MergeAsync_WithTagType_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsMergeTool(CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(
            () => tool.MergeAsync("tag", "phoenix", "titanic", options: null, CancellationToken.None));
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler) =>
        GrampsTestClientFactory.CreateClient(handler);
}
