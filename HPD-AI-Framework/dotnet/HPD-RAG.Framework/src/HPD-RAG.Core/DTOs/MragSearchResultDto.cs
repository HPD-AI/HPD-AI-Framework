using System.Text.Json;

namespace HPD.RAG.Core.DTOs;

/// <summary>
/// Checkpoint-safe representation of a vector search result.
/// </summary>
public sealed record MragSearchResultDto
{
    public required string DocumentId { get; init; }
    public required string Content { get; init; }
    public string? Context { get; init; }
    public required double Score { get; init; }
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}
