using System.Text.RegularExpressions;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Distinguishes a Gramps ID (e.g. "I0155", stable and human-assigned) from an internal handle
/// (e.g. "102adba515546de1ad51712ea3fd" or a UUID, opaque and server-generated). Every Gramps ID
/// observed across object types is a letter prefix followed by digits and nothing else, which is
/// short enough and distinctive enough not to collide with a real handle.
/// </summary>
internal static partial class GrampsIdentifiers
{
    public static bool LooksLikeGrampsId(string idOrHandle) => GrampsIdPattern().IsMatch(idOrHandle);

    /// <summary>
    /// True for strings safe to interpolate directly into a URL path segment: observed handles
    /// are hex strings or UUIDs, so letters, digits, and hyphens cover every legitimate case.
    /// Rejects anything else — in particular <c>/</c> and <c>.</c> — so a caller cannot use
    /// path-traversal or extra segments to escape the collection path it's concatenated onto.
    /// </summary>
    public static bool IsValidHandle(string idOrHandle) => HandlePattern().IsMatch(idOrHandle);

    [GeneratedRegex(@"^[A-Za-z]+\d+$")]
    private static partial Regex GrampsIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9-]+$")]
    private static partial Regex HandlePattern();
}
