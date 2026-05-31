namespace HPD.Events.Struct;

internal class StructEventSubscriber<TEvent> : IDisposable
    where TEvent : struct, IStructEvent
{
    private readonly Action<int>? _onRead;
    private readonly StructEventRingBuffer<TEvent> _buffer;
    private int _disposed;

    public StructEventSubscriber(
        Guid id,
        int capacity,
        StructEventOverflowMode overflowMode,
        bool isInbox,
        Action<int>? onRead = null)
    {
        Id = id;
        OverflowMode = overflowMode;
        IsInbox = isInbox;
        _onRead = onRead;
        _buffer = new StructEventRingBuffer<TEvent>(capacity);
    }

    public Guid Id { get; }

    public StructEventOverflowMode OverflowMode { get; }

    public bool IsInbox { get; }

    public int Count => _buffer.Count;

    public StructEventWriteResult TryWrite(in TEvent evt) =>
        Volatile.Read(ref _disposed) == 0
            ? _buffer.TryWrite(in evt, OverflowMode)
            : new StructEventWriteResult(StructEventWriteStatus.Disposed, 0, 0);

    public bool TryRead(out TEvent evt)
    {
        if (_buffer.TryRead(out evt))
        {
            _onRead?.Invoke(1);
            return true;
        }

        return false;
    }

    public int TryReadBatch(Span<TEvent> destination)
    {
        var count = _buffer.TryReadBatch(destination);
        if (count > 0)
            _onRead?.Invoke(count);

        return count;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _buffer.Dispose();
    }
}
