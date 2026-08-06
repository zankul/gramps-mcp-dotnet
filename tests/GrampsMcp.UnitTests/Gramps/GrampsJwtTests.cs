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

    private static string CreateJwt(object payload)
    {
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "none", typ = "JWT" }));
        var body = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{body}.signature";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
