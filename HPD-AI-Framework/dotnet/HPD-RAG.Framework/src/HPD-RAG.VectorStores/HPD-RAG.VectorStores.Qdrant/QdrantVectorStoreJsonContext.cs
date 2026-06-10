using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Qdrant;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(QdrantVectorStoreConfig))]
public partial class QdrantVectorStoreJsonContext : JsonSerializerContext;
