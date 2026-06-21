namespace HPD.Events.Core;

/// <summary>
/// Manually advanced clock for deterministic tests, hosts, and replay scenarios.
/// </summary>
public sealed class ManualClock : IClock
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ManualTimerHandle> _timers = [];
    private DateTimeOffset _utcNow;

    /// <summary>
    /// Create a manual clock at the Unix epoch.
    /// </summary>
    public ManualClock()
        : this(DateTimeOffset.UnixEpoch)
    {
    }

    /// <summary>
    /// Create a manual clock at a specific UTC time.
    /// </summary>
    public ManualClock(DateTimeOffset utcNow)
    {
        _utcNow = utcNow.ToUniversalTime();
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
                return _utcNow;
        }
    }

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

    /// <summary>
    /// Set the current clock time.
    /// </summary>
    public void Set(DateTimeOffset utcNow)
    {
        lock (_gate)
            _utcNow = utcNow.ToUniversalTime();

        FireDueTimers();
    }

    /// <summary>
    /// Advance the current clock time by a positive delta.
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delta), delta, "Manual clock cannot move backward with Advance.");

        lock (_gate)
            _utcNow += delta;

        FireDueTimers();
    }

    /// <inheritdoc />
    public ITimerHandle SetAlert(string name, DateTimeOffset alertTime, Action<TimeEvent> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);

        return CreateTimer(name, alertTime.ToUniversalTime(), Timeout.InfiniteTimeSpan, callback, recurring: false, stopTime: null);
    }

    /// <inheritdoc />
    public ITimerHandle SetAlert(string name, TimeSpan delay, Action<TimeEvent> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);

        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        return CreateTimer(name, UtcNow + delay, Timeout.InfiniteTimeSpan, callback, recurring: false, stopTime: null);
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

        var firstFire = startTime?.ToUniversalTime() ?? UtcNow + interval;
        return CreateTimer(name, firstFire, interval, callback, recurring: true, stopTime?.ToUniversalTime());
    }

    /// <inheritdoc />
    public void CancelTimer(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ManualTimerHandle? handle;
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
        ManualTimerHandle[] handles;
        lock (_gate)
        {
            handles = _timers.Values.ToArray();
            _timers.Clear();
        }

        foreach (var handle in handles)
            handle.CancelFromClock();
    }

    private ITimerHandle CreateTimer(
        string name,
        DateTimeOffset firstFire,
        TimeSpan interval,
        Action<TimeEvent> callback,
        bool recurring,
        DateTimeOffset? stopTime)
    {
        ManualTimerHandle? oldHandle = null;
        ManualTimerHandle handle;

        lock (_gate)
        {
            if (_timers.TryGetValue(name, out oldHandle))
                _timers.Remove(name);

            handle = new ManualTimerHandle(this, name, firstFire, interval, callback, recurring, stopTime);
            _timers[name] = handle;
        }

        oldHandle?.CancelFromClock();
        FireDueTimers();
        return handle;
    }

    private void Remove(ManualTimerHandle handle)
    {
        lock (_gate)
        {
            if (_timers.TryGetValue(handle.Name, out var current) && ReferenceEquals(current, handle))
                _timers.Remove(handle.Name);
        }
    }

    private void FireDueTimers()
    {
        while (TryTakeDueTimer(out var handle, out var triggerTime))
        {
            handle.Fire(triggerTime);
            if (handle.TryScheduleNext(triggerTime))
            {
                lock (_gate)
                    _timers[handle.Name] = handle;
            }
        }
    }

    private bool TryTakeDueTimer(out ManualTimerHandle handle, out DateTimeOffset triggerTime)
    {
        lock (_gate)
        {
            var now = _utcNow;
            handle = _timers.Values
                .Where(static timer => timer.IsActive && timer.NextFireTime.HasValue)
                .Where(timer => timer.NextFireTime!.Value <= now)
                .OrderBy(timer => timer.NextFireTime!.Value)
                .ThenBy(timer => timer.Name, StringComparer.Ordinal)
                .FirstOrDefault()!;

            if (handle is null)
            {
                triggerTime = default;
                return false;
            }

            _timers.Remove(handle.Name);
            triggerTime = handle.NextFireTime!.Value;
            return true;
        }
    }

    private sealed class ManualTimerHandle : ITimerHandle
    {
        private readonly ManualClock _owner;
        private readonly TimeSpan _interval;
        private readonly Action<TimeEvent> _callback;
        private readonly bool _recurring;
        private readonly DateTimeOffset? _stopTime;
        private bool _active = true;

        public ManualTimerHandle(
            ManualClock owner,
            string name,
            DateTimeOffset nextFireTime,
            TimeSpan interval,
            Action<TimeEvent> callback,
            bool recurring,
            DateTimeOffset? stopTime)
        {
            _owner = owner;
            Name = name;
            NextFireTime = nextFireTime;
            _interval = interval;
            _callback = callback;
            _recurring = recurring;
            _stopTime = stopTime;
        }

        public string Name { get; }

        public bool IsActive => _active;

        public DateTimeOffset? NextFireTime { get; private set; }

        public void Cancel()
        {
            if (!_active)
                return;

            _active = false;
            NextFireTime = null;
            _owner.Remove(this);
        }

        public void CancelFromClock()
        {
            if (!_active)
                return;

            _active = false;
            NextFireTime = null;
        }

        public void Dispose() => Cancel();

        public void Fire(DateTimeOffset triggerTime)
        {
            if (!_active)
                return;

            if (!_recurring)
            {
                _active = false;
                NextFireTime = null;
            }

            _callback(new TimeEvent
            {
                TimerName = Name,
                TriggerTime = triggerTime,
                Timestamp = triggerTime
            });
        }

        public bool TryScheduleNext(DateTimeOffset previousFireTime)
        {
            if (!_active || !_recurring)
                return false;

            var next = previousFireTime + _interval;
            if (_stopTime.HasValue && next > _stopTime.Value)
            {
                _active = false;
                NextFireTime = null;
                return false;
            }

            NextFireTime = next;
            return true;
        }
    }
}
