using System.Runtime.CompilerServices;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform;

/// <summary>
/// Base class for all user strategies.
/// Enforces lifecycle and safety guards.
/// </summary>
public abstract class StrategyBase
{
    protected TradingEngine Engine;
    private int _initializedVersion;

    internal void Initialize(TradingEngine engine)
    {
        Engine = engine;
        OnInitialize();
        // Record version after OnInitialize() completes, since strategies may add instruments during initialization
        _initializedVersion = Engine.BatchMap.Version;
    }

    /// <summary>
    /// Called once during strategy initialization.
    /// Use this to register instruments, indicators, and allocate resources.
    /// </summary>
    protected virtual void OnInitialize() { }

    /// <summary>
    /// Called on every tick.
    /// This is the hot path - must be allocation-free in production builds.
    /// </summary>
    public abstract void OnTick();

    /// <summary>
    /// Adds an equity instrument to the strategy universe.
    /// Returns the AssetId for accessing this instrument's data.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected AssetId AddEquity(string symbol)
    {
        var instrument = new Instrument(new Asset(symbol, AssetClass.Equity), Venue.NASDAQ);

        // Try to get existing range first
        try
        {
            var range = Engine.BatchMap.GetInstrumentRange(instrument);
            return new AssetId(range.Start);
        }
        catch (KeyNotFoundException)
        {
            // Instrument doesn't exist, add it
            Engine.BatchMap.AddInstrument(instrument, 1);
            Engine.Tensors.Grow();
            var range = Engine.BatchMap.GetInstrumentRange(instrument);
            return new AssetId(range.Start);
        }
    }

    /// <summary>
    /// Adds an equity instrument with a specific variant offset.
    /// Useful for grid search or parameter optimization.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected AssetId AddEquity(string symbol, int variantOffset)
    {
        var instrument = new Instrument(new Asset(symbol, AssetClass.Equity), Venue.NASDAQ);

        // Try to get existing range first
        try
        {
            var range = Engine.BatchMap.GetInstrumentRange(instrument);
            return new AssetId(range.Start + variantOffset);
        }
        catch (KeyNotFoundException)
        {
            // Instrument doesn't exist, add it with enough variants
            // We add 10 variants by default to support variant-based strategies
            Engine.BatchMap.AddInstrument(instrument, 10);
            Engine.Tensors.Grow();
            var range = Engine.BatchMap.GetInstrumentRange(instrument);
            return new AssetId(range.Start + variantOffset);
        }
    }

    /// <summary>
    /// Registers an indicator field for AOT compilation and column allocation.
    /// Must be called during OnInitialize.
    /// </summary>
    protected void RegisterIndicator<T>(VectorField<T> field) where T : unmanaged
    {
        Engine.EnsureColumn(field);
    }

    /// <summary>
    /// Internal method to run the strategy tick with safety guards.
    /// Checks for universe version changes and allocations in debug builds.
    /// </summary>
    internal void RunTickGuarded()
    {
        if (Engine.BatchMap.Version != _initializedVersion)
        {
            throw new InvalidOperationException(
                $"Universe version mismatch. Expected {_initializedVersion}, got {Engine.BatchMap.Version}. " +
                "Strategy must be reinitialized when universe topology changes."
            );
        }

#if DEBUG
        long start = GC.GetAllocatedBytesForCurrentThread();
#endif

        OnTick();

#if DEBUG
        long diff = GC.GetAllocatedBytesForCurrentThread() - start;
        if (diff > 0)
        {
            throw new InvalidOperationException(
                $"Hot path allocation detected: {diff} bytes allocated in OnTick(). " +
                "Strategy must be allocation-free on the hot path."
            );
        }
#endif
    }
}
