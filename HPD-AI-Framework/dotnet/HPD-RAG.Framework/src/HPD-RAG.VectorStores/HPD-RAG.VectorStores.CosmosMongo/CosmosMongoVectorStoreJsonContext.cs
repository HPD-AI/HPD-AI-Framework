using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.CosmosMongo;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CosmosMongoVectorStoreConfig))]
public partial class CosmosMongoVectorStoreJsonContext : JsonSerializerContext;
