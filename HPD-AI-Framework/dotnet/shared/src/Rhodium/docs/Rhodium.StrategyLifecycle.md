# Rhodium Strategy Lifecycle

Rhodium has one public generated strategy surface: `Strategy`.

Declare generated bar fields with `[BarField]`, attach single-output bar indicators with `[BarIndicator]`, declare tick fields with `[TickField]`, attach tick indicators with `[TickIndicator]`, and implement either or both generated handlers:

```csharp
partial void OnTick(ref TickContext tick)
{
    if (tick.BookSpreadTicks <= 1) tick.Buy(new Qty(10m));
}

partial void OnBar(ref BarContext bar)
{
    if (!bar.RsiIsReady) return;
    if (bar.Rsi < 30) bar.TargetQuantity(new Qty(100m));
}
```

The generator owns the sealed strategy tick dispatch, iterates registered assets, updates generated tick and bar indicators, constructs stack-only `TickContext` and `BarContext` values, and calls the partial handlers. The nice path and fast path are the same public path.

Generated contexts also emit cross-asset accessors for every generated field:

```csharp
var spread = bar.CloseFor(_spy) - bar.CloseFor(_qqq);
var fastBook = tick.SpreadTicksFor(_spy) <= tick.SpreadTicksFor(_qqq);
```

Hierarchy uses the same `Strategy` surface. Group and meta strategies override `OnGroup`:

```csharp
protected override void OnGroup(ref GroupContext group)
{
    group.AllocateInverseVolatility();
    group.CapGrossExposure(2_000_000m);
}
```

Initialization is always explicit:

```csharp
protected override void OnInitialize(in SetupContext setup)
{
    setup.AddEquity("SPY");
}
```

Instruments and generated tensor fields are registered only during initialization. Hot-path strategy execution must allocate zero managed bytes in debug builds.
