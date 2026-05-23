using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Middleware response endpoints for the HPD-Agent API.
/// These endpoints allow clients to respond to permission requests and client tool calls.
/// </summary>
internal static class MiddlewareResponseEndpoints
{
    /// <summary>
    /// Maps all middleware response endpoints.
    /// </summary>
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        IAgentMiddlewareResponseService responses)
    {
        // POST /agents/{agentId}/sessions/{sid}/branches/{bid}/permissions/respond - Permission decision
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches/{bid}/permissions/respond", (string agentId, string sid, string bid, PermissionResponseEvent evt, CancellationToken ct) =>
                RespondToPermission(agentId, sid, bid, evt, responses, ct))
            .WithName("RespondToPermission")
            .WithSummary("Respond to a permission request from the agent");

        // POST /agents/{agentId}/sessions/{sid}/branches/{bid}/continuation/respond - Continuation decision
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches/{bid}/continuation/respond", (string agentId, string sid, string bid, ContinuationResponseEvent evt, CancellationToken ct) =>
                RespondToContinuation(agentId, sid, bid, evt, responses, ct))
            .WithName("RespondToContinuation")
            .WithSummary("Respond to a continuation request from the agent");

        // POST /agents/{agentId}/sessions/{sid}/branches/{bid}/clarifications/respond - Clarification answer
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches/{bid}/clarifications/respond", (string agentId, string sid, string bid, ClarificationResponseEvent evt, CancellationToken ct) =>
                RespondToClarification(agentId, sid, bid, evt, responses, ct))
            .WithName("RespondToClarification")
            .WithSummary("Respond to a clarification request from the agent");

        // POST /agents/{agentId}/sessions/{sid}/branches/{bid}/client-tools/respond - Client tool result
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches/{bid}/client-tools/respond", (string agentId, string sid, string bid, ClientToolInvokeResponseEvent evt, CancellationToken ct) =>
                RespondToClientTool(agentId, sid, bid, evt, responses, ct))
            .WithName("RespondToClientTool")
            .WithSummary("Respond to a client tool execution request from the agent");
    }

    private static async Task<Results<Ok, NotFound, Conflict, ValidationProblem>> RespondToPermission(
        string agentId,
        string sid,
        string bid,
        PermissionResponseEvent evt,
        IAgentMiddlewareResponseService responses,
        CancellationToken ct = default)
    {
        try
        {
            return ToHttpResult(await responses.RespondToPermissionAsync(agentId, sid, bid, evt, ct));
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PermissionResponseError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok, NotFound, Conflict, ValidationProblem>> RespondToContinuation(
        string agentId,
        string sid,
        string bid,
        ContinuationResponseEvent evt,
        IAgentMiddlewareResponseService responses,
        CancellationToken ct = default)
    {
        try
        {
            return ToHttpResult(await responses.RespondToContinuationAsync(agentId, sid, bid, evt, ct));
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ContinuationResponseError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok, NotFound, Conflict, ValidationProblem>> RespondToClarification(
        string agentId,
        string sid,
        string bid,
        ClarificationResponseEvent evt,
        IAgentMiddlewareResponseService responses,
        CancellationToken ct = default)
    {
        try
        {
            return ToHttpResult(await responses.RespondToClarificationAsync(agentId, sid, bid, evt, ct));
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ClarificationResponseError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok, NotFound, Conflict, ValidationProblem>> RespondToClientTool(
        string agentId,
        string sid,
        string bid,
        ClientToolInvokeResponseEvent evt,
        IAgentMiddlewareResponseService responses,
        CancellationToken ct = default)
    {
        try
        {
            return ToHttpResult(await responses.RespondToClientToolAsync(agentId, sid, bid, evt, ct));
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ClientToolResponseError"] = [ex.Message]
            });
        }
    }

    private static Results<Ok, NotFound, Conflict, ValidationProblem> ToHttpResult(AgentServiceResult result)
    {
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Ok(),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            AgentServiceStatus.Conflict => TypedResults.Conflict(),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.ErrorCode ?? "MiddlewareResponseError"] = [result.ErrorMessage ?? "Middleware response failed."]
            })
        };
    }
}
