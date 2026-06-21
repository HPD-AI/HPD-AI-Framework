using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.VectorStores.CosmosNoSql;

/// <summary>
/// Implements Microsoft.Extensions.VectorData.VectorStore for Azure Cosmos DB NoSQL
/// using the raw Microsoft.Azure.Cosmos SDK — the SK connector 1.51.0-preview uses an
/// incompatible MEVD version (9.0.0-preview) and cannot be mixed with the MEVD 9.7+ API.
/// </summary>
internal sealed class CosmosNoSqlVectorStoreAdapter : Microsoft.Extensions.VectorData.VectorStore
{
    private readonly CosmosClient _client;
    private readonly Database _database;

    public CosmosNoSqlVectorStoreAdapter(CosmosClient client, Database database)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(
        string name, VectorStoreCollectionDefinition? definition = null)
    {
        throw new NotSupportedException(
            "Cosmos DB NoSQL SK connector 1.51.0-preview uses incompatible MEVD types. " +
            "Use a newer SK connector release with MEVD 9.7+ support for typed collection access.");
    }

    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
        string name, VectorStoreCollectionDefinition definition)
    {
        throw new NotSupportedException(
            "GetDynamicCollection is not supported by the CosmosNoSqlVectorStoreAdapter.");
    }

    public override async IAsyncEnumerable<string> ListCollectionNamesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var iterator = _database.GetContainerQueryIterator<string>("SELECT VALUE c.id FROM c");
        while (iterator.HasMoreResults)
        {
            var results = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            foreach (var id in results)
                yield return id;
        }
    }

    public override async Task<bool> CollectionExistsAsync(
        string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = _database.GetContainer(name);
            await container.ReadContainerAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public override async Task EnsureCollectionDeletedAsync(
        string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = _database.GetContainer(name);
            await container.DeleteContainerAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already deleted or never existed — no-op
        }
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(CosmosClient)) return _client;
        if (serviceType == typeof(Database)) return _database;
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _client.Dispose();
        base.Dispose(disposing);
    }
}
