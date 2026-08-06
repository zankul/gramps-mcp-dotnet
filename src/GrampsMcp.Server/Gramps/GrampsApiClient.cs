using System.Globalization;
using System.Net.Http.Json;
using GrampsMcp.Server.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace GrampsMcp.Server.Gramps;

/// <summary>Typed client for the Gramps Web API. Every list call goes through the guardrails in
/// <see cref="GrampsQueryBuilder"/>.</summary>
public sealed class GrampsApiClient(HttpClient httpClient, IOptions<GrampsOptions> options)
{
    public async Task<GrampsListResult<T>> ListAsync<T>(
        string path, GrampsListRequest request, CancellationToken cancellationToken)
    {
        var query = GrampsQueryBuilder.Build(request, options.Value);
        var url = QueryHelpers.AddQueryString(path, query);

        using var response = await httpClient.GetAsync(url, cancellationToken);
        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);

        var items = await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken) ?? [];
        var total = ReadTotalCount(response, items.Count);
        // GrampsQueryBuilder always sets page and pagesize, so these lookups are never null.
        var page = int.Parse(query.First(q => q.Key == "page").Value!, CultureInfo.InvariantCulture);
        var pageSize = int.Parse(query.First(q => q.Key == "pagesize").Value!, CultureInfo.InvariantCulture);

        return new GrampsListResult<T>(items, total, page, pageSize);
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new GrampsApiException(response.StatusCode, $"Gramps API returned an empty body for {path}.");
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
