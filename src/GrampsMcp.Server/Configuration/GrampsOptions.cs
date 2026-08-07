using System.ComponentModel.DataAnnotations;

namespace GrampsMcp.Server.Configuration;

public sealed class GrampsOptions : IValidatableObject
{
    [Required(AllowEmptyStrings = false)]
    [Url]
    public string ApiUrl { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Username { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Password { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int MaxPageSize { get; set; } = 100;

    [Required(AllowEmptyStrings = false)]
    public string DefaultProfile { get; set; } = "self,families,events";

    /// <summary>
    /// Raw <c>MCP_LOG_LEVEL</c> value. Null means "unset" — each transport keeps its current
    /// default (stdio: Trace to stderr; HTTP: whatever <c>appsettings.json</c> says).
    /// </summary>
    public string? LogLevel { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (LogLevel is not null && !TryParseLogLevel(LogLevel, out _))
        {
            yield return new ValidationResult(
                $"MCP_LOG_LEVEL '{LogLevel}' is not a valid log level. Expected one of: None, " +
                "Critical, Error, Warning, Information, Debug, Trace.",
                [nameof(LogLevel)]);
        }
    }

    /// <summary>
    /// Parses a log level name, rejecting values that only parse because
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> also accepts raw numeric input.
    /// </summary>
    public static bool TryParseLogLevel(string value, out Microsoft.Extensions.Logging.LogLevel level)
    {
        if (int.TryParse(value, out _))
        {
            level = default;
            return false;
        }

        return Enum.TryParse(value, ignoreCase: true, out level) && Enum.IsDefined(level);
    }
}
