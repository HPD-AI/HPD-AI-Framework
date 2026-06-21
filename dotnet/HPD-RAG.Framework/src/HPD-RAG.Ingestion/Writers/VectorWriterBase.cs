using System.Text.Json;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.Ingestion.Writers;

/// <summary>
/// Shared upsert logic for all vector-store writer handlers.
/// Resolves keyed <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> and
/// <see cref="VectorStore"/> from DI, builds collection definitions, embeds chunks,
/// and upserts records merging <c>context.RunTags</c>.
/// </summary>
internal static class VectorWriterBase
{
    /// <summary>Default retry: 3 attempts, Exponential, 1–30s.</summary>
    internal static MragRetryPolicy DefaultRetry { get; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(1),
        Strategy = MragBackoffStrategy.Exponential,
        MaxDelay = TimeSpan.FromSeconds(30)
    };

    /// <summary>Default propagation: StopPipeline.</summary>
    internal static MragErrorPropagation DefaultPropagation { get; } = MragErrorPropagation.StopPipeline;

    /// <summary>
    /// Embeds all chunks, upserts into the resolved <see cref="VectorStore"/> collection,
    /// and returns the count of written records.
    /// </summary>
    internal static async Task<int> WriteAsync(
        MragChunkDto[] chunks,
        MragPipelineContext context,
        WriterConfig config,
        CancellationToken cancellationToken)
    {
        if (chunks == null || chunks.Length == 0)
            return 0;

        var collectionName = ResolveCollectionName(config, context);

        var embeddingGenerator = context.Services
            .GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("mrag:embedding");

        var vectorStore = context.Services
            .GetRequiredKeyedService<VectorStore>("mrag:vectorstore");

        // Build collection definition
        var definition = BuildCollectionDefinition(collectionName, config, context.RunTags);

        var collection = vectorStore.GetCollection<string, MragVectorRecord>(
            collectionName, definition);

        // Create or ensure collection exists (respects IncrementalIngestion)
        if (!config.IncrementalIngestion)
        {
            await collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // For incremental, create only if missing
            await collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
        }

        // Embed all chunk contents in one batch for efficiency
        var contents = chunks.Select(c => c.Content).ToList();
        var embeddings = await embeddingGenerator
            .GenerateAsync(contents, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Build and upsert records
        var records = new List<MragVectorRecord>(chunks.Length);
        for (int i = 0; i < chunks.Length; i++)
        {
            var chunk = chunks[i];
            var tags = MergeTags(context.RunTags);

            records.Add(new MragVectorRecord
            {
                Id = BuildRecordId(chunk),
                Content = chunk.Content,
                Context = chunk.Context,
                Embedding = embeddings[i].Vector.ToArray(),
                Tags = tags,
                Metadata = chunk.Metadata != null
                    ? JsonSerializer.Serialize(chunk.Metadata)
                    : null
            });
        }

        // Upsert in a single batch where supported
        await collection.UpsertAsync(records, cancellationToken: cancellationToken).ConfigureAwait(false);

        return records.Count;
    }

    private static string ResolveCollectionName(WriterConfig config, MragPipelineContext context)
    {
        var name = config.CollectionName ?? context.CollectionName;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException(
                "CollectionName must be set on WriterConfig or MragPipelineContext.CollectionName.");
        return name;
    }

    private static VectorStoreCollectionDefinition BuildCollectionDefinition(
        string collectionName,
        WriterConfig config,
        IReadOnlyDictionary<string, string>? runTags)
    {
        var props = new List<VectorStoreProperty>
        {
            new VectorStoreKeyProperty("Id", typeof(string)),
            new VectorStoreDataProperty("Content", typeof(string)) { IsIndexed = true },
            new VectorStoreDataProperty("Context", typeof(string)) { IsIndexed = true },
            new VectorStoreDataProperty("Tags", typeof(string)) { IsIndexed = true },
            new VectorStoreDataProperty("Metadata", typeof(string)),
            new VectorStoreVectorProperty("Embedding", typeof(float[]), config.Dimensions)
            {
                DistanceFunction = config.DistanceFunction,
                IndexKind = config.IndexKind
            }
        };

        return new VectorStoreCollectionDefinition { Properties = props };
    }

    private static string MergeTags(IReadOnlyDictionary<string, string>? runTags)
    {
        if (runTags == null || runTags.Count == 0)
            return string.Empty;

        return string.Join(",", runTags.Select(kvp => $"{kvp.Key}={kvp.Value}"));
    }

    private static string BuildRecordId(MragChunkDto chunk)
    {
        // Stable deterministic ID from documentId + content hash
        var hash = System.IO.Hashing.XxHash32.HashToUInt32(
            System.Text.Encoding.UTF8.GetBytes(chunk.DocumentId + "\0" + chunk.Content));
        return $"{chunk.DocumentId}:{hash:x8}";
    }
}
