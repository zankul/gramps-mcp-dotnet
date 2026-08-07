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
            });

        return services;
    }

    private static void ConfigureBaseAddress(IServiceProvider serviceProvider, HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptions<GrampsOptions>>().Value;
        client.BaseAddress = new Uri(options.ApiUrl);
    }
}
