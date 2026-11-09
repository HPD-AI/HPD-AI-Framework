using System.Collections.ObjectModel;

namespace HPD.Agent.Audio.VoiceActivity;

internal enum VoiceActivitySourceHealth : byte
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

internal sealed record VoiceActivityRuntimeSnapshot
{
    private readonly KeyValuePair<string, VoiceActivitySourceHealth>[] _sourceHealth;
    private readonly string[] _warnings;

    internal VoiceActivityRuntimeSnapshot(
        ulong projectionSequence,
        VoiceActivityLifecycleSnapshot lifecycle,
        VoiceActivityPromotionState promotionState,
        ulong lastPromotionSequence,
        IReadOnlyDictionary<string, VoiceActivitySourceHealth> sourceHealth,
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
            ActivitySourceRequest.RequireAscii(warning, nameof(warnings))).ToArray();
        if (_warnings.Length > 128) throw new ArgumentOutOfRangeException(nameof(warnings));
        ProjectionSequence = projectionSequence;
        PromotionState = promotionState;
        LastPromotionSequence = lastPromotionSequence;
        SourceHealth = new ReadOnlyDictionary<string, VoiceActivitySourceHealth>(
            _sourceHealth.ToDictionary(static row => row.Key, static row => row.Value, StringComparer.Ordinal));
        Warnings = Array.AsReadOnly(_warnings);
        ObserverDrops = observerDrops;
    }

    internal ulong ProjectionSequence { get; }
    internal VoiceActivityLifecycleSnapshot Lifecycle { get; }
    internal VoiceActivityPromotionState PromotionState { get; }
    internal ulong LastPromotionSequence { get; }
    internal IReadOnlyDictionary<string, VoiceActivitySourceHealth> SourceHealth { get; }
    internal IReadOnlyList<string> Warnings { get; }
    internal ulong ObserverDrops { get; }
}

internal enum VoiceActivityObserverAdmissionResult : byte
{
    Admitted = 1,
    ObserverLimit = 2,
    AggregateCapacityExceeded = 3,
    Closed = 4,
}

internal sealed class VoiceActivityObservationSubscription : IDisposable
{
    private readonly VoiceActivityObservationHub _owner;
    private readonly VoiceActivityRuntimeSnapshot?[] _queue;
    private int _head;
    private int _count;
    private bool _disposed;

    internal VoiceActivityObservationSubscription(VoiceActivityObservationHub owner, int capacity)
    {
        _owner = owner;
        _queue = new VoiceActivityRuntimeSnapshot?[capacity];
    }

    internal int Capacity => _queue.Length;
    internal ulong Dropped { get; private set; }

    internal bool TryRead(out VoiceActivityRuntimeSnapshot? snapshot)
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

    internal bool Enqueue(VoiceActivityRuntimeSnapshot snapshot)
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

internal sealed class VoiceActivityObservationHub : IDisposable
{
    private readonly object _gate = new();
    private readonly int _maximumObservers;
    private readonly int _maximumAggregateCapacity;
    private readonly HashSet<VoiceActivityObservationSubscription> _subscriptions = [];
    private int _reservedCapacity;
    private bool _closed;
    private ulong _drops;

    internal VoiceActivityObservationHub(int maximumObservers, int maximumAggregateCapacity)
    {
        if (maximumObservers is < 1 or > 1_024) throw new ArgumentOutOfRangeException(nameof(maximumObservers));
        if (maximumAggregateCapacity is < 1 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(maximumAggregateCapacity));
        _maximumObservers = maximumObservers;
        _maximumAggregateCapacity = maximumAggregateCapacity;
    }

    internal ulong Drops { get { lock (_gate) return _drops; } }

    internal VoiceActivityObserverAdmissionResult TrySubscribe(int capacity,
        out VoiceActivityObservationSubscription? subscription)
    {
        if (capacity is < 1 or > 4_096) throw new ArgumentOutOfRangeException(nameof(capacity));
        lock (_gate)
        {
            if (_closed) { subscription = null; return VoiceActivityObserverAdmissionResult.Closed; }
            if (_subscriptions.Count == _maximumObservers)
            { subscription = null; return VoiceActivityObserverAdmissionResult.ObserverLimit; }
            if (_reservedCapacity > _maximumAggregateCapacity - capacity)
            { subscription = null; return VoiceActivityObserverAdmissionResult.AggregateCapacityExceeded; }
            subscription = new VoiceActivityObservationSubscription(this, capacity);
            _subscriptions.Add(subscription);
            _reservedCapacity += capacity;
            return VoiceActivityObserverAdmissionResult.Admitted;
        }
    }

    internal void Publish(VoiceActivityRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (_closed) return;
            foreach (var subscription in _subscriptions)
                if (subscription.Enqueue(snapshot)) _drops++;
        }
    }

    internal void Release(VoiceActivityObservationSubscription subscription)
    {
        lock (_gate)
            if (_subscriptions.Remove(subscription)) _reservedCapacity -= subscription.Capacity;
    }

    public void Dispose()
    {
        VoiceActivityObservationSubscription[] subscriptions;
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            subscriptions = _subscriptions.ToArray();
        }
        foreach (var subscription in subscriptions) subscription.Dispose();
    }
}

internal sealed class VoiceActivitySnapshotWriter
{
    private readonly object _gate = new();
    private readonly VoiceActivityObservationHub? _observers;
    private VoiceActivityRuntimeSnapshot? _current;

    internal VoiceActivitySnapshotWriter(VoiceActivityObservationHub? observers = null) =>
        _observers = observers;

    internal VoiceActivityRuntimeSnapshot? Current { get { lock (_gate) return _current; } }

    internal bool TryPublish(
        ulong expectedPreviousSequence,
        VoiceActivityLifecycleSnapshot lifecycle,
        VoiceActivityPromotionState promotionState,
        ulong lastPromotionSequence,
        IReadOnlyDictionary<string, VoiceActivitySourceHealth> sourceHealth,
        IReadOnlyList<string> warnings)
    {
        VoiceActivityRuntimeSnapshot snapshot;
        lock (_gate)
        {
            if ((_current?.ProjectionSequence ?? 0) != expectedPreviousSequence) return false;
            if (_current is not null && (lifecycle.LifecycleRevision < _current.Lifecycle.LifecycleRevision ||
                lastPromotionSequence < _current.LastPromotionSequence ||
                _current.Lifecycle.State == VoiceActivityLifecycleState.Completed &&
                lifecycle != _current.Lifecycle))
                return false;
            snapshot = new VoiceActivityRuntimeSnapshot(expectedPreviousSequence + 1, lifecycle,
                promotionState, lastPromotionSequence, sourceHealth, warnings, _observers?.Drops ?? 0);
            _current = snapshot;
        }
        _observers?.Publish(snapshot);
        return true;
    }
}
