using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.AspNetCore.Streaming;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
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
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Maps all streaming-related endpoints.
    /// </summary>
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        IAgentStreamingService streaming)
    {
        // POST /agents/{agentId}/sessions/{sid}/threads/{bid}/inputs - Submit runtime-owned input
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads/{bid}/inputs",
                async (string agentId, string sid, string bid, JsonElement request, CancellationToken ct) =>
                    await SubmitInput(agentId, sid, bid, request, streaming, ct))
            .WithName("SubmitAgentInput")
            .WithSummary("Submit an agent input event to the runtime");

        // GET /agents/{agentId}/sessions/{sid}/threads/{bid}/events/live - SSE observer
        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/threads/{bid}/events/live",
                async (string agentId, string sid, string bid, HttpContext context, CancellationToken ct) =>
                    await ObserveEventsWithSse(agentId, sid, bid, context, streaming, ct))
            .WithName("ObserveLiveEventsWithSse")
            .WithSummary("Observe live agent events using Server-Sent Events (SSE)");

        // POST /agents/{agentId}/sessions/{sid}/threads/{bid}/interrupt - Explicit runtime interruption
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads/{bid}/interrupt",
                async (string agentId, string sid, string bid, JsonElement? request, CancellationToken ct) =>
                    await Interrupt(agentId, sid, bid, request, streaming, ct))
            .WithName("InterruptAgentRun")
            .WithSummary("Explicitly interrupt active runtime work");

        // GET /agents/{agentId}/sessions/{sid}/threads/{bid}/ws - WebSocket streaming
        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/threads/{bid}/ws", (string agentId, string sid, string bid, HttpContext context, CancellationToken ct) =>
                StreamWithWebSocket(agentId, sid, bid, context, streaming, ct))
            .WithName("StreamWithWebSocket")
            .WithSummary("Stream agent responses using WebSocket");
    }

    private static async Task<IResult> SubmitInput(
        string agentId,
        string sid,
        string bid,
        JsonElement request,
        IAgentStreamingService streaming,
        CancellationToken ct = default)
    {
        var input = ParseInputEvent(request);
        if (input == null)
            return TypedResults.BadRequest();

        var result = await streaming.SubmitInputAsync(agentId, sid, bid, input, ct);
        return ToSubmissionHttpResult(result);
    }

    private static async Task<IResult> ObserveEventsWithSse(
        string agentId,
        string sid,
        string bid,
        HttpContext context,
        IAgentStreamingService streaming,
        CancellationToken ct = default)
    {
        var leaseResult = await streaming.GetAgentForThreadAsync(agentId, sid, bid, ct);
        if (leaseResult.Status == AgentServiceStatus.NotFound)
            return TypedResults.NotFound();

        await SseEventHandler.StreamEventsAsync(context, leaseResult.Value!.Agent, sid, bid, ct);
        return TypedResults.Empty;
    }

    private static async Task<IResult> Interrupt(
        string agentId,
        string sid,
        string bid,
        JsonElement? request,
        IAgentStreamingService streaming,
        CancellationToken ct = default)
    {
        var interruption = ParseInterruptionRequest(request);
        var result = await streaming.InterruptAsync(agentId, sid, bid, interruption, ct);
        return ToEmptyHttpResult(result, accepted: true);
    }

    private static async Task<Results<Ok, NotFound, Conflict, ValidationProblem>> StreamWithWebSocket(
        string agentId,
        string sid,
        string bid,
        HttpContext context,
        IAgentStreamingService streaming,
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

        var leaseResult = await streaming.GetAgentForThreadAsync(agentId, sid, bid, ct);
        if (leaseResult.Status == AgentServiceStatus.NotFound)
            return TypedResults.NotFound();
        if (leaseResult.Status == AgentServiceStatus.Conflict)
            return TypedResults.Conflict();

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

        var agent = leaseResult.Value!.Agent;
        IDisposable? subscription = null;

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
                var response = evt as HPD.Events.IResponseEvent;
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

                if (input is not null)
                {
                    var submitStatus = AgentServiceStatus.Success;
                    string? submitErrorCode = null;

                    if (input is InterruptionRequestEvent interruption)
                    {
                        var interruptResult = await streaming.InterruptAsync(agentId, sid, bid, interruption, ct);
                        submitStatus = interruptResult.Status;
                        submitErrorCode = interruptResult.ErrorCode;
                    }
                    else
                    {
                        var inputResult = await streaming.SubmitInputAsync(agentId, sid, bid, input, ct);
                        submitStatus = inputResult.Status;
                        submitErrorCode = inputResult.ErrorCode;
                    }

                    if (submitStatus == AgentServiceStatus.Conflict)
                    {
                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.PolicyViolation,
                            submitErrorCode ?? "Thread run conflict",
                            ct);
                        return TypedResults.Ok();
                    }

                    if (submitStatus == AgentServiceStatus.NotFound)
                    {
                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.InvalidPayloadData,
                            "Session or thread not found",
                            ct);
                        return TypedResults.Ok();
                    }
                }
                else
                {
                    var respondResult = await agent.RespondIfPendingAsync(response!, ct);
                    if (!respondResult.Accepted)
                    {
                        var resultJson = JsonSerializer.Serialize(respondResult, CaseInsensitiveJson);
                        var bytes = Encoding.UTF8.GetBytes(resultJson);

                        await webSocket.SendAsync(
                            new ArraySegment<byte>(bytes),
                            WebSocketMessageType.Text,
                            endOfMessage: true,
                            ct);
                    }
                }
            }
        }
        finally
        {
            subscription?.Dispose();
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

    private static AgentInputEvent? ParseInputEvent(JsonElement request)
    {
        if (request.ValueKind != JsonValueKind.Object)
            return null;

        return AgentEventSerializer.FromJson(request.GetRawText()) as AgentInputEvent;
    }

    private static InterruptionRequestEvent ParseInterruptionRequest(JsonElement? request)
    {
        if (request is { ValueKind: JsonValueKind.Object } body)
        {
            var evt = AgentEventSerializer.FromJson(body.GetRawText()) as InterruptionRequestEvent;
            if (evt != null)
                return evt;

            var reason = TryGetPropertyIgnoreCase(body, "reason", out var reasonElement)
                ? reasonElement.GetString()
                : null;
            var eventFlowId = TryGetPropertyIgnoreCase(body, "eventFlowId", out var eventFlowElement)
                ? eventFlowElement.GetString()
                : null;

            return new InterruptionRequestEvent(
                eventFlowId,
                string.IsNullOrWhiteSpace(reason) ? "Interrupted by client." : reason,
                InterruptionSource.User);
        }

        return new InterruptionRequestEvent(null, "Interrupted by client.", InterruptionSource.User);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static IResult ToEmptyHttpResult(AgentServiceResult result, bool accepted = false)
    {
        return result.Status switch
        {
            AgentServiceStatus.Success when accepted => TypedResults.Accepted(string.Empty),
            AgentServiceStatus.Success => TypedResults.Ok(),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            AgentServiceStatus.Conflict => TypedResults.Conflict(),
            AgentServiceStatus.ValidationError => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.ErrorCode ?? "ValidationError"] = result.ErrorMessages?.ToArray()
                    ?? [result.ErrorMessage ?? "Validation failed."]
            }),
            _ => TypedResults.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static IResult ToSubmissionHttpResult(AgentServiceResult<InputSubmissionDto> result)
    {
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Accepted(string.Empty, result.Value),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            AgentServiceStatus.Conflict => TypedResults.Conflict(),
            AgentServiceStatus.ValidationError => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.ErrorCode ?? "ValidationError"] = result.ErrorMessages?.ToArray()
                    ?? [result.ErrorMessage ?? "Validation failed."]
            }),
            _ => TypedResults.StatusCode(StatusCodes.Status500InternalServerError)
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
