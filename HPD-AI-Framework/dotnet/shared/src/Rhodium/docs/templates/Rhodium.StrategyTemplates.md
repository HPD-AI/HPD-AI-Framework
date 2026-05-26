# Rhodium Strategy Templates

## Generated Strategy

```csharp
public sealed partial class MeanReversion : Strategy
{
    [BarField(Name = "RSI_14", ReadOnly = true)]
    [BarIndicator(typeof(RSI), 14)]
    public partial double Rsi { get; }

    [TickField(ReadOnly = true)]
    [TickIndicator(typeof(Spread))]
    public partial long SpreadTicks { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        setup.AddEquity("SPY");
    }

    partial void OnTick(ref TickContext tick)
    {
        if (tick.SpreadTicks <= 1)
            tick.Buy(new Qty(10m));
    }

    partial void OnBar(ref BarContext bar)
    {
        if (!bar.RsiIsReady) return;

        if (bar.Rsi < 30)
            bar.TargetQuantity(new Qty(100m));
        else if (bar.Rsi > 70)
            bar.Flatten();
    }
}
```

## Generated Book Strategy

```csharp
public sealed partial class BookAwareScalper : Strategy
{
    private AssetId _spy;

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    [BookField(ReadOnly = true)]
    public partial double Close { get; }

    partial void OnBookSnapshot(ref BookSnapshotContext book)
    {
        if (book.AssetId != _spy)
            return;

        var bid = book.BestBidTick;
        var ask = book.BestAskTick;
        if (!bid.HasValue || !ask.HasValue) return;

        if (ask.Value - bid.Value <= 1)
            book.Buy(new Qty(10m), Execution.Limit().AtBid().WithPostOnly());
    }
}
```

## Multi-Asset Bar Strategy

```csharp
protected override void OnInitialize(in SetupContext setup)
{
    setup.AddEquity("SPY");
    setup.AddEquity("MSFT");
}

partial void OnBar(ref BarContext bar)
{
    if (bar.Signal > 0)
        bar.TargetQuantity(new Qty(100m));
}
```

## Pair Trading

```csharp
private AssetId _spy;
private AssetId _qqq;

protected override void OnInitialize(in SetupContext setup)
{
    _spy = setup.AddEquity("SPY");
    _qqq = setup.AddEquity("QQQ");
}

partial void OnBar(ref BarContext bar)
{
    var spread = bar.CloseFor(_spy) - bar.CloseFor(_qqq);
    if (bar.AssetId == _spy && spread < -1) bar.TargetQuantity(new Qty(100m));
    if (bar.AssetId == _qqq && spread > 1) bar.TargetQuantity(new Qty(100m));
}
```

## Group Risk Cap

```csharp
public sealed partial class EquityRiskGroup : Strategy
{
    protected override void OnGroup(ref GroupContext group)
    {
        for (var i = 0; i < group.Children.Length; i++)
        {
            var child = group.Child(i);
            if (child.GrossExposure > 1_000_000m)
                group.Pause(child.StrategyId);
        }
    }
}
```

## Meta Allocation

```csharp
public sealed partial class MetaAllocator : Strategy
{
    protected override void OnGroup(ref GroupContext portfolio)
    {
        portfolio.AllocateInverseVolatility();
        portfolio.CapGrossExposure(2_000_000m);
    }
}
```

## Grid-Search Variant

```csharp
protected override void OnInitialize(in SetupContext setup)
{
    setup.AddEquity("SPY", variantOffset: 0);
    setup.AddEquity("SPY", variantOffset: 1);
    setup.AddEquity("SPY", variantOffset: 2);
}
```

## Strategy Test

```csharp
using var result = StrategyTest
    .For<MeanReversion>()
    .WithCloseSeries(100, 99, 98, 97, 96)
    .Run();

Assert.True(result.PositionQuantity(new AssetId(0)) >= 0m);
```
