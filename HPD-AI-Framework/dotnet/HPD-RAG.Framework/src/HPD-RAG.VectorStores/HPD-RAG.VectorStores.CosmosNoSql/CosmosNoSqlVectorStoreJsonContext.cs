using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.CosmosNoSql;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CosmosNoSqlVectorStoreConfig))]
public partial class CosmosNoSqlVectorStoreJsonContext : JsonSerializerContext;
