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
        endpoints.MapPost("/sessions/{sid}/branches/{bid}/permissions/respond", (string sid, string bid, PermissionResponseRequest request, CancellationToken ct) =>
                RespondToPermission(sid, bid, request, agentManager, ct))
            .WithName("RespondToPermission")
            .WithSummary("Respond to a permission request from the agent");

        // POST /sessions/{sid}/branches/{bid}/client-tools/respond - Client tool result
        endpoints.MapPost("/sessions/{sid}/branches/{bid}/client-tools/respond", (string sid, string bid, ClientToolResponseRequest request, CancellationToken ct) =>
                RespondToClientTool(sid, bid, request, agentManager, ct))
            .WithName("RespondToClientTool")
            .WithSummary("Respond to a client tool execution request from the agent");
    }

    private static async Task<IResult> RespondToPermission(
        string sid,
        string bid,
        PermissionResponseRequest request,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        try
        {
            // Get the cached agent (defaults to "default" for single-agent deployments)
            var agentId = request.AgentId ?? "default";
            var agent = agentManager.GetAgent(agentId);
            if (agent == null)
            {
                return ErrorResponses.NotFound();
            }

            // Convert string choice to PermissionChoice enum
            var choice = request.Choice?.ToLower() switch
            {
                "allow_always" => PermissionChoice.AlwaysAllow,
                "deny_always" => PermissionChoice.AlwaysDeny,
                _ => PermissionChoice.Ask
            };

            // Send response to waiting permission middleware
            agent.SendMiddlewareResponse(
                request.PermissionId,
                new PermissionResponseEvent(
                    request.PermissionId,
                    "PermissionMiddleware",
                    request.Approved,
                    request.Reason,
                    choice));

            return Results.Ok();
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PermissionResponseError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> RespondToClientTool(
        string sid,
        string bid,
        ClientToolResponseRequest request,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        try
        {
            // Get the cached agent (defaults to "default" for single-agent deployments)
            var agentId = request.AgentId ?? "default";
            var agent = agentManager.GetAgent(agentId);
            if (agent == null)
            {
                return ErrorResponses.NotFound();
            }

            // Convert content to IToolResultContent list
            var content = request.Content?.Select<ClientToolContentDto, IToolResultContent>(c => c.Type switch
            {
                "text" => new HPD.Agent.ClientTools.TextContent(c.Text ?? ""),
                "binary" or "data" => new BinaryContent(
                    c.MediaType ?? "application/octet-stream",
                    Convert.ToBase64String(c.Data ?? Array.Empty<byte>()),
                    null,  // url
                    null,  // id
                    null), // filename
                _ => new HPD.Agent.ClientTools.TextContent(c.Text ?? "")
            }).ToList() ?? new List<IToolResultContent>();

            // Send response to waiting ClientToolMiddleware
            agent.SendMiddlewareResponse(
                request.RequestId,
                new ClientToolInvokeResponseEvent(
                    RequestId: request.RequestId,
                    Content: content,
                    Success: request.Success,
                    ErrorMessage: request.ErrorMessage,
                    Augmentation: null));

            return Results.Ok();
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ClientToolResponseError"] = [ex.Message]
            });
        }
    }
}
