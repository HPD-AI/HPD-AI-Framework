using Microsoft.Extensions.VectorData;

namespace HPD.RAG.Ingestion.Writers;

/// <summary>
/// Vector store record written by all MRAG writer handlers.
/// </summary>
internal sealed class MragVectorRecord
{
    [VectorStoreKey]
    public required string Id { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public required string Content { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string? Context { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string? Tags { get; set; }

    /// <summary>Serialized JSON of MragChunkDto.Metadata dictionary.</summary>
    [VectorStoreData]
    public string? Metadata { get; set; }

    [VectorStoreVector(1536)]
    public required float[] Embedding { get; set; }
}
