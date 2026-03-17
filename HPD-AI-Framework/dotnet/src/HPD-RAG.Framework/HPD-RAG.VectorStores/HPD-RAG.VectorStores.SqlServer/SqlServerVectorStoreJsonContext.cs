using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.SqlServer;

[JsonSerializable(typeof(SqlServerVectorStoreConfig))]
public partial class SqlServerVectorStoreJsonContext : JsonSerializerContext;
