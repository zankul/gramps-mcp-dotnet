namespace GrampsMcp.Server.Gramps;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Authenticated with Gramps Web as service account.")]
    public static partial void Authenticated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gramps token refresh failed; re-authenticating with service account.")]
    public static partial void TokenRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "GRAMPS_ALLOW_WRITES is true, but the service account's JWT is missing permission(s): " +
            "{MissingPermissions}. Write tools requiring the missing permission(s) will not be registered.")]
    public static partial void WritePermissionsIncomplete(ILogger logger, string missingPermissions);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "GRAMPS_ALLOW_WRITES is true, but checking the service account's JWT permissions failed; " +
            "starting with write tools disabled for this run.")]
    public static partial void WritePermissionCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "/health reported unhealthy: Gramps Web is unreachable.")]
    public static partial void HealthCheckFailed(ILogger logger, Exception exception);
}
