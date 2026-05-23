using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Kernel;

/// <summary>
/// Stack-only byref-safe view over a <see cref="PortfolioContext"/> for generated contexts.
/// Copies share the mutable command and order-intent counters with the committed context.
/// </summary>
public ref struct PortfolioContextFrame
{
    private Span<PositionState> _positions;
    private ReadOnlySpan<PortfolioSnapshot> _childSnapshots;
    private Span<int> _counters;
    private Span<AllocationCommand> _pendingCommands;
    private Span<OrderIntent> _orderIntents;
    private PagedTensorStore _strategyTensors;

    internal PortfolioContextFrame(
        StrategyId strategyId,
        StrategyId? parentId,
        ReadOnlySpan<StrategyId> childIds,
        Span<int> counters,
        Span<PositionState> positions,
        Span<AllocationCommand> pendingCommands,
        Span<OrderIntent> orderIntents,
        PagedTensorStore strategyTensors,
        Money cash,
        decimal allocationWeight,
        Money? maxCapital,
        bool isPaused,
        ReadOnlySpan<PortfolioSnapshot> childSnapshots)
    {
        StrategyId = strategyId;
        ParentId = parentId;
        ChildIds = childIds;
        _counters = counters;
        _positions = positions;
        _pendingCommands = pendingCommands;
        _orderIntents = orderIntents;
        _strategyTensors = strategyTensors;
        _childSnapshots = childSnapshots;
        Cash = cash;
        AllocationWeight = allocationWeight;
        MaxCapital = maxCapital;
        IsPaused = isPaused;
    }

    public readonly StrategyId StrategyId { get; }
    public readonly StrategyId? ParentId { get; }
    public readonly ReadOnlySpan<StrategyId> ChildIds { get; }
    public readonly Money Cash { get; }
    public readonly decimal AllocationWeight { get; }
    public readonly Money? MaxCapital { get; }
    public readonly bool IsPaused { get; }

    public void Buy(AssetId id, Qty qty, in MarketKernel market)
        => ApplySyntheticFill(id, Side.Buy, qty, market);

    public void Sell(AssetId id, Qty qty, in MarketKernel market)
        => ApplySyntheticFill(id, Side.Sell, qty, market);

    public void Buy(AssetId id, Qty qty, ExecutionSpec execution)
        => AddOrderIntent(id, Side.Buy, qty, execution);

    public void Sell(AssetId id, Qty qty, ExecutionSpec execution)
        => AddOrderIntent(id, Side.Sell, qty, execution);

    public void Flatten(AssetId id, in MarketKernel market)
    {
        var qty = GetPositionQty(id);
        if (qty == 0m) return;
        ApplySyntheticFill(id, qty > 0m ? Side.Sell : Side.Buy, new Qty(Math.Abs(qty)), market);
    }

    public decimal GetPositionQty(AssetId id)
        => Slot(id).Quantity;

    public T GetScalar<T>(VectorField<T> field, AssetId id)
        where T : unmanaged
        => _strategyTensors.GetScalar(field, id.VirtualIndex);

    public void SetScalar<T>(VectorField<T> field, AssetId id, T value)
        where T : unmanaged
        => _strategyTensors.GetScalar(field, id.VirtualIndex) = value;

    public ReadOnlySpan<PortfolioSnapshot> GetChildSnapshots() => _childSnapshots;

    public void SetChildAllocation(StrategyId childId, decimal weight)
        => AddCommand(new AllocationCommand
        {
            TargetStrategy = childId,
            AllocationWeight = weight,
            HasAllocationWeight = true
        });

    public void SetChildMaxCapital(StrategyId childId, Money maxCapital)
        => AddCommand(new AllocationCommand
        {
            TargetStrategy = childId,
            MaxCapitalAmount = maxCapital.Amount,
            HasMaxCapital = true
        });

    public void PauseChild(StrategyId childId)
        => AddCommand(new AllocationCommand { TargetStrategy = childId, Pause = true, HasPause = true });

    public void ResumeChild(StrategyId childId)
        => AddCommand(new AllocationCommand { TargetStrategy = childId, Pause = false, HasPause = true });

    public ReadOnlySpan<AllocationCommand> DrainCommands()
    {
        var count = _counters[PortfolioContext.PendingCommandCounter];
        var commands = _pendingCommands[..count];
        _counters[PortfolioContext.PendingCommandCounter] = 0;
        return commands;
    }

    private ref PositionState Slot(AssetId id)
        => ref _positions[id.VirtualIndex];

    private void AddCommand(AllocationCommand command)
    {
        var count = _counters[PortfolioContext.PendingCommandCounter];
        if (count >= _pendingCommands.Length)
            throw new InvalidOperationException("Allocation command buffer is full.");

        _pendingCommands[count] = command;
        _counters[PortfolioContext.PendingCommandCounter] = count + 1;
    }

    private void AddOrderIntent(AssetId id, Side side, Qty qty, ExecutionSpec execution)
    {
        var count = _counters[PortfolioContext.OrderIntentCounter];
        if (count >= _orderIntents.Length)
            throw new InvalidOperationException("Order intent buffer is full.");

        _orderIntents[count] = new OrderIntent(StrategyId, id, side, qty, execution);
        _counters[PortfolioContext.OrderIntentCounter] = count + 1;
    }

    private void ApplySyntheticFill(AssetId id, Side side, Qty qty, in MarketKernel market)
    {
        var price = new Price((decimal)market.GetScalar(Field.Close, id), market.GetMetadata(id).Currency);
        Slot(id).ApplyFill(side, qty, price, Money.Zero(price.Currency));
    }
}
