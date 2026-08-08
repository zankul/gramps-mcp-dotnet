using System.Net;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsHandleResolverTests
{
    [Fact]
    public async Task ResolveAsync_WithGrampsId_ResolvesToHandle()
    {
        var handler = new TestHttpMessageHandler(_ => TestResponses.Json(
            HttpStatusCode.OK, """[{"handle":"resolved-handle"}]"""));
        var client = GrampsTestClientFactory.CreateClient(handler);

        var handle = await GrampsHandleResolver.ResolveAsync(client, "person", "I0155", CancellationToken.None);

        Assert.Equal("resolved-handle", handle);
        Assert.Contains("gramps_id=I0155", handler.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task ResolveAsync_WithGrampsIdNotFound_ThrowsNamingTheId()
    {
        var handler = new TestHttpMessageHandler(_ => TestResponses.Json(HttpStatusCode.OK, "[]"));
        var client = GrampsTestClientFactory.CreateClient(handler);

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => GrampsHandleResolver.ResolveAsync(client, "person", "I9999999", CancellationToken.None));

        Assert.Contains("I9999999", exception.Message);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task ResolveAsync_WithSyntacticallyInvalidHandle_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var client = GrampsTestClientFactory.CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => GrampsHandleResolver.ResolveAsync(client, "person", "../../etc/passwd", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_WithValidHandle_PassesThroughUnchanged()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var client = GrampsTestClientFactory.CreateClient(handler);

        var handle = await GrampsHandleResolver.ResolveAsync(
            client, "person", "102adba515546de1ad51712ea3fd", CancellationToken.None);

        Assert.Equal("102adba515546de1ad51712ea3fd", handle);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ResolveAsync_WithUnknownType_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var client = GrampsTestClientFactory.CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => GrampsHandleResolver.ResolveAsync(client, "human", "I0155", CancellationToken.None));
    }
}
