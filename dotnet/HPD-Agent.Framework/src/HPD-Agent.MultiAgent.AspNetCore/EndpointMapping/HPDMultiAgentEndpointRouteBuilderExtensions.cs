using System.Text.Json;
using HPD.Agent.MultiAgent.AspNetCore.EndpointMapping;
using HPD.Agent.MultiAgent.AspNetCore.Serialization;
using HPD.Graph.Hosting.Data;
using HPD.Graph.Hosting.Lifecycle;
using HPD.Graph.Hosting.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.MultiAgent.AspNetCore;

public static class HPDMultiAgentEndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapHPDMultiAgentApi(this IEndpointRouteBuilder endpoints)
        => endpoints.MapHPDMultiAgentApi(static _ => { });

    public static RouteGroupBuilder MapHPDMultiAgentApi(
        this IEndpointRouteBuilder endpoints,
        string routePrefix)
        => endpoints.MapHPDMultiAgentApi(options => options.RoutePrefix = routePrefix);

    public static RouteGroupBuilder MapHPDMultiAgentApi(
        this IEndpointRouteBuilder endpoints,
        Action<HPDMultiAgentEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new HPDMultiAgentEndpointOptions();
        configure?.Invoke(options);

        var group = endpoints.MapGroup(options.RoutePrefix);

        if (options.MapWorkflows)
        {
            group.MapGet("/workflows", (
                    GraphManager graphManager,
                    CancellationToken ct) =>
                    ListWorkflowsAsync(graphManager, options.IncludeGenericWorkflows, ct))
                .WithName("ListHPDMultiAgentWorkflows")
                .WithSummary("List HPD multi-agent workflow definitions");

            group.MapGet("/workflows/{workflowId}", GetWorkflowAsync)
                .WithName("GetHPDMultiAgentWorkflow")
                .WithSummary("Get an HPD multi-agent workflow definition");
        }

        if (options.MapRuns)
        {
            group.MapPost("/workflows/{workflowId}/runs", StartRunAsync)
                .WithName("StartHPDMultiAgentRun")
                .WithSummary("Start a multi-agent workflow run");

            group.MapGet("/workflows/{workflowId}/runs", ListRunsAsync)
                .WithName("ListHPDMultiAgentRuns")
                .WithSummary("List runs for a multi-agent workflow");

            group.MapGet("/workflows/{workflowId}/runs/{runId}", GetRunAsync)
                .WithName("GetHPDMultiAgentRun")
                .WithSummary("Get a multi-agent workflow run");

            group.MapPost("/workflows/{workflowId}/runs/{runId}/cancel", CancelRunAsync)
                .WithName("CancelHPDMultiAgentRun")
                .WithSummary("Cancel a multi-agent workflow run");

            group.MapGet("/workflows/{workflowId}/runs/{runId}/suspended-nodes", GetSuspendedNodesAsync)
                .WithName("GetHPDMultiAgentRunSuspendedNodes")
                .WithSummary("Get suspended nodes for a multi-agent workflow run");
        }

        if (options.MapEvents)
        {
            group.MapGet("/workflows/{workflowId}/runs/{runId}/events", StreamRunEventsAsync)
                .WithName("StreamHPDMultiAgentRunEvents")
                .WithSummary("Stream multi-agent workflow run events");
        }

        if (options.MapApprovals)
        {
            group.MapPost("/workflows/{workflowId}/runs/{runId}/approvals/{approvalId}", RespondToApprovalAsync)
                .WithName("RespondToHPDMultiAgentApproval")
                .WithSummary("Respond to a suspended multi-agent approval");
        }

        options.ConfigureRoutes?.Invoke(group);

        return group;
    }

    private static async Task<IResult> ListWorkflowsAsync(
        GraphManager graphManager,
        bool includeGenericWorkflows,
        CancellationToken ct)
    {
        var summaries = await graphManager.ListDefinitionsAsync(ct).ConfigureAwait(false);
        var workflows = new List<MultiAgentWorkflowSummaryDto>();

        foreach (var summary in summaries)
        {
            var stored = await graphManager.GetDefinitionAsync(summary.GraphId, ct).ConfigureAwait(false);
            if (stored is null)
            {
                continue;
            }

            var kind = GetKind(stored.Metadata);
            var isMultiAgent = IsMultiAgentWorkflow(stored.Metadata);
            if (!includeGenericWorkflows && !isMultiAgent)
            {
                continue;
            }

            workflows.Add(new MultiAgentWorkflowSummaryDto
            {
                WorkflowId = stored.GraphId,
                Name = stored.Name,
                GraphVersion = stored.GraphVersion,
                CreatedAt = stored.CreatedAt,
                UpdatedAt = stored.UpdatedAt,
                Description = stored.Description,
                IsMultiAgent = isMultiAgent,
                Kind = kind
            });
        }

        return Results.Ok(new MultiAgentWorkflowListResponse { Workflows = workflows });
    }

    private static async Task<IResult> GetWorkflowAsync(
        string workflowId,
        GraphManager graphManager,
        CancellationToken ct)
    {
        var stored = await graphManager.GetDefinitionAsync(workflowId, ct).ConfigureAwait(false);
        return stored is null
            ? Results.NotFound()
            : Results.Ok(WorkflowDtoMapper.ToWorkflowDto(stored));
    }

    private static async Task<IResult> StartRunAsync(
        string workflowId,
        ExecuteWorkflowRequest request,
        IWorkflowExecutionRunner executionRunner,
        CancellationToken ct)
    {
        try
        {
            var normalized = request with { TriggeredBy = request.TriggeredBy ?? "hpd-multi-agent-api" };
            var execution = await executionRunner.StartAsync(workflowId, normalized, ct).ConfigureAwait(false);
            return normalized.Mode == WorkflowExecutionMode.Foreground && normalized.StartImmediately
                ? Results.Ok(execution)
                : Results.Accepted($"/multi-agent/workflows/{workflowId}/runs/{execution.ExecutionId}", execution);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new ProblemDetails { Title = "Workflow not found", Detail = ex.Message });
        }
    }

    private static async Task<IResult> ListRunsAsync(
        string workflowId,
        ExecutionManager executionManager,
        CancellationToken ct)
    {
        var runs = await executionManager.ListExecutionsAsync(workflowId, ct).ConfigureAwait(false);
        return Results.Ok(new MultiAgentRunListResponse { Runs = runs });
    }

    private static async Task<IResult> GetRunAsync(
        string workflowId,
        string runId,
        ExecutionManager executionManager,
        CancellationToken ct)
    {
        var status = await executionManager.GetStatusAsync(workflowId, runId, ct).ConfigureAwait(false);
        return status is null ? Results.NotFound() : Results.Ok(status);
    }

    private static async Task<IResult> CancelRunAsync(
        string workflowId,
        string runId,
        ExecutionManager executionManager,
        CancellationToken ct)
    {
        try
        {
            await executionManager.CancelAsync(workflowId, runId, ct).ConfigureAwait(false);
            return Results.Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new ProblemDetails { Title = "Run not found", Detail = ex.Message });
        }
    }

    private static async Task<IResult> GetSuspendedNodesAsync(
        string workflowId,
        string runId,
        ExecutionManager executionManager,
        CancellationToken ct)
    {
        var suspended = await executionManager.GetSuspendedNodesAsync(workflowId, runId, ct).ConfigureAwait(false);
        return Results.Ok(suspended);
    }

    private static async Task StreamRunEventsAsync(
        string workflowId,
        string runId,
        ExecutionManager executionManager,
        HttpContext context,
        CancellationToken ct)
    {
        context.Response.ContentType = "text/event-stream";

        await foreach (var entry in executionManager.StreamLogsAsync(workflowId, runId, ct).ConfigureAwait(false))
        {
            var evt = new MultiAgentRunEventDto
            {
                Timestamp = entry.Timestamp,
                Kind = "graph-log",
                Level = entry.Level,
                Source = entry.Source,
                Message = entry.Message,
                NodeId = entry.NodeId,
                Exception = entry.Exception,
                Raw = JsonSerializer.SerializeToElement(
                    entry,
                    GraphHostingJsonSerializerContext.Default.GraphLogEntryDto)
            };

            var json = JsonSerializer.Serialize(
                evt,
                HPDMultiAgentAspNetCoreJsonSerializerContext.Default.MultiAgentRunEventDto);
            await context.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task<IResult> RespondToApprovalAsync(
        string workflowId,
        string runId,
        string approvalId,
        MultiAgentApprovalResponseRequest request,
        ExecutionManager executionManager,
        CancellationToken ct)
    {
        _ = runId;
        var result = await executionManager.ResumeSuspendedNodeAsync(
            workflowId,
            approvalId,
            new ResumeSuspensionRequest { ResumeValue = request.ResumeValue },
            ct).ConfigureAwait(false);

        return result.Status == ResumeSuspensionStatus.NotFound
            ? Results.NotFound(result)
            : Results.Ok(result);
    }

    private static bool IsMultiAgentWorkflow(IReadOnlyDictionary<string, string> metadata)
    {
        var kind = GetKind(metadata);
        return string.Equals(kind, "multi-agent", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(kind, "multi-agent-workflow", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetKind(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("kind", out var kind))
        {
            return kind;
        }

        if (metadata.TryGetValue("workspaceKind", out var workspaceKind))
        {
            return workspaceKind;
        }

        return null;
    }
}
