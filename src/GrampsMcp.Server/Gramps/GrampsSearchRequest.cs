namespace GrampsMcp.Server.Gramps;

/// <summary>A request to <c>/api/search/</c> — the indexed, near-instant lookup path.</summary>
public sealed record GrampsSearchRequest : GrampsListRequest
{
    public required string Query { get; init; }

    /// <summary>Restrict results to these object types (e.g. "person", "family").</summary>
    public IReadOnlyList<string>? Types { get; init; }

    public bool Semantic { get; init; }
}
