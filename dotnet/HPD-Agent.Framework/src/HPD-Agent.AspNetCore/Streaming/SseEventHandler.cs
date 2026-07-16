using HPD.Agent.Serialization;
using Microsoft.AspNetCore.Http;

namespace HPD.Agent.AspNetCore.Streaming;

/// <summary>Adapts canonical journal observation to Server-Sent Events.</summary>
internal static class SseEventHandler
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static async Task StreamEventsAsync(
        HttpContext context,
        ISessionStore store,
        ThreadKey thread,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        var cursor = ParseAppliedCursor(context.Request);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var observer = store.ObserveThreadEventsAsync(
                thread,
                cursor,
                new ThreadObservationOptions(),
                cancellationToken).GetAsyncEnumerator(cancellationToken);

            Task<bool>? pendingMove = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                pendingMove ??= observer.MoveNextAsync().AsTask();
                var heartbeat = Task.Delay(HeartbeatInterval, cancellationToken);
                if (await Task.WhenAny(pendingMove, heartbeat).ConfigureAwait(false) == heartbeat)
                {
                    await context.Response.WriteAsync(": heartbeat\n\n", cancellationToken).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!await pendingMove.ConfigureAwait(false))
                    return;
                pendingMove = null;

                foreach (var evt in observer.Current.Events)
                {
                    var json = AgentEventSerializer.ToJson(evt);
                    await context.Response.WriteAsync($"id: {evt.ThreadSequenceNumber}\n", cancellationToken)
                        .ConfigureAwait(false);
                    await context.Response.WriteAsync("event: agent-event\n", cancellationToken)
                        .ConfigureAwait(false);
                    await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken)
                        .ConfigureAwait(false);
                }
                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected.
        }
        catch
        {
            // Headers are already committed. Closing the connection is the protocol error signal;
            // clients reconnect from their last successfully applied journal position.
            context.Abort();
            throw;
        }
    }

    private static long ParseAppliedCursor(HttpRequest request)
    {
        var value = request.Query.TryGetValue("after", out var queryValue)
            ? queryValue.ToString()
            : request.Headers.TryGetValue("Last-Event-ID", out var headerValue)
                ? headerValue.ToString()
                : "0";
        if (!long.TryParse(value, out var cursor) || cursor < 0)
            throw new BadHttpRequestException("The event cursor must be a non-negative integer.");
        return cursor;
    }
}
