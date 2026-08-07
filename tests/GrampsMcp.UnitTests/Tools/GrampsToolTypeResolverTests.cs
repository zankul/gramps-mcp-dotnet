using System.Net;
using System.Text;
using System.Text.Json;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
        var services = BuildServices(allowWrites: false, tokenStore: null);

        var toolTypes = (await GrampsToolTypeResolver.ResolveAsync(services, BuildConfiguration(allowWrites: false))).ToList();

        Assert.Contains(typeof(GrampsGetTool), toolTypes);
        foreach (var (writeToolType, _) in GrampsWriteTools.All)
        {
            Assert.DoesNotContain(writeToolType, toolTypes);
        }
    }

    [Fact]
    public async Task ResolveAsync_WhenWritesEnabledWithFullPermissions_IncludesEveryWriteTool()
    {
        var tokenStore = CreateTokenStoreWithPermissions("AddObject", "EditObject", "DeleteObject");
        var services = BuildServices(allowWrites: true, tokenStore);

        var toolTypes = (await GrampsToolTypeResolver.ResolveAsync(services, BuildConfiguration(allowWrites: true))).ToList();

        Assert.Contains(typeof(GrampsGetTool), toolTypes);
        foreach (var (writeToolType, _) in GrampsWriteTools.All)
        {
            Assert.Contains(writeToolType, toolTypes);
        }
    }

    [Fact]
    public async Task ResolveAsync_WhenWritesEnabledWithPartialPermissions_RegistersOnlyGrantedWriteTools()
    {
        var tokenStore = CreateTokenStoreWithPermissions("AddObject");
        var services = BuildServices(allowWrites: true, tokenStore);

        var toolTypes = (await GrampsToolTypeResolver.ResolveAsync(services, BuildConfiguration(allowWrites: true))).ToList();

        Assert.Contains(typeof(GrampsCreateTool), toolTypes);
        Assert.DoesNotContain(typeof(GrampsUpdateTool), toolTypes);
        Assert.DoesNotContain(typeof(GrampsDeleteTool), toolTypes);
        Assert.DoesNotContain(typeof(GrampsMergeTool), toolTypes);
    }

    [Fact]
    public async Task ResolveAsync_WhenPermissionCheckFailsTransiently_FallsBackToReadOnly()
    {
        // The auth backend answers every request with a 503, so reading the JWT permission
        // ceiling throws GrampsApiException — the exact kind of transient outage this fallback
        // exists for.
        var authBackend = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var tokenStore = CreateTokenStore(authBackend);
        var services = BuildServices(allowWrites: true, tokenStore);

        var toolTypes = (await GrampsToolTypeResolver.ResolveAsync(services, BuildConfiguration(allowWrites: true))).ToList();

        Assert.Contains(typeof(GrampsGetTool), toolTypes);
        foreach (var (writeToolType, _) in GrampsWriteTools.All)
        {
            Assert.DoesNotContain(writeToolType, toolTypes);
        }
    }

    private static ServiceCollection BuildServices(bool allowWrites, GrampsTokenStore? tokenStore)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (allowWrites)
        {
            tokenStore ??= CreateTokenStoreWithPermissions();
            services.AddSingleton(tokenStore);
            services.AddSingleton(new GrampsPermissionsProvider(tokenStore));
        }

        return services;
    }

    private static IConfiguration BuildConfiguration(bool allowWrites) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GRAMPS_ALLOW_WRITES"] = allowWrites ? "true" : "false",
            })
            .Build();

    private static GrampsTokenStore CreateTokenStoreWithPermissions(params string[] permissions)
    {
        var jwt = CreateJwt(new { permissions });
        var authBackend = new TestHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.OK, $$"""{"access_token":"{{jwt}}","refresh_token":"refresh1"}"""));
        return CreateTokenStore(authBackend);
    }

    private static GrampsTokenStore CreateTokenStore(HttpMessageHandler authBackend)
    {
        var options = Options.Create(new GrampsOptions
        {
            ApiUrl = "https://tree.example.com",
            Username = "owner",
            Password = "owner",
        });
        var factory = new FakeHttpClientFactory(authBackend);
        return new GrampsTokenStore(factory, options, NullLogger<GrampsTokenStore>.Instance);
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

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://tree.example.com") };
    }
}
