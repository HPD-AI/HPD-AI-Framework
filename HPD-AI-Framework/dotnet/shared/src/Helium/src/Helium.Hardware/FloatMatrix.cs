namespace Helium.Hardware;

public sealed class FloatMatrix : IEquatable<FloatMatrix>, IDisposable
{
    private readonly HardwareBuffer<float> _buffer;
    private bool _disposed;

    public int Rows { get; }
    public int Cols { get; }
    public int Length => Rows * Cols;

    public ReadOnlySpan<float> Data => _buffer.AsSpan().ReadOnly;

    public HardwareBuffer<float> Buffer => _buffer;

    public FloatMatrix(int rows, int cols)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));

        Rows = rows;
        Cols = cols;
        _buffer = new HardwareBuffer<float>(checked(rows * cols));
    }

    public FloatMatrix(int rows, int cols, ReadOnlySpan<float> data)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        if (data.Length != checked(rows * cols))
            throw new ArgumentException("Data length must equal rows * cols.", nameof(data));

        Rows = rows;
        Cols = cols;
        _buffer = new HardwareBuffer<float>(data);
    }

    public float this[int row, int col] => Data[row * Cols + col];

    public PackedSpan<float> RowSpan(int row)
    {
        ThrowIfDisposed();
        if ((uint)row >= (uint)Rows)
            throw new ArgumentOutOfRangeException(nameof(row));
        return _buffer.AsSpan(row * Cols, Cols);
    }

    public static FloatMatrix FromArray(int rows, int cols, params ReadOnlySpan<float> data) =>
        new(rows, cols, data);

    public void CopyTo(Span<float> destination) => _buffer.CopyTo(destination);

    public bool Equals(FloatMatrix? other)
    {
        if (other is null || Rows != other.Rows || Cols != other.Cols)
            return false;
        return Data.SequenceEqual(other.Data);
    }

    public override bool Equals(object? obj) => obj is FloatMatrix other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Rows);
        hash.Add(Cols);
        foreach (var value in Data)
            hash.Add(value);
        return hash.ToHashCode();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _buffer.Dispose();
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FloatMatrix));
    }
}
