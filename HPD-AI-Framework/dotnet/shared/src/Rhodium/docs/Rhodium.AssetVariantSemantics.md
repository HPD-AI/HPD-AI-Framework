# Rhodium Asset And Variant Semantics

`AssetId` identifies one virtual slot in the runtime universe. A single instrument can own multiple contiguous slots for parameter variants, grid search, or strategy-specific views.

```csharp
var spy = AddEquity("SPY");      // variant 0
var spyFast = AddEquity("SPY", 1); // variant 1
```

The physical instrument is still SPY. The virtual index differs. Market data fields are shared by virtual slot, while portfolio state is isolated by `StrategyId`.

Rules:

- Use `bar.AssetId` inside generated `OnBar(ref BarContext bar)` logic.
- Use explicit `AssetId` values for cross-asset logic and tests.
- Treat variant offsets as part of the strategy design, not as independent instruments.
- Fills and positions are keyed by `StrategyId` plus virtual index, so two strategies can trade the same instrument without sharing position state.
