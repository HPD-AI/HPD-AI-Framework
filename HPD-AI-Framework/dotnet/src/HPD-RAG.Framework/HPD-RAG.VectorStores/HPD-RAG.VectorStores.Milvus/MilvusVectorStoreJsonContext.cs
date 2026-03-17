using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.Milvus;

[JsonSerializable(typeof(MilvusVectorStoreConfig))]
public partial class MilvusVectorStoreJsonContext : JsonSerializerContext;
