using HPD.Agent;
using HPD.Events;
using HPD.Events.Core;

namespace HPD.MultiAgent;

/// <summary>
/// Event coordinator for multi-agent workflows.
/// Provides approval response methods, subscriptions, and request-session event patterns
/// without requiring a direct reference to HPD.Events or HPD.Events.Core.
///
/// <para>
/// Create one instance, register any subscriptions, then pass it to
/// <see cref="AgentWorkflowInstance.ExecuteStreamingAsync(string, WorkflowEventCoordinator, System.Threading.CancellationToken)"/>.
/// Call <see cref="Approve"/> or <see cref="Deny"/> while iterating the stream to respond to approval requests.
/// </para>
/// </summary>
public sealed class WorkflowEventCoordinator : IDisposable
{
    private readonly EventCoordinator _inner = new();

    /// <summary>
    /// The underlying <see cref="IEventCoordinator"/> used for workflow execution.
    /// </summary>
    internal IEventCoordinator Inner => _inner;

    /// <summary>
    /// Publish a stateless workflow event through the in-process coordinator.
    /// Thread-scoped events must already have crossed their canonical commit boundary.
    /// </summary>
    public ValueTask PublishAsync(
        AgentEvent evt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return _inner.EmitAsync(evt, AgentEventRoutes.Create(_inner, evt), cancellationToken);
    }

    // ── Approval ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Approve a pending <see cref="NodeApprovalRequestEvent"/>.
    /// </summary>
    /// <param name="requestId">The <see cref="NodeApprovalRequestEvent.RequestId"/> from the event.</param>
    /// <param name="reason">Optional reason shown in audit logs.</param>
    /// <param name="resumeData">Optional data passed back into the node after approval.</param>
    public WorkflowApprovalResult Approve(
        string requestId,
        string? reason = null,
        object? resumeData = null) =>
        ToWorkflowApprovalResult(_inner.Approve(requestId, reason, resumeData));

    /// <summary>
    /// Deny a pending <see cref="NodeApprovalRequestEvent"/>.
    /// </summary>
    /// <param name="requestId">The <see cref="NodeApprovalRequestEvent.RequestId"/> from the event.</param>
    /// <param name="reason">Reason for denial (shown to the workflow and in audit logs).</param>
    public WorkflowApprovalResult Deny(
        string requestId,
        string reason = "Denied by user") =>
        ToWorkflowApprovalResult(_inner.Deny(requestId, reason));

    // ── Subscriptions ─────────────────────────────────────────────────────────

    /// <summary>
    /// Register a removable typed subscription.
    /// </summary>
    public IDisposable Subscribe<TEvent>(
        Func<TEvent, ValueTask> handler)
        where TEvent : AgentEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _inner.Subscribe(handler);
    }

    /// <summary>
    /// Register a removable broad subscription for all workflow events.
    /// </summary>
    public IDisposable SubscribeAny(
        Func<AgentEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _inner.SubscribeAny(
            evt => evt is AgentEvent agentEvent
                ? handler(agentEvent)
                : ValueTask.CompletedTask);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose() => _inner.Dispose();

    private static WorkflowApprovalResult ToWorkflowApprovalResult(RespondResult result) =>
        new(
            result.Status switch
            {
                RespondStatus.Accepted => WorkflowApprovalStatus.Accepted,
                RespondStatus.NotFound => WorkflowApprovalStatus.NotFound,
                RespondStatus.AlreadyResolved => WorkflowApprovalStatus.AlreadyResolved,
                RespondStatus.TimedOut => WorkflowApprovalStatus.TimedOut,
                RespondStatus.Cancelled => WorkflowApprovalStatus.Cancelled,
                RespondStatus.ResponseTypeMismatch => WorkflowApprovalStatus.ResponseTypeMismatch,
                RespondStatus.TargetMismatch => WorkflowApprovalStatus.TargetMismatch,
                RespondStatus.AmbiguousRequest => WorkflowApprovalStatus.AmbiguousRequest,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(result),
                    result.Status,
                    "Unknown event response status.")
            },
            result.RequestId,
            result.Message);
}
