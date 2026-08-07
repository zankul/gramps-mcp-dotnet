using System.Reflection;
using GrampsMcp.Server.Gramps;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public static async Task<IEnumerable<Type>> ResolveAsync(IServiceCollection services, IConfiguration configuration)
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
        // The provider is deliberately left undisposed on the success path below: it owns the
        // GrampsTokenStore singleton that just authenticated with Gramps Web, and disposing the
        // provider here would tear that instance (and its semaphore) down before the real host gets a
        // chance to reuse it. Instead, the already-authenticated instance is swapped into `services`
        // so the real host's provider resolves the SAME GrampsTokenStore rather than constructing a
        // fresh one and authenticating a second time from scratch.
#pragma warning disable ASP0000
        var provider = services.BuildServiceProvider();
#pragma warning restore ASP0000

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(GrampsToolTypeResolver).FullName!);

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
                Log.WritePermissionsIncomplete(logger, string.Join(", ", missingPermissions));
            }

            services.Replace(ServiceDescriptor.Singleton(tokenStore));

            return [.. readToolTypes, .. GrampsWriteTools.SelectRegistrable(permissions)];
        }
        catch (Exception exception) when (exception is GrampsApiException
            or HttpRequestException
            or Polly.Timeout.TimeoutRejectedException
            or Polly.CircuitBreaker.BrokenCircuitException
            or FormatException
            or System.Text.Json.JsonException)
        {
            // A transient Gramps outage, or a JWT that doesn't decode as expected (FormatException from
            // GrampsJwt's base64/segment check, JsonException from its payload parse), at startup must
            // not prevent the server from coming up at all — fall back to read-only tools for this run
            // rather than crashing the process.
            Log.WritePermissionCheckFailed(logger, exception);
            await provider.DisposeAsync();
            return readToolTypes;
        }
    }
}
