using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsServiceCollectionExtensionsTests
{
    private static readonly Dictionary<string, string?> RequiredSettings = new()
    {
        ["GRAMPS_API_URL"] = "https://tree.example.com",
        ["GRAMPS_USERNAME"] = "owner",
        ["GRAMPS_PASSWORD"] = "owner",
    };

    [Fact]
    public void AddGrampsClient_WithOnlyRequiredSettings_BindsDocumentedDefaults()
    {
        var options = Resolve(RequiredSettings);

        Assert.Equal("https://tree.example.com", options.ApiUrl);
        Assert.Equal("owner", options.Username);
        Assert.Equal("owner", options.Password);
        Assert.Equal(100, options.MaxPageSize);
        Assert.Equal("self,families,events", options.DefaultProfile);
    }

    [Fact]
    public void AddGrampsClient_WithMaxPageSizeOverride_BindsTheOverride()
    {
        var settings = new Dictionary<string, string?>(RequiredSettings) { ["GRAMPS_MAX_PAGESIZE"] = "25" };

        var options = Resolve(settings);

        Assert.Equal(25, options.MaxPageSize);
    }

    [Fact]
    public void AddGrampsClient_WithMaxPageSizeOverLimit_ThrowsOnResolve()
    {
        var settings = new Dictionary<string, string?>(RequiredSettings) { ["GRAMPS_MAX_PAGESIZE"] = "100000" };

        Assert.Throws<OptionsValidationException>(() => Resolve(settings));
    }

    [Fact]
    public void AddGrampsClient_WithDefaultProfileOverride_BindsTheOverride()
    {
        var settings = new Dictionary<string, string?>(RequiredSettings) { ["GRAMPS_DEFAULT_PROFILE"] = "none" };

        var options = Resolve(settings);

        Assert.Equal("none", options.DefaultProfile);
    }

    [Fact]
    public void AddGrampsClient_WhenApiUrlMissing_ThrowsOnResolve()
    {
        var settings = new Dictionary<string, string?>(RequiredSettings);
        settings.Remove("GRAMPS_API_URL");

        Assert.Throws<OptionsValidationException>(() => Resolve(settings));
    }

    [Fact]
    public void AddGrampsClient_WhenApiUrlIsNotAUrl_ThrowsOnResolve()
    {
        var settings = new Dictionary<string, string?>(RequiredSettings) { ["GRAMPS_API_URL"] = "not-a-url" };

        Assert.Throws<OptionsValidationException>(() => Resolve(settings));
    }

    [Fact]
    public void AddGrampsClient_WhenUsernameMissing_ThrowsOnResolve()
    {
        var settings = new Dictionary<string, string?>(RequiredSettings);
        settings.Remove("GRAMPS_USERNAME");

        Assert.Throws<OptionsValidationException>(() => Resolve(settings));
    }

    [Fact]
    public void AddGrampsClient_WhenPasswordMissing_ThrowsOnResolve()
    {
        var settings = new Dictionary<string, string?>(RequiredSettings);
        settings.Remove("GRAMPS_PASSWORD");

        Assert.Throws<OptionsValidationException>(() => Resolve(settings));
    }

    [Theory]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com/", "https://example.com/")]
    [InlineData("https://example.com/gramps", "https://example.com/gramps/")]
    [InlineData("https://example.com/gramps/", "https://example.com/gramps/")]
    public void NormalizeBaseAddress_AlwaysEndsInTrailingSlash(string apiUrl, string expected)
    {
        // Regression guard for #84 finding 2: GrampsApiClient/GrampsTokenStore both build request
        // URIs as paths relative to HttpClient.BaseAddress, which per Uri combination rules only
        // appends correctly onto a base ending in "/" — a sub-path base URL missing the trailing
        // slash would otherwise silently drop its last path segment when combined with those
        // relative request paths.
        Assert.Equal(expected, GrampsServiceCollectionExtensions.NormalizeBaseAddress(apiUrl));
    }

    [Theory]
    [InlineData("https://example.com/gramps")]
    [InlineData("https://example.com/gramps/")]
    public void AddGrampsClient_WithSubPathApiUrl_GivesDataAndAuthClientsTheSameNormalizedBaseAddress(string apiUrl)
    {
        // Both the typed GrampsApiClient and the GrampsAuth named client go through the same
        // ConfigureBaseAddress, so a sub-path deployment (behind a path-prefixing proxy) must
        // resolve both to the same origin+subpath regardless of whether the operator remembered
        // the trailing slash on GRAMPS_API_URL.
        var settings = new Dictionary<string, string?>(RequiredSettings) { ["GRAMPS_API_URL"] = apiUrl };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddGrampsClient();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var authClient = factory.CreateClient(GrampsTokenStore.AuthClientName);
        var dataClient = factory.CreateClient(nameof(GrampsApiClient));

        Assert.Equal(new Uri("https://example.com/gramps/"), authClient.BaseAddress);
        Assert.Equal(new Uri("https://example.com/gramps/"), dataClient.BaseAddress);
    }

    [Fact]
    public void AddGrampsClient_GrampsAuthClient_HasAnExplicitFiniteTimeout()
    {
        // Regression guard for #84 finding 1: this client has no resilience handler (only the
        // typed GrampsApiClient below gets AddStandardResilienceHandler), so without an explicit
        // Timeout an unreachable host would hang for HttpClient's 100s default before the startup
        // permission probe in GrampsToolTypeResolver could fall back to read-only tools.
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(RequiredSettings).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddGrampsClient();

        using var provider = services.BuildServiceProvider();
        var authClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(GrampsTokenStore.AuthClientName);

        Assert.True(authClient.Timeout < TimeSpan.FromSeconds(100));
    }

    private static GrampsOptions Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddGrampsClient();

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<GrampsOptions>>().Value;
    }
}
