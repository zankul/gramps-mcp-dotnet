using System.Net;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsErrorTranslatorTests
{
    [Fact]
    public async Task EnsureSuccessAsync_WhenSuccessStatus_DoesNotThrow()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        await GrampsErrorTranslator.EnsureSuccessAsync(response, CancellationToken.None);
    }

    [Fact]
    public async Task EnsureSuccessAsync_OnHtmlInternalServerError_ReplacesBodyWithShortMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<!doctype html>\n<html><body><h1>Internal Server Error</h1></body></html>"),
        };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => GrampsErrorTranslator.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.DoesNotContain("<html", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("500", exception.Message, StringComparison.Ordinal);
        Assert.Contains("locked", exception.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
    }

    [Fact]
    public async Task EnsureSuccessAsync_OnHtmlBadGateway_OmitsLockHintAndKeepsExcerpt()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("\uFEFF<html><head><title>502</title></head><body><h1>Bad Gateway</h1></body></html>"),
        };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => GrampsErrorTranslator.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.DoesNotContain("locked", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("<", exception.Message, StringComparison.Ordinal);
        Assert.Contains("502 Bad Gateway", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureSuccessAsync_OnGqlParseError_PassesErrorMessageVerbatim()
    {
        const string parseError = "Expected end of text, found '~' (at char 31), (line:1, col:32)";
        using var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent($$"""{"error": "{{parseError}}"}"""),
        };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => GrampsErrorTranslator.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.Equal(parseError, exception.Message);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
    }

    [Fact]
    public async Task EnsureSuccessAsync_WhenBodyIsNotJson_SurfacesRawBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream is down"),
        };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => GrampsErrorTranslator.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.Equal("upstream is down", exception.Message);
    }

    [Fact]
    public async Task EnsureSuccessAsync_WhenBodyEmpty_FallsBackToReasonPhrase()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            ReasonPhrase = "Internal Server Error",
        };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => GrampsErrorTranslator.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.Equal("Internal Server Error", exception.Message);
    }

    [Fact]
    public async Task EnsureSuccessAsync_OnNestedErrorObjectShape_ExtractsNestedMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"error": {"code": 422, "message": "query: page: Unknown field."}}"""),
        };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => GrampsErrorTranslator.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.Equal("query: page: Unknown field.", exception.Message);
    }

    [Fact]
    public async Task EnsureSuccessAsync_WhenOnlyMessageFieldPresent_ExtractsMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"message": "from the message field"}"""),
        };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => GrampsErrorTranslator.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.Equal("from the message field", exception.Message);
    }

    [Fact]
    public async Task EnsureSuccessAsync_WhenOnlyDetailFieldPresent_ExtractsDetail()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"detail": "from the detail field"}"""),
        };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => GrampsErrorTranslator.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.Equal("from the detail field", exception.Message);
    }

    [Fact]
    public async Task EnsureSuccessAsync_WhenErrorAndMessageBothPresent_PrefersError()
    {
        // Pins the field precedence order (error, then message, then detail) declared by
        // GrampsErrorTranslator.ErrorFieldNames.
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error": "from error", "message": "from message"}"""),
        };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => GrampsErrorTranslator.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.Equal("from error", exception.Message);
    }

    [Fact]
    public async Task EnsureSuccessAsync_OnServiceUnavailable_PassesNestedMessageThroughUnrewritten()
    {
        // The semantic-search-specific rewrite lives at the GrampsApiClient.SearchAsync call
        // site, not here — this type has no notion of which endpoint produced the response.
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                """{"error": {"code": 503, "message": "VECTOR_EMBEDDING_MODEL option not set"}}"""),
        };

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => GrampsErrorTranslator.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.Equal("VECTOR_EMBEDDING_MODEL option not set", exception.Message);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }
}
