namespace GrampsMcp.LiveTests.LiveStack;

/// <summary>
/// Readiness of the live Gramps Web stack, as determined by <see cref="LiveStackProbe"/>. Every
/// live test must check this via <see cref="LiveStackGate.Require"/> before doing anything else.
/// </summary>
public enum LiveStackState
{
    /// <summary>Reachable, authenticated, and seeded — safe to run live tests.</summary>
    Ready,

    /// <summary>The base URL did not respond at all (connection refused, DNS failure, timeout).</summary>
    StackUnreachable,

    /// <summary>The stack responded, but <c>POST /api/token/</c> failed with the configured credentials.</summary>
    AuthFailed,

    /// <summary>Authenticated, but the tree has no data — the seed import never ran or was reset.</summary>
    TreeNotSeeded,

    /// <summary>Authenticated, but <c>/api/metadata/</c> indicates the import started and has not finished.</summary>
    ImportIncomplete,
}
