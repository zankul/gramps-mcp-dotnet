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
    public void AddGrampsClient_WithApiUrlOverride_BindsTheOverride()
    {
        var settings = new Dictionary<string, string?>(RequiredSettings) { ["GRAMPS_API_URL"] = "https://other.example.com" };

        var options = Resolve(settings);

        Assert.Equal("https://other.example.com", options.ApiUrl);
    }

    [Fact]
    public void AddGrampsClient_WithUsernameOverride_BindsTheOverride()
    {
        var settings = new Dictionary<string, string?>(RequiredSettings) { ["GRAMPS_USERNAME"] = "someone-else" };

        var options = Resolve(settings);

        Assert.Equal("someone-else", options.Username);
    }

    [Fact]
    public void AddGrampsClient_WithPasswordOverride_BindsTheOverride()
    {
        var settings = new Dictionary<string, string?>(RequiredSettings) { ["GRAMPS_PASSWORD"] = "hunter2" };

        var options = Resolve(settings);

        Assert.Equal("hunter2", options.Password);
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
