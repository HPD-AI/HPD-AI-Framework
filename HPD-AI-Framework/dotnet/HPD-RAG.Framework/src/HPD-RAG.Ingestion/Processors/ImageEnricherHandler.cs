using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using HPDAgent.Graph.Abstractions.Attributes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Ingestion.Processors;

/// <summary>
/// Enriches image elements in documents by generating alt-text descriptions
/// via a keyed <see cref="IChatClient"/> ("mrag:enricher:images").
/// Only processes elements where Type=="image" and AlternativeText is null.
/// </summary>
[GraphNodeHandler(NodeName = "EnrichImages")]
public sealed partial class ImageEnricherHandler
{
    /// <summary>Default retry: 3 attempts, JitteredExponential, 2–60s.</summary>
    public static MragRetryPolicy DefaultRetry { get; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(2),
        Strategy = MragBackoffStrategy.JitteredExponential,
        MaxDelay = TimeSpan.FromSeconds(60)
    };

    /// <summary>Default propagation: SkipDependents.</summary>
    public static MragErrorPropagation DefaultPropagation { get; } = MragErrorPropagation.SkipDependents;

    public async Task<Output> ExecuteAsync(
        [InputSocket(Description = "Documents containing image elements to enrich")] MragDocumentDto[] Documents,
        MragPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        if (Documents == null || Documents.Length == 0)
            return new Output { Documents = [] };

        var config = GetNodeConfig();
        var chatClient = context.Services.GetRequiredKeyedService<IChatClient>("mrag:enricher:images");

        // Collect all image elements that need alt-text across all documents
        var imagesToEnrich = new List<(int docIdx, int elIdx)>();
        for (int d = 0; d < Documents.Length; d++)
        {
            for (int e = 0; e < Documents[d].Elements.Length; e++)
            {
                var el = Documents[d].Elements[e];
                if (el.Type == "image" && el.AlternativeText == null && el.Base64Content != null)
                {
                    imagesToEnrich.Add((d, e));
                }
            }
        }

        if (imagesToEnrich.Count == 0)
            return new Output { Documents = Documents };

        // Copy documents as mutable arrays so we can update elements
        var updatedDocs = Documents.Select(doc => (doc, doc.Elements.ToArray())).ToArray();

        // Process in batches
        int batchSize = config.BatchSize > 0 ? config.BatchSize : 10;
        for (int i = 0; i < imagesToEnrich.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = imagesToEnrich.Skip(i).Take(batchSize).ToList();

            // Build chat message with all images in the batch
            var contentParts = new List<AIContent>();
            contentParts.Add(new TextContent(
                "Describe each image with a concise alt-text suitable for accessibility. " +
                "Respond with one description per line, in the same order as the images."));

            foreach (var (docIdx, elIdx) in batch)
            {
                var el = Documents[docIdx].Elements[elIdx];
                if (el.Base64Content != null && el.MediaType != null)
                {
                    contentParts.Add(new DataContent(
                        Convert.FromBase64String(el.Base64Content),
                        el.MediaType));
                }
            }

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, contentParts)
            };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var descriptions = (response.Text ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Apply descriptions back — one per image in batch order
            for (int j = 0; j < batch.Count; j++)
            {
                var (docIdx, elIdx) = batch[j];
                var description = j < descriptions.Length ? descriptions[j] : string.Empty;
                var old = updatedDocs[docIdx].Item2[elIdx];
                updatedDocs[docIdx].Item2[elIdx] = old with { AlternativeText = description };
            }
        }

        var result = updatedDocs.Select(t =>
            t.doc with { Elements = t.Item2 }).ToArray();

        return new Output { Documents = result };
    }

    public sealed class Config
    {
        /// <summary>Number of images per chat-client call. Default: 10.</summary>
        public int BatchSize { get; set; } = 10;
    }

    public sealed record Output
    {
        [OutputSocket(Description = "Documents with AlternativeText populated on image elements")]
        public MragDocumentDto[] Documents { get; init; } = [];
    }
}
