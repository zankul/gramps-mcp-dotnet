using System.Globalization;
using GrampsMcp.Server.Configuration;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Owns the pagination and response-shaping guardrails: <c>page</c> is coerced to at least 1
/// (Gramps Web treats an <c>omitted</c> <c>page</c> key, not an explicit <c>page=0</c> — which is
/// 422-rejected — as "unpaginated"; and <c>pagesize</c> alone, without an explicit <c>page</c>,
/// only bounds <c>/api/search/</c>, not <c>/api/people/</c>/GQL), <c>pagesize</c> is capped at
/// <see cref="GrampsOptions.MaxPageSize"/>, and a default <c>profile</c> is applied so responses
/// stay context-cheap unless a caller opts out.
/// </summary>
internal static class GrampsQueryBuilder
{
    public static IReadOnlyList<KeyValuePair<string, string?>> Build(GrampsListRequest request, GrampsOptions options)
    {
        var query = BuildPagination(request.Page, request.PageSize, options);

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
    /// and doesn't honor the profile/extend/keys/skipkeys shaping params the other list endpoints
    /// accept (measured live for <c>profile</c>: silently ignored, <c>200 OK</c>, not rejected).
    /// </summary>
    public static List<KeyValuePair<string, string?>> BuildForSearch(GrampsListRequest request, GrampsOptions options) =>
        BuildPagination(request.Page, request.PageSize, options);

    /// <summary>
    /// Builds query parameters for a GQL filter fetch (<c>/api/{type}/?gql=...</c>) — page/pagesize
    /// and <c>keys</c> only, since GQL accepts no <c>profile</c>/<c>extend</c>/<c>skipkeys</c>/sort
    /// (see <c>docs/PLAN.md</c> fact #5). The <c>gql</c> parameter itself is added by the caller.
    /// </summary>
    public static List<KeyValuePair<string, string?>> BuildForGql(GrampsGqlRequest request, GrampsOptions options)
    {
        var query = BuildPagination(request.Page, request.PageSize, options);
        AddCommaJoined(query, "keys", request.Keys);
        return query;
    }

    private static List<KeyValuePair<string, string?>> BuildPagination(int page, int? pageSize, GrampsOptions options)
    {
        var resolvedPage = Math.Max(1, page);
        var resolvedPageSize = Math.Clamp(pageSize ?? options.MaxPageSize, 1, options.MaxPageSize);

        return
        [
            new("page", resolvedPage.ToString(CultureInfo.InvariantCulture)),
            new("pagesize", resolvedPageSize.ToString(CultureInfo.InvariantCulture)),
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

    /// <summary>
    /// Builds query parameters for a person/family timeline fetch. Same pagination guardrail as
    /// <see cref="Build"/> — an omitted <c>page</c> means "unpaginated" on this endpoint too.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string?>> BuildForTimeline(GrampsTimelineRequest request, GrampsOptions options)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize ?? options.MaxPageSize, 1, options.MaxPageSize);

        var query = new List<KeyValuePair<string, string?>>
        {
            new("page", page.ToString(CultureInfo.InvariantCulture)),
            new("pagesize", pageSize.ToString(CultureInfo.InvariantCulture)),
        };

        AddCommaJoined(query, "keys", request.Keys);
        AddCommaJoined(query, "skipkeys", request.SkipKeys);
        AddCommaJoined(query, "event_classes", request.EventClasses);
        AddCommaJoined(query, "events", request.Events);

        if (!string.IsNullOrEmpty(request.Dates))
        {
            query.Add(new KeyValuePair<string, string?>("dates", request.Dates));
        }

        if (request.Ancestors is { } ancestors)
        {
            query.Add(new KeyValuePair<string, string?>("ancestors", ancestors.ToString(CultureInfo.InvariantCulture)));
        }

        if (request.Offspring is { } offspring)
        {
            query.Add(new KeyValuePair<string, string?>("offspring", offspring.ToString(CultureInfo.InvariantCulture)));
        }

        return query;
    }

    /// <summary>
    /// Builds query parameters for <c>/api/transactions/history/</c> — page/pagesize/sort only,
    /// since the endpoint has no notion of profile/extend/keys and rejects unrecognised params.
    /// Same pagination guardrail as <see cref="Build"/>: an omitted <c>page</c> means "unpaginated".
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string?>> BuildForHistory(GrampsHistoryRequest request, GrampsOptions options)
    {
        var query = BuildPagination(request.Page, request.PageSize, options);

        if (!string.IsNullOrEmpty(request.Sort))
        {
            query.Add(new KeyValuePair<string, string?>("sort", request.Sort));
        }

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
