using System.Text.Json.Serialization;

namespace HPD.RAG.RerankerProviders.Cohere;

/// <summary>
/// Cohere-specific reranker configuration.
/// </summary>
public sealed class CohereRerankerConfig
{
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    [JsonPropertyName("modelName")]
    public string? ModelName { get; set; }

    /// <summary>Maximum number of chunks to send in a single request. Cohere caps at 1000.</summary>
    [JsonPropertyName("maxDocumentsPerRequest")]
    public int? MaxDocumentsPerRequest { get; set; }
}
