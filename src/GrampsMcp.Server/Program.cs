using System.Text.Json;
using GrampsMcp.Server.Gramps;

var transport = Environment.GetEnvironmentVariable("MCP_TRANSPORT") ?? "http";

if (string.Equals(transport, "stdio", StringComparison.OrdinalIgnoreCase))
{
    await RunStdioAsync();
}
else
{
    RunHttp(args);
}

static async Task RunStdioAsync()
{
    var builder = Host.CreateApplicationBuilder();

    // stdout is the MCP protocol channel under stdio; all logging must go to stderr instead.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services.AddGrampsClient();
    builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();

    var host = builder.Build();
    await host.RunAsync();
}

static void RunHttp(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddGrampsClient();
    builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

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

    app.Run();
}

/// <summary>Exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
public partial class Program;
