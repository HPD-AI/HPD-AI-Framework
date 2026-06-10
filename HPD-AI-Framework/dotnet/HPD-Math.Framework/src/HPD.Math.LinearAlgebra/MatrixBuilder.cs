using HPD.Math.Core;

namespace HPD.Math.LinearAlgebra;

/// <summary>
/// Caller-owned builder for dense row-major matrices.
/// </summary>
public ref struct MatrixBuilder<T>
{
    private readonly Span<T> _values;
    private int _rows;
    private int _columns;

    public MatrixBuilder(Span<T> values)
    {
        _values = values;
        _rows = 0;
        _columns = 0;
    }

    public int Rows => _rows;

    public int Columns => _columns;

    public int Capacity => _values.Length;

    public void Clear()
    {
        _rows = 0;
        _columns = 0;
    }

    public AlgebraStatus TrySetShape(int rows, int columns)
    {
        if (rows < 0 || columns < 0)
            return AlgebraStatus.InvalidInput;

        var count = (long)rows * columns;
        if (count > int.MaxValue)
            return AlgebraStatus.InvalidInput;
        if ((int)count > Capacity)
            return AlgebraStatus.InsufficientDestination;

        _rows = rows;
        _columns = columns;
        return AlgebraStatus.Ok;
    }

    public Span<T> WrittenSpan => _values[..((int)((long)_rows * _columns))];

    public MatrixView<T> AsView() => new(_rows, _columns, _values[..((int)((long)_rows * _columns))]);
}
