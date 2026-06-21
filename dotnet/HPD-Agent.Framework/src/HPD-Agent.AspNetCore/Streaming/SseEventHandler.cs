using HPD.Agent;
using HPD.Agent.Serialization;
using Microsoft.AspNetCore.Http;
using System.Text;

namespace HPD.Agent.AspNetCore.Streaming;

/// <summary>
/// Handles SSE (Server-Sent Events) streaming for agent events.
/// </summary>
internal static class SseEventHandler
{
    /// <summary>
    /// Stream agent events as SSE to the HTTP response.
    /// </summary>
    public static async Task StreamEventsAsync(
        HttpContext context,
        Agent agent,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        await context.Response.Body.FlushAsync(cancellationToken);

        using var writeLock = new SemaphoreSlim(1, 1);
        try
        {
            using var subscription = agent.SubscribeAny((Func<AgentEvent, Task>)(async evt =>
            {
                if (!await IsInRouteScopeAsync(agent, evt, sessionId, threadId, cancellationToken).ConfigureAwait(false))
                    return;

                var json = AgentEventSerializer.ToJson(evt);
                var data = $"data: {json}\n\n";
                var bytes = Encoding.UTF8.GetBytes(data);

                await writeLock.WaitAsync(cancellationToken);
                try
                {
                    await context.Response.Body.WriteAsync(bytes, cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
                finally
                {
                    writeLock.Release();
                }
            }));

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected or request cancelled — exit cleanly, no error event needed.
        }
        catch (Exception ex)
        {
            // SSE headers already sent — cannot return a 5xx response.
            // Serialize the error as a MessageTurnErrorEvent so the client renders it gracefully.
            try
            {
                var errorEvt = new MessageTurnErrorEvent(ex.Message, ex);
                var json = AgentEventSerializer.ToJson(errorEvt);
                var data = $"data: {json}\n\n";
                var bytes = Encoding.UTF8.GetBytes(data);
                await context.Response.Body.WriteAsync(bytes, CancellationToken.None);
                await context.Response.Body.FlushAsync(CancellationToken.None);
            }
            catch
            {
                // Response stream already closed — nothing we can do.
            }
        }
    }

    internal static async Task<bool> IsInRouteScopeAsync(
        Agent agent,
        AgentEvent evt,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken)
    {
        if (IsDirectRouteScope(evt, sessionId, threadId))
            return true;

        // Root runtime events often stream before durable SessionId/ThreadId scope is attached.
        // The SSE subscription is already bound to one thread-owned runtime instance, so
        // scope-less events from that runtime should still flow to the connected client.
        if (string.IsNullOrWhiteSpace(evt.SessionId) && string.IsNullOrWhiteSpace(evt.ThreadId))
            return true;

        if (string.IsNullOrWhiteSpace(evt.SessionId) || string.IsNullOrWhiteSpace(evt.ThreadId))
            return false;

        return await IsSubAgentChildOfRouteAsync(
            agent,
            evt.SessionId!,
            evt.ThreadId!,
            sessionId,
            threadId,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsDirectRouteScope(AgentEvent evt, string sessionId, string threadId)
    {
        if (!string.IsNullOrWhiteSpace(evt.SessionId) && evt.SessionId != sessionId)
            return false;

        return string.IsNullOrWhiteSpace(evt.ThreadId) || evt.ThreadId == threadId;
    }

    private static async Task<bool> IsSubAgentChildOfRouteAsync(
        Agent agent,
        string eventSessionId,
        string eventThreadId,
        string routeSessionId,
        string routeThreadId,
        CancellationToken cancellationToken)
    {
        var store = agent.Config?.SessionStore;
        if (store == null)
            return false;

        var thread = await store.LoadThreadAsync(eventSessionId, eventThreadId, cancellationToken)
            .ConfigureAwait(false);
        if (thread == null)
            return false;

        return thread.Kind == ThreadKind.SubAgent &&
            string.Equals(thread.ParentSessionId, routeSessionId, StringComparison.Ordinal) &&
            string.Equals(thread.ParentThreadId, routeThreadId, StringComparison.Ordinal);
    }
}
