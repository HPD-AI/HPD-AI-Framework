namespace HPD.Events;

/// <summary>
/// Stable identity and priority for a replay source.
/// </summary>
/// <param name="SourceId">Logical source identifier.</param>
/// <param name="Priority">Source priority used for same-time tie breaking.</param>
/// <param name="SourceOrdinal">Timeline-local source ordinal used as a deterministic tie breaker.</param>
public sealed record ReplaySourceInfo(
    string SourceId,
    int Priority,
    int SourceOrdinal);
