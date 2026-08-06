using GrampsMcp.Server.Gramps;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsObjectTypesTests
{
    [Theory]
    [InlineData("person", "/api/people/")]
    [InlineData("PERSON", "/api/people/")]
    [InlineData("family", "/api/families/")]
    [InlineData("event", "/api/events/")]
    [InlineData("place", "/api/places/")]
    [InlineData("citation", "/api/citations/")]
    [InlineData("source", "/api/sources/")]
    [InlineData("repository", "/api/repositories/")]
    [InlineData("media", "/api/media/")]
    [InlineData("note", "/api/notes/")]
    [InlineData("tag", "/api/tags/")]
    public void ResolveCollectionPath_MapsKnownTypes(string type, string expectedPath)
    {
        Assert.Equal(expectedPath, GrampsObjectTypes.ResolveCollectionPath(type));
    }

    [Fact]
    public void ResolveCollectionPath_OnUnknownType_ThrowsWithValidTypesListed()
    {
        var exception = Assert.Throws<ArgumentException>(() => GrampsObjectTypes.ResolveCollectionPath("people"));

        Assert.Contains("people", exception.Message);
        Assert.Contains("person", exception.Message);
    }
}
