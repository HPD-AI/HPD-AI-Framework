namespace HPD.Agent.Sandbox.State;

internal sealed class ProcessIsolationViolationStore
{
    public const int DefaultCapacity = 100;

    private readonly object _gate = new();
    private readonly Queue<ProcessIsolationViolation> _tail;
    private readonly Dictionary<long, Action<IReadOnlyList<ProcessIsolationViolation>>> _listeners = [];
    private readonly int _capacity;
    private long _nextListenerId;

    public ProcessIsolationViolationStore(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

        _capacity = capacity;
        _tail = new Queue<ProcessIsolationViolation>(capacity);
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _tail.Count;
        }
    }

    private int _totalCount;

    public int TotalCount
    {
        get
        {
            lock (_gate)
                return _totalCount;
        }
    }

    public void Add(ProcessIsolationViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        Action<IReadOnlyList<ProcessIsolationViolation>>[] listeners;
        IReadOnlyList<ProcessIsolationViolation> snapshot;

        lock (_gate)
        {
            _totalCount++;

            if (_tail.Count == _capacity)
                _tail.Dequeue();
            _tail.Enqueue(violation);

            snapshot = _tail.ToArray();
            listeners = _listeners.Values.ToArray();
        }

        foreach (var listener in listeners)
        {
            try
            {
                listener(snapshot);
            }
            catch
            {
                // Subscribers are observers; recording should not fail because
                // a listener could not process a notification.
            }
        }
    }

    public IReadOnlyList<ProcessIsolationViolation> Get(int? limit = null)
    {
        lock (_gate)
        {
            if (limit is null)
                return _tail.ToArray();

            if (limit.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be non-negative.");

            if (limit.Value == 0)
                return [];

            return _tail
                .Skip(Math.Max(0, _tail.Count - limit.Value))
                .ToArray();
        }
    }

    public IReadOnlyList<ProcessIsolationViolation> GetSinceTotalCount(int totalCount)
    {
        lock (_gate)
        {
            if (totalCount < 0)
                throw new ArgumentOutOfRangeException(nameof(totalCount), "Total count must be non-negative.");

            var availableStart = _totalCount - _tail.Count;
            var skip = Math.Max(0, totalCount - availableStart);

            return _tail
                .Skip(skip)
                .ToArray();
        }
    }

    public IDisposable Subscribe(Action<IReadOnlyList<ProcessIsolationViolation>> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        long id;
        IReadOnlyList<ProcessIsolationViolation> snapshot;
        lock (_gate)
        {
            id = _nextListenerId++;
            _listeners.Add(id, listener);
            snapshot = _tail.ToArray();
        }

        try
        {
            listener(snapshot);
        }
        catch
        {
            // Subscribers are observers; subscription should still succeed even
            // when the initial snapshot cannot be processed.
        }

        return new Subscription(this, id);
    }

    private void Unsubscribe(long id)
    {
        lock (_gate)
            _listeners.Remove(id);
    }

    private sealed class Subscription(ProcessIsolationViolationStore owner, long id) : IDisposable
    {
        private ProcessIsolationViolationStore? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Unsubscribe(id);
        }
    }
}
