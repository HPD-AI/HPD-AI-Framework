namespace HPD.Events.Struct;

internal enum StructEventWriteStatus
{
    Accepted,
    Dropped,
    Backpressured,
    Rejected,
    Disposed
}

internal readonly record struct StructEventWriteResult(
    StructEventWriteStatus Status,
    int DroppedCount,
    int DepthDelta);

internal sealed class StructEventRingBuffer<TEvent>
    where TEvent : struct, IStructEvent
{
    private readonly TEvent[] _buffer;
    private readonly object _gate = new();
    private int _head;
    private int _tail;
    private int _count;
    private bool _disposed;

    public StructEventRingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Struct event buffer capacity must be greater than zero.");

        _buffer = new TEvent[capacity];
    }

    public int Count => Volatile.Read(ref _count);

    public StructEventWriteResult TryWrite(in TEvent evt, StructEventOverflowMode overflowMode)
    {
        lock (_gate)
        {
            if (_disposed)
                return new StructEventWriteResult(StructEventWriteStatus.Disposed, 0, 0);

            if (_count < _buffer.Length)
            {
                WriteTail(evt);
                return new StructEventWriteResult(StructEventWriteStatus.Accepted, 0, 1);
            }

            switch (overflowMode)
            {
                case StructEventOverflowMode.DropOldest:
                    _head = Next(_head);
                    _count--;
                    WriteTail(evt);
                    return new StructEventWriteResult(StructEventWriteStatus.Accepted, 1, 0);

                case StructEventOverflowMode.DropNewest:
                    return new StructEventWriteResult(StructEventWriteStatus.Dropped, 1, 0);

                case StructEventOverflowMode.Reject:
                    return new StructEventWriteResult(StructEventWriteStatus.Rejected, 0, 0);

                case StructEventOverflowMode.Backpressure:
                default:
                    return new StructEventWriteResult(StructEventWriteStatus.Backpressured, 0, 0);
            }
        }
    }

    public bool TryRead(out TEvent evt)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                evt = default;
                return false;
            }

            evt = _buffer[_head];
            _buffer[_head] = default;
            _head = Next(_head);
            _count--;
            return true;
        }
    }

    public int TryReadBatch(Span<TEvent> destination)
    {
        if (destination.Length == 0)
            return 0;

        lock (_gate)
        {
            var read = Math.Min(destination.Length, _count);
            for (var i = 0; i < read; i++)
            {
                destination[i] = _buffer[_head];
                _buffer[_head] = default;
                _head = Next(_head);
            }

            _count -= read;
            return read;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Array.Clear(_buffer);
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }

    private void WriteTail(in TEvent evt)
    {
        _buffer[_tail] = evt;
        _tail = Next(_tail);
        _count++;
    }

    private int Next(int index)
    {
        var next = index + 1;
        return next == _buffer.Length ? 0 : next;
    }
}
