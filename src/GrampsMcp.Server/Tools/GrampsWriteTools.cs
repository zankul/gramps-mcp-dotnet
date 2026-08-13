using System.Reflection;

namespace GrampsMcp.Server.Tools;

/// <summary>
/// The write tools discovered via <see cref="GrampsWriteToolAttribute"/>, each paired with the
/// Gramps JWT permission(s) it needs before it may be registered. Deliberately separate from
/// <c>[McpServerToolType]</c> assembly scanning: <see cref="Tools.GrampsToolTypeResolver.ResolveAsync(IServiceCollection, IConfiguration)"/>
/// excludes every type carrying <see cref="GrampsWriteToolAttribute"/> from the read-tool scan,
/// then adds back whichever of <see cref="All"/> pass <see cref="SelectRegistrable"/> once the
/// service account's real permission ceiling (read from the JWT, not merely
/// <c>GRAMPS_ALLOW_WRITES</c>) has been checked — the two lists are combined into one
/// <c>IEnumerable&lt;Type&gt;</c> that <c>Program.cs</c> passes to <c>WithTools</c> in a single
/// call, not registered one by one. An unregistered tool cannot be planned around or invoked by
/// the model, whereas a registered tool that merely errors at call time still shows up in
/// <c>list_tools</c>.
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
