namespace HPD.Events.Signals;

/// <summary>
/// Bounded local queue plus wake signal for event-loop work.
/// </summary>
public sealed class EventLoopMailbox<T> : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly T[] _buffer;
    private readonly EventSignal _signal;
    private readonly EventLoopMailboxOverflowMode _overflowMode;
    private int _head;
    private int _tail;
    private int _count;
    private long _acceptedWrites;
    private long _rejectedWrites;
    private long _droppedWrites;
    private long _reads;
    private long _signals;
    private bool _disposed;

    /// <summary>Create a bounded mailbox with default options.</summary>
    public EventLoopMailbox()
        : this(null)
    {
    }

    /// <summary>Create a bounded mailbox with the supplied options.</summary>
    public EventLoopMailbox(EventLoopMailboxOptions? options)
    {
        options ??= new EventLoopMailboxOptions();
        if (options.Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Mailbox capacity must be greater than zero.");

        _buffer = new T[options.Capacity];
        _overflowMode = options.OverflowMode;
        _signal = new EventSignal();
        Signal = _signal;
    }

    /// <summary>Wake signal raised when a write is accepted.</summary>
    public IEventSignal Signal { get; }

    /// <summary>Try to enqueue one item.</summary>
    public bool TryWrite(T item)
    {
        var accepted = false;

        lock (_gate)
        {
            if (_disposed)
            {
                _rejectedWrites++;
                return false;
            }

            if (_count < _buffer.Length)
            {
                WriteTail(item);
                _acceptedWrites++;
                accepted = true;
            }
            else
            {
                switch (_overflowMode)
                {
                    case EventLoopMailboxOverflowMode.DropOldest:
                        _buffer[_head] = default!;
                        _head = Next(_head);
                        _count--;
                        _droppedWrites++;
                        WriteTail(item);
                        _acceptedWrites++;
                        accepted = true;
                        break;

                    case EventLoopMailboxOverflowMode.DropNewest:
                        _droppedWrites++;
                        break;

                    case EventLoopMailboxOverflowMode.Reject:
                    case EventLoopMailboxOverflowMode.Backpressure:
                    default:
                        _rejectedWrites++;
                        break;
                }
            }
        }

        if (accepted)
        {
            Interlocked.Increment(ref _signals);
            _signal.Signal();
        }

        return accepted;
    }

    /// <summary>Try to read one queued item.</summary>
    public bool TryRead(out T item)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                item = default!;
                return false;
            }

            item = _buffer[_head];
            _buffer[_head] = default!;
            _head = Next(_head);
            _count--;
            _reads++;
            return true;
        }
    }

    /// <summary>Read up to <paramref name="destination" />.Length queued items.</summary>
    public int TryReadBatch(Span<T> destination)
    {
        if (destination.Length == 0)
            return 0;

        lock (_gate)
        {
            var read = Math.Min(destination.Length, _count);
            for (var i = 0; i < read; i++)
            {
                destination[i] = _buffer[_head];
                _buffer[_head] = default!;
                _head = Next(_head);
            }

            _count -= read;
            _reads += read;
            return read;
        }
    }

    /// <summary>
    /// Wait until the mailbox may contain readable items.
    /// </summary>
    /// <remarks>
    /// This consumes pending wake state from the underlying signal. Callers should still drain the
    /// mailbox after this method returns because multiple writes may be represented by one wake.
    /// </remarks>
    public async ValueTask WaitToReadAsync(CancellationToken cancellationToken = default)
    {
        await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        while (_signal.TryConsume())
        {
        }
    }

    /// <summary>Drain up to <paramref name="destination" />.Length queued items.</summary>
    public int Drain(Span<T> destination) => TryReadBatch(destination);

    /// <summary>Get current mailbox statistics.</summary>
    public EventLoopMailboxStats GetStats()
    {
        lock (_gate)
        {
            return new EventLoopMailboxStats(
                _buffer.Length,
                _count,
                _acceptedWrites,
                _reads,
                _droppedWrites,
                _rejectedWrites,
                Volatile.Read(ref _signals),
                _disposed);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            Array.Clear(_buffer);
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        _signal.Signal();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void WriteTail(T item)
    {
        _buffer[_tail] = item;
        _tail = Next(_tail);
        _count++;
    }

    private int Next(int index)
    {
        var next = index + 1;
        return next == _buffer.Length ? 0 : next;
    }
}
