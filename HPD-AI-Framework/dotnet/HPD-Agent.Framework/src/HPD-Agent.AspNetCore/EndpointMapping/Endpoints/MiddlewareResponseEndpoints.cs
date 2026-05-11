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
        // POST /agents/{agentId}/sessions/{sid}/branches/{bid}/permissions/respond - Permission decision
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches/{bid}/permissions/respond", (string agentId, string sid, string bid, PermissionResponseEvent evt, CancellationToken ct) =>
                RespondToPermission(agentId, sid, bid, evt, sessionManager, agentManager, ct))
            .WithName("RespondToPermission")
            .WithSummary("Respond to a permission request from the agent");

        // POST /agents/{agentId}/sessions/{sid}/branches/{bid}/continuation/respond - Continuation decision
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches/{bid}/continuation/respond", (string agentId, string sid, string bid, ContinuationResponseEvent evt, CancellationToken ct) =>
                RespondToContinuation(agentId, sid, bid, evt, sessionManager, agentManager, ct))
            .WithName("RespondToContinuation")
            .WithSummary("Respond to a continuation request from the agent");

        // POST /agents/{agentId}/sessions/{sid}/branches/{bid}/clarifications/respond - Clarification answer
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches/{bid}/clarifications/respond", (string agentId, string sid, string bid, ClarificationResponseEvent evt, CancellationToken ct) =>
                RespondToClarification(agentId, sid, bid, evt, sessionManager, agentManager, ct))
            .WithName("RespondToClarification")
            .WithSummary("Respond to a clarification request from the agent");

        // POST /agents/{agentId}/sessions/{sid}/branches/{bid}/client-tools/respond - Client tool result
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches/{bid}/client-tools/respond", (string agentId, string sid, string bid, ClientToolInvokeResponseEvent evt, CancellationToken ct) =>
                RespondToClientTool(agentId, sid, bid, evt, sessionManager, agentManager, ct))
            .WithName("RespondToClientTool")
            .WithSummary("Respond to a client tool execution request from the agent");
    }

    private static async Task<Results<Ok, NotFound, ValidationProblem>> RespondToPermission(
        string agentId,
        string sid,
        string bid,
        PermissionResponseEvent evt,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        try
        {
            if (!await RouteScopeExistsAsync(sessionManager, sid, bid, ct))
            {
                return TypedResults.NotFound();
            }

            var agent = agentManager.GetAgent(agentId);
            if (agent == null)
            {
                return TypedResults.NotFound();
            }

            // Send response to the waiting permission middleware.
            await agent.RespondAsync(evt, ct);

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
        string agentId,
        string sid,
        string bid,
        ContinuationResponseEvent evt,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        try
        {
            if (!await RouteScopeExistsAsync(sessionManager, sid, bid, ct))
            {
                return TypedResults.NotFound();
            }

            var agent = agentManager.GetAgent(agentId);
            if (agent == null)
            {
                return TypedResults.NotFound();
            }

            // Send response to the waiting continuation middleware.
            await agent.RespondAsync(evt, ct);

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

    private static async Task<Results<Ok, NotFound, ValidationProblem>> RespondToClarification(
        string agentId,
        string sid,
        string bid,
        ClarificationResponseEvent evt,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        try
        {
            if (!await RouteScopeExistsAsync(sessionManager, sid, bid, ct))
            {
                return TypedResults.NotFound();
            }

            var agent = agentManager.GetAgent(agentId);
            if (agent == null)
            {
                return TypedResults.NotFound();
            }

            await agent.RespondAsync(evt, ct);

            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ClarificationResponseError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok, NotFound, ValidationProblem>> RespondToClientTool(
        string agentId,
        string sid,
        string bid,
        ClientToolInvokeResponseEvent evt,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        try
        {
            if (!await RouteScopeExistsAsync(sessionManager, sid, bid, ct))
            {
                return TypedResults.NotFound();
            }

            var agent = agentManager.GetAgent(agentId);
            if (agent == null)
            {
                return TypedResults.NotFound();
            }

            // Send response to the waiting ClientToolMiddleware.
            await agent.RespondAsync(evt, ct);

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

    private static async Task<bool> RouteScopeExistsAsync(
        AspNetCoreSessionManager sessionManager,
        string sid,
        string bid,
        CancellationToken ct)
    {
        var session = await sessionManager.Store.LoadSessionAsync(sid, ct);
        if (session == null)
            return false;

        var branch = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
        return branch != null;
    }
}
