using System.Reflection;

namespace GrampsMcp.Server.Tools;

/// <summary>
/// The write tools discovered via <see cref="GrampsWriteToolAttribute"/>, each paired with the
/// Gramps JWT permission(s) it needs before it may be registered. Deliberately separate from
/// <c>[McpServerToolType]</c> assembly scanning: <c>Program.cs</c> registers write tool types
/// explicitly, one by one, only once the service account's real permission ceiling (read from the
/// JWT, not merely <c>GRAMPS_ALLOW_WRITES</c>) has been checked — an unregistered tool cannot be
/// planned around or invoked by the model, whereas a registered tool that merely errors at call
/// time still shows up in <c>list_tools</c>.
/// </summary>
internal static class GrampsWriteTools
{
    public static readonly IReadOnlyList<(Type ToolType, string[] RequiredPermissions)> All =
        [.. typeof(GrampsWriteTools).Assembly.GetTypes()
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<GrampsWriteToolAttribute>()))
            .Where(t => t.Attribute is not null)
            .Select(t => (t.Type, RequiredPermissions: t.Attribute!.RequiredPermissions.ToArray()))];

    /// <summary>Tool types whose required permissions are all present in <paramref name="permissions"/>.</summary>
    public static IReadOnlyList<Type> SelectRegistrable(IReadOnlySet<string> permissions) =>
        All.Where(tool => tool.RequiredPermissions.All(permissions.Contains))
            .Select(tool => tool.ToolType)
            .ToList();
}
