namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Serializes write calls to Gramps Web. Overlapping writes can make a SQLite-backed Gramps Web
/// fail with <c>database is locked</c> and stay wedged until restart, so the typed client — which
/// is transient — shares one instance of this gate (registered as a singleton) and queues writes
/// through it. Reads never touch it.
/// </summary>
public sealed class GrampsWriteGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Runs <paramref name="action"/> once all earlier writes have finished. Cancellation
    /// while queued abandons the wait without ever holding the gate.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
