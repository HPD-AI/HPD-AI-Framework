using System.Text.Json;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Serialization;
using HPD.Agent.AspNetCore.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HPD.Agent.AspNetCore.Streaming;

/// <summary>One transport-ready SSE event produced from a committed or live agent event.</summary>
/// <param name="EventName">The SSE event name.</param>
/// <param name="DataJson">Compact JSON or text written as the SSE data payload.</param>
public sealed record AgentSseProjection(string EventName, string DataJson);

/// <summary>Context supplied to a review- or product-specific SSE projector.</summary>
public sealed record AgentSseProjectionContext(
    AgentEventDelivery Delivery,
    AgentEventCodec EventCodec,
    long Generation,
    bool IsLive,
    bool IsAnchorThread);

/// <summary>Projects one framework event into zero or one product-safe SSE event.</summary>
public delegate ValueTask<AgentSseProjection?> AgentSseProjector(
    AgentSseProjectionContext context,
    CancellationToken cancellationToken);

/// <summary>Combines canonical journal replay with live runtime delivery over SSE.</summary>
/// <remarks>
/// The raw overload preserves the hosted framework behavior. Applications can use
/// <see cref="StreamProjectedEventsAsync"/> to reuse cursor replay, live inbox draining,
/// heartbeats, and journal-rebase handling while applying an application-owned safe projection.
/// </remarks>
public static class SseEventHandler
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>Streams the framework's full event delivery envelope.</summary>
    public static Task StreamEventsAsync(
        HttpContext context,
        ThreadEventObservationLease observation,
        CancellationToken cancellationToken)
        => StreamProjectedEventsAsync(
            context,
            observation,
            static (projectionContext, _) => ValueTask.FromResult<AgentSseProjection?>(
                new AgentSseProjection(
                    projectionContext.IsLive ? "live-agent-event" : "agent-event",
                    SerializeDelivery(
                        projectionContext.EventCodec,
                        projectionContext.Delivery))),
            cancellationToken);

    /// <summary>
    /// Streams zero or one application-projected SSE event for every framework event while
    /// preserving the framework's durable replay and reconnect contract.
    /// </summary>
    public static async Task StreamProjectedEventsAsync(
        HttpContext context,
        ThreadEventObservationLease observation,
        AgentSseProjector projector,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(projector);

        var applicationLifetime = context.RequestServices is { } services
            ? services.GetService<IHostApplicationLifetime>()
            : null;
        using var streamLifetime = applicationLifetime is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                applicationLifetime.ApplicationStopping);
        var streamCancellationToken = streamLifetime.Token;

        var store = observation.Store;
        var eventCodec = store.EventCodec;
        var thread = observation.Anchor;

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        // The live inbox is created before this handler is entered. Capture one finite
        // journal boundary, replay only through it, and then use the inbox exclusively.
        var head = await store.GetThreadEventHeadAsync(
            thread,
            streamCancellationToken).ConfigureAwait(false)
            ?? throw new BadHttpRequestException("The requested thread does not exist.");
        var cursor = ParseAppliedCursor(context.Request, head.Generation);
        await context.Response.WriteAsync(
            ": connected\n\n",
            streamCancellationToken).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(
            streamCancellationToken).ConfigureAwait(false);

        try
        {
            await foreach (var batch in store.ReadThreadEventsAsync(
                thread,
                new ThreadEventReadRequest(cursor, head.ThreadSequenceNumber),
                streamCancellationToken).ConfigureAwait(false))
            {
                foreach (var evt in batch.Events)
                {
                    var route = await AgentEventRoutes.CreateFromStoreAsync(
                        store,
                        evt,
                        streamCancellationToken).ConfigureAwait(false);
                    var delivery = new AgentEventDelivery(evt, route.ToPublic());
                    await ProjectAndWriteAsync(
                        context,
                        projector,
                        delivery,
                        eventCodec,
                        batch.Generation,
                        isLive: false,
                        isAnchorThread: true,
                        streamCancellationToken).ConfigureAwait(false);
                    cursor = new ThreadJournalCursor(
                        batch.Generation,
                        evt.ThreadSequenceNumber);
                }

                await context.Response.Body.FlushAsync(
                    streamCancellationToken).ConfigureAwait(false);
            }

            while (!streamCancellationToken.IsCancellationRequested)
            {
                while (observation.LiveEvents.Reader.TryRead(out var delivery))
                {
                    var evt = delivery.Event;
                    var selectedThread =
                        string.Equals(
                            evt.SessionId,
                            thread.SessionId,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            evt.ThreadId,
                            thread.ThreadId,
                            StringComparison.Ordinal);
                    var liveGeneration = head.Generation;
                    if (selectedThread && evt.ThreadSequenceNumber > 0)
                    {
                        var liveHead = await store.GetThreadEventHeadAsync(
                            thread,
                            streamCancellationToken).ConfigureAwait(false)
                            ?? throw new ThreadDeletedException(thread);
                        liveGeneration = liveHead.Generation;
                        if (liveGeneration != head.Generation)
                        {
                            await WriteRebasedAsync(
                                context,
                                head.Generation,
                                liveGeneration,
                                streamCancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }

                    if (evt.ThreadSequenceNumber > 0 &&
                        selectedThread &&
                        evt.ThreadSequenceNumber <= head.ThreadSequenceNumber)
                    {
                        continue;
                    }

                    await ProjectAndWriteAsync(
                        context,
                        projector,
                        delivery,
                        eventCodec,
                        liveGeneration,
                        isLive: true,
                        selectedThread,
                        streamCancellationToken).ConfigureAwait(false);
                    if (evt.ThreadSequenceNumber > 0 && selectedThread)
                    {
                        cursor = new ThreadJournalCursor(
                            head.Generation,
                            evt.ThreadSequenceNumber);
                    }
                }

                await context.Response.Body.FlushAsync(
                    streamCancellationToken).ConfigureAwait(false);

                var available = observation.LiveEvents.Reader
                    .WaitToReadAsync(streamCancellationToken)
                    .AsTask();
                var heartbeat = Task.Delay(
                    HeartbeatInterval,
                    streamCancellationToken);
                if (await Task.WhenAny(available, heartbeat).ConfigureAwait(false) == heartbeat)
                {
                    await context.Response.WriteAsync(
                        ": heartbeat\n\n",
                        streamCancellationToken).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(
                        streamCancellationToken).ConfigureAwait(false);
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

    private static async Task ProjectAndWriteAsync(
        HttpContext context,
        AgentSseProjector projector,
        AgentEventDelivery delivery,
        AgentEventCodec eventCodec,
        long generation,
        bool isLive,
        bool isAnchorThread,
        CancellationToken cancellationToken)
    {
        var projection = await projector(
            new AgentSseProjectionContext(
                delivery,
                eventCodec,
                generation,
                isLive,
                isAnchorThread),
            cancellationToken).ConfigureAwait(false);
        if (projection is null)
            return;

        ValidateProjection(projection);
        if (isAnchorThread &&
            delivery.Event.ThreadSequenceNumber > 0)
        {
            await context.Response.WriteAsync(
                $"id: {generation}:{delivery.Event.ThreadSequenceNumber}\n",
                cancellationToken).ConfigureAwait(false);
        }

        await context.Response.WriteAsync(
            $"event: {projection.EventName}\n",
            cancellationToken).ConfigureAwait(false);
        var normalized = projection.DataJson
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            await context.Response.WriteAsync(
                $"data: {line}\n",
                cancellationToken).ConfigureAwait(false);
        }
        await context.Response.WriteAsync(
            "\n",
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateProjection(AgentSseProjection projection)
    {
        if (string.IsNullOrWhiteSpace(projection.EventName) ||
            projection.EventName.Any(static character =>
                character is '\r' or '\n' or ':'))
        {
            throw new InvalidOperationException(
                "The projected SSE event name is invalid.");
        }
    }

    private static string SerializeDelivery(
        AgentEventCodec codec,
        AgentEventDelivery delivery)
    {
        var eventJson = codec.Serialize(delivery.Event);
        var routeJson = JsonSerializer.Serialize(
            delivery.Route,
            HPDAgentAspNetCoreJsonSerializerContext.Default.AgentEventRoute);
        return $"{{\"event\":{eventJson},\"route\":{routeJson}}}";
    }

    private static async Task WriteRebasedAsync(
        HttpContext context,
        long previousGeneration,
        long currentGeneration,
        CancellationToken cancellationToken)
    {
        await context.Response.WriteAsync(
            "event: thread-journal-rebased\n",
            cancellationToken).ConfigureAwait(false);
        await context.Response.WriteAsync(
            $"data: {{\"previousGeneration\":{previousGeneration},\"currentGeneration\":{currentGeneration}}}\n\n",
            cancellationToken).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ThreadJournalCursor ParseAppliedCursor(
        HttpRequest request,
        long currentGeneration)
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
            !long.TryParse(value.AsSpan(0, separator), out var generation) ||
            generation <= 0 ||
            !long.TryParse(value.AsSpan(separator + 1), out var sequence) ||
            sequence < 0)
        {
            throw new BadHttpRequestException(
                "The event cursor must use the generation:sequence format.");
        }

        return new ThreadJournalCursor(generation, sequence);
    }
}
