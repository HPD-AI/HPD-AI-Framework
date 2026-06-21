using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.SqlServer;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SqlServerVectorStoreConfig))]
public partial class SqlServerVectorStoreJsonContext : JsonSerializerContext;
