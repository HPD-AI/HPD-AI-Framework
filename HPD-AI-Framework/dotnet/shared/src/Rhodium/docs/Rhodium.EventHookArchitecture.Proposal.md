# Rhodium Event Hook Architecture Proposal

**Document:** `Rhodium.EventHookArchitecture.Proposal.md`  
**Version:** 1.0.0  
**Date:** May 2026  
**Status:** Proposal  
**Supersedes:** Single internal `OnTick` dispatch for generated strategies  
**Depends On:** Implemented Unified Kernel foundation (`MarketKernel`, `PortfolioContext`, `WorldState`, generated `Strategy`)  
**Required By:** VectorCore vNext, VectorMode vNext

## Executive Summary

Rhodium currently has a strong generated strategy authoring model: users write `Strategy`, declare generated fields and indicators, and implement typed partial hooks such as `OnTick(ref TickContext)` and `OnBar(ref BarContext)`. The nice path and fast path are already the same public path.

The remaining architectural gap is dispatch semantics. Internally, `TradingHost.ProcessEvent` applies any `FinanceEvent`, creates a `MarketKernel`, and dispatches every strategy through the same hierarchical tick path. The generated `Strategy.OnTick` then runs both generated tick and bar loops every time. That was acceptable for the first unified kernel cut, but it is not the right foundation for first-class quote, trade, book, order, position, timer, and lifecycle hooks.

This proposal replaces the single universal generated dispatch path with an event-specific hook architecture:

```csharp
partial void OnQuote(ref QuoteContext quote);
partial void OnTrade(ref TradeContext trade);
partial void OnBookSnapshot(ref BookSnapshotContext book);
partial void OnBar(ref BarContext bar);
partial void OnTick(ref TickContext tick);

protected virtual void OnOrderAccepted(ref OrderContext order) {}
protected virtual void OnOrderRejected(ref OrderContext order) {}
protected virtual void OnOrderFilled(ref FillContext fill) {}

protected virtual void OnPositionOpened(ref PositionContext position) {}
protected virtual void OnPositionChanged(ref PositionContext position) {}
protected virtual void OnPositionClosed(ref PositionContext position) {}

protected virtual void OnStart(ref LifecycleContext lifecycle) {}
protected virtual void OnStop(ref LifecycleContext lifecycle) {}
protected virtual void OnScheduled(ref TimerContext timer) {}

protected virtual void OnGroup(ref GroupContext group) {}
```

The design borrows the best parts of HPD Agent middleware: typed hook contexts, explicit ordering, scoped execution, and controlled state access. It does not copy the agent middleware implementation. Rhodium remains generated, synchronous, stack-oriented, and zero-allocation on the hot path.

## Current Code Findings

The current Rhodium code already contains the right ingredients:

- `MarketEvents.cs` defines `QuoteReceived`, `TradeOccurred`, `BarClosed`, and `BookSnapshotReceived`.
- `ExecutionEvents.cs` defines strategy-routed order events, including `StrategyId`.
- `LifecycleEvents.cs` defines session, market, scheduled, and universe events.
- `StrategyGenerator` already emits generated partial market hooks for tick and bar contexts.
- `PortfolioContext` already buffers order intents and allocation commands.
- `WorldState` already isolates per-strategy positions and order intents.

The issue is not missing concepts. The issue is that dispatch is too coarse.

Current flow:

```csharp
TradingHost.ProcessEvent(any FinanceEvent)
    -> SimulationMarketProjector.Apply(...) or SimulationPortfolioProjector.Apply(...)
    -> market.RunAdjustmentKernel()
    -> internal event-specific dispatch loop
    -> Strategy.OnTick(...)
    -> __GeneratedRunTick(...)
    -> __GeneratedRunBars(...)
    -> OnGroup(...)
    -> SubmitOrderIntents(...)
```

That means a quote event can run bar hooks, a bar event can run tick hooks, and execution events can run market hooks. This blurs lifecycle semantics and makes advanced hooks feel retrofitted.

The new model breaks that path deliberately.

## Post-Implementation Audit Notes

The implemented Unified Kernel foundation is structurally correct, and this proposal has been mostly carried into the runtime. These notes are kept as historical audit context for why the event-specific hook work was required.

The original implementation had three concrete issues this proposal resolved:

1. `TradingHost.ProcessEvent` treats every `FinanceEvent` as a strategy dispatch trigger.
2. `Strategy.OnTick` invokes generated tick and bar hooks unconditionally.
3. Generated contexts pass `PortfolioContext` by value into generated context structs.

The third point is especially important. `PortfolioContext` is a `ref struct` carrying spans plus scalar counters such as pending command count and order-intent count. A generated context must not copy it by value. Span-backed position mutations may still hit the same underlying memory, but scalar fields and intent counters can diverge. Generated market contexts now use a byref-safe `PortfolioContextFrame` rather than exposing or copying `PortfolioContext`.

This is a correctness issue, not only a DX issue. Generated order-intent helpers such as:

```csharp
bar.Buy(new Qty(1m), Execution.Limit().AtBid().WithPostOnly());
```

must enqueue into the same `PortfolioContext` instance committed by the internal runtime dispatch loop, not into a copied context frame.
`EngineLoops` is runtime-internal infrastructure; it is not a user-facing strategy authoring API.

## Design Principles

### One Public Strategy Surface

There is one official user-facing base type:

```csharp
public abstract partial class Strategy
```

Raw kernel dispatch is engine-internal. User strategies derive from `Strategy` and use generated typed hooks.

### Typed Contexts, Not Mega-Contexts

Every hook receives a narrow context exposing only what is valid for that event.

Examples:

```csharp
partial void OnQuote(ref QuoteContext quote)
{
    if (quote.SpreadTicks <= 1)
        quote.Buy(new Qty(10m), Execution.Limit().AtBid().WithPostOnly());
}

partial void OnBar(ref BarContext bar)
{
    if (!bar.RsiIsReady) return;
    if (bar.Rsi < 30) bar.TargetQuantity(new Qty(100m));
}

protected override void OnOrderFilled(ref FillContext fill)
{
    if (fill.Position.Quantity == 0)
        fill.EmitMetric("round_trip_complete");
}
```

No hook receives a nullable grab bag. No public hook receives `FinanceEvent`.

### Generated Hooks For Market Data

Market-data hooks are generated partial methods because they sit on the hot path and need typed generated field access.

Generated partial hooks:

```csharp
partial void OnQuote(ref QuoteContext quote);
partial void OnTrade(ref TradeContext trade);
partial void OnBookSnapshot(ref BookSnapshotContext book);
partial void OnBar(ref BarContext bar);
partial void OnTick(ref TickContext tick);
```

The generator emits:

- per-asset iteration
- field and indicator updates
- generated context construction
- cross-asset generated accessors
- no-op elimination when the partial hook is not implemented

### Virtual Hooks For Operational Events

Operational hooks are virtual methods on `Strategy`. They are not partial generated methods because they are not field-generation dependent and are not normally called per market-data tick.

Virtual operational hooks:

```csharp
protected virtual void OnStart(ref LifecycleContext lifecycle) {}
protected virtual void OnStop(ref LifecycleContext lifecycle) {}
protected virtual void OnScheduled(ref TimerContext timer) {}

protected virtual void OnOrderAccepted(ref OrderContext order) {}
protected virtual void OnOrderRejected(ref OrderContext order) {}
protected virtual void OnOrderCancelled(ref OrderContext order) {}
protected virtual void OnOrderExpired(ref OrderContext order) {}
protected virtual void OnOrderFilled(ref FillContext fill) {}

protected virtual void OnPositionOpened(ref PositionContext position) {}
protected virtual void OnPositionChanged(ref PositionContext position) {}
protected virtual void OnPositionClosed(ref PositionContext position) {}
```

These hooks are routed by `StrategyId` where applicable. A strategy never receives another strategy's execution event.

### Strict Hook Ordering

Hook order is part of the trading contract.

For market-data events:

```text
1. Apply state transition into MarketKernel / WorldState.
2. Run AdjustmentKernel only if the event updates adjusted market fields.
3. Execute matching leaf hook for the event kind.
4. Snapshot leaf contexts.
5. Execute group/meta hooks.
6. Apply allocation commands between phases.
7. Drain order intents and submit commands.
```

For execution events:

```text
1. Apply execution state transition into WorldState.
2. Route order hook to the owning StrategyId.
3. Synthesize position transition, if any.
4. Route position hook to the owning StrategyId.
5. Execute group/meta hooks if portfolio state changed.
6. Drain order intents and submit commands.
```

For lifecycle events:

```text
1. Apply lifecycle state, if any.
2. Route lifecycle hook to all strategies or relevant scoped strategies.
3. Execute group/meta hooks only when lifecycle state can affect allocation.
4. Drain order intents only for hooks that permit trading.
```

## Dispatch Model

### New Dispatch Kinds

Create a small event-kind enum used internally by the host and engine loops:

```csharp
internal enum StrategyDispatchKind
{
    Tick,
    Quote,
    Trade,
    Book,
    Bar,
    GroupOnly,
    Execution,
    Lifecycle,
    Timer
}
```

`TradingHost.ProcessEvent` becomes an event router rather than a universal tick dispatcher.

Target shape:

```csharp
private void ProcessEvent(FinanceEvent evt)
{
    var transition = evt is ExecutionEvent execution
        ? _portfolioProjector.Apply(execution, _runtime)
        : _marketProjector.Apply(evt, _runtime);

    var market = _runtime.CreateMarketKernel();

    if (transition.RequiresAdjustment)
        market.RunAdjustmentKernel();

    switch (evt)
    {
        case QuoteReceived quote:
            DispatchQuote(in market, quote);
            break;

        case TradeOccurred trade:
            DispatchTrade(in market, trade);
            break;

        case BookSnapshotReceived book:
            DispatchBook(in market, book);
            break;

        case BarClosed bar:
            DispatchBar(in market, bar);
            break;

        case ExecutionEvent execution:
            DispatchExecution(in market, execution, transition);
            break;

        case LifecycleEvent lifecycle:
            DispatchLifecycle(in market, lifecycle);
            break;
    }

    SubmitOrderIntents(in market);
}
```

### Strategy Entry Points

`Strategy.OnTick` is not the public generated dispatch entry point. `Strategy` owns separate internal guarded entry points, each preserving the debug hot-path allocation guard where relevant:

```csharp
internal void RunQuoteGuarded(in MarketKernel market, ref PortfolioContext portfolio, in QuoteReceived evt);
internal void RunTradeGuarded(in MarketKernel market, ref PortfolioContext portfolio, in TradeOccurred evt);
internal void RunBookSnapshotGuarded(in MarketKernel market, ref PortfolioContext portfolio, in BookSnapshotReceived evt);
internal void RunBarGuarded(in MarketKernel market, ref PortfolioContext portfolio, in BarClosed evt);
internal void RunTickGuarded(in MarketKernel market, ref PortfolioContext portfolio);
internal void RunGroupGuarded(in MarketKernel market, ref PortfolioContext portfolio);
internal void RunExecutionGuarded(in MarketKernel market, ref PortfolioContext portfolio, in ExecutionEvent evt);
```

The generator-only dispatch bridge uses event-specific methods that remain hidden from normal
authoring (`EditorBrowsable(Never)`). They are not a documented user strategy surface:

```csharp
protected virtual void __GeneratedRunQuote(in MarketKernel market, ref PortfolioContext portfolio, in QuoteReceived evt) {}
protected virtual void __GeneratedRunTrade(in MarketKernel market, ref PortfolioContext portfolio, in TradeOccurred evt) {}
protected virtual void __GeneratedRunBookSnapshot(in MarketKernel market, ref PortfolioContext portfolio, in BookSnapshotReceived evt) {}
protected virtual void __GeneratedRunBars(in MarketKernel market, ref PortfolioContext portfolio, in BarClosed evt) {}
protected virtual void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio) {}
```

If a user does not implement a partial hook, the generator emits no call for it.

## Context Types

### Market Contexts

All market contexts are `ref struct` values. They carry:

- `AssetId`
- `StrategyId`
- private engine access to `MarketKernel` for generated accessors and order helpers
- byref-safe `PortfolioContextFrame` for generated field writes and order-intent helpers
- event-specific payload
- generated field accessors
- order intent helpers

Example public surface:

```csharp
public ref struct QuoteContext
{
    public AssetId AssetId { get; }
    public StrategyId StrategyId { get; }
    public Quote Quote { get; }
    public long? BidTick { get; }
    public long? AskTick { get; }
    public long SpreadTicks { get; }

    public void Buy(Qty qty, ExecutionSpec execution = default);
    public void Sell(Qty qty, ExecutionSpec execution = default);
    public void TargetQuantity(Qty qty, ExecutionSpec execution = default);
}
```

`TradeContext`, `BookSnapshotContext`, `BarContext`, and `TickContext` follow the same pattern.

### Execution Contexts

Execution contexts are stack-only where they need private engine state. Public operational contexts should expose domain data and helpers, not raw `MarketKernel` or `PortfolioContext` handles.

```csharp
public ref struct OrderContext
{
    public StrategyId StrategyId { get; }
    public OrderId OrderId { get; }
    public AssetId AssetId { get; }
    public OrderStatus Status { get; }
    public string? Reason { get; }
}

public ref struct FillContext
{
    public StrategyId StrategyId { get; }
    public OrderId OrderId { get; }
    public AssetId AssetId { get; }
    public Side Side { get; }
    public Qty FilledQty { get; }
    public Price FillPrice { get; }
    public Money Commission { get; }
    public Position Position { get; }
}
```

### Position Contexts

Position hooks are synthesized from execution transitions. `SimulationPortfolioProjector.Apply(OrderFilled, ...)` should return before/after position metadata rather than only mutating state.

```csharp
public enum PositionTransitionKind
{
    None,
    Opened,
    Changed,
    Closed
}

public readonly struct PositionTransition
{
    public StrategyId StrategyId { get; init; }
    public AssetId AssetId { get; init; }
    public PositionTransitionKind Kind { get; init; }
    public Position Previous { get; init; }
    public Position Current { get; init; }
}
```

The host routes `Opened`, `Changed`, and `Closed` to the owning strategy only.

## Scoping

Rhodium should adopt scoping as a design concept, not as runtime middleware metadata.

Initial scopes:

- strategy scope: only the owning strategy receives execution and position events
- asset scope: generated market hooks run only for registered assets matching the event instrument
- hierarchy scope: group/meta hooks run only after relevant child state changes

Later scopes:

- venue scope
- asset-class scope
- session scope
- strategy-tag scope

The generator can eventually support attributes such as:

```csharp
[OnVenue(Venue.NASDAQ)]
partial void OnQuote(ref QuoteContext quote);
```

This is explicitly deferred. The first implementation should use registered assets and `StrategyId` routing only.

## What This Learns From HPD Agent Middleware

HPD Agent middleware has the right high-level hook discipline:

- typed contexts per lifecycle point
- explicit before/after/wrap semantics
- reverse-order unwind for after/error hooks
- scoped execution
- controlled state mutation

Rhodium should keep those ideas and reject the parts that do not fit trading:

- no async hot-path hooks
- no runtime middleware chains for every quote/trade/bar
- no class allocation per hook
- no nullable mega-contexts
- no public `OnEvent(FinanceEvent)` default path

Rhodium's version is:

```text
typed contexts + strict order + generated dispatch + ref structs + zero allocation
```

## What This Learns From NautilusTrader

NautilusTrader demonstrates that advanced trading systems need a broad lifecycle:

- market-data hooks
- order hooks
- position hooks
- timer hooks
- lifecycle hooks
- subscription control

The lesson is not to copy every hook one-for-one. The lesson is that serious strategies need these lifecycle points to be first-class.

Rhodium should provide fewer hooks than Nautilus initially, but each hook should be more strongly typed and integrated with generated field access.

## Files To Update

### `src/Rhodium.Connectivity/TradingHost.cs`

- Replace universal dispatch in `ProcessEvent` with event-specific routing.
- Call `market.RunAdjustmentKernel()` only when required by the transition.
- Route execution hooks by `StrategyId`.
- Route lifecycle hooks to all or scoped strategies.
- Drain order intents after each routed event.

### `src/Rhodium.Simulation/Projection/SimulationMarketProjector.cs`

- Return a `StateTransitionResult`.
- Apply `QuoteReceived`, `TradeOccurred`, and `BookSnapshotReceived`.
- Mark whether adjusted market fields require recomputation.

### `src/Rhodium.Simulation/Projection/SimulationPortfolioProjector.cs`

- Return a `StateTransitionResult`.
- Apply execution and account events.
- Expand `OrderFilled` transition to include before/after position state.

### `src/Rhodium.Platform/Strategy.Core.cs`

- Replace the single internal guarded tick entry point with event-specific guarded entry points.
- Keep allocation guards around hot market-data hooks.
- Add operational dispatch methods for lifecycle, order, and position hooks.

### `src/Rhodium.Platform/Strategy.cs`

- Remove universal `OnTick` as the generated public path.
- Add event-specific generated run methods.
- Keep `OnGroup` as the hierarchy hook.
- Add virtual operational hooks.

### `src/Rhodium.Generators/StrategyGenerator.cs`

- Generate `QuoteContext`, `TradeContext`, and `BookSnapshotContext` hooks.
- Change bar generation to accept the triggering `BarClosed` event.
- Generate no-op-free calls only for implemented partial hooks.
- Preserve existing generated `TickContext` support for synthetic/manual tick dispatch.
- Generate context structs that carry byref-safe `PortfolioContextFrame` semantics without copying `PortfolioContext` by value or exposing raw engine handles.
- Add generator tests proving generated execution-spec helpers drain through `WorldState.DrainOrderIntents`.

### `src/Rhodium.Platform/Patterns/EngineLoops.cs`

- Split hierarchical dispatch into event-specific leaf execution plus reusable group/meta phases.
- Keep sequential and parallel support, but parallelism applies within the event-specific leaf phase.
- Keep this type internal. Tests and benchmarks may use friend-assembly access, but public strategy examples and user APIs must not teach direct `EngineLoops` calls.

### New Files

```text
src/Rhodium.Platform/QuoteContext.cs
src/Rhodium.Platform/TradeContext.cs
src/Rhodium.Platform/BookSnapshotContext.cs
src/Rhodium.Platform/OrderContext.cs
src/Rhodium.Platform/FillContext.cs
src/Rhodium.Platform/PositionContext.cs
src/Rhodium.Platform/LifecycleContext.cs
src/Rhodium.Platform/TimerContext.cs
src/Rhodium.Control/StateTransitionResult.cs
src/Rhodium.Control/PositionTransition.cs
```

## Files To Delete Or Retire

No new compatibility shim should be added.

The following internal concepts should be retired:

- universal generated `Strategy.OnTick` dispatch
- unconditional `__GeneratedRunTick` plus `__GeneratedRunBars` execution on every event
- host behavior that treats all `FinanceEvent` values as strategy ticks

If a compatibility layer feels necessary, the design is drifting toward retrofit. Break it instead.

## Implementation Phases

### Phase 0 - Generated Context Byref Fix

Goal: fix generated context correctness before widening the hook surface.

Work:

- Change generated market-context construction so `PortfolioContext` is not copied by value.
- Add a generated-strategy test where `OnBar(ref BarContext bar)` calls `bar.Buy(..., ExecutionSpec)` and the resulting `OrderIntent` is visible after dispatch.
- Add a generated-strategy test where `OnGroup` commands and generated order intents both survive the same dispatch pass.

Gate:

- Generated context order-intent helpers commit correctly.
- Generated context writable fields still write to the strategy-private tensor store.
- Hot-path allocation guard remains green.

### Phase 1 - Operational Hooks

Goal: Add lifecycle and execution hooks without changing market-data generation yet.

Work:

- Add `LifecycleContext`, `OrderContext`, `FillContext`, and `PositionContext`.
- Add virtual hooks on `Strategy`.
- Route `OrderAccepted`, `OrderRejected`, `OrderCancelled`, `OrderExpired`, and `OrderFilled` by `StrategyId`.
- Add `StateTransitionResult` and `PositionTransition`.
- Add tests proving strategy A never receives strategy B's execution event.

Gate:

- Existing generated market hooks still pass.
- Execution hooks observe updated portfolio state after fills.
- Position hooks fire exactly once per open/change/close transition.

### Phase 2 - Market Event Dispatch Split

Goal: Stop running every generated hook on every event.

Work:

- Split `TradingHost.ProcessEvent` by event type.
- Split the internal dispatch loop into event-specific leaf dispatch and reusable group/meta phases.
- Change `Strategy` generated entry points to event-specific methods.
- Run `BarContext` only for `BarClosed`.
- Run `TickContext` only for synthetic/manual tick dispatch or explicit tick-frame events.

Gate:

- A `QuoteReceived` event does not invoke `OnBar`.
- A `BarClosed` event does not invoke `OnQuote`.
- Existing bar strategies continue to compile against the new generated path.

### Phase 3 - Quote, Trade, And Book Generated Hooks

Goal: Make HFT-facing market hooks first-class.

Work:

- Add `[QuoteField]`, `[TradeField]`, or extend existing tick field attributes if the existing model is sufficient.
- Generate `QuoteContext`, `TradeContext`, and `BookSnapshotContext`.
- Generate partial hooks:

```csharp
partial void OnQuote(ref QuoteContext quote);
partial void OnTrade(ref TradeContext trade);
partial void OnBookSnapshot(ref BookSnapshotContext book);
```

- Add cross-asset accessors for generated quote/trade/book fields.

Gate:

- Generated quote/trade/book hooks allocate zero managed bytes in debug hot-path guard.
- Generated hooks can submit `OrderIntent` through the same `PortfolioContext` path as bar hooks.

### Phase 4 - Timer Hooks

Goal: Add deterministic scheduled strategy hooks.

Work:

- Add timer registration during initialization.
- Route `Scheduled` lifecycle events to `OnScheduled(ref TimerContext timer)`.
- Ensure timers are deterministic in replay/backtest.

Gate:

- Scheduled hooks replay identically with the same event stream.
- Timer hooks can trade only through order intents.

### Phase 5 - Scoped Hook Filters

Goal: Add optional compile-time filters after the base hook model is stable.

Work:

- Add venue/asset-class/session hook attributes if real strategies need them.
- Keep filters generator-resolved where possible.
- Avoid runtime middleware metadata in hot dispatch.

Gate:

- Filtered hooks do not add hot-path allocations.
- Filtered hooks are compile-time diagnosable when impossible or contradictory.

## Example Final DX

```csharp
public sealed partial class LiquidityFade : Strategy
{
    [BarField(ReadOnly = true)]
    [BarIndicator(typeof(RSI), 14)]
    public partial double Rsi { get; }

    [TickField]
    [TickIndicator(typeof(Spread))]
    public partial double SpreadTicks { get; }

    private AssetId _spy;

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnQuote(ref QuoteContext quote)
    {
        if (quote.AssetId != _spy) return;
        if (quote.SpreadTicks <= 1 && quote.BidSize > quote.AskSize * 2)
            quote.Buy(new Qty(100m), Execution.Limit().AtBid().WithPostOnly());
    }

    partial void OnBar(ref BarContext bar)
    {
        if (!bar.RsiIsReady) return;
        if (bar.Rsi > 70)
            bar.Flatten();
    }

    protected override void OnOrderFilled(ref FillContext fill)
    {
        if (fill.Position.Quantity == 0)
            fill.EmitMetric("flat_after_fill");
    }

    protected override void OnGroup(ref GroupContext group)
    {
        group.CapGrossExposure(1_000_000m);
    }
}
```

The user gets an event-rich model without choosing between ergonomics and latency.

## Acceptance Criteria

- A market event invokes only its matching generated market hook.
- Execution events route only to the owning `StrategyId`.
- Position hooks are synthesized from before/after position state and fire exactly once.
- Group/meta hooks execute after leaf hooks and before order intents are drained.
- Hot market hooks allocate zero managed bytes.
- No public `OnEvent(FinanceEvent)` escape hatch is introduced.
- No async hook is introduced in the hot path.
- The generated `Strategy` surface remains the official recommended DX.
- Raw kernel dispatch remains internal; `Strategy` is the only public authoring surface.

## Decision

Proceed with the event-specific hook architecture. Break the current universal generated `OnTick` dispatch. Do not add a compatibility layer. If a hook feels like middleware, keep the ordering and typed-context idea, but implement it as generated/static Rhodium dispatch, not runtime agent middleware.
