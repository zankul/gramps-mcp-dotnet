using GrampsMcp.Server.Tools;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsWriteToolsTests
{
    [Fact]
    public void All_ContainsExactlyTheSixKnownWriteTools()
    {
        // GrampsWriteTools.All is discovered by reflection over [GrampsWriteTool], so this can't
        // catch a tool that lost the attribute (it would just silently drop out of All and stay
        // unregistered — fails closed). It guards the other direction: a typo'd or accidentally
        // duplicated attribute application changing the discovered set unexpectedly.
        var toolTypes = GrampsWriteTools.All.Select(tool => tool.ToolType).ToHashSet();

        Assert.Equal(
            [
                typeof(GrampsCreateTool), typeof(GrampsUpdateTool), typeof(GrampsDeleteTool),
                typeof(GrampsMergeTool), typeof(GrampsHistoryTool), typeof(GrampsUndoTool),
            ],
            toolTypes);
    }

    [Fact]
    public void SelectRegistrable_WithNoPermissions_ReturnsNone()
    {
        var registrable = GrampsWriteTools.SelectRegistrable(new HashSet<string>());

        Assert.Empty(registrable);
    }

    [Fact]
    public void SelectRegistrable_WithFullPermissions_ReturnsAllWriteTools()
    {
        var permissions = new HashSet<string> { "AddObject", "EditObject", "DeleteObject" };

        var registrable = GrampsWriteTools.SelectRegistrable(permissions);

        Assert.Equal(GrampsWriteTools.All.Count, registrable.Count);
    }

    [Fact]
    public void SelectRegistrable_WithOnlyAddObject_RegistersOnlyCreate()
    {
        var permissions = new HashSet<string> { "AddObject" };

        var registrable = GrampsWriteTools.SelectRegistrable(permissions);

        Assert.Equal([typeof(GrampsCreateTool)], registrable);
    }

    [Fact]
    public void SelectRegistrable_MergeRequiresBothEditAndDeletePermission()
    {
        var editOnly = GrampsWriteTools.SelectRegistrable(new HashSet<string> { "EditObject" });
        var editAndDelete = GrampsWriteTools.SelectRegistrable(new HashSet<string> { "EditObject", "DeleteObject" });

        Assert.DoesNotContain(typeof(GrampsMergeTool), editOnly);
        Assert.Contains(typeof(GrampsMergeTool), editAndDelete);
    }
}
