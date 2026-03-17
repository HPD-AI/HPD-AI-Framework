namespace HPD.Agent;

/// <summary>
/// Observer that processes agent events for observability purposes.
/// Inherits from HPD.Events.IEventObserver for cross-domain consistency.
/// Implementations can log, emit telemetry, cache results, etc.
/// </summary>
public interface IAgentEventObserver : HPD.Events.IEventObserver<AgentEvent>
{
    // Inherits:
    // - bool ShouldProcess(AgentEvent evt)
    // - Task OnEventAsync(AgentEvent evt, CancellationToken cancellationToken)
}
