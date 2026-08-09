namespace GrampsMcp.LiveTests.LiveStack;

internal enum RateLimitOutcome
{
    Succeeded,
    RateLimited,
    Failed,
}

/// <summary>The outcome of one attempt handed to <see cref="RateLimitRetry.RunAsync{T}"/>.</summary>
internal readonly record struct RateLimitedAttempt<T>(RateLimitOutcome Outcome, T? Value);

/// <summary>
/// Shared "retry up to 3 times, ~1.5s apart, only on 429" loop for <c>POST /api/token/</c>
/// collisions. Gramps Web hardcodes a 1 request/second rate limit on that endpoint, keyed by
/// source IP; every host-originated call to the published port gets NAT'd through the same Docker
/// bridge gateway IP, so it collides with whatever else (a probe run, a manual curl, a previous
/// test run, another fixture's own auth call moments earlier) hit the endpoint in the last second.
/// Both <see cref="LiveStackProbe"/>'s own token call and <see cref="LiveGrampsFixture"/>'s token
/// warm-up call hit this same collision — measured recovery is ~2s, retry with margin. Extracted
/// so the retry count/delay only needs tuning in one place.
/// </summary>
internal static class RateLimitRetry
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Runs <paramref name="attempt"/> up to <see cref="MaxAttempts"/> times. A
    /// <see cref="RateLimitOutcome.Failed"/> result returns immediately without consuming the
    /// remaining retries, since those exist only for the 429-collision case, not genuine errors.
    /// </summary>
    public static async Task<T?> RunAsync<T>(
        Func<Task<RateLimitedAttempt<T>>> attempt, CancellationToken cancellationToken)
    {
        for (var i = 1; i <= MaxAttempts; i++)
        {
            var result = await attempt();
            if (result.Outcome == RateLimitOutcome.Succeeded)
            {
                return result.Value;
            }

            if (result.Outcome == RateLimitOutcome.Failed || i == MaxAttempts)
            {
                return default;
            }

            await Task.Delay(Delay, cancellationToken);
        }

        return default;
    }
}
