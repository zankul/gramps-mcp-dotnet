using System.Net;
using System.Text;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsQueryToolTests
{
    [Fact]
    public async Task QueryAsync_ResolvesTypeAndPassesThroughGqlPageAndKeys()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"handle":"h1"}]""", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsQueryTool(GrampsTestClientFactory.CreateClient(handler));

        await tool.QueryAsync(
            "person", "child_ref_list.length >= 3", page: 2, pagesize: 10, keys: ["gramps_id", "handle"]);

        var request = handler.Requests.Single();
        Assert.Equal("/api/people/", request.RequestUri!.AbsolutePath);
        Assert.Contains("gql=child_ref_list.length", request.RequestUri.Query);
        Assert.Contains("page=2", request.RequestUri.Query);
        Assert.Contains("pagesize=10", request.RequestUri.Query);
        Assert.Contains("keys=gramps_id,handle", request.RequestUri.Query);
    }

    [Fact]
    public async Task QueryAsync_OnUnknownType_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsQueryTool(GrampsTestClientFactory.CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(() => tool.QueryAsync("people", "surname = X"));
    }

    [Fact]
    public async Task QueryAsync_OnMalformedGql_SurfacesUpstreamParseErrorVerbatim()
    {
        const string upstreamError = "Expected end of text, found '~' (at char 31), (line:1, col:32)";
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)422)
        {
            Content = new StringContent($$"""{"error":"{{upstreamError}}"}""", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsQueryTool(GrampsTestClientFactory.CreateClient(handler));

        var exception = await Assert.ThrowsAsync<McpException>(() => tool.QueryAsync("person", "surname ~ X"));

        Assert.Equal(upstreamError, exception.Message);
    }
}
