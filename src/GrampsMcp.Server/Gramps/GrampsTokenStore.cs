using System.Net.Http.Headers;
using System.Net.Http.Json;
using GrampsMcp.Server.Configuration;
using Microsoft.Extensions.Options;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Holds the current Gramps Web access/refresh token pair in memory and knows how to obtain and
/// renew them. Never logs a token value.
/// </summary>
public sealed class GrampsTokenStore(
    IHttpClientFactory httpClientFactory,
    IOptions<GrampsOptions> options,
    ILogger<GrampsTokenStore> logger) : IDisposable
{
    public const string AuthClientName = "GrampsAuth";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private string? _refreshToken;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null)
        {
            return _accessToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null)
            {
                return _accessToken;
            }

            await AuthenticateAsync(cancellationToken);
            return _accessToken!;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Forces a new access token: tries the refresh token first, falls back to re-authenticating
    /// with the service account if the refresh token is missing or rejected.
    /// </summary>
    public async Task<string> RefreshAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_refreshToken is not null)
            {
                try
                {
                    await RefreshWithTokenAsync(cancellationToken);
                    return _accessToken!;
                }
                catch (GrampsApiException exception)
                {
                    Log.TokenRefreshFailed(logger, exception);
                }
            }

            await AuthenticateAsync(cancellationToken);
            return _accessToken!;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        var grampsOptions = options.Value;
        using var client = httpClientFactory.CreateClient(AuthClientName);
        using var response = await client.PostAsJsonAsync(
            "api/token/",
            new { username = grampsOptions.Username, password = grampsOptions.Password },
            cancellationToken);

        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GrampsTokenResponse>(cancellationToken)
            ?? throw new GrampsApiException(response.StatusCode, "Gramps token endpoint returned an empty body.");

        _accessToken = payload.AccessToken;
        _refreshToken = payload.RefreshToken ?? _refreshToken;
        Log.Authenticated(logger);
    }

    private async Task RefreshWithTokenAsync(CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(AuthClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/token/refresh/")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", _refreshToken) },
        };

        using var response = await client.SendAsync(request, cancellationToken);
        await GrampsErrorTranslator.EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GrampsTokenResponse>(cancellationToken)
            ?? throw new GrampsApiException(response.StatusCode, "Gramps refresh endpoint returned an empty body.");

        _accessToken = payload.AccessToken;
    }

    public void Dispose() => _lock.Dispose();
}
