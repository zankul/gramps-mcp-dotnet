using System.Text.Json.Nodes;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Applies an RFC 7396 JSON Merge Patch: fields in the patch overwrite matching fields in the
/// target, <see langword="null"/> removes a field, nested objects merge recursively, and
/// non-object values (including arrays) replace the target wholesale. Used by
/// <c>gramps_update</c> because Gramps Web's <c>PUT</c> replaces the entire object — a patch has
/// to be merged onto the full current object client-side before writing it back.
/// </summary>
internal static class GrampsJsonMergePatch
{
    public static JsonNode? Apply(JsonNode? target, JsonNode? patch)
    {
        if (patch is not JsonObject patchObject)
        {
            return patch?.DeepClone();
        }

        var result = target is JsonObject targetObject ? targetObject.DeepClone().AsObject() : new JsonObject();

        foreach (var (key, value) in patchObject)
        {
            if (value is null)
            {
                result.Remove(key);
            }
            else
            {
                result[key] = Apply(result[key], value);
            }
        }

        return result;
    }
}
