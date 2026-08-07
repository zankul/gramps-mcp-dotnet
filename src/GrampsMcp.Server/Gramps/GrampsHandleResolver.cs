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
            return found.ValueKind != JsonValueKind.Undefined
                ? found.GetProperty("handle").GetString()!
                : throw new GrampsApiException(HttpStatusCode.NotFound, $"No {type} found with Gramps ID '{idOrHandle}'.");
        }

        if (!GrampsIdentifiers.IsValidHandle(idOrHandle))
        {
            throw new ArgumentException($"'{idOrHandle}' is not a valid Gramps ID or handle.", nameof(idOrHandle));
        }

        return idOrHandle;
    }
}
