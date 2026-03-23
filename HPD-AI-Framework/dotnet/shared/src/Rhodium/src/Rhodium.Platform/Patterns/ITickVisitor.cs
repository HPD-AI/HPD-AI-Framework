using Rhodium.Kernel;

namespace Rhodium.Platform.Patterns;

/// <summary>
/// Struct visitor contract for zero-cost iteration.
/// Implement this interface to process assets in a tight loop.
/// </summary>
public interface ITickVisitor
{
    /// <summary>
    /// Processes a single asset during iteration.
    /// Called once per asset in the universe.
    /// </summary>
    /// <param name="id">The asset ID to process.</param>
    /// <param name="engine">Reference to the trading engine.</param>
    void Visit(AssetId id, ref TradingEngine engine);
}
