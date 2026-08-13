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
    ILogger<GrampsTokenStore> logger,
    GrampsTokenSeed? seed = null) : IDisposable
{
    public const string AuthClientName = "GrampsAuth";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken = seed?.AccessToken;
    private string? _refreshToken = seed?.RefreshToken;

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
    /// with the service account if the refresh token is missing or rejected. Equivalent to
    /// <see cref="RefreshAsync(string?, CancellationToken)"/> with no stale token to compare
    /// against, so it always performs a fresh refresh even if another caller just did one.
    /// </summary>
    public Task<string> RefreshAsync(CancellationToken cancellationToken) =>
        RefreshAsync(expectedStaleToken: null, cancellationToken);

    /// <summary>
    /// Forces a new access token, unless <paramref name="expectedStaleToken"/> no longer matches
    /// the current access token by the time this caller gets the lock — in that case another
    /// caller already refreshed while this one was waiting, so the already-refreshed token is
    /// reused instead of issuing a redundant refresh request. This is what stops a token expiry
    /// under N concurrent callers from producing N serial refresh round-trips (a stampede): every
    /// caller but the first that acquired the lock finds <see cref="_accessToken"/> has already
    /// moved on from the token they each saw 401 for.
    /// </summary>
    public async Task<string> RefreshAsync(string? expectedStaleToken, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (expectedStaleToken is not null && _accessToken is not null && _accessToken != expectedStaleToken)
            {
                return _accessToken;
            }

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

/// <summary>
/// An access/refresh token pair already obtained by <see cref="Tools.GrampsToolTypeResolver"/>'s
/// startup permission check, registered into DI (see <c>Program.cs</c>) so the real
/// <see cref="GrampsTokenStore"/> the host builds afterwards starts pre-authenticated instead of
/// logging in again on its first real request — see #84's "logs in twice at startup" finding.
/// </summary>
public sealed record GrampsTokenSeed(string AccessToken, string? RefreshToken);
