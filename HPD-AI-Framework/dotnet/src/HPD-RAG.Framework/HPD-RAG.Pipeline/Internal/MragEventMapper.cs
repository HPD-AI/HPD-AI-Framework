using HPD.Events;
using HPD.RAG.Core.Events;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;

namespace HPD.RAG.Pipeline.Internal;

/// <summary>
/// Maps raw HPD.Graph events to strongly-typed MRAG domain events.
/// Called from the streaming loops in <see cref="MragIngestionPipeline"/>,
/// <see cref="MragRetrievalPipeline"/>, and <see cref="MragEvaluationPipeline"/>.
///
/// <para>
/// Returns <c>null</c> when an event should be silently discarded (no yield).
/// Returns a <see cref="MragRawGraphEvent"/> passthrough for events that have
/// no MRAG-specific meaning but should still be observable by consumers.
/// </para>
///
/// <para>
/// Pattern mirrors <c>AgentWorkflowInstance.WrapGraphEvent</c> in HPD.MultiAgent.
/// </para>
/// </summary>
internal static class MragEventMapper
{
    // ------------------------------------------------------------------ //
    // Ingestion                                                            //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Maps a raw graph <paramref name="rawEvent"/> to an ingestion-domain MRAG event.
    /// </summary>
    /// <param name="rawEvent">The raw event object from the graph orchestrator.</param>
    /// <param name="pipelineName">Name of the owning pipeline (set on the returned event).</param>
    /// <param name="ctx">Mutable ingestion context — updated for accounting events (WrittenChunks, etc.).</param>
    /// <returns>
    /// A strongly-typed <see cref="MragEvent"/>, a <see cref="MragRawGraphEvent"/> passthrough,
    /// or <c>null</c> to discard the event entirely.
    /// </returns>
    public static MragEvent? MapIngestionEvent(object rawEvent, string pipelineName, IngestionEventContext ctx)
    {
        return rawEvent switch
        {
            // ── Graph lifecycle ──────────────────────────────────────────
            GraphExecutionStartedEvent g => new IngestionStartedEvent
            {
                PipelineName   = pipelineName,
                DocumentCount  = g.NodeCount,
                CollectionName = ctx.CollectionName
            },

            GraphExecutionCompletedEvent g => new IngestionCompletedEvent
            {
                PipelineName   = pipelineName,
                DocumentCount  = ctx.TotalDocumentCount,
                WrittenChunks  = ctx.WrittenChunks,
                Duration       = g.Duration
            },

            // ── Node started — pass through (no domain value, but observable) ──
            NodeExecutionStartedEvent => null,

            // ── Node completed — classify by HandlerName ─────────────────
            NodeExecutionCompletedEvent n when n.Result is NodeExecutionResult.Failure f =>
                new DocumentFailedEvent
                {
                    PipelineName = pipelineName,
                    DocumentId   = ExtractDocumentId(n.Outputs, n.NodeId),
                    NodeId       = n.NodeId,
                    Exception    = f.Exception
                },

            NodeExecutionCompletedEvent n when IsReader(n.HandlerName) =>
                new DocumentReadEvent
                {
                    PipelineName = pipelineName,
                    DocumentId   = ExtractDocumentId(n.Outputs, n.NodeId),
                    ElementCount = ExtractInt(n.Outputs, "ElementCount", "element_count", "Count", 0)
                },

            NodeExecutionCompletedEvent n when IsChunker(n.HandlerName) =>
                new ChunkingCompletedEvent
                {
                    PipelineName = pipelineName,
                    DocumentId   = ExtractDocumentId(n.Outputs, n.NodeId),
                    ChunkCount   = ExtractInt(n.Outputs, "ChunkCount", "chunk_count", "Count", 0)
                },

            NodeExecutionCompletedEvent n when IsImageEmbedder(n.HandlerName) =>
                new EmbeddingCompletedEvent
                {
                    PipelineName = pipelineName,
                    DocumentId   = ExtractDocumentId(n.Outputs, n.NodeId),
                    ChunkCount   = ExtractInt(n.Outputs, "ChunkCount", "chunk_count", "Count", 0),
                    Dimensions   = ExtractInt(n.Outputs, "Dimensions", "dimensions", "Dim", 0)
                },

            NodeExecutionCompletedEvent n when IsEnricher(n.HandlerName) =>
                new EnrichmentCompletedEvent
                {
                    PipelineName = pipelineName,
                    DocumentId   = ExtractDocumentId(n.Outputs, n.NodeId),
                    ChunkCount   = ExtractInt(n.Outputs, "ChunkCount", "chunk_count", "Count", 0),
                    EnricherName = n.HandlerName
                },

            NodeExecutionCompletedEvent n when IsWriter(n.HandlerName) =>
                ProduceDocumentWrittenEvent(n, pipelineName, ctx),

            // ── Node skipped — pass through ──────────────────────────────
            NodeSkippedEvent => new MragRawGraphEvent { PipelineName = pipelineName, UnderlyingEvent = rawEvent },

            // ── All other events — pass through ──────────────────────────
            _ => new MragRawGraphEvent { PipelineName = pipelineName, UnderlyingEvent = rawEvent }
        };
    }

    // ------------------------------------------------------------------ //
    // Retrieval                                                            //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Maps a raw graph <paramref name="rawEvent"/> to a retrieval-domain MRAG event.
    /// </summary>
    public static MragEvent? MapRetrievalEvent(object rawEvent, string pipelineName, string query)
    {
        return rawEvent switch
        {
            GraphExecutionStartedEvent => new RetrievalStartedEvent
            {
                PipelineName = pipelineName,
                Query        = query
            },

            GraphExecutionCompletedEvent g => new RetrievalCompletedEvent
            {
                PipelineName = pipelineName,
                Query        = query,
                ResultCount  = 0,   // Not available from graph-level summary; FormatContext node provides this
                Duration     = g.Duration
            },

            NodeExecutionCompletedEvent n when n.HandlerName is
                MragHandlerNames.VectorSearch or MragHandlerNames.HybridSearch =>
                new VectorSearchCompletedEvent
                {
                    PipelineName   = pipelineName,
                    ResultCount    = ExtractInt(n.Outputs, "ResultCount", "result_count", "Count", 0),
                    CollectionName = ExtractString(n.Outputs, "CollectionName", "collection_name") ?? string.Empty,
                    TopK           = ExtractInt(n.Outputs, "TopK", "top_k", "TopK", 0)
                },

            NodeExecutionCompletedEvent n when n.HandlerName == MragHandlerNames.Rerank =>
                new RerankedEvent
                {
                    PipelineName = pipelineName,
                    InputCount   = ExtractInt(n.Outputs, "InputCount", "input_count", "Count", 0),
                    OutputCount  = ExtractInt(n.Outputs, "OutputCount", "output_count", "ResultCount", 0)
                },

            NodeExecutionCompletedEvent n when n.HandlerName == MragHandlerNames.FormatContext =>
                new ContextFormattedEvent
                {
                    PipelineName  = pipelineName,
                    Format        = ExtractString(n.Outputs, "Format", "format") ?? "plain",
                    TokenEstimate = ExtractInt(n.Outputs, "TokenEstimate", "token_estimate", "Tokens", 0)
                },

            NodeExecutionStartedEvent => null,

            _ => new MragRawGraphEvent { PipelineName = pipelineName, UnderlyingEvent = rawEvent }
        };
    }

    // ------------------------------------------------------------------ //
    // Evaluation                                                           //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Maps a raw graph <paramref name="rawEvent"/> to an evaluation-domain MRAG event.
    /// </summary>
    /// <param name="rawEvent">The raw event object from the graph orchestrator.</param>
    /// <param name="pipelineName">Name of the owning pipeline.</param>
    /// <param name="scoreAccumulator">
    /// Mutable dictionary used to accumulate per-partition scores for computing averages
    /// in <see cref="EvalCompletedEvent"/>. Callers own this dictionary across the entire backfill run.
    /// </param>
    public static MragEvent? MapEvaluationEvent(
        object rawEvent,
        string pipelineName,
        List<IReadOnlyDictionary<string, double>> scoreAccumulator)
    {
        return rawEvent switch
        {
            BackfillStartedEvent b => new EvalStartedEvent
            {
                PipelineName  = pipelineName,
                ScenarioCount = b.TotalPartitions
            },

            BackfillPartitionCompletedEvent b =>
                ProduceEvalPartitionEvent(b, pipelineName, scoreAccumulator),

            BackfillCompletedEvent b =>
                ProduceEvalCompletedEvent(b, pipelineName, scoreAccumulator),

            NodeExecutionStartedEvent => null,

            _ => new MragRawGraphEvent { PipelineName = pipelineName, UnderlyingEvent = rawEvent }
        };
    }

    // ------------------------------------------------------------------ //
    // Private helpers                                                      //
    // ------------------------------------------------------------------ //

    /// <summary>Checks whether a handler name indicates a document reader node.</summary>
    private static bool IsReader(string handlerName) =>
        handlerName.StartsWith("Read", StringComparison.Ordinal);

    /// <summary>Checks whether a handler name indicates a chunker node.</summary>
    private static bool IsChunker(string handlerName) =>
        handlerName.StartsWith("Chunk", StringComparison.Ordinal);

    /// <summary>
    /// Checks whether a handler name indicates an enricher or classifier node.
    /// Note: EnrichImages is handled separately via <see cref="IsImageEmbedder"/>
    /// before this predicate is evaluated in the match arms, so it never falls through here.
    /// </summary>
    private static bool IsEnricher(string handlerName) =>
        handlerName.StartsWith("Enrich", StringComparison.Ordinal) ||
        handlerName.StartsWith("Classify", StringComparison.Ordinal);

    /// <summary>
    /// Checks whether a handler name specifically indicates the image enricher node,
    /// which doubles as an embedding-adjacent handler and therefore produces an
    /// <see cref="EmbeddingCompletedEvent"/> rather than an <see cref="EnrichmentCompletedEvent"/>.
    /// </summary>
    private static bool IsImageEmbedder(string handlerName) =>
        handlerName == MragHandlerNames.EnrichImages;

    /// <summary>Checks whether a handler name indicates a vector store writer node.</summary>
    private static bool IsWriter(string handlerName) =>
        handlerName.StartsWith("Write", StringComparison.Ordinal);

    /// <summary>
    /// Produces a <see cref="DocumentWrittenEvent"/> and updates the running totals in
    /// <paramref name="ctx"/>.
    /// </summary>
    private static DocumentWrittenEvent ProduceDocumentWrittenEvent(
        NodeExecutionCompletedEvent n,
        string pipelineName,
        IngestionEventContext ctx)
    {
        var documentId  = ExtractDocumentId(n.Outputs, n.NodeId);
        var chunkCount  = ExtractInt(n.Outputs, "ChunkCount", "chunk_count", "Count", 0);
        var collection  = ExtractString(n.Outputs, "CollectionName", "collection_name")
                          ?? ctx.CollectionName;

        ctx.WrittenChunks += chunkCount;
        ctx.WrittenDocumentIds.Add(documentId);

        return new DocumentWrittenEvent
        {
            PipelineName   = pipelineName,
            DocumentId     = documentId,
            ChunkCount     = chunkCount,
            CollectionName = collection
        };
    }

    /// <summary>
    /// Produces an <see cref="EvalPartitionCompletedEvent"/> and records the scores in the
    /// accumulator for later averaging.
    /// </summary>
    private static EvalPartitionCompletedEvent ProduceEvalPartitionEvent(
        BackfillPartitionCompletedEvent b,
        string pipelineName,
        List<IReadOnlyDictionary<string, double>> accumulator)
    {
        // Extract per-metric scores from the artifact if it carries an MragMetricsDto.
        var scores = ExtractScores(b.Artifact);
        accumulator.Add(scores);

        var segments = b.Partition.Dimensions;
        return new EvalPartitionCompletedEvent
        {
            PipelineName  = pipelineName,
            ScenarioName  = segments.Count > 0 ? segments[0] : string.Empty,
            IterationName = segments.Count > 1 ? segments[1] : string.Empty,
            Scores        = scores
        };
    }

    /// <summary>
    /// Produces an <see cref="EvalCompletedEvent"/> with macro-averaged scores over all
    /// successfully collected partitions.
    /// </summary>
    private static EvalCompletedEvent ProduceEvalCompletedEvent(
        BackfillCompletedEvent b,
        string pipelineName,
        List<IReadOnlyDictionary<string, double>> accumulator)
    {
        var averages = ComputeAverages(accumulator);
        return new EvalCompletedEvent
        {
            PipelineName  = pipelineName,
            AverageScores = averages,
            FailedCount   = b.FailedPartitions
        };
    }

    // ------------------------------------------------------------------ //
    // Output socket value extraction helpers                              //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Tries to read a document ID from node outputs.  Falls back to the node ID
    /// when no recognised key is present.
    /// </summary>
    private static string ExtractDocumentId(
        IReadOnlyDictionary<string, object>? outputs,
        string fallbackNodeId)
    {
        if (outputs is null) return fallbackNodeId;

        foreach (var key in new[] { "DocumentId", "document_id", "Id", "id", "FilePath", "file_path" })
        {
            if (outputs.TryGetValue(key, out var val) && val is string s && !string.IsNullOrEmpty(s))
                return s;
        }

        return fallbackNodeId;
    }

    /// <summary>Reads an integer value from node outputs by trying multiple candidate keys.</summary>
    private static int ExtractInt(
        IReadOnlyDictionary<string, object>? outputs,
        string key1,
        string key2,
        string key3,
        int fallback)
    {
        if (outputs is null) return fallback;

        foreach (var key in new[] { key1, key2, key3 })
        {
            if (outputs.TryGetValue(key, out var val))
            {
                if (val is int i) return i;
                if (val is long l) return (int)l;
                if (val is double d) return (int)d;
                if (val is string s && int.TryParse(s, out var parsed)) return parsed;
            }
        }

        return fallback;
    }

    /// <summary>Reads a string value from node outputs by trying multiple candidate keys.</summary>
    private static string? ExtractString(
        IReadOnlyDictionary<string, object>? outputs,
        string key1,
        string key2)
    {
        if (outputs is null) return null;

        foreach (var key in new[] { key1, key2 })
        {
            if (outputs.TryGetValue(key, out var val) && val is string s && !string.IsNullOrEmpty(s))
                return s;
        }

        return null;
    }

    /// <summary>
    /// Extracts a metric scores dictionary from a backfill artifact.
    /// Handles <c>MragMetricsDto</c> (the canonical form) and falls back gracefully
    /// to an empty dictionary for unknown artifact types.
    /// </summary>
    private static IReadOnlyDictionary<string, double> ExtractScores(object? artifact)
    {
        if (artifact is Core.DTOs.MragMetricsDto metrics)
            return metrics.Scores;

        if (artifact is IReadOnlyDictionary<string, double> direct)
            return direct;

        if (artifact is Dictionary<string, double> dict)
            return dict;

        return new Dictionary<string, double>();
    }

    /// <summary>
    /// Macro-averages each metric across all collected score dictionaries.
    /// </summary>
    private static IReadOnlyDictionary<string, double> ComputeAverages(
        List<IReadOnlyDictionary<string, double>> accumulator)
    {
        if (accumulator.Count == 0)
            return new Dictionary<string, double>();

        // Collect all unique metric names
        var allKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scores in accumulator)
            foreach (var key in scores.Keys)
                allKeys.Add(key);

        var result = new Dictionary<string, double>(allKeys.Count, StringComparer.Ordinal);
        foreach (var key in allKeys)
        {
            double sum   = 0;
            int    count = 0;
            foreach (var scores in accumulator)
            {
                if (scores.TryGetValue(key, out var v))
                {
                    sum += v;
                    count++;
                }
            }

            result[key] = count > 0 ? sum / count : 0;
        }

        return result;
    }
}
