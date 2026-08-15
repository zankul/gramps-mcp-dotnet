using System.ComponentModel;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

[McpServerToolType]
public sealed class GrampsQueryTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_query")]
    [Description(
        "Advanced escape hatch: filter a single object type with a GQL expression that gramps_search " +
        "or gramps_list can't express, e.g. \"child_ref_list.length >= 3\". Slow — 10-15s per call " +
        "regardless of result size, because GQL scans the whole database upstream. No sorting, " +
        "aggregation, joins, or cross-type queries; one object type per call. Prefer gramps_search " +
        "first. Values containing spaces must be quoted, e.g. surname = \"(6th Earl of) SUTHERLAND\". " +
        "A malformed query returns the upstream parse error verbatim (exact character/column) so it " +
        "can be corrected and retried. Results are profiled by default (self,families,events), " +
        "resolving handles into readable text; pass profile: \"none\" to opt out.")]
    public async Task<GrampsListResult<JsonElement>> QueryAsync(
        [Description("Object type: person, family, event, place, citation, source, repository, media, note, tag.")]
        string type,
        [Description("GQL filter expression, e.g. 'child_ref_list.length >= 3'. Quote values containing spaces.")]
        string gql,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Results per page. Defaults to ~20 (not the server cap) to keep results self-contained; raise explicitly up to the server-capped max for more.")] int? pagesize = null,
        [Description("Profile sections to include, e.g. 'self,families,events'. Pass 'none' to disable.")]
        string? profile = null,
        [Description("Related-object keys to expand inline, e.g. 'family_list,event_ref_list'. Use 'all' for everything.")]
        IReadOnlyList<string>? extend = null,
        [Description("Top-level keys to include in each item; all others are omitted.")]
        IReadOnlyList<string>? keys = null,
        [Description("Top-level keys to omit from each item.")]
        IReadOnlyList<string>? skipkeys = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GrampsGqlRequest
        {
            Gql = gql,
            Page = page,
            PageSize = pagesize,
            Profile = profile,
            Extend = extend,
            Keys = keys,
            SkipKeys = skipkeys,
        };
        return await GrampsToolErrors.TranslateAsync(() =>
        {
            var path = GrampsObjectTypes.ResolveCollectionPath(type);
            return apiClient.QueryAsync<JsonElement>(path, request, cancellationToken);
        });
    }
}
