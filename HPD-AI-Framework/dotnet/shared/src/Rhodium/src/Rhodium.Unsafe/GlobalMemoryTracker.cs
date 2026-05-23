namespace Rhodium.Unsafe;

public sealed class GlobalMemoryTracker
{
    private long _bytesAllocated;
    private long _bytesFreed;
    private long _peakBytes;
    private int _rentCount;
    private int _returnCount;

    public long BytesAllocated => Volatile.Read(ref _bytesAllocated);
    public long BytesFreed => Volatile.Read(ref _bytesFreed);
    public long BytesInUse => BytesAllocated - BytesFreed;
    public long PeakBytes => Volatile.Read(ref _peakBytes);

    public void TrackAllocation(long bytes)
    {
        var newTotal = Interlocked.Add(ref _bytesAllocated, bytes);
        Interlocked.Increment(ref _rentCount);

        while (true)
        {
            var currentPeak = Volatile.Read(ref _peakBytes);
            var inUse = newTotal - Volatile.Read(ref _bytesFreed);
            if (inUse <= currentPeak) return;
            if (Interlocked.CompareExchange(ref _peakBytes, inUse, currentPeak) == currentPeak)
                return;
        }
    }

    public void TrackDeallocation(long bytes)
    {
        Interlocked.Add(ref _bytesFreed, bytes);
        Interlocked.Increment(ref _returnCount);
    }

    public void TrackPoolReturn()
    {
        var returns = Interlocked.Increment(ref _returnCount);
        if (returns > Volatile.Read(ref _rentCount))
            throw new InvalidOperationException($"Pool double-return detected: {returns} returns for {_rentCount} rents.");
    }

    public void AssertNoLeaks()
    {
        var inUse = BytesInUse;
        if (inUse != 0)
            throw new MemoryLeakException(
                $"Unmanaged memory leak: {inUse:N0} bytes outstanding. " +
                $"Allocated: {BytesAllocated:N0}, Freed: {BytesFreed:N0}");
    }

    public void AssertBalancedPool()
    {
        if (Volatile.Read(ref _returnCount) > Volatile.Read(ref _rentCount))
            throw new InvalidOperationException(
                $"Pool double-return detected: {_returnCount} returns for {_rentCount} rents.");
    }

    public MemoryReport GetReport() => new(BytesAllocated, BytesFreed, BytesInUse, PeakBytes);
}
