using GrampsMcp.Server.Configuration;
using Microsoft.Extensions.Options;

namespace GrampsMcp.Server.Gramps;

public static class GrampsServiceCollectionExtensions
{
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
                options.AllowWrites = configuration.GetValue("GRAMPS_ALLOW_WRITES", false);
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
            .AddStandardResilienceHandler();

        return services;
    }

    private static void ConfigureBaseAddress(IServiceProvider serviceProvider, HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptions<GrampsOptions>>().Value;
        client.BaseAddress = new Uri(options.ApiUrl);
    }
}
