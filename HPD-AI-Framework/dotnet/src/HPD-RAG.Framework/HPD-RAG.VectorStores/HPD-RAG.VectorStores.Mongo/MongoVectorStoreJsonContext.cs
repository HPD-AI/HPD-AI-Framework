using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Mongo;

[JsonSerializable(typeof(MongoVectorStoreConfig))]
public partial class MongoVectorStoreJsonContext : JsonSerializerContext;
