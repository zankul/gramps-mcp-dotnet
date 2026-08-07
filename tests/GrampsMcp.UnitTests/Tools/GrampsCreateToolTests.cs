using System.Net;
using System.Text;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsCreateToolTests
{
    [Fact]
    public async Task CreateAsync_PostsToCollectionEndpoint()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""[{"type":"add","handle":"h1"}]""", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsCreateTool(CreateClient(handler));
        var payload = JsonDocument.Parse("""{"gramps_id":"I9999"}""").RootElement;

        await tool.CreateAsync("person", payload, CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/people/", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CreateAsync_OnUnknownType_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsCreateTool(CreateClient(handler));
        var payload = JsonDocument.Parse("{}").RootElement;

        await Assert.ThrowsAsync<McpException>(
            () => tool.CreateAsync("human", payload, CancellationToken.None));
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler) =>
        GrampsTestClientFactory.CreateClient(handler);
}
