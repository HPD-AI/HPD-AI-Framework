using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.AzureAI;

/// <summary>
/// Source-generated JSON serializer context for AzureAI embedding provider types.
/// Enables AOT-compatible serialization.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AzureAIEmbeddingConfig))]
public partial class AzureAIEmbeddingJsonContext : JsonSerializerContext
{
}
