namespace HPD.Events.Core;

/// <summary>
/// Production wall-clock implementation for event hosts.
/// </summary>
public sealed class SystemClock : IClock, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ClockTimerHandle> _timers = [];
    private bool _disposed;

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public long UnixNanos => ReplayTime.ToUnixNanoseconds(UtcNow);

    /// <inheritdoc />
    public IEnumerable<string> TimerNames
    {
        get
        {
            lock (_gate)
                return _timers.Keys.ToArray();
        }
    }

    /// <inheritdoc />
    public ITimerHandle SetAlert(string name, DateTimeOffset alertTime, Action<TimeEvent> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);

        var delay = alertTime - UtcNow;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        return CreateTimer(name, alertTime, delay, Timeout.InfiniteTimeSpan, callback, recurring: false, stopTime: null);
    }

    /// <inheritdoc />
    public ITimerHandle SetAlert(string name, TimeSpan delay, Action<TimeEvent> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);

        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        return CreateTimer(name, UtcNow + delay, delay, Timeout.InfiniteTimeSpan, callback, recurring: false, stopTime: null);
    }

    /// <inheritdoc />
    public ITimerHandle SetTimer(
        string name,
        TimeSpan interval,
        Action<TimeEvent> callback,
        DateTimeOffset? startTime = null,
        DateTimeOffset? stopTime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);

        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Timer interval must be positive.");

        var nextFireTime = startTime ?? UtcNow + interval;
        var delay = nextFireTime - UtcNow;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        return CreateTimer(name, nextFireTime, delay, interval, callback, recurring: true, stopTime);
    }

    /// <inheritdoc />
    public void CancelTimer(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ClockTimerHandle? handle;
        lock (_gate)
        {
            if (!_timers.Remove(name, out handle))
                return;
        }

        handle.CancelFromClock();
    }

    /// <inheritdoc />
    public void CancelAllTimers()
    {
        ClockTimerHandle[] handles;
        lock (_gate)
        {
            handles = _timers.Values.ToArray();
            _timers.Clear();
        }

        foreach (var handle in handles)
            handle.CancelFromClock();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelAllTimers();
    }

    private ITimerHandle CreateTimer(
        string name,
        DateTimeOffset nextFireTime,
        TimeSpan dueTime,
        TimeSpan period,
        Action<TimeEvent> callback,
        bool recurring,
        DateTimeOffset? stopTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ClockTimerHandle? oldHandle = null;
        ClockTimerHandle handle;

        lock (_gate)
        {
            if (_timers.TryGetValue(name, out oldHandle))
                _timers.Remove(name);

            handle = new ClockTimerHandle(this, name, nextFireTime, period, callback, recurring, stopTime);
            _timers[name] = handle;
        }

        oldHandle?.CancelFromClock();
        handle.Start(dueTime);
        return handle;
    }

    private void Remove(ClockTimerHandle handle)
    {
        lock (_gate)
        {
            if (_timers.TryGetValue(handle.Name, out var current) && ReferenceEquals(current, handle))
                _timers.Remove(handle.Name);
        }
    }

    private sealed class ClockTimerHandle : ITimerHandle
    {
        private readonly SystemClock _owner;
        private readonly TimeSpan _period;
        private readonly Action<TimeEvent> _callback;
        private readonly bool _recurring;
        private readonly DateTimeOffset? _stopTime;
        private readonly object _gate = new();
        private Timer? _timer;
        private bool _active = true;

        public ClockTimerHandle(
            SystemClock owner,
            string name,
            DateTimeOffset nextFireTime,
            TimeSpan period,
            Action<TimeEvent> callback,
            bool recurring,
            DateTimeOffset? stopTime)
        {
            _owner = owner;
            Name = name;
            NextFireTime = nextFireTime;
            _period = period;
            _callback = callback;
            _recurring = recurring;
            _stopTime = stopTime;
        }

        public string Name { get; }

        public bool IsActive
        {
            get
            {
                lock (_gate)
                    return _active;
            }
        }

        public DateTimeOffset? NextFireTime { get; private set; }

        public void Start(TimeSpan dueTime) =>
            _timer = new Timer(static state => ((ClockTimerHandle)state!).Fire(), this, dueTime, Timeout.InfiniteTimeSpan);

        public void Cancel()
        {
            if (!TryDeactivate())
                return;

            _owner.Remove(this);
            _timer?.Dispose();
            NextFireTime = null;
        }

        public void CancelFromClock()
        {
            if (!TryDeactivate())
                return;

            _timer?.Dispose();
            NextFireTime = null;
        }

        public void Dispose() => Cancel();

        private void Fire()
        {
            if (!IsActive)
                return;

            var triggerTime = _owner.UtcNow;
            if (!_recurring)
            {
                if (!TryDeactivate())
                    return;

                _owner.Remove(this);
                _timer?.Dispose();
                NextFireTime = null;
            }

            TryInvoke(triggerTime);

            if (!_recurring)
                return;

            var next = triggerTime + _period;
            if (_stopTime.HasValue && next > _stopTime.Value)
            {
                Cancel();
                return;
            }

            lock (_gate)
            {
                if (!_active)
                    return;

                NextFireTime = next;
                _timer?.Change(_period, Timeout.InfiniteTimeSpan);
            }
        }

        private void TryInvoke(DateTimeOffset triggerTime)
        {
            try
            {
                _callback(new TimeEvent
                {
                    TimerName = Name,
                    TriggerTime = triggerTime,
                    Timestamp = triggerTime
                });
            }
            catch
            {
                // Timer callbacks run on the ThreadPool; user code should not tear down the process.
            }
        }

        private bool TryDeactivate()
        {
            lock (_gate)
            {
                if (!_active)
                    return false;

                _active = false;
                return true;
            }
        }
    }
}
