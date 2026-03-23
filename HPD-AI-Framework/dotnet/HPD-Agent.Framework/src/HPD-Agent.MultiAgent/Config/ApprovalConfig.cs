namespace HPD.MultiAgent.Config;

/// <summary>
/// Configuration for human-in-the-loop approval workflows.
/// </summary>
public sealed record ApprovalConfig
{
    /// <summary>
    /// Condition that determines if approval is required.
    /// Receives the agent's outputs and returns true if approval needed.
    /// Null = no approval needed (default).
    /// </summary>
    public Func<ApprovalContext, bool>? Condition { get; init; }

    /// <summary>
    /// Message to display in the approval request.
    /// Can be static or dynamic based on context.
    /// </summary>
    public Func<ApprovalContext, string>? Message { get; init; }

    /// <summary>
    /// How long to wait for approval before timing out.
    /// Default: 5 minutes.
    /// TimeSpan.Zero = immediate suspend (for long-term/async approvals).
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Optional description providing more context about the approval.
    /// </summary>
    public Func<ApprovalContext, string?>? Description { get; init; }

    /// <summary>
    /// What to do if approval times out.
    /// Default: Deny (fail the node).
    /// </summary>
    public ApprovalTimeoutBehavior TimeoutBehavior { get; init; } = ApprovalTimeoutBehavior.Deny;
}

/// <summary>
/// Behavior when approval times out.
/// </summary>
public enum ApprovalTimeoutBehavior
{
    /// <summary>
    /// Deny the operation (node fails with timeout error).
    /// </summary>
    Deny,

    /// <summary>
    /// Auto-approve the operation after timeout.
    /// Use with caution - only for low-risk operations.
    /// </summary>
    AutoApprove,

    /// <summary>
    /// Suspend indefinitely until manual intervention.
    /// Useful for long-term approvals that may take hours/days.
    /// </summary>
    SuspendIndefinitely
}

/// <summary>
/// Context passed to approval condition and message functions.
/// </summary>
public sealed class ApprovalContext
{
    /// <summary>
    /// The node ID requesting approval.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// The agent's outputs that triggered the approval check.
    /// </summary>
    public required IReadOnlyDictionary<string, object> Outputs { get; init; }

    /// <summary>
    /// The original input to the workflow.
    /// </summary>
    public string? OriginalInput { get; init; }

    /// <summary>
    /// Workflow-level data that may have been set by previous nodes.
    /// </summary>
    public IReadOnlyDictionary<string, object>? WorkflowData { get; init; }

    /// <summary>
    /// Get a typed output value.
    /// </summary>
    public T? GetOutput<T>(string key)
    {
        if (Outputs.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    /// <summary>
    /// Check if an output key exists.
    /// </summary>
    public bool HasOutput(string key) => Outputs.ContainsKey(key);
}
