using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Thread CRUD endpoints for the HPD-Agent API.
/// </summary>
internal static class ThreadEndpoints
{
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        IAgentThreadService threads)
    {
        endpoints.MapGet("/sessions/{sid}/threads", (string sid, CancellationToken ct) =>
                ListThreads(sid, threads, ct))
            .WithName("ListThreads")
            .WithSummary("List all threads in a session");

        endpoints.MapGet("/sessions/{sid}/threads/{bid}", (string sid, string bid, CancellationToken ct) =>
                GetThread(sid, bid, threads, ct))
            .WithName("GetThread")
            .WithSummary("Get thread metadata by ID");

        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads", (string agentId, string sid, CreateThreadRequest request, CancellationToken ct) =>
                CreateThread(agentId, sid, request, threads, ct))
            .WithName("CreateThread")
            .WithSummary("Create a new thread in a session");

        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads/{bid}/fork", (string agentId, string sid, string bid, ForkThreadRequest request, CancellationToken ct) =>
                ForkThread(agentId, sid, bid, request, threads, ct))
            .WithName("ForkThread")
            .WithSummary("Fork an existing thread at a specific message id");

        endpoints.MapPatch("/sessions/{sid}/threads/{bid}", (string sid, string bid, UpdateThreadRequest request, CancellationToken ct) =>
                UpdateThread(sid, bid, request, threads, ct))
            .WithName("UpdateThread")
            .WithSummary("Update thread name, description, or tags");

        endpoints.MapDelete("/sessions/{sid}/threads/{bid}", (string sid, string bid, bool recursive = false, CancellationToken ct = default) =>
                DeleteThread(sid, bid, recursive, threads, ct))
            .WithName("DeleteThread")
            .WithSummary("Delete a thread");

        endpoints.MapGet("/sessions/{sid}/threads/{bid}/events", (string sid, string bid, CancellationToken ct) =>
                GetEvents(sid, bid, threads, ct))
            .WithName("GetThreadEvents")
            .WithSummary("Get the normalized event log for a thread");

        endpoints.MapGet("/sessions/{sid}/threads/{bid}/siblings", (string sid, string bid, CancellationToken ct) =>
                GetSiblings(sid, bid, threads, ct))
            .WithName("GetSiblingThreads")
            .WithSummary("Get sibling thread IDs");
    }

    private static async Task<Results<Ok<List<ThreadDto>>, NotFound, ValidationProblem>> ListThreads(
        string sid,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threads.ListThreadsAsync(sid, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return Validation("ListThreadsError", ex.Message);
        }
    }

    private static async Task<Results<Ok<ThreadDto>, NotFound, ValidationProblem>> GetThread(
        string sid,
        string bid,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            return ToOkNotFoundValidation(await threads.GetThreadAsync(sid, bid, ct), "GetThreadError");
        }
        catch (Exception ex)
        {
            return Validation("GetThreadError", ex.Message);
        }
    }

    private static async Task<Results<Created<ThreadDto>, NotFound, Conflict, ValidationProblem>> CreateThread(
        string agentId,
        string sid,
        CreateThreadRequest request,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threads.CreateThreadAsync(agentId, sid, request, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.Created($"/sessions/{sid}/threads/{result.Value!.Id}", result.Value),
                AgentServiceStatus.NotFound => TypedResults.NotFound(),
                AgentServiceStatus.Conflict => TypedResults.Conflict(),
                _ => Validation(result, "CreateThreadError", "Create thread failed.")
            };
        }
        catch (Exception ex)
        {
            return Validation("CreateThreadError", ex.Message);
        }
    }

    private static async Task<Results<Created<ThreadDto>, NotFound, ValidationProblem>> ForkThread(
        string agentId,
        string sid,
        string bid,
        ForkThreadRequest request,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threads.ForkThreadAsync(agentId, sid, bid, request, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.Created($"/sessions/{sid}/threads/{result.Value!.Id}", result.Value),
                AgentServiceStatus.NotFound => TypedResults.NotFound(),
                _ => Validation(result, "ForkThreadError", "Fork thread failed.")
            };
        }
        catch (Exception ex)
        {
            return Validation("ForkThreadError", ex.Message);
        }
    }

    private static async Task<Results<Ok<ThreadDto>, NotFound, ValidationProblem>> UpdateThread(
        string sid,
        string bid,
        UpdateThreadRequest request,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            return ToOkNotFoundValidation(await threads.UpdateThreadAsync(sid, bid, request, ct), "UpdateThreadError");
        }
        catch (Exception ex)
        {
            return Validation("UpdateThreadError", ex.Message);
        }
    }

    private static async Task<Results<NoContent, NotFound, Conflict, ValidationProblem>> DeleteThread(
        string sid,
        string bid,
        bool recursive,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threads.DeleteThreadAsync(sid, bid, recursive, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.NoContent(),
                AgentServiceStatus.NotFound => TypedResults.NotFound(),
                AgentServiceStatus.Conflict => TypedResults.Conflict(),
                _ => Validation(result, "DeleteThreadError", "Delete thread failed.")
            };
        }
        catch (Exception ex)
        {
            return Validation("DeleteThreadError", ex.Message);
        }
    }

    private static async Task<Results<Ok<List<AgentEvent>>, NotFound, ValidationProblem>> GetEvents(
        string sid,
        string bid,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threads.GetEventsAsync(sid, bid, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return Validation("GetEventsError", ex.Message);
        }
    }

    private static async Task<Results<Ok<List<ThreadDto>>, NotFound, ValidationProblem>> GetSiblings(
        string sid,
        string bid,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threads.GetSiblingsAsync(sid, bid, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return Validation("GetSiblingsError", ex.Message);
        }
    }

    private static Results<Ok<ThreadDto>, NotFound, ValidationProblem> ToOkNotFoundValidation(
        AgentServiceResult<ThreadDto> result,
        string fallbackErrorCode)
    {
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Ok(result.Value!),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            _ => Validation(result, fallbackErrorCode, "Thread operation failed.")
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
