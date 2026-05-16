using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.AspNetCore.Streaming;
using HPD.Agent.Hosting.Data;
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
        // POST /agents/{agentId}/sessions/{sid}/branches/{bid}/stream - SSE text streaming
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches/{bid}/stream",
                async (string agentId, string sid, string bid, StreamTextRequest request, HttpContext context, CancellationToken ct) =>
                    await StreamTextWithSse(agentId, sid, bid, request, context, sessionManager, agentManager, ct))
            .WithName("StreamWithSse")
            .WithSummary("Stream agent events using Server-Sent Events (SSE)");

        // POST /agents/{agentId}/sessions/{sid}/branches/{bid}/events/stream - SSE raw event streaming
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches/{bid}/events/stream",
                async (string agentId, string sid, string bid, JsonElement request, HttpContext context, CancellationToken ct) =>
                    await StreamEventWithSse(agentId, sid, bid, request, context, sessionManager, agentManager, ct))
            .WithName("StreamRawEventWithSse")
            .WithSummary("Stream agent events using Server-Sent Events (SSE) from a raw event envelope");

        // GET /agents/{agentId}/sessions/{sid}/branches/{bid}/ws - WebSocket streaming
        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/branches/{bid}/ws", (string agentId, string sid, string bid, HttpContext context, CancellationToken ct) =>
                StreamWithWebSocket(agentId, sid, bid, context, sessionManager, agentManager, ct))
            .WithName("StreamWithWebSocket")
            .WithSummary("Stream agent responses using WebSocket");
    }

    private static async Task StreamTextWithSse(
        string agentId,
        string sid,
        string bid,
        StreamTextRequest request,
        HttpContext context,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var input = new UserTextInputEvent(request.Text)
        {
            RunConfig = request.RunConfig
        };

        await StreamInputWithSse(agentId, sid, bid, input, context, sessionManager, agentManager, ct);
    }

    private static async Task StreamEventWithSse(
        string agentId,
        string sid,
        string bid,
        JsonElement request,
        HttpContext context,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        var input = ParseInputEvent(request);
        if (input == null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await StreamInputWithSse(agentId, sid, bid, input, context, sessionManager, agentManager, ct);
    }

    private static async Task StreamInputWithSse(
        string agentId,
        string sid,
        string bid,
        AgentInputEvent input,
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
            input = ApplyRouteScope(input, agentId, sid, bid);
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
        string agentId,
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

            var agent = await agentManager.GetOrBuildAgentAsync(agentId, ct);
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

                    var evt = AgentEventSerializer.FromJson(json);
                    var input = evt as AgentInputEvent;
                    var response = evt as HPD.Events.IBidirectionalEvent;
                    if (input is null && response is null)
                    {
                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.InvalidPayloadData,
                            "Invalid agent input or response event envelope",
                            ct);
                        return TypedResults.Ok();
                    }

                    if (subscription == null)
                    {
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

                    if (input is not null)
                    {
                        input = ApplyRouteScope(input, agentId, sid, bid);
                        await agent.RunAsync(input, ct);
                    }
                    else
                    {
                        await agent.TryRespondAsync(response!, ct);
                    }
                }
            }
            finally
            {
                subscription?.Dispose();

                if (runtimeStarted)
                {
                    await agent.StopAsync(CancellationToken.None);
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

    private static AgentInputEvent? ParseInputEvent(JsonElement request)
    {
        return AgentEventSerializer.FromJson(request.GetRawText()) as AgentInputEvent;
    }

    private static AgentInputEvent ApplyRouteScope(AgentInputEvent input, string agentId, string sid, string bid)
    {
        return input switch
        {
            UserTextInputEvent text => text with
            {
                AgentId = agentId,
                SessionId = sid,
                BranchId = bid
            },
            UserMessagesInputEvent messages => messages with
            {
                AgentId = agentId,
                SessionId = sid,
                BranchId = bid
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
