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

    /// <summary>
    /// Upper bound is deliberately finite, not <see cref="int.MaxValue"/>: this value is both the
    /// clamp ceiling <see cref="GrampsQueryBuilder"/> enforces on every <c>pagesize</c> and the
    /// default it falls back to when a caller omits one (<c>GrampsQueryBuilder.cs</c>), so an
    /// unbounded range would let an operator turn the forced-pagination guardrail into a no-op via
    /// <c>GRAMPS_MAX_PAGESIZE</c>. 1000 is a round order-of-magnitude above the documented default
    /// (100) — <c>docs/PLAN.md</c>'s measured API facts don't name a hard upstream ceiling, but do
    /// note collections in the thousands of rows (e.g. a 4,663-person tree returned whole when
    /// pagination was bypassed), so this keeps a single page comfortably below "the whole database"
    /// while still giving operators real headroom over the default.
    /// </summary>
    [Range(1, 1000)]
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Default <c>pagesize</c> for <c>/api/search/</c> when a caller omits it. Deliberately
    /// smaller than <see cref="MaxPageSize"/>: search is the tool the model is told to reach for
    /// by default, and each hit carries the full raw object (no server-side trimming is possible —
    /// <c>/api/search/</c> silently ignores <c>profile</c>/<c>keys</c>/<c>skipkeys</c>), so
    /// defaulting to <see cref="MaxPageSize"/> would flood the model's context on the common case.
    /// Callers can still request up to <see cref="MaxPageSize"/> explicitly.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int SearchDefaultPageSize { get; set; } = 20;

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

        if (SearchDefaultPageSize > MaxPageSize)
        {
            yield return new ValidationResult(
                $"SearchDefaultPageSize ({SearchDefaultPageSize}) cannot exceed MaxPageSize ({MaxPageSize}).",
                [nameof(SearchDefaultPageSize)]);
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
