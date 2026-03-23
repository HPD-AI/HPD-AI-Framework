using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Pinecone;

[JsonSerializable(typeof(PineconeVectorStoreConfig))]
public partial class PineconeVectorStoreJsonContext : JsonSerializerContext;
