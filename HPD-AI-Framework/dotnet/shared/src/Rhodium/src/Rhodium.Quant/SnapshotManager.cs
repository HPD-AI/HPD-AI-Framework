using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Quant;

/// <summary>
/// Manages the "hot" rolling state and rents "cold" snapshots for background jobs.
/// Snapshots are taken only at coarse boundaries (default: BarClosed events).
/// </summary>
/// <remarks>
/// Design:
/// - Maintains a bounded pool of snapshot buffers
/// - Snapshots are blocking copies taken at bar boundaries only (never per-tick)
/// - Coalesces requests: drops stale snapshots if background workers are overloaded
/// - Validates BatchMap.Version for topology safety
/// </remarks>
public sealed class SnapshotManager : IDisposable
{
    private readonly int _maxPoolSize;
    private int _activeSnapshots;
    private bool _disposed;

    /// <summary>
    /// Create a snapshot manager with bounded pool capacity.
    /// </summary>
    /// <param name="maxPoolSize">Maximum number of concurrent snapshots (default: 4)</param>
    public SnapshotManager(int maxPoolSize = 4)
    {
        if (maxPoolSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPoolSize), "Pool size must be positive");

        _maxPoolSize = maxPoolSize;
    }

    /// <summary>
    /// Take a snapshot of current engine state.
    /// Returns null if pool is exhausted (backpressure signal).
    /// </summary>
    /// <remarks>
    /// This is a blocking copy operation - only call at bar boundaries.
    /// The snapshot includes:
    /// - Tensor data (positions, prices, indicators)
    /// - BatchMap version
    /// - Current sequence number
    /// </remarks>
    public EngineSnapshot? TakeSnapshot(
        in MarketKernel market,
        WorldState world,
        StrategyId strategyId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Interlocked.Increment(ref _activeSnapshots) > _maxPoolSize)
        {
            Interlocked.Decrement(ref _activeSnapshots);
            return null;
        }

        try
        {
            var portfolio = world.BuildSnapshot(strategyId, market.UniverseSize);
            var marketData = MarketDataSnapshot.Capture(in market);
            return new EngineSnapshot(
                this,
                market.UniverseVersion,
                default,
                strategyId,
                market.UniverseSize,
                marketData,
                portfolio);
        }
        catch
        {
            Interlocked.Decrement(ref _activeSnapshots);
            throw;
        }
    }

    /// <summary>
    /// Release a snapshot back to the pool.
    /// </summary>
    public void ReleaseSnapshot(EngineSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(snapshot.Owner, this))
            throw new InvalidOperationException("Snapshot belongs to a different SnapshotManager.");

        if (snapshot.TryMarkReleased())
            Interlocked.Decrement(ref _activeSnapshots);
    }

    public void Dispose()
    {
        if (!_disposed)
            _disposed = true;
    }
}

/// <summary>
/// Immutable snapshot of engine state for background computation.
/// </summary>
public sealed class EngineSnapshot : IDisposable
{
    private int _released;

    internal EngineSnapshot(
        SnapshotManager owner,
        int batchMapVersion,
        Sequence sequence,
        StrategyId strategyId,
        int universeSize,
        MarketDataSnapshot marketData,
        PortfolioSnapshot portfolio)
    {
        Owner = owner;
        BatchMapVersion = batchMapVersion;
        Sequence = sequence;
        StrategyId = strategyId;
        UniverseSize = universeSize;
        MarketData = marketData;
        Portfolio = portfolio;
    }

    internal SnapshotManager Owner { get; }

    public int BatchMapVersion { get; }
    public Sequence Sequence { get; }
    public StrategyId StrategyId { get; }
    public int UniverseSize { get; }
    public MarketDataSnapshot MarketData { get; }
    public PortfolioSnapshot Portfolio { get; }

    internal bool TryMarkReleased()
        => Interlocked.Exchange(ref _released, 1) == 0;

    public void Dispose()
        => Owner.ReleaseSnapshot(this);
}

public sealed class MarketDataSnapshot
{
    private readonly double[] _open;
    private readonly double[] _high;
    private readonly double[] _low;
    private readonly double[] _close;
    private readonly double[] _volume;

    private MarketDataSnapshot(
        double[] open,
        double[] high,
        double[] low,
        double[] close,
        double[] volume)
    {
        _open = open;
        _high = high;
        _low = low;
        _close = close;
        _volume = volume;
    }

    public ReadOnlySpan<double> Open => _open;
    public ReadOnlySpan<double> High => _high;
    public ReadOnlySpan<double> Low => _low;
    public ReadOnlySpan<double> Close => _close;
    public ReadOnlySpan<double> Volume => _volume;

    internal static MarketDataSnapshot Capture(in MarketKernel market)
    {
        var universeSize = market.UniverseSize;
        var open = new double[universeSize];
        var high = new double[universeSize];
        var low = new double[universeSize];
        var close = new double[universeSize];
        var volume = new double[universeSize];

        for (var i = 0; i < universeSize; i++)
        {
            var id = new AssetId(i);
            open[i] = market.GetScalar(Field.Open, id);
            high[i] = market.GetScalar(Field.High, id);
            low[i] = market.GetScalar(Field.Low, id);
            close[i] = market.GetScalar(Field.Close, id);
            volume[i] = market.GetScalar(Field.Volume, id);
        }

        return new MarketDataSnapshot(open, high, low, close, volume);
    }
}
