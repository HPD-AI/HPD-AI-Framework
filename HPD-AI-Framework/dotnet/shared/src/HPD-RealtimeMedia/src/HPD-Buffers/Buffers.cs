#nullable enable

using System.Buffers;

namespace HPD.Buffers;

/// <summary>
/// Owns a leased byte buffer used to transfer retained payloads without steady-state allocation.
/// </summary>
public interface IByteBufferLease : IDisposable
{
    /// <summary>Gets the leased memory.</summary>
    Memory<byte> Memory { get; }
}

/// <summary>
/// Rents byte buffers for retained async, queued, fanout, and reorder boundaries.
/// </summary>
public interface IByteBufferPool
{
    /// <summary>Rents a buffer with at least <paramref name="minimumLength"/> bytes.</summary>
    IByteBufferLease Rent(int minimumLength);
}

/// <summary>
/// Rents byte buffers from <see cref="ArrayPool{T}"/> for retained media payload ownership.
/// </summary>
public sealed class ArrayPoolByteBufferPool : IByteBufferPool
{
    private readonly ArrayPool<byte> pool;
    private readonly bool clearOnReturn;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrayPoolByteBufferPool"/> class.
    /// </summary>
    public ArrayPoolByteBufferPool(ArrayPool<byte>? pool = null, bool clearOnReturn = false)
    {
        this.pool = pool ?? ArrayPool<byte>.Shared;
        this.clearOnReturn = clearOnReturn;
    }

    /// <inheritdoc />
    public IByteBufferLease Rent(int minimumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumLength);
        return new ArrayPoolByteBufferLease(pool, pool.Rent(minimumLength), clearOnReturn);
    }
}

/// <summary>
/// Owns one byte array rented from an <see cref="ArrayPool{T}"/>.
/// </summary>
public sealed class ArrayPoolByteBufferLease : IByteBufferLease
{
    private ArrayPool<byte>? pool;
    private byte[]? buffer;
    private readonly bool clearOnReturn;

    internal ArrayPoolByteBufferLease(ArrayPool<byte> pool, byte[] buffer, bool clearOnReturn)
    {
        this.pool = pool;
        this.buffer = buffer;
        this.clearOnReturn = clearOnReturn;
    }

    /// <inheritdoc />
    public Memory<byte> Memory => buffer ?? Memory<byte>.Empty;

    /// <inheritdoc />
    public void Dispose()
    {
        byte[]? returnedBuffer = Interlocked.Exchange(ref buffer, null);
        ArrayPool<byte>? returnedPool = Interlocked.Exchange(ref pool, null);
        if (returnedBuffer is null || returnedPool is null)
        {
            return;
        }

        returnedPool.Return(returnedBuffer, clearOnReturn);
    }
}
