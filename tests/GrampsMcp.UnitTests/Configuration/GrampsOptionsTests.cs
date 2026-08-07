using System.ComponentModel.DataAnnotations;
using GrampsMcp.Server.Configuration;

namespace GrampsMcp.UnitTests.Configuration;

public class GrampsOptionsTests
{
    [Theory]
    [InlineData("None")]
    [InlineData("critical")]
    [InlineData("Error")]
    [InlineData("WARNING")]
    [InlineData("Information")]
    [InlineData("Debug")]
    [InlineData("Trace")]
    public void Validate_AcceptsKnownLogLevelNames(string logLevel)
    {
        var options = CreateValidOptions();
        options.LogLevel = logLevel;

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_WhenLogLevelUnset_Accepts()
    {
        var options = CreateValidOptions();
        options.LogLevel = null;

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("verbose")]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("0")]
    public void Validate_RejectsInvalidLogLevelNames(string logLevel)
    {
        var options = CreateValidOptions();
        options.LogLevel = logLevel;

        var results = Validate(options);

        var result = Assert.Single(results);
        Assert.Contains(nameof(GrampsOptions.LogLevel), result.MemberNames);
    }

    private static GrampsOptions CreateValidOptions() => new()
    {
        ApiUrl = "https://example.com",
        Username = "owner",
        Password = "owner",
    };

    private static List<ValidationResult> Validate(GrampsOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
