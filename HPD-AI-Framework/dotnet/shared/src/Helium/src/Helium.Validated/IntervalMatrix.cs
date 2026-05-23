namespace Helium.Validated;

/// <summary>
/// Dense row-major matrix of intervals. This is a validated numeric container,
/// not an exact algebraic matrix.
/// </summary>
public sealed class IntervalMatrix : IEquatable<IntervalMatrix>
{
    private readonly Interval[] _data;

    public int Rows { get; }
    public int Cols { get; }
    public int Length => Rows * Cols;

    public ReadOnlySpan<Interval> Data => _data;

    public IntervalMatrix(int rows, int cols, ReadOnlySpan<Interval> data)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        if (data.Length != rows * cols)
            throw new ArgumentException("Data length must equal rows * cols.", nameof(data));

        Rows = rows;
        Cols = cols;
        _data = data.ToArray();
    }

    public Interval this[int row, int col] => _data[row * Cols + col];

    public ReadOnlySpan<Interval> Row(int row)
    {
        if ((uint)row >= (uint)Rows)
            throw new ArgumentOutOfRangeException(nameof(row));
        return _data.AsSpan(row * Cols, Cols);
    }

    public static IntervalMatrix FromArray(int rows, int cols, params ReadOnlySpan<Interval> data) =>
        new(rows, cols, data);

    public void CopyTo(Span<Interval> destination) => _data.CopyTo(destination);

    public bool Equals(IntervalMatrix? other)
    {
        if (other is null || Rows != other.Rows || Cols != other.Cols)
            return false;
        return Data.SequenceEqual(other.Data);
    }

    public override bool Equals(object? obj) => obj is IntervalMatrix other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Rows);
        hash.Add(Cols);
        foreach (var value in _data)
            hash.Add(value);
        return hash.ToHashCode();
    }
}
