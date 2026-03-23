using System.Text.Json.Serialization;

namespace HPD.RAG.VectorStores.AzureAISearch;

[JsonSerializable(typeof(AzureAISearchVectorStoreConfig))]
public partial class AzureAISearchVectorStoreJsonContext : JsonSerializerContext;
