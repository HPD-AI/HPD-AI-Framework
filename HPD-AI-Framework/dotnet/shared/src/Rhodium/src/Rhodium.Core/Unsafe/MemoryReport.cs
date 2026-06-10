namespace Rhodium.Unsafe;

public readonly record struct MemoryReport(
    long BytesAllocated,
    long BytesFreed,
    long BytesInUse,
    long PeakBytes);
