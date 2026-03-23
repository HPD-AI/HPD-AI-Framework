using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.CosmosMongo;

[JsonSerializable(typeof(CosmosMongoVectorStoreConfig))]
public partial class CosmosMongoVectorStoreJsonContext : JsonSerializerContext;
