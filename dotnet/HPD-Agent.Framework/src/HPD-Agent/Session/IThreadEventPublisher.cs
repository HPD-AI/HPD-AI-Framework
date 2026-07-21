using HPD.Events;
using System.Text.Json;

namespace HPD.Agent;

/// <summary>
/// Commits canonical thread events and only then publishes the exact committed values.
/// </summary>
public interface IThreadEventPublisher
{
    ValueTask<ThreadEventHead?> GetHeadAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default);

    ValueTask<AgentEvent> CommitAndPublishAsync(
        ThreadKey thread,
        AgentEvent proposedEvent,
        CancellationToken cancellationToken = default);

    ValueTask<ThreadEventAppendResult> CommitAndPublishAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> proposedEvents,
        ThreadAppendCondition condition = default,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a progressive delta durably and publishes its sequence-zero live form.</summary>
    ValueTask<AgentEvent> StageAndPublishDeltaAsync(
        ThreadKey thread,
        AgentEvent delta,
        CancellationToken cancellationToken = default);

    /// <summary>Commits compact staged deltas plus their boundary and publishes only the boundary live.</summary>
    ValueTask<ThreadEventAppendResult> FinalizeAndPublishDeltasAsync(
        ThreadKey thread,
        AgentEvent messageEnd,
        CancellationToken cancellationToken = default);

    ValueTask<ThreadJournalReplaceResult> ReplaceAndPublishAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> replacementEvents,
        ThreadJournalCursor expectedCursor,
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

    public ValueTask<ThreadEventHead?> GetHeadAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default) =>
        _store.GetThreadEventHeadAsync(thread, cancellationToken);

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

    public async ValueTask<AgentEvent> StageAndPublishDeltaAsync(
        ThreadKey thread,
        AgentEvent delta,
        CancellationToken cancellationToken = default)
    {
        if (_store is not IThreadDeltaStore deltaStore)
            return await CommitAndPublishAsync(thread, delta, cancellationToken).ConfigureAwait(false);

        var scoped = ThreadEventValidation.PrepareForAppend(thread.SessionId, thread.ThreadId, delta) with
        {
            ThreadSequenceNumber = 0
        };
        _ = JsonSerializer.Serialize<AgentEvent>(scoped, ThreadEventJson.CompactOptions);
        await deltaStore.StageThreadDeltaAsync(thread, scoped, cancellationToken).ConfigureAwait(false);
        await _coordinator.EmitAsync(scoped, cancellationToken).ConfigureAwait(false);
        return scoped;
    }

    public async ValueTask<ThreadEventAppendResult> FinalizeAndPublishDeltasAsync(
        ThreadKey thread,
        AgentEvent messageEnd,
        CancellationToken cancellationToken = default)
    {
        if (_store is not IThreadDeltaStore deltaStore)
            return await CommitAndPublishAsync(thread, [messageEnd], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        var scoped = ThreadEventValidation.PrepareForAppend(thread.SessionId, thread.ThreadId, messageEnd);
        _ = JsonSerializer.Serialize<AgentEvent>(scoped, ThreadEventJson.CompactOptions);
        var result = await deltaStore.FinalizeThreadDeltasAsync(thread, scoped, cancellationToken)
            .ConfigureAwait(false);
        await _coordinator.EmitAsync(result.CommittedEvents[^1], cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<ThreadJournalReplaceResult> ReplaceAndPublishAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> replacementEvents,
        ThreadJournalCursor expectedCursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacementEvents);
        if (replacementEvents.Count == 0)
            throw new ArgumentException("A replacement journal cannot be empty.", nameof(replacementEvents));

        var scoped = replacementEvents.Select(evt => ThreadEventValidation.PrepareForAppend(
            thread.SessionId, thread.ThreadId, evt)).ToArray();
        foreach (var evt in scoped)
            _ = JsonSerializer.Serialize<AgentEvent>(evt, ThreadEventJson.CompactOptions);

        var result = await _store.ReplaceThreadEventsAsync(
            thread, scoped, expectedCursor, cancellationToken).ConfigureAwait(false);
        foreach (var committed in result.CommittedEvents)
            await _coordinator.EmitAsync(committed, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
