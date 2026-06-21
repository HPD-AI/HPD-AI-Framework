#nullable enable

using HPD.Buffers;

namespace HPD.Buffers.Tests.Ownership;

public sealed class ArrayPoolByteBufferPoolTests
{
    [Fact]
    public void Rent_ReturnsAtLeastRequestedLength()
    {
        var pool = new ArrayPoolByteBufferPool();

        using IByteBufferLease lease = pool.Rent(1024);

        Assert.True(lease.Memory.Length >= 1024);
    }

    [Fact]
    public void Rent_AllowsZeroLengthRequest()
    {
        var pool = new ArrayPoolByteBufferPool();

        using IByteBufferLease lease = pool.Rent(0);

        Assert.True(lease.Memory.Length >= 0);
    }

    [Fact]
    public void Rent_RejectsNegativeLength()
    {
        var pool = new ArrayPoolByteBufferPool();

        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Rent(-1));
    }

    [Fact]
    public void Lease_DisposeIsIdempotent()
    {
        var pool = new ArrayPoolByteBufferPool();
        IByteBufferLease lease = pool.Rent(16);

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(0, lease.Memory.Length);
    }

    [Fact]
    public void Lease_MemoryCanCarryRetainedPayloadUntilDisposed()
    {
        var pool = new ArrayPoolByteBufferPool();
        using IByteBufferLease lease = pool.Rent(4);

        ReadOnlySpan<byte> payload = [1, 2, 3, 4];
        payload.CopyTo(lease.Memory.Span[..4]);

        Assert.Equal([1, 2, 3, 4], lease.Memory[..4].ToArray());
    }

    [Fact]
    public void Lease_DisposeReturnsBufferWithConfiguredClearPolicy()
    {
        var backingPool = new RecordingArrayPool();
        var pool = new ArrayPoolByteBufferPool(backingPool, clearOnReturn: true);

        using (IByteBufferLease lease = pool.Rent(8))
        {
            lease.Memory.Span[0] = 0x5A;
        }

        Assert.Equal(8, backingPool.LastMinimumLength);
        Assert.Same(backingPool.Buffer, backingPool.LastReturnedArray);
        Assert.True(backingPool.LastClearArray);
    }

    private sealed class RecordingArrayPool : System.Buffers.ArrayPool<byte>
    {
        public byte[] Buffer { get; } = new byte[16];

        public int LastMinimumLength { get; private set; }

        public byte[]? LastReturnedArray { get; private set; }

        public bool LastClearArray { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            LastMinimumLength = minimumLength;
            return Buffer;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            LastReturnedArray = array;
            LastClearArray = clearArray;
        }
    }
}
