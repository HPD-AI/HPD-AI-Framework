using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Redis;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RedisVectorStoreConfig))]
public partial class RedisVectorStoreJsonContext : JsonSerializerContext;
