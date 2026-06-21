using System.Runtime.CompilerServices;

namespace HPD.Events.Core;

/// <summary>
/// Deterministic merged replay timeline over one or more replay sources.
/// </summary>
/// <typeparam name="TEvent">Event type produced by the timeline.</typeparam>
public sealed class ReplayTimeline<TEvent>
    where TEvent : Event
{
    private readonly List<ReplaySourceRegistration> _sources = [];
    private IReplayOrderingPolicy<TEvent> _ordering = DefaultReplayOrderingPolicy<TEvent>.Instance;

    private ReplayTimeline()
    {
    }

    /// <summary>
    /// Create an empty replay timeline.
    /// </summary>
    public static ReplayTimeline<TEvent> Create() => new();

    /// <summary>
    /// Add a replay source to the timeline.
    /// </summary>
    public ReplayTimeline<TEvent> AddSource(
        string sourceId,
        IReplaySource<TEvent> source,
        int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(source);

        _sources.Add(new ReplaySourceRegistration(
            new ReplaySourceInfo(sourceId, priority, _sources.Count),
            source));

        return this;
    }

    /// <summary>
    /// Add an enumerable replay source to the timeline.
    /// </summary>
    public ReplayTimeline<TEvent> AddSource(
        string sourceId,
        IEnumerable<TEvent> events,
        int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(events);
        return AddSource(sourceId, new EnumerableReplaySource<TEvent>(events), priority);
    }

    /// <summary>
    /// Add an async enumerable replay source to the timeline.
    /// </summary>
    public ReplayTimeline<TEvent> AddSource(
        string sourceId,
        IAsyncEnumerable<TEvent> events,
        int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(events);
        return AddSource(sourceId, new AsyncEnumerableReplaySource<TEvent>(events), priority);
    }

    /// <summary>
    /// Use a custom replay ordering policy.
    /// </summary>
    public ReplayTimeline<TEvent> WithOrdering(IReplayOrderingPolicy<TEvent> ordering)
    {
        ArgumentNullException.ThrowIfNull(ordering);
        _ordering = ordering;
        return this;
    }

    /// <summary>
    /// Read the merged replay stream.
    /// </summary>
    public async IAsyncEnumerable<TEvent> ReadAsync(
        ReplayReadOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var queue = new PriorityQueue<QueuedReplayEvent, ReplayKey>();
        var states = new List<SourceState>(_sources.Count);

        try
        {
            for (var i = 0; i < _sources.Count; i++)
            {
                var registration = _sources[i];
                var enumerator = registration.Source
                    .ReadAsync(options with { Limit = null }, ct)
                    .GetAsyncEnumerator(ct);

                var state = new SourceState(registration.Info, enumerator);
                states.Add(state);

                if (await AdvanceAsync(state, queue, ct).ConfigureAwait(false))
                    continue;

                await enumerator.DisposeAsync().ConfigureAwait(false);
                state.Disposed = true;
            }

            var emitted = 0;
            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var queued = queue.Dequeue();
                yield return queued.Event;
                emitted++;

                if (options.Limit is { } limit && emitted >= limit)
                    yield break;

                await AdvanceAsync(queued.Source, queue, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var state in states)
            {
                if (!state.Disposed)
                    await state.Enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Publish all replayed events into an event publisher.
    /// </summary>
    public async Task PublishAsync(
        IEventPublisher publisher,
        ReplayReadOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(options);

        await foreach (var evt in ReadAsync(options, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            await publisher.EmitAsync(evt, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> AdvanceAsync(
        SourceState state,
        PriorityQueue<QueuedReplayEvent, ReplayKey> queue,
        CancellationToken ct)
    {
        if (!await state.Enumerator.MoveNextAsync().ConfigureAwait(false))
        {
            await state.Enumerator.DisposeAsync().ConfigureAwait(false);
            state.Disposed = true;
            return false;
        }

        var sourceSequence = state.NextSequence++;
        var evt = state.Enumerator.Current;
        var key = _ordering.GetKey(evt, state.Info, sourceSequence);
        if (key.SourceOrdinal != state.Info.SourceOrdinal)
        {
            key = key with { SourceOrdinal = state.Info.SourceOrdinal };
        }

        ct.ThrowIfCancellationRequested();
        queue.Enqueue(new QueuedReplayEvent(state, evt), key);
        return true;
    }

    private sealed record ReplaySourceRegistration(
        ReplaySourceInfo Info,
        IReplaySource<TEvent> Source);

    private sealed class SourceState(
        ReplaySourceInfo info,
        IAsyncEnumerator<TEvent> enumerator)
    {
        public ReplaySourceInfo Info { get; } = info;
        public IAsyncEnumerator<TEvent> Enumerator { get; } = enumerator;
        public long NextSequence { get; set; }
        public bool Disposed { get; set; }
    }

    private sealed record QueuedReplayEvent(SourceState Source, TEvent Event);
}
