using System.Net;
using System.Text;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsListToolTests
{
    [Fact]
    public async Task ListAsync_ResolvesTypeAndPassesThroughSortAndKeys()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"handle":"h1"}]""", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsListTool(GrampsTestClientFactory.CreateClient(handler));

        await tool.ListAsync("family", sort: "-change", page: 2, pagesize: 10, keys: ["gramps_id", "handle"]);

        var request = handler.Requests.Single();
        Assert.Equal("/api/families/", request.RequestUri!.AbsolutePath);
        Assert.Contains("sort=-change", request.RequestUri.Query);
        Assert.Contains("page=2", request.RequestUri.Query);
        Assert.Contains("pagesize=10", request.RequestUri.Query);
        Assert.Contains("keys=gramps_id,handle", request.RequestUri.Query);
    }

    [Fact]
    public async Task ListAsync_OnUnknownType_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsListTool(GrampsTestClientFactory.CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(() => tool.ListAsync("families"));
    }
}
