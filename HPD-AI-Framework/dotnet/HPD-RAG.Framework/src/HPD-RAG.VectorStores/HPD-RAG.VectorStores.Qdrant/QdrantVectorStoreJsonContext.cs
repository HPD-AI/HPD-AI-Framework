using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Qdrant;

[JsonSerializable(typeof(QdrantVectorStoreConfig))]
public partial class QdrantVectorStoreJsonContext : JsonSerializerContext;
