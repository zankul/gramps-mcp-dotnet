namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Reads our real permission ceiling — the <c>permissions</c> claim on the current Gramps JWT —
/// so callers can check what the service account is actually allowed to do before attempting it.
/// </summary>
public sealed class GrampsPermissionsProvider(GrampsTokenStore tokenStore)
{
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken cancellationToken)
    {
        var accessToken = await tokenStore.GetAccessTokenAsync(cancellationToken);
        return GrampsJwt.ReadPermissions(accessToken);
    }
}
