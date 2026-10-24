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

    /// <summary>Runs <paramref name="action"/> once all earlier writes have finished. Cancelling while
    /// queued abandons the wait without ever holding the gate. Once the action has started it is
    /// handed <see cref="CancellationToken.None"/> and runs to completion: aborting the local HTTP
    /// call would release the gate while Gramps Web may still be committing the write, letting the
    /// next queued write overlap it — the very race this gate exists to prevent.</summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Last chance to back out: nothing has been sent yet.
            cancellationToken.ThrowIfCancellationRequested();
            return await action(CancellationToken.None);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>Overload for actions with no result.</summary>
    public Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            await action(ct);
            return true;
        }, cancellationToken);

    public void Dispose() => _semaphore.Dispose();
}
