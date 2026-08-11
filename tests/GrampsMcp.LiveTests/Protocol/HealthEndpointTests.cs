using System.Net;
using GrampsMcp.LiveTests.LiveStack;

namespace GrampsMcp.LiveTests.Protocol;

/// <summary>
/// Hits the <c>gramps-mcp</c> container's <c>/health</c> endpoint directly over plain HTTP — not
/// through the MCP protocol at all, since <c>Program.cs</c> maps it as an ordinary minimal-API GET
/// alongside (not through) <c>MapMcp()</c>. Only reachable when the live stack is actually up, so
/// this belongs in the live tier rather than the unit tests, which never run a real Kestrel host on
/// a real port.
/// </summary>
[Trait("Tier", "Live")]
[Collection(LiveGrampsCollection.Name)]
public class HealthEndpointTests(LiveGrampsFixture fixture)
{
    [Fact]
    public async Task Health_Returns200()
    {
        LiveStackGate.Require(fixture);

        using var httpClient = new HttpClient { BaseAddress = new Uri(fixture.Options.McpUrl) };
        using var response = await httpClient.GetAsync("/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
