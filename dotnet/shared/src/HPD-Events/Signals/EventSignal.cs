namespace HPD.Events.Signals;

/// <summary>
/// Default local wake primitive for event loops and schedulers.
/// </summary>
public sealed class EventSignal : IEventSignal, IEventSignalSource
{
    private readonly object _gate = new();
    private readonly EventSignalMode _mode;
    private List<Waiter>? _waiters;
    private int _pending;

    /// <summary>Create a signal with coalescing wake behavior.</summary>
    public EventSignal()
        : this(null)
    {
    }

    /// <summary>Create a signal with the supplied options.</summary>
    public EventSignal(EventSignalOptions? options)
    {
        _mode = (options ?? new EventSignalOptions()).Mode;
    }

    /// <inheritdoc />
    public ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        lock (_gate)
        {
            if (_pending > 0)
                return ValueTask.CompletedTask;

            var waiter = new Waiter(this);
            _waiters ??= [];
            _waiters.Add(waiter);

            if (cancellationToken.CanBeCanceled)
                waiter.RegisterCancellation(cancellationToken);

            return new ValueTask(waiter.Task);
        }
    }

    /// <inheritdoc />
    public bool TryConsume()
    {
        lock (_gate)
        {
            if (_pending <= 0)
                return false;

            _pending--;
            return true;
        }
    }

    /// <inheritdoc />
    public void Signal()
    {
        Waiter[]? waiters = null;

        lock (_gate)
        {
            if (_mode == EventSignalMode.Coalescing)
            {
                _pending = 1;
            }
            else if (_pending < int.MaxValue)
            {
                _pending++;
            }

            if (_waiters is { Count: > 0 } current)
            {
                waiters = current.ToArray();
                current.Clear();
            }
        }

        if (waiters is null)
            return;

        for (var i = 0; i < waiters.Length; i++)
            waiters[i].Signal();
    }

    private void Cancel(Waiter waiter, CancellationToken cancellationToken)
    {
        var removed = false;

        lock (_gate)
        {
            if (_waiters is not null)
                removed = _waiters.Remove(waiter);
        }

        if (removed)
            waiter.Cancel(cancellationToken);
    }

    private sealed class Waiter
    {
        private readonly EventSignal _owner;
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _registration;

        public Waiter(EventSignal owner) => _owner = owner;

        public Task Task => _completion.Task;

        public void RegisterCancellation(CancellationToken cancellationToken)
        {
            _registration = cancellationToken.Register(
                static state =>
                {
                    var tuple = ((Waiter Waiter, CancellationToken Token))state!;
                    tuple.Waiter._owner.Cancel(tuple.Waiter, tuple.Token);
                },
                (this, cancellationToken));
        }

        public void Signal()
        {
            _registration.Dispose();
            _completion.TrySetResult();
        }

        public void Cancel(CancellationToken cancellationToken)
        {
            _registration.Dispose();
            _completion.TrySetCanceled(cancellationToken);
        }
    }
}
