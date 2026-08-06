namespace GrampsMcp.Server.Gramps;

/// <summary>
/// A list request before guardrails are applied. <see cref="GrampsApiClient"/> coerces
/// <see cref="Page"/> to at least 1 and caps <see cref="PageSize"/> — Gramps Web treats
/// <c>page=0</c> as "return everything unpaginated", which this type exists to prevent.
/// </summary>
public sealed record GrampsListRequest
{
    public int Page { get; init; } = 1;

    public int? PageSize { get; init; }

    /// <summary>Pass "none" to suppress the server's default profile expansion.</summary>
    public string? Profile { get; init; }

    public IReadOnlyList<string>? Keys { get; init; }

    public IReadOnlyList<string>? SkipKeys { get; init; }
}
