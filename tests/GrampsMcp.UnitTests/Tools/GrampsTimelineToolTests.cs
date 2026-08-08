using System.Net;
using System.Text;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;
using ModelContextProtocol;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsTimelineToolTests
{
    [Fact]
    public async Task TimelineAsync_ForPerson_HitsPeopleTimelineEndpoint()
    {
        var handler = new TestHttpMessageHandler(_ => TestResponses.JsonList("""[{"type":"Birth"}]""", 1));
        var tool = new GrampsTimelineTool(GrampsTestClientFactory.CreateClient(handler));

        var result = await tool.TimelineAsync("person", "handle-a", cancellationToken: CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal("/api/people/handle-a/timeline", request.RequestUri!.AbsolutePath);
        Assert.Equal(1, result.Total);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task TimelineAsync_ForFamily_HitsFamiliesTimelineEndpoint()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsTimelineTool(GrampsTestClientFactory.CreateClient(handler));

        await tool.TimelineAsync("family", "handle-f", cancellationToken: CancellationToken.None);

        Assert.Equal("/api/families/handle-f/timeline", handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task TimelineAsync_DefaultsToPageOne_EvenWhenUnspecified()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsTimelineTool(GrampsTestClientFactory.CreateClient(handler));

        await tool.TimelineAsync("person", "handle-a", cancellationToken: CancellationToken.None);

        var query = handler.Requests.Single().RequestUri!.Query;
        Assert.Contains("page=1", query);
    }

    [Fact]
    public async Task TimelineAsync_ForFamily_OmitsPersonOnlyAncestorsAndOffspring()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsTimelineTool(GrampsTestClientFactory.CreateClient(handler));

        await tool.TimelineAsync("family", "handle-f", ancestors: 3, offspring: 2, cancellationToken: CancellationToken.None);

        var query = handler.Requests.Single().RequestUri!.Query;
        Assert.DoesNotContain("ancestors", query);
        Assert.DoesNotContain("offspring", query);
    }

    [Fact]
    public async Task TimelineAsync_ForPerson_PassesThroughAncestorsAndOffspring()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        });
        var tool = new GrampsTimelineTool(GrampsTestClientFactory.CreateClient(handler));

        await tool.TimelineAsync("person", "handle-a", ancestors: 3, offspring: 2, cancellationToken: CancellationToken.None);

        var query = handler.Requests.Single().RequestUri!.Query;
        Assert.Contains("ancestors=3", query);
        Assert.Contains("offspring=2", query);
    }

    [Fact]
    public async Task TimelineAsync_WithGrampsId_ResolvesHandleFirst()
    {
        var handler = new TestHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/people/")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""[{"handle":"resolved-handle"}]""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });
        var tool = new GrampsTimelineTool(GrampsTestClientFactory.CreateClient(handler));

        await tool.TimelineAsync("person", "I0155", cancellationToken: CancellationToken.None);

        Assert.Equal("/api/people/resolved-handle/timeline", handler.Requests.Last().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task TimelineAsync_OnUnknownType_ThrowsBeforeMakingAnyRequest()
    {
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var tool = new GrampsTimelineTool(GrampsTestClientFactory.CreateClient(handler));

        await Assert.ThrowsAsync<McpException>(
            () => tool.TimelineAsync("human", "I0155", cancellationToken: CancellationToken.None));
    }
}
