using System.Text.Json;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsJwtTests
{
    [Fact]
    public void ReadPermissions_ReturnsPermissionsClaim()
    {
        string[] grantedPermissions = ["AddObject", "EditObject", "ViewPrivate"];
        var jwt = CreateJwt(new { permissions = grantedPermissions });

        var permissions = GrampsJwt.ReadPermissions(jwt);

        Assert.Equal(["AddObject", "EditObject", "ViewPrivate"], permissions.OrderBy(p => p));
    }

    [Fact]
    public void ReadPermissions_WhenClaimMissing_ReturnsEmptySet()
    {
        var jwt = CreateJwt(new { sub = "owner" });

        var permissions = GrampsJwt.ReadPermissions(jwt);

        Assert.Empty(permissions);
    }

    [Fact]
    public void DecodePayload_WhenNotThreeSegments_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => GrampsJwt.DecodePayload("not-a-jwt"));
    }

    [Fact]
    public void DecodePayload_WhenThreeSegmentsButPayloadUndecodable_ThrowsFormatOrJsonException()
    {
        // The actual trigger for GrampsToolTypeResolver's read-only fallback isn't the wrong-segment
        // -count case above (a malformed JWT still has three dot-separated segments) — it's this one:
        // three segments where the middle one isn't valid base64url/JSON. Both exception types this
        // can throw are catch-filter members in GrampsToolTypeResolver.cs, so either is an acceptable
        // outcome here; what matters is that decoding doesn't succeed or throw something uncaught.
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "none", typ = "JWT" }));
        const string undecodablePayload = "not-valid-base64url!!!";

        var exception = Record.Exception(() => GrampsJwt.DecodePayload($"{header}.{undecodablePayload}.signature"));

        Assert.True(
            exception is FormatException or JsonException,
            $"expected FormatException or JsonException, got {exception?.GetType().ToString() ?? "no exception"}");
    }

    private static string CreateJwt(object payload)
    {
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "none", typ = "JWT" }));
        var body = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{body}.signature";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
