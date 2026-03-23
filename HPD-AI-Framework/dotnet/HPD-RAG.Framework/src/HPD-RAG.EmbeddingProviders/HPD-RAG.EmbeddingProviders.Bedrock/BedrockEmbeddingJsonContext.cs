using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.Bedrock;

/// <summary>
/// Source-generated JSON serializer context for Bedrock embedding provider types.
/// Enables AOT-compatible serialization.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BedrockEmbeddingConfig))]
public partial class BedrockEmbeddingJsonContext : JsonSerializerContext
{
}
