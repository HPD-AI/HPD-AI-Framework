namespace HPD.Events.Core;

internal class LocalStructSubscriber<TEvent> : IDisposable
    where TEvent : struct, IStructEvent
{
    private readonly Action<int>? _onRead;
    private readonly LocalStructRingBuffer<TEvent> _buffer;
    private int _disposed;

    public LocalStructSubscriber(
        Guid id,
        int capacity,
        LocalStructFullMode fullMode,
        bool isInbox,
        bool isObserver,
        Action<int>? onRead = null)
    {
        Id = id;
        FullMode = fullMode;
        IsInbox = isInbox;
        IsObserver = isObserver;
        _onRead = onRead;
        _buffer = new LocalStructRingBuffer<TEvent>(capacity);
    }

    public Guid Id { get; }

    public LocalStructFullMode FullMode { get; }

    public bool IsInbox { get; }

    public bool IsObserver { get; }

    public int Count => _buffer.Count;

    public LocalStructWriteResult TryWrite(in TEvent evt) =>
        Volatile.Read(ref _disposed) == 0
            ? _buffer.TryWrite(in evt, FullMode)
            : new LocalStructWriteResult(LocalStructWriteStatus.Disposed, 0, 0);

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
