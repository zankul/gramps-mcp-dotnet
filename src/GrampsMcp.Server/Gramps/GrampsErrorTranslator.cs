using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Turns a failed Gramps Web API response into a <see cref="GrampsApiException"/>. Upstream
/// error text — notably GQL 422 parse errors, which name the exact character and column — is
/// passed through verbatim rather than replaced with a generic message, since the model can
/// self-correct a malformed query from that detail. Endpoint-specific error rewrites (e.g.
/// <see cref="GrampsApiClient.SearchAsync"/>'s handling of the unconfigured-semantic-search 503)
/// belong at the call site, not here — this type has no notion of which endpoint it's translating
/// for.
/// </summary>
internal static partial class GrampsErrorTranslator
{
    private static readonly string[] ErrorFieldNames = ["error", "message", "detail"];

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new GrampsApiException(response.StatusCode, ExtractMessage(body, response));
    }

    private static string ExtractMessage(string body, HttpResponseMessage response)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return response.ReasonPhrase ?? $"Gramps API request failed with status {(int)response.StatusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var extracted = ExtractFromObject(document.RootElement);
            if (extracted is not null)
            {
                return extracted;
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through and surface the raw body.
        }

        return LooksLikeHtml(body) ? HtmlErrorMessage(body, response) : body;
    }

    private static bool LooksLikeHtml(string body)
    {
        var trimmed = body.AsSpan().TrimStart('\uFEFF').TrimStart();
        return trimmed.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private const int HtmlExcerptLength = 200;

    [GeneratedRegex(@"(?:<[^>]*>|\s)+")]
    private static partial Regex TagsAndWhitespace();

    /// <summary>An HTML error page (typically a bare 500 from gunicorn or a proxy) is noise to the
    /// model, so replace it with a short summary plus a text-only excerpt. Only a plain 500 gets
    /// the database-lock hint — 502/503/504 from a proxy are not lock symptoms.</summary>
    private static string HtmlErrorMessage(string body, HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var text = TagsAndWhitespace().Replace(body, " ").Trim();
        var excerpt = text.Length > HtmlExcerptLength ? text[..HtmlExcerptLength] + "…" : text;
        var detail = excerpt.Length > 0 ? $" Page text: \"{excerpt}\"" : string.Empty;

        return response.StatusCode == HttpStatusCode.InternalServerError
            ? $"Gramps Web returned an internal error (HTTP {status}) with an HTML error page. If writes " +
              "keep failing while reads work, the Gramps Web database may be locked (e.g. SQLite after " +
              $"overlapping writes) and need a restart.{detail}"
            : $"Gramps Web or a proxy in front of it returned an HTML error page (HTTP {status}) " +
              $"instead of a JSON error.{detail}";
    }

    /// <summary>Looks for a top-level string error field, or Gramps Web's nested
    /// <c>{"error": {"code": ..., "message": "..."}}</c> shape.</summary>
    private static string? ExtractFromObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var fieldName in ErrorFieldNames)
        {
            if (!root.TryGetProperty(fieldName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("message", out var nestedMessage) &&
                nestedMessage.ValueKind == JsonValueKind.String)
            {
                return nestedMessage.GetString();
            }
        }

        return null;
    }
}
