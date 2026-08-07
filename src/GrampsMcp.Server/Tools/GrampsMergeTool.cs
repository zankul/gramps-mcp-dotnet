using System.ComponentModel;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

/// <summary>WRITE tool — only registered when writes are enabled and the service account has both
/// <c>EditObject</c> and <c>DeleteObject</c> permission. See <see cref="GrampsWriteTools"/>.</summary>
[McpServerToolType]
[GrampsWriteTool("EditObject", "DeleteObject")]
public sealed class GrampsMergeTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_merge")]
    [Description(
        "Merge two Gramps objects of the same type into one. `keep_handle` (the \"phoenix\") survives; " +
        "`discard_handle` (the \"titanic\") absorbs into it and is deleted. WRITE tool, only available " +
        "when writes are enabled; not supported for type 'tag'. `options` is an optional type-specific " +
        "body: for 'person', {\"family_merger\": true} (default true) also merges duplicate families " +
        "that result from the merge; for 'family', {\"phoenix_father_handle\": ..., " +
        "\"phoenix_mother_handle\": ...} picks which parent survives a conflict (omit to keep the " +
        "phoenix family's existing parent); other types take no options. Recoverable via " +
        "gramps_history + gramps_undo immediately afterward.")]
    public async Task<JsonElement> MergeAsync(
        [Description("Object type: person, family, event, place, citation, source, repository, media, note.")]
        string type,
        [Description("Handle of the object that survives the merge.")]
        string keepHandle,
        [Description("Handle of the object that is absorbed and deleted.")]
        string discardHandle,
        [Description("Optional type-specific merge options; defaults to {} (Gramps' own defaults).")]
        JsonElement? options = null,
        CancellationToken cancellationToken = default)
    {
        return await GrampsToolErrors.TranslateAsync(async () =>
        {
            var collectionPath = GrampsObjectTypes.ResolveCollectionPath(type);

            if (string.Equals(type, "tag", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Merge is not supported for type 'tag'.", nameof(type));
            }

            GrampsIdentifiers.EnsureValidHandle(keepHandle, nameof(keepHandle));
            GrampsIdentifiers.EnsureValidHandle(discardHandle, nameof(discardHandle));

            if (string.Equals(keepHandle, discardHandle, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "'keep_handle' and 'discard_handle' must not be the same object.", nameof(discardHandle));
            }

            var body = options ?? JsonSerializer.SerializeToElement(new { });
            var path = $"{collectionPath}{keepHandle}/merge/{discardHandle}";
            return await apiClient.PostAsync(path, body, cancellationToken);
        });
    }
}
