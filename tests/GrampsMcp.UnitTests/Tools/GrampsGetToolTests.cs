using System.Net;
using System.Text;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsGetToolTests
{
    [Fact]
    public async Task GetAsync_WithHandleShapedId_HitsSingleObjectEndpoint()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"handle":"102adba515546de1ad51712ea3fd"}""", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsGetTool(GrampsTestClientFactory.CreateClient(handler));

        await tool.GetAsync("person", "102adba515546de1ad51712ea3fd", cancellationToken: CancellationToken.None);

        Assert.Equal(
            "/api/people/102adba515546de1ad51712ea3fd",
            handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetAsync_WithGrampsId_FiltersTheCollectionEndpoint()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"handle":"h1","gramps_id":"I0155"}]""", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsGetTool(GrampsTestClientFactory.CreateClient(handler));

        var result = await tool.GetAsync("person", "I0155", cancellationToken: CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal("/api/people/", request.RequestUri!.AbsolutePath);
        Assert.Contains("gramps_id=I0155", request.RequestUri.Query);
        Assert.Equal("h1", result.GetProperty("handle").GetString());
    }

    [Fact]
    public async Task GetAsync_WithGrampsId_WhenNoMatch_ThrowsNotFound()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"code":404,"status":"Not Found"}"""),
        });
        var tool = new GrampsGetTool(GrampsTestClientFactory.CreateClient(handler));

        var exception = await Assert.ThrowsAsync<McpException>(
            () => tool.GetAsync("person", "I9999999", cancellationToken: CancellationToken.None));

        Assert.Contains("I9999999", exception.Message);
    }

    [Fact]
    public async Task GetAsync_OnUnknownType_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsGetTool(GrampsTestClientFactory.CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(
            () => tool.GetAsync("human", "I0155", cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_WithPathTraversalHandle_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsGetTool(GrampsTestClientFactory.CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(
            () => tool.GetAsync("person", "../../users/", cancellationToken: CancellationToken.None));
    }
}
