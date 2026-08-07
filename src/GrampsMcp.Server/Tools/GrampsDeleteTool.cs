using System.ComponentModel;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

/// <summary>WRITE tool — only registered when writes are enabled and the service account has
/// <c>DeleteObject</c> permission. See <see cref="GrampsWriteTools"/>.</summary>
[McpServerToolType]
[GrampsWriteTool("DeleteObject")]
public sealed class GrampsDeleteTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_delete")]
    [Description(
        "Delete a Gramps object permanently. WRITE tool, only available when writes are enabled. This " +
        "is destructive and leaves dangling references in any object that pointed at the deleted " +
        "handle — check with gramps_get/gramps_search first. Recoverable via gramps_history + " +
        "gramps_undo immediately afterward, but not once further writes have happened.")]
    public async Task<object> DeleteAsync(
        [Description("Object type: person, family, event, place, citation, source, repository, media, note, tag.")]
        string type,
        [Description("The object's internal handle (not its Gramps ID).")]
        string handle,
        CancellationToken cancellationToken = default)
    {
        return await GrampsToolErrors.TranslateAsync(async () =>
        {
            var collectionPath = GrampsObjectTypes.ResolveCollectionPath(type);

            GrampsIdentifiers.EnsureValidHandle(handle, nameof(handle));

            await apiClient.DeleteAsync($"{collectionPath}{handle}", cancellationToken);
            return (object)new { deleted = true, type, handle };
        });
    }
}
