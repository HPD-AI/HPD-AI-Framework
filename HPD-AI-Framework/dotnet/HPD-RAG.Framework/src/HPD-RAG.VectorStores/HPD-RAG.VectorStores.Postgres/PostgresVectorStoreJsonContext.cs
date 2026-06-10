using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Postgres;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PostgresVectorStoreConfig))]
public partial class PostgresVectorStoreJsonContext : JsonSerializerContext;
