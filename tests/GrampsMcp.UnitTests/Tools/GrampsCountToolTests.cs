using System.Net;
using System.Text;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsCountToolTests
{
    private static readonly GrampsOptions Options = new()
    {
        ApiUrl = "https://tree.example.com",
        Username = "owner",
        Password = "owner",
        MaxPageSize = 50,
    };

    [Fact]
    public async Task CountAsync_ResolvesTypeForcesPagesizeOneAndReturnsTotal()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"handle":"h1"}]""", Encoding.UTF8, "application/json"),
            Headers = { { "x-total-count", "42" } },
        });
        var tool = new GrampsCountTool(CreateClient(handler));

        var total = await tool.CountAsync("person", "surname = SUTHERLAND");

        var request = handler.Requests.Single();
        Assert.Equal("/api/people/", request.RequestUri!.AbsolutePath);
        Assert.Contains("pagesize=1", request.RequestUri.Query);
        Assert.Contains("gql=surname", request.RequestUri.Query);
        Assert.Equal(42, total);
    }

    [Fact]
    public async Task CountAsync_OnUnknownType_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsCountTool(CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(() => tool.CountAsync("people", "surname = X"));
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(Options.ApiUrl) };
        return new GrampsApiClient(httpClient, Microsoft.Extensions.Options.Options.Create(Options));
    }
}
