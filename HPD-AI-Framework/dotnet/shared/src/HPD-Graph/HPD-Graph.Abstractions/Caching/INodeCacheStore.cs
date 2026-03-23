namespace HPDAgent.Graph.Abstractions.Caching;

/// <summary>
/// Storage interface for cached node execution results.
/// Supports multi-tier caching (L1: Memory/SQLite, L2: Redis/S3).
/// </summary>
public interface INodeCacheStore
{
    /// <summary>
    /// Get cached result by fingerprint.
    /// </summary>
    /// <param name="fingerprint">Content-addressable hash</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Cached result if found, null otherwise</returns>
    Task<CachedNodeResult?> GetAsync(string fingerprint, CancellationToken ct = default);

    /// <summary>
    /// Store node execution result with fingerprint.
    /// </summary>
    /// <param name="fingerprint">Content-addressable hash</param>
    /// <param name="result">Result to cache</param>
    /// <param name="ct">Cancellation token</param>
    Task SetAsync(string fingerprint, CachedNodeResult result, CancellationToken ct = default);

    /// <summary>
    /// Check if fingerprint exists in cache (without fetching full result).
    /// </summary>
    /// <param name="fingerprint">Content-addressable hash</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if cached, false otherwise</returns>
    Task<bool> ExistsAsync(string fingerprint, CancellationToken ct = default);

    /// <summary>
    /// Delete cached result.
    /// </summary>
    /// <param name="fingerprint">Content-addressable hash</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteAsync(string fingerprint, CancellationToken ct = default);

    /// <summary>
    /// Clear all cached results (use with caution).
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task ClearAllAsync(CancellationToken ct = default);
}

/// <summary>
/// Cached node execution result.
/// </summary>
public sealed record CachedNodeResult
{
    /// <summary>
    /// Outputs from the node execution.
    /// </summary>
    public required Dictionary<string, object> Outputs { get; init; }

    /// <summary>
    /// When this result was cached.
    /// </summary>
    public required DateTimeOffset CachedAt { get; init; }

    /// <summary>
    /// Execution duration (for metrics).
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Optional metadata about the cached result.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Time-to-live for this cached result.
    /// Null = no expiration (cached forever).
    /// </summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>
    /// Check if this cached result has expired.
    /// </summary>
    public bool IsExpired
    {
        get
        {
            if (!Ttl.HasValue)
                return false;

            var elapsed = DateTimeOffset.UtcNow - CachedAt;
            return elapsed > Ttl.Value;
        }
    }
}
