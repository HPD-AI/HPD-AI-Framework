using System;

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
    public EngineSnapshot? TakeSnapshot(ref Rhodium.Kernel.TradingEngine engine)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // TODO: Actual snapshot implementation
        // - Check pool capacity
        // - Copy tensor data
        // - Capture BatchMap version
        // - Return snapshot handle

        return null;
    }

    /// <summary>
    /// Release a snapshot back to the pool.
    /// </summary>
    public void ReleaseSnapshot(EngineSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // TODO: Return buffers to pool
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // TODO: Dispose pool resources
            _disposed = true;
        }
    }
}

/// <summary>
/// Immutable snapshot of engine state for background computation.
/// </summary>
public sealed class EngineSnapshot : IDisposable
{
    public int BatchMapVersion { get; init; }
    public Rhodium.Primitives.Sequence Sequence { get; init; }

    // TODO: Snapshot data (tensor copies, positions, etc.)

    public void Dispose()
    {
        // TODO: Release snapshot buffers
    }
}
