using System.Net.Http.Json;
using System.Reflection;
using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace GrampsMcp.Server.Tools;

/// <summary>
/// Builds the set of tool types <c>Program.cs</c> registers: every <c>[McpServerToolType]</c> read
/// tool, plus — only when <c>GRAMPS_ALLOW_WRITES</c> is true — whichever write tools the service
/// account's real JWT permission ceiling actually grants (see <see cref="GrampsWriteTools"/>). An
/// unregistered tool cannot be discovered via <c>list_tools</c> or invoked at all, which is the
/// point: the model should never be able to plan around a write it isn't allowed to make. When
/// writes are disabled (the default) this returns immediately without touching the network.
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
internal static class GrampsToolTypeResolver
{
    public static Task<IEnumerable<Type>> ResolveAsync(IServiceCollection services, IConfiguration configuration) =>
        ResolveAsync(services, configuration, authHandler: null);

    /// <param name="authHandler">
    /// Test seam only: substitutes the transport <see cref="CheckPermissionsAsync"/>'s throwaway
    /// <see cref="HttpClient"/> sends through, so tests can stub the startup permission check
    /// without a real network call. Production callers always use the parameterless overload
    /// above, which leaves this <see langword="null"/> (the client's real <c>SocketsHttpHandler</c>).
    /// </param>
    internal static async Task<IEnumerable<Type>> ResolveAsync(
        IServiceCollection services, IConfiguration configuration, HttpMessageHandler? authHandler)
    {
        // Excluded by the [GrampsWriteTool] marker attribute, not by membership in GrampsWriteTools.All:
        // a write tool that is forgotten from that list must still fail closed (stay unregistered), not
        // leak through here as an ungated read tool.
        var readToolTypes = typeof(GrampsToolTypeResolver).Assembly.GetTypes()
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
        // Deliberately used ONLY to resolve already-validated GrampsOptions and a logger — never
        // GrampsTokenStore or IHttpClientFactory. Resolving either of those here would force this
        // throwaway container to build real HTTP infrastructure that then has to either be
        // disposed (tearing down a token store the real host might still be using) or leaked for
        // the life of the process (the bug this fixed: see #84). The permission check below
        // instead talks to Gramps Web with its own one-off HttpClient built straight from the
        // validated options, so this provider can always be disposed right after use.
#pragma warning disable ASP0000
        await using var provider = services.BuildServiceProvider();
#pragma warning restore ASP0000

        var options = provider.GetRequiredService<IOptions<GrampsOptions>>().Value;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(GrampsToolTypeResolver).FullName!);

        try
        {
            var (permissions, tokens) = await CheckPermissionsAsync(options, authHandler, CancellationToken.None);

            // Seeds the real host's GrampsTokenStore (registered later, in Program.cs, once this
            // method returns) with the token pair already obtained above, so it starts
            // pre-authenticated instead of logging in a second time on its first real request.
            services.AddSingleton(new GrampsTokenSeed(tokens.AccessToken, tokens.RefreshToken));

            var missingPermissions = GrampsWriteTools.All
                .SelectMany(tool => tool.RequiredPermissions)
                .Distinct()
                .Where(permission => !permissions.Contains(permission))
                .ToList();

            if (missingPermissions.Count > 0)
            {
                Log.WritePermissionsIncomplete(logger, string.Join(", ", missingPermissions));
            }

            return [.. readToolTypes, .. GrampsWriteTools.SelectRegistrable(permissions)];
        }
        catch (Exception exception) when (exception is GrampsApiException
            or HttpRequestException
            or Polly.Timeout.TimeoutRejectedException
            or Polly.CircuitBreaker.BrokenCircuitException
            or FormatException
            or System.Text.Json.JsonException
            or OperationCanceledException)
        {
            // A transient Gramps outage, an unreachable host (OperationCanceledException from the
            // HttpClient timeout below — there is no resilience pipeline on this one-off client to
            // translate it into TimeoutRejectedException the way the typed GrampsApiClient would),
            // or a JWT that doesn't decode as expected (FormatException from GrampsJwt's
            // base64/segment check, JsonException from its payload parse), at startup must not
            // prevent the server from coming up at all — fall back to read-only tools for this run
            // rather than crashing the process.
            Log.WritePermissionCheckFailed(logger, exception);
            return readToolTypes;
        }
    }

    /// <summary>
    /// Authenticates once with a throwaway <see cref="HttpClient"/> (deliberately not routed
    /// through <see cref="GrampsTokenStore"/> or DI's <c>IHttpClientFactory</c> — see the comment
    /// in <see cref="ResolveAsync"/>) and reads the JWT's <c>permissions</c> claim. The client gets
    /// the same explicit timeout as the real <c>GrampsAuth</c> named client, since it likewise has
    /// no resilience handler to bound an unreachable host's hang. Also returns the token pair
    /// obtained along the way, so <see cref="ResolveAsync"/> can seed the real
    /// <see cref="GrampsTokenStore"/> and avoid a second, redundant login moments later.
    /// </summary>
    private static async Task<(IReadOnlySet<string> Permissions, GrampsTokenResponse Tokens)> CheckPermissionsAsync(
        GrampsOptions options, HttpMessageHandler? authHandler, CancellationToken cancellationToken)
    {
        using var client = authHandler is null ? new HttpClient() : new HttpClient(authHandler, disposeHandler: false);
        client.BaseAddress = new Uri(GrampsServiceCollectionExtensions.NormalizeBaseAddress(options.ApiUrl));
        client.Timeout = TimeSpan.FromSeconds(10);

        using var response = await client.PostAsJsonAsync(
            "api/token/", new { username = options.Username, password = options.Password }, cancellationToken);
        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<GrampsTokenResponse>(cancellationToken)
            ?? throw new GrampsApiException(response.StatusCode, "Gramps token endpoint returned an empty body.");

        return (GrampsJwt.ReadPermissions(payload.AccessToken), payload);
    }
}
