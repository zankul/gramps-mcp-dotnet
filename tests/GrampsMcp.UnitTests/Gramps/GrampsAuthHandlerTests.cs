using System.Net;
using System.Text;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsAuthHandlerTests
{
    [Fact]
    public async Task SendAsync_AttachesBearerTokenFromTokenStore()
    {
        var authHandlerBackend = new TestHttpMessageHandler(request =>
            JsonResponse(HttpStatusCode.OK, """{"access_token":"token1","refresh_token":"refresh1"}"""));
        var tokenStore = CreateTokenStore(authHandlerBackend);

        var apiHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(tokenStore, apiHandler);

        await client.GetAsync("/api/people/I0001");

        var sentAuth = Assert.Single(apiHandler.Requests).Headers.Authorization;
        Assert.Equal("Bearer", sentAuth!.Scheme);
        Assert.Equal("token1", sentAuth.Parameter);
    }

    [Fact]
    public async Task SendAsync_On401_RefreshesTokenAndRetriesOnce()
    {
        var authCallCount = 0;
        var authHandlerBackend = new TestHttpMessageHandler(request =>
        {
            authCallCount++;
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/token/" => JsonResponse(HttpStatusCode.OK,
                    """{"access_token":"token1","refresh_token":"refresh1"}"""),
                "/api/token/refresh/" => JsonResponse(HttpStatusCode.OK, """{"access_token":"token2"}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var tokenStore = CreateTokenStore(authHandlerBackend);

        var apiCallCount = 0;
        var apiHandler = new TestHttpMessageHandler(_ =>
        {
            apiCallCount++;
            return apiCallCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = CreateClient(tokenStore, apiHandler);

        var response = await client.GetAsync("/api/people/I0001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, apiHandler.Requests.Count);
        Assert.Equal("token1", apiHandler.Requests[0].Headers.Authorization!.Parameter);
        Assert.Equal("token2", apiHandler.Requests[1].Headers.Authorization!.Parameter);
        Assert.Equal(2, authCallCount); // initial login + one refresh
    }

    [Fact]
    public async Task SendAsync_OnRepeated401_RetriesOnceThenSurfacesThe401()
    {
        // Regression guard for GrampsAuthHandler.SendAsync's early return
        // (`if (response.StatusCode != HttpStatusCode.Unauthorized) return response;`): if that were
        // ever changed to a loop, a server with permanently-revoked credentials would make the
        // handler hammer /api/token/ forever. Stubbing 401 on every call, forever, is what proves
        // the handler stops after exactly one retry instead of merely proving it retries at least once.
        var authHandlerBackend = new TestHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/api/token/" => JsonResponse(HttpStatusCode.OK,
                    """{"access_token":"token1","refresh_token":"refresh1"}"""),
                "/api/token/refresh/" => JsonResponse(HttpStatusCode.OK, """{"access_token":"token2"}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        var tokenStore = CreateTokenStore(authHandlerBackend);

        var apiHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient(tokenStore, apiHandler);

        var response = await client.GetAsync("/api/people/I0001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, apiHandler.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_On401WithRequestBody_RetriesWithBodyAndContentTypeIntact()
    {
        var authHandlerBackend = new TestHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/api/token/" => JsonResponse(HttpStatusCode.OK,
                    """{"access_token":"token1","refresh_token":"refresh1"}"""),
                "/api/token/refresh/" => JsonResponse(HttpStatusCode.OK, """{"access_token":"token2"}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        var tokenStore = CreateTokenStore(authHandlerBackend);

        var apiCallCount = 0;
        var apiHandler = new TestHttpMessageHandler(_ =>
        {
            apiCallCount++;
            return apiCallCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = CreateClient(tokenStore, apiHandler);

        var response = await client.PostAsync(
            "/api/people/", new StringContent("""{"gramps_id":"I0001"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, apiHandler.Requests.Count);

        var retriedRequest = apiHandler.Requests[1];
        Assert.Equal("token2", retriedRequest.Headers.Authorization!.Parameter);
        Assert.NotNull(retriedRequest.Content);
        Assert.Equal("application/json", retriedRequest.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("""{"gramps_id":"I0001"}""", await retriedRequest.Content.ReadAsStringAsync());
    }

    private static GrampsTokenStore CreateTokenStore(HttpMessageHandler authBackend)
    {
        var options = Options.Create(new GrampsOptions
        {
            ApiUrl = "https://tree.example.com",
            Username = "owner",
            Password = "owner",
        });
        var factory = new FakeHttpClientFactory(authBackend);
        return new GrampsTokenStore(factory, options, NullLogger<GrampsTokenStore>.Instance);
    }

    private static HttpClient CreateClient(GrampsTokenStore tokenStore, HttpMessageHandler apiBackend)
    {
        var authHandler = new GrampsAuthHandler(tokenStore) { InnerHandler = apiBackend };
        return new HttpClient(authHandler) { BaseAddress = new Uri("https://tree.example.com") };
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://tree.example.com") };
    }
}
