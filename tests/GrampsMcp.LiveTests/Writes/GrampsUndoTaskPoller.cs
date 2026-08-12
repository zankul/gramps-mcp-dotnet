using System.Text.Json;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.LiveTests.Writes;

/// <summary>
/// Polls <c>GET /api/tasks/{id}</c> until an undo reaches a terminal state. Verified empirically
/// against the live stack: unlike create/update/delete/merge (all synchronous 200s),
/// <c>POST /api/transactions/history/{id}/undo</c> always returns 202 with a
/// <c>{"task":{"id":...}}</c> pointer — the undo itself runs asynchronously via Celery.
/// <see cref="GrampsApiClient.UndoTransactionAsync"/> (and therefore <c>gramps_undo</c>) returns
/// that immediate 202 body as-is, not the completed result, so a caller that needs to know
/// whether the undo actually succeeded has to poll this endpoint itself — there is no
/// production-code equivalent to reuse here.
/// </summary>
internal static class GrampsUndoTaskPoller
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Extracts the task ID from an undo response's <c>{"task":{"id":...}}</c> envelope.</summary>
    public static string ExtractTaskId(JsonElement undoResponse) =>
        undoResponse.GetProperty("task").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Undo response's task.id was null.");

    /// <summary>Polls until the task's <c>state</c> is "SUCCESS" or "FAILURE" (Celery's terminal
    /// states), or throws <see cref="TimeoutException"/> after <see cref="Timeout"/> — measured
    /// completion in practice was well under a second, so this budget is generous, not tight.</summary>
    public static async Task<JsonElement> WaitForCompletionAsync(
        GrampsApiClient apiClient, string taskId, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (true)
        {
            var task = await apiClient.GetAsync<JsonElement>($"/api/tasks/{taskId}", cancellationToken);
            var state = task.GetProperty("state").GetString();

            if (state is "SUCCESS" or "FAILURE")
            {
                return task;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"gramps_undo task {taskId} did not reach a terminal state within {Timeout}; last state: {state}.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
