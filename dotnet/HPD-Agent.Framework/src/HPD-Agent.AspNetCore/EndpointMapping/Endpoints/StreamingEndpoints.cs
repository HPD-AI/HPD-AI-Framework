using System.Text.Json;
using HPD.Agent;
using HPD.Agent.AspNetCore.Streaming;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Serialization;
using HPD.Agent.Providers;
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
        IAgentStreamingService streaming,
        AgentInputCodec inputCodec)
    {
        // POST /agents/{agentId}/sessions/{sid}/threads/{bid}/inputs - Submit runtime-owned input
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads/{bid}/inputs",
                async (string agentId, string sid, string bid, JsonElement request, CancellationToken ct) =>
                    await SubmitInput(RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid), request, streaming, inputCodec, ct))
            .WithName("SubmitAgentInput")
            .WithSummary("Submit an agent input event to the runtime");

        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads/{bid}/subagents/{localId}/inputs",
                async (string agentId, string sid, string bid, string localId, string childAgentId,
                    string childSessionId, string childThreadId, JsonElement request, CancellationToken ct) =>
                    await SubmitSubAgentInput(
                        RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid),
                        RouteValue.Decode(localId), RouteValue.Decode(childAgentId),
                        RouteValue.Decode(childSessionId), RouteValue.Decode(childThreadId),
                        request, streaming, inputCodec, ct))
            .WithName("SubmitControlledSubAgentInput")
            .WithSummary("Submit input to a child through its durable controller registry entry");

        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads/{bid}/context-usage",
                async (string agentId, string sid, string bid, JsonElement? request, CancellationToken ct) =>
                    await EstimateContextUsage(RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid), request, streaming, ct))
            .WithName("EstimateAgentThreadContextUsage")
            .WithSummary("Estimate model context usage for the current thread");

        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/threads/{bid}/state",
                async (string agentId, string sid, string bid, CancellationToken ct) =>
                    ToValueHttpResult(await streaming.GetThreadStateAsync(RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid), ct)))
            .WithName("GetAgentThreadRuntimeState")
            .WithSummary("Get one authoritative thread history and active-run snapshot");

        // One stream performs finite catch-up and remains attached for future commits.
        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/threads/{bid}/events",
                async (string agentId, string sid, string bid, HttpContext context, CancellationToken ct) =>
                    await ObserveEventsWithSse(RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid), context, streaming, ct))
            .WithName("ObserveLiveEventsWithSse")
            .WithSummary("Replay durable thread events and observe all live runtime events using SSE");

    }

    private static async Task<IResult> SubmitInput(
        string agentId,
        string sid,
        string bid,
        JsonElement request,
        IAgentStreamingService streaming,
        AgentInputCodec inputCodec,
        CancellationToken ct = default)
    {
        var input = ParseInputEvent(request, inputCodec);
        if (input == null)
            return TypedResults.BadRequest();

        var result = await streaming.SubmitInputAsync(agentId, sid, bid, input, ct);
        return ToSubmissionHttpResult(result);
    }

    private static async Task<IResult> SubmitSubAgentInput(
        string controllerAgentId,
        string controllerSessionId,
        string controllerThreadId,
        string localId,
        string childAgentId,
        string childSessionId,
        string childThreadId,
        JsonElement request,
        IAgentStreamingService streaming,
        AgentInputCodec inputCodec,
        CancellationToken ct)
    {
        var input = ParseInputEvent(request, inputCodec);
        if (input is null)
            return TypedResults.BadRequest();
        var result = await streaming.SubmitSubAgentInputAsync(
            controllerAgentId,
            controllerSessionId,
            controllerThreadId,
            new SubAgentLocalId(localId),
            childAgentId,
            childSessionId,
            childThreadId,
            input,
            ct).ConfigureAwait(false);
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
        if (!TryParseHierarchy(context.Request.Query["hierarchy"].ToString(), out var hierarchy))
            return TypedResults.BadRequest();

        var leaseResult = await streaming.ObserveThreadEventsAsync(agentId, new ThreadKey(sid, bid), hierarchy, ct);
        if (leaseResult.Status == AgentServiceStatus.NotFound)
            return TypedResults.NotFound();

        await using var observation = leaseResult.Value!;
        await SseEventHandler.StreamEventsAsync(context, observation, ct);
        return TypedResults.Empty;
    }

    private static bool TryParseHierarchy(string value, out AgentEventHierarchy hierarchy)
    {
        if (value.Length == 0 || value.Equals("exactThread", StringComparison.OrdinalIgnoreCase))
            hierarchy = AgentEventHierarchy.ExactThread;
        else if (value.Equals("directChildren", StringComparison.OrdinalIgnoreCase))
            hierarchy = AgentEventHierarchy.DirectChildren;
        else if (value.Equals("threadAndDirectChildren", StringComparison.OrdinalIgnoreCase))
            hierarchy = AgentEventHierarchy.ThreadAndDirectChildren;
        else if (value.Equals("descendants", StringComparison.OrdinalIgnoreCase))
            hierarchy = AgentEventHierarchy.Descendants;
        else if (value.Equals("threadAndDescendants", StringComparison.OrdinalIgnoreCase))
            hierarchy = AgentEventHierarchy.ThreadAndDescendants;
        else
        {
            hierarchy = default;
            return false;
        }
        return true;
    }

    private static AgentInputEvent? ParseInputEvent(
        JsonElement request,
        AgentInputCodec inputCodec)
    {
        if (request.ValueKind != JsonValueKind.Object)
            return null;

        try
        {
            return inputCodec.Deserialize(request.GetRawText());
        }
        catch (JsonException)
        {
            // A typed but unknown/malformed envelope is an invalid request. An
            // untyped object may still be the text convenience shape below.
        }
        if (TryGetPropertyIgnoreCase(request, "type", out _))
            return null;

        var text = TryGetPropertyIgnoreCase(request, "text", out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString()
            : null;
        var clientInputId = TryGetPropertyIgnoreCase(request, "clientInputId", out var clientInputElement) && clientInputElement.ValueKind == JsonValueKind.String
            ? clientInputElement.GetString()
            : null;
        AgentRunConfig? runConfig = null;
        if (TryGetPropertyIgnoreCase(request, "runConfig", out var runConfigElement) &&
            runConfigElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            runConfig = HpdAgentConfigSerializer.DeserializeRunConfig(
                runConfigElement.GetRawText(), inputCodec.ProviderComposition);
        }
        return string.IsNullOrWhiteSpace(text)
            ? null
            : new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, text)],
                RunConfig = runConfig,
                ClientInputId = clientInputId
            };
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
