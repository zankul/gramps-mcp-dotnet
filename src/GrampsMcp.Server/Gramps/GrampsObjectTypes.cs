namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Maps the singular object type names used by tool callers and <c>api/search/</c> (e.g.
/// "person") to their REST collection path (e.g. "api/people/").
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

    private static readonly Dictionary<string, string> TypesBySegment = PathSegments
        .ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves a type name to its REST collection path, e.g. "person" -> "api/people/".
    /// Deliberately relative (no leading slash): <see cref="GrampsApiClient"/> composes this
    /// against <c>HttpClient.BaseAddress</c>, and a leading slash would resolve path-absolute,
    /// discarding any sub-path already present in that base address instead of appending to it.
    /// </summary>
    /// <exception cref="ArgumentException">The type name is not recognised.</exception>
    public static string ResolveCollectionPath(string type)
    {
        if (!PathSegments.TryGetValue(type, out var segment))
        {
            throw new ArgumentException(
                $"Unknown Gramps object type '{type}'. Valid types: {string.Join(", ", PathSegments.Keys)}.",
                nameof(type));
        }

        return $"api/{segment}/";
    }

    /// <summary>
    /// Recovers the singular type name from a request path, e.g. "api/events/" or
    /// "api/events/abc123" -> "event". Returns <see langword="null"/> for paths that aren't an
    /// object collection (e.g. "api/search/"), so callers can treat the type as unknown.
    /// </summary>
    public static string? FromPath(string path)
    {
        var segments = path.Split('?', 2)[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 && segments[0] == "api" && TypesBySegment.TryGetValue(segments[1], out var type)
            ? type
            : null;
    }
}
