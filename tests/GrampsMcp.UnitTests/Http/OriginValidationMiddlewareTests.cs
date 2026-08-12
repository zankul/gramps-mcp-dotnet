using System.Net;
using GrampsMcp.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace GrampsMcp.UnitTests.Http;

/// <summary>
/// Exercises the Origin-allowlist middleware registered in <c>Program.cs</c> ahead of
/// <c>app.MapMcp()</c> — this server's actual defense against DNS rebinding, since the HTTP
/// transport ships with no built-in auth scheme (see <c>SECURITY.md</c>). Requests target a path
/// that no endpoint maps, so a pass means "reached routing" (404) and a block means "rejected by
/// the middleware before routing" (403) — deliberately avoiding <c>/health</c>, which would call
/// out to a Gramps backend this test has no interest in reaching.
/// </summary>
public class OriginValidationMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OriginValidationMiddlewareTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
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
    public async Task SendAsync_WithAllowedOrigin_IsNotRejected()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/no-such-route");
        request.Headers.Add("Origin", "http://localhost:8080");

        using var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WithDisallowedOrigin_IsRejected()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/no-such-route");
        request.Headers.Add("Origin", "https://evil.example.com");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WithoutOriginHeader_IsNotRejected()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/no-such-route");

        using var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
