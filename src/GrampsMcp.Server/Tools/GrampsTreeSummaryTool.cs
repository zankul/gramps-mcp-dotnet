using System.ComponentModel;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using Microsoft.AspNetCore.WebUtilities;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

[McpServerToolType]
public sealed class GrampsTreeSummaryTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_tree_summary")]
    [Description("Summarize the tree: object counts per type, software/schema versions, and (optionally) the full surname list.")]
    public async Task<JsonElement> GetAsync(
        [Description("If true, include the full list of surnames found in the tree.")] bool includeSurnames = false,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["surnames"] = includeSurnames ? "true" : "false" };
        var path = QueryHelpers.AddQueryString("/api/metadata/", query);
        return await GrampsToolErrors.TranslateAsync(() => apiClient.GetAsync<JsonElement>(path, cancellationToken));
    }
}
