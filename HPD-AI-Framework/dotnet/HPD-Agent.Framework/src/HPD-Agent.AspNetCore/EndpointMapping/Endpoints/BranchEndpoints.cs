using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Branch CRUD endpoints for the HPD-Agent API.
/// </summary>
internal static class BranchEndpoints
{
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        IAgentBranchService branches)
    {
        endpoints.MapGet("/sessions/{sid}/branches", (string sid, CancellationToken ct) =>
                ListBranches(sid, branches, ct))
            .WithName("ListBranches")
            .WithSummary("List all branches in a session");

        endpoints.MapGet("/sessions/{sid}/branches/{bid}", (string sid, string bid, CancellationToken ct) =>
                GetBranch(sid, bid, branches, ct))
            .WithName("GetBranch")
            .WithSummary("Get branch metadata by ID");

        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches", (string agentId, string sid, CreateBranchRequest request, CancellationToken ct) =>
                CreateBranch(agentId, sid, request, branches, ct))
            .WithName("CreateBranch")
            .WithSummary("Create a new branch in a session");

        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/branches/{bid}/fork", (string agentId, string sid, string bid, ForkBranchRequest request, CancellationToken ct) =>
                ForkBranch(agentId, sid, bid, request, branches, ct))
            .WithName("ForkBranch")
            .WithSummary("Fork an existing branch at a specific message id");

        endpoints.MapPatch("/sessions/{sid}/branches/{bid}", (string sid, string bid, UpdateBranchRequest request, CancellationToken ct) =>
                UpdateBranch(sid, bid, request, branches, ct))
            .WithName("UpdateBranch")
            .WithSummary("Update branch name, description, or tags");

        endpoints.MapDelete("/sessions/{sid}/branches/{bid}", (string sid, string bid, bool recursive = false, CancellationToken ct = default) =>
                DeleteBranch(sid, bid, recursive, branches, ct))
            .WithName("DeleteBranch")
            .WithSummary("Delete a branch");

        endpoints.MapGet("/sessions/{sid}/branches/{bid}/events", (string sid, string bid, CancellationToken ct) =>
                GetEvents(sid, bid, branches, ct))
            .WithName("GetBranchEvents")
            .WithSummary("Get the normalized event log for a branch");

        endpoints.MapGet("/sessions/{sid}/branches/{bid}/siblings", (string sid, string bid, CancellationToken ct) =>
                GetSiblings(sid, bid, branches, ct))
            .WithName("GetSiblingBranches")
            .WithSummary("Get sibling branch IDs");
    }

    private static async Task<Results<Ok<List<BranchDto>>, NotFound, ValidationProblem>> ListBranches(
        string sid,
        IAgentBranchService branches,
        CancellationToken ct = default)
    {
        try
        {
            var result = await branches.ListBranchesAsync(sid, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return Validation("ListBranchesError", ex.Message);
        }
    }

    private static async Task<Results<Ok<BranchDto>, NotFound, ValidationProblem>> GetBranch(
        string sid,
        string bid,
        IAgentBranchService branches,
        CancellationToken ct = default)
    {
        try
        {
            return ToOkNotFoundValidation(await branches.GetBranchAsync(sid, bid, ct), "GetBranchError");
        }
        catch (Exception ex)
        {
            return Validation("GetBranchError", ex.Message);
        }
    }

    private static async Task<Results<Created<BranchDto>, NotFound, Conflict, ValidationProblem>> CreateBranch(
        string agentId,
        string sid,
        CreateBranchRequest request,
        IAgentBranchService branches,
        CancellationToken ct = default)
    {
        try
        {
            var result = await branches.CreateBranchAsync(agentId, sid, request, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.Created($"/sessions/{sid}/branches/{result.Value!.Id}", result.Value),
                AgentServiceStatus.NotFound => TypedResults.NotFound(),
                AgentServiceStatus.Conflict => TypedResults.Conflict(),
                _ => Validation(result, "CreateBranchError", "Create branch failed.")
            };
        }
        catch (Exception ex)
        {
            return Validation("CreateBranchError", ex.Message);
        }
    }

    private static async Task<Results<Created<BranchDto>, NotFound, ValidationProblem>> ForkBranch(
        string agentId,
        string sid,
        string bid,
        ForkBranchRequest request,
        IAgentBranchService branches,
        CancellationToken ct = default)
    {
        try
        {
            var result = await branches.ForkBranchAsync(agentId, sid, bid, request, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.Created($"/sessions/{sid}/branches/{result.Value!.Id}", result.Value),
                AgentServiceStatus.NotFound => TypedResults.NotFound(),
                _ => Validation(result, "ForkBranchError", "Fork branch failed.")
            };
        }
        catch (Exception ex)
        {
            return Validation("ForkBranchError", ex.Message);
        }
    }

    private static async Task<Results<Ok<BranchDto>, NotFound, ValidationProblem>> UpdateBranch(
        string sid,
        string bid,
        UpdateBranchRequest request,
        IAgentBranchService branches,
        CancellationToken ct = default)
    {
        try
        {
            return ToOkNotFoundValidation(await branches.UpdateBranchAsync(sid, bid, request, ct), "UpdateBranchError");
        }
        catch (Exception ex)
        {
            return Validation("UpdateBranchError", ex.Message);
        }
    }

    private static async Task<Results<NoContent, NotFound, Conflict, ValidationProblem>> DeleteBranch(
        string sid,
        string bid,
        bool recursive,
        IAgentBranchService branches,
        CancellationToken ct = default)
    {
        try
        {
            var result = await branches.DeleteBranchAsync(sid, bid, recursive, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.NoContent(),
                AgentServiceStatus.NotFound => TypedResults.NotFound(),
                AgentServiceStatus.Conflict => TypedResults.Conflict(),
                _ => Validation(result, "DeleteBranchError", "Delete branch failed.")
            };
        }
        catch (Exception ex)
        {
            return Validation("DeleteBranchError", ex.Message);
        }
    }

    private static async Task<Results<Ok<List<AgentEvent>>, NotFound, ValidationProblem>> GetEvents(
        string sid,
        string bid,
        IAgentBranchService branches,
        CancellationToken ct = default)
    {
        try
        {
            var result = await branches.GetEventsAsync(sid, bid, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return Validation("GetEventsError", ex.Message);
        }
    }

    private static async Task<Results<Ok<List<BranchDto>>, NotFound, ValidationProblem>> GetSiblings(
        string sid,
        string bid,
        IAgentBranchService branches,
        CancellationToken ct = default)
    {
        try
        {
            var result = await branches.GetSiblingsAsync(sid, bid, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return Validation("GetSiblingsError", ex.Message);
        }
    }

    private static Results<Ok<BranchDto>, NotFound, ValidationProblem> ToOkNotFoundValidation(
        AgentServiceResult<BranchDto> result,
        string fallbackErrorCode)
    {
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Ok(result.Value!),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            _ => Validation(result, fallbackErrorCode, "Branch operation failed.")
        };
    }

    private static ValidationProblem Validation(string code, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [code] = [message]
        });

    private static ValidationProblem Validation<T>(
        AgentServiceResult<T> result,
        string fallbackCode,
        string fallbackMessage)
    {
        var messages = result.ErrorMessages?.ToArray()
            ?? [result.ErrorMessage ?? fallbackMessage];

        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [result.ErrorCode ?? fallbackCode] = messages
        });
    }

    private static ValidationProblem Validation(
        AgentServiceResult result,
        string fallbackCode,
        string fallbackMessage)
    {
        var messages = result.ErrorMessages?.ToArray()
            ?? [result.ErrorMessage ?? fallbackMessage];

        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [result.ErrorCode ?? fallbackCode] = messages
        });
    }
}
