using GrampsMcp.Server.Gramps;

namespace GrampsMcp.LiveTests.Writes;

/// <summary>
/// Per-test layer of the three-layer cleanup write tests rely on (the other two are
/// <see cref="ZzTestSweep"/>'s opening/closing passes): tracks every object a test creates and
/// best-effort deletes them on disposal. <c>await using</c> this inside a <c>try</c>/an
/// implicit <c>finally</c> so cleanup runs even when an assertion throws mid-test.
/// </summary>
internal sealed class TestObjectScope(GrampsApiClient apiClient) : IAsyncDisposable
{
    private readonly List<(string Type, string Handle)> _created = [];

    /// <summary>Registers an object for cleanup. Safe to call even if the test later deletes the
    /// object itself (directly, via merge-discard, or via a successful undo) — the matching
    /// delete on disposal then just 404s, which is swallowed below.</summary>
    public void Track(string type, string handle) => _created.Add((type, handle));

    public async ValueTask DisposeAsync()
    {
        // Reverse creation order on general principle (undo anything that referenced an earlier
        // object first), though nothing here actually creates cross-references.
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            var (type, handle) = _created[i];

            try
            {
                var collectionPath = GrampsObjectTypes.ResolveCollectionPath(type);
                await apiClient.DeleteAsync($"{collectionPath}{handle}", CancellationToken.None);
            }
            catch
            {
                // Best-effort and never throws: a cleanup failure here (already deleted by the
                // test itself, absorbed by a merge, restored/removed by an undo, or a genuine
                // transient error) must never mask the real assertion failure the test's own
                // "await using" is protecting.
            }
        }
    }
}
