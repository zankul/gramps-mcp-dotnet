using System.Net;
using System.Text.Json;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Resolves a caller-supplied Gramps ID or handle into a raw handle, for endpoints (relations,
/// timelines) that take a handle directly in the URL path rather than accepting either form the
/// way <see cref="GrampsApiClient.GetAsync{T}(string, GrampsGetRequest, CancellationToken)"/> does.
/// </summary>
internal static class GrampsHandleResolver
{
    private static readonly GrampsGetRequest HandleOnlyRequest = new() { Profile = "none", Keys = ["handle"] };

    public static async Task<string> ResolveAsync(
        GrampsApiClient apiClient, string type, string idOrHandle, CancellationToken cancellationToken)
    {
        var collectionPath = GrampsObjectTypes.ResolveCollectionPath(type);

        if (GrampsIdentifiers.LooksLikeGrampsId(idOrHandle))
        {
            var found = await apiClient.FindByGrampsIdAsync<JsonElement>(
                collectionPath, idOrHandle, HandleOnlyRequest, cancellationToken);

            if (found.ValueKind == JsonValueKind.Undefined)
            {
                throw new GrampsApiException(HttpStatusCode.NotFound, $"No {type} found with Gramps ID '{idOrHandle}'.");
            }

            // Keys = ["handle"] on HandleOnlyRequest asks Gramps Web to trim the response to just
            // this field, but that's a hint some upstream versions may not honor — TryGetProperty
            // (rather than an unchecked GetProperty(...)!) turns a missing field into a real
            // GrampsApiException instead of an untranslated KeyNotFoundException reaching the
            // model as the MCP SDK's generic error message.
            if (!found.TryGetProperty("handle", out var handleProperty) || handleProperty.GetString() is not { } handle)
            {
                throw new GrampsApiException(
                    HttpStatusCode.InternalServerError,
                    $"Gramps API's lookup for {type} '{idOrHandle}' returned an object with no 'handle' field.");
            }

            return handle;
        }

        if (!GrampsIdentifiers.IsValidHandle(idOrHandle))
        {
            throw new ArgumentException($"'{idOrHandle}' is not a valid Gramps ID or handle.", nameof(idOrHandle));
        }

        return idOrHandle;
    }
}
