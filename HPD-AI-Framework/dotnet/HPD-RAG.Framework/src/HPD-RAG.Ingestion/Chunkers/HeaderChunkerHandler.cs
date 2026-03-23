using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using HPD.RAG.Ingestion.Internal;
using HPDAgent.Graph.Abstractions.Attributes;

namespace HPD.RAG.Ingestion.Chunkers;

/// <summary>
/// Splits documents into chunks by Markdown/structural header boundaries.
/// Each header section becomes a chunk; sections exceeding <see cref="Config.MaxTokensPerChunk"/>
/// are sub-split with token overlap.
/// Output is a jagged array: one inner array per document, preserving document identity.
/// </summary>
[GraphNodeHandler(NodeName = "ChunkByHeader")]
public sealed partial class HeaderChunkerHandler
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
            batches[i] = ChunkByHeaders(Documents[i], config);
        }

        return Task.FromResult(new Output { ChunkBatches = batches });
    }

    private static MragChunkDto[] ChunkByHeaders(MragDocumentDto doc, Config config)
    {
        var chunks = new List<MragChunkDto>();
        string? currentHeader = null;
        var currentText = new System.Text.StringBuilder();

        void Flush()
        {
            var text = currentText.ToString().Trim();
            if (text.Length == 0) return;

            // Sub-split if over token limit
            var subChunks = TokenSplitter.Split(text, config.MaxTokensPerChunk, config.OverlapTokens);
            foreach (var sub in subChunks)
            {
                chunks.Add(new MragChunkDto
                {
                    DocumentId = doc.Id,
                    Content = sub,
                    Context = currentHeader
                });
            }
            currentText.Clear();
        }

        foreach (var el in doc.Elements)
        {
            if (el.Type == "header")
            {
                Flush();
                currentHeader = el.Text;
            }
            else if (el.Text != null)
            {
                if (currentText.Length > 0) currentText.Append('\n');
                currentText.Append(el.Text);
            }
        }
        Flush();

        return chunks.ToArray();
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
