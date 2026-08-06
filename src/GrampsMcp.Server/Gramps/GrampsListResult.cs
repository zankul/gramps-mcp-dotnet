namespace GrampsMcp.Server.Gramps;

public sealed record GrampsListResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
