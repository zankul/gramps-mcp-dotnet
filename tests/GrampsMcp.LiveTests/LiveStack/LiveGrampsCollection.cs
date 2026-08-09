namespace GrampsMcp.LiveTests.LiveStack;

/// <summary>
/// Shares one <see cref="LiveGrampsFixture"/> (one authentication, one readiness probe) across
/// every live test class. <c>DisableParallelization = true</c> is not optional: transaction
/// history/undo tests are globally ordered and write tests mutate shared state, so every live
/// test in this collection must run sequentially even though no write tests exist yet.
/// </summary>
#pragma warning disable CA1711 // xunit's collection-definition convention is to suffix with "Collection".
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveGrampsCollection : ICollectionFixture<LiveGrampsFixture>
{
    public const string Name = "Live Gramps stack";
}
#pragma warning restore CA1711
