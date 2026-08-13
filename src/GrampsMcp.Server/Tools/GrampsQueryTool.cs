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
        "can be corrected and retried.")]
    public async Task<GrampsListResult<JsonElement>> QueryAsync(
        [Description("Object type: person, family, event, place, citation, source, repository, media, note, tag.")]
        string type,
        [Description("GQL filter expression, e.g. 'child_ref_list.length >= 3'. Quote values containing spaces.")]
        string gql,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Items per page. Omitting it returns the server's max page size; explicit values are still capped at that max.")] int? pagesize = null,
        [Description("Top-level keys to include in each item; all others are omitted.")]
        IReadOnlyList<string>? keys = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GrampsGqlRequest { Gql = gql, Page = page, PageSize = pagesize, Keys = keys };
        return await GrampsToolErrors.TranslateAsync(() =>
        {
            var path = GrampsObjectTypes.ResolveCollectionPath(type);
            return apiClient.QueryAsync<JsonElement>(path, request, cancellationToken);
        });
    }
}
