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

    [Fact]
    public async Task SendAsync_OnTransientFailure_RetriesGet()
    {
        var apiHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = BuildProvider(apiHandler);
        var apiClient = provider.GetRequiredService<GrampsApiClient>();

        // Bounded instead of letting all retry attempts play out (base 2s exponential backoff would
        // make this test take ~15s): cancel once the first retry has had time to fire, which is
        // enough to prove GET is retried at all.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            await apiClient.GetAsync<JsonElement>("/api/people/I0001", cancellation.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or GrampsApiException)
        {
            // Expected: either the bounded cancellation or the pipeline exhausting its own retries
            // first, both of which leave apiHandler.Requests populated for the assertion below.
        }

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

    private static ServiceProvider BuildProvider(HttpMessageHandler apiHandler)
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

        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
