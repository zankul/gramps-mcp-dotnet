using System.ComponentModel;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

[McpServerToolType]
public sealed class GrampsListTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_list")]
    [Description(
        "Paginated, sorted listing of Gramps objects by type. Prefer gramps_search for free-text " +
        "lookups; use this for a deliberate browse (e.g. \"list families sorted by change date\").")]
    public async Task<GrampsListResult<JsonElement>> ListAsync(
        [Description("Object type: person, family, event, place, citation, source, repository, media, note, tag.")]
        string type,
        [Description("Comma-delimited sort keys, prefix with '-' for descending, e.g. 'surname,-birth'.")]
        string? sort = null,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Items per page (server-capped).")] int? pagesize = null,
        [Description("Top-level keys to include in each item; all others are omitted.")]
        IReadOnlyList<string>? keys = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GrampsListRequest { Page = page, PageSize = pagesize, Sort = sort, Keys = keys };
        return await GrampsToolErrors.TranslateAsync(() =>
        {
            var path = GrampsObjectTypes.ResolveCollectionPath(type);
            return apiClient.ListAsync<JsonElement>(path, request, cancellationToken);
        });
    }
}
