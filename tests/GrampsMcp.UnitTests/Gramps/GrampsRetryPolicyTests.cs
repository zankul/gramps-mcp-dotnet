using System.Net;
using System.Text;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace GrampsMcp.UnitTests.Gramps;

/// <summary>
/// Proves the guardrail added in AddGrampsClient's ShouldHandle predicate: a transient failure on
/// a non-idempotent write (POST) must never be retried, since the response for a duplicated
/// gramps_create/merge/undo can't be told apart from a fresh one. Deliberately exercises the real
/// DI-wired resilience pipeline end to end rather than an extracted predicate, since a passing
/// isolated predicate test wouldn't catch the pipeline being wired up wrong.
/// </summary>
public class GrampsRetryPolicyTests
{
    [Fact]
    public async Task SendAsync_OnTransientFailure_DoesNotRetryPost()
    {
        var apiHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = BuildProvider(apiHandler);
        var apiClient = provider.GetRequiredService<GrampsApiClient>();

        await Assert.ThrowsAsync<GrampsApiException>(() =>
            apiClient.CreateAsync("/api/people/", JsonDocument.Parse("{}").RootElement, CancellationToken.None));

        Assert.Single(apiHandler.Requests);
    }

    // Timeout is a belt-and-suspenders bound, not the mechanism that keeps these tests fast: if
    // shrinkRetryDelay's Retry.Delay override (see BuildProvider) ever silently stopped applying —
    // e.g. AddStandardResilienceHandler's named-options key drifting from
    // $"{nameof(GrampsApiClient)}-standard" — the pipeline would fall back to real exponential
    // backoff bounded only by the 120s TotalRequestTimeout, turning a fast, obvious failure into a
    // multi-minute CI hang. 5s comfortably covers the near-zero-delay retry budget while still
    // failing fast if the override ever silently stops applying.
    [Fact(Timeout = 5000)]
    public async Task SendAsync_OnTransientFailure_RetriesGet()
    {
        var apiHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = BuildProvider(apiHandler, shrinkRetryDelay: true);
        var apiClient = provider.GetRequiredService<GrampsApiClient>();

        // With the retry delay shrunk to near-zero (see BuildProvider), the pipeline runs its full
        // retry budget in milliseconds instead of racing a multi-second exponential backoff — no
        // bounded CancellationTokenSource needed to keep this deterministic.
        await Assert.ThrowsAsync<GrampsApiException>(() =>
            apiClient.GetAsync<JsonElement>("/api/people/I0001", CancellationToken.None));

        Assert.True(apiHandler.Requests.Count > 1, $"expected more than one request, got {apiHandler.Requests.Count}");
    }

    [Fact(Timeout = 5000)]
    public async Task SendAsync_OnTransientFailure_RetriesPut()
    {
        // Companion to RetriesGet above: PUT is in IdempotentMethods too (Gramps Web's PUT replaces
        // the whole object, which is safe to repeat), but nothing previously pinned that — only GET
        // -retried and POST-not-retried were tested, so dropping PUT from the set would silently
        // remove transient-fault resilience from gramps_update with no test failing.
        var apiHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = BuildProvider(apiHandler, shrinkRetryDelay: true);
        var apiClient = provider.GetRequiredService<GrampsApiClient>();

        await Assert.ThrowsAsync<GrampsApiException>(() =>
            apiClient.ReplaceAsync("/api/people/I0001", JsonDocument.Parse("{}").RootElement, CancellationToken.None));

        Assert.True(apiHandler.Requests.Count > 1, $"expected more than one request, got {apiHandler.Requests.Count}");
    }

    [Fact(Timeout = 5000)]
    public async Task SendAsync_OnTransientFailure_RetriesDelete()
    {
        // Same guardrail as RetriesPut above, for DELETE (gramps_delete).
        var apiHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = BuildProvider(apiHandler, shrinkRetryDelay: true);
        var apiClient = provider.GetRequiredService<GrampsApiClient>();

        await Assert.ThrowsAsync<GrampsApiException>(() =>
            apiClient.DeleteAsync("/api/people/I0001", CancellationToken.None));

        Assert.True(apiHandler.Requests.Count > 1, $"expected more than one request, got {apiHandler.Requests.Count}");
    }

    /// <summary>
    /// Pins the timeouts configured in AddGrampsClient against the library defaults (10s attempt /
    /// 30s total), which are too short for GQL-backed calls (gramps_query/gramps_count) measured
    /// at 10-15s per call (docs/PLAN.md) — see issue #57. Resolving the named options here also
    /// exercises the standard resilience handler's own startup validation (attempt timeout must be
    /// less than total timeout, and circuit breaker sampling duration must be at least double the
    /// attempt timeout), so a future edit that violates either constraint fails this test instead
    /// of failing silently until the app is started.
    /// </summary>
    [Fact]
    public async Task AddGrampsClient_ConfiguresTimeoutsLongEnoughForGql()
    {
        var apiHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await using var provider = BuildProvider(apiHandler);

        var options = provider
            .GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{nameof(GrampsApiClient)}-standard");

        Assert.Equal(TimeSpan.FromSeconds(45), options.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(120), options.TotalRequestTimeout.Timeout);
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler apiHandler, bool shrinkRetryDelay = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GRAMPS_API_URL"] = "https://tree.example.com",
                ["GRAMPS_USERNAME"] = "owner",
                ["GRAMPS_PASSWORD"] = "owner",
            })
            .Build();

        var authHandler = new TestHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.OK, """{"access_token":"token1","refresh_token":"refresh1"}"""));

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddGrampsClient();
        services.AddHttpClient(GrampsTokenStore.AuthClientName).ConfigurePrimaryHttpMessageHandler(() => authHandler);
        services.AddHttpClient<GrampsApiClient>().ConfigurePrimaryHttpMessageHandler(() => apiHandler);

        if (shrinkRetryDelay)
        {
            // Registered after AddGrampsClient's own Configure call above, so this overrides Retry.Delay
            // on the same named options instance ($"{nameof(GrampsApiClient)}-standard") without
            // disturbing the AttemptTimeout/TotalRequestTimeout/CircuitBreaker settings under test
            // elsewhere. Lets retry-exhaustion tests run in milliseconds instead of racing the real
            // multi-second exponential backoff against a CancellationTokenSource.
            services.Configure<HttpStandardResilienceOptions>(
                $"{nameof(GrampsApiClient)}-standard",
                options => options.Retry.Delay = TimeSpan.FromMilliseconds(1));
        }

        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
