using System.Text.Json;
using GrampsMcp.LiveTests.LiveStack;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.LiveTests;

/// <summary>
/// The one trivial live test this step ships: proves the whole plumbing (probe, auth, DI graph,
/// gate) works end to end against a real stack. Contract/protocol/write tests are later steps.
/// </summary>
[Trait("Tier", "Live")]
[Collection(LiveGrampsCollection.Name)]
public class MetadataTests(LiveGrampsFixture fixture)
{
    [Fact]
    public async Task Metadata_IsReachable()
    {
        LiveStackGate.Require(fixture);

        var metadata = await fixture.ApiClient.GetAsync<JsonElement>("/api/metadata/", CancellationToken.None);

        Assert.True(metadata.TryGetProperty("object_counts", out var counts));
        Assert.True(counts.TryGetProperty("people", out var people));
        Assert.True(people.GetInt32() > 0);
    }
}
