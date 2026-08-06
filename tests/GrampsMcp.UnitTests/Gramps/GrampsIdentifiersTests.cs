using GrampsMcp.Server.Gramps;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsIdentifiersTests
{
    [Theory]
    [InlineData("I0155")]
    [InlineData("F1964")]
    [InlineData("E16236")]
    [InlineData("O0037")]
    [InlineData("i0001")]
    public void LooksLikeGrampsId_OnGrampsIdShapedStrings_ReturnsTrue(string idOrHandle)
    {
        Assert.True(GrampsIdentifiers.LooksLikeGrampsId(idOrHandle));
    }

    [Theory]
    [InlineData("102adba515546de1ad51712ea3fd")]
    [InlineData("083ff1c0-0c5a-408a-8937-ffb743a39e3c")]
    [InlineData("")]
    [InlineData("0155")]
    public void LooksLikeGrampsId_OnHandleShapedStrings_ReturnsFalse(string idOrHandle)
    {
        Assert.False(GrampsIdentifiers.LooksLikeGrampsId(idOrHandle));
    }

    [Theory]
    [InlineData("102adba515546de1ad51712ea3fd")]
    [InlineData("083ff1c0-0c5a-408a-8937-ffb743a39e3c")]
    [InlineData("I0155")]
    public void IsValidHandle_OnLegitimateIdsAndHandles_ReturnsTrue(string idOrHandle)
    {
        Assert.True(GrampsIdentifiers.IsValidHandle(idOrHandle));
    }

    [Theory]
    [InlineData("../../users/")]
    [InlineData("..%2f..%2fusers")]
    [InlineData("foo/bar")]
    [InlineData("foo.bar")]
    [InlineData("")]
    public void IsValidHandle_OnPathTraversalOrDelimiterCharacters_ReturnsFalse(string idOrHandle)
    {
        Assert.False(GrampsIdentifiers.IsValidHandle(idOrHandle));
    }
}
