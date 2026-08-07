namespace GrampsMcp.Server.Gramps;

/// <summary>
/// A transaction-history list request. Unlike <see cref="GrampsListRequest"/>, this carries no
/// profile/extend/keys — <c>/api/transactions/history/</c> rejects those as unknown fields.
/// </summary>
public sealed record GrampsHistoryRequest
{
    public int Page { get; init; } = 1;

    public int? PageSize { get; init; }

    /// <summary>Sort key, e.g. "id" or "-id" for descending (most recent first).</summary>
    public string? Sort { get; init; }
}
