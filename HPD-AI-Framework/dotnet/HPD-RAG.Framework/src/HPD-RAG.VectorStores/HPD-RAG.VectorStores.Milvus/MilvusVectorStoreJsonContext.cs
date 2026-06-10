using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Milvus;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MilvusVectorStoreConfig))]
public partial class MilvusVectorStoreJsonContext : JsonSerializerContext;
