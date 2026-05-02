using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.AspNetCore.Streaming;
using HPD.Agent.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Streaming endpoints for real-time agent communication.
/// Supports both SSE (Server-Sent Events) and WebSocket protocols.
/// </summary>
internal static class StreamingEndpoints
{
    /// <summary>
    /// Maps all streaming-related endpoints.
    /// </summary>
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager)
    {
        // POST /sessions/{sid}/branches/{bid}/stream - SSE streaming
        endpoints.MapPost("/sessions/{sid}/branches/{bid}/stream",
                async (string sid, string bid, JsonElement request, HttpContext context, CancellationToken ct) =>
                    await StreamWithSse(sid, bid, request, context, sessionManager, agentManager, ct))
            .WithName("StreamWithSse")
            .WithSummary("Stream agent events using Server-Sent Events (SSE)");

        // GET /sessions/{sid}/branches/{bid}/ws - WebSocket streaming
        endpoints.MapGet("/sessions/{sid}/branches/{bid}/ws", (string sid, string bid, HttpContext context, CancellationToken ct) =>
                StreamWithWebSocket(sid, bid, context, sessionManager, agentManager, ct))
            .WithName("StreamWithWebSocket")
            .WithSummary("Stream agent responses using WebSocket");
    }

    private static async Task StreamWithSse(
        string sid,
        string bid,
        JsonElement request,
        HttpContext context,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        // Validate session and branch exist BEFORE starting stream
        var session = await sessionManager.Store.LoadSessionAsync(sid, ct);
        if (session == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var branch = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
        if (branch == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Try to acquire stream lock (prevents concurrent streams on same branch)
        if (!sessionManager.TryAcquireStreamLock(sid, bid))
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        // Register cleanup callback for request cancellation
        // This ensures lock is released even if request is aborted (critical for TestServer scenarios)
        using var _ = context.RequestAborted.Register(() =>
        {
            sessionManager.ReleaseStreamLock(sid, bid);
        });

        try
        {
            var input = ParseInputEvent(request);
            if (input == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            input = ApplyRouteScope(input, sid, bid);

            // Get or build the agent (keyed by event AgentId, defaults to "default")
            var agentId = input is AgentInputEvent agentInput
                ? agentInput.AgentId ?? "default"
                : "default";
            var agent = await agentManager.GetOrBuildAgentAsync(agentId, ct);

            // Stream events using SSE - this sends headers and starts streaming
            try
            {
                await SseEventHandler.StreamEventsAsync(context, agent, input, ct);
            }
            catch (OperationCanceledException)
            {
                // Gracefully handle cancellation - lock will be released via registered callback
                // Don't rethrow since we can't return a proper response after SSE headers sent
            }
            catch
            {
                // SseEventHandler already sent a MessageTurnErrorEvent — swallow here so
                // Kestrel doesn't log an unhandled exception and close the connection abruptly.
            }
        }
        finally
        {
            sessionManager.ReleaseStreamLock(sid, bid);
        }
    }

    private static async Task<Results<Ok, NotFound, Conflict, ValidationProblem>> StreamWithWebSocket(
        string sid,
        string bid,
        HttpContext context,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        // Validate WebSocket request
        if (!context.WebSockets.IsWebSocketRequest)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["InvalidRequest"] = ["This endpoint requires a WebSocket connection."]
            });
        }

        // Validate session and branch exist BEFORE accepting WebSocket
        var session = await sessionManager.Store.LoadSessionAsync(sid, ct);
        if (session == null)
        {
            return TypedResults.NotFound();
        }

        var branch = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
        if (branch == null)
        {
            return TypedResults.NotFound();
        }

        // Try to acquire stream lock
        if (!sessionManager.TryAcquireStreamLock(sid, bid))
        {
            return TypedResults.Conflict();
        }

        try
        {
            // Check cancellation before accepting connection
            ct.ThrowIfCancellationRequested();

            // Accept WebSocket connection with cancellation support
            // AcceptWebSocketAsync doesn't take a CT, so we need to handle cancellation manually
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var acceptTask = context.WebSockets.AcceptWebSocketAsync();
            var delayTask = Task.Delay(Timeout.Infinite, linkedCts.Token);
            var completedTask = await Task.WhenAny(acceptTask, delayTask);

            if (completedTask == delayTask)
            {
                // Cancellation was requested before WebSocket was accepted
                ct.ThrowIfCancellationRequested();
            }

            using var webSocket = await acceptTask;

            Agent? agent = null;
            IDisposable? subscription = null;
            var runtimeStarted = false;

            try
            {
                var buffer = new byte[1024 * 4];
                while (!ct.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    var json = await ReceiveTextMessageAsync(webSocket, buffer, ct);
                    if (json == null)
                    {
                        break;
                    }

                    var input = AgentEventSerializer.FromJson(json);
                    if (input == null)
                    {
                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.InvalidPayloadData,
                            "Invalid agent event envelope",
                            ct);
                        return TypedResults.Ok();
                    }

                    input = ApplyRouteScope(input, sid, bid);
                    if (agent == null)
                    {
                        var agentId = input is AgentInputEvent agentInput
                            ? agentInput.AgentId ?? "default"
                            : "default";

                        agent = await agentManager.GetOrBuildAgentAsync(agentId, ct);
                        subscription = agent.SubscribeAny((Func<AgentEvent, Task>)(async evt =>
                        {
                            var eventJson = AgentEventSerializer.ToJson(evt);
                            var bytes = Encoding.UTF8.GetBytes(eventJson);

                            await webSocket.SendAsync(
                                new ArraySegment<byte>(bytes),
                                WebSocketMessageType.Text,
                                endOfMessage: true,
                                ct);
                        }));
                    }

                    if (!runtimeStarted)
                    {
                        await agent.StartAsync(ct);
                        runtimeStarted = true;
                    }

                    await agent.RunAsync(input, ct);
                }
            }
            finally
            {
                subscription?.Dispose();

                if (runtimeStarted)
                {
                    await agent!.StopAsync(CancellationToken.None);
                }
            }

            // Close WebSocket gracefully
            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Stream completed",
                    ct);
            }

            return TypedResults.Ok();
        }
        finally
        {
            sessionManager.ReleaseStreamLock(sid, bid);
        }
    }

    private static AgentEvent? ParseInputEvent(JsonElement request)
    {
        return AgentEventSerializer.FromJson(request.GetRawText());
    }

    private static AgentEvent ApplyRouteScope(AgentEvent input, string sid, string bid)
    {
        return input switch
        {
            UserTextInputEvent text => text with
            {
                SessionId = text.SessionId ?? sid,
                BranchId = text.BranchId ?? bid
            },
            UserMessagesInputEvent messages => messages with
            {
                SessionId = messages.SessionId ?? sid,
                BranchId = messages.BranchId ?? bid
            },
            _ => input
        };
    }

    private static async Task<string?> ReceiveTextMessageAsync(
        WebSocket webSocket,
        byte[] buffer,
        CancellationToken ct)
    {
        using var payload = new MemoryStream();

        while (true)
        {
            var receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                ct);

            if (receiveResult.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (receiveResult.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidOperationException("Only text agent event envelopes are supported.");
            }

            payload.Write(buffer, 0, receiveResult.Count);

            if (receiveResult.EndOfMessage)
            {
                return Encoding.UTF8.GetString(payload.ToArray());
            }
        }
    }
}
