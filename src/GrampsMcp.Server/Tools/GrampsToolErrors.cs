using GrampsMcp.Server.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.Server.Tools;

/// <summary>
/// Translates <see cref="GrampsApiException"/> (an upstream Gramps Web error, e.g. a GQL 422
/// parse error) and <see cref="ArgumentException"/> (invalid tool input, e.g. an unknown object
/// type) into <see cref="McpException"/>, so their message reaches the calling model verbatim
/// instead of the MCP SDK's generic "An error occurred invoking '...'" fallback, which it
/// substitutes for any exception type it doesn't recognise as safe to expose.
/// </summary>
internal static class GrampsToolErrors
{
    public static async Task<T> TranslateAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (GrampsApiException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }
}
