using HPD.Agent.Serialization;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        var applicationLifetime = context.RequestServices.GetService<IHostApplicationLifetime>();
        using var streamLifetime = applicationLifetime is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                applicationLifetime.ApplicationStopping);
        var streamCancellationToken = streamLifetime.Token;

        var store = observation.Store;
        var thread = observation.Thread;

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        // The live inbox is created before this handler is entered. Capture one finite
        // journal boundary, replay only through it, and then use the inbox exclusively.
        // This makes the journal the recovery source and the coordinator the live source.
        var head = await store.GetThreadEventHeadAsync(thread, streamCancellationToken).ConfigureAwait(false)
            ?? throw new BadHttpRequestException("The requested thread does not exist.");
        var cursor = ParseAppliedCursor(context.Request, head.Generation);
        await context.Response.Body.FlushAsync(streamCancellationToken).ConfigureAwait(false);

        try
        {
            await foreach (var batch in store.ReadThreadEventsAsync(
                thread,
                new ThreadEventReadRequest(cursor, head.ThreadSequenceNumber),
                streamCancellationToken).ConfigureAwait(false))
            {
                foreach (var evt in batch.Events)
                {
                    await WriteJournalEventAsync(context, batch.Generation, evt, streamCancellationToken)
                        .ConfigureAwait(false);
                    cursor = new ThreadJournalCursor(batch.Generation, evt.ThreadSequenceNumber);
                }
                await context.Response.Body.FlushAsync(streamCancellationToken).ConfigureAwait(false);
            }

            while (!streamCancellationToken.IsCancellationRequested)
            {
                while (observation.LiveEvents.Reader.TryRead(out var evt))
                {
                    var selectedThread = string.Equals(evt.SessionId, thread.SessionId, StringComparison.Ordinal) &&
                        string.Equals(evt.ThreadId, thread.ThreadId, StringComparison.Ordinal);
                    var liveGeneration = head.Generation;
                    if (selectedThread && evt.ThreadSequenceNumber > 0)
                    {
                        var liveHead = await store.GetThreadEventHeadAsync(thread, streamCancellationToken).ConfigureAwait(false)
                            ?? throw new ThreadDeletedException(thread);
                        liveGeneration = liveHead.Generation;
                        if (liveGeneration != head.Generation)
                        {
                            await WriteRebasedAsync(
                                context, head.Generation, liveGeneration, streamCancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }

                    // The inbox was subscribed before the boundary was captured, so it can
                    // contain committed events already included in the finite replay.
                    if (evt.ThreadSequenceNumber > 0 &&
                        selectedThread &&
                        evt.ThreadSequenceNumber <= head.ThreadSequenceNumber)
                        continue;

                    await WriteLiveEventAsync(
                        context, liveGeneration, evt, selectedThread, streamCancellationToken).ConfigureAwait(false);
                    if (evt.ThreadSequenceNumber > 0 && selectedThread)
                    {
                        cursor = new ThreadJournalCursor(head.Generation, evt.ThreadSequenceNumber);
                    }
                }
                await context.Response.Body.FlushAsync(streamCancellationToken).ConfigureAwait(false);

                var available = observation.LiveEvents.Reader.WaitToReadAsync(streamCancellationToken).AsTask();
                var heartbeat = Task.Delay(HeartbeatInterval, streamCancellationToken);
                if (await Task.WhenAny(available, heartbeat).ConfigureAwait(false) == heartbeat)
                {
                    await context.Response.WriteAsync(": heartbeat\n\n", streamCancellationToken).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(streamCancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!await available.ConfigureAwait(false))
                    return;
            }
        }
        catch (OperationCanceledException) when (streamCancellationToken.IsCancellationRequested)
        {
            // The client disconnected or the host began graceful shutdown.
        }
        catch (ThreadJournalReplacedException rebased)
        {
            await WriteRebasedAsync(
                context,
                rebased.PreviousCursor.Generation,
                rebased.CurrentCursor.Generation,
                streamCancellationToken).ConfigureAwait(false);
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
        long generation,
        AgentEvent evt,
        bool includeJournalCursor,
        CancellationToken cancellationToken)
    {
        var json = AgentEventSerializer.ToJson(evt);
        if (includeJournalCursor && evt.ThreadSequenceNumber > 0)
        {
            await context.Response.WriteAsync(
                    $"id: {generation}:{evt.ThreadSequenceNumber}\n",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        await context.Response.WriteAsync("event: live-agent-event\n", cancellationToken)
            .ConfigureAwait(false);
        await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteJournalEventAsync(
        HttpContext context,
        long generation,
        AgentEvent evt,
        CancellationToken cancellationToken)
    {
        var json = AgentEventSerializer.ToJson(evt);
        await context.Response.WriteAsync(
                $"id: {generation}:{evt.ThreadSequenceNumber}\n",
                cancellationToken)
            .ConfigureAwait(false);
        await context.Response.WriteAsync("event: agent-event\n", cancellationToken).ConfigureAwait(false);
        await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteRebasedAsync(
        HttpContext context,
        long previousGeneration,
        long currentGeneration,
        CancellationToken cancellationToken)
    {
        await context.Response.WriteAsync("event: thread-journal-rebased\n", cancellationToken)
            .ConfigureAwait(false);
        await context.Response.WriteAsync(
                $"data: {{\"previousGeneration\":{previousGeneration},\"currentGeneration\":{currentGeneration}}}\n\n",
                cancellationToken)
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
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
