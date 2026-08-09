using System.Text.Json;
using GrampsMcp.Server.Gramps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrampsMcp.LiveTests.Demo;

/// <summary>
/// Authenticates against the public demo.grampsweb.org tree with its published read-only
/// credentials (owner/owner). Opt-in via the "Demo" trait (excluded from a bare `dotnet test` by
/// `tests/default.runsettings`) rather than a runtime early-return — a prior version of this test
/// returned silently when GRAMPS_RUN_DEMO_SMOKE_TEST wasn't set, reporting green while actually
/// skipping. That pattern is exactly what this project's tiering exists to eliminate.
/// </summary>
[Trait("Tier", "Demo")]
public class GrampsDemoSmokeTests
{
    [Fact]
    public async Task Authenticate_AgainstDemoTree_ListsPeopleAndReadsPermissions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GRAMPS_API_URL"] = "https://demo.grampsweb.org",
                ["GRAMPS_USERNAME"] = "owner",
                ["GRAMPS_PASSWORD"] = "owner",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddGrampsClient();
        await using var provider = services.BuildServiceProvider();

        var apiClient = provider.GetRequiredService<GrampsApiClient>();
        var people = await apiClient.ListAsync<JsonElement>(
            "/api/people/", new GrampsListRequest { PageSize = 1 }, CancellationToken.None);
        Assert.True(people.Total > 0);

        var permissionsProvider = provider.GetRequiredService<GrampsPermissionsProvider>();
        var permissions = await permissionsProvider.GetPermissionsAsync(CancellationToken.None);
        Assert.NotEmpty(permissions);
    }
}
