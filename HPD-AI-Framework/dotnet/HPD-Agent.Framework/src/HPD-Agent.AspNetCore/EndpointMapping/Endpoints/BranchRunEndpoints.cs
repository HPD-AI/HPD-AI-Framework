using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

internal static class BranchRunEndpoints
{
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        IAgentBranchRunService branchRuns)
    {
        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/branches/{bid}/runs",
                (string agentId, string sid, string bid, CancellationToken ct) =>
                    ListRuns(agentId, sid, bid, branchRuns, ct))
            .WithName("ListBranchRuns")
            .WithSummary("List runtime-owned runs projected from a branch event log");

        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/branches/{bid}/runs/active",
                (string agentId, string sid, string bid, CancellationToken ct) =>
                    GetActiveRun(agentId, sid, bid, branchRuns, ct))
            .WithName("GetActiveBranchRun")
            .WithSummary("Get the currently active runtime-owned run for a branch");

        endpoints.MapGet("/agents/{agentId}/sessions/{sid}/branches/{bid}/runs/{runtimeRunId}",
                (string agentId, string sid, string bid, string runtimeRunId, CancellationToken ct) =>
                    GetRun(agentId, sid, bid, runtimeRunId, branchRuns, ct))
            .WithName("GetBranchRun")
            .WithSummary("Get a runtime-owned branch run by ID");
    }

    private static async Task<Results<Ok<List<BranchRunDto>>, NotFound, ValidationProblem>> ListRuns(
        string agentId,
        string sid,
        string bid,
        IAgentBranchRunService branchRuns,
        CancellationToken ct = default)
    {
        try
        {
            var result = await branchRuns.ListRunsAsync(agentId, sid, bid, ct).ConfigureAwait(false);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return Validation("ListBranchRunsError", ex.Message);
        }
    }

    private static async Task<Results<Ok<BranchRunDto?>, NotFound, ValidationProblem>> GetActiveRun(
        string agentId,
        string sid,
        string bid,
        IAgentBranchRunService branchRuns,
        CancellationToken ct = default)
    {
        try
        {
            var result = await branchRuns.GetActiveRunAsync(agentId, sid, bid, ct).ConfigureAwait(false);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value);
        }
        catch (Exception ex)
        {
            return Validation("GetActiveBranchRunError", ex.Message);
        }
    }

    private static async Task<Results<Ok<BranchRunDto>, NotFound, ValidationProblem>> GetRun(
        string agentId,
        string sid,
        string bid,
        string runtimeRunId,
        IAgentBranchRunService branchRuns,
        CancellationToken ct = default)
    {
        try
        {
            var result = await branchRuns.GetRunAsync(agentId, sid, bid, runtimeRunId, ct).ConfigureAwait(false);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!);
        }
        catch (Exception ex)
        {
            return Validation("GetBranchRunError", ex.Message);
        }
    }

    private static ValidationProblem Validation(string code, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [code] = [message]
        });
}
