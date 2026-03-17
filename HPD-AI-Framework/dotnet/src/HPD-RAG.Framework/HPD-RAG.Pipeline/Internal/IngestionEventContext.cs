namespace HPD.RAG.Pipeline.Internal;

/// <summary>
/// Mutable context object that accumulates state across events during a single
/// ingestion pipeline streaming execution.  One instance is created per
/// <see cref="MragIngestionPipeline.RunStreamingAsync"/> call and passed into
/// <see cref="MragEventMapper.MapIngestionEvent"/> on every event.
/// </summary>
internal sealed class IngestionEventContext
{
    /// <summary>
    /// Target vector store collection for this run.
    /// Populated from <c>inputs["collection"]</c> or left empty.
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>
    /// Total number of documents submitted for ingestion in this run.
    /// Set once when the <c>GraphExecutionStartedEvent</c> is processed.
    /// </summary>
    public int TotalDocumentCount { get; set; }

    /// <summary>
    /// Running count of chunks written to the vector store.
    /// Incremented by <see cref="MragEventMapper.MapIngestionEvent"/> each time a
    /// <c>DocumentWrittenEvent</c> is produced.
    /// </summary>
    public int WrittenChunks { get; set; }

    /// <summary>
    /// Document IDs for which a <c>DocumentWrittenEvent</c> has been produced.
    /// Used after the main event loop to identify skipped documents.
    /// </summary>
    public HashSet<string> WrittenDocumentIds { get; } = new(StringComparer.Ordinal);
}
