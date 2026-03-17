namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Error severity determines orchestrator behavior.
/// </summary>
public enum ErrorSeverity
{
    /// <summary>
    /// Transient error - retry may succeed.
    /// Examples: network timeout, rate limit, temporary resource unavailable.
    /// </summary>
    Transient,

    /// <summary>
    /// Fatal error - retry will not help.
    /// Examples: invalid input, logic error, permanent resource missing.
    /// </summary>
    Fatal,

    /// <summary>
    /// Warning - node failed but graph can continue.
    /// Examples: optional enrichment failed, non-critical API unavailable.
    /// </summary>
    Warning
}
