using Rhodium.Kernel;

namespace Rhodium.Platform.Patterns;

/// <summary>
/// Zero-cost iteration patterns for processing assets.
/// Uses struct visitors to avoid virtual dispatch overhead.
/// </summary>
public static class EngineLoops
{
    /// <summary>
    /// Iterates over all assets in the universe, calling the visitor for each one.
    /// This is a zero-allocation, zero-dispatch hot path.
    /// The visitor struct is passed by reference to avoid copying.
    /// </summary>
    /// <typeparam name="TVisitor">The visitor type (must be a struct implementing ITickVisitor).</typeparam>
    /// <param name="engine">Reference to the trading engine.</param>
    /// <param name="visitor">Reference to the visitor instance.</param>
    public static void ForEachAsset<TVisitor>(ref TradingEngine engine, ref TVisitor visitor)
        where TVisitor : struct, ITickVisitor
    {
        int count = engine.BatchMap.TotalSize;
        for (int i = 0; i < count; i++)
        {
            visitor.Visit(new AssetId(i), ref engine);
        }
    }

    /// <summary>
    /// Iterates over a specific range of assets, calling the visitor for each one.
    /// Useful for processing a subset of the universe (e.g., a specific sector).
    /// </summary>
    /// <typeparam name="TVisitor">The visitor type (must be a struct implementing ITickVisitor).</typeparam>
    /// <param name="engine">Reference to the trading engine.</param>
    /// <param name="visitor">Reference to the visitor instance.</param>
    /// <param name="start">Starting virtual index (inclusive).</param>
    /// <param name="count">Number of assets to process.</param>
    public static void ForEachAssetInRange<TVisitor>(ref TradingEngine engine, ref TVisitor visitor, int start, int count)
        where TVisitor : struct, ITickVisitor
    {
        int end = start + count;
        for (int i = start; i < end; i++)
        {
            visitor.Visit(new AssetId(i), ref engine);
        }
    }
}
