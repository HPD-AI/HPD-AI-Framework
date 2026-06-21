using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.Handlers.Tests.Shared;
using HPD.RAG.Retrieval.Handlers;
using HPD.RAG.Retrieval.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
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
        var services = new ServiceCollection();
        services.AddKeyedSingleton<VectorStore>(
            "mrag:vectorstore", new EmptyVectorStore());
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
    /// FakeVectorStoreFeatures.Translate() returns an opaque object, so the expression
    /// filter is null and the search runs unfiltered.
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

    private sealed class EmptyVectorStore : VectorStore
    {
        public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(
            string name,
            VectorStoreCollectionDefinition? definition = null)
            => new EmptyVectorStoreCollection<TKey, TRecord>(name);

        public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
            string name,
            VectorStoreCollectionDefinition definition)
            => new EmptyVectorStoreCollection<object, Dictionary<string, object?>>(name);

        public override IAsyncEnumerable<string> ListCollectionNamesAsync(CancellationToken cancellationToken = default)
            => EmptyAsync<string>();

        public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;
    }

    private sealed class EmptyVectorStoreCollection<TKey, TRecord>(string name)
        : VectorStoreCollection<TKey, TRecord>
        where TKey : notnull
        where TRecord : class
    {
        public override string Name { get; } = name;

        public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task<TRecord?> GetAsync(
            TKey key,
            RecordRetrievalOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TRecord?>(default);

        public override IAsyncEnumerable<TRecord> GetAsync(
            System.Linq.Expressions.Expression<Func<TRecord, bool>> filter,
            int top,
            FilteredRecordRetrievalOptions<TRecord>? options = null,
            CancellationToken cancellationToken = default)
            => EmptyAsync<TRecord>();

        public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
            TInput searchValue,
            int top,
            VectorSearchOptions<TRecord>? options = null,
            CancellationToken cancellationToken = default)
            => EmptyAsync<VectorSearchResult<TRecord>>();

        public override object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
