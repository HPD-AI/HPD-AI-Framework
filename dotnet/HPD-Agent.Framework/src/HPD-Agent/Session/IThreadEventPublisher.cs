using HPD.Events;
using System.Text.Json;

namespace HPD.Agent;

/// <summary>
/// Commits canonical thread events and only then publishes the exact committed values.
/// </summary>
public interface IThreadEventPublisher
{
    ValueTask<AgentEvent> CommitAndPublishAsync(
        ThreadKey thread,
        AgentEvent proposedEvent,
        CancellationToken cancellationToken = default);

    ValueTask<ThreadEventAppendResult> CommitAndPublishAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> proposedEvents,
        ThreadAppendCondition condition = default,
        CancellationToken cancellationToken = default);
}

/// <summary>Default canonical publisher over one configured session store and event coordinator.</summary>
public sealed class ThreadEventPublisher : IThreadEventPublisher
{
    private readonly ISessionStore _store;
    private readonly IEventCoordinator _coordinator;

    public ThreadEventPublisher(ISessionStore store, IEventCoordinator coordinator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public async ValueTask<AgentEvent> CommitAndPublishAsync(
        ThreadKey thread,
        AgentEvent proposedEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposedEvent);
        var result = await CommitAndPublishAsync(
            thread,
            [proposedEvent],
            ThreadAppendCondition.Any,
            cancellationToken).ConfigureAwait(false);
        return result.CommittedEvents[0];
    }

    public async ValueTask<ThreadEventAppendResult> CommitAndPublishAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> proposedEvents,
        ThreadAppendCondition condition = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposedEvents);
        if (proposedEvents.Count == 0)
            throw new ArgumentException("At least one event is required.", nameof(proposedEvents));

        var scoped = proposedEvents
            .Select(evt => ThreadEventValidation.PrepareForAppend(
                thread.SessionId,
                thread.ThreadId,
                evt))
            .ToArray();

        // Validate the canonical codec before the store mutates durable state. This also
        // makes in-memory and custom stores obey the same registered-event contract.
        foreach (var evt in scoped)
            _ = JsonSerializer.Serialize<AgentEvent>(evt, ThreadEventJson.CompactOptions);

        var result = await _store.AppendThreadEventsAsync(
            thread,
            scoped,
            condition,
            cancellationToken).ConfigureAwait(false);

        foreach (var committed in result.CommittedEvents)
            await _coordinator.EmitAsync(committed, cancellationToken).ConfigureAwait(false);

        return result;
    }
}
