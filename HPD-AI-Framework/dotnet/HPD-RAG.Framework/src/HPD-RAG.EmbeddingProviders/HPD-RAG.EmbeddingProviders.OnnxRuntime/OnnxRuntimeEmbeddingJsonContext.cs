using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.OnnxRuntime;

/// <summary>
/// Source-generated JSON serializer context for OnnxRuntime embedding provider types.
/// Enables AOT-compatible serialization.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OnnxRuntimeEmbeddingConfig))]
public partial class OnnxRuntimeEmbeddingJsonContext : JsonSerializerContext
{
}
