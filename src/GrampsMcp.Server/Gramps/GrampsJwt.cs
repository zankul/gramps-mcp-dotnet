using System.Text.Json;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Reads claims out of a Gramps-issued JWT without validating its signature — the token came
/// straight from Gramps over TLS, so the only thing we need from it is the payload.
/// </summary>
internal static class GrampsJwt
{
    public static JsonElement DecodePayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            throw new FormatException("Not a JWT: expected header.payload.signature.");
        }

        var payloadBytes = Base64UrlDecode(parts[1]);
        return JsonSerializer.Deserialize<JsonElement>(payloadBytes);
    }

    public static IReadOnlySet<string> ReadPermissions(string jwt)
    {
        var payload = DecodePayload(jwt);
        if (!payload.TryGetProperty("permissions", out var permissions) || permissions.ValueKind != JsonValueKind.Array)
        {
            return new HashSet<string>();
        }

        return permissions
            .EnumerateArray()
            .Select(p => p.GetString())
            .Where(p => p is not null)
            .Select(p => p!)
            .ToHashSet();
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
