namespace GrampsMcp.Server.Gramps;

/// <summary>
/// A timeline fetch request. Like <see cref="GrampsListRequest"/>, this carries page/pagesize
/// guardrails — Gramps Web's timeline endpoints treat an omitted <c>page</c> as "return
/// everything unpaginated", the same trap as the plain list endpoints.
/// </summary>
public sealed record GrampsTimelineRequest
{
    public int Page { get; init; } = 1;

    public int? PageSize { get; init; }

    public IReadOnlyList<string>? Keys { get; init; }

    public IReadOnlyList<string>? SkipKeys { get; init; }

    public IReadOnlyList<string>? EventClasses { get; init; }

    public IReadOnlyList<string>? Events { get; init; }

    /// <summary>Date range filter: "-y/m/d" (before), "y/m/d-" (after), or "y/m/d-y/m/d" (range).</summary>
    public string? Dates { get; init; }

    /// <summary>Person timelines only: number of ancestor generations to include.</summary>
    public int? Ancestors { get; init; }

    /// <summary>Person timelines only: number of offspring generations to include.</summary>
    public int? Offspring { get; init; }
}
