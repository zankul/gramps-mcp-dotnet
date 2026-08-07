using System.ComponentModel;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

[McpServerToolType]
public sealed class GrampsCountTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_count")]
    [Description(
        "Count objects of one type matching a GQL expression, without returning the matching rows — " +
        "cheaper than gramps_query when only the total is needed. Same cost and constraints as " +
        "gramps_query: slow (10-15s per call), one object type per call, no sorting or aggregation. " +
        "Prefer gramps_search first. Values containing spaces must be quoted, e.g. surname = \"(6th " +
        "Earl of) SUTHERLAND\". A malformed query returns the upstream parse error verbatim.")]
    public async Task<int> CountAsync(
        [Description("Object type: person, family, event, place, citation, source, repository, media, note, tag.")]
        string type,
        [Description("GQL filter expression, e.g. 'child_ref_list.length >= 3'. Quote values containing spaces.")]
        string gql,
        CancellationToken cancellationToken = default)
    {
        return await GrampsToolErrors.TranslateAsync(() =>
        {
            var path = GrampsObjectTypes.ResolveCollectionPath(type);
            return apiClient.CountAsync(path, gql, cancellationToken);
        });
    }
}
