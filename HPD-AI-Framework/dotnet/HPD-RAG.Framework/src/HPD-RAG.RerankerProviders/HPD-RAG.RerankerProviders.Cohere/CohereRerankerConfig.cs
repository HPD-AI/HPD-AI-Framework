namespace HPD.RAG.RerankerProviders.Cohere;

/// <summary>
/// Cohere-specific reranker configuration.
/// </summary>
public sealed class CohereRerankerConfig
{
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string? ModelName { get; set; }

    /// <summary>Maximum number of chunks to send in a single request. Cohere caps at 1000.</summary>
    public int? MaxDocumentsPerRequest { get; set; }
}
