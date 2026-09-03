using HPD.Agent;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentStreamingService
{
    /// <summary>Creates a live observation lease rooted at one complete, non-empty thread key.</summary>
    /// <param name="agentId">The hosted agent definition whose runtime graph is observed.</param>
    /// <param name="anchor">The complete session/thread key selecting the exact thread or hierarchy root.</param>
    /// <param name="hierarchy">The explicit branch-relative scope; omission selects only <paramref name="anchor"/>.</param>
    /// <param name="cancellationToken">Cancels lease creation without affecting agent execution.</param>
    /// <remarks>
    /// Keyed observation excludes threadless events and sibling branches. Transitive selections preserve each
    /// origin's route and journal cursor rather than merging descendant journals. The returned inbox retains
    /// per-origin delivery order and its configured backpressure policy; publication does not wait for a consumer.
    /// Disposing the lease completes observation without stopping execution or event bubbling.
    /// </remarks>
    Task<AgentServiceResult<ThreadEventObservationLease>> ObserveThreadEventsAsync(
        string agentId,
        ThreadKey anchor,
        AgentEventHierarchy hierarchy = AgentEventHierarchy.ExactThread,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<InputSubmissionDto>> SubmitInputAsync(
        string agentId,
        string sessionId,
        string threadId,
        AgentInputEvent input,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<ThreadRuntimeStateDto>> GetThreadStateAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<ThreadContextUsage>> EstimateContextUsageAsync(
        string agentId,
        string sessionId,
        string threadId,
        AgentRunConfig? runConfig,
        CancellationToken cancellationToken = default);

    AgentInputEvent ApplyRouteScope(
        AgentInputEvent input,
        string agentId,
        string sessionId,
        string threadId,
        string? threadExecutionId = null);
}

/// <summary>
/// Owns the two sources required for complete hosted event delivery: the canonical thread
/// journal and the selected runtime's live event inbox.
/// </summary>
public sealed class ThreadEventObservationLease : IAsyncDisposable
{
    /// <summary>Creates a lease owning one already-registered live inbox.</summary>
    public ThreadEventObservationLease(
        ISessionStore store,
        ThreadKey anchor,
        AgentEventHierarchy hierarchy,
        HPD.Events.DeliveryInbox<AgentEventDelivery> liveEvents)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Anchor = anchor;
        Hierarchy = hierarchy;
        LiveEvents = liveEvents;
    }

    /// <summary>Gets the authoritative journal store used for anchor replay.</summary>
    public ISessionStore Store { get; }
    /// <summary>Gets the complete thread key at which observation is rooted.</summary>
    public ThreadKey Anchor { get; }
    /// <summary>Gets the explicit live hierarchy selected for this lease.</summary>
    public AgentEventHierarchy Hierarchy { get; }
    /// <summary>Gets routed live deliveries; descendant items retain their own journal identity.</summary>
    public HPD.Events.DeliveryInbox<AgentEventDelivery> LiveEvents { get; }

    /// <summary>Stops observation without stopping execution or mutating journal state.</summary>
    public ValueTask DisposeAsync() => LiveEvents.DisposeAsync();
}
