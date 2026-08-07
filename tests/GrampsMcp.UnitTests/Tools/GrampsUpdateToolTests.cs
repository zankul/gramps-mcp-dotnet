using System.Net;
using System.Text;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsUpdateToolTests
{
    [Fact]
    public async Task UpdateAsync_ReadsCurrentObjectThenPutsMergedResult()
    {
        var handler = new TestHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"handle":"h1","gender":1,"gramps_id":"I0001"}""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var tool = new GrampsUpdateTool(CreateClient(handler));
        var patch = JsonDocument.Parse("""{"gender":0}""").RootElement;

        var result = await tool.UpdateAsync("person", "h1", patch, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal("/api/people/h1", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal(0, result.GetProperty("gender").GetInt32());
        Assert.Equal("I0001", result.GetProperty("gramps_id").GetString());
    }

    [Fact]
    public async Task UpdateAsync_GetRequestOmitsProfile()
    {
        var handler = new TestHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"handle":"h1"}""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var tool = new GrampsUpdateTool(CreateClient(handler));
        var patch = JsonDocument.Parse("{}").RootElement;

        await tool.UpdateAsync("person", "h1", patch, CancellationToken.None);

        Assert.DoesNotContain("profile=", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task UpdateAsync_WithInvalidHandle_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsUpdateTool(CreateClient(handler));
        var patch = JsonDocument.Parse("{}").RootElement;

        await Assert.ThrowsAsync<McpException>(
            () => tool.UpdateAsync("person", "../escape", patch, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_WithNullPatch_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsUpdateTool(CreateClient(handler));
        var patch = JsonDocument.Parse("null").RootElement;

        await Assert.ThrowsAsync<McpException>(
            () => tool.UpdateAsync("person", "h1", patch, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_WithArrayPatch_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsUpdateTool(CreateClient(handler));
        var patch = JsonDocument.Parse("[1,2,3]").RootElement;

        await Assert.ThrowsAsync<McpException>(
            () => tool.UpdateAsync("person", "h1", patch, CancellationToken.None));
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler) =>
        GrampsTestClientFactory.CreateClient(handler);
}
