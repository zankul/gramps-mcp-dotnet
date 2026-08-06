using System.Text.Json;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Turns a failed Gramps Web API response into a <see cref="GrampsApiException"/>. Upstream
/// error text — notably GQL 422 parse errors, which name the exact character and column — is
/// passed through verbatim rather than replaced with a generic message.
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
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var fieldName in ErrorFieldNames)
                {
                    if (document.RootElement.TryGetProperty(fieldName, out var value) &&
                        value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString()!;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through and surface the raw body.
        }

        return body;
    }
}
