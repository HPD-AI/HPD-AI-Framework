using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Redis;

[JsonSerializable(typeof(RedisVectorStoreConfig))]
public partial class RedisVectorStoreJsonContext : JsonSerializerContext;
