using System.Globalization;
using GrampsMcp.Server.Configuration;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Owns the pagination and response-shaping guardrails: <c>page</c> is coerced to at least 1
/// (Gramps Web treats <c>page=0</c> as "unpaginated"), <c>pagesize</c> is capped at
/// <see cref="GrampsOptions.MaxPageSize"/>, and a default <c>profile</c> is applied so responses
/// stay context-cheap unless a caller opts out.
/// </summary>
internal static class GrampsQueryBuilder
{
    public static IReadOnlyList<KeyValuePair<string, string?>> Build(GrampsListRequest request, GrampsOptions options)
    {
        var query = BuildPagination(request, options);

        AppendShapingParameters(query, request.Profile, extend: null, request.Keys, request.SkipKeys, options);

        if (!string.IsNullOrEmpty(request.Sort))
        {
            query.Add(new KeyValuePair<string, string?>("sort", request.Sort));
        }

        return query;
    }

    /// <summary>
    /// Builds query parameters for <c>/api/search/</c> — page/pagesize only, since the search
    /// endpoint only supports <c>type[]</c> and <c>semantic</c> (added separately by the caller)
    /// and rejects the profile/extend/keys/skipkeys shaping params the other list endpoints accept.
    /// </summary>
    public static List<KeyValuePair<string, string?>> BuildForSearch(GrampsListRequest request, GrampsOptions options) =>
        BuildPagination(request, options);

    private static List<KeyValuePair<string, string?>> BuildPagination(GrampsListRequest request, GrampsOptions options)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize ?? options.MaxPageSize, 1, options.MaxPageSize);

        return
        [
            new("page", page.ToString(CultureInfo.InvariantCulture)),
            new("pagesize", pageSize.ToString(CultureInfo.InvariantCulture)),
        ];
    }

    /// <summary>
    /// Builds query parameters for a single-object fetch — no page/pagesize, since Gramps Web's
    /// single-object endpoint rejects unrecognised fields.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string?>> BuildForGet(GrampsGetRequest request, GrampsOptions options)
    {
        var query = new List<KeyValuePair<string, string?>>();
        AppendShapingParameters(query, request.Profile, request.Extend, request.Keys, request.SkipKeys, options);
        return query;
    }

    private static void AppendShapingParameters(
        List<KeyValuePair<string, string?>> query,
        string? profile,
        IReadOnlyList<string>? extend,
        IReadOnlyList<string>? keys,
        IReadOnlyList<string>? skipKeys,
        GrampsOptions options)
    {
        var resolvedProfile = profile ?? options.DefaultProfile;
        if (!string.IsNullOrEmpty(resolvedProfile) && !string.Equals(resolvedProfile, "none", StringComparison.OrdinalIgnoreCase))
        {
            query.Add(new KeyValuePair<string, string?>("profile", resolvedProfile));
        }

        AddCommaJoined(query, "extend", extend);
        AddCommaJoined(query, "keys", keys);
        AddCommaJoined(query, "skipkeys", skipKeys);
    }

    // Gramps Web parses these as a single delimited value (OpenAPI style=form, explode=false);
    // repeating the query key instead of comma-joining silently drops all but the last value.
    private static void AddCommaJoined(List<KeyValuePair<string, string?>> query, string name, IReadOnlyList<string>? values)
    {
        if (values is { Count: > 0 })
        {
            query.Add(new KeyValuePair<string, string?>(name, string.Join(',', values)));
        }
    }
}
