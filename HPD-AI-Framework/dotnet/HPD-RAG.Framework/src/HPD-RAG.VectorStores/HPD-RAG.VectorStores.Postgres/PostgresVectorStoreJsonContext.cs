using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Postgres;

[JsonSerializable(typeof(PostgresVectorStoreConfig))]
public partial class PostgresVectorStoreJsonContext : JsonSerializerContext;
