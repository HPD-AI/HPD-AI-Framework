using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Sqlite;

[JsonSerializable(typeof(SqliteVectorStoreConfig))]
public partial class SqliteVectorStoreJsonContext : JsonSerializerContext;
