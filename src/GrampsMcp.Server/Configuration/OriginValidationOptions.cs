using System.ComponentModel.DataAnnotations;

namespace GrampsMcp.Server.Configuration;

/// <summary>
/// Configures the HTTP transport's Origin allowlist — this server's actual defense against DNS
/// rebinding, since it deliberately ships no built-in auth scheme (see <c>SECURITY.md</c>). A
/// request with an <c>Origin</c> header not on this list is rejected; a request with no
/// <c>Origin</c> header at all is allowed through, since many MCP clients don't send one.
/// </summary>
public sealed class OriginValidationOptions : IValidatableObject
{
    /// <summary>
    /// Origins allowed to talk to the HTTP transport. Defaults cover the loopback addresses
    /// <c>docker-compose.yml</c> binds to; override via <c>MCP_ALLOWED_ORIGINS</c> (comma-separated)
    /// if the server is reached through a reverse proxy on a different origin.
    /// </summary>
    public HashSet<string> AllowedOrigins { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://localhost:8080",
        "http://127.0.0.1:8080",
    };

    /// <summary>
    /// Rejects any entry that isn't a well-formed <c>scheme://host[:port]</c> origin — covers
    /// <c>*</c>, subdomain globs (<c>*.example.com</c>), CIDR ranges (<c>192.168.0.0/24</c>), and
    /// plain typos alike. None of these can ever equal a real browser <c>Origin</c> header, so
    /// left unchecked they'd silently become dead entries: the request-time comparison is an exact
    /// string match, so a malformed entry just never matches anything and every legitimate request
    /// with a present <c>Origin</c> header gets rejected. Origin validation is this server's only
    /// defense against DNS rebinding, so fail loudly at startup instead of failing silently at
    /// request time.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var origin in AllowedOrigins)
        {
            if (!IsWellFormedOrigin(origin))
            {
                yield return new ValidationResult(
                    $"MCP_ALLOWED_ORIGINS entry '{origin}' is not a valid origin. An Origin header " +
                    "is always exactly scheme://host[:port] — no wildcards, subdomain globs, CIDR " +
                    "ranges, paths, or query strings are matched. List each concrete origin allowed " +
                    "to reach the server.",
                    [nameof(AllowedOrigins)]);
            }
        }
    }

    private static bool IsWellFormedOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.AbsolutePath == "/"
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0;
}
