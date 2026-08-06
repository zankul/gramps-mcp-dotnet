using System.ComponentModel;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

[McpServerToolType]
public sealed class GrampsSearchTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_search")]
    [Description(
        "Indexed full-text search across the whole tree; answers in ~2s regardless of tree size. " +
        "This is the default way to find people, places, events, sources, etc. — reach for gramps_query " +
        "only when a filter this can't express is required.")]
    public async Task<GrampsListResult<GrampsSearchHit>> SearchAsync(
        [Description("The search string.")] string query,
        [Description("Restrict results to these object types: person, family, event, place, citation, source, repository, media, note, tag. Omit to search everything.")]
        IReadOnlyList<string>? types = null,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Results per page (server-capped).")] int? pagesize = null,
        [Description("Use semantic (vector) search instead of full-text search.")] bool semantic = false,
        CancellationToken cancellationToken = default)
    {
        var request = new GrampsSearchRequest
        {
            Query = query,
            Types = types,
            Page = page,
            PageSize = pagesize,
            Semantic = semantic,
        };

        return await GrampsToolErrors.TranslateAsync(() => apiClient.SearchAsync(request, cancellationToken));
    }
}
