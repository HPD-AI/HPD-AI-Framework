using HPD.Agent;
using HPD.Events;
using HPD.Events.Core;

namespace HPD.MultiAgent;

/// <summary>
/// Event coordinator for multi-agent workflows.
/// Provides approval response methods, subscriptions, and bidirectional event patterns
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
    /// Publish an event through the workflow coordinator.
    /// </summary>
    public void Emit(HPD.Events.Event evt) => _inner.Emit(evt);

    // ── Approval ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Approve a pending <see cref="NodeApprovalRequestEvent"/>.
    /// </summary>
    /// <param name="requestId">The <see cref="NodeApprovalRequestEvent.RequestId"/> from the event.</param>
    /// <param name="reason">Optional reason shown in audit logs.</param>
    /// <param name="resumeData">Optional data passed back into the node after approval.</param>
    public void Approve(string requestId, string? reason = null, object? resumeData = null)
        => _inner.Approve(requestId, reason, resumeData);

    /// <summary>
    /// Deny a pending <see cref="NodeApprovalRequestEvent"/>.
    /// </summary>
    /// <param name="requestId">The <see cref="NodeApprovalRequestEvent.RequestId"/> from the event.</param>
    /// <param name="reason">Reason for denial (shown to the workflow and in audit logs).</param>
    public void Deny(string requestId, string reason = "Denied by user")
        => _inner.Deny(requestId, reason);

    // ── Subscriptions ─────────────────────────────────────────────────────────

    /// <summary>
    /// Register a removable typed subscription.
    /// </summary>
    public IDisposable Subscribe<TEvent>(
        Func<TEvent, ValueTask> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : HPD.Events.Event
        => _inner.Subscribe(handler, options);

    /// <summary>
    /// Register a removable broad subscription for all workflow events.
    /// </summary>
    public IDisposable SubscribeAny(
        Func<HPD.Events.Event, ValueTask> handler,
        EventSubscriptionOptions? options = null)
        => _inner.SubscribeAny(handler, options);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose() => _inner.Dispose();
}
