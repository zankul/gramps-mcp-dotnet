namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Maps the singular object type names used by tool callers and <c>/api/search/</c> (e.g.
/// "person") to their REST collection path (e.g. "/api/people/").
/// </summary>
public static class GrampsObjectTypes
{
    private static readonly IReadOnlyDictionary<string, string> PathSegments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["person"] = "people",
        ["family"] = "families",
        ["event"] = "events",
        ["place"] = "places",
        ["citation"] = "citations",
        ["source"] = "sources",
        ["repository"] = "repositories",
        ["media"] = "media",
        ["note"] = "notes",
        ["tag"] = "tags",
    };

    public static IReadOnlyCollection<string> Names => (IReadOnlyCollection<string>)PathSegments.Keys;

    /// <summary>Resolves a type name to its REST collection path, e.g. "person" -> "/api/people/".</summary>
    /// <exception cref="ArgumentException">The type name is not recognised.</exception>
    public static string ResolveCollectionPath(string type)
    {
        if (!PathSegments.TryGetValue(type, out var segment))
        {
            throw new ArgumentException(
                $"Unknown Gramps object type '{type}'. Valid types: {string.Join(", ", PathSegments.Keys)}.",
                nameof(type));
        }

        return $"/api/{segment}/";
    }
}
