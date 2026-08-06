using System.ComponentModel;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

[McpServerToolType]
public sealed class GrampsTypesTool(GrampsApiClient apiClient)
{
    [McpServerTool(Name = "gramps_types")]
    [Description(
        "List the default and custom type vocabularies (event types, attribute types, place types, " +
        "name types, etc.) so callers know which strings are valid.")]
    public async Task<JsonElement> GetAsync(CancellationToken cancellationToken = default) =>
        await GrampsToolErrors.TranslateAsync(() => apiClient.GetAsync<JsonElement>("/api/types/", cancellationToken));
}
