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
    public static IReadOnlyList<KeyValuePair<string, string?>> Build(GrampsListRequest request, GrampsOptions options, string? objectType = null)
    {
        var query = BuildPagination(request.Page, request.PageSize, options);

        AppendShapingParameters(query, request.Profile, extend: null, request.Keys, request.SkipKeys, options, objectType);

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
    /// An omitted <c>pagesize</c> defaults to <see cref="GrampsOptions.SearchDefaultPageSize"/>,
    /// not <see cref="GrampsOptions.MaxPageSize"/> — gramps_search is the tool callers are told to
    /// reach for by default, and every hit carries its full untrimmed object (no server-side
    /// trimming is possible here), so defaulting to the max page would flood the model's context
    /// on the common case. Callers can still explicitly request up to <c>MaxPageSize</c>.
    /// </summary>
    public static List<KeyValuePair<string, string?>> BuildForSearch(GrampsListRequest request, GrampsOptions options) =>
        BuildPagination(request.Page, request.PageSize, options, options.SearchDefaultPageSize);

    /// <summary>
    /// Builds query parameters for a GQL filter fetch (<c>/api/{type}/?gql=...</c>). Measured live,
    /// GQL endpoints actually *honor* <c>profile</c>/<c>extend</c>/<c>skipkeys</c> exactly like the
    /// plain list endpoints do (adds a resolved <c>profile</c> section, an <c>extended</c> section,
    /// or drops the named keys respectively — see <c>docs/PLAN.md</c> fact #14), so this applies the
    /// same <see cref="AppendShapingParameters"/> shaping <see cref="Build"/> does — a defaulted
    /// <c>profile</c> removes a whole class of follow-up <c>gramps_get</c> calls after a GQL search.
    /// Pagesize defaults to <see cref="GrampsOptions.GqlDefaultPageSize"/>, not
    /// <see cref="GrampsOptions.MaxPageSize"/>: <c>gramps_query</c> is framed as a narrow escape
    /// hatch (see its tool description), so a conservative default bounds the case where a GQL
    /// filter matches broadly, the same way <see cref="BuildForSearch"/> bounds search's common
    /// case. The <c>gql</c> parameter itself is added by the caller.
    /// </summary>
    public static List<KeyValuePair<string, string?>> BuildForGql(GrampsGqlRequest request, GrampsOptions options, string? objectType = null)
    {
        var query = BuildPagination(request.Page, request.PageSize, options, options.GqlDefaultPageSize);
        AppendShapingParameters(query, request.Profile, request.Extend, request.Keys, request.SkipKeys, options, objectType);
        return query;
    }

    private static List<KeyValuePair<string, string?>> BuildPagination(
        int page, int? pageSize, GrampsOptions options, int? defaultPageSize = null)
    {
        var resolvedPage = Math.Max(1, page);
        var resolvedPageSize = Math.Clamp(pageSize ?? defaultPageSize ?? options.MaxPageSize, 1, options.MaxPageSize);

        return
        [
            new("page", resolvedPage.ToString(CultureInfo.InvariantCulture)),
            new("pagesize", resolvedPageSize.ToString(CultureInfo.InvariantCulture)),
        ];
    }

    /// <summary>
    /// Builds query parameters for a genuine single-object fetch, e.g. <c>/api/people/{handle}</c> —
    /// no page/pagesize, since that endpoint rejects unrecognised fields and would 422 on them.
    /// This governs only the single-object-by-handle call site
    /// (<see cref="GrampsApiClient.GetAsync{T}(string,GrampsGetRequest,CancellationToken)"/>); it
    /// must NOT be reused for a collection endpoint filtered down to one row — see
    /// <see cref="BuildForIdLookup"/> for that case, which needs the opposite pagination guardrail.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string?>> BuildForGet(GrampsGetRequest request, GrampsOptions options, string? objectType = null)
    {
        var query = new List<KeyValuePair<string, string?>>();
        AppendShapingParameters(query, request.Profile, request.Extend, request.Keys, request.SkipKeys, options, objectType);
        return query;
    }

    /// <summary>
    /// Builds query parameters for <see cref="GrampsApiClient.FindByGrampsIdAsync{T}"/>, which
    /// resolves a Gramps ID via a <em>collection</em> endpoint's <c>gramps_id</c> filter (e.g.
    /// <c>/api/people/?gramps_id=I0155</c>), not a single-object endpoint — so, unlike
    /// <see cref="BuildForGet"/>, this call site is subject to the same forced-pagination guardrail
    /// as any other collection call: an omitted <c>page</c> is what makes Gramps Web return the
    /// whole collection. If the <c>gramps_id</c> filter were ever silently ignored upstream (the
    /// same failure mode already measured for <c>/api/search/</c>'s <c>profile</c> param), an
    /// unpaginated request here would materialize the entire collection. Forces
    /// <c>page=1&amp;pagesize=1</c> — a Gramps ID lookup only ever wants a single row.
    /// </summary>
    public static List<KeyValuePair<string, string?>> BuildForIdLookup(GrampsGetRequest request, GrampsOptions options, string? objectType = null)
    {
        var query = BuildPagination(1, 1, options);

        AppendShapingParameters(query, request.Profile, request.Extend, request.Keys, request.SkipKeys, options, objectType);
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
        GrampsOptions options,
        string? objectType)
    {
        var resolvedProfile = profile ?? ResolveDefaultProfile(objectType, options);
        if (!string.IsNullOrEmpty(resolvedProfile) && !string.Equals(resolvedProfile, "none", StringComparison.OrdinalIgnoreCase))
        {
            query.Add(new KeyValuePair<string, string?>("profile", resolvedProfile));
        }

        AddCommaJoined(query, "extend", extend);
        AddCommaJoined(query, "keys", keys);
        AddCommaJoined(query, "skipkeys", skipKeys);
    }

    // Measured live: `event` objects reject the person-oriented default keys with 422 "profile
    // contains invalid keys"; every other object type accepted them. Only the *default* is
    // filtered — an explicit caller-supplied profile goes through untouched so an upstream error
    // still reaches the model verbatim.
    private static readonly Dictionary<string, HashSet<string>> UnsupportedDefaultProfileKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["event"] = new(["families", "events"], StringComparer.OrdinalIgnoreCase),
        };

    private static string? ResolveDefaultProfile(string? objectType, GrampsOptions options)
    {
        if (objectType is null || !UnsupportedDefaultProfileKeys.TryGetValue(objectType, out var unsupported))
        {
            return options.DefaultProfile;
        }

        var supported = options.DefaultProfile
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(key => !unsupported.Contains(key));

        var joined = string.Join(',', supported);
        return joined.Length == 0 ? null : joined;
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
