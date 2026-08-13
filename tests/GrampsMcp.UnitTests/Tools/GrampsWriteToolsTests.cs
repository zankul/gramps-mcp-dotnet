using GrampsMcp.Server.Tools;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsWriteToolsTests
{
    [Fact]
    public void SelectRegistrable_WithNoPermissions_ReturnsNone()
    {
        var registrable = GrampsWriteTools.SelectRegistrable(new HashSet<string>());

        Assert.Empty(registrable);
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
