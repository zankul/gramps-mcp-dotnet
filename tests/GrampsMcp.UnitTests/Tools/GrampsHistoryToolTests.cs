using System.Net;
using System.Text;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using GrampsMcp.UnitTests.Gramps;

namespace GrampsMcp.UnitTests.Tools;

public class GrampsHistoryToolTests
{
    [Fact]
    public async Task ListAsync_HitsHistoryEndpointWithDefaultDescendingSort()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"id":2},{"id":1}]""", Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("x-total-count", "2");
            return response;
        });
        var tool = new GrampsHistoryTool(CreateClient(handler));

        var result = await tool.ListAsync(cancellationToken: CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal("/api/transactions/history/", request.RequestUri!.AbsolutePath);
        Assert.Contains("sort=-id", request.RequestUri.Query);
        Assert.Equal(2, result.Total);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler) =>
        GrampsTestClientFactory.CreateClient(handler);
}
