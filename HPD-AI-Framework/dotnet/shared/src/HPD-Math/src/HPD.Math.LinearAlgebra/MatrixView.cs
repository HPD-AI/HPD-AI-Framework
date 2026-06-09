using HPD.Math.Core;

namespace HPD.Math.LinearAlgebra;

/// <summary>
/// Non-owning dense row-major matrix view.
/// </summary>
public readonly ref struct MatrixView<T>
{
    public MatrixView(int rows, int columns, ReadOnlySpan<T> values)
    {
        Rows = rows;
        Columns = columns;
        Values = values;
    }

    public int Rows { get; }

    public int Columns { get; }

    public ReadOnlySpan<T> Values { get; }

    public int Count => Values.Length;

    public T this[int row, int column] => Values[(row * Columns) + column];

    public ReadOnlySpan<T> Row(int row) => Values.Slice(row * Columns, Columns);

    public AlgebraStatus ValidateShape()
    {
        if (Rows < 0 || Columns < 0)
            return AlgebraStatus.InvalidInput;

        var count = (long)Rows * Columns;
        if (count > int.MaxValue)
            return AlgebraStatus.InvalidInput;

        return Values.Length == (int)count
            ? AlgebraStatus.Ok
            : AlgebraStatus.InvalidInput;
    }
}
