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
        IAgentThreadService threads,
        string endpointName)
    {
        endpoints.MapGet("/sessions/{sessionId}/threads", (string sessionId, CancellationToken ct) =>
                ListThreads(RouteValue.Decode(sessionId), threads, ct))
            .WithHostName(endpointName, "ListThreads")
            .WithSummary("List all threads in a session");

        endpoints.MapGet("/sessions/{sessionId}/thread-graph", (string sessionId, CancellationToken ct) =>
                GetThreadGraph(RouteValue.Decode(sessionId), threads, ct))
            .WithHostName(endpointName, "GetThreadGraph")
            .WithSummary("Get threads and fork groups for a session");

        endpoints.MapGet("/sessions/{sessionId}/threads/{threadId}", (string sessionId, string threadId, CancellationToken ct) =>
                GetThread(RouteValue.Decode(sessionId), RouteValue.Decode(threadId), threads, ct))
            .WithHostName(endpointName, "GetThread")
            .WithSummary("Get thread metadata by ID");

        endpoints.MapGet("/sessions/{sessionId}/threads/{threadId}/subagents", (string sessionId, string threadId, CancellationToken ct) =>
                ListSubAgents(RouteValue.Decode(sessionId), RouteValue.Decode(threadId), threads, ct))
            .WithHostName(endpointName, "ListThreadSubAgents")
            .WithSummary("List durable subagents owned by a parent thread");

        endpoints.MapPost("/agents/{agentId}/sessions/{sessionId}/threads", (
                string agentId,
                string sessionId,
                CreateThreadRequest request,
                LinkGenerator links,
                CancellationToken ct) =>
                CreateThread(
                    RouteValue.Decode(agentId),
                    RouteValue.Decode(sessionId),
                    request,
                    threads,
                    links,
                    endpointName,
                    ct))
            .WithHostName(endpointName, "CreateThread")
            .WithSummary("Create a new thread in a session");

        endpoints.MapPost("/agents/{agentId}/sessions/{sessionId}/threads/{threadId}/fork", (
                string agentId,
                string sessionId,
                string threadId,
                ForkThreadRequest request,
                LinkGenerator links,
                CancellationToken ct) =>
                ForkThread(
                    RouteValue.Decode(agentId),
                    RouteValue.Decode(sessionId),
                    RouteValue.Decode(threadId),
                    request,
                    threads,
                    links,
                    endpointName,
                    ct))
            .WithHostName(endpointName, "ForkThread")
            .WithSummary("Fork an existing thread at a specific message id");

        endpoints.MapPatch("/sessions/{sessionId}/threads/{threadId}", (string sessionId, string threadId, UpdateThreadRequest request, CancellationToken ct) =>
                UpdateThread(RouteValue.Decode(sessionId), RouteValue.Decode(threadId), request, threads, ct))
            .WithHostName(endpointName, "UpdateThread")
            .WithSummary("Update thread name, description, or tags");

        endpoints.MapDelete("/sessions/{sessionId}/threads/{threadId}", (string sessionId, string threadId, bool recursive = false, CancellationToken ct = default) =>
                DeleteThread(RouteValue.Decode(sessionId), RouteValue.Decode(threadId), recursive, threads, ct))
            .WithHostName(endpointName, "DeleteThread")
            .WithSummary("Delete a thread");

    }

    private static async Task<Results<Ok<List<ThreadDto>>, NotFound, ValidationProblem>> ListThreads(
        string sessionId,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threads.ListThreadsAsync(sessionId, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return Validation("ListThreadsError", ex.Message);
        }
    }

    private static async Task<Results<Ok<ThreadGraphDto>, NotFound, ValidationProblem>> GetThreadGraph(
        string sessionId,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threads.GetThreadGraphAsync(sessionId, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!);
        }
        catch (Exception ex)
        {
            return Validation("GetThreadGraphError", ex.Message);
        }
    }

    private static async Task<Results<Ok<ThreadDto>, NotFound, ValidationProblem>> GetThread(
        string sessionId,
        string threadId,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            return ToOkNotFoundValidation(await threads.GetThreadAsync(sessionId, threadId, ct), "GetThreadError");
        }
        catch (Exception ex)
        {
            return Validation("GetThreadError", ex.Message);
        }
    }

    private static async Task<Results<Ok<List<SubAgentDto>>, NotFound, ValidationProblem>> ListSubAgents(
        string sessionId, string threadId, IAgentThreadService threads, CancellationToken ct = default)
    {
        try
        {
            var result = await threads.ListSubAgentsAsync(sessionId, threadId, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex) { return Validation("ListSubAgentsError", ex.Message); }
    }

    private static async Task<Results<Created<ThreadDto>, NotFound, Conflict, ValidationProblem>> CreateThread(
        string agentId,
        string sessionId,
        CreateThreadRequest request,
        IAgentThreadService threads,
        LinkGenerator links,
        string endpointName,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threads.CreateThreadAsync(agentId, sessionId, request, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.Created(
                    CreatedLocation.Resolve(
                        links,
                        $"{endpointName}.GetThread",
                        ("sessionId", sessionId),
                        ("threadId", result.Value!.Id)),
                    result.Value),
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

    private static async Task<Results<Created<ThreadForkResultDto>, NotFound, ValidationProblem>> ForkThread(
        string agentId,
        string sessionId,
        string threadId,
        ForkThreadRequest request,
        IAgentThreadService threads,
        LinkGenerator links,
        string endpointName,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threads.ForkThreadAsync(agentId, sessionId, threadId, request, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.Created(
                    CreatedLocation.Resolve(
                        links,
                        $"{endpointName}.GetThread",
                        ("sessionId", sessionId),
                        ("threadId", result.Value!.Target.Id)),
                    result.Value),
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
        string sessionId,
        string threadId,
        UpdateThreadRequest request,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            return ToOkNotFoundValidation(await threads.UpdateThreadAsync(sessionId, threadId, request, ct), "UpdateThreadError");
        }
        catch (Exception ex)
        {
            return Validation("UpdateThreadError", ex.Message);
        }
    }

    private static async Task<Results<NoContent, NotFound, Conflict, ValidationProblem>> DeleteThread(
        string sessionId,
        string threadId,
        bool recursive,
        IAgentThreadService threads,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threads.DeleteThreadAsync(sessionId, threadId, recursive, ct);
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
