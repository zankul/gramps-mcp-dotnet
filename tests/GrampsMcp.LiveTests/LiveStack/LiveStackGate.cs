namespace GrampsMcp.LiveTests.LiveStack;

/// <summary>
/// The single place the "no live stack, don't silently pass" mechanism lives. xunit 2.9.3 has no
/// <c>Assert.Skip()</c> (a v3-only feature) and throwing <c>SkipException</c> reports as a
/// failure anyway, so this project relies on being its own excluded-by-default project (see
/// <c>tests/default.runsettings</c>) plus this gate — never an <c>if (...) return;</c> early-out,
/// which is exactly the silent-pass bug this whole workstream exists to remove.
/// </summary>
internal static class LiveStackGate
{
    private const string RemedyCommand =
        "docker compose -f docker-compose.test.yml -p gramps-test up -d --wait";

    /// <summary>Throws with an actionable diagnosis if <paramref name="fixture"/> did not reach
    /// <see cref="LiveStackState.Ready"/>. Call this first in every live test body.</summary>
    public static void Require(LiveGrampsFixture fixture)
    {
        if (fixture.State == LiveStackState.Ready)
        {
            return;
        }

        throw new InvalidOperationException(Diagnose(fixture));
    }

    private static string Diagnose(LiveGrampsFixture fixture) => fixture.State switch
    {
        LiveStackState.StackUnreachable =>
            $"Live Gramps stack unreachable at {fixture.Options.ApiUrl} ({LiveStackOptions.ApiUrlVariable}). " +
            $"Start it with: {RemedyCommand}",
        LiveStackState.AuthFailed =>
            $"Live Gramps stack at {fixture.Options.ApiUrl} rejected the configured credentials " +
            $"({LiveStackOptions.UsernameVariable}/{LiveStackOptions.PasswordVariable}). If you started " +
            $"the stack with {RemedyCommand}, the default owner/owner credentials should work — " +
            "check for a stale/mismatched .env override.",
        LiveStackState.TreeNotSeeded =>
            $"Live Gramps stack at {fixture.Options.ApiUrl} is reachable but the tree has no people — " +
            $"the example-dataset import never ran. Recreate the stack cold: {RemedyCommand} " +
            "(after `docker compose -f docker-compose.test.yml -p gramps-test down --volumes`).",
        LiveStackState.ImportIncomplete =>
            $"Live Gramps stack at {fixture.Options.ApiUrl} responded but /api/metadata/ looks like " +
            "the seed import hasn't finished. This should not happen after `up --wait` returns — " +
            $"re-check the stack: {RemedyCommand}",
        LiveStackState.Ready =>
            throw new InvalidOperationException("Diagnose() should not be called when the stack is Ready."),
        _ => throw new InvalidOperationException($"Unhandled {nameof(LiveStackState)}: {fixture.State}."),
    };
}
