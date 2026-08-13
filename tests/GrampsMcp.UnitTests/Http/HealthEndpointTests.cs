using System.Net;
using System.Net.Http.Json;
using GrampsMcp.Server;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace GrampsMcp.UnitTests.Http;

/// <summary>
/// Pins <c>Program.cs</c>'s <c>/health</c> failure branch (lines ~69-86), which the Live-tier
/// <c>Protocol/HealthEndpointTests.cs</c> cannot exercise — it only ever sees the happy path
/// against a real Docker stack. Each test replaces the DI-registered <see cref="GrampsApiClient"/>
/// with one wired to an <see cref="HttpMessageHandler"/> that throws a specific exception,
/// bypassing <see cref="GrampsAuthHandler"/> and the resilience pipeline entirely (neither is
/// under test here — only whether <c>/health</c>'s catch filter still maps that exception type to
/// a 503, and whether the response body still withholds the raw exception text). See #65.
/// </summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string UpstreamSecret = "nginx 502 from gramps-internal.local:8080 — traceback at /srv/gramps/app.py line 42";

    private readonly WebApplicationFactory<Program> _baseFactory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GRAMPS_API_URL"] = "http://gramps.invalid",
                    ["GRAMPS_USERNAME"] = "test-user",
                    ["GRAMPS_PASSWORD"] = "test-password",
                    ["GRAMPS_ALLOW_WRITES"] = "false",
                });
            });
        });
    }

    [Fact]
    public async Task Health_WhenGrampsApiExceptionThrown_Returns503WithoutUpstreamBody()
    {
        using var response = await GetHealthAsync(new GrampsApiException(HttpStatusCode.BadGateway, UpstreamSecret));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertBodyDoesNotLeakUpstreamTextAsync(response);
    }

    [Fact]
    public async Task Health_WhenHttpRequestExceptionThrown_Returns503WithoutUpstreamBody()
    {
        using var response = await GetHealthAsync(new HttpRequestException(UpstreamSecret));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertBodyDoesNotLeakUpstreamTextAsync(response);
    }

    [Fact]
    public async Task Health_WhenTimeoutRejectedExceptionThrown_Returns503WithoutUpstreamBody()
    {
        using var response = await GetHealthAsync(new TimeoutRejectedException(UpstreamSecret));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertBodyDoesNotLeakUpstreamTextAsync(response);
    }

    [Fact]
    public async Task Health_WhenBrokenCircuitExceptionThrown_Returns503WithoutUpstreamBody()
    {
        using var response = await GetHealthAsync(new BrokenCircuitException(UpstreamSecret));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertBodyDoesNotLeakUpstreamTextAsync(response);
    }

    [Fact]
    public async Task Health_WhenUpstreamSucceeds_Returns200()
    {
        using var response = await GetHealthAsync(throwException: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetHealthAsync(Exception? throwException)
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddTransient(sp => new GrampsApiClient(
                    new HttpClient(new ThrowingHandler(throwException)) { BaseAddress = new Uri("http://gramps.invalid") },
                    sp.GetRequiredService<IOptions<GrampsOptions>>()));
            });
        });

        using var client = factory.CreateClient();
        return await client.GetAsync("/health");
    }

    private static async Task AssertBodyDoesNotLeakUpstreamTextAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(UpstreamSecret, body);
        Assert.DoesNotContain("gramps-internal.local", body);
    }

    /// <summary>Stands in for the innermost transport handler: either throws the configured
    /// exception, or (when null) returns a minimal successful <c>/api/metadata/</c> response.</summary>
    private sealed class ThrowingHandler(Exception? exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { }),
            });
        }
    }
}
