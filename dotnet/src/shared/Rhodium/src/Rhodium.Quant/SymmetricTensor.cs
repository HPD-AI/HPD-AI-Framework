using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Rhodium.Quant;

/// <summary>
/// Unmanaged aligned symmetric matrix (packed storage).
/// Rented from a bounded pool for background quant computations.
/// </summary>
/// <remarks>
/// Packed symmetric storage: only lower/upper triangle stored.
/// For dimension N, stores N*(N+1)/2 elements.
/// </remarks>
public sealed unsafe class SymmetricTensor : IDisposable
{
    private readonly double[] _data;
    private bool _disposed;

    /// <summary>
    /// Matrix dimension (N x N).
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    /// Total number of stored elements (N*(N+1)/2).
    /// </summary>
    public int PackedLength { get; }

    /// <summary>
    /// Create a symmetric tensor with the given dimension.
    /// Data is rented from ArrayPool.
    /// </summary>
    public SymmetricTensor(int dimension)
    {
        if (dimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimension), "Dimension must be positive");

        Dimension = dimension;
        PackedLength = dimension * (dimension + 1) / 2;
        _data = ArrayPool<double>.Shared.Rent(PackedLength);
        Array.Clear(_data, 0, PackedLength);
    }

    /// <summary>
    /// Get/set element at (row, col).
    /// Symmetry is maintained: T[i,j] == T[j,i].
    /// </summary>
    public double this[int row, int col]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _data[GetPackedIndex(row, col)];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _data[GetPackedIndex(row, col)] = value;
        }
    }

    /// <summary>
    /// Get direct access to packed storage (lower triangle).
    /// </summary>
    public Span<double> PackedData
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _data.AsSpan(0, PackedLength);
        }
    }

    /// <summary>
    /// Convert (row, col) to packed storage index.
    /// Uses lower-triangle packing: (i >= j) → index = i*(i+1)/2 + j
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetPackedIndex(int row, int col)
    {
        if (row < 0 || row >= Dimension)
            throw new ArgumentOutOfRangeException(nameof(row));
        if (col < 0 || col >= Dimension)
            throw new ArgumentOutOfRangeException(nameof(col));

        // Ensure lower triangle (swap if needed)
        if (col > row)
            (row, col) = (col, row);

        return row * (row + 1) / 2 + col;
    }

    /// <summary>
    /// Clear all elements to zero.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Array.Clear(_data, 0, PackedLength);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            ArrayPool<double>.Shared.Return(_data);
            _disposed = true;
        }
    }
}
