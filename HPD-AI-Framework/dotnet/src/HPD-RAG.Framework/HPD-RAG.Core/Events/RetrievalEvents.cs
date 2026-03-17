using HPD.Events;

namespace HPD.RAG.Core.Events;

/// <summary>
/// Emitted when a retrieval pipeline run begins.
/// </summary>
public sealed record RetrievalStartedEvent : MragEvent
{
    /// <summary>The natural-language query being retrieved.</summary>
    public required string Query { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when a retrieval pipeline run finishes.
/// </summary>
public sealed record RetrievalCompletedEvent : MragEvent
{
    /// <summary>The natural-language query that was retrieved.</summary>
    public required string Query { get; init; }

    /// <summary>Number of results in the final formatted context.</summary>
    public required int ResultCount { get; init; }

    /// <summary>Wall-clock duration of the retrieval run.</summary>
    public required TimeSpan Duration { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when a vector search handler (VectorSearchHandler or HybridSearchHandler)
/// finishes querying the vector store.
/// </summary>
public sealed record VectorSearchCompletedEvent : MragEvent
{
    /// <summary>Number of results returned from the vector store.</summary>
    public required int ResultCount { get; init; }

    /// <summary>Name of the collection that was searched.</summary>
    public required string CollectionName { get; init; }

    /// <summary>Top-K value used for the search.</summary>
    public required int TopK { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when a reranker handler finishes reranking retrieved results.
/// </summary>
public sealed record RerankedEvent : MragEvent
{
    /// <summary>Number of results fed into the reranker.</summary>
    public required int InputCount { get; init; }

    /// <summary>Number of results returned by the reranker (may be less than InputCount).</summary>
    public required int OutputCount { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when the FormatContextHandler finishes producing the formatted context string.
/// </summary>
public sealed record ContextFormattedEvent : MragEvent
{
    /// <summary>Output format used (e.g. "markdown", "xml", "plain").</summary>
    public required string Format { get; init; }

    /// <summary>Rough token estimate of the formatted context string.</summary>
    public required int TokenEstimate { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}
