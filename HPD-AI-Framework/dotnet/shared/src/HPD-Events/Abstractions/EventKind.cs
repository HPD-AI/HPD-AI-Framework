namespace HPD.Events;

/// <summary>
/// Event classification for filtering and routing.
/// Helps categorize events by their purpose in the system.
/// </summary>
public enum EventKind
{
    /// <summary>
    /// Component lifecycle events (started, stopped, completed).
    /// Examples: MessageTurnStarted, AgentTurnStarted, GraphExecutionCompleted
    /// </summary>
    Lifecycle,

    /// <summary>
    /// Data flowing through the system (text, audio, results).
    /// Examples: TextDelta, AudioChunk, ToolResult, NodeExecutionCompleted
    /// </summary>
    Content,

    /// <summary>
    /// Control flow events (permissions, interruptions, user input).
    /// Examples: PermissionRequest, ClarificationRequest, InterruptionRequest
    /// </summary>
    Control,

    /// <summary>
    /// Telemetry and debugging information.
    /// Examples: CheckpointSaved, CacheHit, PerformanceMetric
    /// </summary>
    Diagnostic
}
