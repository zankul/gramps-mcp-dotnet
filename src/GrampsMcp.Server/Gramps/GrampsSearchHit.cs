using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsMcp.Server.Configuration;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// One row from <c>/api/search/</c>. <see cref="Data"/> is the full, untrimmed raw object — the
/// search endpoint silently ignores <c>profile</c>/<c>keys</c>/<c>skipkeys</c> (measured, see
/// <c>ApiClientContractTests</c>), so there is no server-side way to shrink it. Client-side field
/// trimming was deliberately left out of this type: picking which fields are safe to drop varies
/// per Gramps object type and is a separate, riskier design decision than the page-size default
/// change that motivated this file. The primary mitigation for context bloat is
/// <see cref="GrampsOptions.SearchDefaultPageSize"/> — a small default hit count rather than a
/// per-hit trim.
/// </summary>
/// <remarks>
/// <see cref="Rank"/> and <see cref="Score"/> are nullable, not <c>double</c>: <c>/api/search/</c>
/// is known to return <c>"score": null</c> for otherwise-legitimate hits (see #84), and
/// <c>GrampsToolErrors</c> does not translate
/// <see cref="JsonException"/> — a non-nullable <c>double</c> deserializing a JSON <c>null</c>
/// would throw one, which the model would then see as the MCP SDK's generic, useless error
/// message instead of any real search result.
/// </remarks>
public sealed record GrampsSearchHit(
    string Handle,
    [property: JsonPropertyName("object_type")] string ObjectType,
    [property: JsonPropertyName("object")] JsonElement Data,
    double? Rank,
    double? Score);
