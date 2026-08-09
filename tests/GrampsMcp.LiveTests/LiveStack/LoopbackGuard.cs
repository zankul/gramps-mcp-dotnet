namespace GrampsMcp.LiveTests.LiveStack;

/// <summary>
/// Makes <c>docs/PLAN.md</c>'s "never write to a non-local instance" rule mechanical rather than
/// remembered. No write tests exist yet (that's step 10), but the guard lands now so every write
/// test added later starts with this check rather than reinventing it.
/// </summary>
internal static class LoopbackGuard
{
    /// <summary>Throws unless <paramref name="baseUrl"/> is loopback or a private-network host.
    /// Call this before any test that creates, updates, deletes, merges, or undoes data.</summary>
    public static void RequireLocal(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || !IsLocal(uri.Host))
        {
            throw new InvalidOperationException(
                $"Refusing to run write tests against {baseUrl}: docs/PLAN.md forbids writes to any " +
                "non-local instance.");
        }
    }

    private static bool IsLocal(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!System.Net.IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (System.Net.IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // RFC 1918 private ranges: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16.
            return bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168);
        }

        // Unique local IPv6 addresses (RFC 4193): fc00::/7.
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
            (bytes[0] & 0xfe) == 0xfc;
    }
}
