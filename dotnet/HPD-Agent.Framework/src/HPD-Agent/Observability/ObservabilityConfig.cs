namespace HPD.Agent;

/// <summary>
/// Configuration for event observer sampling and performance optimization.
/// Reduces overhead from high-volume events.
/// </summary>
public class ObservabilityConfig
{
    /// <summary>
    /// Sampling rate for text delta events (0.0 to 1.0).
    /// Example: 0.1 = sample 10% of deltas.
    /// Default: 1.0 (no sampling).
    /// </summary>
    public double TextDeltaSamplingRate { get; set; } = 1.0;

    /// <summary>
    /// Sampling rate for reasoning delta events (0.0 to 1.0).
    /// Default: 1.0 (no sampling).
    /// </summary>
    public double ReasoningDeltaSamplingRate { get; set; } = 1.0;

    /// <summary>
    /// Whether to enable sampling for high-volume events.
    /// Default: false (all events delivered).
    /// </summary>
    public bool EnableSampling { get; set; } = false;

    /// <summary>
    /// Whether to emit observability events (IObservabilityEvent).
    /// Observability events are internal diagnostic events for logging, metrics, and debugging.
    /// When false (default), observability events are not emitted, improving performance
    /// and reducing noise for typical applications.
    /// Set to true to enable internal diagnostics for debugging or monitoring.
    /// Default: false (observability events disabled).
    /// </summary>
    public bool EmitObservabilityEvents { get; set; } = false;

    /// <summary>
    /// Maximum observers to notify concurrently per event.
    /// Prevents task explosion with many observers.
    /// Default: 10.
    /// </summary>
    public int MaxConcurrentObservers { get; set; } = 10;

    /// <summary>
    /// Maximum consecutive failures before observer circuit breaker opens.
    /// Default: 10.
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 10;

    /// <summary>
    /// Number of successful operations required to close an open circuit breaker.
    /// Default: 3.
    /// </summary>
    public int SuccessesToResetCircuitBreaker { get; set; } = 3;
}
