using HPD.Agent.Serialization;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Http;

namespace HPD.Agent.AspNetCore.Streaming;

/// <summary>Combines canonical journal replay with complete live runtime delivery over SSE.</summary>
internal static class SseEventHandler
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static async Task StreamEventsAsync(
        HttpContext context,
        ThreadEventObservationLease observation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(observation);

        var store = observation.Store;
        var thread = observation.Thread;

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
            Task<bool>? pendingLive = null;
            var liveCompleted = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                pendingMove ??= observer.MoveNextAsync().AsTask();
                if (!liveCompleted)
                    pendingLive ??= observation.LiveEvents.Reader.WaitToReadAsync(cancellationToken).AsTask();
                var heartbeat = Task.Delay(HeartbeatInterval, cancellationToken);
                var completed = pendingLive is null
                    ? await Task.WhenAny(pendingMove, heartbeat).ConfigureAwait(false)
                    : await Task.WhenAny(pendingMove, pendingLive, heartbeat).ConfigureAwait(false);
                if (completed == heartbeat)
                {
                    await context.Response.WriteAsync(": heartbeat\n\n", cancellationToken).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (completed == pendingLive)
                {
                    if (!await pendingLive!.ConfigureAwait(false))
                    {
                        liveCompleted = true;
                        pendingLive = null;
                    }
                    else
                    {
                        pendingLive = null;
                        while (observation.LiveEvents.Reader.TryRead(out var evt))
                        {
                            // Selected-thread committed events are delivered by the canonical journal.
                            // Stateless selected-thread events and every bubbled descendant event are
                            // live-only in this observation scope and must cross the hosted boundary.
                            if (evt.ThreadSequenceNumber > 0 &&
                                string.Equals(evt.SessionId, thread.SessionId, StringComparison.Ordinal) &&
                                string.Equals(evt.ThreadId, thread.ThreadId, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            await WriteLiveEventAsync(context, evt, cancellationToken).ConfigureAwait(false);
                        }
                        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                if (completed == pendingMove)
                {
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

    private static async Task WriteLiveEventAsync(
        HttpContext context,
        AgentEvent evt,
        CancellationToken cancellationToken)
    {
        var json = AgentEventSerializer.ToJson(evt);
        await context.Response.WriteAsync("event: live-agent-event\n", cancellationToken)
            .ConfigureAwait(false);
        await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken)
            .ConfigureAwait(false);
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
