using System.Collections.ObjectModel;

namespace HPD.Agent.Audio.VoiceActivity;

internal enum VoiceActivitySourceHealthV1 : byte
{
    Acquiring = 1,
    Warming = 2,
    Ready = 3,
    Degraded = 4,
    Saturated = 5,
    Faulted = 6,
    Replacing = 7,
    Draining = 8,
    Stopped = 9,
    NotObservable = 10,
}

internal sealed record VoiceActivityRuntimeSnapshotV1
{
    private readonly KeyValuePair<string, VoiceActivitySourceHealthV1>[] _sourceHealth;
    private readonly string[] _warnings;

    internal VoiceActivityRuntimeSnapshotV1(
        ulong projectionSequence,
        VoiceActivityLifecycleSnapshotV1 lifecycle,
        VoiceActivityPromotionStateV1 promotionState,
        ulong lastPromotionSequence,
        IReadOnlyDictionary<string, VoiceActivitySourceHealthV1> sourceHealth,
        IReadOnlyList<string> warnings,
        ulong observerDrops)
    {
        if (projectionSequence == 0) throw new ArgumentOutOfRangeException(nameof(projectionSequence));
        Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        if (!Enum.IsDefined(promotionState)) throw new ArgumentOutOfRangeException(nameof(promotionState));
        ArgumentNullException.ThrowIfNull(sourceHealth);
        ArgumentNullException.ThrowIfNull(warnings);
        _sourceHealth = sourceHealth.OrderBy(static row => row.Key, StringComparer.Ordinal).ToArray();
        if (_sourceHealth.Length != lifecycle.SourceGenerations.Count || _sourceHealth.Any(static row =>
                string.IsNullOrWhiteSpace(row.Key) || !Enum.IsDefined(row.Value)) ||
            lifecycle.SourceGenerations.Keys.Any(key => !_sourceHealth.Any(row => row.Key == key)))
            throw new ArgumentException("Snapshot health must exactly cover current sources.", nameof(sourceHealth));
        _warnings = warnings.Select(static warning =>
            ActivitySourceRequestV1.RequireAscii(warning, nameof(warnings))).ToArray();
        if (_warnings.Length > 128) throw new ArgumentOutOfRangeException(nameof(warnings));
        ProjectionSequence = projectionSequence;
        PromotionState = promotionState;
        LastPromotionSequence = lastPromotionSequence;
        SourceHealth = new ReadOnlyDictionary<string, VoiceActivitySourceHealthV1>(
            _sourceHealth.ToDictionary(static row => row.Key, static row => row.Value, StringComparer.Ordinal));
        Warnings = Array.AsReadOnly(_warnings);
        ObserverDrops = observerDrops;
    }

    internal ulong ProjectionSequence { get; }
    internal VoiceActivityLifecycleSnapshotV1 Lifecycle { get; }
    internal VoiceActivityPromotionStateV1 PromotionState { get; }
    internal ulong LastPromotionSequence { get; }
    internal IReadOnlyDictionary<string, VoiceActivitySourceHealthV1> SourceHealth { get; }
    internal IReadOnlyList<string> Warnings { get; }
    internal ulong ObserverDrops { get; }
}

internal enum VoiceActivityObserverAdmissionResultV1 : byte
{
    Admitted = 1,
    ObserverLimit = 2,
    AggregateCapacityExceeded = 3,
    Closed = 4,
}

internal sealed class VoiceActivityObservationSubscriptionV1 : IDisposable
{
    private readonly VoiceActivityObservationHubV1 _owner;
    private readonly VoiceActivityRuntimeSnapshotV1?[] _queue;
    private int _head;
    private int _count;
    private bool _disposed;

    internal VoiceActivityObservationSubscriptionV1(VoiceActivityObservationHubV1 owner, int capacity)
    {
        _owner = owner;
        _queue = new VoiceActivityRuntimeSnapshotV1?[capacity];
    }

    internal int Capacity => _queue.Length;
    internal ulong Dropped { get; private set; }

    internal bool TryRead(out VoiceActivityRuntimeSnapshotV1? snapshot)
    {
        lock (_queue)
        {
            if (_count == 0) { snapshot = null; return false; }
            snapshot = _queue[_head];
            _queue[_head] = null;
            _head = (_head + 1) % _queue.Length;
            _count--;
            return true;
        }
    }

    internal bool Enqueue(VoiceActivityRuntimeSnapshotV1 snapshot)
    {
        lock (_queue)
        {
            if (_disposed) return false;
            var dropped = _count == _queue.Length;
            if (dropped)
            {
                _queue[_head] = null;
                _head = (_head + 1) % _queue.Length;
                _count--;
                Dropped++;
            }
            _queue[(_head + _count) % _queue.Length] = snapshot;
            _count++;
            return dropped;
        }
    }

    public void Dispose()
    {
        lock (_queue)
        {
            if (_disposed) return;
            _disposed = true;
            Array.Clear(_queue);
            _count = 0;
        }
        _owner.Release(this);
    }
}

internal sealed class VoiceActivityObservationHubV1 : IDisposable
{
    private readonly object _gate = new();
    private readonly int _maximumObservers;
    private readonly int _maximumAggregateCapacity;
    private readonly HashSet<VoiceActivityObservationSubscriptionV1> _subscriptions = [];
    private int _reservedCapacity;
    private bool _closed;
    private ulong _drops;

    internal VoiceActivityObservationHubV1(int maximumObservers, int maximumAggregateCapacity)
    {
        if (maximumObservers is < 1 or > 1_024) throw new ArgumentOutOfRangeException(nameof(maximumObservers));
        if (maximumAggregateCapacity is < 1 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(maximumAggregateCapacity));
        _maximumObservers = maximumObservers;
        _maximumAggregateCapacity = maximumAggregateCapacity;
    }

    internal ulong Drops { get { lock (_gate) return _drops; } }

    internal VoiceActivityObserverAdmissionResultV1 TrySubscribe(int capacity,
        out VoiceActivityObservationSubscriptionV1? subscription)
    {
        if (capacity is < 1 or > 4_096) throw new ArgumentOutOfRangeException(nameof(capacity));
        lock (_gate)
        {
            if (_closed) { subscription = null; return VoiceActivityObserverAdmissionResultV1.Closed; }
            if (_subscriptions.Count == _maximumObservers)
            { subscription = null; return VoiceActivityObserverAdmissionResultV1.ObserverLimit; }
            if (_reservedCapacity > _maximumAggregateCapacity - capacity)
            { subscription = null; return VoiceActivityObserverAdmissionResultV1.AggregateCapacityExceeded; }
            subscription = new VoiceActivityObservationSubscriptionV1(this, capacity);
            _subscriptions.Add(subscription);
            _reservedCapacity += capacity;
            return VoiceActivityObserverAdmissionResultV1.Admitted;
        }
    }

    internal void Publish(VoiceActivityRuntimeSnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (_closed) return;
            foreach (var subscription in _subscriptions)
                if (subscription.Enqueue(snapshot)) _drops++;
        }
    }

    internal void Release(VoiceActivityObservationSubscriptionV1 subscription)
    {
        lock (_gate)
            if (_subscriptions.Remove(subscription)) _reservedCapacity -= subscription.Capacity;
    }

    public void Dispose()
    {
        VoiceActivityObservationSubscriptionV1[] subscriptions;
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            subscriptions = _subscriptions.ToArray();
        }
        foreach (var subscription in subscriptions) subscription.Dispose();
    }
}

internal sealed class VoiceActivitySnapshotWriterV1
{
    private readonly object _gate = new();
    private readonly VoiceActivityObservationHubV1? _observers;
    private VoiceActivityRuntimeSnapshotV1? _current;

    internal VoiceActivitySnapshotWriterV1(VoiceActivityObservationHubV1? observers = null) =>
        _observers = observers;

    internal VoiceActivityRuntimeSnapshotV1? Current { get { lock (_gate) return _current; } }

    internal bool TryPublish(
        ulong expectedPreviousSequence,
        VoiceActivityLifecycleSnapshotV1 lifecycle,
        VoiceActivityPromotionStateV1 promotionState,
        ulong lastPromotionSequence,
        IReadOnlyDictionary<string, VoiceActivitySourceHealthV1> sourceHealth,
        IReadOnlyList<string> warnings)
    {
        VoiceActivityRuntimeSnapshotV1 snapshot;
        lock (_gate)
        {
            if ((_current?.ProjectionSequence ?? 0) != expectedPreviousSequence) return false;
            if (_current is not null && (lifecycle.LifecycleRevision < _current.Lifecycle.LifecycleRevision ||
                lastPromotionSequence < _current.LastPromotionSequence ||
                _current.Lifecycle.State == VoiceActivityLifecycleStateV1.Completed &&
                lifecycle != _current.Lifecycle))
                return false;
            snapshot = new VoiceActivityRuntimeSnapshotV1(expectedPreviousSequence + 1, lifecycle,
                promotionState, lastPromotionSequence, sourceHealth, warnings, _observers?.Drops ?? 0);
            _current = snapshot;
        }
        _observers?.Publish(snapshot);
        return true;
    }
}
