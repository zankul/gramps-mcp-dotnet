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
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize ?? options.MaxPageSize, 1, options.MaxPageSize);
        var profile = request.Profile ?? options.DefaultProfile;

        var query = new List<KeyValuePair<string, string?>>
        {
            new("page", page.ToString(CultureInfo.InvariantCulture)),
            new("pagesize", pageSize.ToString(CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrEmpty(profile) && !string.Equals(profile, "none", StringComparison.OrdinalIgnoreCase))
        {
            query.Add(new KeyValuePair<string, string?>("profile", profile));
        }

        foreach (var key in request.Keys ?? [])
        {
            query.Add(new KeyValuePair<string, string?>("keys", key));
        }

        foreach (var key in request.SkipKeys ?? [])
        {
            query.Add(new KeyValuePair<string, string?>("skipkeys", key));
        }

        return query;
    }
}
