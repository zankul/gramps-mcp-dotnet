using System.Net;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using ModelContextProtocol;

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
    public async Task TranslateAsync_WithUnrelatedExceptionType_PropagatesUnchanged()
    {
        // Pins the deliberate narrowness of the catch: only GrampsApiException and
        // ArgumentException are translated. Anything else (e.g. a bug elsewhere in a tool body)
        // must surface as-is rather than being silently swallowed or reworded.
        var inner = new InvalidOperationException("unexpected failure");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => GrampsToolErrors.TranslateAsync<object>(() => throw inner));

        Assert.Same(inner, exception);
    }
}
