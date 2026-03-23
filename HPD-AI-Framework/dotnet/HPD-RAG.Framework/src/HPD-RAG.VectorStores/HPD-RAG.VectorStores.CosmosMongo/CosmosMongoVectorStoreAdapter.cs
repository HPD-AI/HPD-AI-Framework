using Microsoft.Extensions.VectorData;
using MongoDB.Driver;

namespace HPD.RAG.VectorStores.CosmosMongo;

/// <summary>
/// Implements Microsoft.Extensions.VectorData.VectorStore for Azure Cosmos DB for MongoDB
/// using the raw MongoDB.Driver — the SK connector 1.51.0-preview uses an incompatible
/// MEVD version (9.0.0-preview) and cannot be mixed with the MEVD 9.7+ API.
/// </summary>
internal sealed class CosmosMongoVectorStoreAdapter : Microsoft.Extensions.VectorData.VectorStore
{
    private readonly IMongoDatabase _database;

    public CosmosMongoVectorStoreAdapter(IMongoDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(
        string name, VectorStoreCollectionDefinition? definition = null)
    {
        throw new NotSupportedException(
            "Cosmos DB for MongoDB SK connector 1.51.0-preview uses incompatible MEVD types. " +
            "Use a newer SK connector release with MEVD 9.7+ support for typed collection access.");
    }

    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
        string name, VectorStoreCollectionDefinition definition)
    {
        throw new NotSupportedException(
            "GetDynamicCollection is not supported by the CosmosMongoVectorStoreAdapter.");
    }

    public override async IAsyncEnumerable<string> ListCollectionNamesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var cursor = await _database.ListCollectionNamesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            foreach (var name in cursor.Current)
                yield return name;
    }

    public override async Task<bool> CollectionExistsAsync(
        string name, CancellationToken cancellationToken = default)
    {
        var cursor = await _database.ListCollectionNamesAsync(
            new ListCollectionNamesOptions { Filter = new MongoDB.Bson.BsonDocument("name", name) },
            cancellationToken).ConfigureAwait(false);
        return await cursor.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task EnsureCollectionDeletedAsync(
        string name, CancellationToken cancellationToken = default)
    {
        bool exists = await CollectionExistsAsync(name, cancellationToken).ConfigureAwait(false);
        if (exists)
            await _database.DropCollectionAsync(name, cancellationToken).ConfigureAwait(false);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(IMongoDatabase))
            return _database;
        return null;
    }
}
