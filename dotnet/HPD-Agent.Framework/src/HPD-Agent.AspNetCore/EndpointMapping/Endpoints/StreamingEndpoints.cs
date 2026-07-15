using System.Text.Json;
using HPD.Agent;
using HPD.Agent.AspNetCore.Streaming;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Streaming endpoints for real-time agent communication.
/// Uses committed SSE observation with separate lifecycle submission endpoints.
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

        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads/{bid}/context-usage",
                async (string agentId, string sid, string bid, JsonElement? request, CancellationToken ct) =>
                    await EstimateContextUsage(agentId, sid, bid, request, streaming, ct))
            .WithName("EstimateAgentThreadContextUsage")
            .WithSummary("Estimate model context usage for the current thread");

        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/threads/{bid}/state",
                async (string agentId, string sid, string bid, CancellationToken ct) =>
                    ToValueHttpResult(await streaming.GetThreadStateAsync(agentId, sid, bid, ct)))
            .WithName("GetAgentThreadRuntimeState")
            .WithSummary("Get one authoritative thread history and active-run snapshot");

        // GET /agents/{agentId}/sessions/{sid}/threads/{bid}/events/live - SSE observer
        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/threads/{bid}/events/live",
                async (string agentId, string sid, string bid, HttpContext context, CancellationToken ct) =>
                    await ObserveEventsWithSse(agentId, sid, bid, context, streaming, ct))
            .WithName("ObserveLiveEventsWithSse")
            .WithSummary("Replay and observe committed events for one thread using SSE");

        // POST /agents/{agentId}/sessions/{sid}/threads/{bid}/interrupt - Explicit runtime interruption
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads/{bid}/interrupt",
                async (string agentId, string sid, string bid, JsonElement? request, CancellationToken ct) =>
                    await Interrupt(agentId, sid, bid, request, streaming, ct))
            .WithName("InterruptAgentRun")
            .WithSummary("Explicitly interrupt active runtime work");

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

    private static async Task<IResult> EstimateContextUsage(
        string agentId,
        string sid,
        string bid,
        JsonElement? request,
        IAgentStreamingService streaming,
        CancellationToken ct = default)
    {
        var body = ParseContextUsageRequest(request);
        var result = await streaming.EstimateContextUsageAsync(agentId, sid, bid, body.RunConfig, ct);
        return ToValueHttpResult(result);
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
        var expectedRuntimeRunId = request is { ValueKind: JsonValueKind.Object } body &&
            body.TryGetProperty("expectedRuntimeRunId", out var expectedRunIdElement) &&
            expectedRunIdElement.ValueKind == JsonValueKind.String
                ? expectedRunIdElement.GetString()
                : null;
        var result = await streaming.InterruptAsync(
            agentId,
            sid,
            bid,
            expectedRuntimeRunId,
            interruption,
            ct);
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Accepted(string.Empty, result.Value),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            AgentServiceStatus.Conflict => ConflictProblem(result),
            AgentServiceStatus.ValidationError => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.ErrorCode ?? "ValidationError"] = result.ErrorMessages?.ToArray()
                    ?? [result.ErrorMessage ?? "Validation failed."]
            }),
            _ => TypedResults.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static AgentInputEvent? ParseInputEvent(JsonElement request)
    {
        if (request.ValueKind != JsonValueKind.Object)
            return null;

        var envelope = AgentEventSerializer.FromJson(request.GetRawText()) as AgentInputEvent;
        if (envelope != null)
            return envelope;
        if (TryGetPropertyIgnoreCase(request, "type", out _))
            return null;

        var textRequest = JsonSerializer.Deserialize<StreamTextRequest>(request.GetRawText(), CaseInsensitiveJson);
        return string.IsNullOrWhiteSpace(textRequest?.Text)
            ? null
            : new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, textRequest.Text)],
                RunConfig = textRequest.RunConfig,
                ClientInputId = textRequest.ClientInputId
            };
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

    private static ContextUsageRequest ParseContextUsageRequest(JsonElement? request)
    {
        if (request is { ValueKind: JsonValueKind.Object } body)
        {
            return JsonSerializer.Deserialize<ContextUsageRequest>(
                    body.GetRawText(),
                    CaseInsensitiveJson)
                ?? new ContextUsageRequest(null);
        }

        return new ContextUsageRequest(null);
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

    private static IResult ToSubmissionHttpResult(AgentServiceResult<InputSubmissionDto> result)
    {
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Accepted(string.Empty, result.Value),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            AgentServiceStatus.Conflict => ConflictProblem(result),
            AgentServiceStatus.ValidationError => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.ErrorCode ?? "ValidationError"] = result.ErrorMessages?.ToArray()
                    ?? [result.ErrorMessage ?? "Validation failed."]
            }),
            _ => TypedResults.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static IResult ToValueHttpResult<T>(AgentServiceResult<T> result)
    {
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Ok(result.Value),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            AgentServiceStatus.Conflict => ConflictProblem(result),
            AgentServiceStatus.ValidationError => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.ErrorCode ?? "ValidationError"] = result.ErrorMessages?.ToArray()
                    ?? [result.ErrorMessage ?? "Validation failed."]
            }),
            _ => TypedResults.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static IResult ConflictProblem(AgentServiceResult result)
        => Results.Json(new Dictionary<string, string[]>
        {
            [result.ErrorCode ?? "Conflict"] = result.ErrorMessages?.ToArray()
                ?? [result.ErrorMessage ?? "The requested operation conflicts with the current thread state."]
        }, statusCode: StatusCodes.Status409Conflict);

    private static IResult ConflictProblem<T>(AgentServiceResult<T> result)
        => Results.Json(new Dictionary<string, string[]>
        {
            [result.ErrorCode ?? "Conflict"] = result.ErrorMessages?.ToArray()
                ?? [result.ErrorMessage ?? "The requested operation conflicts with the current thread state."]
        }, statusCode: StatusCodes.Status409Conflict);

}
