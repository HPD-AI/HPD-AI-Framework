using HPD.RAG.Core.Filters;

namespace HPD.RAG.Core.Retrieval;

/// <summary>
/// Single integration boundary between a retrieval pipeline and any consumer —
/// an agent tool, middleware, background job, etc.
/// Query string in, formatted context string out.
/// MragRetrievalPipeline implements this directly — no wrapper class needed.
/// </summary>
public interface IMragRetriever
{
    Task<string> RetrieveAsync(string query, MragFilterNode? filter = null, CancellationToken ct = default);
}
