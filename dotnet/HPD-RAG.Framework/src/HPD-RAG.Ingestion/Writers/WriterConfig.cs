using Microsoft.Extensions.VectorData;

namespace HPD.RAG.Ingestion.Writers;

/// <summary>
/// Shared configuration record inherited by all vector-store writer handlers.
/// </summary>
public class WriterConfig
{
    /// <summary>Target collection name. Overrides <c>MragPipelineContext.CollectionName</c> when set.</summary>
    public string? CollectionName { get; set; }

    /// <summary>Embedding vector dimensionality. Must match the embedding model.</summary>
    public int Dimensions { get; set; } = 1536;

    /// <summary>Distance function for the vector index. Use <see cref="DistanceFunction"/> constants.</summary>
    public string? DistanceFunction { get; set; } = Microsoft.Extensions.VectorData.DistanceFunction.CosineSimilarity;

    /// <summary>Index kind for the vector store. Use <see cref="IndexKind"/> constants.</summary>
    public string? IndexKind { get; set; } = Microsoft.Extensions.VectorData.IndexKind.Hnsw;

    /// <summary>
    /// When true, existing records with the same ID are updated rather than inserted.
    /// All writers use upsert semantics; this flag controls whether the collection
    /// creation step should expect the collection may already exist.
    /// </summary>
    public bool IncrementalIngestion { get; set; } = false;
}
