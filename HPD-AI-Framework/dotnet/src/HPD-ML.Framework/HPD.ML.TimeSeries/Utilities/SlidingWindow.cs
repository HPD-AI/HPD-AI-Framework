namespace HPD.ML.TimeSeries;

/// <summary>
/// Fixed-size circular buffer for sliding window operations.
/// Mutable for performance; use Clone() for checkpointing.
/// </summary>
public sealed class SlidingWindow<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    public int Capacity { get; }
    public int Count => _count;
    public bool IsFull => _count == Capacity;

    public SlidingWindow(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        _buffer = new T[capacity];
    }

    public void Push(T value)
    {
        _buffer[_head] = value;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    /// <summary>Access in insertion order. index=0 is oldest.</summary>
    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
            int actual = (_head - _count + index + Capacity) % Capacity;
            return _buffer[actual];
        }
    }

    public void CopyTo(Span<T> destination)
    {
        for (int i = 0; i < _count; i++)
            destination[i] = this[i];
    }

    public SlidingWindow<T> Clone()
    {
        var copy = new SlidingWindow<T>(Capacity);
        Array.Copy(_buffer, copy._buffer, Capacity);
        copy._head = _head;
        copy._count = _count;
        return copy;
    }
}
