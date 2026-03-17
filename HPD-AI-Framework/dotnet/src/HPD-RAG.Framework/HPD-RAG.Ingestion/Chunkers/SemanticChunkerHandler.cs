using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using HPD.RAG.Ingestion.Internal;
using HPDAgent.Graph.Abstractions.Attributes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Ingestion.Chunkers;

/// <summary>
/// Splits documents into semantically coherent chunks using an embedding model.
/// Sentences are embedded and cosine-similarity breakpoints at the configured
/// <see cref="Config.ThresholdPercentile"/> trigger chunk boundaries.
/// Output is a jagged array: one inner array per document.
/// </summary>
[GraphNodeHandler(NodeName = "ChunkSemantic")]
public sealed partial class SemanticChunkerHandler
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

    public async Task<Output> ExecuteAsync(
        [InputSocket(Description = "Parsed documents to chunk")] MragDocumentDto[] Documents,
        MragPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        var config = GetNodeConfig();
        ValidateConfig(config);

        if (Documents == null || Documents.Length == 0)
            return new Output { ChunkBatches = [] };

        var embeddingGenerator = context.Services
            .GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("mrag:embedding");

        var batches = new MragChunkDto[Documents.Length][];

        for (int i = 0; i < Documents.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batches[i] = await ChunkSemanticAsync(
                Documents[i], config, embeddingGenerator, cancellationToken).ConfigureAwait(false);
        }

        return new Output { ChunkBatches = batches };
    }

    private static async Task<MragChunkDto[]> ChunkSemanticAsync(
        MragDocumentDto doc,
        Config config,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        CancellationToken ct)
    {
        // Collect text-bearing elements as candidate sentences
        var sentences = doc.Elements
            .Where(e => e.Text != null)
            .Select(e => e.Text!)
            .ToList();

        if (sentences.Count == 0)
            return [];

        if (sentences.Count == 1)
            return [new MragChunkDto { DocumentId = doc.Id, Content = sentences[0] }];

        // Embed all sentences in one batch for efficiency
        var embeddings = await embeddingGenerator
            .GenerateAsync(sentences, cancellationToken: ct).ConfigureAwait(false);

        // Compute cosine distances between adjacent sentences
        var distances = new double[sentences.Count - 1];
        for (int i = 0; i < distances.Length; i++)
        {
            distances[i] = 1.0 - CosineSimilarity(
                embeddings[i].Vector.Span,
                embeddings[i + 1].Vector.Span);
        }

        // Determine breakpoint threshold via percentile
        var sorted = distances.OrderBy(d => d).ToArray();
        int pIdx = (int)Math.Ceiling(config.ThresholdPercentile / 100.0 * sorted.Length) - 1;
        pIdx = Math.Clamp(pIdx, 0, sorted.Length - 1);
        double threshold = sorted[pIdx];

        // Build chunks at breakpoints, respecting MaxTokensPerChunk
        var chunks = new List<MragChunkDto>();
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < sentences.Count; i++)
        {
            current.Append(i > 0 && current.Length > 0 ? " " : "").Append(sentences[i]);

            bool isBreakpoint = i < distances.Length && distances[i] >= threshold;
            bool overLimit = TokenSplitter.EstimateTokens(current.ToString()) >= config.MaxTokensPerChunk;

            if (isBreakpoint || overLimit || i == sentences.Count - 1)
            {
                var text = current.ToString().Trim();
                if (text.Length > 0)
                    chunks.Add(new MragChunkDto { DocumentId = doc.Id, Content = text });
                current.Clear();
            }
        }

        return chunks.ToArray();
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < 1e-8f ? 0f : dot / denom;
    }

    private static void ValidateConfig(Config config)
    {
        if (config.MaxTokensPerChunk <= 0)
            throw new InvalidOperationException(
                $"MaxTokensPerChunk must be > 0, got {config.MaxTokensPerChunk}.");
        if (config.ThresholdPercentile is < 0 or > 100)
            throw new InvalidOperationException(
                $"ThresholdPercentile must be in [0,100], got {config.ThresholdPercentile}.");
    }

    public sealed class Config
    {
        public int MaxTokensPerChunk { get; set; } = 512;
        /// <summary>Percentile of cosine-distance distribution used as breakpoint threshold. Default: 95.</summary>
        public double ThresholdPercentile { get; set; } = 95.0;
    }

    public sealed record Output
    {
        [OutputSocket(Description = "Jagged array of chunk batches, one inner array per input document")]
        public MragChunkDto[][] ChunkBatches { get; init; } = [];
    }
}
