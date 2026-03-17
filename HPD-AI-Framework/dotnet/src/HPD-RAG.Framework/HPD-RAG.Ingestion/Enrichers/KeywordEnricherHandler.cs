using System.Text.Json;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using HPDAgent.Graph.Abstractions.Attributes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Ingestion.Enrichers;

/// <summary>
/// Enriches each chunk with a <c>keywords</c> metadata key containing a JSON array of keywords.
/// Uses a keyed <see cref="IChatClient"/> ("mrag:enricher:keywords") for extraction.
/// </summary>
[GraphNodeHandler(NodeName = "EnrichKeywords")]
public sealed partial class KeywordEnricherHandler
{
    private const string MetadataKey = "keywords";

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
        [InputSocket(Description = "Chunks to enrich with keywords")] MragChunkDto[] Chunks,
        MragPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        if (Chunks == null || Chunks.Length == 0)
            return new Output { Chunks = [] };

        var config = GetNodeConfig();
        var chatClient = context.Services.GetRequiredKeyedService<IChatClient>("mrag:enricher:keywords");
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
                var rawKeywords = j < lines.Length ? lines[j] : string.Empty;
                var keywordsElement = ParseKeywordsToElement(rawKeywords);
                result[i + j] = AppendMetadata(batch[j], MetadataKey, keywordsElement);
            }
        }

        return new Output { Chunks = result };
    }

    private static string BuildPrompt(MragChunkDto[] batch)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "Extract 3-10 keywords from each chunk. " +
            "Respond with one JSON array per line (e.g. [\"ai\",\"search\"]), one line per chunk, in order.");
        for (int i = 0; i < batch.Length; i++)
        {
            sb.AppendLine($"Chunk {i + 1}: {batch[i].Content}");
        }
        return sb.ToString();
    }

    private static JsonElement ParseKeywordsToElement(string raw)
    {
        raw = raw.Trim();
        if (string.IsNullOrEmpty(raw))
            raw = "[]";

        try
        {
            // Accept either a bare JSON array or a line starting with the array
            int arrayStart = raw.IndexOf('[');
            if (arrayStart > 0) raw = raw[arrayStart..];
            return JsonSerializer.Deserialize<JsonElement>(raw);
        }
        catch
        {
            // Fall back: wrap comma-separated words in a JSON array
            var words = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => w.Trim('"', '\'', ' '));
            var json = "[" + string.Join(",", words.Select(w => JsonSerializer.Serialize(w))) + "]";
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
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
        /// <summary>Number of chunks per chat-client call. Default: 10.</summary>
        public int BatchSize { get; set; } = 10;
    }

    public sealed record Output
    {
        [OutputSocket(Description = "Chunks with Metadata[\"keywords\"] populated as JsonElement array")]
        public MragChunkDto[] Chunks { get; init; } = [];
    }
}
