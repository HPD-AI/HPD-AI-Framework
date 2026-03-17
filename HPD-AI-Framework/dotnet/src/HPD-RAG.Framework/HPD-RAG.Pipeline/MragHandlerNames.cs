namespace HPD.RAG.Pipeline;

/// <summary>
/// Canonical string constants for every built-in MRAG handler name.
/// Use these instead of raw string literals when calling
/// <see cref="MragPipeline.AddHandler{TConfig}"/> so that refactors and typos
/// are caught at compile time.
///
/// These names are locked by M1e / M1f / M2 — do not change them.
/// </summary>
public static class MragHandlerNames
{
    // ------------------------------------------------------------------ //
    // Ingestion — Document Readers                                         //
    // ------------------------------------------------------------------ //

    /// <summary>MarkItDownReaderHandler — reads arbitrary file formats via MarkItDown.</summary>
    public const string ReadDocuments = "ReadDocuments";

    /// <summary>MarkdownReaderHandler — reads plain Markdown files.</summary>
    public const string ReadMarkdown = "ReadMarkdown";

    // ------------------------------------------------------------------ //
    // Ingestion — Document Processors                                      //
    // ------------------------------------------------------------------ //

    /// <summary>ImageEnricherHandler — generates AI alt-text for image elements.</summary>
    public const string EnrichImages = "EnrichImages";

    // ------------------------------------------------------------------ //
    // Ingestion — Chunkers                                                 //
    // ------------------------------------------------------------------ //

    /// <summary>HeaderChunkerHandler — splits documents by Markdown / heading hierarchy.</summary>
    public const string ChunkByHeader = "ChunkByHeader";

    /// <summary>SemanticChunkerHandler — embedding-based semantic chunking.</summary>
    public const string ChunkSemantic = "ChunkSemantic";

    /// <summary>SectionChunkerHandler — splits documents by structural section markers.</summary>
    public const string ChunkBySection = "ChunkBySection";

    /// <summary>TokenChunkerHandler — fixed-size token-count splitting.</summary>
    public const string ChunkByToken = "ChunkByToken";

    // ------------------------------------------------------------------ //
    // Ingestion — Chunk Enrichers                                          //
    // ------------------------------------------------------------------ //

    /// <summary>KeywordEnricherHandler — adds keyword list to chunk metadata.</summary>
    public const string EnrichKeywords = "EnrichKeywords";

    /// <summary>SummaryEnricherHandler — adds a short summary to chunk metadata.</summary>
    public const string EnrichSummary = "EnrichSummary";

    /// <summary>SentimentEnricherHandler — adds sentiment label to chunk metadata.</summary>
    public const string EnrichSentiment = "EnrichSentiment";

    /// <summary>ClassificationEnricherHandler — adds classification label to chunk metadata.</summary>
    public const string ClassifyChunks = "ClassifyChunks";

    // ------------------------------------------------------------------ //
    // Ingestion — Vector Store Writers                                     //
    // ------------------------------------------------------------------ //

    /// <summary>PostgresWriterHandler.</summary>
    public const string WritePostgres = "WritePostgres";

    /// <summary>QdrantWriterHandler.</summary>
    public const string WriteQdrant = "WriteQdrant";

    /// <summary>WeaviateWriterHandler.</summary>
    public const string WriteWeaviate = "WriteWeaviate";

    /// <summary>PineconeWriterHandler.</summary>
    public const string WritePinecone = "WritePinecone";

    /// <summary>RedisWriterHandler.</summary>
    public const string WriteRedis = "WriteRedis";

    /// <summary>AzureAISearchWriterHandler.</summary>
    public const string WriteAzureAISearch = "WriteAzureAISearch";

    /// <summary>MongoWriterHandler.</summary>
    public const string WriteMongo = "WriteMongo";

    /// <summary>CosmosMongoWriterHandler.</summary>
    public const string WriteCosmosMongo = "WriteCosmosMongo";

    /// <summary>CosmosNoSqlWriterHandler.</summary>
    public const string WriteCosmosNoSql = "WriteCosmosNoSql";

    /// <summary>SqlServerWriterHandler.</summary>
    public const string WriteSqlServer = "WriteSqlServer";

    /// <summary>SqliteWriterHandler.</summary>
    public const string WriteSqlite = "WriteSqlite";

    /// <summary>MilvusWriterHandler.</summary>
    public const string WriteMilvus = "WriteMilvus";

    /// <summary>InMemoryWriterHandler.</summary>
    public const string WriteInMemory = "WriteInMemory";

    // ------------------------------------------------------------------ //
    // Retrieval — Core                                                     //
    // ------------------------------------------------------------------ //

    /// <summary>EmbedQueryHandler — converts a query string to a float embedding.</summary>
    public const string EmbedQuery = "EmbedQuery";

    /// <summary>VectorSearchHandler — ANN vector similarity search.</summary>
    public const string VectorSearch = "VectorSearch";

    /// <summary>HybridSearchHandler — combined dense + sparse retrieval.</summary>
    public const string HybridSearch = "HybridSearch";

    /// <summary>RerankHandler — reranks results using a configured IReranker.</summary>
    public const string Rerank = "Rerank";

    /// <summary>FormatContextHandler — serialises search results to a string context block.</summary>
    public const string FormatContext = "FormatContext";

    // ------------------------------------------------------------------ //
    // Retrieval — Query Transforms                                         //
    // ------------------------------------------------------------------ //

    /// <summary>GenerateHypotheticalHandler — HyDE: generates a hypothetical answer to embed.</summary>
    public const string GenerateHypothetical = "GenerateHypothetical";

    /// <summary>DecomposeQueryHandler — splits a complex query into sub-queries.</summary>
    public const string DecomposeQuery = "DecomposeQuery";

    /// <summary>MergeResultsHandler — deduplicates and merges multiple result sets.</summary>
    public const string MergeResults = "MergeResults";

    /// <summary>GraphRetrieverHandler — graph traversal retrieval via IGraphStore.</summary>
    public const string GraphRetrieve = "GraphRetrieve";

    // ------------------------------------------------------------------ //
    // Evaluation                                                           //
    // ------------------------------------------------------------------ //

    /// <summary>RelevanceEvalHandler — LLM-as-judge relevance scoring.</summary>
    public const string EvalRelevance = "EvalRelevance";

    /// <summary>GroundednessEvalHandler — LLM-as-judge grounding check.</summary>
    public const string EvalGroundedness = "EvalGroundedness";

    /// <summary>FluencyEvalHandler — LLM-as-judge fluency scoring.</summary>
    public const string EvalFluency = "EvalFluency";

    /// <summary>CompletenessEvalHandler — LLM-as-judge completeness scoring.</summary>
    public const string EvalCompleteness = "EvalCompleteness";

    /// <summary>BLEUEvalHandler — deterministic BLEU metric computation.</summary>
    public const string EvalBLEU = "EvalBLEU";

    /// <summary>WriteEvalResultHandler — persists evaluation results to storage.</summary>
    public const string WriteEvalResult = "WriteEvalResult";
}
