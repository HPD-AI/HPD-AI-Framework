using HPD.Events;

namespace HPD.RAG.Core.Events;

/// <summary>
/// Emitted when an ingestion pipeline run begins.
/// </summary>
public sealed record IngestionStartedEvent : MragEvent
{
    /// <summary>Number of documents submitted for ingestion in this run.</summary>
    public required int DocumentCount { get; init; }

    /// <summary>Target vector store collection for this run.</summary>
    public required string CollectionName { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when an ingestion pipeline run finishes successfully.
/// </summary>
public sealed record IngestionCompletedEvent : MragEvent
{
    /// <summary>Number of documents submitted for ingestion.</summary>
    public required int DocumentCount { get; init; }

    /// <summary>Total number of chunks written to the vector store.</summary>
    public required int WrittenChunks { get; init; }

    /// <summary>Wall-clock duration of the ingestion run.</summary>
    public required TimeSpan Duration { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when a document reader handler finishes reading a single document.
/// </summary>
public sealed record DocumentReadEvent : MragEvent
{
    /// <summary>Identifier of the document that was read (typically the file path or URI).</summary>
    public required string DocumentId { get; init; }

    /// <summary>Number of structural elements extracted from the document.</summary>
    public required int ElementCount { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when a chunker handler finishes splitting a document into chunks.
/// </summary>
public sealed record ChunkingCompletedEvent : MragEvent
{
    /// <summary>Identifier of the document that was chunked.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Number of chunks produced for this document.</summary>
    public required int ChunkCount { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when an enricher handler (keyword, summary, sentiment, classification) finishes
/// processing the chunks for a single document.
/// </summary>
public sealed record EnrichmentCompletedEvent : MragEvent
{
    /// <summary>Identifier of the document whose chunks were enriched.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Number of chunks enriched for this document.</summary>
    public required int ChunkCount { get; init; }

    /// <summary>Display name of the enricher that completed (e.g. "EnrichKeywords").</summary>
    public required string EnricherName { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when an embedding handler (including image alt-text embedding) finishes
/// generating embeddings for a single document's chunks.
/// </summary>
public sealed record EmbeddingCompletedEvent : MragEvent
{
    /// <summary>Identifier of the document whose chunks were embedded.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Number of chunk embeddings produced.</summary>
    public required int ChunkCount { get; init; }

    /// <summary>Dimensionality of the produced embedding vectors.</summary>
    public required int Dimensions { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when a vector store writer handler finishes persisting a document's chunks.
/// </summary>
public sealed record DocumentWrittenEvent : MragEvent
{
    /// <summary>Identifier of the document whose chunks were written.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Number of chunks written to the vector store.</summary>
    public required int ChunkCount { get; init; }

    /// <summary>Name of the collection the chunks were written to.</summary>
    public required string CollectionName { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Synthetic event emitted after the main ingestion stream ends for every document
/// that was submitted but did not produce a <see cref="DocumentWrittenEvent"/>.
/// The most common reason is that the document was unchanged since the last run
/// and was deduplicated by the writer handler.
/// </summary>
public sealed record DocumentSkippedEvent : MragEvent
{
    /// <summary>Identifier of the document that was skipped.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Human-readable reason for skipping (e.g. "unchanged").</summary>
    public required string Reason { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Diagnostic;
}

/// <summary>
/// Emitted when a node fails to process a document and produces a
/// <see cref="HPDAgent.Graph.Abstractions.Execution.NodeExecutionResult.Failure"/> result.
/// </summary>
public sealed record DocumentFailedEvent : MragEvent
{
    /// <summary>Identifier of the document that caused the failure.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Graph node ID of the handler that failed.</summary>
    public required string NodeId { get; init; }

    /// <summary>Exception thrown by the failing node.</summary>
    public required Exception Exception { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Diagnostic;
}
