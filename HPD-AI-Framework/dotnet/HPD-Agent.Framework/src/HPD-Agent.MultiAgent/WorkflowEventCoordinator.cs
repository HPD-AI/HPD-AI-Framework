using HPD.Agent;
using HPD.Events;
using HPD.Events.Core;

namespace HPD.MultiAgent;

/// <summary>
/// Event coordinator for multi-agent workflows.
/// Provides approval response methods, observer registration, and bidirectional event patterns
/// without requiring a direct reference to HPD.Events or HPD.Events.Core.
///
/// <para>
/// Create one instance, register any observers, then pass it to
/// <see cref="AgentWorkflowInstance.ExecuteStreamingAsync(string, WorkflowEventCoordinator, System.Threading.CancellationToken)"/>.
/// Call <see cref="Approve"/> or <see cref="Deny"/> while iterating the stream to respond to approval requests.
/// </para>
/// </summary>
public sealed class WorkflowEventCoordinator : IDisposable
{
    private readonly EventCoordinator _inner = new();
    private readonly List<Func<HPD.Events.Event, CancellationToken, Task>> _observers = new();

    /// <summary>
    /// The underlying <see cref="IEventCoordinator"/> used for workflow execution.
    /// </summary>
    internal IEventCoordinator Inner => _inner;

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

    // ── Observers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Register an observer to receive all events emitted during the workflow.
    /// Observers are invoked for each event yielded from <c>ExecuteStreamingAsync</c>.
    /// Multiple observers can be registered; they are called in registration order.
    /// </summary>
    /// <typeparam name="TEvent">Base event type (use <c>Event</c> for all events).</typeparam>
    /// <param name="observer">The observer to register.</param>
    public void AddObserver<TEvent>(IEventObserver<TEvent> observer)
        where TEvent : HPD.Events.Event
    {
        _observers.Add(async (evt, ct) =>
        {
            if (evt is TEvent typedEvt && observer.ShouldProcess(typedEvt))
                await observer.OnEventAsync(typedEvt, ct);
        });
    }

    /// <summary>
    /// Dispatch an event to all registered observers.
    /// Called internally by <see cref="AgentWorkflowInstance"/> for each streamed event.
    /// </summary>
    public async Task DispatchToObserversAsync(HPD.Events.Event evt, CancellationToken ct = default)
    {
        foreach (var observer in _observers)
        {
            try { await observer(evt, ct); }
            catch { /* Observers must not crash the workflow */ }
        }
    }

    /// <summary>
    /// Whether any observers are registered.
    /// </summary>
    public bool HasObservers => _observers.Count > 0;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose() => _inner.Dispose();
}
