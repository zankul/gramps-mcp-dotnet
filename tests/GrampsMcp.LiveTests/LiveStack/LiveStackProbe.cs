using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.LiveTests.LiveStack;

/// <summary>
/// The result of probing a live Gramps Web stack: what state it's in, and — when
/// <see cref="LiveStackState.Ready"/> — the access token and decoded permissions obtained along
/// the way, so the fixture doesn't have to authenticate twice.
/// </summary>
public sealed record LiveStackProbeResult(
    LiveStackState State,
    string? AccessToken,
    IReadOnlySet<string>? Permissions);

/// <summary>
/// Determines whether a live Gramps Web stack is reachable, authenticates, and seeded, in that
/// order — each step's failure maps to a distinct <see cref="LiveStackState"/> so
/// <see cref="LiveStackGate"/> can name the exact remedy rather than a generic "not ready".
/// </summary>
internal static class LiveStackProbe
{
    public static async Task<LiveStackProbeResult> ProbeAsync(
        LiveStackOptions options, HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (!await IsReachableAsync(httpClient, cancellationToken))
        {
            return new LiveStackProbeResult(LiveStackState.StackUnreachable, null, null);
        }

        var accessToken = await TryAuthenticateAsync(options, httpClient, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return new LiveStackProbeResult(LiveStackState.AuthFailed, null, null);
        }

        var permissions = GrampsJwt.ReadPermissions(accessToken);

        // Defensive only: the compose healthcheck already guarantees the seed import finished
        // before gramps-mcp (and therefore this probe) ever runs. This exists to turn "someone
        // pointed GRAMPS_LIVE_API_URL at an empty/unrelated tree" into a named state instead of
        // a confusing failure deep inside the first real test.
        var seedState = await CheckSeedStateAsync(accessToken, httpClient, cancellationToken);
        if (seedState is not null)
        {
            return new LiveStackProbeResult(seedState.Value, accessToken, permissions);
        }

        return new LiveStackProbeResult(LiveStackState.Ready, accessToken, permissions);
    }

    private static async Task<bool> IsReachableAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("/", cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static Task<string?> TryAuthenticateAsync(
        LiveStackOptions options, HttpClient httpClient, CancellationToken cancellationToken) =>
        RateLimitRetry.RunAsync<string?>(async () =>
        {
            try
            {
                using var response = await httpClient.PostAsJsonAsync(
                    "api/token/",
                    new { username = options.Username, password = options.Password },
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return new RateLimitedAttempt<string?>(RateLimitOutcome.RateLimited, null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    // A genuine auth failure (bad credentials, etc.) should fail fast rather than
                    // burn through the remaining retries meant for the 429 collision case.
                    return new RateLimitedAttempt<string?>(RateLimitOutcome.Failed, null);
                }

                var payload = await response.Content.ReadFromJsonAsync<GrampsTokenResponse>(cancellationToken);
                return new RateLimitedAttempt<string?>(RateLimitOutcome.Succeeded, payload?.AccessToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                return new RateLimitedAttempt<string?>(RateLimitOutcome.Failed, null);
            }
        }, cancellationToken);

    private static async Task<LiveStackState?> CheckSeedStateAsync(
        string accessToken, HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/metadata/");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return LiveStackState.ImportIncomplete;
            }

            var metadata = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (!metadata.TryGetProperty("object_counts", out var counts) ||
                !counts.TryGetProperty("people", out var people) ||
                people.ValueKind != JsonValueKind.Number ||
                !people.TryGetInt32(out var peopleCount) ||
                peopleCount <= 0)
            {
                return LiveStackState.TreeNotSeeded;
            }

            return null;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return LiveStackState.ImportIncomplete;
        }
    }
}
