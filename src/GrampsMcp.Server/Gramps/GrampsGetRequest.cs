namespace GrampsMcp.Server.Gramps;

/// <summary>
/// A single-object fetch request. Unlike <see cref="GrampsListRequest"/>, this carries no
/// page/pagesize — the Gramps Web single-object endpoint rejects those as unknown fields.
/// </summary>
public sealed record GrampsGetRequest
{
    /// <summary>Pass "none" to suppress the server's default profile expansion.</summary>
    public string? Profile { get; init; }

    /// <summary>Related-object keys to expand inline (e.g. "family_list,event_ref_list").</summary>
    public IReadOnlyList<string>? Extend { get; init; }

    public IReadOnlyList<string>? Keys { get; init; }

    public IReadOnlyList<string>? SkipKeys { get; init; }
}
