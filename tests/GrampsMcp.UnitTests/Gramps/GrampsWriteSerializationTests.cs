using System.Net;
using System.Text;
using System.Text.Json;
using GrampsMcp.Server.Gramps;
using GrampsMcp.Server.Tools;
using Microsoft.Extensions.Options;

namespace GrampsMcp.UnitTests.Gramps;

public class GrampsWriteSerializationTests
{
    private static GrampsApiClient CreateClient(HttpMessageHandler handler, GrampsWriteGate gate) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri(GrampsTestClientFactory.Options.ApiUrl) },
            Options.Create(GrampsTestClientFactory.Options),
            gate);

    [Fact]
    public async Task ConcurrentWrites_AcrossClientInstances_NeverOverlap()
    {
        using var gate = new GrampsWriteGate();
        var inFlight = 0;
        var maxInFlight = 0;
        var handler = new TestHttpMessageHandler(async (_, ct) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref maxInFlight, now);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref inFlight);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var body = JsonDocument.Parse("{}").RootElement;

        // Distinct client instances sharing one gate, as the transient typed client does in DI.
        var tasks = Enumerable.Range(0, 8).Select(i =>
        {
            var client = CreateClient(handler, gate);
            return (i % 4) switch
            {
                0 => client.CreateAsync("api/notes/", body, CancellationToken.None),
                1 => client.ReplaceAsync("api/notes/h", body, CancellationToken.None),
                2 => client.DeleteAsync("api/notes/h", CancellationToken.None),
                _ => client.UndoTransactionAsync(1, false, null, CancellationToken.None),
            };
        });
        await Task.WhenAll(tasks);

        Assert.Equal(8, handler.Requests.Count);
        Assert.Equal(1, maxInFlight);
    }

    [Fact]
    public async Task ConcurrentUpdates_OnSameObject_BothPatchesLand()
    {
        using var gate = new GrampsWriteGate();
        var stored = JsonDocument.Parse("""{"handle":"h1","a":0,"b":0}""").RootElement.Clone();
        var handler = new TestHttpMessageHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                var snapshot = stored.GetRawText();
                await Task.Delay(20, ct); // widen the read-to-write window
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(snapshot, Encoding.UTF8, "application/json"),
                };
            }

            stored = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var first = new GrampsUpdateTool(CreateClient(handler, gate));
        var second = new GrampsUpdateTool(CreateClient(handler, gate));

        await Task.WhenAll(
            first.UpdateAsync("person", "h1", JsonDocument.Parse("""{"a":1}""").RootElement, CancellationToken.None),
            second.UpdateAsync("person", "h1", JsonDocument.Parse("""{"b":2}""").RootElement, CancellationToken.None));

        Assert.Equal(1, stored.GetProperty("a").GetInt32());
        Assert.Equal(2, stored.GetProperty("b").GetInt32());
    }

    [Fact]
    public async Task CancelledWaiter_ReleasesWithoutHoldingGate()
    {
        using var gate = new GrampsWriteGate();
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var holder = gate.RunAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            return 0;
        }, CancellationToken.None);
        await entered.Task;

        using var cts = new CancellationTokenSource();
        var waiter = gate.RunAsync(() => Task.FromResult(1), cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);

        release.SetResult();
        await holder;

        // The cancelled waiter must not have leaked the semaphore.
        var next = await gate.RunAsync(() => Task.FromResult(2), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, next);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                break;
            }
        }
    }
}
