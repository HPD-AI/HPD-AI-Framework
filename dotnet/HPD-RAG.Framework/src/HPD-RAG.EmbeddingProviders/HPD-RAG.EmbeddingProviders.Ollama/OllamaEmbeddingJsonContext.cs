using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.Ollama;

/// <summary>
/// Source-generated JSON serializer context for Ollama embedding provider types.
/// Enables AOT-compatible serialization.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OllamaEmbeddingConfig))]
public partial class OllamaEmbeddingJsonContext : JsonSerializerContext
{
}
