using System.Globalization;
using System.Net;
using System.Net.Http.Json;
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

    public Task<GrampsListResult<GrampsSearchHit>> SearchAsync(
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

        return ExecuteListAsync<GrampsSearchHit>("/api/search/", query, cancellationToken);
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
