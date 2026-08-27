using HPD.Events;

namespace HPD.Agent;

/// <summary>Commits canonical operation events through the owning agent coordinator.</summary>
internal sealed class AgentOperationEventSink(
    IEventCoordinator events,
    IThreadEventPublisher? threadEvents = null) : IAgentOperationEventSink
{
    public async ValueTask AppendAsync(AgentEvent operationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationEvent);
        cancellationToken.ThrowIfCancellationRequested();
        var hasSession = !string.IsNullOrWhiteSpace(operationEvent.SessionId);
        var hasThread = !string.IsNullOrWhiteSpace(operationEvent.ThreadId);
        if (hasSession != hasThread)
            throw new InvalidOperationException(
                "Operation events must provide both SessionId and ThreadId, or neither.");
        if (hasSession && threadEvents is not null)
        {
            await threadEvents.CommitAndPublishAsync(
                new ThreadKey(operationEvent.SessionId!, operationEvent.ThreadId!),
                operationEvent,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await events.EmitAsync(operationEvent, cancellationToken).ConfigureAwait(false);
    }
}
