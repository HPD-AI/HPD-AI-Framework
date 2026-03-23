namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Outcome of a suspension for checkpoint metadata and tracking.
/// </summary>
public enum SuspensionOutcome
{
    /// <summary>
    /// Suspension is active, waiting for response.
    /// </summary>
    Pending,

    /// <summary>
    /// Response received and approved - execution continued.
    /// </summary>
    Approved,

    /// <summary>
    /// Response received but denied - execution halted.
    /// </summary>
    Denied,

    /// <summary>
    /// No response received within timeout - execution halted.
    /// </summary>
    TimedOut
}
