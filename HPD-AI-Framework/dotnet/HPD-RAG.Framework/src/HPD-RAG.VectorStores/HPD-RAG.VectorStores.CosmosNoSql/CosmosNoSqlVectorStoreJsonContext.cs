using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.CosmosNoSql;

[JsonSerializable(typeof(CosmosNoSqlVectorStoreConfig))]
public partial class CosmosNoSqlVectorStoreJsonContext : JsonSerializerContext;
