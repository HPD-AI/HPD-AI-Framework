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
        string branchId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        await context.Response.Body.FlushAsync(cancellationToken);

        using var writeLock = new SemaphoreSlim(1, 1);
        try
        {
            using var subscription = agent.SubscribeAny((Func<AgentEvent, Task>)(async evt =>
            {
                if (!await IsInRouteScopeAsync(agent, evt, sessionId, branchId, cancellationToken).ConfigureAwait(false))
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
        string branchId,
        CancellationToken cancellationToken)
    {
        if (IsDirectRouteScope(evt, sessionId, branchId))
            return true;

        if (string.IsNullOrWhiteSpace(evt.SessionId) || string.IsNullOrWhiteSpace(evt.BranchId))
            return false;

        return await IsSubAgentChildOfRouteAsync(
            agent,
            evt.SessionId!,
            evt.BranchId!,
            sessionId,
            branchId,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsDirectRouteScope(AgentEvent evt, string sessionId, string branchId)
    {
        if (!string.IsNullOrWhiteSpace(evt.SessionId) && evt.SessionId != sessionId)
            return false;

        return string.IsNullOrWhiteSpace(evt.BranchId) || evt.BranchId == branchId;
    }

    private static async Task<bool> IsSubAgentChildOfRouteAsync(
        Agent agent,
        string eventSessionId,
        string eventBranchId,
        string routeSessionId,
        string routeBranchId,
        CancellationToken cancellationToken)
    {
        var store = agent.Config?.SessionStore;
        if (store == null)
            return false;

        var branch = await store.LoadBranchAsync(eventSessionId, eventBranchId, cancellationToken)
            .ConfigureAwait(false);
        if (branch == null)
            return false;

        return IsMetadataValue(branch.Metadata, "kind", "subagent") &&
            IsMetadataValue(branch.Metadata, "parentSessionId", routeSessionId) &&
            IsMetadataValue(branch.Metadata, "parentBranchId", routeBranchId);
    }

    private static bool IsMetadataValue(
        IReadOnlyDictionary<string, object> metadata,
        string key,
        string expected)
    {
        return metadata.TryGetValue(key, out var value) &&
            string.Equals(Convert.ToString(value), expected, StringComparison.Ordinal);
    }
}
