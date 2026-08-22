using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Agent.MultiAgent.AspNetCore.EndpointMapping;

/// <summary>Lists graph-installed multi-agent workflow definitions.</summary>
public sealed record MultiAgentWorkflowListResponse
{
    /// <summary>Gets the immutable installed workflow descriptions.</summary>
    public required IReadOnlyList<MultiAgentWorkflowSummaryDto> Workflows { get; init; }
}

/// <summary>Describes one graph-installed multi-agent workflow.</summary>
public sealed record MultiAgentWorkflowSummaryDto
{
    /// <summary>Gets the stable graph identity.</summary>
    public required string WorkflowId { get; init; }
    /// <summary>Gets the graph semantic version.</summary>
    public required string GraphVersion { get; init; }
    /// <summary>Gets the activation-definition version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the lowercase graph checksum.</summary>
    public required string GraphChecksum { get; init; }
}

/// <summary>Requests one identified durable multi-agent graph run.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MultiAgentRunRequest
{
    /// <summary>Gets an optional caller-owned execution identity.</summary>
    public string? ExecutionId { get; init; }
    /// <summary>Gets the graph input value.</summary>
    public required JsonElement Input { get; init; }
    /// <summary>Gets an optional requested due instant as Unix milliseconds.</summary>
    public long? DueAtUnixMilliseconds { get; init; }
}

/// <summary>Returns durable activation authority for one accepted graph run.</summary>
public sealed record MultiAgentRunAcceptedResult
{
    /// <summary>Gets the logical graph execution identity.</summary>
    public required string ExecutionId { get; init; }
    /// <summary>Gets the durable activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the durable activation state.</summary>
    public required BaseActivationState State { get; init; }
    /// <summary>Gets whether the request committed or exactly replayed.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}
