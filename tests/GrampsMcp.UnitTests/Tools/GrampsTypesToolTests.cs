using System.Net;
using System.Text;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsTypesToolTests
{
    private static readonly GrampsOptions Options = new()
    {
        ApiUrl = "https://tree.example.com",
        Username = "owner",
        Password = "owner",
        MaxPageSize = 50,
    };

    [Fact]
    public async Task GetAsync_FetchesTypesEndpoint()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"default":{},"custom":{}}""", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsTypesTool(CreateClient(handler));

        var result = await tool.GetAsync();

        Assert.Equal("/api/types/", handler.Requests.Single().RequestUri!.AbsolutePath);
        Assert.True(result.TryGetProperty("default", out _));
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(Options.ApiUrl) };
        return new GrampsApiClient(httpClient, Microsoft.Extensions.Options.Options.Create(Options));
    }
}
