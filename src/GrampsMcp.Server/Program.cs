using System.Text.Json;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Http;
using GrampsMcp.Server.Tools;
using ModelContextProtocol.Server;

// The transport choice has to be known before either host builder exists (it picks which one gets
// built), so it can't go through builder.Configuration the way MCP_LOG_LEVEL and the rest do below.
// This mirrors the sources those builders load by default — appsettings.json, environment
// variables, command-line args — so MCP_TRANSPORT isn't the one setting that ignores them.
var transport = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build()["MCP_TRANSPORT"] ?? "http";

if (string.Equals(transport, "stdio", StringComparison.OrdinalIgnoreCase))
{
    await RunStdioAsync();
}
else
{
    await RunHttpAsync(args);
}

static async Task RunStdioAsync()
{
    var builder = Host.CreateApplicationBuilder();

    // stdout is the MCP protocol channel under stdio; all logging must go to stderr instead.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    ApplyLogLevelOverride(builder.Configuration, builder.Logging);

    builder.Services.AddGrampsClient();
    var toolTypes = await GrampsToolTypeResolver.ResolveAsync(builder.Services, builder.Configuration);
    builder.Services.AddMcpServer().WithStdioServerTransport().WithTools(toolTypes);

    var host = builder.Build();
    await host.RunAsync();
}

static async Task RunHttpAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    ApplyLogLevelOverride(builder.Configuration, builder.Logging);

    builder.Services
        .AddOptions<OriginValidationOptions>()
        .Configure<IConfiguration>((options, configuration) =>
        {
            var raw = configuration["MCP_ALLOWED_ORIGINS"];
            if (!string.IsNullOrWhiteSpace(raw))
            {
                options.AllowedOrigins = new HashSet<string>(
                    raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase);
            }
        })
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddGrampsClient();
    var toolTypes = await GrampsToolTypeResolver.ResolveAsync(builder.Services, builder.Configuration);
    builder.Services.AddMcpServer().WithHttpTransport().WithTools(toolTypes);

    var app = builder.Build();

    // The only defense against DNS rebinding for this loopback-bound, unauthenticated transport
    // (see SECURITY.md) — must run ahead of MapMcp() so a disallowed Origin never reaches it.
    app.UseOriginValidation();

    app.MapMcp();

    app.MapGet("/health", async (GrampsApiClient apiClient, ILogger<Program> logger, CancellationToken cancellationToken) =>
    {
        try
        {
            await apiClient.GetAsync<JsonElement>("api/metadata/", cancellationToken);
            return Results.Ok(new { status = "healthy" });
        }
        catch (Exception exception) when (exception is GrampsApiException
            or HttpRequestException
            or Polly.Timeout.TimeoutRejectedException
            or Polly.CircuitBreaker.BrokenCircuitException)
        {
            // The real exception detail (which may embed the raw upstream response body — an
            // nginx error page, a gunicorn traceback, internal hostnames/ports) is logged
            // server-side only. The HTTP response gets a fixed, non-revealing string: unlike the
            // tool-facing GrampsErrorTranslator passthrough (deliberate, for a model consumer),
            // /health has no business exposing upstream internals to whatever hit this endpoint.
            Log.HealthCheckFailed(logger, exception);
            return Results.Problem(
                detail: "Gramps Web could not be reached. See server logs for details.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Gramps Web is unreachable.");
        }
    });

    await app.RunAsync();
}

/// <summary>
/// Applies <c>MCP_LOG_LEVEL</c> as a minimum-level override on top of each transport's existing
/// logging setup. Unset leaves the transport's current default untouched. An invalid value is
/// left for <see cref="GrampsOptions"/>'s <c>ValidateOnStart</c> check to reject with a clear
/// error when the host starts — this method is not the source of truth for validation, so it
/// doesn't duplicate that check.
/// </summary>
static void ApplyLogLevelOverride(IConfiguration configuration, ILoggingBuilder logging)
{
    var raw = configuration["MCP_LOG_LEVEL"];
    if (raw is not null && GrampsOptions.TryParseLogLevel(raw, out var level))
    {
        logging.SetMinimumLevel(level);
    }
}

/// <summary>Exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in unit tests.</summary>
public partial class Program;
