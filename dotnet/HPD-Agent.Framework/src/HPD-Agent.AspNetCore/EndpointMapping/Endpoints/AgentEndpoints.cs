using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Agent definition CRUD endpoints.
/// All routes are relative to the route group prefix set by
/// <see cref="HPDAgentEndpointRouteBuilderExtensions"/>.
/// </summary>
internal static class AgentEndpoints
{
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        IAgentDefinitionService agents)
    {
        // POST /agents — create definition
        endpoints.MapPost("/agents", (CreateAgentRequest request, CancellationToken ct) =>
                CreateAgent(request, agents, ct))
            .WithName("CreateAgent")
            .WithSummary("Create a new agent definition");

        // GET /agents — list definitions
        endpoints.MapGet("/agents", (CancellationToken ct) =>
                ListAgents(agents, ct))
            .WithName("ListAgents")
            .WithSummary("List all agent definitions");

        // GET /agents/{agentId} — get definition
        endpoints.MapGet("/agents/{agentId}", (string agentId, CancellationToken ct) =>
                GetAgent(RouteValue.Decode(agentId), agents, ct))
            .WithName("GetAgent")
            .WithSummary("Get an agent definition by ID");

        // PUT /agents/{agentId} — update definition (evicts cached instance)
        endpoints.MapPut("/agents/{agentId}", (string agentId, UpdateAgentRequest request, CancellationToken ct) =>
                UpdateAgent(RouteValue.Decode(agentId), request, agents, ct))
            .WithName("UpdateAgent")
            .WithSummary("Update an agent definition and evict the cached instance");

        // DELETE /agents/{agentId} — delete definition + evict cached instance
        endpoints.MapDelete("/agents/{agentId}", (string agentId, CancellationToken ct) =>
                DeleteAgent(RouteValue.Decode(agentId), agents, ct))
            .WithName("DeleteAgent")
            .WithSummary("Delete an agent definition and evict the cached instance");
    }

    private static async Task<Results<Created<StoredAgentDto>, ValidationProblem>> CreateAgent(
        CreateAgentRequest request,
        IAgentDefinitionService agents,
        CancellationToken ct)
    {
        try
        {
            var result = await agents.CreateAgentAsync(request, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.Created($"/agents/{result.Value!.Id}", result.Value),
                _ => ToValidation(result, "CreateAgentError")
            };
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["CreateAgentError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<List<AgentSummaryDto>>, ValidationProblem>> ListAgents(
        IAgentDefinitionService agents,
        CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok((await agents.ListAgentsAsync(ct)).ToList());
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ListAgentsError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<StoredAgentDto>, NotFound, ValidationProblem>> GetAgent(
        string agentId,
        IAgentDefinitionService agents,
        CancellationToken ct)
    {
        try
        {
            var stored = await agents.GetAgentAsync(agentId, ct);
            if (stored == null)
                return TypedResults.NotFound();

            return TypedResults.Ok(stored);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetAgentError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<StoredAgentDto>, NotFound, ValidationProblem>> UpdateAgent(
        string agentId,
        UpdateAgentRequest request,
        IAgentDefinitionService agents,
        CancellationToken ct)
    {
        try
        {
            var result = await agents.UpdateAgentAsync(agentId, request, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.Ok(result.Value!),
                AgentServiceStatus.NotFound => TypedResults.NotFound(),
                _ => ToValidation(result, "UpdateAgentError")
            };
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["UpdateAgentError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> DeleteAgent(
        string agentId,
        IAgentDefinitionService agents,
        CancellationToken ct)
    {
        try
        {
            var result = await agents.DeleteAgentAsync(agentId, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.NoContent(),
                AgentServiceStatus.NotFound => TypedResults.NotFound(),
                _ => ToValidation(result, "DeleteAgentError")
            };
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DeleteAgentError"] = [ex.Message]
            });
        }
    }

    private static ValidationProblem ToValidation<T>(
        AgentServiceResult<T> result,
        string fallbackErrorCode)
    {
        var messages = result.ErrorMessages?.ToArray()
            ?? [result.ErrorMessage ?? "Agent definition operation failed."];

        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [result.ErrorCode ?? fallbackErrorCode] = messages
        });
    }

    private static ValidationProblem ToValidation(
        AgentServiceResult result,
        string fallbackErrorCode)
    {
        var messages = result.ErrorMessages?.ToArray()
            ?? [result.ErrorMessage ?? "Agent definition operation failed."];

        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [result.ErrorCode ?? fallbackErrorCode] = messages
        });
    }

}
