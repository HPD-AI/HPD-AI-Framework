using Microsoft.Extensions.VectorData;

// Alias to avoid conflict with our own namespace segment "Milvus"
using MilvusClientType = Milvus.Client.MilvusClient;

namespace HPD.RAG.VectorStores.Milvus;

/// <summary>
/// Adapts the Milvus.Client.MilvusClient to the Microsoft.Extensions.VectorData.VectorStore
/// abstract class. The Microsoft.SemanticKernel.Connectors.Milvus package (1.73.0-alpha) only
/// exposes MilvusMemoryStore (the old IMemoryStore API) and does not yet provide a VectorStore
/// implementation, so this adapter bridges the gap by delegating to the raw MilvusClient.
/// GetCollection and GetDynamicCollection throw NotSupportedException because no typed
/// VectorStoreCollection implementation is available; ListCollectionNamesAsync and
/// CollectionExistsAsync are delegated directly to the Milvus gRPC client.
/// </summary>
internal sealed class MilvusVectorStoreAdapter : Microsoft.Extensions.VectorData.VectorStore
{
    private readonly MilvusClientType _client;

    public MilvusVectorStoreAdapter(MilvusClientType client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(
        string name, VectorStoreCollectionDefinition? definition = null)
    {
        throw new NotSupportedException(
            "The Milvus SK connector (1.73.0-alpha) does not yet implement the " +
            "Microsoft.Extensions.VectorData VectorStoreCollection API. " +
            "Use Milvus.Client.MilvusClient directly for collection-level operations.");
    }

    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
        string name, VectorStoreCollectionDefinition definition)
    {
        throw new NotSupportedException(
            "The Milvus SK connector (1.73.0-alpha) does not yet implement the " +
            "Microsoft.Extensions.VectorData VectorStoreDynamicCollection API.");
    }

    public override IAsyncEnumerable<string> ListCollectionNamesAsync(
        CancellationToken cancellationToken = default)
    {
        return ListCollectionNamesInternalAsync(cancellationToken);
    }

    public override async Task<bool> CollectionExistsAsync(
        string name, CancellationToken cancellationToken = default)
    {
        var collections = await _client.ListCollectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var c in collections)
        {
            if (string.Equals(c.Name, name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public override async Task EnsureCollectionDeletedAsync(
        string name, CancellationToken cancellationToken = default)
    {
        bool exists = await CollectionExistsAsync(name, cancellationToken).ConfigureAwait(false);
        if (exists)
            await _client.GetCollection(name).DropAsync(cancellationToken).ConfigureAwait(false);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(MilvusClientType))
            return _client;
        return null;
    }

    private async IAsyncEnumerable<string> ListCollectionNamesInternalAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var collections = await _client.ListCollectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var collection in collections)
            yield return collection.Name;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _client.Dispose();
        base.Dispose(disposing);
    }
}
