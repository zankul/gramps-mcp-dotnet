using System.Net;
using System.Text;
using System.Text.Json;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GrampsMcp.UnitTests.Tools;

/// <summary>
/// Covers the fail-closed write-tool gate CLAUDE.md calls out as safety-critical: writes must stay
/// unregistered unless GRAMPS_ALLOW_WRITES is set AND the service account's real JWT permission
/// ceiling grants them, and a startup failure reading that ceiling must fall back to read-only
/// rather than crash or (worse) register writes anyway.
/// </summary>
public class GrampsToolTypeResolverTests
{
    [Fact]
    public async Task ResolveAsync_WhenWritesDisabled_ExcludesEveryWriteTool()
    {
        var services = BuildServices();

        var toolTypes = (await GrampsToolTypeResolver.ResolveAsync(
            services, BuildConfiguration(allowWrites: false), authHandler: null)).ToList();

        Assert.Contains(typeof(GrampsGetTool), toolTypes);
        foreach (var (writeToolType, _) in GrampsWriteTools.All)
        {
            Assert.DoesNotContain(writeToolType, toolTypes);
        }
    }

    [Fact]
    public async Task ResolveAsync_WhenWritesEnabledWithFullPermissions_IncludesEveryWriteTool()
    {
        var services = BuildServices();
        var authHandler = CreateAuthHandler("AddObject", "EditObject", "DeleteObject", "ViewPrivate");

        var toolTypes = (await GrampsToolTypeResolver.ResolveAsync(
            services, BuildConfiguration(allowWrites: true), authHandler)).ToList();

        Assert.Contains(typeof(GrampsGetTool), toolTypes);
        foreach (var (writeToolType, _) in GrampsWriteTools.All)
        {
            Assert.Contains(writeToolType, toolTypes);
        }
    }

    [Fact]
    public async Task ResolveAsync_WhenWritesEnabledWithPartialPermissions_RegistersOnlyGrantedWriteTools()
    {
        var services = BuildServices();
        var authHandler = CreateAuthHandler("AddObject");

        var toolTypes = (await GrampsToolTypeResolver.ResolveAsync(
            services, BuildConfiguration(allowWrites: true), authHandler)).ToList();

        Assert.Contains(typeof(GrampsCreateTool), toolTypes);
        Assert.DoesNotContain(typeof(GrampsUpdateTool), toolTypes);
        Assert.DoesNotContain(typeof(GrampsDeleteTool), toolTypes);
        Assert.DoesNotContain(typeof(GrampsMergeTool), toolTypes);
    }

    [Fact]
    public async Task ResolveAsync_WhenWritesEnabled_SeedsRealTokenStoreToAvoidASecondLogin()
    {
        // Regression test for the "logs in twice at startup" finding from the #84 review: the
        // permission check already has a valid access/refresh token pair in hand by the time it
        // succeeds, so it should hand that pair to the real host (via a GrampsTokenSeed added to
        // `services`) rather than discarding it and letting GrampsTokenStore log in again on its
        // first real request.
        var services = BuildServices();
        var authHandler = CreateAuthHandler("AddObject");

        await GrampsToolTypeResolver.ResolveAsync(services, BuildConfiguration(allowWrites: true), authHandler);

        var seed = services.BuildServiceProvider().GetRequiredService<GrampsTokenSeed>();
        Assert.Equal("refresh1", seed.RefreshToken);
        Assert.False(string.IsNullOrEmpty(seed.AccessToken));
    }

    [Fact]
    public async Task ResolveAsync_WhenPermissionCheckFailsTransiently_FallsBackToReadOnly()
    {
        // The auth backend answers every request with a 503, so reading the JWT permission
        // ceiling throws GrampsApiException — the exact kind of transient outage this fallback
        // exists for.
        var services = BuildServices();
        var authHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var toolTypes = (await GrampsToolTypeResolver.ResolveAsync(
            services, BuildConfiguration(allowWrites: true), authHandler)).ToList();

        Assert.Contains(typeof(GrampsGetTool), toolTypes);
        foreach (var (writeToolType, _) in GrampsWriteTools.All)
        {
            Assert.DoesNotContain(writeToolType, toolTypes);
        }
    }

    [Fact]
    public async Task ResolveAsync_WhenAuthClientHangsAndTimesOut_FallsBackToReadOnlyInsteadOfThrowing()
    {
        // Regression test for #84 finding 1: the permission check runs over a plain HttpClient
        // with no resilience handler, so an unreachable host surfaces as
        // TaskCanceledException/OperationCanceledException (HttpClient's own Timeout), not one of
        // the Polly exception types. Before the fix, that type wasn't in ResolveAsync's catch
        // filter and crashed startup instead of degrading to read-only tools. The handler below
        // throws that exact exception type to stand in for a real 10s timeout without the test
        // actually waiting that long.
        var services = BuildServices();
        var authHandler = new TestHttpMessageHandler((_, cancellationToken) =>
            throw new TaskCanceledException("Simulated HttpClient.Timeout expiry.", null, cancellationToken));

        var toolTypes = (await GrampsToolTypeResolver.ResolveAsync(
            services, BuildConfiguration(allowWrites: true), authHandler)).ToList();

        Assert.Contains(typeof(GrampsGetTool), toolTypes);
        foreach (var (writeToolType, _) in GrampsWriteTools.All)
        {
            Assert.DoesNotContain(writeToolType, toolTypes);
        }
    }

    private static ServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // GrampsToolTypeResolver only ever resolves IOptions<GrampsOptions> and ILoggerFactory
        // from the temporary provider it builds internally — never GrampsTokenStore or
        // IHttpClientFactory (see #84 finding 4) — so registering just the options here is
        // enough; there is deliberately no GrampsTokenStore/GrampsPermissionsProvider/HttpClient
        // registration for these tests to fake.
        services.AddSingleton<IOptions<GrampsOptions>>(Options.Create(new GrampsOptions
        {
            ApiUrl = "https://tree.example.com",
            Username = "owner",
            Password = "owner",
        }));

        return services;
    }

    private static IConfiguration BuildConfiguration(bool allowWrites) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GRAMPS_ALLOW_WRITES"] = allowWrites ? "true" : "false",
            })
            .Build();

    /// <summary>Fakes <c>POST api/token/</c> answering with a JWT carrying the given permissions,
    /// the only endpoint <see cref="GrampsToolTypeResolver"/>'s startup permission check calls.</summary>
    private static TestHttpMessageHandler CreateAuthHandler(params string[] permissions)
    {
        var jwt = CreateJwt(new { permissions });
        return new TestHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.OK, $$"""{"access_token":"{{jwt}}","refresh_token":"refresh1"}"""));
    }

    private static string CreateJwt(object payload)
    {
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "none", typ = "JWT" }));
        var body = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{body}.signature";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
