using HPD.Agent.Serialization;
using HPD.Events;

namespace HPD.Agent;

/// <summary>Routes live events and commits durable events through one store-owned codec authority.</summary>
public interface IAgentEventPublisher
{
    /// <summary>Gets the exact codec owned by the backing session store.</summary>
    AgentEventCodec EventCodec { get; }

    /// <summary>Gets the current committed journal head.</summary>
    ValueTask<ThreadEventHead?> GetHeadAsync(ThreadKey thread, CancellationToken cancellationToken = default);

    /// <summary>Routes an event according to its immutable durability descriptor.</summary>
    ValueTask<AgentEvent> PublishAsync(ThreadKey thread, AgentEvent value, CancellationToken cancellationToken = default);

    /// <summary>Publishes an explicit sequence-zero observation without journal mutation.</summary>
    ValueTask<AgentEvent> PublishLiveAsync(AgentEvent value, CancellationToken cancellationToken = default);

    /// <summary>Commits one explicitly durable event and then publishes the committed value.</summary>
    ValueTask<AgentEvent> CommitAndPublishAsync(ThreadKey thread, AgentEvent value, CancellationToken cancellationToken = default);

    /// <summary>Conditionally commits durable events and then publishes their exact committed values.</summary>
    ValueTask<ThreadEventAppendResult> CommitAndPublishAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> proposedEvents,
        ThreadAppendCondition condition = default,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a durable progressive delta and publishes its sequence-zero live form.</summary>
    ValueTask<AgentEvent> StageAndPublishDeltaAsync(ThreadKey thread, AgentEvent delta, CancellationToken cancellationToken = default);

    /// <summary>Settles staged durable deltas and publishes the committed boundary.</summary>
    ValueTask<ThreadEventAppendResult> FinalizeAndPublishDeltasAsync(ThreadKey thread, AgentEvent messageEnd, CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces a journal with durable events and publishes committed replacements.</summary>
    ValueTask<ThreadJournalReplaceResult> ReplaceAndPublishAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> replacementEvents,
        ThreadJournalCursor expectedCursor,
        CancellationToken cancellationToken = default);
}

/// <summary>Coordinator-scoped event publisher backed by one codec-owned session store.</summary>
public sealed class AgentEventPublisher : IAgentEventPublisher
{
    private readonly ISessionStore _store;
    private readonly IEventCoordinator _coordinator;

    /// <summary>Creates a publisher whose serialization authority is derived exclusively from its store.</summary>
    public AgentEventPublisher(ISessionStore store, IEventCoordinator coordinator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    /// <inheritdoc />
    public AgentEventCodec EventCodec => _store.EventCodec;

    /// <inheritdoc />
    public ValueTask<ThreadEventHead?> GetHeadAsync(ThreadKey thread, CancellationToken cancellationToken = default) =>
        _store.GetThreadEventHeadAsync(thread, cancellationToken);

    /// <inheritdoc />
    public ValueTask<AgentEvent> PublishAsync(
        ThreadKey thread,
        AgentEvent value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!EventCodec.TryGetByType(value.GetType(), out var descriptor))
            throw new InvalidOperationException($"Agent event type '{value.GetType().FullName}' is not present in codec '{EventCodec.Digest}'.");
        return descriptor.Durability == AgentEventDurability.Durable
            ? CommitAndPublishAsync(thread, value, cancellationToken)
            : PublishLiveAsync(value, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<AgentEvent> PublishLiveAsync(
        AgentEvent value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!EventCodec.TryGetByType(value.GetType(), out _))
            throw new InvalidOperationException($"Agent event type '{value.GetType().FullName}' is not present in codec '{EventCodec.Digest}'.");
        var live = value with { ThreadSequenceNumber = 0 };
        await _coordinator.EmitAsync(live, cancellationToken).ConfigureAwait(false);
        return live;
    }

    /// <inheritdoc />
    public async ValueTask<AgentEvent> CommitAndPublishAsync(
        ThreadKey thread,
        AgentEvent value,
        CancellationToken cancellationToken = default)
    {
        var result = await CommitAndPublishAsync(thread, [value], ThreadAppendCondition.Any, cancellationToken)
            .ConfigureAwait(false);
        return result.CommittedEvents[0];
    }

    /// <inheritdoc />
    public async ValueTask<ThreadEventAppendResult> CommitAndPublishAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> proposedEvents,
        ThreadAppendCondition condition = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposedEvents);
        if (proposedEvents.Count == 0)
            throw new ArgumentException("At least one event is required.", nameof(proposedEvents));
        foreach (var evt in proposedEvents)
            EventCodec.RequireDurable(evt);
        var scoped = proposedEvents.Select(evt => ThreadEventValidation.PrepareForAppend(
            thread.SessionId, thread.ThreadId, evt)).ToArray();
        var result = await _store.AppendThreadEventsAsync(thread, scoped, condition, cancellationToken)
            .ConfigureAwait(false);
        foreach (var committed in result.CommittedEvents)
            await _coordinator.EmitAsync(committed, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<AgentEvent> StageAndPublishDeltaAsync(
        ThreadKey thread,
        AgentEvent delta,
        CancellationToken cancellationToken = default)
    {
        EventCodec.RequireDurable(delta);
        if (_store is not IThreadDeltaStore deltaStore)
            return await CommitAndPublishAsync(thread, delta, cancellationToken).ConfigureAwait(false);
        var scoped = ThreadEventValidation.PrepareForAppend(thread.SessionId, thread.ThreadId, delta) with
        {
            ThreadSequenceNumber = 0
        };
        await deltaStore.StageThreadDeltaAsync(thread, scoped, cancellationToken).ConfigureAwait(false);
        await _coordinator.EmitAsync(scoped, cancellationToken).ConfigureAwait(false);
        return scoped;
    }

    /// <inheritdoc />
    public async ValueTask<ThreadEventAppendResult> FinalizeAndPublishDeltasAsync(
        ThreadKey thread,
        AgentEvent messageEnd,
        CancellationToken cancellationToken = default)
    {
        EventCodec.RequireDurable(messageEnd);
        if (_store is not IThreadDeltaStore deltaStore)
            return await CommitAndPublishAsync(thread, [messageEnd], cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        var scoped = ThreadEventValidation.PrepareForAppend(thread.SessionId, thread.ThreadId, messageEnd);
        var result = await deltaStore.FinalizeThreadDeltasAsync(thread, scoped, cancellationToken)
            .ConfigureAwait(false);
        await _coordinator.EmitAsync(result.CommittedEvents[^1], cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<ThreadJournalReplaceResult> ReplaceAndPublishAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> replacementEvents,
        ThreadJournalCursor expectedCursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacementEvents);
        if (replacementEvents.Count == 0)
            throw new ArgumentException("A replacement journal cannot be empty.", nameof(replacementEvents));
        foreach (var evt in replacementEvents)
            EventCodec.RequireDurable(evt);
        var scoped = replacementEvents.Select(evt => ThreadEventValidation.PrepareForAppend(
            thread.SessionId, thread.ThreadId, evt)).ToArray();
        var result = await _store.ReplaceThreadEventsAsync(thread, scoped, expectedCursor, cancellationToken)
            .ConfigureAwait(false);
        foreach (var committed in result.CommittedEvents)
            await _coordinator.EmitAsync(committed, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
