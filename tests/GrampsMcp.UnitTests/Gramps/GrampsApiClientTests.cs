using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsApiClientTests
{
    [Fact]
    public async Task ListAsync_ReadsTotalCountFromHeader()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"handle":"h1","gramps_id":"I0001"}]""", Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("x-total-count", "4663");
            return response;
        });
        var client = CreateClient(handler);

        var result = await client.ListAsync<TestPerson>("/api/people/", new GrampsListRequest(), CancellationToken.None);

        Assert.Equal(4663, result.Total);
        Assert.Single(result.Items);
        Assert.Equal("h1", result.Items[0].Handle);
        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
    }

    [Fact]
    public async Task ListAsync_WhenTotalCountHeaderMissing_FallsBackToItemCount()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """[{"handle":"h1","gramps_id":"I0001"},{"handle":"h2","gramps_id":"I0002"}]""",
                Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        var result = await client.ListAsync<TestPerson>("/api/people/", new GrampsListRequest(), CancellationToken.None);

        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task CountAsync_ReadsTotalCountFromHeader()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"handle":"h1"}]""", Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("x-total-count", "4663");
            return response;
        });
        var client = CreateClient(handler);

        var total = await client.CountAsync("/api/people/", "gender == 1", CancellationToken.None);

        Assert.Equal(4663, total);
    }

    [Fact]
    public async Task CountAsync_WhenTotalCountHeaderMissing_ThrowsRatherThanReportingPageSize()
    {
        // With pagesize=1, an items.Count fallback would always read 1 regardless of the real
        // match count — a plausible-looking wrong answer. CountAsync must refuse instead.
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"handle":"h1"}]""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => client.CountAsync("/api/people/", "gender == 1", CancellationToken.None));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Contains("x-total-count", exception.Message);
    }

    [Fact]
    public async Task GetAsync_ReturnsDeserializedObject()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"handle":"h1","gramps_id":"I0001"}""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        var person = await client.GetAsync<TestPerson>("/api/people/I0001", CancellationToken.None);

        Assert.Equal("I0001", person.GrampsId);
    }

    [Fact]
    public async Task GetAsync_OnErrorStatus_ThrowsGrampsApiException()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error": "not found"}"""),
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => client.GetAsync<TestPerson>("/api/people/I9999", CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("not found", exception.Message);
    }

    [Fact]
    public async Task GetAsync_WithRequest_OmitsPageAndPagesizeFromQuery()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"handle":"h1","gramps_id":"I0001"}""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        await client.GetAsync<TestPerson>(
            "/api/people/h1", new GrampsGetRequest { Extend = ["family_list"] }, CancellationToken.None);

        var query = handler.Requests.Single().RequestUri!.Query;
        Assert.Contains("extend=family_list", query);
        Assert.DoesNotContain("page=", query);
        Assert.DoesNotContain("pagesize=", query);
    }

    [Fact]
    public async Task FindByGrampsIdAsync_OnMatch_ReturnsFirstItem()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"handle":"h1","gramps_id":"I0155"}]""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        var found = await client.FindByGrampsIdAsync<TestPerson>(
            "/api/people/", "I0155", new GrampsGetRequest(), CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("I0155", found.GrampsId);
        Assert.Contains("gramps_id=I0155", handler.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task FindByGrampsIdAsync_OnNotFound_ReturnsDefaultRatherThanThrowing()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"code":404,"status":"Not Found"}"""),
        });
        var client = CreateClient(handler);

        var found = await client.FindByGrampsIdAsync<TestPerson>(
            "/api/people/", "I9999999", new GrampsGetRequest(), CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task SearchAsync_BuildsQueryAndTypeAndSemanticParameters()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"handle":"h1","object_type":"person","object":{},"rank":0,"score":1.5}]""",
                    Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("x-total-count", "1");
            return response;
        });
        var client = CreateClient(handler);

        var result = await client.SearchAsync(
            new GrampsSearchRequest { Query = "Sutherland", Types = ["person", "family"], Semantic = true },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("person", result.Items[0].ObjectType);
        var query = handler.Requests.Single().RequestUri!.Query;
        Assert.Contains("query=Sutherland", query);
        Assert.Contains("type=person,family", query);
        Assert.Contains("semantic=true", query);
        Assert.DoesNotContain("profile=", query);
    }

    [Fact]
    public async Task SearchAsync_WhenPageSizeOmitted_DefaultsToSearchDefaultNotMaxPageSize()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("x-total-count", "0");
            return response;
        });
        var client = CreateClient(handler);

        await client.SearchAsync(new GrampsSearchRequest { Query = "Sutherland" }, CancellationToken.None);

        var query = handler.Requests.Single().RequestUri!.Query;
        Assert.Contains($"pagesize={GrampsTestClientFactory.Options.SearchDefaultPageSize}", query);
        Assert.DoesNotContain($"pagesize={GrampsTestClientFactory.Options.MaxPageSize}", query);
    }

    [Fact]
    public async Task SearchAsync_OnUnconfiguredSemanticSearch503_ThrowsActionableMessage()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                """{"error": {"code": 503, "message": "VECTOR_EMBEDDING_MODEL option not set"}}"""),
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => client.SearchAsync(
                new GrampsSearchRequest { Query = "Sutherland", Semantic = true }, CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.DoesNotContain("option not set", exception.Message);
        Assert.Contains("semantic=false", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_OnUnrelated503_PassesMessageThroughUnchanged()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"error": {"code": 503, "message": "database unavailable"}}"""),
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => client.SearchAsync(
                new GrampsSearchRequest { Query = "Sutherland", Semantic = true }, CancellationToken.None));

        Assert.Equal("database unavailable", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_OnEmbeddingModelMarker503WithoutSemanticRequested_PassesMessageThroughUnchanged()
    {
        // Guards against the rewrite firing on a coincidental substring match when the caller
        // never asked for semantic search in the first place.
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                """{"error": {"code": 503, "message": "VECTOR_EMBEDDING_MODEL option not set"}}"""),
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<GrampsApiException>(
            () => client.SearchAsync(
                new GrampsSearchRequest { Query = "Sutherland", Semantic = false }, CancellationToken.None));

        Assert.Contains("option not set", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_PostsPayloadAndReturnsResponseBody()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""[{"type":"add","handle":"h1"}]""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);
        var payload = JsonDocument.Parse("""{"gramps_id":"I9999"}""").RootElement;

        var result = await client.CreateAsync("/api/people/", payload, CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("h1", result[0].GetProperty("handle").GetString());
        Assert.Equal("""{"gramps_id":"I9999"}""", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task CreateAsync_WhenResponseHasNoBody_ReturnsSerializableNullElement()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var client = CreateClient(handler);
        var payload = JsonDocument.Parse("{}").RootElement;

        var result = await client.CreateAsync("/api/people/", payload, CancellationToken.None);

        // Must be ValueKind.Null, not the default(JsonElement) ValueKind.Undefined — the MCP SDK
        // serializes the tool's return value, and JsonSerializer throws on Undefined elements.
        Assert.Equal(JsonValueKind.Null, result.ValueKind);
        JsonSerializer.Serialize(result);
    }

    [Fact]
    public async Task CreateAsync_WhenResponseBodyIsNotValidJson_ThrowsGrampsApiException()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("<html>not json</html>", Encoding.UTF8, "text/html"),
        });
        var client = CreateClient(handler);
        var payload = JsonDocument.Parse("{}").RootElement;

        await Assert.ThrowsAsync<GrampsApiException>(
            () => client.CreateAsync("/api/people/", payload, CancellationToken.None));
    }

    [Fact]
    public async Task ReplaceAsync_SendsPut()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);
        var payload = JsonDocument.Parse("""{"handle":"h1"}""").RootElement;

        await client.ReplaceAsync("/api/people/h1", payload, CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/api/people/h1", request.RequestUri!.AbsolutePath);
        Assert.Equal("""{"handle":"h1"}""", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task DeleteAsync_SendsDelete()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        await client.DeleteAsync("/api/people/h1", CancellationToken.None);

        Assert.Equal(HttpMethod.Delete, handler.Requests.Single().Method);
    }

    [Fact]
    public async Task PostAsync_SendsBodyAndReturnsResponse()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"handle":"phoenix"}""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);
        var body = JsonDocument.Parse("""{"family_merger":true}""").RootElement;

        var result = await client.PostAsync("/api/people/phoenix/merge/titanic", body, CancellationToken.None);

        Assert.Equal("phoenix", result.GetProperty("handle").GetString());
    }

    [Fact]
    public async Task UndoTransactionAsync_BuildsForceAndMessageQueryParameters()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        await client.UndoTransactionAsync(42, force: true, message: "oops", CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/transactions/history/42/undo", request.RequestUri!.AbsolutePath);
        Assert.Contains("force=true", request.RequestUri.Query);
        Assert.Contains("message=oops", request.RequestUri.Query);
    }

    [Fact]
    public async Task ListHistoryAsync_OmitsProfileAndAppliesSort()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"id":1}]""", Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("x-total-count", "1");
            return response;
        });
        var client = CreateClient(handler);

        var result = await client.ListHistoryAsync<JsonElement>(
            new GrampsHistoryRequest { Sort = "-id" }, CancellationToken.None);

        var query = handler.Requests.Single().RequestUri!.Query;
        Assert.Contains("sort=-id", query);
        Assert.DoesNotContain("profile=", query);
        Assert.Equal(1, result.Total);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler) =>
        GrampsTestClientFactory.CreateClient(handler);

    private sealed record TestPerson(
        [property: JsonPropertyName("handle")] string Handle,
        [property: JsonPropertyName("gramps_id")] string GrampsId);
}
