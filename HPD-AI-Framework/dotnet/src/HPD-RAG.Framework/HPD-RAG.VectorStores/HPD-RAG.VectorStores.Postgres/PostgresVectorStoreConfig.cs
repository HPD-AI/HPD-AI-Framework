namespace HPD.RAG.VectorStores.Postgres;

/// <summary>
/// Postgres-specific typed config. Extends base connection string with schema support.
/// Serialized into VectorStoreConfig.ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class PostgresVectorStoreConfig
{
    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "public";
}
