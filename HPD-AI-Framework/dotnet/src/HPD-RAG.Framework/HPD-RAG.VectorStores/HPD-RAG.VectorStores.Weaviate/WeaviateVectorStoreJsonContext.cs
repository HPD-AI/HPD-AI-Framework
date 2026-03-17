using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Weaviate;

[JsonSerializable(typeof(WeaviateVectorStoreConfig))]
public partial class WeaviateVectorStoreJsonContext : JsonSerializerContext;
