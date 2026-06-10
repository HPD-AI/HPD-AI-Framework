using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Mongo;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MongoVectorStoreConfig))]
public partial class MongoVectorStoreJsonContext : JsonSerializerContext;
