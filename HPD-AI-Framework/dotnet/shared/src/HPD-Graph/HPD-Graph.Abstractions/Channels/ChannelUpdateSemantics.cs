namespace HPDAgent.Graph.Abstractions.Channels;

/// <summary>
/// Defines how channel updates are handled.
/// Determines the behavior when multiple writes occur to the same channel.
/// </summary>
public enum ChannelUpdateSemantics
{
    /// <summary>
    /// Last write wins (default).
    /// Example: status, currentStep, result
    /// </summary>
    LastValue,

    /// <summary>
    /// Append values to a list.
    /// Example: chunks, messages, logs
    /// CRITICAL: Prevents data loss in parallel execution!
    /// </summary>
    Append,

    /// <summary>
    /// Apply a custom reducer function.
    /// Example: merge dictionaries, sum values
    /// </summary>
    Reducer,

    /// <summary>
    /// Barrier - wait for N writes before proceeding.
    /// Example: synchronization point for parallel tasks
    /// </summary>
    Barrier,

    /// <summary>
    /// Ephemeral - cleared after each step.
    /// Example: temporary routing decisions
    /// </summary>
    Ephemeral
}
