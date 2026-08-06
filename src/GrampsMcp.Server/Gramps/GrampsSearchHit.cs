using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsMcp.Server.Gramps;

/// <summary>One row from <c>/api/search/</c>.</summary>
public sealed record GrampsSearchHit(
    string Handle,
    [property: JsonPropertyName("object_type")] string ObjectType,
    [property: JsonPropertyName("object")] JsonElement Data,
    double Rank,
    double Score);
