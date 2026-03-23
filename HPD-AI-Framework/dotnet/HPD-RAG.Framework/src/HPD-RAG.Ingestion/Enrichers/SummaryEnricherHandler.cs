using System.Text.Json;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using HPDAgent.Graph.Abstractions.Attributes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Ingestion.Enrichers;

/// <summary>
/// Enriches each chunk with a <c>summary</c> metadata key containing a one-sentence summary.
/// Uses a keyed <see cref="IChatClient"/> ("mrag:enricher:summary").
/// </summary>
[GraphNodeHandler(NodeName = "EnrichSummary")]
public sealed partial class SummaryEnricherHandler
{
    private const string MetadataKey = "summary";

    /// <summary>Default retry: 4 attempts, JitteredExponential, 2–120s.</summary>
    public static MragRetryPolicy DefaultRetry { get; } = new()
    {
        MaxAttempts = 4,
        InitialDelay = TimeSpan.FromSeconds(2),
        Strategy = MragBackoffStrategy.JitteredExponential,
        MaxDelay = TimeSpan.FromSeconds(120)
    };

    /// <summary>Default propagation: Isolate.</summary>
    public static MragErrorPropagation DefaultPropagation { get; } = MragErrorPropagation.Isolate;

    public async Task<Output> ExecuteAsync(
        [InputSocket(Description = "Chunks to enrich with summaries")] MragChunkDto[] Chunks,
        MragPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        if (Chunks == null || Chunks.Length == 0)
            return new Output { Chunks = [] };

        var config = GetNodeConfig();
        var chatClient = context.Services.GetRequiredKeyedService<IChatClient>("mrag:enricher:summary");
        int batchSize = config.BatchSize > 0 ? config.BatchSize : 10;

        var result = new MragChunkDto[Chunks.Length];

        for (int i = 0; i < Chunks.Length; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int end = Math.Min(i + batchSize, Chunks.Length);
            var batch = Chunks[i..end];

            var prompt = BuildPrompt(batch);
            var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var lines = (response.Text ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            for (int j = 0; j < batch.Length; j++)
            {
                var summary = j < lines.Length ? lines[j] : string.Empty;
                var element = JsonSerializer.SerializeToElement(summary);
                result[i + j] = AppendMetadata(batch[j], MetadataKey, element);
            }
        }

        return new Output { Chunks = result };
    }

    private static string BuildPrompt(MragChunkDto[] batch)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "Summarize each chunk in one sentence. " +
            "Respond with one summary per line, in the same order as the chunks.");
        for (int i = 0; i < batch.Length; i++)
            sb.AppendLine($"Chunk {i + 1}: {batch[i].Content}");
        return sb.ToString();
    }

    private static MragChunkDto AppendMetadata(MragChunkDto chunk, string key, JsonElement value)
    {
        var metadata = chunk.Metadata != null
            ? new Dictionary<string, JsonElement>(chunk.Metadata) { [key] = value }
            : new Dictionary<string, JsonElement> { [key] = value };
        return chunk with { Metadata = metadata };
    }

    public sealed class Config
    {
        public int BatchSize { get; set; } = 10;
    }

    public sealed record Output
    {
        [OutputSocket(Description = "Chunks with Metadata[\"summary\"] populated as JsonElement string")]
        public MragChunkDto[] Chunks { get; init; } = [];
    }
}
