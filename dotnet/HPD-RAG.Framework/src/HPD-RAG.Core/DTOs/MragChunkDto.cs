using System.Text.Json;

namespace HPD.RAG.Core.DTOs;

/// <summary>
/// Checkpoint-safe representation of a document chunk.
/// All metadata values use JsonElement for type-safe roundtripping without reflection.
/// </summary>
public sealed record MragChunkDto
{
    public required string DocumentId { get; init; }
    public required string Content { get; init; }

    /// <summary>Header/section context from the source document structure.</summary>
    public string? Context { get; init; }

    /// <summary>
    /// Arbitrary metadata produced by enrichers (keywords, summary, sentiment, etc.).
    /// JsonElement values roundtrip safely through HPD.Graph checkpoints.
    /// Handlers extract typed values with .GetString(), .GetInt32(), etc.
    /// </summary>
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}
