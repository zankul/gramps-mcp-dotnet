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
        "This is the default way to find people, places, events, sources, etc. by matching text — " +
        "reach for gramps_query instead when you need a structural predicate this can't express " +
        "(e.g. \"people with 3+ children\", date-range or relationship filters). " +
        "Omitting pagesize returns only ~20 hits by default (not the server max) because each hit " +
        "carries its full raw object with no server-side trimming available on this endpoint; pass " +
        "pagesize explicitly for more, up to the server cap.")]
    public async Task<GrampsListResult<GrampsSearchHit>> SearchAsync(
        [Description("The search string. Matches text only — not a query language.")] string query,
        [Description("Restrict results to these object types: person, family, event, place, citation, source, repository, media, note, tag. Omit to search everything.")]
        IReadOnlyList<string>? types = null,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Results per page. Defaults to ~20 (not the server cap) to keep each call cheap; raise explicitly up to the server-capped max for more.")] int? pagesize = null,
        [Description(
            "Use semantic (vector) search instead of full-text search. Requires the Gramps Web instance to have " +
            "VECTOR_EMBEDDING_MODEL configured — most instances don't, in which case this errors regardless of " +
            "query. Leave false unless you know embeddings are configured.")]
        bool semantic = false,
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
