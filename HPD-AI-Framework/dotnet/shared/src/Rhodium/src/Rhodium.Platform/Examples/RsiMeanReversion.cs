using Rhodium.Platform.Extensions;
using Rhodium.Platform.Patterns;
using Rhodium.Primitives;
using Rhodium.Kernel;

namespace Rhodium.Platform.Examples;

/// <summary>
/// Struct visitor that scans for RSI mean reversion signals.
/// Used with EngineLoops.ForEachAsset for zero-allocation iteration.
/// </summary>
readonly struct SignalScanner : ITickVisitor
{
    public void Visit(AssetId id, ref TradingEngine engine)
    {
        double rsi = engine.GetRsi14(id);

        // Oversold condition - buy signal
        if (rsi < 30)
        {
            engine.SetPosition(id, new Qty(0.5m));
        }
        // Overbought condition - sell signal
        else if (rsi > 70)
        {
            engine.Flatten(id);
        }
    }
}

/// <summary>
/// Example RSI mean reversion strategy demonstrating the Platform Layer API.
/// Trades multiple instruments using RSI(14) indicator with oversold/overbought thresholds.
/// </summary>
public sealed class RsiMeanReversion : StrategyBase
{
    private AssetId _spy;
    private AssetId _spyOptimized; // Variant 1 - for parameter optimization grid

    protected override void OnInitialize()
    {
        // Register primary instrument
        _spy = AddEquity("SPY");

        // Register variant for grid search/optimization
        _spyOptimized = AddEquity("SPY", 1);

        // Register RSI(14) indicator column
        // This ensures the column is allocated and rooted for NativeAOT
        RegisterIndicator(Fields.RSI_14);
    }

    public override void OnTick()
    {
        // Example: Check if market data is available
        if (Engine.GetBestBidTick(_spy).HasValue)
        {
            decimal depth = Engine.GetBidDepth(_spy);

            // Could use depth for position sizing or filtering
            // if (depth < 1000m) return;
        }

        // Use struct visitor pattern for zero-cost iteration
        var scanner = new SignalScanner();
        EngineLoops.ForEachAsset(ref Engine, ref scanner);
    }
}

/// <summary>
/// Alternative RSI strategy using direct position management instead of visitor pattern.
/// Demonstrates both approaches for strategy implementation.
/// </summary>
public sealed class SimpleRsiStrategy : StrategyBase
{
    private AssetId _spy;

    protected override void OnInitialize()
    {
        _spy = AddEquity("SPY");
        RegisterIndicator(Fields.RSI_14);
    }

    public override void OnTick()
    {
        double rsi = Engine.GetRsi14(_spy);

        if (rsi < 30 && Engine.GetPosition(_spy) == 0)
        {
            // Buy when oversold
            Engine.Buy(_spy, new Qty(100m), ExecutionPolicy.Safe);
        }
        else if (rsi > 70 && Engine.GetPosition(_spy) > 0)
        {
            // Sell when overbought
            Engine.Flatten(_spy);
        }
    }
}
