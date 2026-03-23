namespace HPD.RAG.RerankerProviders.Cohere;

/// <summary>
/// Cohere-specific reranker configuration.
/// ApiKey and ModelName are supplied via the base RerankerConfig fields;
/// this typed config carries Cohere-specific extensions.
/// </summary>
public sealed class CohereRerankerConfig
{
    /// <summary>Maximum number of chunks to send in a single request. Cohere caps at 1000.</summary>
    public int? MaxDocumentsPerRequest { get; set; }
}
