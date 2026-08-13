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

    [Fact]
    public void SelectRegistrable_UpdateRequiresBothEditAndViewPrivatePermission()
    {
        // See docs/PLAN.md fact #15: a service account lacking ViewPrivate gets a private record's
        // GET silently redacted to {} rather than refused, and gramps_update's read-modify-write
        // would merge-patch onto that redaction and PUT it back, permanently destroying data. This
        // pins the fail-closed fix for that.
        var editOnly = GrampsWriteTools.SelectRegistrable(new HashSet<string> { "EditObject" });
        var editAndViewPrivate = GrampsWriteTools.SelectRegistrable(new HashSet<string> { "EditObject", "ViewPrivate" });

        Assert.DoesNotContain(typeof(GrampsUpdateTool), editOnly);
        Assert.Contains(typeof(GrampsUpdateTool), editAndViewPrivate);
    }
}
