namespace HPD.RAG.VectorStores.Sqlite;

/// <summary>
/// SQLite-specific typed config.
/// Serialized into VectorStoreConfig.ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class SqliteVectorStoreConfig
{
    /// <summary>Path to the SQLite database file, or ":memory:" for an in-memory database.</summary>
    public string? DatabasePath { get; set; }
}
