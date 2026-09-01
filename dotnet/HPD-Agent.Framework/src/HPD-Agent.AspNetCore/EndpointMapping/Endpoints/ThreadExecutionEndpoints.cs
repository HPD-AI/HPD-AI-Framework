using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

internal static class ThreadExecutionEndpoints
{
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        IAgentThreadExecutionService threadExecutions)
    {
        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/threads/{bid}/executions",
                (string agentId, string sid, string bid, CancellationToken ct) =>
                    ListExecutions(RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid), threadExecutions, ct))
            .WithName("ListThreadExecutions")
            .WithSummary("List runtime-owned executions projected from a thread event log");

        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/threads/{bid}/executions/{threadExecutionId}",
                (string agentId, string sid, string bid, string threadExecutionId, CancellationToken ct) =>
                    GetExecution(RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid), RouteValue.Decode(threadExecutionId), threadExecutions, ct))
            .WithName("GetThreadExecution")
            .WithSummary("Get a runtime-owned thread execution by ID");

        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads/{bid}/executions/{threadExecutionId}/cancel",
                (string agentId, string sid, string bid, string threadExecutionId, CancellationToken ct) =>
                    CancelExecution(RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid), RouteValue.Decode(threadExecutionId), threadExecutions, ct))
            .WithName("CancelThreadExecution")
            .WithSummary("Cancel one exact runtime-owned thread execution and await its terminal fact");

        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads/{bid}/queue/start",
                (string agentId, string sid, string bid, CancellationToken ct) =>
                    StartQueuedWork(RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid), threadExecutions, ct))
            .WithName("StartQueuedThreadWork")
            .WithSummary("Resume promotion after an accepted hosted cancellation");
    }

    private static async Task<Results<Ok<List<ThreadExecutionDto>>, NotFound, ValidationProblem>> ListExecutions(
        string agentId,
        string sid,
        string bid,
        IAgentThreadExecutionService threadExecutions,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threadExecutions.ListExecutionsAsync(agentId, sid, bid, ct).ConfigureAwait(false);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return Validation("ListThreadExecutionsError", ex.Message);
        }
    }

    private static async Task<Results<Ok<ThreadExecutionDto>, NotFound, ValidationProblem>> GetExecution(
        string agentId,
        string sid,
        string bid,
        string threadExecutionId,
        IAgentThreadExecutionService threadExecutions,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threadExecutions.GetExecutionAsync(agentId, sid, bid, threadExecutionId, ct).ConfigureAwait(false);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!);
        }
        catch (Exception ex)
        {
            return Validation("GetThreadExecutionError", ex.Message);
        }
    }

    private static async Task<IResult> CancelExecution(
        string agentId,
        string sid,
        string bid,
        string threadExecutionId,
        IAgentThreadExecutionService threadExecutions,
        CancellationToken ct)
    {
        var result = await threadExecutions.CancelExecutionAsync(
                agentId, sid, bid, threadExecutionId, ct)
            .ConfigureAwait(false);
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Ok(result.Value),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            _ => Validation(result.ErrorCode ?? "CancelThreadExecutionError", result.ErrorMessage ?? "Cancellation was refused.")
        };
    }

    private static async Task<IResult> StartQueuedWork(
        string agentId,
        string sid,
        string bid,
        IAgentThreadExecutionService threadExecutions,
        CancellationToken ct)
    {
        var result = await threadExecutions.StartQueuedWorkAsync(agentId, sid, bid, ct).ConfigureAwait(false);
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Ok(result.Value),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            _ => Validation(result.ErrorCode ?? "StartQueuedWorkError", result.ErrorMessage ?? "Queue promotion was refused.")
        };
    }

    private static ValidationProblem Validation(string code, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [code] = [message]
        });
}
