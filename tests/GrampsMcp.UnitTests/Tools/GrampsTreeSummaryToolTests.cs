using System.Net;
using System.Text;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsTreeSummaryToolTests
{
    private static readonly GrampsOptions Options = new()
    {
        ApiUrl = "https://tree.example.com",
        Username = "owner",
        Password = "owner",
        MaxPageSize = 50,
    };

    [Theory]
    [InlineData(false, "surnames=false")]
    [InlineData(true, "surnames=true")]
    public async Task GetAsync_PassesIncludeSurnamesThrough(bool includeSurnames, string expectedQueryFragment)
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"object_counts":{"people":1}}""", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsTreeSummaryTool(CreateClient(handler));

        await tool.GetAsync(includeSurnames);

        var request = handler.Requests.Single();
        Assert.Equal("/api/metadata/", request.RequestUri!.AbsolutePath);
        Assert.Contains(expectedQueryFragment, request.RequestUri.Query);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(Options.ApiUrl) };
        return new GrampsApiClient(httpClient, Microsoft.Extensions.Options.Options.Create(Options));
    }
}
