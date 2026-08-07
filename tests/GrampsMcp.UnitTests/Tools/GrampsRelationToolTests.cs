using System.Net;
using System.Text;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsRelationToolTests
{
    private static readonly GrampsOptions Options = new()
    {
        ApiUrl = "https://tree.example.com",
        Username = "owner",
        Password = "owner",
        MaxPageSize = 50,
    };

    [Fact]
    public async Task RelationAsync_WithHandles_HitsRelationsEndpoint()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"relationship_string":"husband","distance_common_origin":-1,"distance_common_other":-1}""",
                Encoding.UTF8,
                "application/json"),
        });
        var tool = new GrampsRelationTool(CreateClient(handler));

        var result = await tool.RelationAsync("handle-a", "handle-b", cancellationToken: CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal("/api/relations/handle-a/handle-b", request.RequestUri!.AbsolutePath);
        Assert.Equal("husband", result.GetProperty("relationship_string").GetString());
    }

    [Fact]
    public async Task RelationAsync_WithDepthAndLocale_PassesThemAsQueryParameters()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsRelationTool(CreateClient(handler));

        await tool.RelationAsync("handle-a", "handle-b", depth: 5, locale: "de", cancellationToken: CancellationToken.None);

        var query = handler.Requests.Single().RequestUri!.Query;
        Assert.Contains("depth=5", query);
        Assert.Contains("locale=de", query);
    }

    [Fact]
    public async Task RelationAsync_WithGrampsIds_ResolvesHandlesFirst()
    {
        var handler = new TestHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/people/")
            {
                var handle = request.RequestUri.Query.Contains("gramps_id=I0001", StringComparison.Ordinal) ? "handle-a" : "handle-b";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""[{"handle":"{{handle}}"}]""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });
        var tool = new GrampsRelationTool(CreateClient(handler));

        await tool.RelationAsync("I0001", "I0002", cancellationToken: CancellationToken.None);

        var relationsRequest = handler.Requests.Last();
        Assert.Equal("/api/relations/handle-a/handle-b", relationsRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RelationAsync_WithGrampsIdNotFound_ThrowsBeforeCallingRelationsEndpoint()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsRelationTool(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<McpException>(
            () => tool.RelationAsync("I9999999", "I0002", cancellationToken: CancellationToken.None));

        Assert.Contains("I9999999", exception.Message);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.StartsWith("/api/relations/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RelationAsync_WithInvalidHandle_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsRelationTool(CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(
            () => tool.RelationAsync("../../etc", "handle-b", cancellationToken: CancellationToken.None));
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(Options.ApiUrl) };
        return new GrampsApiClient(httpClient, Microsoft.Extensions.Options.Options.Create(Options));
    }
}
