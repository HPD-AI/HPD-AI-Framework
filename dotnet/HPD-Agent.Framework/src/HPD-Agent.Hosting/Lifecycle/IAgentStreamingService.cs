using HPD.Agent;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentStreamingService
{
    Task<AgentServiceResult<ThreadEventObservationLease>> ObserveThreadEventsAsync(
        string agentId,
        string sessionId,
        string threadId,
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

    Task<AgentServiceResult<InterruptionSubmissionDto>> InterruptAsync(
        string agentId,
        string sessionId,
        string threadId,
        string? expectedThreadExecutionId,
        InterruptionRequestEvent interruption,
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
    public ThreadEventObservationLease(
        ISessionStore store,
        ThreadKey thread,
        HPD.Events.EventInbox<AgentEvent> liveEvents)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Thread = thread;
        LiveEvents = liveEvents;
    }

    public ISessionStore Store { get; }
    public ThreadKey Thread { get; }
    public HPD.Events.EventInbox<AgentEvent> LiveEvents { get; }

    public ValueTask DisposeAsync() => LiveEvents.DisposeAsync();
}
