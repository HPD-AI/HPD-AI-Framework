using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.AspNetCore.Streaming;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Extensions;
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
        endpoints.MapPost("/sessions/{sid}/branches/{bid}/stream", async (string sid, string bid, StreamRequest request, HttpContext context, CancellationToken ct) =>
        {
            // Validate session and branch exist BEFORE starting stream
            var session = await sessionManager.Store.LoadSessionAsync(sid, ct);
            if (session == null)
            {
                return ErrorResponses.NotFound();
            }

            var branch = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
            if (branch == null)
            {
                return ErrorResponses.NotFound();
            }

            // Try to acquire stream lock (prevents concurrent streams on same branch)
            if (!sessionManager.TryAcquireStreamLock(sid, bid))
            {
                return ErrorResponses.Conflict();
            }

            // Register cleanup callback for request cancellation
            // This ensures lock is released even if request is aborted (critical for TestServer scenarios)
            using var _ = context.RequestAborted.Register(() =>
            {
                sessionManager.ReleaseStreamLock(sid, bid);
            });

            try
            {
                // Get or build the agent (keyed by agentId, defaults to "default")
                var agentId = request.AgentId ?? "default";
                var agent = await agentManager.GetOrBuildAgentAsync(agentId, ct);

                // Extract user message from request
                string userMessage = "";
                if (request.Messages != null && request.Messages.Count > 0)
                {
                    userMessage = string.Join("\n", request.Messages.Select(m => m.Content));
                }

                // Build run configuration from request
                var runConfig = BuildRunConfig(request.RunConfig);
                runConfig.ClientToolInput = request.ToAgentClientInput();

                // Stream events using SSE - this sends headers and starts streaming
                // After this call, we cannot return a typed result
                try
                {
                    var events = agent.RunAsync(userMessage, sid, bid, options: runConfig, cancellationToken: ct);
                    await SseEventHandler.StreamEventsAsync(context, events, ct);
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

                // Response is complete - return Empty to indicate no further action needed
                return Results.Empty;
            }
            finally
            {
                sessionManager.ReleaseStreamLock(sid, bid);
            }
        })
            .WithName("StreamWithSse")
            .WithSummary("Stream agent responses using Server-Sent Events (SSE)");

        // GET /sessions/{sid}/branches/{bid}/ws - WebSocket streaming
        endpoints.MapGet("/sessions/{sid}/branches/{bid}/ws", (string sid, string bid, HttpContext context, CancellationToken ct) =>
                StreamWithWebSocket(sid, bid, context, sessionManager, agentManager, ct))
            .WithName("StreamWithWebSocket")
            .WithSummary("Stream agent responses using WebSocket");
    }

    private static async Task<IResult> StreamWithWebSocket(
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
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["InvalidRequest"] = ["This endpoint requires a WebSocket connection."]
            });
        }

        // Validate session and branch exist BEFORE accepting WebSocket
        var session = await sessionManager.Store.LoadSessionAsync(sid, ct);
        if (session == null)
        {
            return ErrorResponses.NotFound();
        }

        var branch = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
        if (branch == null)
        {
            return ErrorResponses.NotFound();
        }

        // Try to acquire stream lock
        if (!sessionManager.TryAcquireStreamLock(sid, bid))
        {
            return ErrorResponses.Conflict();
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

            // Receive initial message from client
            var buffer = new byte[1024 * 4];
            var receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), ct);

            if (receiveResult.MessageType == WebSocketMessageType.Close)
            {
                // Release the lock before echoing the close frame so a subsequent
                // connection attempt on the same branch is not rejected with 409.
                sessionManager.ReleaseStreamLock(sid, bid);
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client closed connection",
                    ct);
                return Results.Ok();
            }

            // Parse stream request
            var json = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
            var request = JsonSerializer.Deserialize<StreamRequest>(json);

            if (request == null)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.InvalidPayloadData,
                    "Invalid request format",
                    ct);
                return Results.Ok(); // Can't return error after WS accepted
            }

            // Extract user message from request
            string userMessage = "";
            if (request.Messages != null && request.Messages.Count > 0)
            {
                userMessage = string.Join("\n", request.Messages.Select(m => m.Content));
            }

            // Build run configuration
            var runConfig = BuildRunConfig(request.RunConfig);
            runConfig.ClientToolInput = request.ToAgentClientInput();

            // Get or build the agent (keyed by agentId, defaults to "default")
            var agentId = request.AgentId ?? "default";
            var agent = await agentManager.GetOrBuildAgentAsync(agentId, ct);

            // Stream events via WebSocket with ID-based API
            var events = agent.RunAsync(userMessage, sid, bid, options: runConfig, cancellationToken: ct);
            await foreach (var evt in events.WithCancellation(ct))
            {
                var eventJson = AgentEventSerializer.ToJson(evt);
                var bytes = Encoding.UTF8.GetBytes(eventJson);

                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct);
            }

            // Close WebSocket gracefully
            await webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Stream completed",
                ct);

            return Results.Ok();
        }
        finally
        {
            sessionManager.ReleaseStreamLock(sid, bid);
        }
    }

    private static AgentRunConfig BuildRunConfig(StreamRunConfigDto? dto)
    {
        if (dto == null)
        {
            return new AgentRunConfig();
        }

        var config = new AgentRunConfig();

        // Apply chat options if provided
        if (dto.Chat != null)
        {
            config.Chat = new ChatRunConfig
            {
                Temperature = dto.Chat.Temperature,
                MaxOutputTokens = dto.Chat.MaxOutputTokens,
                TopP = dto.Chat.TopP,
                FrequencyPenalty = dto.Chat.FrequencyPenalty,
                PresencePenalty = dto.Chat.PresencePenalty
            };
        }

        // Apply provider and model overrides
        if (!string.IsNullOrEmpty(dto.ProviderKey))
        {
            config.ProviderKey = dto.ProviderKey;
        }

        if (!string.IsNullOrEmpty(dto.ModelId))
        {
            config.ModelId = dto.ModelId;
        }

        // Apply additional system instructions
        if (!string.IsNullOrEmpty(dto.AdditionalSystemInstructions))
        {
            config.AdditionalSystemInstructions = dto.AdditionalSystemInstructions;
        }

        // Apply context overrides
        if (dto.ContextOverrides != null)
        {
            config.ContextOverrides = dto.ContextOverrides;
        }

        // Apply permission overrides
        if (dto.PermissionOverrides != null)
        {
            config.PermissionOverrides = dto.PermissionOverrides;
        }

        // Apply coalesce deltas
        if (dto.CoalesceDeltas.HasValue)
        {
            config.CoalesceDeltas = dto.CoalesceDeltas.Value;
        }

        // Apply skip tools
        if (dto.SkipTools.HasValue)
        {
            config.SkipTools = dto.SkipTools.Value;
        }

        // Apply run timeout
        if (!string.IsNullOrEmpty(dto.RunTimeout))
        {
            if (TimeSpan.TryParse(dto.RunTimeout, out var timeout))
            {
                config.RunTimeout = timeout;
            }
        }

        return config;
    }
}
