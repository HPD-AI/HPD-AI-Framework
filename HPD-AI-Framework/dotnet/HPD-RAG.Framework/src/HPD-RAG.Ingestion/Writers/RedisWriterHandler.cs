using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using HPDAgent.Graph.Abstractions.Attributes;

namespace HPD.RAG.Ingestion.Writers;

/// <summary>Writes chunks to a Redis Stack (RediSearch / VSS) collection.</summary>
[GraphNodeHandler(NodeName = "WriteRedis")]
public sealed partial class RedisWriterHandler
{
    public static MragRetryPolicy DefaultRetry { get; } = VectorWriterBase.DefaultRetry;
    public static MragErrorPropagation DefaultPropagation { get; } = VectorWriterBase.DefaultPropagation;

    public async Task<Output> ExecuteAsync(
        [InputSocket(Description = "Chunks to write")] MragChunkDto[] Chunks,
        MragPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        var config = GetNodeConfig();
        int written = await VectorWriterBase.WriteAsync(Chunks, context, config, cancellationToken)
            .ConfigureAwait(false);
        return new Output { WrittenCount = written };
    }

    public sealed class Config : WriterConfig { }

    public sealed record Output
    {
        [OutputSocket(Description = "Number of records upserted")]
        public int WrittenCount { get; init; }
    }
}
