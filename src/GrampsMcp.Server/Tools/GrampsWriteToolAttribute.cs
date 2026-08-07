namespace GrampsMcp.Server.Tools;

/// <summary>
/// Marks a tool type as a WRITE tool and declares the Gramps JWT permission(s) it needs before
/// it may be registered. <c>Program.cs</c>'s assembly scan excludes any type carrying this
/// attribute from the read-tool set it registers unconditionally, and <see cref="GrampsWriteTools.All"/>
/// discovers write tools by scanning for it — a single mechanism, so a write tool's exclusion from
/// reads and its permission requirement can never drift apart the way two independently maintained
/// lists could.
/// </summary>
/// <param name="requiredPermissions">The Gramps JWT permission(s) needed to register this tool.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GrampsWriteToolAttribute(params string[] requiredPermissions) : Attribute
{
    public IReadOnlyList<string> RequiredPermissions { get; } = requiredPermissions;
}
