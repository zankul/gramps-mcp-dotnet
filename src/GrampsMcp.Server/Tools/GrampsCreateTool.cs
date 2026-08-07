using System.ComponentModel;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

/// <summary>WRITE tool — only registered when writes are enabled and the service account has
/// <c>AddObject</c> permission. See <see cref="GrampsWriteTools"/>.</summary>
[McpServerToolType]
[GrampsWriteTool("AddObject")]
public sealed class GrampsCreateTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_create")]
    [Description(
        "Create a new Gramps object. WRITE tool, only available when writes are enabled. `payload` is " +
        "the full object body matching the Gramps schema for `type` (use gramps_get on an existing " +
        "object of the same type to see the shape); Gramps assigns the handle. Prefer gramps_search " +
        "first to confirm the object doesn't already exist. Returns whatever Gramps reports back for " +
        "the write — call gramps_search or gramps_list afterward to find the new object's handle.")]
    public async Task<JsonElement> CreateAsync(
        [Description("Object type: person, family, event, place, citation, source, repository, media, note, tag.")]
        string type,
        [Description("The full JSON object to create, matching the Gramps schema for this type.")]
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        return await GrampsToolErrors.TranslateAsync(async () =>
        {
            var collectionPath = GrampsObjectTypes.ResolveCollectionPath(type);
            return await apiClient.CreateAsync(collectionPath, payload, cancellationToken);
        });
    }
}
