using Rhodium.Indicators.Streaming;
using Rhodium.Kernel;
using Rhodium.Platform.Attributes;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;

namespace Rhodium.Platform.Examples;

public sealed partial class RsiMeanReversion : Strategy
{
    [BarField(Name = "RSI_14", ReadOnly = true)]
    [BarIndicator(typeof(RSI), 14)]
    public partial double Rsi { get; }

    [TickField(ReadOnly = true)]
    [TickIndicator(typeof(Spread))]
    public partial long SpreadTicks { get; }

    private AssetId _spy;
    private AssetId _spyOptimized;

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
        _spyOptimized = setup.AddEquity("SPY", 1);
    }

    partial void OnTick(ref TickContext tick)
    {
        if (tick.AssetId == _spy && tick.SpreadTicks <= 1)
        {
            // Tick path intentionally stays tiny; bar path owns the RSI signal.
        }
    }

    partial void OnBar(ref BarContext bar)
    {
        if (!bar.RsiIsReady) return;

        if (bar.Rsi < 30)
            bar.TargetQuantity(new Qty(0.5m));
        else if (bar.Rsi > 70)
            bar.Flatten();
    }
}

public sealed partial class SimpleRsiStrategy : Strategy
{
    [BarField(Name = "RSI_14", ReadOnly = true)]
    [BarIndicator(typeof(RSI), 14)]
    public partial double Rsi { get; }

    private AssetId _spy;

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId != _spy || !bar.RsiIsReady) return;

        if (bar.Rsi < 30 && bar.PositionQuantity == 0m)
            bar.Buy(new Qty(100m));
        else if (bar.Rsi > 70 && bar.PositionQuantity > 0m)
            bar.Flatten();
    }
}
