using System.Text.Json;
using HPD.RAG.Core.DTOs;

namespace HPD.RAG.Core.Providers.GraphStore;

/// <summary>
/// Standard property graph store abstraction. No equivalent exists in Microsoft.Extensions or Semantic Kernel.
/// Covers both ingestion-time writes and retrieval-time traversal.
/// Query parameter values use JsonElement for AOT-safe trimming — consistent with all MRAG socket types.
/// </summary>
public interface IGraphStore
{
    // ── Write (ingestion) ──────────────────────────────────────────────────────

    Task UpsertNodesAsync(
        IReadOnlyList<MragGraphNodeDto> nodes,
        CancellationToken ct = default);

    Task UpsertEdgesAsync(
        IReadOnlyList<MragGraphEdgeDto> edges,
        CancellationToken ct = default);

    Task DeleteAsync(
        IReadOnlyList<string>? nodeIds = null,
        IReadOnlyList<string>? edgeTypes = null,
        CancellationToken ct = default);

    // ── Read (retrieval) ───────────────────────────────────────────────────────

    /// <summary>
    /// Standard traversal path. Seed entity IDs in, subgraph out.
    /// maxDepth controls hop count (default 2, matching LlamaIndex/LightRAG conventions).
    /// limit caps total result nodes.
    /// IsTruncated is set when limit was hit.
    /// </summary>
    Task<MragGraphResultDto> GetRelationshipsAsync(
        IReadOnlyList<string> seedEntityIds,
        int maxDepth = 2,
        int limit = 30,
        CancellationToken ct = default);

    /// <summary>
    /// Escape hatch for backend-specific structured queries (Cypher, Gremlin, etc.).
    /// The handler passes the query string through — no translation occurs here.
    /// Parameters use JsonElement values for AOT safety.
    /// </summary>
    Task<MragGraphResultDto> StructuredQueryAsync(
        string query,
        IReadOnlyDictionary<string, JsonElement>? parameters = null,
        CancellationToken ct = default);
}
