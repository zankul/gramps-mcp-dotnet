using GrampsMcp.Server.Gramps;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsObjectTypesTests
{
    [Theory]
    [InlineData("person", "api/people/")]
    [InlineData("PERSON", "api/people/")]
    [InlineData("family", "api/families/")]
    [InlineData("event", "api/events/")]
    [InlineData("place", "api/places/")]
    [InlineData("citation", "api/citations/")]
    [InlineData("source", "api/sources/")]
    [InlineData("repository", "api/repositories/")]
    [InlineData("media", "api/media/")]
    [InlineData("note", "api/notes/")]
    [InlineData("tag", "api/tags/")]
    public void ResolveCollectionPath_MapsKnownTypes(string type, string expectedPath)
    {
        // Deliberately relative, no leading slash (see #84): GrampsApiClient composes this
        // against HttpClient.BaseAddress, and a leading slash would resolve path-absolute,
        // discarding a sub-path already present in that base address.
        Assert.Equal(expectedPath, GrampsObjectTypes.ResolveCollectionPath(type));
    }

    [Fact]
    public void ResolveCollectionPath_OnUnknownType_ThrowsWithValidTypesListed()
    {
        var exception = Assert.Throws<ArgumentException>(() => GrampsObjectTypes.ResolveCollectionPath("people"));

        Assert.Contains("people", exception.Message);
        Assert.Contains("person", exception.Message);
    }

    [Theory]
    [InlineData("api/events/", "event")]
    [InlineData("/api/events/", "event")]
    [InlineData("api/events/104d3c7223d4", "event")]
    [InlineData("api/people/?gramps_id=I0155", "person")]
    [InlineData("api/families/", "family")]
    [InlineData("api/media/", "media")]
    public void FromPath_RecoversTypeFromCollectionAndObjectPaths(string path, string expectedType)
    {
        Assert.Equal(expectedType, GrampsObjectTypes.FromPath(path));
    }

    [Theory]
    [InlineData("api/search/")]
    [InlineData("api/transactions/history/")]
    [InlineData("people/")]
    [InlineData("")]
    public void FromPath_OnNonObjectPath_ReturnsNull(string path)
    {
        Assert.Null(GrampsObjectTypes.FromPath(path));
    }
}
