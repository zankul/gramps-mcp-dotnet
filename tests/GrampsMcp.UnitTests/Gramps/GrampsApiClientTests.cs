using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsApiClientTests
{
    private static readonly GrampsOptions Options = new()
    {
        ApiUrl = "https://tree.example.com",
        Username = "owner",
        Password = "owner",
        MaxPageSize = 50,
    };

    [Fact]
    public async Task ListAsync_ReadsTotalCountFromHeader()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"handle":"h1","gramps_id":"I0001"}]""", Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("x-total-count", "4663");
            return response;
        });
        var client = CreateClient(handler);

        var result = await client.ListAsync<TestPerson>("/api/people/", new GrampsListRequest(), CancellationToken.None);

        Assert.Equal(4663, result.Total);
        Assert.Single(result.Items);
        Assert.Equal("h1", result.Items[0].Handle);
        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
    }

    [Fact]
    public async Task ListAsync_WhenTotalCountHeaderMissing_FallsBackToItemCount()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """[{"handle":"h1","gramps_id":"I0001"},{"handle":"h2","gramps_id":"I0002"}]""",
                Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        var result = await client.ListAsync<TestPerson>("/api/people/", new GrampsListRequest(), CancellationToken.None);

        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task GetAsync_ReturnsDeserializedObject()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"handle":"h1","gramps_id":"I0001"}""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        var person = await client.GetAsync<TestPerson>("/api/people/I0001", CancellationToken.None);

        Assert.Equal("I0001", person.GrampsId);
    }

    [Fact]
    public async Task GetAsync_OnErrorStatus_ThrowsGrampsApiException()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error": "not found"}"""),
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => client.GetAsync<TestPerson>("/api/people/I9999", CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("not found", exception.Message);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(Options.ApiUrl) };
        return new GrampsApiClient(httpClient, Microsoft.Extensions.Options.Options.Create(Options));
    }

    private sealed record TestPerson(
        [property: JsonPropertyName("handle")] string Handle,
        [property: JsonPropertyName("gramps_id")] string GrampsId);
}
