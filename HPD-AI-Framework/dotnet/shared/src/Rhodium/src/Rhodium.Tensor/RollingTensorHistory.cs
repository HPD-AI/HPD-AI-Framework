namespace Rhodium.Tensor;

public sealed class RollingTensorHistory
{
    private readonly double[] _values;
    private int _next;

    public RollingTensorHistory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _values = new double[capacity];
    }

    public int Capacity => _values.Length;

    public int Count { get; private set; }

    public void Push(double value)
    {
        _values[_next] = value;
        _next = (_next + 1) % _values.Length;
        if (Count < _values.Length)
            Count++;
    }

    public TensorWindow Window(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        if (length > Count)
            throw new InvalidOperationException("Not enough history is available for the requested window.");

        var start = (_next - length + _values.Length) % _values.Length;
        if (start + length <= _values.Length)
            return new TensorWindow(_values.AsSpan(start, length), ReadOnlySpan<double>.Empty);

        var firstLength = _values.Length - start;
        return new TensorWindow(
            _values.AsSpan(start, firstLength),
            _values.AsSpan(0, length - firstLength));
    }
}
