namespace HPD.MultiAgent;

/// <summary>
/// Checkpoint retention policy for multi-agent workflow stores.
/// </summary>
public enum MultiAgentCheckpointRetention
{
    /// <summary>
    /// Keep only the latest checkpoint per workflow execution.
    /// </summary>
    LatestOnly,

    /// <summary>
    /// Keep every checkpoint for debugging, audit, or replay scenarios.
    /// </summary>
    FullHistory
}
