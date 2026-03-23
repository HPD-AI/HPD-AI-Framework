namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Configuration for suspension behavior in human-in-the-loop workflows.
/// Controls checkpointing, event emission, and active waiting behavior.
/// </summary>
public sealed record SuspensionOptions
{
    /// <summary>
    /// How long to actively wait for a response before halting.
    /// Default: 30 seconds (quick approvals).
    /// Set to TimeSpan.Zero to halt immediately without waiting.
    /// </summary>
    public TimeSpan ActiveWaitTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to emit bidirectional events during suspension.
    /// Default: true.
    /// Note: Events only emitted if EventCoordinator is available in context.
    /// </summary>
    public bool EmitEvents { get; init; } = true;

    /// <summary>
    /// Whether to save checkpoint before waiting.
    /// Default: true (ensures durability even if process crashes during wait).
    /// Note: Checkpoint only saved if IGraphCheckpointStore is configured.
    /// If no store is configured, this option is ignored.
    /// </summary>
    public bool SaveCheckpointFirst { get; init; } = true;

    /// <summary>
    /// Static factory for immediate suspend (no waiting).
    /// Use when approval may take hours/days and caller will resume from checkpoint.
    /// </summary>
    public static SuspensionOptions ImmediateSuspend => new() { ActiveWaitTimeout = TimeSpan.Zero };

    /// <summary>
    /// Static factory for default suspension options.
    /// 30 second active wait, events enabled, checkpoint first.
    /// </summary>
    public static SuspensionOptions Default => new();

    /// <summary>
    /// Creates suspension options with a custom active wait timeout.
    /// </summary>
    /// <param name="timeout">How long to wait for a response.</param>
    /// <returns>SuspensionOptions configured with the specified timeout.</returns>
    public static SuspensionOptions WithTimeout(TimeSpan timeout) => new() { ActiveWaitTimeout = timeout };
}
