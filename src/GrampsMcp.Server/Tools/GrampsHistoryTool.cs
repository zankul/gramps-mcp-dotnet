using System.ComponentModel;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

/// <summary>WRITE-adjacent tool — only registered when writes are enabled and the service account
/// has <c>EditObject</c> permission (paired with <c>gramps_undo</c>). See <see cref="GrampsWriteTools"/>.</summary>
[McpServerToolType]
[GrampsWriteTool("EditObject")]
public sealed class GrampsHistoryTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_history")]
    [Description(
        "List committed write transactions, most recent first by default. Only available when writes " +
        "are enabled. Use the `id` of a transaction with gramps_undo to reverse it.")]
    public async Task<GrampsListResult<JsonElement>> ListAsync(
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Items per page (server-capped).")] int? pagesize = null,
        [Description("Sort key: 'id' (oldest first) or '-id' (most recent first, the default).")]
        string sort = "-id",
        CancellationToken cancellationToken = default)
    {
        var request = new GrampsHistoryRequest { Page = page, PageSize = pagesize, Sort = sort };
        return await GrampsToolErrors.TranslateAsync(
            () => apiClient.ListHistoryAsync<JsonElement>(request, cancellationToken));
    }
}
