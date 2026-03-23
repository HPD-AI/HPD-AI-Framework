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
        IAsyncEnumerable<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(events);

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        await context.Response.Body.FlushAsync(cancellationToken);

        try
        {
            await foreach (var evt in events.WithCancellation(cancellationToken))
            {
                var json = AgentEventSerializer.ToJson(evt);
                var data = $"data: {json}\n\n";
                var bytes = Encoding.UTF8.GetBytes(data);

                await context.Response.Body.WriteAsync(bytes, cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
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
