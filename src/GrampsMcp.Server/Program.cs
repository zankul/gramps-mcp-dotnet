using System.Reflection;
using System.Text.Json;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;

var transport = Environment.GetEnvironmentVariable("MCP_TRANSPORT") ?? "http";

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
    var toolTypes = await ResolveToolTypesAsync(builder.Services, builder.Configuration);
    builder.Services.AddMcpServer().WithStdioServerTransport().WithTools(toolTypes);

    var host = builder.Build();
    await host.RunAsync();
}

static async Task RunHttpAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    ApplyLogLevelOverride(builder.Configuration, builder.Logging);

    builder.Services.AddGrampsClient();
    var toolTypes = await ResolveToolTypesAsync(builder.Services, builder.Configuration);
    builder.Services.AddMcpServer().WithHttpTransport().WithTools(toolTypes);

    var app = builder.Build();

    app.MapMcp();

    app.MapGet("/health", async (GrampsApiClient apiClient, CancellationToken cancellationToken) =>
    {
        try
        {
            await apiClient.GetAsync<JsonElement>("/api/metadata/", cancellationToken);
            return Results.Ok(new { status = "healthy" });
        }
        catch (Exception exception) when (exception is GrampsApiException
            or HttpRequestException
            or Polly.Timeout.TimeoutRejectedException
            or Polly.CircuitBreaker.BrokenCircuitException)
        {
            return Results.Problem(
                detail: exception.Message,
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

/// <summary>
/// Builds the set of tool types to register: every <c>[McpServerToolType]</c> read tool, plus —
/// only when <c>GRAMPS_ALLOW_WRITES</c> is true — whichever write tools the service account's real
/// JWT permission ceiling actually grants (see <see cref="GrampsWriteTools"/>). An unregistered
/// tool cannot be discovered via <c>list_tools</c> or invoked at all, which is the point: the
/// model should never be able to plan around a write it isn't allowed to make. When writes are
/// disabled (the default) this returns immediately without touching the network.
/// </summary>
/// <remarks>
/// Deliberately typed <see cref="IEnumerable{Type}"/>, not <c>IReadOnlyList</c>: passing this to
/// the SDK's <c>WithTools</c> needs its static type to be exactly <c>IEnumerable&lt;Type&gt;</c>
/// so overload resolution ties between the intended <c>WithTools(IEnumerable&lt;Type&gt;, ...)</c>
/// overload and the generic <c>WithTools&lt;TToolType&gt;(TToolType target, ...)</c> single-target
/// overload, which the "prefer non-generic on a tie" rule then resolves correctly. A more specific
/// static type (e.g. <c>IReadOnlyList&lt;Type&gt;</c>) gives the generic overload an exact-match
/// identity conversion the non-generic overload can't match, so it wins outright instead — and
/// silently scans the list instance itself for <c>[McpServerTool]</c> methods, registering none.
/// </remarks>
static async Task<IEnumerable<Type>> ResolveToolTypesAsync(IServiceCollection services, IConfiguration configuration)
{
    // Excluded by the [GrampsWriteTool] marker attribute, not by membership in GrampsWriteTools.All:
    // a write tool that is forgotten from that list must still fail closed (stay unregistered), not
    // leak through here as an ungated read tool.
    var readToolTypes = typeof(Program).Assembly.GetTypes()
        .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
        .Where(type => type.GetCustomAttribute<GrampsWriteToolAttribute>() is null)
        .ToList();

    if (!configuration.GetValue("GRAMPS_ALLOW_WRITES", false))
    {
        return readToolTypes;
    }

    // ASP0000: this is a one-off startup check, not the request-pipeline antipattern the rule
    // warns about — it runs once before the real host is built, to learn the service account's
    // real JWT permission ceiling before deciding which write tools to register.
    //
    // The provider is deliberately left undisposed on the success path below: it owns the
    // GrampsTokenStore singleton that just authenticated with Gramps Web, and disposing the
    // provider here would tear that instance (and its semaphore) down before the real host gets a
    // chance to reuse it. Instead, the already-authenticated instance is swapped into `services`
    // so the real host's provider resolves the SAME GrampsTokenStore rather than constructing a
    // fresh one and authenticating a second time from scratch.
#pragma warning disable ASP0000
    var provider = services.BuildServiceProvider();
#pragma warning restore ASP0000

    try
    {
        var tokenStore = provider.GetRequiredService<GrampsTokenStore>();
        var permissions = await provider.GetRequiredService<GrampsPermissionsProvider>()
            .GetPermissionsAsync(CancellationToken.None);

        var missingPermissions = GrampsWriteTools.All
            .SelectMany(tool => tool.RequiredPermissions)
            .Distinct()
            .Where(permission => !permissions.Contains(permission))
            .ToList();

        if (missingPermissions.Count > 0)
        {
            Log.WritePermissionsIncomplete(
                provider.GetRequiredService<ILogger<Program>>(), string.Join(", ", missingPermissions));
        }

        services.Replace(ServiceDescriptor.Singleton(tokenStore));

        return [.. readToolTypes, .. GrampsWriteTools.SelectRegistrable(permissions)];
    }
    catch (Exception exception) when (exception is GrampsApiException
        or HttpRequestException
        or Polly.Timeout.TimeoutRejectedException
        or Polly.CircuitBreaker.BrokenCircuitException
        or FormatException
        or JsonException)
    {
        // A transient Gramps outage, or a JWT that doesn't decode as expected (FormatException from
        // GrampsJwt's base64/segment check, JsonException from its payload parse), at startup must
        // not prevent the server from coming up at all — fall back to read-only tools for this run
        // rather than crashing the process.
        Log.WritePermissionCheckFailed(provider.GetRequiredService<ILogger<Program>>(), exception);
        await provider.DisposeAsync();
        return readToolTypes;
    }
}

/// <summary>Exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
public partial class Program;
