using Microsoft.Data.Sqlite;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.VectorStores.Sqlite;

/// <summary>
/// Implements Microsoft.Extensions.VectorData.VectorStore for SQLite using the raw
/// Microsoft.Data.Sqlite connection — the SK Sqlite connector 1.51.0-preview uses an
/// incompatible MEVD version (9.0.0-preview) and cannot be mixed with MEVD 9.7+.
/// Table management uses sqlite_master / sqlite_schema for collection discovery.
/// GetCollection and GetDynamicCollection require a newer SK connector.
/// </summary>
internal sealed class SqliteVectorStoreAdapter : Microsoft.Extensions.VectorData.VectorStore
{
    private readonly SqliteConnection _connection;

    public SqliteVectorStoreAdapter(SqliteConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(
        string name, VectorStoreCollectionDefinition? definition = null)
    {
        throw new NotSupportedException(
            "SQLite SK connector 1.51.0-preview uses incompatible MEVD types. " +
            "Use a newer SK connector release with MEVD 9.7+ support for typed collection access.");
    }

    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
        string name, VectorStoreCollectionDefinition definition)
    {
        throw new NotSupportedException(
            "GetDynamicCollection is not supported by the SqliteVectorStoreAdapter.");
    }

    public override async IAsyncEnumerable<string> ListCollectionNamesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            yield return reader.GetString(0);
    }

    public override async Task<bool> CollectionExistsAsync(
        string name, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", name);
        var count = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        return count > 0;
    }

    public override async Task EnsureCollectionDeletedAsync(
        string name, CancellationToken cancellationToken = default)
    {
        bool exists = await CollectionExistsAsync(name, cancellationToken).ConfigureAwait(false);
        if (!exists) return;
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"DROP TABLE IF EXISTS \"{name}\"";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(SqliteConnection))
            return _connection;
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _connection.Dispose();
        base.Dispose(disposing);
    }
}
