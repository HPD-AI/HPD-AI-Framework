namespace HPD.Agent;

/// <summary>Projects the actionable Agent requests owned by one live thread execution.</summary>
public static class AgentRequestProjector
{
    /// <summary>
    /// Returns unresolved requests for <paramref name="activeThreadExecutionId"/>.
    /// Historical requests owned by any other execution are never actionable.
    /// </summary>
    public static IReadOnlyList<AgentEvent> ProjectPending(
        IEnumerable<AgentEvent> events,
        string? activeThreadExecutionId)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (string.IsNullOrWhiteSpace(activeThreadExecutionId))
            return [];

        var pending = new Dictionary<string, AgentEvent>(StringComparer.Ordinal);
        foreach (var evt in events.OrderBy(item => item.ThreadSequenceNumber))
        {
            if (!StringComparer.Ordinal.Equals(evt.ThreadExecutionId, activeThreadExecutionId))
                continue;

            switch (evt)
            {
                case IAgentRequestEvent request when !string.IsNullOrWhiteSpace(request.RequestId):
                    pending[request.RequestId] = evt;
                    break;

                case IAgentResponseEvent response when !string.IsNullOrWhiteSpace(response.RequestId):
                    pending.Remove(response.RequestId);
                    break;

                case AgentRequestTerminatedEvent terminal when !string.IsNullOrWhiteSpace(terminal.RequestId):
                    pending.Remove(terminal.RequestId);
                    break;
            }
        }

        return pending.Values
            .OrderBy(item => item.ThreadSequenceNumber)
            .ToArray();
    }

    /// <summary>Classifies a response attempt from durable request history.</summary>
    public static AgentRespondResult ClassifyResponseAttempt(
        IEnumerable<AgentEvent> events,
        string requestId,
        string? activeThreadExecutionId)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var correlated = events
            .OrderBy(item => item.ThreadSequenceNumber)
            .Where(item => item switch
            {
                IAgentRequestEvent request => request.RequestId == requestId,
                IAgentResponseEvent response => response.RequestId == requestId,
                AgentRequestTerminatedEvent terminal => terminal.RequestId == requestId,
                _ => false
            })
            .ToArray();
        var requestEvent = correlated
            .LastOrDefault(item => item is IAgentRequestEvent);
        if (requestEvent is null)
            return new AgentRespondResult(AgentRespondStatus.NotFound, requestId, "No durable Agent request was found.");

        foreach (var evt in correlated.Where(item => item.ThreadSequenceNumber > requestEvent.ThreadSequenceNumber))
        {
            if (!StringComparer.Ordinal.Equals(evt.ThreadExecutionId, requestEvent.ThreadExecutionId))
                continue;

            if (evt is IAgentResponseEvent)
                return new AgentRespondResult(AgentRespondStatus.AlreadyResolved, requestId, "The request already has a durable response.");
            if (evt is AgentRequestTerminatedEvent terminal)
            {
                return new AgentRespondResult(
                    terminal.TerminalKind switch
                    {
                        AgentRequestTerminalKind.Expired => AgentRespondStatus.TimedOut,
                        AgentRequestTerminalKind.Cancelled => AgentRespondStatus.Cancelled,
                        AgentRequestTerminalKind.Abandoned => AgentRespondStatus.ExecutionEnded,
                        _ => throw new ArgumentOutOfRangeException(nameof(terminal.TerminalKind))
                    },
                    requestId,
                    terminal.Reason);
            }
        }

        return StringComparer.Ordinal.Equals(requestEvent.ThreadExecutionId, activeThreadExecutionId)
            ? new AgentRespondResult(AgentRespondStatus.RuntimeUnavailable, requestId, "The owning execution is active but its request waiter is unavailable.")
            : new AgentRespondResult(AgentRespondStatus.ExecutionEnded, requestId, "The execution that owned this request is no longer active.");
    }
}
