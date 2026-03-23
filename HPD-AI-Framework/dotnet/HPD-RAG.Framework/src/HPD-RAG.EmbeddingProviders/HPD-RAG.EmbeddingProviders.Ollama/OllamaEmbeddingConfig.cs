using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.Ollama;

/// <summary>
/// Ollama-specific embedding configuration.
/// Endpoint defaults to http://localhost:11434 if not specified.
///
/// JSON Example (ProviderOptionsJson):
/// <code>
/// { "endpoint": "http://localhost:11434" }
/// </code>
/// </summary>
public sealed class OllamaEmbeddingConfig
{
    /// <summary>
    /// The Ollama server endpoint. Defaults to http://localhost:11434.
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }
}
