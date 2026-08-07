namespace GrampsMcp.Server.Gramps;

/// <summary>
/// A GQL filter request against a single object type's collection endpoint. GQL has no sort,
/// aggregation, joins, or cross-type support (see <c>docs/PLAN.md</c> facts #3-5), so this
/// carries only the pagination guardrail and <see cref="Keys"/> shaping alongside the filter.
/// </summary>
public sealed record GrampsGqlRequest
{
    public required string Gql { get; init; }

    public int Page { get; init; } = 1;

    public int? PageSize { get; init; }

    public IReadOnlyList<string>? Keys { get; init; }
}
