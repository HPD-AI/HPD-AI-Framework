using System.Text.Json;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.Retrieval.Internal;

/// <summary>
/// Standard vector record shape used by VectorSearchHandler and HybridSearchHandler.
/// All MRAG vector store backends must store records matching this schema.
/// Dimensions = 0 means the dimension is determined at runtime by the backend configuration.
/// </summary>
internal sealed class MragVectorRecord
{
    [VectorStoreKey]
    public required string DocumentId { get; init; }

    [VectorStoreData(IsFullTextIndexed = true)]
    public required string Content { get; init; }

    [VectorStoreData]
    public string? Context { get; init; }

    [VectorStoreVector(1536)]
    public ReadOnlyMemory<float> Embedding { get; init; }

    [VectorStoreData]
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}
