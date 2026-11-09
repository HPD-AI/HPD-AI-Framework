using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace HPD.Agent.MultiAgent.AspNetCore;

/// <summary>Describes one workflow exposed by a multi-agent HTTP backend.</summary>
public sealed record HPDMultiAgentWorkflow
{
    public required string WorkflowId { get; init; }
    public required string WorkflowVersion { get; init; }
    public required int DefinitionVersion { get; init; }
    public required string Checksum { get; init; }
}

/// <summary>Contains one validated multi-agent run submission.</summary>
public sealed record HPDMultiAgentRunCommand
{
    public required string WorkflowId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required JsonElement Input { get; init; }
    public string? ExecutionId { get; init; }
    public DateTimeOffset? DueAt { get; init; }
}

/// <summary>Reports the durable identity accepted for one workflow run.</summary>
public sealed record HPDMultiAgentRunAdmission
{
    public required string ExecutionId { get; init; }
    public required string ActivationId { get; init; }
    public required string State { get; init; }
    public required string Disposition { get; init; }
}

/// <summary>Contains one backend submission result.</summary>
public sealed record HPDMultiAgentRunSubmission
{
    public required int StatusCode { get; init; }
    public HPDMultiAgentRunAdmission? Admission { get; init; }
    public string? ErrorCode { get; init; }

    public bool IsSuccess => StatusCode is >= 200 and < 300 && Admission is not null;

    public static HPDMultiAgentRunSubmission Accepted(HPDMultiAgentRunAdmission admission) => new()
    {
        StatusCode = StatusCodes.Status202Accepted,
        Admission = admission,
    };

    public static HPDMultiAgentRunSubmission Failed(int statusCode, string errorCode) => new()
    {
        StatusCode = statusCode,
        ErrorCode = errorCode,
    };
}

/// <summary>
/// Provider-neutral backend used by the multi-agent HTTP routes to list workflows
/// and submit durable runs.
/// </summary>
public interface IHPDMultiAgentWorkflowBackend
{
    IReadOnlyList<HPDMultiAgentWorkflow> Workflows { get; }

    ValueTask<HPDMultiAgentRunSubmission> EnqueueAsync(
        HPDMultiAgentRunCommand command,
        HttpContext context,
        CancellationToken cancellationToken);
}

/// <summary>Defines HTTP headers owned by the provider-neutral multi-agent API.</summary>
public static class HPDMultiAgentHttpHeaders
{
    public const string IdempotencyKey = "Idempotency-Key";
}
