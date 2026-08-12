using System.ComponentModel.DataAnnotations;
using GrampsMcp.Server.Configuration;

namespace GrampsMcp.UnitTests.Configuration;

public class OriginValidationOptionsTests
{
    [Fact]
    public void Validate_WithDefaultOrigins_Accepts()
    {
        var options = new OriginValidationOptions();

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("*.example.com")]
    [InlineData("192.168.0.0/24")]
    [InlineData("localhost:8080")]
    [InlineData("http://localhost:8080/api")]
    [InlineData("http://localhost:8080?query=1")]
    [InlineData("ftp://localhost:8080")]
    [InlineData("not a uri at all")]
    public void Validate_WithMalformedOrigin_Rejects(string origin)
    {
        var options = new OriginValidationOptions { AllowedOrigins = [origin] };

        var results = Validate(options);

        var result = Assert.Single(results);
        Assert.Contains(nameof(OriginValidationOptions.AllowedOrigins), result.MemberNames);
        Assert.Contains(origin, result.ErrorMessage);
    }

    [Fact]
    public void Validate_WithWildcardAlongsideRealOrigins_StillRejects()
    {
        var options = new OriginValidationOptions
        {
            AllowedOrigins = ["http://localhost:8080", "*"],
        };

        var results = Validate(options);

        Assert.Single(results);
    }

    [Theory]
    [InlineData("https://gramps.home.lan")]
    [InlineData("http://192.168.1.50:8080")]
    [InlineData("http://[::1]:8080")]
    public void Validate_WithWellFormedOrigin_Accepts(string origin)
    {
        var options = new OriginValidationOptions { AllowedOrigins = [origin] };

        var results = Validate(options);

        Assert.Empty(results);
    }

    private static List<ValidationResult> Validate(OriginValidationOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
