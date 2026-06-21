using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Sqlite;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SqliteVectorStoreConfig))]
public partial class SqliteVectorStoreJsonContext : JsonSerializerContext;
