using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.Handlers.Tests.Shared;
using HPD.RAG.Retrieval.Handlers;
using HPD.RAG.Retrieval.Internal;
using HPD.RAG.VectorStores.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;
using Xunit;

namespace HPD.RAG.Handlers.Tests.Retrieval;

/// <summary>
/// T-099 and T-100 — VectorSearchHandler direct-invocation tests.
/// Collection must be pre-created before SearchAsync can run.
/// Embedding dimensions must match VectorSearchHandler.Config.Dimensions default (1536).
/// </summary>
public sealed class VectorSearchHandlerTests
{
    // Must match VectorSearchHandler.Config.Dimensions default
    private const int EmbeddingDimensions = 1536;

    private static IServiceProvider BuildServices()
    {
        InMemoryVectorStoreModule.Initialize();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<VectorStore>(
            "mrag:vectorstore", new InMemoryVectorStore());
        services.AddKeyedSingleton<IVectorStoreFeatures>(
            "mrag:vectorstore-features", new FakeVectorStoreFeatures());
        return services.BuildServiceProvider();
    }

    private static float[] MakeEmbedding() =>
        Enumerable.Range(0, EmbeddingDimensions)
                  .Select(i => (float)(i + 1) / EmbeddingDimensions)
                  .ToArray();

    private static async Task EnsureCollectionAsync(IServiceProvider sp, string collectionName)
    {
        var store = sp.GetRequiredKeyedService<VectorStore>("mrag:vectorstore");
        var col = store.GetCollection<string, MragVectorRecord>(collectionName);
        await col.EnsureCollectionExistsAsync();
    }

    /// <summary>
    /// T-099 — VectorSearchHandler with no filter returns empty results (collection is empty).
    /// </summary>
    [Fact]
    public async Task VectorSearchHandler_WithNoFilter_CallsSearchWithoutFilter()
    {
        var sp = BuildServices();
        await EnsureCollectionAsync(sp, "vs-no-filter");
        var context = HandlerTestContext.CreateWithProvider(sp, collectionName: "vs-no-filter");
        var handler = new VectorSearchHandler();

        var output = await handler.ExecuteAsync(
            context,
            Embedding: MakeEmbedding(),
            Filter: null,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(output);
        Assert.NotNull(output.Results);
        Assert.Empty(output.Results);
    }

    /// <summary>
    /// T-100 — VectorSearchHandler with a filter node does not throw.
    /// FakeVectorStoreFeatures.Translate() returns an opaque object (not VectorSearchFilter),
    /// so vsf is null and the search runs unfiltered.
    /// </summary>
    [Fact]
    public async Task VectorSearchHandler_WithFilter_CallsTranslatorAndPassesResult()
    {
        var sp = BuildServices();
        await EnsureCollectionAsync(sp, "vs-with-filter");
        var context = HandlerTestContext.CreateWithProvider(sp, collectionName: "vs-with-filter");
        var handler = new VectorSearchHandler();

        var output = await handler.ExecuteAsync(
            context,
            Embedding: MakeEmbedding(),
            Filter: MragFilter.Eq("category", "tech"),
            cancellationToken: CancellationToken.None);

        Assert.NotNull(output);
        Assert.NotNull(output.Results);
    }
}
