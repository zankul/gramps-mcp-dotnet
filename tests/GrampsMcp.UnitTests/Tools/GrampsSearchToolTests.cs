using System.Net;
using System.Text;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsSearchToolTests
{
    private static readonly GrampsOptions Options = new()
    {
        ApiUrl = "https://tree.example.com",
        Username = "owner",
        Password = "owner",
        MaxPageSize = 50,
    };

    [Fact]
    public async Task SearchAsync_PassesQueryTypesAndSemanticThrough()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("x-total-count", "0");
            return response;
        });
        var tool = new GrampsSearchTool(CreateClient(handler));

        await tool.SearchAsync("Sutherland", types: ["person"], semantic: true);

        var request = handler.Requests.Single();
        Assert.Equal("/api/search/", request.RequestUri!.AbsolutePath);
        Assert.Contains("query=Sutherland", request.RequestUri.Query);
        Assert.Contains("type=person", request.RequestUri.Query);
        Assert.Contains("semantic=true", request.RequestUri.Query);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(Options.ApiUrl) };
        return new GrampsApiClient(httpClient, Microsoft.Extensions.Options.Options.Create(Options));
    }
}
