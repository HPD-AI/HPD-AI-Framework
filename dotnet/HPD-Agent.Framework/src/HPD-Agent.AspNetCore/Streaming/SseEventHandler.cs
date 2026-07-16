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

        var head = await store.GetThreadEventHeadAsync(thread, cancellationToken).ConfigureAwait(false)
            ?? throw new BadHttpRequestException("The requested thread does not exist.");
        var cursor = ParseAppliedCursor(context.Request, head.Generation);
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
                    await context.Response.WriteAsync(
                            $"id: {observer.Current.Generation}:{evt.ThreadSequenceNumber}\n",
                            cancellationToken)
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
        catch (ThreadJournalReplacedException rebased)
        {
            await context.Response.WriteAsync("event: thread-journal-rebased\n", cancellationToken)
                .ConfigureAwait(false);
            await context.Response.WriteAsync(
                    $"data: {{\"previousGeneration\":{rebased.PreviousCursor.Generation},\"currentGeneration\":{rebased.CurrentCursor.Generation}}}\n\n",
                    cancellationToken)
                .ConfigureAwait(false);
            await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Headers are already committed. Closing the connection is the protocol error signal;
            // clients reconnect from their last successfully applied journal position.
            context.Abort();
            throw;
        }
    }

    private static ThreadJournalCursor ParseAppliedCursor(HttpRequest request, long currentGeneration)
    {
        var value = request.Query.TryGetValue("after", out var queryValue)
            ? queryValue.ToString()
            : request.Headers.TryGetValue("Last-Event-ID", out var headerValue)
                ? headerValue.ToString()
                : string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return ThreadJournalCursor.Start(currentGeneration);

        var separator = value.IndexOf(':');
        if (separator <= 0 ||
            !long.TryParse(value.AsSpan(0, separator), out var generation) || generation <= 0 ||
            !long.TryParse(value.AsSpan(separator + 1), out var sequence) || sequence < 0)
        {
            throw new BadHttpRequestException(
                "The event cursor must use the generation:sequence format.");
        }
        return new ThreadJournalCursor(generation, sequence);
    }
}
