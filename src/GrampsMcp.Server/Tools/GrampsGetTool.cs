using System.ComponentModel;
using System.Net;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

[McpServerToolType]
public sealed class GrampsGetTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_get")]
    [Description("Fetch a single Gramps object by type and ID or handle.")]
    public async Task<JsonElement> GetAsync(
        [Description("Object type: person, family, event, place, citation, source, repository, media, note, tag.")]
        string type,
        [Description("Either the object's Gramps ID (e.g. 'I0155') or its internal handle.")]
        string idOrHandle,
        [Description("Related-object keys to expand inline, e.g. 'family_list,event_ref_list'. Use 'all' for everything.")]
        IReadOnlyList<string>? extend = null,
        [Description("Profile sections to include, e.g. 'self,families,events'. Pass 'none' to disable.")]
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GrampsGetRequest { Profile = profile, Extend = extend };

        return await GrampsToolErrors.TranslateAsync(async () =>
        {
            var collectionPath = GrampsObjectTypes.ResolveCollectionPath(type);

            if (GrampsIdentifiers.LooksLikeGrampsId(idOrHandle))
            {
                var found = await apiClient.FindByGrampsIdAsync<JsonElement>(collectionPath, idOrHandle, request, cancellationToken);
                return found.ValueKind != JsonValueKind.Undefined
                    ? found
                    : throw new GrampsApiException(HttpStatusCode.NotFound, $"No {type} found with Gramps ID '{idOrHandle}'.");
            }

            if (!GrampsIdentifiers.IsValidHandle(idOrHandle))
            {
                throw new ArgumentException(
                    $"'{idOrHandle}' is not a valid Gramps ID or handle.", nameof(idOrHandle));
            }

            return await apiClient.GetAsync<JsonElement>($"{collectionPath}{idOrHandle}", request, cancellationToken);
        });
    }
}
