using System.Text.Json;

namespace HPD.RAG.Core.DTOs;

/// <summary>
/// Checkpoint-safe representation of a graph node (property graph databases).
/// </summary>
public sealed record MragGraphNodeDto
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public Dictionary<string, JsonElement>? Properties { get; init; }
}

/// <summary>
/// Checkpoint-safe representation of a graph edge (relationship).
/// </summary>
public sealed record MragGraphEdgeDto
{
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public required string Type { get; init; }
    public Dictionary<string, JsonElement>? Properties { get; init; }
}

/// <summary>
/// Checkpoint-safe subgraph result from graph retrieval.
/// </summary>
public sealed record MragGraphResultDto
{
    public required MragGraphNodeDto[] Nodes { get; init; }
    public required MragGraphEdgeDto[] Edges { get; init; }

    /// <summary>
    /// True when the traversal hit the configured limit.
    /// Callers may want to note to the LLM that graph context may be incomplete.
    /// </summary>
    public bool IsTruncated { get; init; }
}
