using System.Text.Json;
using GrampsMcp.LiveTests.LiveStack;
using ModelContextProtocol.Protocol;

namespace GrampsMcp.LiveTests.Protocol;

/// <summary>
/// End-to-end <c>tools/call</c> invocations against the running <c>gramps-mcp</c> container over
/// the real MCP protocol — as opposed to <c>Contract/ApiClientContractTests</c>'s
/// <c>GrampsGetTool</c>/<c>GrampsSearchTool</c> calls, which construct the tool class directly and
/// never touch JSON-RPC framing, argument marshalling, or tool-name dispatch. A bug in the SDK's
/// argument binding (e.g. a parameter name mismatch between a tool's C# signature and its MCP-visible
/// schema) would sail through the in-process contract tests and only show up here.
/// </summary>
[Trait("Tier", "Live")]
[Collection(LiveGrampsCollection.Name)]
public class ToolCallTests(LiveGrampsFixture fixture)
{
    [Fact]
    public async Task CallTool_GrampsSearch_ReturnsAnchorSurnameHitsOverTheWire()
    {
        LiveStackGate.Require(fixture);

        await using var connection = await McpTestClient.ConnectAsync(fixture, CancellationToken.None);
        var client = connection.Client;

        var arguments = new Dictionary<string, object?>
        {
            ["query"] = ExampleTree.CommonSearchSurname,
            ["pagesize"] = 5,
        };
        var result = await client.CallToolAsync("gramps_search", arguments, cancellationToken: CancellationToken.None);

        // CallToolResult.IsError is bool?, and a successful call leaves it null rather than an
        // explicit false (confirmed against the real server: a successful gramps_get/gramps_search
        // response serializes "isError":null) — Assert.False(bool?) treats null as a failure just
        // like false does, so this must check for an explicit true, not merely "not false".
        Assert.True(result.IsError != true, "gramps_search reported an error over the MCP protocol: " + DescribeContent(result));
        var payload = ReadJsonPayload(result);

        Assert.Equal(ExampleTree.CommonSearchSurnameHitCount, payload.GetProperty("total").GetInt32());
        Assert.True(payload.GetProperty("items").GetArrayLength() > 0, "Expected at least one search hit for the anchor surname.");
    }

    [Fact]
    public async Task CallTool_GrampsGet_ResolvesAnchorPersonByGrampsIdOverTheWire()
    {
        LiveStackGate.Require(fixture);

        await using var connection = await McpTestClient.ConnectAsync(fixture, CancellationToken.None);
        var client = connection.Client;

        var arguments = new Dictionary<string, object?>
        {
            ["type"] = "person",
            ["idOrHandle"] = ExampleTree.AnchorPersonGrampsId,
        };
        var result = await client.CallToolAsync("gramps_get", arguments, cancellationToken: CancellationToken.None);

        Assert.True(result.IsError != true, "gramps_get reported an error over the MCP protocol: " + DescribeContent(result));
        var payload = ReadJsonPayload(result);

        Assert.Equal(ExampleTree.AnchorPersonHandle, payload.GetProperty("handle").GetString());
        Assert.Equal(ExampleTree.AnchorPersonGrampsId, payload.GetProperty("gramps_id").GetString());
    }

    private static JsonElement ReadJsonPayload(CallToolResult result)
    {
        var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return JsonSerializer.Deserialize<JsonElement>(block.Text);
    }

    private static string DescribeContent(CallToolResult result) =>
        string.Join(" | ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
