using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.HuggingFace;

/// <summary>
/// Source-generated JSON serializer context for HuggingFace embedding provider types.
/// Enables AOT-compatible serialization.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HuggingFaceEmbeddingConfig))]
public partial class HuggingFaceEmbeddingJsonContext : JsonSerializerContext
{
}
