namespace GrampsMcp.Server.Gramps;

/// <summary>
/// A GQL filter request against a single object type's collection endpoint. GQL has no sort,
/// aggregation, joins, or cross-type support (see <c>docs/PLAN.md</c> facts #3-5), so this
/// carries only the pagination guardrail and the same profile/extend/keys/skipkeys shaping
/// params <see cref="GrampsListRequest"/> exposes (see <c>docs/PLAN.md</c> fact #14 — GQL honors
/// them just like the plain list endpoints).
/// </summary>
public sealed record GrampsGqlRequest
{
    public required string Gql { get; init; }

    public int Page { get; init; } = 1;

    public int? PageSize { get; init; }

    public string? Profile { get; init; }

    public IReadOnlyList<string>? Extend { get; init; }

    public IReadOnlyList<string>? Keys { get; init; }

    public IReadOnlyList<string>? SkipKeys { get; init; }
}
