namespace GrampsMcp.LiveTests.Writes;

/// <summary>
/// The reserved <c>gramps_id</c> namespace write tests use for isolation, instead of a second
/// tree: every object a write test creates gets an explicit <c>ZZTEST-{runId}-{n}</c> ID, which
/// cannot collide with the seeded <c>example.gramps</c> data (its IDs are a single letter prefix
/// plus digits only — "I0155", "F0012" — never a hyphen; see <c>GrampsIdentifiers</c>) and can be
/// found again with one query per type via <see cref="ZzTestSweep"/>.
/// </summary>
internal static class ZzTestNamespace
{
    // One ID per test process, so a leftover object's gramps_id records which run created it —
    // useful when diagnosing a killed run's leftovers by hand. Collision with a previous run
    // doesn't matter for correctness (the sweep bounds below catch every run's IDs together), only
    // for that diagnostic readability.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static int _counter;

    public const string Prefix = "ZZTEST-";

    /// <summary>
    /// Exclusive upper bound for the <c>gramps_id &gt;= LowerBound and gramps_id &lt; UpperBound</c>
    /// GQL range sweep. Verified empirically against the live stack: Gramps QL's grammar rejects
    /// method-call-style filters entirely (<c>gramps_id.startswith("ZZTEST-")</c>,
    /// <c>gramps_id.contains(...)</c>, <c>like(...)</c> all fail to parse with "Expected end of
    /// text, found '('"), and rejects regex/membership operators too (<c>=~</c>, <c>in</c>) — but
    /// plain string comparison operators (<c>&lt;</c>, <c>&gt;=</c>, ...) work. "ZZTEST-..." sorts
    /// before "ZZTEU" for every possible suffix (comparing character by character, 'S' &lt; 'U' at
    /// the fifth position), so this pair of bounds brackets exactly — and only — the reserved
    /// namespace, without a method call GQL can't parse.
    /// </summary>
    public const string LowerBound = "ZZTEST-";

    public const string UpperBound = "ZZTEU";

    /// <summary>
    /// Object types the sweep can reach. Deliberately excludes "note" and "tag": both return
    /// HTTP 500 (server-side <c>UnboundLocalError</c> in <c>gramps_ql</c>'s attribute traversal,
    /// not a 422 parse error) for *any* GQL comparison against those two endpoints specifically —
    /// verified directly against this stack (<c>docker logs</c> showed the Python traceback), not
    /// assumed. Every write test in this file only ever creates "person" objects, so this doesn't
    /// limit real coverage — it's a documented gap in the sweep's reach for object types nothing
    /// here creates, not a gap in cleanup for what these tests actually do. If a future test ever
    /// creates a "note" or "tag" and its own <c>TestObjectScope</c> cleanup is skipped (e.g. an
    /// unhandled exception before tracking it), neither this sweep nor a future run's opening pass
    /// could find and remove it — only <c>ExampleTreeUnchanged_AfterWriteSuite</c>'s exact-count
    /// check would catch the resulting drift, and the tree would stay dirty until fixed by hand.
    /// </summary>
    public static readonly IReadOnlyList<string> SweepableTypes =
        ["person", "family", "event", "place", "citation", "source", "repository", "media"];

    /// <summary>A fresh, collision-proof Gramps ID in the reserved ZZTEST namespace, e.g.
    /// "ZZTEST-3F2A9C1B-4".</summary>
    public static string NextGrampsId() => $"{Prefix}{RunId}-{Interlocked.Increment(ref _counter)}";
}
