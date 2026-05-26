namespace HPD.Events.Core;

internal enum LocalStructWriteStatus
{
    Accepted,
    Dropped,
    Backpressured,
    Rejected,
    Disposed
}

internal readonly record struct LocalStructWriteResult(
    LocalStructWriteStatus Status,
    int DroppedCount,
    int DepthDelta);

internal sealed class LocalStructRingBuffer<TEvent>
    where TEvent : struct, IStructEvent
{
    private readonly TEvent[] _buffer;
    private readonly object _gate = new();
    private int _head;
    private int _tail;
    private int _count;
    private bool _disposed;

    public LocalStructRingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Local struct buffer capacity must be greater than zero.");

        _buffer = new TEvent[capacity];
    }

    public int Count => Volatile.Read(ref _count);

    public LocalStructWriteResult TryWrite(in TEvent evt, LocalStructFullMode fullMode)
    {
        lock (_gate)
        {
            if (_disposed)
                return new LocalStructWriteResult(LocalStructWriteStatus.Disposed, 0, 0);

            if (_count < _buffer.Length)
            {
                WriteTail(evt);
                return new LocalStructWriteResult(LocalStructWriteStatus.Accepted, 0, 1);
            }

            switch (fullMode)
            {
                case LocalStructFullMode.DropOldest:
                    _head = Next(_head);
                    _count--;
                    WriteTail(evt);
                    return new LocalStructWriteResult(LocalStructWriteStatus.Accepted, 1, 0);

                case LocalStructFullMode.DropNewest:
                    return new LocalStructWriteResult(LocalStructWriteStatus.Dropped, 1, 0);

                case LocalStructFullMode.Reject:
                    return new LocalStructWriteResult(LocalStructWriteStatus.Rejected, 0, 0);

                case LocalStructFullMode.Backpressure:
                default:
                    return new LocalStructWriteResult(LocalStructWriteStatus.Backpressured, 0, 0);
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
