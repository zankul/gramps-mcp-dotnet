using System.Text.Json.Nodes;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsJsonMergePatchTests
{
    [Fact]
    public void Apply_OverwritesScalarField()
    {
        var target = JsonNode.Parse("""{"gender":1,"gramps_id":"I0001"}""");
        var patch = JsonNode.Parse("""{"gender":0}""");

        var result = GrampsJsonMergePatch.Apply(target, patch);

        Assert.Equal(0, (int)result!["gender"]!);
        Assert.Equal("I0001", (string)result["gramps_id"]!);
    }

    [Fact]
    public void Apply_NullFieldRemovesIt()
    {
        var target = JsonNode.Parse("""{"note":"old note","handle":"h1"}""");
        var patch = JsonNode.Parse("""{"note":null}""");

        var result = GrampsJsonMergePatch.Apply(target, patch);

        Assert.False(result!.AsObject().ContainsKey("note"));
        Assert.Equal("h1", (string)result["handle"]!);
    }

    [Fact]
    public void Apply_MergesNestedObjectsRecursively()
    {
        var target = JsonNode.Parse("""{"primary_name":{"first_name":"Jane","surname_list":["Doe"]}}""");
        var patch = JsonNode.Parse("""{"primary_name":{"first_name":"Janet"}}""");

        var result = GrampsJsonMergePatch.Apply(target, patch);

        var name = result!["primary_name"]!;
        Assert.Equal("Janet", (string)name["first_name"]!);
        Assert.Single(name["surname_list"]!.AsArray());
    }

    [Fact]
    public void Apply_ArrayInPatchReplacesArrayWholesale()
    {
        var target = JsonNode.Parse("""{"tag_list":["a","b","c"]}""");
        var patch = JsonNode.Parse("""{"tag_list":["z"]}""");

        var result = GrampsJsonMergePatch.Apply(target, patch);

        var tags = result!["tag_list"]!.AsArray();
        Assert.Single(tags);
        Assert.Equal("z", (string)tags[0]!);
    }

    [Fact]
    public void Apply_AddsFieldNotPresentOnTarget()
    {
        var target = JsonNode.Parse("""{"handle":"h1"}""");
        var patch = JsonNode.Parse("""{"privacy":true}""");

        var result = GrampsJsonMergePatch.Apply(target, patch);

        Assert.True((bool)result!["privacy"]!);
    }

    [Fact]
    public void Apply_DoesNotMutateOriginalTarget()
    {
        var target = JsonNode.Parse("""{"gender":1}""");
        var patch = JsonNode.Parse("""{"gender":0}""");

        GrampsJsonMergePatch.Apply(target, patch);

        Assert.Equal(1, (int)target!["gender"]!);
    }
}
