using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrampsMcp.LiveTests.LiveStack;

/// <summary>
/// Collection fixture that probes the live Gramps Web stack once per test run, authenticates,
/// and — only when the stack is actually ready — builds the production DI graph
/// (<see cref="GrampsServiceCollectionExtensions.AddGrampsClient"/>) so live tests exercise the
/// real query builder, auth handler, and resilience pipeline against a real server rather than a
/// stub. Also exposes a raw, bearer-authenticated <see cref="HttpClient"/> with none of those
/// guardrails, for contract tests that need to see the server's response to an unshaped request.
/// </summary>
public sealed class LiveGrampsFixture : IAsyncLifetime
{
    // Same collision as LiveStackProbe's own POST /api/token/ call (see the comment there):
    // GrampsTokenStore lazily authenticates on its first real request, which — moments after the
    // probe's own token call — can still land inside Gramps Web's 1 request/second bucket for
    // POST /api/token/ and come back 429. Forcing that first request here, retried, means the
    // token gets cached (GrampsTokenStore keeps it for the DI container's lifetime) before any
    // test body runs, instead of surfacing the 429 as a confusing failure in an unrelated test.
    private const int MaxWarmUpAttempts = 3;
    private static readonly TimeSpan WarmUpRetryDelay = TimeSpan.FromMilliseconds(1500);

    private ServiceProvider? _serviceProvider;

    public LiveStackOptions Options { get; } = LiveStackOptions.FromEnvironment();

    /// <summary>The outcome of the readiness probe. Every live test must check this via
    /// <see cref="LiveStackGate.Require"/> before doing anything else.</summary>
    public LiveStackState State { get; private set; } = LiveStackState.StackUnreachable;

    public IReadOnlySet<string> Permissions { get; private set; } = new HashSet<string>();

    /// <summary>Valid only when <see cref="State"/> is <see cref="LiveStackState.Ready"/>.</summary>
    public GrampsApiClient ApiClient { get; private set; } = null!;

    /// <summary>Valid only when <see cref="State"/> is <see cref="LiveStackState.Ready"/>.</summary>
    public HttpClient RawClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var probeClient = new HttpClient
        {
            BaseAddress = new Uri(Options.ApiUrl),
            Timeout = TimeSpan.FromSeconds(15),
        };

        var probeResult = await LiveStackProbe.ProbeAsync(Options, probeClient, CancellationToken.None);
        State = probeResult.State;
        Permissions = probeResult.Permissions ?? new HashSet<string>();

        if (State != LiveStackState.Ready)
        {
            // Nothing further to build — LiveStackGate.Require stops every test body before
            // ApiClient/RawClient would otherwise be touched while null.
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GRAMPS_API_URL"] = Options.ApiUrl,
                ["GRAMPS_USERNAME"] = Options.Username,
                ["GRAMPS_PASSWORD"] = Options.Password,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddGrampsClient();
        _serviceProvider = services.BuildServiceProvider();

        ApiClient = _serviceProvider.GetRequiredService<GrampsApiClient>();

        if (!await WarmUpTokenAsync())
        {
            State = LiveStackState.AuthFailed;
            return;
        }

        RawClient = new HttpClient { BaseAddress = new Uri(Options.ApiUrl) };
        RawClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", probeResult.AccessToken);
    }

    /// <summary>
    /// Forces <see cref="GrampsApiClient"/>'s first real request now, so <c>GrampsTokenStore</c>
    /// authenticates (and caches the token) during fixture setup rather than lazily inside the
    /// first test body — retried on 429 only; any other failure propagates immediately.
    /// </summary>
    private async Task<bool> WarmUpTokenAsync()
    {
        for (var attempt = 1; attempt <= MaxWarmUpAttempts; attempt++)
        {
            try
            {
                await ApiClient.GetAsync<JsonElement>("/api/metadata/", CancellationToken.None);
                return true;
            }
            catch (GrampsApiException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (attempt == MaxWarmUpAttempts)
                {
                    return false;
                }

                await Task.Delay(WarmUpRetryDelay);
            }
        }

        return false;
    }

    public async Task DisposeAsync()
    {
        RawClient?.Dispose();

        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }
}
