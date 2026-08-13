using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using Microsoft.AspNetCore.WebUtilities;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

[McpServerToolType]
public sealed class GrampsRelationTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_relation")]
    [Description(
        "Calculate the most direct kinship relationship between two people, e.g. \"first cousin " +
        "once removed\" — the genealogy question a filter or search cannot answer.")]
    public async Task<JsonElement> RelationAsync(
        [Description("Gramps ID (e.g. 'I0155') or handle of the first person.")]
        string personA,
        [Description("Gramps ID (e.g. 'I0155') or handle of the second person.")]
        string personB,
        [Description("Maximum number of generations to search for a common ancestor (default 15).")]
        int? depth = null,
        [Description("Language code for the relationship string, e.g. 'en', 'de'.")]
        string? locale = null,
        CancellationToken cancellationToken = default)
    {
        return await GrampsToolErrors.TranslateAsync(async () =>
        {
            var handleA = await GrampsHandleResolver.ResolveAsync(apiClient, "person", personA, cancellationToken);
            var handleB = await GrampsHandleResolver.ResolveAsync(apiClient, "person", personB, cancellationToken);

            var query = new List<KeyValuePair<string, string?>>();
            if (depth is { } depthValue)
            {
                query.Add(new KeyValuePair<string, string?>("depth", depthValue.ToString(CultureInfo.InvariantCulture)));
            }

            if (!string.IsNullOrEmpty(locale))
            {
                query.Add(new KeyValuePair<string, string?>("locale", locale));
            }

            var path = QueryHelpers.AddQueryString($"api/relations/{handleA}/{handleB}", query);
            return await apiClient.GetAsync<JsonElement>(path, cancellationToken);
        });
    }
}
