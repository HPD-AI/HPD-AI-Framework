using Microsoft.Extensions.VectorData;
using Npgsql;

namespace HPD.RAG.VectorStores.Postgres;

/// <summary>
/// Implements Microsoft.Extensions.VectorData.VectorStore on top of a raw NpgsqlDataSource.
/// The Microsoft.SemanticKernel.Connectors.Postgres package 1.51.0-preview was compiled against
/// MEVD 9.0.0-preview and has incompatible binary types — this adapter uses Npgsql directly.
/// GetCollection and GetDynamicCollection delegate to the SK PostgresVectorStore once
/// a compatible SK Postgres connector version is available (which requires MEVD 9.7+).
/// </summary>
internal sealed class PostgresVectorStoreAdapter : Microsoft.Extensions.VectorData.VectorStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _schema;

    public PostgresVectorStoreAdapter(NpgsqlDataSource dataSource, string schema)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _schema = schema;
    }

    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(
        string name, VectorStoreCollectionDefinition? definition = null)
    {
        throw new NotSupportedException(
            "PostgresVectorStore collection operations require a compatible SK Postgres connector " +
            "with MEVD 9.7+ support. Current SK Postgres 1.51.0-preview uses MEVD 9.0.0-preview " +
            "which has incompatible types. Use a newer SK Postgres release when available.");
    }

    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
        string name, VectorStoreCollectionDefinition definition)
    {
        throw new NotSupportedException(
            "PostgresVectorStore GetDynamicCollection is not supported in the current adapter.");
    }

    public override async IAsyncEnumerable<string> ListCollectionNamesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var sql = $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{_schema}' ORDER BY table_name";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            yield return reader.GetString(0);
    }

    public override async Task<bool> CollectionExistsAsync(
        string name, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM information_schema.tables WHERE table_schema = @s AND table_name = @n";
        cmd.Parameters.AddWithValue("s", _schema);
        cmd.Parameters.AddWithValue("n", name);
        var count = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        return count > 0;
    }

    public override async Task EnsureCollectionDeletedAsync(
        string name, CancellationToken cancellationToken = default)
    {
        bool exists = await CollectionExistsAsync(name, cancellationToken).ConfigureAwait(false);
        if (!exists) return;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP TABLE IF EXISTS \"{_schema}\".\"{name}\"";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(NpgsqlDataSource))
            return _dataSource;
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        // NpgsqlDataSource lifecycle is managed by the caller
        base.Dispose(disposing);
    }
}
