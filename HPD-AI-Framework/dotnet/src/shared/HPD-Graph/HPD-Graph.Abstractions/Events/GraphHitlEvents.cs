using HPD.Events;

namespace HPDAgent.Graph.Abstractions.Events;

/// <summary>
/// Node handler requests approval to proceed with an operation.
/// Handler should wait using context.WaitForResponseAsync().
/// </summary>
/// <remarks>
/// <para>
/// Used for Human-in-the-Loop (HITL) scenarios where a node needs user approval
/// before executing a sensitive or irreversible operation.
/// </para>
/// <para><b>When to Use:</b></para>
/// <list type="bullet">
/// <item>Before deleting/modifying data</item>
/// <item>Before expensive operations (API calls, deployments)</item>
/// <item>Before irreversible actions (sending emails, charging credit cards)</item>
/// <item>Dynamic approval decisions within node logic</item>
/// </list>
/// <para><b>Pattern:</b></para>
/// <para>
/// 1. Node emits NodeApprovalRequestEvent<br/>
/// 2. Node calls context.WaitForResponseAsync() - BLOCKS<br/>
/// 3. User/UI receives event and prompts for approval<br/>
/// 4. User responds via NodeApprovalResponseEvent<br/>
/// 5. Node receives response and continues/skips
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // In node handler
/// var requestId = Guid.NewGuid().ToString();
///
/// context.EventCoordinator?.Emit(new NodeApprovalRequestEvent
/// {
///     RequestId = requestId,
///     SourceName = "DeleteRecordsNode",
///     NodeId = context.CurrentNodeId,
///     Message = "Approve deletion of 1000 records?",
///     Description = "This will permanently delete all records matching the criteria.",
///     Metadata = new Dictionary&lt;string, object?&gt; { ["RecordCount"] = 1000 },
///     Priority = EventPriority.Control
/// });
///
/// var response = await context.WaitForResponseAsync&lt;NodeApprovalResponseEvent&gt;(
///     requestId,
///     timeout: TimeSpan.FromMinutes(5),
///     cancellationToken
/// );
///
/// if (!response.Approved)
/// {
///     context.Log("DeleteRecordsNode", $"Operation denied: {response.Reason}");
///     return new NodeExecutionResult.Skipped(SkipReason.UserCancelled, response.Reason);
/// }
///
/// // Continue with deletion...
/// </code>
/// </example>
public sealed record NodeApprovalRequestEvent : GraphEvent, IBidirectionalGraphEvent
{
    /// <summary>Unique identifier for this approval request</summary>
    public required string RequestId { get; init; }

    /// <summary>Source that emitted this event (node ID, handler name, etc.)</summary>
    public required string SourceName { get; init; }

    /// <summary>ID of the node requesting approval</summary>
    public required string NodeId { get; init; }

    /// <summary>Message to display to the user</summary>
    public required string Message { get; init; }

    /// <summary>Optional detailed description of the operation requiring approval</summary>
    public string? Description { get; init; }

    /// <summary>Additional metadata for the approval request (e.g., record counts, cost estimates)</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

    /// <summary>Override Kind to Control for priority routing</summary>
    public new EventKind Kind { get; init; } = EventKind.Control;
}

/// <summary>
/// Response to node approval request.
/// Sent by external handler back to waiting node.
/// </summary>
public sealed record NodeApprovalResponseEvent : GraphEvent, IBidirectionalGraphEvent
{
    /// <summary>Unique identifier matching the request</summary>
    public required string RequestId { get; init; }

    /// <summary>Source that emitted this response (typically "User" or handler name)</summary>
    public required string SourceName { get; init; }

    /// <summary>Whether the operation was approved</summary>
    public required bool Approved { get; init; }

    /// <summary>Optional reason for approval/denial</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Optional data to pass back to the suspended node on approval.
    /// Stored in context channel "suspend_response:{nodeId}" for downstream access.
    /// </summary>
    public object? ResumeData { get; init; }

    /// <summary>Override Kind to Control for priority routing</summary>
    public new EventKind Kind { get; init; } = EventKind.Control;
}

/// <summary>
/// Emitted when an approval request times out waiting for response.
/// Useful for observability, monitoring, and alerting.
/// </summary>
/// <remarks>
/// This event is emitted when:
/// <list type="bullet">
/// <item>A node was suspended with ActiveWaitTimeout greater than zero</item>
/// <item>No NodeApprovalResponseEvent was received within the timeout</item>
/// <item>The graph is about to halt with GraphSuspendedException</item>
/// </list>
///
/// Subscribe to this event type to:
/// <list type="bullet">
/// <item>Log timeout occurrences</item>
/// <item>Send alerts for stuck approvals</item>
/// <item>Track approval SLA metrics</item>
/// </list>
/// </remarks>
public sealed record NodeApprovalTimeoutEvent : GraphEvent
{
    /// <summary>Unique identifier matching the original request</summary>
    public required string RequestId { get; init; }

    /// <summary>Source that originally emitted the request</summary>
    public required string SourceName { get; init; }

    /// <summary>ID of the node that timed out waiting for approval</summary>
    public required string NodeId { get; init; }

    /// <summary>How long the node waited before timing out</summary>
    public required TimeSpan WaitedFor { get; init; }

    /// <summary>Override Kind to Diagnostic for observability routing</summary>
    public new EventKind Kind { get; init; } = EventKind.Diagnostic;
}
