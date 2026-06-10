namespace Rhodium.Platform;

public class StrategyExecutionInvariantException : InvalidOperationException
{
    public StrategyExecutionInvariantException(string message)
        : base(message)
    {
    }
}

public sealed class HotPathAllocationException : StrategyExecutionInvariantException
{
    public HotPathAllocationException(long bytes)
        : base(
            $"Hot path allocation detected: {bytes} bytes allocated in OnTick(). " +
            "Strategy must be allocation-free on the hot path.")
    {
        Bytes = bytes;
    }

    public long Bytes { get; }
}

public sealed class UniverseTopologyChangedException : StrategyExecutionInvariantException
{
    public UniverseTopologyChangedException(int expectedVersion, int actualVersion)
        : base(
            $"Universe version mismatch. Expected {expectedVersion}, got {actualVersion}. " +
            "Strategy must be reinitialized when universe topology changes.")
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public int ExpectedVersion { get; }
    public int ActualVersion { get; }
}
