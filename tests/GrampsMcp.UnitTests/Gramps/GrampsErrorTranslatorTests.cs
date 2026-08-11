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
