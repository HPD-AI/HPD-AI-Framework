using System.Text.Json;
using HPD.Graph.Abstractions.Discovery;
using HPD.Graph.Hosting.Data;
using HPD.Graph.Hosting.Lifecycle;
using HPD.Graph.Hosting.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace HPD.Graph.AspNetCore.EndpointMapping;

public static class GraphEndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapHPDGraphWorkflows(this IEndpointRouteBuilder endpoints, string prefix = "/workflows")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var group = endpoints.MapGroup(prefix);

        group.MapPost("/", CreateWorkflowAsync);
        group.MapGet("/", ListWorkflowsAsync);
        group.MapGet("/handlers", GetHandlers);
        group.MapGet("/scheduled", ListScheduledGraphs);
        group.MapGet("/{graphId}", GetWorkflowAsync);
        group.MapPut("/{graphId}", UpdateWorkflowAsync);
        group.MapDelete("/{graphId}", DeleteWorkflowAsync);
        group.MapPost("/{graphId}/execute", ExecuteWorkflowAsync);
        group.MapGet("/{graphId}/executions/{executionId}", GetExecutionStatusAsync);
        group.MapGet("/{graphId}/executions/{executionId}/logs", StreamExecutionLogsAsync);
        group.MapPost("/{graphId}/executions/{executionId}/cancel", CancelExecutionAsync);
        group.MapGet("/{graphId}/executions/{executionId}/suspended-nodes", GetSuspendedNodesAsync);
        group.MapPost("/{graphId}/resume/{suspendToken}", ResumeSuspendedNodeAsync);
        group.MapGet("/{graphId}/polling-status/{suspendToken}", GetPollingStatusAsync);
        group.MapPost("/{graphId}/schedule", CreateSchedule);
        group.MapGet("/{graphId}/schedule", GetSchedule);
        group.MapPut("/{graphId}/schedule", UpdateSchedule);
        group.MapDelete("/{graphId}/schedule", DeleteSchedule);

        return group;
    }

    private static async Task<IResult> CreateWorkflowAsync(
        CreateWorkflowRequest request,
        GraphManager graphManager,
        CancellationToken ct)
    {
        var stored = await graphManager.CreateDefinitionAsync(request.Config, ct).ConfigureAwait(false);
        return Results.Created($"/workflows/{stored.GraphId}", WorkflowDtoMapper.ToWorkflowDto(stored));
    }

    private static async Task<IResult> ListWorkflowsAsync(GraphManager graphManager, CancellationToken ct)
    {
        var summaries = await graphManager.ListDefinitionsAsync(ct).ConfigureAwait(false);
        return Results.Ok(new WorkflowListResponse { Workflows = summaries });
    }

    private static IResult GetHandlers(IGeneratedHandlerCatalog catalog)
    {
        return Results.Ok(new HandlerCatalogResponse { Handlers = catalog.GetHandlers() });
    }

    private static async Task<IResult> GetWorkflowAsync(string graphId, GraphManager graphManager, CancellationToken ct)
    {
        var stored = await graphManager.GetDefinitionAsync(graphId, ct).ConfigureAwait(false);
        return stored is null
            ? Results.NotFound()
            : Results.Ok(WorkflowDtoMapper.ToWorkflowDto(stored));
    }

    private static async Task<IResult> UpdateWorkflowAsync(
        string graphId,
        UpdateWorkflowRequest request,
        GraphManager graphManager,
        CancellationToken ct)
    {
        try
        {
            var stored = await graphManager.UpdateDefinitionAsync(graphId, request.Config, ct).ConfigureAwait(false);
            return Results.Ok(WorkflowDtoMapper.ToWorkflowDto(stored));
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new ProblemDetails { Title = "Workflow not found", Detail = ex.Message });
        }
    }

    private static async Task<IResult> DeleteWorkflowAsync(string graphId, GraphManager graphManager, CancellationToken ct)
    {
        await graphManager.DeleteDefinitionAsync(graphId, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ExecuteWorkflowAsync(
        string graphId,
        ExecuteWorkflowRequest request,
        IWorkflowExecutionRunner executionRunner,
        CancellationToken ct)
    {
        try
        {
            var execution = await executionRunner.StartAsync(graphId, request, ct).ConfigureAwait(false);
            return request.Mode == WorkflowExecutionMode.Foreground && request.StartImmediately
                ? Results.Ok(execution)
                : Results.Accepted($"/workflows/{graphId}/executions/{execution.ExecutionId}", execution);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new ProblemDetails { Title = "Workflow not found", Detail = ex.Message });
        }
    }

    private static async Task<IResult> GetExecutionStatusAsync(
        string graphId,
        string executionId,
        ExecutionManager executionManager,
        CancellationToken ct)
    {
        var status = await executionManager.GetStatusAsync(graphId, executionId, ct).ConfigureAwait(false);
        return status is null ? Results.NotFound() : Results.Ok(status);
    }

    private static async Task StreamExecutionLogsAsync(
        string graphId,
        string executionId,
        ExecutionManager executionManager,
        HttpContext httpContext,
        CancellationToken ct)
    {
        httpContext.Response.ContentType = "text/event-stream";
        await foreach (var entry in executionManager.StreamLogsAsync(graphId, executionId, ct).ConfigureAwait(false))
        {
            var json = JsonSerializer.Serialize(entry, GraphHostingJsonSerializerContext.Default.GraphLogEntryDto);
            await httpContext.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
            await httpContext.Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task<IResult> CancelExecutionAsync(
        string graphId,
        string executionId,
        ExecutionManager executionManager,
        CancellationToken ct)
    {
        try
        {
            await executionManager.CancelAsync(graphId, executionId, ct).ConfigureAwait(false);
            return Results.Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new ProblemDetails { Title = "Execution not found", Detail = ex.Message });
        }
    }

    private static async Task<IResult> GetSuspendedNodesAsync(
        string graphId,
        string executionId,
        ExecutionManager executionManager,
        CancellationToken ct)
    {
        var suspended = await executionManager.GetSuspendedNodesAsync(graphId, executionId, ct).ConfigureAwait(false);
        return Results.Ok(suspended);
    }

    private static async Task<IResult> ResumeSuspendedNodeAsync(
        string graphId,
        string suspendToken,
        ResumeSuspensionRequest request,
        ExecutionManager executionManager,
        CancellationToken ct)
    {
        var result = await executionManager.ResumeSuspendedNodeAsync(graphId, suspendToken, request, ct).ConfigureAwait(false);
        return result.Status == ResumeSuspensionStatus.NotFound
            ? Results.NotFound(result)
            : Results.Ok(result);
    }

    private static async Task<IResult> GetPollingStatusAsync(
        string graphId,
        string suspendToken,
        ExecutionManager executionManager,
        CancellationToken ct)
    {
        var status = await executionManager.GetPollingStatusAsync(graphId, suspendToken, ct).ConfigureAwait(false);
        return status is null ? Results.NotFound() : Results.Ok(status);
    }

    private static async Task<IResult> ListScheduledGraphs(SchedulingManager schedulingManager, CancellationToken ct)
    {
        var schedules = await schedulingManager.ListSchedulesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new ScheduledGraphListResponse { Schedules = schedules });
    }

    private static async Task<IResult> CreateSchedule(
        string graphId,
        CreateScheduleRequest request,
        SchedulingManager schedulingManager,
        CancellationToken ct)
    {
        try
        {
            var schedule = await schedulingManager.CreateScheduleAsync(graphId, request, ct).ConfigureAwait(false);
            return Results.Created($"/workflows/{graphId}/schedule", schedule);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new ProblemDetails { Title = "Workflow not found", Detail = ex.Message });
        }
    }

    private static async Task<IResult> GetSchedule(
        string graphId,
        SchedulingManager schedulingManager,
        CancellationToken ct)
    {
        var schedule = await schedulingManager.GetScheduleAsync(graphId, ct).ConfigureAwait(false);
        return schedule is null ? Results.NotFound() : Results.Ok(schedule);
    }

    private static async Task<IResult> UpdateSchedule(
        string graphId,
        UpdateScheduleRequest request,
        SchedulingManager schedulingManager,
        CancellationToken ct)
    {
        try
        {
            var schedule = await schedulingManager.UpdateScheduleAsync(graphId, request, ct).ConfigureAwait(false);
            return Results.Ok(schedule);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new ProblemDetails { Title = "Schedule not found", Detail = ex.Message });
        }
    }

    private static async Task<IResult> DeleteSchedule(
        string graphId,
        SchedulingManager schedulingManager,
        CancellationToken ct)
    {
        await schedulingManager.DeleteScheduleAsync(graphId, ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}
