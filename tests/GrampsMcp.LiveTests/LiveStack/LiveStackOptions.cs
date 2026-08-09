namespace GrampsMcp.LiveTests.LiveStack;

/// <summary>
/// Connection details for the disposable <c>docker-compose.test.yml</c> stack (see
/// <c>docs/testing.md</c>), resolved from environment variables with defaults matching that
/// compose file's published ports and seeded owner account.
/// </summary>
public sealed record LiveStackOptions
{
    public const string ApiUrlVariable = "GRAMPS_LIVE_API_URL";
    public const string McpUrlVariable = "GRAMPS_LIVE_MCP_URL";
    public const string UsernameVariable = "GRAMPS_LIVE_USERNAME";
    public const string PasswordVariable = "GRAMPS_LIVE_PASSWORD";

    public required string ApiUrl { get; init; }

    public required string McpUrl { get; init; }

    public required string Username { get; init; }

    public required string Password { get; init; }

    public static LiveStackOptions FromEnvironment() => new()
    {
        ApiUrl = Environment.GetEnvironmentVariable(ApiUrlVariable) is { Length: > 0 } apiUrl
            ? apiUrl
            : "http://localhost:8091",
        McpUrl = Environment.GetEnvironmentVariable(McpUrlVariable) is { Length: > 0 } mcpUrl
            ? mcpUrl
            : "http://localhost:8081",
        Username = Environment.GetEnvironmentVariable(UsernameVariable) is { Length: > 0 } username
            ? username
            : "owner",
        Password = Environment.GetEnvironmentVariable(PasswordVariable) is { Length: > 0 } password
            ? password
            : "owner",
    };
}
