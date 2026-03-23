namespace HPDAgent.Graph.Abstractions.Caching;

/// <summary>
/// Declarative cache configuration for a node.
/// When set, orchestrator automatically caches node results.
/// </summary>
public sealed record CacheOptions
{
    /// <summary>
    /// Cache key strategy (how to compute cache key).
    /// Default: InputsAndCode (cache based on inputs + handler fingerprint)
    /// </summary>
    public CacheKeyStrategy Strategy { get; init; } = CacheKeyStrategy.InputsAndCode;

    /// <summary>
    /// Time-to-live for cached results.
    /// Null = no expiration (cache forever).
    /// </summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>
    /// When to invalidate cache.
    /// Default: OnCodeChange (invalidate when handler code changes)
    /// </summary>
    public CacheInvalidation Invalidation { get; init; } = CacheInvalidation.OnCodeChange;
}

/// <summary>
/// Strategy for computing cache keys.
/// </summary>
public enum CacheKeyStrategy
{
    /// <summary>Hash inputs only (cache across code changes).</summary>
    Inputs,

    /// <summary>Hash inputs + handler code fingerprint (default).</summary>
    InputsAndCode,

    /// <summary>Hash inputs + code + node configuration.</summary>
    InputsCodeAndConfig
}

/// <summary>
/// When to invalidate cached results.
/// </summary>
public enum CacheInvalidation
{
    /// <summary>Never invalidate (cache forever, unless TTL expires).</summary>
    Never,

    /// <summary>Invalidate when handler code changes (default).</summary>
    OnCodeChange,

    /// <summary>Invalidate when node config changes.</summary>
    OnConfigChange,

    /// <summary>Invalidate when inputs change.</summary>
    OnInputChange
}
