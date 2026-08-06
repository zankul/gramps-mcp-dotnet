namespace GrampsMcp.Server.Gramps;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Authenticated with Gramps Web as service account.")]
    public static partial void Authenticated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gramps token refresh failed; re-authenticating with service account.")]
    public static partial void TokenRefreshFailed(ILogger logger, Exception exception);
}
