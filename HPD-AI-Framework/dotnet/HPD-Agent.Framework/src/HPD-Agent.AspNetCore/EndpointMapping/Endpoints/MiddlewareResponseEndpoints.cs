using System.Text.Json;
using HPD.Agent;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Data;
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
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager)
    {
        // POST /sessions/{sid}/branches/{bid}/permissions/respond - Permission decision
        endpoints.MapPost("/sessions/{sid}/branches/{bid}/permissions/respond", (string sid, string bid, PermissionResponseEvent evt, CancellationToken ct) =>
                RespondToPermission(sid, bid, evt, agentManager, ct))
            .WithName("RespondToPermission")
            .WithSummary("Respond to a permission request from the agent");

        // POST /sessions/{sid}/branches/{bid}/continuation/respond - Continuation decision
        endpoints.MapPost("/sessions/{sid}/branches/{bid}/continuation/respond", (string sid, string bid, ContinuationResponseEvent evt, CancellationToken ct) =>
                RespondToContinuation(sid, bid, evt, agentManager, ct))
            .WithName("RespondToContinuation")
            .WithSummary("Respond to a continuation request from the agent");

        // POST /sessions/{sid}/branches/{bid}/client-tools/respond - Client tool result
        endpoints.MapPost("/sessions/{sid}/branches/{bid}/client-tools/respond", (string sid, string bid, ClientToolInvokeResponseEvent evt, CancellationToken ct) =>
                RespondToClientTool(sid, bid, evt, agentManager, ct))
            .WithName("RespondToClientTool")
            .WithSummary("Respond to a client tool execution request from the agent");
    }

    private static async Task<Results<Ok, NotFound, ValidationProblem>> RespondToPermission(
        string sid,
        string bid,
        PermissionResponseEvent evt,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        try
        {
            // Get the cached agent (defaults to "default" for single-agent deployments)
            var agentId = "default"; // PermissionResponseEvent doesn't carry AgentId; default to "default"
            var agent = agentManager.GetAgent(agentId);
            if (agent == null)
            {
                return TypedResults.NotFound();
            }

            // Send response directly to waiting permission middleware
            agent.SendMiddlewareResponse(evt.PermissionId, evt);

            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PermissionResponseError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok, NotFound, ValidationProblem>> RespondToContinuation(
        string sid,
        string bid,
        ContinuationResponseEvent evt,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        try
        {
            // Get the cached agent (defaults to "default" for single-agent deployments)
            var agentId = "default"; // ContinuationResponseEvent doesn't carry AgentId; default to "default"
            var agent = agentManager.GetAgent(agentId);
            if (agent == null)
            {
                return TypedResults.NotFound();
            }

            // Send response directly to waiting continuation permission middleware
            agent.SendMiddlewareResponse(evt.ContinuationId, evt);

            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ContinuationResponseError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok, NotFound, ValidationProblem>> RespondToClientTool(
        string sid,
        string bid,
        ClientToolInvokeResponseEvent evt,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        try
        {
            // Get the cached agent (defaults to "default" for single-agent deployments)
            var agentId = "default"; // ClientToolInvokeResponseEvent doesn't carry AgentId; default to "default"
            var agent = agentManager.GetAgent(agentId);
            if (agent == null)
            {
                return TypedResults.NotFound();
            }

            // Send response directly to waiting ClientToolMiddleware
            agent.SendMiddlewareResponse(evt.RequestId, evt);

            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ClientToolResponseError"] = [ex.Message]
            });
        }
    }
}
