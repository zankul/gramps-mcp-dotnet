using System.Text.Json.Serialization;

namespace GrampsMcp.Server.Gramps;

internal sealed record GrampsTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken);
