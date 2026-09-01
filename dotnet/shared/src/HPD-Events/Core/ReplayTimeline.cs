using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

#pragma warning restore CS1591

namespace HPD.Events.Core;

/// <summary>Deterministic merged replay timeline over one or more replay sources.</summary>
/// <typeparam name="TEvent">Event type produced by the timeline.</typeparam>
public sealed class ReplayTimeline<TEvent> where TEvent : Event
{
    private static long s_nextOwnerId;
    private readonly object _gate = new();
    private readonly List<Registration> _sources = [];
    private readonly HashSet<string> _sourceIds = new(StringComparer.Ordinal);
    private IReplayOrderingPolicy<TEvent> _ordering = DefaultReplayOrderingPolicy<TEvent>.Instance;
    private Snapshot? _snapshot;
    private readonly long _ownerId = Interlocked.Increment(ref s_nextOwnerId);
    private int _finalizedReadGeneration;

    private ReplayTimeline() { }

    /// <summary>Creates an empty replay timeline.</summary>
    public static ReplayTimeline<TEvent> Create() => new();

    /// <summary>Adds an ordinary replay source without a complete-frame claim.</summary>
    public ReplayTimeline<TEvent> AddSource(string sourceId, IReplaySource<TEvent> source, int priority = 0) => Add(sourceId, source, null, null, priority);

    /// <summary>Adds an ordinary enumerable replay source without a complete-frame claim.</summary>
    public ReplayTimeline<TEvent> AddSource(string sourceId, IEnumerable<TEvent> events, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(events);
        return AddSource(sourceId, new EnumerableReplaySource<TEvent>(events), priority);
    }

    /// <summary>Adds an ordinary asynchronous replay source without a complete-frame claim.</summary>
    public ReplayTimeline<TEvent> AddSource(string sourceId, IAsyncEnumerable<TEvent> events, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(events);
        return AddSource(sourceId, new AsyncEnumerableReplaySource<TEvent>(events), priority);
    }

    /// <summary>Adds a completion-final source with an explicit complete-frame contract.</summary>
    public ReplayTimeline<TEvent> AddFrameSource(string sourceId, IReplaySource<TEvent> source, ReplayFrameSourceContract contract, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (contract.Finality == ReplayTimestampFinality.ExclusiveWatermark)
            throw new ArgumentException("Use the watermarked overload for exclusive-watermark finality.", nameof(contract));
        return Add(sourceId, source, null, contract, priority);
    }

    /// <summary>Adds a watermarked source with an explicit complete-frame contract.</summary>
    public ReplayTimeline<TEvent> AddFrameSource(string sourceId, IWatermarkedReplaySource<TEvent> source, ReplayFrameSourceContract contract, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(contract);
        if (contract.Finality != ReplayTimestampFinality.ExclusiveWatermark)
            throw new ArgumentException("A watermarked source requires exclusive-watermark finality.", nameof(contract));
        return Add(sourceId, null, source, contract, priority);
    }

    /// <summary>Uses a custom replay ordering policy.</summary>
    public ReplayTimeline<TEvent> WithOrdering(IReplayOrderingPolicy<TEvent> ordering)
    {
        ArgumentNullException.ThrowIfNull(ordering);
        lock (_gate)
        {
            EnsureMutable();
            _ordering = ordering;
        }
        return this;
    }

    /// <summary>Reads the merged ordinary replay stream.</summary>
    public IAsyncEnumerable<TEvent> ReadAsync(ReplayReadOptions options, CancellationToken ct = default)
    {
        ValidateOptions(options);
        Snapshot snapshot = BeginRead(requireFrames: false);
        return ReadEventsCore(snapshot, options, ct);
    }

    /// <summary>Reads events with the exact keys and source evidence used by the timeline.</summary>
    public IAsyncEnumerable<ReplayEntry<TEvent>> ReadEntriesAsync(ReplayReadOptions options, CancellationToken ct = default)
    {
        ValidateOptions(options);
        Snapshot snapshot = BeginRead(requireFrames: false);
        return ReadEntriesCore(snapshot, options, ct);
    }

    /// <summary>Reads complete effective-timestamp frames from explicitly contracted sources.</summary>
    public IAsyncEnumerable<ReplayFrame<TEvent>> ReadFramesAsync(ReplayReadOptions options, CancellationToken ct = default)
    {
        ValidateOptions(options);
        Snapshot snapshot = BeginRead(requireFrames: true);
        return ReadFramesCore(snapshot, options, ct);
    }

    /// <summary>
    /// Reads complete frames as opaque, short-lived finality capabilities. Each
    /// handle expires when enumeration advances beyond that frame or is disposed.
    /// </summary>
    public IAsyncEnumerable<FinalizedReplayFrameHandle<TEvent>> ReadFinalizedFramesAsync(
        ReplayReadOptions options,
        CancellationToken ct = default)
    {
        ValidateOptions(options);
        Snapshot snapshot = BeginRead(requireFrames: true);
        int generation = Interlocked.Increment(ref _finalizedReadGeneration);
        return ReadFinalizedFramesCore(snapshot, options, generation, ct);
    }

    private async IAsyncEnumerable<FinalizedReplayFrameHandle<TEvent>> ReadFinalizedFramesCore(
        Snapshot snapshot,
        ReplayReadOptions options,
        int generation,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var owner = new FinalizedReplayFrameOwner<TEvent>(_ownerId);
        await foreach (ReplayFrame<TEvent> frame in ReadFramesCore(snapshot, options, ct).ConfigureAwait(false))
        {
            string digest = ComputeFrameDigest(frame);
            owner.Publish(frame, generation, frame.Boundary.FrameOrdinal, digest);
            try
            {
                yield return new(owner, _ownerId, generation, frame.Boundary.FrameOrdinal, digest);
            }
            finally
            {
                owner.Release();
            }
        }
    }

    internal static string ComputeFrameDigest(ReplayFrame<TEvent> frame)
    {
        var builder = new StringBuilder();
        builder.Append(frame.TimestampNs).Append('|')
            .Append(frame.Boundary.FrameOrdinal).Append('|')
            .Append(frame.Boundary.FirstEntryOrdinal).Append('|')
            .Append(frame.Boundary.EntryCount).Append('|')
            .Append(frame.Boundary.RequestedEventLimit).Append('|')
            .Append(frame.Boundary.ActualCumulativeEntryCount).Append('|')
            .Append(frame.Boundary.CompletedRequestedLimit);
        foreach (ReplayEntry<TEvent> entry in frame.Entries)
        {
            if (entry.Event is not IReplayContentDigest content || string.IsNullOrWhiteSpace(content.ReplayContentDigest))
                throw new ReplayFrameContractException(
                    "Finalized frame capabilities require every event to supply a canonical content digest.",
                    entry.Source,
                    null,
                    entry.Key,
                    null);
            builder.Append('|').Append(entry.Key).Append('|').Append(entry.Source.SourceId).Append('|')
                .Append(entry.Event.GetType().FullName).Append('|').Append(content.ReplayContentDigest);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>Publishes all ordinarily replayed events into an event publisher.</summary>
    public async Task PublishAsync(IEventPublisher publisher, ReplayReadOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        await foreach (TEvent evt in ReadAsync(options, ct).ConfigureAwait(false))
            await publisher.EmitAsync(evt, ct).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<TEvent> ReadEventsCore(Snapshot snapshot, ReplayReadOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (ReplayEntry<TEvent> entry in ReadEntriesCore(snapshot, options, ct).ConfigureAwait(false))
            yield return entry.Event;
    }

    private async IAsyncEnumerable<ReplayEntry<TEvent>> ReadEntriesCore(Snapshot snapshot, ReplayReadOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        var queue = new PriorityQueue<QueuedEntry, ReplayKey>();
        var states = new List<OrdinaryState>(snapshot.Sources.Length);
        try
        {
            foreach (Registration registration in snapshot.Sources)
            {
                IAsyncEnumerator<TEvent> enumerator = registration.Source!
                    .ReadAsync(options with { Limit = null }, ct).GetAsyncEnumerator(ct);
                var state = new OrdinaryState(registration.Info, enumerator);
                states.Add(state);
                if (!await AdvanceOrdinaryAsync(snapshot.Ordering, state, queue).ConfigureAwait(false))
                    await state.DisposeAsync().ConfigureAwait(false);
            }

            int emitted = 0;
            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                QueuedEntry queued = queue.Dequeue();
                yield return queued.Entry;
                if (options.Limit is { } limit && ++emitted >= limit)
                    yield break;
                await AdvanceOrdinaryAsync(snapshot.Ordering, queued.Source, queue).ConfigureAwait(false);
            }
        }
        finally
        {
            await DisposeAllAsync(states, static state => state.DisposeAsync()).ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<ReplayFrame<TEvent>> ReadFramesCore(Snapshot snapshot, ReplayReadOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        long? fromNs = options.From is { } from ? ReplayTime.ToUnixNanoseconds(from) : null;
        long? toNs = options.To is { } to ? ReplayTime.ToUnixNanoseconds(to) : null;
        var states = new List<FrameState>(snapshot.Sources.Length);
        try
        {
            foreach (Registration registration in snapshot.Sources)
            {
                FrameState state = CreateFrameState(registration, ct);
                states.Add(state);
                await AdvanceAsync(snapshot.Ordering, state, toNs, ct).ConfigureAwait(false);
            }

            long frameOrdinal = 0;
            long entryOrdinal = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (states.All(state => AtBoundary(state, toNs)))
                    yield break;

                long? timestamp = MinimumTimestamp(states, toNs);
                if (timestamp is null)
                {
                    bool advanced = false;
                    foreach (FrameState state in states)
                    {
                        if (state.Head is null && !state.Completed && !AtBoundary(state, toNs))
                        {
                            await AdvanceAsync(snapshot.Ordering, state, toNs, ct).ConfigureAwait(false);
                            advanced = true;
                        }
                    }
                    if (!advanced || states.All(state => AtBoundary(state, toNs)))
                        yield break;
                    continue;
                }

                var raw = new List<ReplayEntry<TEvent>>();
                foreach (FrameState state in states)
                    await DrainAsync(snapshot.Ordering, state, timestamp.Value, toNs, raw, ct).ConfigureAwait(false);
                raw.Sort(static (left, right) => left.Key.CompareTo(right.Key));
                ReplayEntry<TEvent>[] visible = raw.Where(entry => Matches(entry, options, fromNs, toNs)).ToArray();
                if (visible.Length == 0)
                    continue;

                long cumulative = checked(entryOrdinal + visible.Length);
                bool limitReached = options.Limit is { } limit && cumulative >= limit;
                var boundary = new ReplayFrameBoundary(frameOrdinal, entryOrdinal, visible.Length, options.Limit, cumulative, limitReached);
                ReadOnlyCollection<ReplayEntry<TEvent>> immutable = Array.AsReadOnly(visible);
                yield return new ReplayFrame<TEvent>(timestamp.Value, immutable, boundary);
                frameOrdinal++;
                entryOrdinal = cumulative;
                if (limitReached)
                    yield break;
            }
        }
        finally
        {
            await DisposeAllAsync(states, static state => state.DisposeAsync()).ConfigureAwait(false);
        }
    }

    private ReplayTimeline<TEvent> Add(string sourceId, IReplaySource<TEvent>? source, IWatermarkedReplaySource<TEvent>? watermarked, ReplayFrameSourceContract? contract, int priority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (source is null && watermarked is null) throw new ArgumentNullException(nameof(source));
        if (contract is not null) ValidateContract(contract);
        lock (_gate)
        {
            EnsureMutable();
            if (!_sourceIds.Add(sourceId)) throw new ArgumentException($"Replay source ID '{sourceId}' is already registered.", nameof(sourceId));
            _sources.Add(new Registration(new ReplaySourceInfo(sourceId, priority, _sources.Count), source, watermarked, contract));
        }
        return this;
    }

    private Snapshot BeginRead(bool requireFrames)
    {
        lock (_gate)
        {
            _snapshot ??= new Snapshot(_sources.ToArray(), _ordering);
            foreach (Registration registration in _snapshot.Sources)
            {
                if (requireFrames && registration.Contract is null)
                    throw new InvalidOperationException($"Replay source '{registration.Info.SourceId}' has no complete-frame contract.");
                if (!requireFrames && registration.Source is null)
                    throw new InvalidOperationException($"Watermarked source '{registration.Info.SourceId}' supports frame reads only.");
                if (registration.Contract?.Cardinality == ReplaySourceCardinality.SingleUse && registration.ReadCount != 0)
                    throw new InvalidOperationException($"Replay source '{registration.Info.SourceId}' is single-use and was already read.");
            }
            foreach (Registration registration in _snapshot.Sources)
                registration.ReadCount++;
            return _snapshot;
        }
    }

    private static async ValueTask<bool> AdvanceOrdinaryAsync(IReplayOrderingPolicy<TEvent> ordering, OrdinaryState state, PriorityQueue<QueuedEntry, ReplayKey> queue)
    {
        if (!await state.Enumerator.MoveNextAsync().ConfigureAwait(false)) return false;
        TEvent evt = state.Enumerator.Current;
        ReplayKey key = Normalize(ordering.GetKey(evt, state.Info, state.NextSequence++), state.Info);
        var entry = new ReplayEntry<TEvent>(evt, key, state.Info);
        queue.Enqueue(new QueuedEntry(state, entry), key);
        return true;
    }

    private static FrameState CreateFrameState(Registration registration, CancellationToken ct) => registration.Watermarked is not null
        ? FrameState.From(registration.Info, registration.Watermarked.ReadMessagesAsync(ct).GetAsyncEnumerator(ct))
        : FrameState.From(registration.Info, registration.Source!.ReadAsync(ReplayReadOptions.All, ct).GetAsyncEnumerator(ct));

    private static async ValueTask AdvanceAsync(IReplayOrderingPolicy<TEvent> ordering, FrameState state, long? toNs, CancellationToken ct)
    {
        while (!state.Completed && state.Head is null && !AtBoundary(state, toNs))
        {
            if (state.Events is not null)
            {
                if (!await state.Events.MoveNextAsync().ConfigureAwait(false)) { state.Completed = true; return; }
                Admit(ordering, state, state.Events.Current);
                return;
            }
            if (!await state.Messages!.MoveNextAsync().ConfigureAwait(false)) { state.Completed = true; return; }
            ReplaySourceMessage<TEvent> message = state.Messages.Current;
            if (message.Kind == ReplaySourceMessageKind.Event)
            {
                if (message.Event is null) throw Failure(state, "An event message contained no event.");
                Admit(ordering, state, message.Event);
                return;
            }
            if (message.Kind != ReplaySourceMessageKind.ExclusiveWatermark) throw Failure(state, "Unknown replay source message kind.");
            if (state.Watermark is { } prior && message.ExclusiveWatermarkTimestampNs < prior)
                throw Failure(state, $"Source watermark regressed from {prior} to {message.ExclusiveWatermarkTimestampNs}.");
            state.Watermark = message.ExclusiveWatermarkTimestampNs;
            ct.ThrowIfCancellationRequested();
            return;
        }
    }

    private static void Admit(IReplayOrderingPolicy<TEvent> ordering, FrameState state, TEvent evt)
    {
        ReplayKey key = Normalize(ordering.GetKey(evt, state.Info, state.NextSequence++), state.Info);
        if (state.Previous is { } previous && key.TimestampNs < previous.TimestampNs)
            throw Failure(state, $"Source regressed from {previous.TimestampNs} to {key.TimestampNs}.", previous, key);
        if (state.Watermark is { } watermark && key.TimestampNs < watermark)
            throw Failure(state, $"Source emitted {key.TimestampNs} below watermark {watermark}.", state.Previous, key);
        state.Previous = key;
        state.Head = new ReplayEntry<TEvent>(evt, key, state.Info);
    }

    private static async ValueTask DrainAsync(IReplayOrderingPolicy<TEvent> ordering, FrameState state, long timestamp, long? toNs, List<ReplayEntry<TEvent>> entries, CancellationToken ct)
    {
        while (true)
        {
            if (state.Head is { } head)
            {
                if (head.Key.TimestampNs > timestamp) return;
                if (head.Key.TimestampNs < timestamp) throw Failure(state, "Internal frame ordering invariant failed.", state.Previous, head.Key);
                entries.Add(head);
                state.Head = null;
            }
            if (state.Completed || state.Watermark is { } watermark && watermark > timestamp) return;
            await AdvanceAsync(ordering, state, toNs, ct).ConfigureAwait(false);
        }
    }

    private static long? MinimumTimestamp(IReadOnlyList<FrameState> states, long? toNs)
    {
        long minimum = long.MaxValue;
        bool found = false;
        foreach (FrameState state in states)
        {
            if (state.Head is not { } head || toNs is { } to && head.Key.TimestampNs >= to) continue;
            minimum = Math.Min(minimum, head.Key.TimestampNs);
            found = true;
        }
        return found ? minimum : null;
    }

    private static bool AtBoundary(FrameState state, long? toNs)
    {
        if (toNs is null) return state.Completed && state.Head is null;
        if (state.Head is { } head) return head.Key.TimestampNs >= toNs.Value;
        return state.Completed || state.Watermark is { } watermark && watermark >= toNs.Value;
    }

    private static bool Matches(ReplayEntry<TEvent> entry, ReplayReadOptions options, long? fromNs, long? toNs) =>
        (options.EventFlowId is null || entry.Event.EventFlowId == options.EventFlowId) &&
        (fromNs is null || entry.Key.TimestampNs >= fromNs.Value) &&
        (toNs is null || entry.Key.TimestampNs < toNs.Value);

    private static ReplayKey Normalize(ReplayKey key, ReplaySourceInfo source) => key.SourceOrdinal == source.SourceOrdinal ? key : key with { SourceOrdinal = source.SourceOrdinal };

    private static void ValidateContract(ReplayFrameSourceContract contract)
    {
        if (contract.TimestampOrder != ReplayTimestampOrder.Nondecreasing) throw new ArgumentException("Complete frames require nondecreasing timestamps.", nameof(contract));
        if (contract.Finality == ReplayTimestampFinality.None) throw new ArgumentException("Complete frames require a finality contract.", nameof(contract));
    }

    private static void ValidateOptions(ReplayReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Limit is <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Replay limits must be positive.");
        if (options.From is { } from && options.To is { } to && from > to) throw new ArgumentException("Replay From must not be later than To.", nameof(options));
    }

    private void EnsureMutable() { if (_snapshot is not null) throw new InvalidOperationException("Replay timeline configuration is sealed after its first read call."); }
    private static ReplayFrameContractException Failure(FrameState state, string message, ReplayKey? previous = null, ReplayKey? offending = null) => new(message, state.Info, previous, offending, state.Watermark);

    private static async ValueTask DisposeAllAsync<TState>(IReadOnlyList<TState> states, Func<TState, ValueTask> dispose)
    {
        List<Exception>? failures = null;
        foreach (TState state in states)
        {
            try { await dispose(state).ConfigureAwait(false); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is { Count: > 0 }) throw new AggregateException("One or more replay sources failed during disposal.", failures);
    }

    private sealed record Snapshot(Registration[] Sources, IReplayOrderingPolicy<TEvent> Ordering);

    private sealed class Registration(ReplaySourceInfo info, IReplaySource<TEvent>? source, IWatermarkedReplaySource<TEvent>? watermarked, ReplayFrameSourceContract? contract)
    {
        public ReplaySourceInfo Info { get; } = info;
        public IReplaySource<TEvent>? Source { get; } = source;
        public IWatermarkedReplaySource<TEvent>? Watermarked { get; } = watermarked;
        public ReplayFrameSourceContract? Contract { get; } = contract;
        public int ReadCount { get; set; }
    }

    private sealed class OrdinaryState(ReplaySourceInfo info, IAsyncEnumerator<TEvent> enumerator)
    {
        public ReplaySourceInfo Info { get; } = info;
        public IAsyncEnumerator<TEvent> Enumerator { get; } = enumerator;
        public long NextSequence { get; set; }
        private bool Disposed { get; set; }
        public async ValueTask DisposeAsync() { if (Disposed) return; Disposed = true; await Enumerator.DisposeAsync().ConfigureAwait(false); }
    }

    private sealed record QueuedEntry(OrdinaryState Source, ReplayEntry<TEvent> Entry);

    private sealed class FrameState
    {
        private FrameState(ReplaySourceInfo info) => Info = info;
        public ReplaySourceInfo Info { get; }
        public IAsyncEnumerator<TEvent>? Events { get; private init; }
        public IAsyncEnumerator<ReplaySourceMessage<TEvent>>? Messages { get; private init; }
        public ReplayEntry<TEvent>? Head { get; set; }
        public ReplayKey? Previous { get; set; }
        public long? Watermark { get; set; }
        public long NextSequence { get; set; }
        public bool Completed { get; set; }
        private bool Disposed { get; set; }
        public static FrameState From(ReplaySourceInfo info, IAsyncEnumerator<TEvent> events) => new(info) { Events = events };
        public static FrameState From(ReplaySourceInfo info, IAsyncEnumerator<ReplaySourceMessage<TEvent>> messages) => new(info) { Messages = messages };
        public async ValueTask DisposeAsync()
        {
            if (Disposed) return;
            Disposed = true;
            if (Events is not null) await Events.DisposeAsync().ConfigureAwait(false);
            if (Messages is not null) await Messages.DisposeAsync().ConfigureAwait(false);
        }
    }
}
