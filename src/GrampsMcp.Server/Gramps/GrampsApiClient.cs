using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrampsMcp.Server.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace GrampsMcp.Server.Gramps;

/// <summary>Typed client for the Gramps Web API. Every list call goes through the guardrails in
/// <see cref="GrampsQueryBuilder"/>.</summary>
public sealed class GrampsApiClient(HttpClient httpClient, IOptions<GrampsOptions> options)
{
    public Task<GrampsListResult<T>> ListAsync<T>(
        string path, GrampsListRequest request, CancellationToken cancellationToken) =>
        ExecuteListAsync<T>(path, GrampsQueryBuilder.Build(request, options.Value), cancellationToken);

    /// <summary>Fetches a paginated person/family timeline, e.g. <c>/api/people/{handle}/timeline</c>.</summary>
    public Task<GrampsListResult<T>> ListTimelineAsync<T>(
        string path, GrampsTimelineRequest request, CancellationToken cancellationToken) =>
        ExecuteListAsync<T>(path, GrampsQueryBuilder.BuildForTimeline(request, options.Value), cancellationToken);

    /// <summary>Filters a single object type's collection with a GQL expression, e.g.
    /// <c>/api/people/?gql=child_ref_list.length%20%3E%3D%203</c>.</summary>
    public Task<GrampsListResult<T>> QueryAsync<T>(
        string path, GrampsGqlRequest request, CancellationToken cancellationToken)
    {
        var query = GrampsQueryBuilder.BuildForGql(request, options.Value);
        query.Add(new KeyValuePair<string, string?>("gql", request.Gql));
        return ExecuteListAsync<T>(path, query, cancellationToken);
    }

    /// <summary>Counts objects matching a GQL expression, at the cost of one request with
    /// <c>pagesize=1</c> (every list response carries <c>x-total-count</c>).</summary>
    public async Task<int> CountAsync(string path, string gql, CancellationToken cancellationToken)
    {
        var request = new GrampsGqlRequest { Gql = gql, PageSize = 1, Keys = ["handle"] };
        var result = await QueryAsync<JsonElement>(path, request, cancellationToken);
        return result.Total;
    }

    /// <summary>Fetches a paginated log of committed transactions, e.g. <c>/api/transactions/history/</c>.</summary>
    public Task<GrampsListResult<T>> ListHistoryAsync<T>(
        GrampsHistoryRequest request, CancellationToken cancellationToken) =>
        ExecuteListAsync<T>("/api/transactions/history/", GrampsQueryBuilder.BuildForHistory(request, options.Value), cancellationToken);

    /// <summary>Creates a new object, e.g. <c>POST /api/people/</c>. Returns whatever body Gramps sends back
    /// (a list of change records), or an empty <see cref="JsonElement"/> if the response has no content.
    /// Identical wire behavior to <see cref="PostAsync"/>; kept as a separate name for call-site clarity.</summary>
    public Task<JsonElement> CreateAsync(string path, JsonElement payload, CancellationToken cancellationToken) =>
        PostAsync(path, payload, cancellationToken);

    /// <summary>Replaces an object wholesale, e.g. <c>PUT /api/people/{handle}</c> — Gramps Web's PUT
    /// replaces the entire object, so callers must read-modify-write the full body.</summary>
    public async Task ReplaceAsync(string path, JsonElement payload, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(path, payload, cancellationToken);
        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>Deletes an object, e.g. <c>DELETE /api/people/{handle}</c>.</summary>
    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync(path, cancellationToken);
        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>Posts a JSON body with no list-shaped response, e.g. merging two objects at
    /// <c>POST /api/people/{phoenix}/merge/{titanic}</c>.</summary>
    public async Task<JsonElement> PostAsync(string path, JsonElement body, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(path, body, cancellationToken);
        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonOrEmptyAsync(response, cancellationToken);
    }

    /// <summary>Undoes a committed transaction, e.g. <c>POST /api/transactions/history/{id}/undo</c>.</summary>
    public async Task<JsonElement> UndoTransactionAsync(
        int transactionId, bool force, string? message, CancellationToken cancellationToken)
    {
        var query = new List<KeyValuePair<string, string?>> { new("force", force ? "true" : "false") };
        if (!string.IsNullOrWhiteSpace(message))
        {
            query.Add(new KeyValuePair<string, string?>("message", message));
        }

        var url = QueryHelpers.AddQueryString($"/api/transactions/history/{transactionId}/undo", query);
        using var response = await httpClient.PostAsync(url, content: null, cancellationToken);
        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonOrEmptyAsync(response, cancellationToken);
    }

    private const string SemanticSearchUnconfiguredMarker = "VECTOR_EMBEDDING_MODEL";

    private const string SemanticSearchUnconfiguredMessage =
        "Semantic search is not available: this Gramps Web instance has no VECTOR_EMBEDDING_MODEL " +
        "configured upstream, so semantic=true always fails here. Retrying will not help — call " +
        "gramps_search with semantic=false (the default) instead.";

    public async Task<GrampsListResult<GrampsSearchHit>> SearchAsync(
        GrampsSearchRequest request, CancellationToken cancellationToken)
    {
        var query = GrampsQueryBuilder.BuildForSearch(request, options.Value);
        query.Add(new KeyValuePair<string, string?>("query", request.Query));

        if (request.Semantic)
        {
            query.Add(new KeyValuePair<string, string?>("semantic", "true"));
        }

        if (request.Types is { Count: > 0 })
        {
            query.Add(new KeyValuePair<string, string?>("type", string.Join(',', request.Types)));
        }

        try
        {
            return await ExecuteListAsync<GrampsSearchHit>("/api/search/", query, cancellationToken);
        }
        catch (GrampsApiException ex) when (
            request.Semantic &&
            ex.StatusCode == HttpStatusCode.ServiceUnavailable &&
            ex.Message.Contains(SemanticSearchUnconfiguredMarker, StringComparison.Ordinal))
        {
            throw new GrampsApiException(ex.StatusCode, SemanticSearchUnconfiguredMessage);
        }
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new GrampsApiException(response.StatusCode, $"Gramps API returned an empty body for {path}.");
    }

    /// <summary>Fetches a single object by its internal handle, e.g. <c>/api/people/{handle}</c>.</summary>
    public Task<T> GetAsync<T>(string path, GrampsGetRequest request, CancellationToken cancellationToken)
    {
        var query = GrampsQueryBuilder.BuildForGet(request, options.Value);
        return GetAsync<T>(QueryHelpers.AddQueryString(path, query), cancellationToken);
    }

    /// <summary>
    /// Looks up a single object by its user-managed Gramps ID (e.g. "I0155") via the collection
    /// endpoint's <c>gramps_id</c> filter. Returns <see langword="default"/> if there is no match.
    /// </summary>
    public async Task<T?> FindByGrampsIdAsync<T>(
        string collectionPath, string grampsId, GrampsGetRequest request, CancellationToken cancellationToken)
    {
        var query = GrampsQueryBuilder.BuildForGet(request, options.Value).ToList();
        query.Add(new KeyValuePair<string, string?>("gramps_id", grampsId));
        var url = QueryHelpers.AddQueryString(collectionPath, query);

        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken) ?? [];
        return items.Count > 0 ? items[0] : default;
    }

    private async Task<GrampsListResult<T>> ExecuteListAsync<T>(
        string path, IReadOnlyList<KeyValuePair<string, string?>> query, CancellationToken cancellationToken)
    {
        var url = QueryHelpers.AddQueryString(path, query);

        using var response = await httpClient.GetAsync(url, cancellationToken);
        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);

        var items = await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken) ?? [];
        var total = ReadTotalCount(response, items.Count);
        // GrampsQueryBuilder.Build always sets page and pagesize, so these lookups are never null.
        var page = int.Parse(query.First(q => q.Key == "page").Value!, CultureInfo.InvariantCulture);
        var pageSize = int.Parse(query.First(q => q.Key == "pagesize").Value!, CultureInfo.InvariantCulture);

        return new GrampsListResult<T>(items, total, page, pageSize);
    }

    private static async Task<JsonElement> ReadJsonOrEmptyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // default(JsonElement) has ValueKind.Undefined, which JsonSerializer throws on when the MCP SDK
        // serializes the tool's return value — parse an explicit JSON null instead so it stays serializable.
        if (string.IsNullOrWhiteSpace(body))
        {
            return JsonSerializer.Deserialize<JsonElement>("null");
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (JsonException exception)
        {
            // A 2xx response with a body that isn't valid JSON (e.g. a proxy returning HTML) must
            // surface as a GrampsApiException, not an unhandled JsonException — GrampsToolErrors
            // only translates the former, and letting this escape trades the real diagnostic for
            // the MCP SDK's generic "An error occurred invoking '...'" message.
            throw new GrampsApiException(
                response.StatusCode, $"Gramps API returned a response that isn't valid JSON: {exception.Message}");
        }
    }

    private static int ReadTotalCount(HttpResponseMessage response, int fallback)
    {
        if (response.Headers.TryGetValues("x-total-count", out var values) &&
            int.TryParse(values.FirstOrDefault(), out var total))
        {
            return total;
        }

        return fallback;
    }
}
