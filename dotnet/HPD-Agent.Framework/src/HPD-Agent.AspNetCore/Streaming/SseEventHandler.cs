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

        var cursor = context.Request.Query.TryGetValue("after", out var after) &&
            long.TryParse(after.ToString(), out var parsedAfter) && parsedAfter >= 0
                ? parsedAfter
                : 0;
        var store = agent.Config?.SessionStore
            ?? throw new InvalidOperationException("Agent live observation requires a session store.");
        var lastWrite = DateTimeOffset.UtcNow;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var document = await store.LoadThreadDocumentAsync(sessionId, threadId, cancellationToken)
                    .ConfigureAwait(false);
                if (document is not null)
                {
                    foreach (var evt in document.Events
                        .Where(evt => evt.SequenceNumber > cursor)
                        .OrderBy(evt => evt.SequenceNumber))
                    {
                        var json = AgentEventSerializer.ToJson(evt);
                        await context.Response.WriteAsync($"id: {evt.SequenceNumber}\n", cancellationToken)
                            .ConfigureAwait(false);
                        await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken)
                            .ConfigureAwait(false);
                        cursor = evt.SequenceNumber;
                        lastWrite = DateTimeOffset.UtcNow;
                    }
                }

                if (DateTimeOffset.UtcNow - lastWrite >= TimeSpan.FromSeconds(15))
                {
                    await context.Response.WriteAsync(": heartbeat\n\n", cancellationToken)
                        .ConfigureAwait(false);
                    lastWrite = DateTimeOffset.UtcNow;
                }

                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }
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

}
