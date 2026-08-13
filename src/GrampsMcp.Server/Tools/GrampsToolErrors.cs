using GrampsMcp.Server.Gramps;
using ModelContextProtocol;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace GrampsMcp.Server.Tools;

/// <summary>
/// Translates <see cref="GrampsApiException"/> (an upstream Gramps Web error, e.g. a GQL 422
/// parse error), <see cref="ArgumentException"/> (invalid tool input, e.g. an unknown object
/// type), and the resilience-pipeline exceptions that can surface when the upstream call times
/// out or the circuit breaker trips (<see cref="TimeoutRejectedException"/>,
/// <see cref="BrokenCircuitException"/>, <see cref="HttpRequestException"/>) into
/// <see cref="McpException"/>, so their message reaches the calling model verbatim instead of the
/// MCP SDK's generic "An error occurred invoking '...'" fallback, which it substitutes for any
/// exception type it doesn't recognise as safe to expose.
/// </summary>
internal static class GrampsToolErrors
{
    public static async Task<T> TranslateAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (GrampsApiException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new McpException(
                "The request to Gramps Web timed out. GQL-backed tools (gramps_query, gramps_count) "
                    + "can take 10-15s; if this keeps happening, the upstream server may be overloaded "
                    + "or unreachable.",
                exception);
        }
        catch (BrokenCircuitException exception)
        {
            throw new McpException(
                "Gramps Web is currently unreachable (circuit breaker open after repeated failures). "
                    + "Wait a moment before retrying.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new McpException($"Failed to reach Gramps Web: {exception.Message}", exception);
        }
    }
}
