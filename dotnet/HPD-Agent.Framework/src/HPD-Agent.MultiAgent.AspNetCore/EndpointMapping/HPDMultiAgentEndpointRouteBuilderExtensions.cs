using System.Text.Json;
using HPD.Agent.MultiAgent.AspNetCore.EndpointMapping;
using HPD.Agent.MultiAgent.AspNetCore.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.MultiAgent.AspNetCore;

/// <summary>Maps multi-agent discovery and durable activation enqueue routes.</summary>
public static class HPDMultiAgentEndpointRouteBuilderExtensions
{
    /// <summary>Maps routes using the default prefix.</summary>
    public static RouteGroupBuilder MapHPDMultiAgentApi(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHPDMultiAgentApi(static _ => { });

    /// <summary>Maps routes using a selected prefix.</summary>
    public static RouteGroupBuilder MapHPDMultiAgentApi(this IEndpointRouteBuilder endpoints, string routePrefix) =>
        endpoints.MapHPDMultiAgentApi(options => options.RoutePrefix = routePrefix);

    /// <summary>Maps graph-installed workflow discovery and enqueue routes.</summary>
    public static RouteGroupBuilder MapHPDMultiAgentApi(
        this IEndpointRouteBuilder endpoints,
        Action<HPDMultiAgentEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = new HPDMultiAgentEndpointOptions();
        configure?.Invoke(options);
        IHPDMultiAgentWorkflowBackend[] backends = endpoints.ServiceProvider
            .GetServices<IHPDMultiAgentWorkflowBackend>()
            .ToArray();
        if (backends.Length == 0)
        {
            throw new InvalidOperationException(
                "Register at least one IHPDMultiAgentWorkflowBackend before mapping the multi-agent API.");
        }

        (IHPDMultiAgentWorkflowBackend Backend, HPDMultiAgentWorkflow Workflow)[] workflows = backends
            .SelectMany(static backend => backend.Workflows.Select(workflow => (Backend: backend, Workflow: workflow)))
            .OrderBy(static item => item.Workflow.WorkflowId, StringComparer.Ordinal)
            .ToArray();
        if (workflows.Select(static item => item.Workflow.WorkflowId).Distinct(StringComparer.Ordinal).Count() != workflows.Length)
        {
            throw new InvalidOperationException("Multi-agent workflow identifiers must be unique across registered backends.");
        }

        var group = endpoints.MapGroup(options.RoutePrefix);
        group.MapGet("/workflows", () => Results.Json(new MultiAgentWorkflowListResponse
        {
            Workflows = workflows.Select(static item => new MultiAgentWorkflowSummaryDto
            {
                WorkflowId = item.Workflow.WorkflowId,
                GraphVersion = item.Workflow.WorkflowVersion,
                DefinitionVersion = item.Workflow.DefinitionVersion,
                GraphChecksum = item.Workflow.Checksum,
            }).ToArray(),
        }, HPDMultiAgentAspNetCoreJsonSerializerContext.Default.MultiAgentWorkflowListResponse));
        foreach ((IHPDMultiAgentWorkflowBackend backend, HPDMultiAgentWorkflow workflow) in workflows)
        {
            IHPDMultiAgentWorkflowBackend capturedBackend = backend;
            HPDMultiAgentWorkflow capturedWorkflow = workflow;
            group.MapPost(
                $"/workflows/{workflow.WorkflowId}/runs",
                (RequestDelegate)(context => StartRunAsync(context, capturedBackend, capturedWorkflow)));
        }
        options.ConfigureRoutes?.Invoke(group);
        return group;
    }

    private static async Task StartRunAsync(
        HttpContext context,
        IHPDMultiAgentWorkflowBackend backend,
        HPDMultiAgentWorkflow workflow)
    {
        MultiAgentRunRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                HPDMultiAgentAspNetCoreJsonSerializerContext.Default.MultiAgentRunRequest,
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false);
            return;
        }
        if (request is null || !context.Request.Headers.TryGetValue(HPDMultiAgentHttpHeaders.IdempotencyKey, out var keys)
            || keys.Count != 1 || request.DueAtUnixMilliseconds is < 0)
        {
            await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false);
            return;
        }

        DateTimeOffset? dueAt = null;
        if (request.DueAtUnixMilliseconds is long dueAtUnixMilliseconds)
        {
            try
            {
                dueAt = DateTimeOffset.FromUnixTimeMilliseconds(dueAtUnixMilliseconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false);
                return;
            }
        }

        HPDMultiAgentRunSubmission result = await backend.EnqueueAsync(new HPDMultiAgentRunCommand
        {
            WorkflowId = workflow.WorkflowId,
            IdempotencyKey = keys[0]!,
            Input = request.Input,
            ExecutionId = request.ExecutionId,
            DueAt = dueAt,
        }, context, context.RequestAborted).ConfigureAwait(false);

        if (!result.IsSuccess || result.Admission is null)
        {
            await Problem(
                context,
                result.StatusCode,
                result.ErrorCode ?? "multi-agent.activation.storeError").ConfigureAwait(false);
            return;
        }
        await Results.Json(new MultiAgentRunAcceptedResult
        {
            ExecutionId = result.Admission.ExecutionId,
            ActivationId = result.Admission.ActivationId,
            State = result.Admission.State,
            Disposition = result.Admission.Disposition,
        }, HPDMultiAgentAspNetCoreJsonSerializerContext.Default.MultiAgentRunAcceptedResult,
            statusCode: StatusCodes.Status202Accepted)
            .ExecuteAsync(context).ConfigureAwait(false);
    }

    private static Task Problem(HttpContext context, int status, string code) => Results.Problem(
        statusCode: status,
        title: "The durable multi-agent run request failed.",
        extensions: new Dictionary<string, object?> { ["hpd.error.code"] = code }).ExecuteAsync(context);
}
