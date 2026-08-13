using System.ComponentModel;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

[McpServerToolType]
public sealed class GrampsTimelineTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_timeline")]
    [Description(
        "Paginated, chronological timeline of life events for a person or family. Person " +
        "timelines can also pull in ancestor/offspring generations. Accepts a Gramps ID or handle.")]
    public async Task<GrampsListResult<JsonElement>> TimelineAsync(
        [Description("Object type: person or family.")]
        string type,
        [Description("Gramps ID (e.g. 'I0155') or handle of the person or family.")]
        string idOrHandle,
        [Description(
            "Person only: number of ancestor generations to include (default 1, max 5). " +
            "0 is rejected by the server; omit this parameter instead of passing 0.")]
        int? ancestors = null,
        [Description(
            "Person only: number of offspring generations to include (default 1, max 5). " +
            "0 is rejected by the server; omit this parameter instead of passing 0.")]
        int? offspring = null,
        [Description(
            "Event class keywords to include, e.g. 'vital,family'. Options: vital, family, " +
            "religious, vocational, academic, travel, legal, residence, other, custom.")]
        IReadOnlyList<string>? eventClasses = null,
        [Description("Specific event type names to include. Birth and death are always included.")]
        IReadOnlyList<string>? events = null,
        [Description(
            "Date range filter: '-y/m/d' (before), 'y/m/d-' (after), or 'y/m/d-y/m/d' (range). " +
            "Month/day must NOT be zero-padded (e.g. '1850/1/1-' works, '1850/01/01-' is rejected " +
            "with a 422).")]
        string? dates = null,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Items per page. Omitting it returns the server's max page size; explicit values are still capped at that max.")] int? pagesize = null,
        [Description("Top-level keys to include in each event; all others are omitted.")]
        IReadOnlyList<string>? keys = null,
        CancellationToken cancellationToken = default)
    {
        return await GrampsToolErrors.TranslateAsync(async () =>
        {
            if (!string.Equals(type, "person", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(type, "family", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unknown timeline type '{type}'. Valid types: person, family.", nameof(type));
            }

            var isPerson = string.Equals(type, "person", StringComparison.OrdinalIgnoreCase);
            var handle = await GrampsHandleResolver.ResolveAsync(apiClient, type, idOrHandle, cancellationToken);
            var path = isPerson ? $"/api/people/{handle}/timeline" : $"/api/families/{handle}/timeline";

            var request = new GrampsTimelineRequest
            {
                Page = page,
                PageSize = pagesize,
                Keys = keys,
                EventClasses = eventClasses,
                Events = events,
                Dates = dates,
                Ancestors = isPerson ? ancestors : null,
                Offspring = isPerson ? offspring : null,
            };

            return await apiClient.ListTimelineAsync<JsonElement>(path, request, cancellationToken);
        });
    }
}
