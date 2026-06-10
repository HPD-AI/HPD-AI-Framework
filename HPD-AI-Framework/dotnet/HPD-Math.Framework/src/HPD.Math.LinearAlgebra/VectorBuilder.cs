using HPD.Math.Core;

namespace HPD.Math.LinearAlgebra;

/// <summary>
/// Caller-owned builder for dense vectors.
/// </summary>
public ref struct VectorBuilder<T>
{
    private readonly Span<T> _values;
    private int _length;

    public VectorBuilder(Span<T> values)
    {
        _values = values;
        _length = 0;
    }

    public int Length => _length;

    public int Capacity => _values.Length;

    public void Clear() => _length = 0;

    public AlgebraStatus TryAppend(in T value)
    {
        if (_length >= Capacity)
            return AlgebraStatus.InsufficientDestination;

        _values[_length] = value;
        _length++;
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TrySetLength(int length)
    {
        if (length < 0)
            return AlgebraStatus.InvalidInput;
        if (length > Capacity)
            return AlgebraStatus.InsufficientDestination;

        _length = length;
        return AlgebraStatus.Ok;
    }

    public Span<T> WrittenSpan => _values[.._length];

    public VectorView<T> AsView() => new(_values[.._length]);
}
