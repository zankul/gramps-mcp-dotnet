using System.Net;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using ModelContextProtocol;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsToolErrorsTests
{
    [Fact]
    public async Task TranslateAsync_WithGrampsApiException_BecomesMcpExceptionPreservingMessageAndInner()
    {
        var inner = new GrampsApiException(HttpStatusCode.UnprocessableEntity, "Expected end of text, found '~'.");

        var exception = await Assert.ThrowsAsync<McpException>(
            () => GrampsToolErrors.TranslateAsync<object>(() => throw inner));

        Assert.Equal(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public async Task TranslateAsync_WithArgumentException_BecomesMcpExceptionPreservingMessageAndInner()
    {
        var inner = new ArgumentException("'human' is not a known Gramps object type.", "type");

        var exception = await Assert.ThrowsAsync<McpException>(
            () => GrampsToolErrors.TranslateAsync<object>(() => throw inner));

        Assert.Equal(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public async Task TranslateAsync_WithTimeoutRejectedException_BecomesMcpExceptionWithReadableMessage()
    {
        // Regression for #57: a GQL call cut off by the resilience pipeline's attempt/total
        // timeout used to surface as the MCP SDK's generic "An error occurred invoking '...'"
        // because TimeoutRejectedException isn't a GrampsApiException.
        var inner = new TimeoutRejectedException("attempt timed out");

        var exception = await Assert.ThrowsAsync<McpException>(
            () => GrampsToolErrors.TranslateAsync<object>(() => throw inner));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public async Task TranslateAsync_WithBrokenCircuitException_BecomesMcpExceptionWithReadableMessage()
    {
        var inner = new BrokenCircuitException("circuit is open");

        var exception = await Assert.ThrowsAsync<McpException>(
            () => GrampsToolErrors.TranslateAsync<object>(() => throw inner));

        Assert.Contains("unreachable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public async Task TranslateAsync_WithHttpRequestException_BecomesMcpExceptionPreservingMessageAndInner()
    {
        var inner = new HttpRequestException("connection refused");

        var exception = await Assert.ThrowsAsync<McpException>(
            () => GrampsToolErrors.TranslateAsync<object>(() => throw inner));

        Assert.Contains(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public async Task TranslateAsync_WithUnrelatedExceptionType_PropagatesUnchanged()
    {
        // Pins the deliberate narrowness of the catch: only GrampsApiException,
        // ArgumentException, and the resilience-pipeline exceptions (timeout/circuit-breaker/
        // HttpRequestException) are translated. Anything else (e.g. a bug elsewhere in a tool
        // body) must surface as-is rather than being silently swallowed or reworded.
        var inner = new InvalidOperationException("unexpected failure");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => GrampsToolErrors.TranslateAsync<object>(() => throw inner));

        Assert.Same(inner, exception);
    }
}
