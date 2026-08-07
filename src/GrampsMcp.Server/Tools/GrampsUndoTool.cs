using System.ComponentModel;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

/// <summary>WRITE tool — only registered when writes are enabled and the service account has
/// <c>EditObject</c> permission. See <see cref="GrampsWriteTools"/>.</summary>
[McpServerToolType]
[GrampsWriteTool("EditObject")]
public sealed class GrampsUndoTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_undo")]
    [Description(
        "Reverse a previously committed transaction. WRITE tool, only available when writes are " +
        "enabled. Use gramps_history to find the transaction `id`. Fails if later transactions " +
        "conflict with the undo unless `force` is set.")]
    public async Task<JsonElement> UndoAsync(
        [Description("The transaction ID to undo, from gramps_history.")] int id,
        [Description("Force the undo even if later transactions conflict with it.")] bool force = false,
        [Description("Message to record for the undo transaction; defaults to Gramps' own 'Undo'.")]
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        return await GrampsToolErrors.TranslateAsync(
            () => apiClient.UndoTransactionAsync(id, force, message, cancellationToken));
    }
}
