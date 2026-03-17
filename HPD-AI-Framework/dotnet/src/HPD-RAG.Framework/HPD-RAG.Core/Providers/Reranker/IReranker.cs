using HPD.RAG.Core.DTOs;

namespace HPD.RAG.Core.Providers.Reranker;

/// <summary>
/// Standard reranking abstraction. No equivalent exists in Microsoft.Extensions.AI or Semantic Kernel.
/// Covers both LLM-based reranking (IChatClient) and dedicated reranking APIs (Cohere, Jina, etc.).
/// </summary>
public interface IReranker
{
    /// <summary>
    /// Rerank the candidate results against the query and return the top-N.
    /// Returned list is sorted by score descending and trimmed to topN.
    /// Score range is provider-dependent — no normalization is required by this interface.
    /// </summary>
    Task<IReadOnlyList<MragSearchResultDto>> RerankAsync(
        string query,
        IReadOnlyList<MragSearchResultDto> results,
        int topN,
        CancellationToken cancellationToken = default);
}
