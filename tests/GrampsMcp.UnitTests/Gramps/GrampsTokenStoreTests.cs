using System.Net;
using GrampsMcp.Server.Gramps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsTokenStoreTests
{
    [Fact]
    public async Task GetAccessTokenAsync_FirstCall_AuthenticatesExactlyOnce()
    {
        var handler = new TestHttpMessageHandler(_ => TestResponses.Json(
            HttpStatusCode.OK, """{"access_token":"token1","refresh_token":"refresh1"}"""));
        using var store = CreateStore(handler);

        var token = await store.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token1", token);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/token/", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetAccessTokenAsync_SecondCall_ReusesCachedTokenWithoutFurtherHttp()
    {
        var handler = new TestHttpMessageHandler(_ => TestResponses.Json(
            HttpStatusCode.OK, """{"access_token":"token1","refresh_token":"refresh1"}"""));
        using var store = CreateStore(handler);
        await store.GetAccessTokenAsync(CancellationToken.None);

        var token = await store.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token1", token);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetAccessTokenAsync_TenConcurrentCallers_ResultInExactlyOnePost()
    {
        // The whole reason GrampsTokenStore takes a lock: without it, ten callers racing on a
        // cold cache would each fire their own authentication request. Holding the first
        // response open with a TaskCompletionSource forces every caller to actually queue up
        // behind the semaphore before any of them can complete.
        var gate = new TaskCompletionSource();
        var handler = new TestHttpMessageHandler(async (_, _) =>
        {
            await gate.Task;
            return TestResponses.Json(HttpStatusCode.OK, """{"access_token":"token1","refresh_token":"refresh1"}""");
        });
        using var store = CreateStore(handler);

        var callers = Enumerable.Range(0, 10)
            .Select(_ => store.GetAccessTokenAsync(CancellationToken.None))
            .ToArray();
        gate.SetResult();
        var tokens = await Task.WhenAll(callers);

        Assert.All(tokens, token => Assert.Equal("token1", token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RefreshAsync_WithRefreshTokenAvailable_PostsToRefreshEndpointWithBearerRefreshToken()
    {
        var handler = new TestHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/token/" => TestResponses.Json(HttpStatusCode.OK, """{"access_token":"token1","refresh_token":"refresh1"}"""),
            "/api/token/refresh/" => TestResponses.Json(HttpStatusCode.OK, """{"access_token":"token2"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var store = CreateStore(handler);
        await store.GetAccessTokenAsync(CancellationToken.None);

        var refreshed = await store.RefreshAsync(CancellationToken.None);

        Assert.Equal("token2", refreshed);
        var refreshRequest = handler.Requests.Last();
        Assert.Equal("/api/token/refresh/", refreshRequest.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", refreshRequest.Headers.Authorization!.Scheme);
        Assert.Equal("refresh1", refreshRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task RefreshAsync_WhenRefreshTokenRejected_FallsBackToReauthenticatingWithServiceAccount()
    {
        // This branch (401 on /api/token/refresh/) was entirely unexercised before this test.
        var tokenCallCount = 0;
        var handler = new TestHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/token/")
            {
                tokenCallCount++;
                return tokenCallCount == 1
                    ? TestResponses.Json(HttpStatusCode.OK, """{"access_token":"token1","refresh_token":"refresh1"}""")
                    : TestResponses.Json(HttpStatusCode.OK, """{"access_token":"token2","refresh_token":"refresh2"}""");
            }

            return path == "/api/token/refresh/"
                ? TestResponses.Json(HttpStatusCode.Unauthorized, """{"error":"invalid refresh token"}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var store = CreateStore(handler);
        await store.GetAccessTokenAsync(CancellationToken.None);

        var refreshed = await store.RefreshAsync(CancellationToken.None);

        Assert.Equal("token2", refreshed);
        Assert.Equal(2, tokenCallCount);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath == "/api/token/refresh/");
    }

    [Fact]
    public async Task RefreshAsync_WithNoRefreshTokenAvailable_AuthenticatesDirectly()
    {
        var handler = new TestHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/token/" => TestResponses.Json(HttpStatusCode.OK, """{"access_token":"token1","refresh_token":"refresh1"}"""),
            "/api/token/refresh/" => throw new InvalidOperationException("should not be called"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var store = CreateStore(handler);

        var token = await store.RefreshAsync(CancellationToken.None);

        Assert.Equal("token1", token);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/token/", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RefreshAsync_WhenReauthenticateResponseOmitsRefreshToken_KeepsPreviouslyHeldOne()
    {
        // If AuthenticateAsync unconditionally overwrote _refreshToken instead of falling back to
        // the held value, the second RefreshAsync call below would find no refresh token and skip
        // straight to re-authenticating — this test would see no second /refresh/ request instead
        // of one still carrying "refresh1".
        var tokenCallCount = 0;
        var refreshCallCount = 0;
        var handler = new TestHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/token/")
            {
                tokenCallCount++;
                return tokenCallCount == 1
                    ? TestResponses.Json(HttpStatusCode.OK, """{"access_token":"token1","refresh_token":"refresh1"}""")
                    : TestResponses.Json(HttpStatusCode.OK, """{"access_token":"token2"}"""); // no refresh_token
            }

            if (path == "/api/token/refresh/")
            {
                refreshCallCount++;
                return refreshCallCount == 1
                    ? TestResponses.Json(HttpStatusCode.Unauthorized, "{}")
                    : TestResponses.Json(HttpStatusCode.OK, """{"access_token":"token3"}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var store = CreateStore(handler);
        await store.GetAccessTokenAsync(CancellationToken.None);
        await store.RefreshAsync(CancellationToken.None);

        var result = await store.RefreshAsync(CancellationToken.None);

        Assert.Equal("token3", result);
        var secondRefreshRequest = handler.Requests.Last();
        Assert.Equal("/api/token/refresh/", secondRefreshRequest.RequestUri!.AbsolutePath);
        Assert.Equal("refresh1", secondRefreshRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTokenResponseBodyIsEmpty_ThrowsGrampsApiException()
    {
        var handler = new TestHttpMessageHandler(_ => TestResponses.Json(HttpStatusCode.OK, "null"));
        using var store = CreateStore(handler);

        await Assert.ThrowsAsync<GrampsApiException>(() => store.GetAccessTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Logger_AfterAuthenticateAndFailedRefresh_NeverContainsTokenValues()
    {
        const string accessToken = "s3cr3t-access-token-value";
        const string refreshToken = "s3cr3t-refresh-token-value";
        var tokenCallCount = 0;
        var handler = new TestHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/token/")
            {
                tokenCallCount++;
                return tokenCallCount == 1
                    ? TestResponses.Json(
                        HttpStatusCode.OK,
                        $$"""{"access_token":"{{accessToken}}","refresh_token":"{{refreshToken}}"}""")
                    : TestResponses.Json(HttpStatusCode.OK, """{"access_token":"token2","refresh_token":"refresh2"}""");
            }

            return path == "/api/token/refresh/"
                ? TestResponses.Json(HttpStatusCode.Unauthorized, """{"error":"invalid refresh token"}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var logger = new RecordingLogger();
        using var store = CreateStore(handler, logger);

        await store.GetAccessTokenAsync(CancellationToken.None);
        await store.RefreshAsync(CancellationToken.None);

        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(accessToken, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains(refreshToken, StringComparison.Ordinal));
    }

    private static GrampsTokenStore CreateStore(HttpMessageHandler handler, ILogger<GrampsTokenStore>? logger = null) =>
        new(
            new FakeHttpClientFactory(handler),
            Options.Create(GrampsTestClientFactory.Options),
            logger ?? NullLogger<GrampsTokenStore>.Instance);

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri(GrampsTestClientFactory.Options.ApiUrl) };
    }

    /// <summary>Captures every formatted log message (and, for warnings, the exception's own text)
    /// so a test can assert a secret never appears in either — a mock would only verify that
    /// <c>Log()</c> was called, not what it actually rendered.</summary>
    private sealed class RecordingLogger : ILogger<GrampsTokenStore>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null)
            {
                Messages.Add(exception.ToString());
            }
        }
    }
}
