using System.Text.Json;
using HPD.Graph.Hosting.Data;

namespace HPD.Agent.MultiAgent.AspNetCore.EndpointMapping;

public sealed record MultiAgentWorkflowListResponse
{
    public required IReadOnlyList<MultiAgentWorkflowSummaryDto> Workflows { get; init; }
}

public sealed record MultiAgentWorkflowSummaryDto
{
    public required string WorkflowId { get; init; }
    public required string Name { get; init; }
    public required string GraphVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? Description { get; init; }
    public bool IsMultiAgent { get; init; }
    public string? Kind { get; init; }
}

public sealed record MultiAgentRunListResponse
{
    public required IReadOnlyList<WorkflowExecutionDto> Runs { get; init; }
}

public sealed record MultiAgentRunEventDto
{
    public DateTimeOffset Timestamp { get; init; }
    public required string Kind { get; init; }
    public required string Level { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }
    public string? NodeId { get; init; }
    public string? Exception { get; init; }
    public JsonElement? Raw { get; init; }
}

public sealed record MultiAgentApprovalResponseRequest
{
    public object? ResumeValue { get; init; }
}
