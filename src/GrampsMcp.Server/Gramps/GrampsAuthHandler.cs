using System.Net;
using System.Net.Http.Headers;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Attaches the current Gramps bearer token to every outgoing request and retries once, after a
/// forced token refresh, if the server responds 401.
/// </summary>
public sealed class GrampsAuthHandler(GrampsTokenStore tokenStore) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = await tokenStore.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var bufferedRequest = await BufferRequestContentAsync(request, cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var refreshedToken = await tokenStore.RefreshAsync(cancellationToken);
        var retryRequest = await CloneRequestAsync(bufferedRequest, refreshedToken, cancellationToken);
        return await base.SendAsync(retryRequest, cancellationToken);
    }

    /// <summary>
    /// Reads request content into memory up front so it can be resent after a 401 — the original
    /// <see cref="HttpRequestMessage"/> cannot be sent twice.
    /// </summary>
    private static async Task<HttpRequestMessage> BufferRequestContentAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            request.Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = request.Content.Headers.ContentType },
            };
        }

        return request;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage original, string accessToken, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
        };

        if (original.Content is ByteArrayContent byteContent)
        {
            var bytes = await byteContent.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = byteContent.Headers.ContentType },
            };
        }

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return clone;
    }
}
