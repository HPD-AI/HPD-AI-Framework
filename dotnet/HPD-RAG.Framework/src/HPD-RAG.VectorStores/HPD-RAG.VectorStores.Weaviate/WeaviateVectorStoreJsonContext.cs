using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Weaviate;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WeaviateVectorStoreConfig))]
public partial class WeaviateVectorStoreJsonContext : JsonSerializerContext;
