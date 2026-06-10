namespace Rhodium.Tensor;

public readonly ref struct TensorWindow
{
    private readonly ReadOnlySpan<double> _first;
    private readonly ReadOnlySpan<double> _second;

    internal TensorWindow(ReadOnlySpan<double> first, ReadOnlySpan<double> second)
    {
        _first = first;
        _second = second;
    }

    public int Length => _first.Length + _second.Length;

    public bool IsSplit => !_second.IsEmpty;

    public double this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Length);
            return index < _first.Length
                ? _first[index]
                : _second[index - _first.Length];
        }
    }

    public double Sum()
    {
        var sum = 0d;
        foreach (var value in _first)
            sum += value;
        foreach (var value in _second)
            sum += value;
        return sum;
    }

    public double Mean()
    {
        var length = Length;
        return length == 0 ? 0d : Sum() / length;
    }

    public double Max()
    {
        if (Length == 0)
            return 0d;

        var max = double.NegativeInfinity;
        foreach (var value in _first)
            max = Math.Max(max, value);
        foreach (var value in _second)
            max = Math.Max(max, value);
        return max;
    }

    public double Min()
    {
        if (Length == 0)
            return 0d;

        var min = double.PositiveInfinity;
        foreach (var value in _first)
            min = Math.Min(min, value);
        foreach (var value in _second)
            min = Math.Min(min, value);
        return min;
    }

    public double StdDev()
    {
        var length = Length;
        if (length == 0)
            return 0d;

        var mean = Mean();
        var sumSquares = 0d;
        foreach (var value in _first)
        {
            var delta = value - mean;
            sumSquares += delta * delta;
        }

        foreach (var value in _second)
        {
            var delta = value - mean;
            sumSquares += delta * delta;
        }

        return Math.Sqrt(sumSquares / length);
    }

    public void CopyTo(Span<double> destination)
    {
        if (destination.Length < Length)
            throw new ArgumentException("Destination span is too small.", nameof(destination));

        _first.CopyTo(destination);
        _second.CopyTo(destination[_first.Length..]);
    }
}
