using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

/// <summary>WRITE tool — only registered when writes are enabled and the service account has
/// <c>EditObject</c> and <c>ViewPrivate</c> permission. See <see cref="GrampsWriteTools"/>.
/// <c>ViewPrivate</c> is required because this tool's read-modify-write (see
/// <see cref="UpdateAsync"/>) reads the current object with <c>Profile = "none"</c>, merges the
/// patch client-side, then PUTs the merged object back whole. Measured live (docs/PLAN.md fact
/// #15): a service account lacking <c>ViewPrivate</c> gets a private record's <c>GET</c> silently
/// redacted to <c>{}</c> (<c>200</c>, not <c>403</c>) rather than refused — without this
/// permission requirement, that redaction would merge-patch onto an empty object and PUT back a
/// record stripped of every field the account couldn't see, permanently destroying data.</summary>
[McpServerToolType]
[GrampsWriteTool("EditObject", "ViewPrivate")]
public sealed class GrampsUpdateTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_update")]
    [Description(
        "Update an existing Gramps object with a partial patch. WRITE tool, only available when writes " +
        "are enabled. Reads the full current object, applies `patch` as a JSON Merge Patch (RFC 7396: " +
        "fields in `patch` overwrite matching fields, null removes a field, nested objects merge " +
        "recursively, arrays are replaced wholesale — not appended to), then writes the full merged " +
        "object back, because Gramps Web's PUT replaces the entire object. Returns the object as it " +
        "will look after the merge.")]
    public async Task<JsonElement> UpdateAsync(
        [Description("Object type: person, family, event, place, citation, source, repository, media, note, tag.")]
        string type,
        [Description("The object's internal handle (not its Gramps ID).")]
        string handle,
        [Description("A JSON Merge Patch (RFC 7396) to apply to the current object.")]
        JsonElement patch,
        CancellationToken cancellationToken = default)
    {
        return await GrampsToolErrors.TranslateAsync(async () =>
        {
            var collectionPath = GrampsObjectTypes.ResolveCollectionPath(type);

            GrampsIdentifiers.EnsureValidHandle(handle, nameof(handle));

            // RFC 7396 defines a non-object patch (including JSON null) as replacing the whole
            // target with that value — technically compliant, but silently wiping the entire
            // object is exactly the kind of destructive accident this tool's guardrails exist to
            // prevent, so a partial-update patch must be a JSON object.
            if (patch.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    $"'patch' must be a JSON object; got {patch.ValueKind}. " +
                    "gramps_update only supports partial updates, not replacing the whole object.",
                    nameof(patch));
            }

            var path = $"{collectionPath}{handle}";
            var current = await apiClient.GetAsync<JsonElement>(
                path, new GrampsGetRequest { Profile = "none" }, cancellationToken);

            var merged = GrampsJsonMergePatch.Apply(
                JsonNode.Parse(current.GetRawText()), JsonNode.Parse(patch.GetRawText()));
            var mergedElement = JsonSerializer.SerializeToElement(merged);

            await apiClient.ReplaceAsync(path, mergedElement, cancellationToken);
            return mergedElement;
        });
    }
}
