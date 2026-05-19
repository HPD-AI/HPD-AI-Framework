using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Validation;
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
        AspNetCoreAgentManager agentManager)
    {
        // POST /agents — create definition
        endpoints.MapPost("/agents", (CreateAgentRequest request, CancellationToken ct) =>
                CreateAgent(request, agentManager, ct))
            .WithName("CreateAgent")
            .WithSummary("Create a new agent definition");

        // GET /agents — list definitions
        endpoints.MapGet("/agents", (CancellationToken ct) =>
                ListAgents(agentManager, ct))
            .WithName("ListAgents")
            .WithSummary("List all agent definitions");

        // GET /agents/{agentId} — get definition
        endpoints.MapGet("/agents/{agentId}", (string agentId, CancellationToken ct) =>
                GetAgent(agentId, agentManager, ct))
            .WithName("GetAgent")
            .WithSummary("Get an agent definition by ID");

        // PUT /agents/{agentId} — update definition (evicts cached instance)
        endpoints.MapPut("/agents/{agentId}", (string agentId, UpdateAgentRequest request, CancellationToken ct) =>
                UpdateAgent(agentId, request, agentManager, ct))
            .WithName("UpdateAgent")
            .WithSummary("Update an agent definition and evict the cached instance");

        // DELETE /agents/{agentId} — delete definition + evict cached instance
        endpoints.MapDelete("/agents/{agentId}", (string agentId, CancellationToken ct) =>
                DeleteAgent(agentId, agentManager, ct))
            .WithName("DeleteAgent")
            .WithSummary("Delete an agent definition and evict the cached instance");
    }

    private static async Task<Results<Created<StoredAgentDto>, ValidationProblem>> CreateAgent(
        CreateAgentRequest request,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct)
    {
        var errors = AgentConfigValidator.Validate(request.Config);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["config"] = errors.ToArray()
            });

        try
        {
            var stored = await agentManager.CreateDefinitionAsync(
                request.Config,
                request.Name,
                request.Metadata,
                ct);

            var dto = ToDto(stored);
            return TypedResults.Created($"/agents/{stored.Id}", dto);
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
        AspNetCoreAgentManager agentManager,
        CancellationToken ct)
    {
        try
        {
            var agents = await agentManager.ListDefinitionsAsync(ct);
            return TypedResults.Ok(agents.Select(ToSummaryDto).ToList());
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
        AspNetCoreAgentManager agentManager,
        CancellationToken ct)
    {
        try
        {
            var stored = await agentManager.GetDefinitionAsync(agentId, ct);
            if (stored == null)
                return TypedResults.NotFound();

            return TypedResults.Ok(ToDto(stored));
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
        AspNetCoreAgentManager agentManager,
        CancellationToken ct)
    {
        var errors = AgentConfigValidator.Validate(request.Config);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["config"] = errors.ToArray()
            });

        try
        {
            var stored = await agentManager.UpdateDefinitionAsync(agentId, request.Config, ct);
            return TypedResults.Ok(ToDto(stored));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
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
        AspNetCoreAgentManager agentManager,
        CancellationToken ct)
    {
        try
        {
            var existing = await agentManager.GetDefinitionAsync(agentId, ct);
            if (existing == null)
                return TypedResults.NotFound();

            await agentManager.DeleteDefinitionAsync(agentId, ct);
            return TypedResults.NoContent();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DeleteAgentError"] = [ex.Message]
            });
        }
    }

    private static StoredAgentDto ToDto(StoredAgent stored) => new(
        stored.Id,
        stored.Name,
        stored.Config,
        stored.CreatedAt,
        stored.UpdatedAt,
        stored.Metadata);

    private static AgentSummaryDto ToSummaryDto(StoredAgent stored) => new(
        stored.Id,
        stored.Name,
        stored.CreatedAt,
        stored.UpdatedAt,
        stored.Metadata);
}
