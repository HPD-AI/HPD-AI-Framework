using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.AzureAISearch;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AzureAISearchVectorStoreConfig))]
public partial class AzureAISearchVectorStoreJsonContext : JsonSerializerContext;
