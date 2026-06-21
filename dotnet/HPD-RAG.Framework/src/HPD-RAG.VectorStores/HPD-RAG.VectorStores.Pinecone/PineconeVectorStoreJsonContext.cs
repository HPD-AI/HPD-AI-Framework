using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Pinecone;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PineconeVectorStoreConfig))]
public partial class PineconeVectorStoreJsonContext : JsonSerializerContext;
