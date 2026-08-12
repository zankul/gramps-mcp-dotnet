using System.Text.Json;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.LiveTests.Writes;

/// <summary>
/// One GQL prefix query per sweepable type (see <see cref="ZzTestNamespace.SweepableTypes"/>),
/// deleting every object it finds in the reserved ZZTEST namespace. Two of the three cleanup
/// layers the write tests rely on call this: <c>WriteRoundTripTests</c>' opening sweep (catches
/// leftovers from a run that was killed before its own <c>TestObjectScope</c> could run) and its
/// closing sweep inside <c>ExampleTreeUnchanged_AfterWriteSuite</c> (the last line of defense
/// before that test's fingerprint check). The third layer, per-test <c>TestObjectScope</c>
/// disposal, is what should make this sweep normally find nothing.
/// </summary>
internal static class ZzTestSweep
{
    /// <summary>Deletes every object in the reserved ZZTEST namespace across every sweepable
    /// type. Best-effort per object — one delete failure must not abort the rest of the sweep or
    /// mask whatever the caller is really trying to verify.</summary>
    /// <returns>How many objects were found and removed.</returns>
    public static async Task<int> RunAsync(GrampsApiClient apiClient, CancellationToken cancellationToken)
    {
        var deleted = 0;

        foreach (var type in ZzTestNamespace.SweepableTypes)
        {
            var collectionPath = GrampsObjectTypes.ResolveCollectionPath(type);
            var gql = $"gramps_id >= \"{ZzTestNamespace.LowerBound}\" and gramps_id < \"{ZzTestNamespace.UpperBound}\"";
            var request = new GrampsGqlRequest { Gql = gql, PageSize = 100, Keys = ["handle"] };

            var result = await apiClient.QueryAsync<JsonElement>(collectionPath, request, cancellationToken);

            foreach (var item in result.Items)
            {
                var handle = item.GetProperty("handle").GetString();
                if (string.IsNullOrEmpty(handle))
                {
                    continue;
                }

                try
                {
                    await apiClient.DeleteAsync($"{collectionPath}{handle}", cancellationToken);
                    deleted++;
                }
                catch
                {
                    // Best-effort: this sweep exists to catch leftovers, not to be a strict
                    // assertion in its own right — a single stubborn object must not stop the
                    // sweep from clearing everything else, or mask the caller's real assertion.
                }
            }
        }

        return deleted;
    }
}
