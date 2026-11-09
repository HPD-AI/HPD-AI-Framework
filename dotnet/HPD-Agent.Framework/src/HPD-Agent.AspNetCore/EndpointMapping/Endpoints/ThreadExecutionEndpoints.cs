using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.AspNetCore.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

internal static class ThreadExecutionEndpoints
{
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        IAgentThreadExecutionService threadExecutions,
        string endpointName)
    {
        endpoints.MapGet("/agents/{agentId}/sessions/{sessionId}/threads/{threadId}/executions",
                (string agentId, string sessionId, string threadId, CancellationToken ct) =>
                    ListExecutions(RouteValue.Decode(agentId), RouteValue.Decode(sessionId), RouteValue.Decode(threadId), threadExecutions, ct))
            .WithHostName(endpointName, "ListThreadExecutions")
            .WithSummary("List runtime-owned executions projected from a thread event log");

        endpoints.MapGet("/agents/{agentId}/sessions/{sessionId}/threads/{threadId}/executions/{threadExecutionId}",
                (string agentId, string sessionId, string threadId, string threadExecutionId, CancellationToken ct) =>
                    GetExecution(RouteValue.Decode(agentId), RouteValue.Decode(sessionId), RouteValue.Decode(threadId), RouteValue.Decode(threadExecutionId), threadExecutions, ct))
            .WithHostName(endpointName, "GetThreadExecution")
            .WithSummary("Get a runtime-owned thread execution by ID");

        endpoints.MapPost("/agents/{agentId}/sessions/{sessionId}/threads/{threadId}/executions/{threadExecutionId}/cancel",
                (string agentId, string sessionId, string threadId, string threadExecutionId, CancellationToken ct) =>
                    CancelExecution(RouteValue.Decode(agentId), RouteValue.Decode(sessionId), RouteValue.Decode(threadId), RouteValue.Decode(threadExecutionId), threadExecutions, ct))
            .WithHostName(endpointName, "CancelThreadExecution")
            .WithSummary("Cancel one exact runtime-owned thread execution and await its terminal fact");

        endpoints.MapPost("/agents/{agentId}/sessions/{sessionId}/threads/{threadId}/queue/start",
                (string agentId, string sessionId, string threadId, CancellationToken ct) =>
                    StartQueuedWork(RouteValue.Decode(agentId), RouteValue.Decode(sessionId), RouteValue.Decode(threadId), threadExecutions, ct))
            .WithHostName(endpointName, "StartQueuedThreadWork")
            .WithSummary("Resume promotion after an accepted hosted cancellation");
    }

    private static async Task<Results<Ok<List<ThreadExecutionDto>>, NotFound, ValidationProblem>> ListExecutions(
        string agentId,
        string sessionId,
        string threadId,
        IAgentThreadExecutionService threadExecutions,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threadExecutions.ListExecutionsAsync(agentId, sessionId, threadId, ct).ConfigureAwait(false);
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
        string sessionId,
        string threadId,
        string threadExecutionId,
        IAgentThreadExecutionService threadExecutions,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threadExecutions.GetExecutionAsync(agentId, sessionId, threadId, threadExecutionId, ct).ConfigureAwait(false);
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
        string sessionId,
        string threadId,
        string threadExecutionId,
        IAgentThreadExecutionService threadExecutions,
        CancellationToken ct)
    {
        var result = await threadExecutions.CancelExecutionAsync(
                agentId, sessionId, threadId, threadExecutionId, ct)
            .ConfigureAwait(false);
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Ok(result.Value),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            AgentServiceStatus.Conflict => Conflict(
                result.ErrorCode ?? "CancelThreadExecutionError",
                result.ErrorMessage ?? "Cancellation was refused."),
            _ => Validation(
                result.ErrorCode ?? "CancelThreadExecutionError",
                result.ErrorMessage ?? "Cancellation was refused.")
        };
    }

    private static async Task<IResult> StartQueuedWork(
        string agentId,
        string sessionId,
        string threadId,
        IAgentThreadExecutionService threadExecutions,
        CancellationToken ct)
    {
        var result = await threadExecutions.StartQueuedWorkAsync(agentId, sessionId, threadId, ct).ConfigureAwait(false);
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Ok(result.Value),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            AgentServiceStatus.Conflict => Conflict(
                result.ErrorCode ?? "StartQueuedWorkError",
                result.ErrorMessage ?? "Queue promotion was refused."),
            _ => Validation(
                result.ErrorCode ?? "StartQueuedWorkError",
                result.ErrorMessage ?? "Queue promotion was refused.")
        };
    }

    private static IResult Conflict(string code, string message) =>
        Results.Json(
            new Dictionary<string, string[]>
            {
                [code] = [message]
            },
            HPDAgentAspNetCoreJsonSerializerContext.Default.DictionaryStringStringArray,
            statusCode: StatusCodes.Status409Conflict);

    private static ValidationProblem Validation(string code, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [code] = [message]
        });
}
