using HPD.Agent;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentStreamingService
{
    /// <summary>Creates a live observation lease rooted at one complete thread key.</summary>
    /// <remarks>Omission selects only the anchor. Descendant delivery is explicit and never merges descendant journals into the anchor cursor.</remarks>
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
