using System.Text.Json;

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
internal static class GrampsErrorTranslator
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

        return LooksLikeHtml(body) ? HtmlErrorMessage(response) : body;
    }

    private static bool LooksLikeHtml(string body)
    {
        var trimmed = body.AsSpan().TrimStart();
        return trimmed.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An HTML error page (typically a bare 500 from gunicorn or a proxy) tells the model
    /// nothing actionable, so replace it with a short summary rather than passing it through.</summary>
    private static string HtmlErrorMessage(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        return response.StatusCode >= System.Net.HttpStatusCode.InternalServerError
            ? $"Gramps Web returned an internal error (HTTP {status}) with an HTML error page. If writes " +
              "keep failing while reads work, the Gramps Web database may be locked (e.g. SQLite after " +
              "overlapping writes) and need a restart."
            : $"Gramps Web returned an HTML error page (HTTP {status}) instead of a JSON error.";
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
