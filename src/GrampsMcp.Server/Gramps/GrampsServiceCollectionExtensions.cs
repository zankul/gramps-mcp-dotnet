using GrampsMcp.Server.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace GrampsMcp.Server.Gramps;

public static class GrampsServiceCollectionExtensions
{
    private static readonly HashSet<HttpMethod> IdempotentMethods =
        [HttpMethod.Get, HttpMethod.Head, HttpMethod.Put, HttpMethod.Delete];

    /// <summary>
    /// Registers <see cref="GrampsApiClient"/> and everything it needs: options bound from
    /// environment variables and validated at startup, the token store, the auth handler, and a
    /// standard resilience pipeline for transient failures.
    /// </summary>
    public static IServiceCollection AddGrampsClient(this IServiceCollection services)
    {
        services
            .AddOptions<GrampsOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.ApiUrl = configuration["GRAMPS_API_URL"] ?? string.Empty;
                options.Username = configuration["GRAMPS_USERNAME"] ?? string.Empty;
                options.Password = configuration["GRAMPS_PASSWORD"] ?? string.Empty;
                options.MaxPageSize = configuration.GetValue("GRAMPS_MAX_PAGESIZE", 100);
                options.DefaultProfile = configuration["GRAMPS_DEFAULT_PROFILE"] ?? "self,families,events";
                options.LogLevel = configuration["MCP_LOG_LEVEL"];
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(GrampsTokenStore.AuthClientName, ConfigureBaseAddress);
        services.AddSingleton<GrampsTokenStore>();
        services.AddSingleton<GrampsPermissionsProvider>();
        services.AddTransient<GrampsAuthHandler>();

        services
            .AddHttpClient<GrampsApiClient>(ConfigureBaseAddress)
            // HttpClient's own Timeout (default 100s) would otherwise race the resilience
            // pipeline's TotalRequestTimeout below (120s) and win, throwing a raw
            // TaskCanceledException that bypasses GrampsToolErrors' Polly-specific handling.
            // The resilience handler's AttemptTimeout/TotalRequestTimeout are meant to be the
            // only timeouts governing the request.
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddHttpMessageHandler<GrampsAuthHandler>()
            .AddStandardResilienceHandler(resilienceOptions =>
            {
                // The default retry predicate only checks for a transient status/exception, not the
                // HTTP method — retrying a POST (create, merge, undo) after a transient fault whose
                // response never arrived can duplicate a non-idempotent write. Only retry methods
                // that are safe to repeat; GET/HEAD/PUT/DELETE are, POST/PATCH are not.
                resilienceOptions.Retry.ShouldHandle = args =>
                {
                    var method = args.Context.GetRequestMessage()?.Method;
                    var isIdempotent = method is not null && IdempotentMethods.Contains(method);
                    return ValueTask.FromResult(isIdempotent && HttpClientResiliencePredicates.IsTransient(args.Outcome));
                };

                // The library defaults (10s attempt / 30s total) are tuned for typical REST calls,
                // but GQL-backed calls (gramps_query/gramps_count) are measured at 10-15s per call
                // upstream (docs/PLAN.md) because GQL scans the whole database regardless of result
                // size. A 10s attempt timeout cuts those off mid-flight, and since GET is retried
                // (see ShouldHandle above), the aborted attempt then triggers a second full-database
                // scan, compounding the cost until the 30s total budget also aborts. Give GQL enough
                // room for one attempt (45s) plus headroom for a retried attempt within the total
                // budget (120s).
                resilienceOptions.AttemptTimeout.Timeout = TimeSpan.FromSeconds(45);
                resilienceOptions.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);

                // The standard resilience handler also requires the circuit breaker's sampling
                // window to be at least double the attempt timeout (so it observes enough attempts
                // per window to make a meaningful open/close decision); the library default (30s)
                // no longer satisfies that once AttemptTimeout grows to 45s above.
                resilienceOptions.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(100);
            });

        return services;
    }

    private static void ConfigureBaseAddress(IServiceProvider serviceProvider, HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptions<GrampsOptions>>().Value;
        client.BaseAddress = new Uri(options.ApiUrl);
    }
}
