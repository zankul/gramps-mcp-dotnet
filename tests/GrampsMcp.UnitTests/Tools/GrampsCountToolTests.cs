using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsCountToolTests
{
    [Fact]
    public async Task CountAsync_ResolvesTypeForcesPagesizeOneAndReturnsTotal()
    {
        var handler = new TestHttpMessageHandler(_ => TestResponses.JsonList("""[{"handle":"h1"}]""", 42));
        var tool = new GrampsCountTool(GrampsTestClientFactory.CreateClient(handler));

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
        var tool = new GrampsCountTool(GrampsTestClientFactory.CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(() => tool.CountAsync("people", "surname = X"));
    }
}
