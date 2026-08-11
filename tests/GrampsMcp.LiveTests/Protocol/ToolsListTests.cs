using GrampsMcp.LiveTests.LiveStack;

namespace GrampsMcp.LiveTests.Protocol;

/// <summary>
/// Drives the real MCP protocol (<c>tools/list</c>) against the running <c>gramps-mcp</c> container,
/// as opposed to every other live test class, which talks to <c>GrampsApiClient</c>/the tool classes
/// in-process. That distinction matters here specifically: <c>GrampsToolTypeResolver</c>'s own doc
/// comment (see <c>src/GrampsMcp.Server/Tools/GrampsToolTypeResolver.cs</c>) warns that if C#
/// overload resolution ever picks the generic <c>WithTools&lt;TToolType&gt;(TToolType)</c> overload
/// instead of the intended <c>WithTools(IEnumerable&lt;Type&gt;, ...)</c> one, the SDK silently scans
/// the <c>List&lt;Type&gt;</c> instance itself for <c>[McpServerTool]</c> methods and registers zero
/// tools — a regression nothing in-process would ever see, since nothing in-process asks the running
/// host what it actually registered.
/// </summary>
[Trait("Tier", "Live")]
[Collection(LiveGrampsCollection.Name)]
public class ToolsListTests(LiveGrampsFixture fixture)
{
    // docker-compose.test.yml sets GRAMPS_ALLOW_WRITES: "true" and the seeded owner account (role 4,
    // created by grampsweb_bootstrap) holds every Gramps permission, so GrampsWriteTools.All's
    // required permissions (AddObject, EditObject, DeleteObject) are all satisfied and every write
    // tool registers alongside every read tool — 9 read + 6 write = 15. Names below are each tool's
    // [McpServerTool(Name = "...")] value, the source of truth in src/GrampsMcp.Server/Tools/*.cs;
    // confirmed by a direct tools/list probe against a running stack before this test was written.
    private static readonly string[] ExpectedToolNames =
    [
        // Read tools.
        "gramps_get",
        "gramps_search",
        "gramps_list",
        "gramps_query",
        "gramps_count",
        "gramps_types",
        "gramps_relation",
        "gramps_timeline",
        "gramps_tree_summary",

        // Write tools — each carries [GrampsWriteTool(...)] and only registers when the service
        // account's JWT permissions cover it (see GrampsToolTypeResolver / GrampsWriteTools).
        "gramps_create",
        "gramps_update",
        "gramps_delete",
        "gramps_merge",
        "gramps_history",
        "gramps_undo",
    ];

    [Fact]
    public async Task ToolsList_WithWritesEnabled_RegistersExpectedSet()
    {
        LiveStackGate.Require(fixture);

        await using var connection = await McpTestClient.ConnectAsync(fixture, CancellationToken.None);
        var tools = await connection.Client.ListToolsAsync(cancellationToken: CancellationToken.None);

        var actualNames = tools.Select(tool => tool.Name).ToHashSet();
        var expectedNames = ExpectedToolNames.ToHashSet();

        // Exact-set comparison, not Assert.Contains/subset: a partial match (or a superset from a
        // stray extra registration) is exactly the class of drift this test exists to catch, and
        // Contains-style assertions would let a silently-empty registration slip through if the
        // expected set were ever (wrongly) checked as "at least these" instead of "exactly these".
        Assert.True(
            expectedNames.SetEquals(actualNames),
            "Registered tool set differs from the expected set. " +
            $"Missing: [{string.Join(", ", expectedNames.Except(actualNames))}]. " +
            $"Unexpected: [{string.Join(", ", actualNames.Except(expectedNames))}]. " +
            "If this is an empty actual set, see GrampsToolTypeResolver's doc comment on the " +
            "WithTools<TToolType> overload-resolution hazard this test guards against.");
    }

    [Fact]
    public async Task EveryRegisteredTool_HasNonEmptyDescription()
    {
        LiveStackGate.Require(fixture);

        await using var connection = await McpTestClient.ConnectAsync(fixture, CancellationToken.None);
        var tools = await connection.Client.ListToolsAsync(cancellationToken: CancellationToken.None);

        Assert.NotEmpty(tools);
        Assert.All(tools, tool => Assert.False(
            string.IsNullOrWhiteSpace(tool.Description),
            $"Tool '{tool.Name}' has an empty or whitespace-only description. CLAUDE.md treats " +
            "[Description] text as prompt content for the calling model, not documentation — an " +
            "empty one is a real product bug, since it leaves the model with no signal for when to " +
            "reach for this tool."));
    }
}
