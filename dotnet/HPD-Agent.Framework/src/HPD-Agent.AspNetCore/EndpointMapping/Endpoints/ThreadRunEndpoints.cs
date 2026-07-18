using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

internal static class ThreadRunEndpoints
{
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        IAgentThreadRunService threadRuns)
    {
        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/threads/{bid}/runs",
                (string agentId, string sid, string bid, CancellationToken ct) =>
                    ListRuns(RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid), threadRuns, ct))
            .WithName("ListThreadRuns")
            .WithSummary("List runtime-owned runs projected from a thread event log");

        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/threads/{bid}/runs/{runtimeRunId}",
                (string agentId, string sid, string bid, string runtimeRunId, CancellationToken ct) =>
                    GetRun(RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid), RouteValue.Decode(runtimeRunId), threadRuns, ct))
            .WithName("GetThreadRun")
            .WithSummary("Get a runtime-owned thread run by ID");
    }

    private static async Task<Results<Ok<List<ThreadRunDto>>, NotFound, ValidationProblem>> ListRuns(
        string agentId,
        string sid,
        string bid,
        IAgentThreadRunService threadRuns,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threadRuns.ListRunsAsync(agentId, sid, bid, ct).ConfigureAwait(false);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return Validation("ListThreadRunsError", ex.Message);
        }
    }

    private static async Task<Results<Ok<ThreadRunDto>, NotFound, ValidationProblem>> GetRun(
        string agentId,
        string sid,
        string bid,
        string runtimeRunId,
        IAgentThreadRunService threadRuns,
        CancellationToken ct = default)
    {
        try
        {
            var result = await threadRuns.GetRunAsync(agentId, sid, bid, runtimeRunId, ct).ConfigureAwait(false);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!);
        }
        catch (Exception ex)
        {
            return Validation("GetThreadRunError", ex.Message);
        }
    }

    private static ValidationProblem Validation(string code, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [code] = [message]
        });
}
