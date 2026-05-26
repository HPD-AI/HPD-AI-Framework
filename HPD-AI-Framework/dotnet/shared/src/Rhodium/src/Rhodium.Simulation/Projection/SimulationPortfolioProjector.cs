using Rhodium.Events;
using Rhodium.Control;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Projection;

/// <summary>
/// Projects execution effects into strategy-local world state.
/// </summary>
public sealed class SimulationPortfolioProjector
{
    /// <summary>Apply one exchange execution event to strategy-local world state.</summary>
    public StateTransitionResult Apply(ExecutionEvent evt, RhodiumRuntime runtime, AssetId? assetId = null)
    {
        if (evt is OrderFilled fill)
            return ApplyFill(fill, runtime, assetId);
        if (evt is PackageLegFilled legFill)
            return ApplyPackageLegFill(legFill, runtime);

        return StateTransitionResult.None;
    }

    /// <summary>Apply one corporate-action effect to strategy-local world state.</summary>
    public StateTransitionResult Apply(CorporateActionEffectSnapshot evt, RhodiumRuntime runtime)
    {
        var assetId = ResolveAssetId(evt.Instrument, evt.VariantId, runtime);
        ref var position = ref runtime.WorldState.PositionAt(evt.StrategyId, assetId.VirtualIndex);
        var previous = position;
        position.Quantity = evt.QuantityAfter.Value;
        position.AvgEntryPrice = evt.AvgEntryPriceAfter.Value;
        var current = position;

        return new StateTransitionResult
        {
            PositionTransition = new PositionTransition
            {
                StrategyId = evt.StrategyId,
                AssetId = assetId,
                Kind = ClassifyPositionTransition(previous, current),
                Previous = previous,
                Current = current
            }
        };
    }

    /// <summary>Apply one option lifecycle effect to strategy-local world state.</summary>
    public StateTransitionResult Apply(OptionLifecycleApplied evt, RhodiumRuntime runtime)
    {
        if (!evt.CashFlow.IsZero)
            runtime.WorldState.AdjustCash(evt.StrategyId, evt.CashFlow);

        return evt.LifecycleKind switch
        {
            OptionLifecycleKind.CashSettlement
                or OptionLifecycleKind.ExpireWorthless
                or OptionLifecycleKind.ExpireUnexercised
                or OptionLifecycleKind.ExpireUnassigned =>
                CloseInstrumentQuantity(evt.StrategyId, evt.VariantId, evt.Instrument, evt.Quantity, runtime),
            OptionLifecycleKind.PhysicalDelivery =>
                ApplyPhysicalDelivery(evt, runtime),
            OptionLifecycleKind.Exercise
                or OptionLifecycleKind.Assignment
                or OptionLifecycleKind.Blocked =>
                StateTransitionResult.None,
            _ => throw new InvalidOperationException($"Unknown option lifecycle kind {evt.LifecycleKind}.")
        };
    }

    /// <summary>Apply one account transfer to strategy-local world state.</summary>
    public StateTransitionResult Apply(AccountTransferCompleted evt, RhodiumRuntime runtime)
    {
        return evt.TransferType switch
        {
            AccountTransferType.CashDeposit => ApplyCash(evt.StrategyId, evt.CashAmount, runtime, 1m),
            AccountTransferType.CashWithdrawal => ApplyCash(evt.StrategyId, evt.CashAmount, runtime, -1m),
            AccountTransferType.AssetDeposit => ApplyAsset(evt.StrategyId, evt.VariantId, evt.Instrument, evt.Quantity, evt.CarryingPrice, runtime, 1m),
            AccountTransferType.AssetWithdrawal => ApplyAsset(evt.StrategyId, evt.VariantId, evt.Instrument, evt.Quantity, evt.CarryingPrice, runtime, -1m),
            AccountTransferType.InternalTransfer => ApplyInternalTransfer(evt, runtime),
            _ => StateTransitionResult.None
        };
    }

    private static StateTransitionResult ApplyFill(OrderFilled e, RhodiumRuntime runtime, AssetId? assetId)
    {
        var resolvedAssetId = assetId ?? e.AssetId ?? ResolveAssetId(e, runtime);
        var virtualIndex = resolvedAssetId.VirtualIndex;
        ref var position = ref runtime.WorldState.PositionAt(e.StrategyId, virtualIndex);
        var previous = position;
        var contract = runtime.CreateMarketKernel().GetContract(resolvedAssetId);
        if (contract.Package is not null)
            return StateTransitionResult.None;

        position.ApplyFill(contract, e.Side, e.FilledQty, e.FillPrice, e.Commission);
        var current = position;

        return new StateTransitionResult
        {
            PositionTransition = new PositionTransition
            {
                StrategyId = e.StrategyId,
                AssetId = resolvedAssetId,
                Kind = ClassifyPositionTransition(previous, current),
                Previous = previous,
                Current = current
            }
        };
    }

    private static StateTransitionResult ApplyPackageLegFill(PackageLegFilled e, RhodiumRuntime runtime)
    {
        var assetId = ResolveAssetId(e.LegInstrument, e.VariantId, runtime);
        var virtualIndex = assetId.VirtualIndex;
        ref var position = ref runtime.WorldState.PositionAt(e.StrategyId, virtualIndex);
        var previous = position;
        var contract = runtime.CreateMarketKernel().GetContract(assetId);
        position.ApplyFill(contract, e.Side, e.FilledQty, e.FillPrice, Money.Zero(e.FillPrice.Currency));
        var current = position;

        return new StateTransitionResult
        {
            PositionTransition = new PositionTransition
            {
                StrategyId = e.StrategyId,
                AssetId = assetId,
                Kind = ClassifyPositionTransition(previous, current),
                Previous = previous,
                Current = current
            }
        };
    }

    private static AssetId ResolveAssetId(OrderFilled e, RhodiumRuntime runtime)
        => ResolveAssetId(e.Instrument, e.VariantId, runtime);

    private static StateTransitionResult ApplyCash(
        StrategyId strategyId,
        Money? amount,
        RhodiumRuntime runtime,
        decimal sign)
    {
        if (!amount.HasValue || amount.Value.Amount <= 0m)
            return StateTransitionResult.None;

        runtime.WorldState.AdjustCash(strategyId, new Money(amount.Value.Amount * sign, amount.Value.Currency));
        return StateTransitionResult.None;
    }

    private static StateTransitionResult ApplyPhysicalDelivery(OptionLifecycleApplied evt, RhodiumRuntime runtime)
    {
        var transition = CloseInstrumentQuantity(evt.StrategyId, evt.VariantId, evt.Instrument, evt.Quantity, runtime);
        ApplyAsset(
            evt.StrategyId,
            evt.VariantId,
            evt.Deliverable,
            evt.DeliverableQuantity!.Value.Abs,
            evt.SettlementPrice,
            runtime,
            Math.Sign(evt.DeliverableQuantity.Value.Value));

        return transition;
    }

    private static StateTransitionResult CloseInstrumentQuantity(
        StrategyId strategyId,
        int variantId,
        Instrument instrument,
        Qty quantity,
        RhodiumRuntime runtime)
    {
        var assetId = ResolveAssetId(instrument, variantId, runtime);
        ref var position = ref runtime.WorldState.PositionAt(strategyId, assetId.VirtualIndex);
        var previous = position;
        ApplyPositionDelta(ref position, -quantity.Value, position.AvgEntryPrice);
        var current = position;

        return new StateTransitionResult
        {
            PositionTransition = new PositionTransition
            {
                StrategyId = strategyId,
                AssetId = assetId,
                Kind = ClassifyPositionTransition(previous, current),
                Previous = previous,
                Current = current
            }
        };
    }

    private static StateTransitionResult ApplyInternalTransfer(AccountTransferCompleted evt, RhodiumRuntime runtime)
    {
        if (!evt.DestinationStrategyId.HasValue)
            return StateTransitionResult.None;

        if (evt.CashAmount.HasValue)
        {
            ApplyCash(evt.StrategyId, evt.CashAmount, runtime, -1m);
            ApplyCash(evt.DestinationStrategyId.Value, evt.CashAmount, runtime, 1m);
            return StateTransitionResult.None;
        }

        if (!evt.Instrument.HasValue || evt.Quantity.Value <= 0m)
            return StateTransitionResult.None;

        ApplyAsset(evt.StrategyId, evt.VariantId, evt.Instrument, evt.Quantity, evt.CarryingPrice, runtime, -1m);
        return ApplyAsset(evt.DestinationStrategyId.Value, evt.DestinationVariantId, evt.Instrument, evt.Quantity, evt.CarryingPrice, runtime, 1m);
    }

    private static StateTransitionResult ApplyAsset(
        StrategyId strategyId,
        int variantId,
        Instrument? instrument,
        Qty quantity,
        Price? carryingPrice,
        RhodiumRuntime runtime,
        decimal sign)
    {
        if (!instrument.HasValue || quantity.Value <= 0m)
            return StateTransitionResult.None;

        var assetId = ResolveAssetId(instrument.Value, variantId, runtime);
        ref var position = ref runtime.WorldState.PositionAt(strategyId, assetId.VirtualIndex);
        var previous = position;
        ApplyPositionDelta(ref position, quantity.Value * sign, carryingPrice?.Value ?? position.AvgEntryPrice);
        var current = position;

        return new StateTransitionResult
        {
            PositionTransition = new PositionTransition
            {
                StrategyId = strategyId,
                AssetId = assetId,
                Kind = ClassifyPositionTransition(previous, current),
                Previous = previous,
                Current = current
            }
        };
    }

    private static AssetId ResolveAssetId(Instrument instrument, int variantId, RhodiumRuntime runtime)
    {
        var (start, _) = runtime.BatchMap.GetInstrumentRange(instrument);
        return new AssetId(start + variantId);
    }

    private static PositionTransitionKind ClassifyPositionTransition(PositionState previous, PositionState current)
    {
        if (previous.IsFlat && !current.IsFlat)
            return PositionTransitionKind.Opened;
        if (!previous.IsFlat && current.IsFlat)
            return PositionTransitionKind.Closed;
        if (!previous.IsFlat && !current.IsFlat)
            return PositionTransitionKind.Changed;

        return PositionTransitionKind.None;
    }

    private static void ApplyPositionDelta(ref PositionState position, decimal delta, decimal carryingPrice)
    {
        if (delta == 0m)
            return;

        var current = position.Quantity;
        var next = current + delta;
        if (next == 0m)
        {
            position.Quantity = 0m;
            position.AvgEntryPrice = 0m;
            return;
        }

        var addsSameSide = current == 0m || Math.Sign(current) == Math.Sign(delta);
        if (addsSameSide)
        {
            position.AvgEntryPrice = ((Math.Abs(current) * position.AvgEntryPrice) + (Math.Abs(delta) * carryingPrice)) / Math.Abs(next);
        }

        position.Quantity = next;
    }
}
