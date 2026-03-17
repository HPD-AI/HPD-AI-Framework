using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using HPD.RAG.Ingestion.Internal;
using HPDAgent.Graph.Abstractions.Attributes;

namespace HPD.RAG.Ingestion.Chunkers;

/// <summary>
/// Splits each document's full text into fixed-size token windows with overlap.
/// Concatenates all text-bearing elements and slides a window of
/// <see cref="Config.MaxTokensPerChunk"/> tokens with a step of
/// <c>MaxTokensPerChunk - OverlapTokens</c>.
/// Output is a jagged array: one inner array per document.
/// </summary>
[GraphNodeHandler(NodeName = "ChunkByToken")]
public sealed partial class TokenChunkerHandler
{
    /// <summary>Default retry: 2 attempts, Constant, 500ms.</summary>
    public static MragRetryPolicy DefaultRetry { get; } = new()
    {
        MaxAttempts = 2,
        InitialDelay = TimeSpan.FromMilliseconds(500),
        Strategy = MragBackoffStrategy.Constant
    };

    /// <summary>Default propagation: StopPipeline.</summary>
    public static MragErrorPropagation DefaultPropagation { get; } = MragErrorPropagation.StopPipeline;

    public Task<Output> ExecuteAsync(
        [InputSocket(Description = "Parsed documents to chunk")] MragDocumentDto[] Documents,
        MragPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        var config = GetNodeConfig();
        ValidateConfig(config);

        if (Documents == null || Documents.Length == 0)
            return Task.FromResult(new Output { ChunkBatches = [] });

        var batches = new MragChunkDto[Documents.Length][];

        for (int i = 0; i < Documents.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batches[i] = ChunkByTokens(Documents[i], config);
        }

        return Task.FromResult(new Output { ChunkBatches = batches });
    }

    private static MragChunkDto[] ChunkByTokens(MragDocumentDto doc, Config config)
    {
        var fullText = string.Join("\n", doc.Elements
            .Where(e => e.Text != null)
            .Select(e => e.Text!));

        if (string.IsNullOrWhiteSpace(fullText))
            return [];

        var subChunks = TokenSplitter.Split(fullText, config.MaxTokensPerChunk, config.OverlapTokens);
        return subChunks.Select(s => new MragChunkDto
        {
            DocumentId = doc.Id,
            Content = s
        }).ToArray();
    }

    private static void ValidateConfig(Config config)
    {
        if (config.MaxTokensPerChunk <= 0)
            throw new InvalidOperationException(
                $"MaxTokensPerChunk must be > 0, got {config.MaxTokensPerChunk}.");
        if (config.OverlapTokens >= config.MaxTokensPerChunk)
            throw new InvalidOperationException(
                $"OverlapTokens ({config.OverlapTokens}) must be less than MaxTokensPerChunk ({config.MaxTokensPerChunk}).");
    }

    public sealed class Config
    {
        public int MaxTokensPerChunk { get; set; } = 512;
        public int OverlapTokens { get; set; } = 64;
    }

    public sealed record Output
    {
        [OutputSocket(Description = "Jagged array of chunk batches, one inner array per input document")]
        public MragChunkDto[][] ChunkBatches { get; init; } = [];
    }
}
