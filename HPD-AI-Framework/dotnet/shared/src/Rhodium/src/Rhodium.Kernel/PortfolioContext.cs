using System.Runtime.InteropServices;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Kernel;

/// <summary>
/// Isolated mutable per-strategy state passed by ref to strategy execution.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public ref struct PortfolioContext
{
    public const int CounterCount = 2;
    internal const int PendingCommandCounter = 0;
    internal const int OrderIntentCounter = 1;

    private Span<PositionState> _positions;
    private Span<OrderState> _workingOrders;
    private Money _cash;
    private ReadOnlySpan<PortfolioSnapshot> _childSnapshots;
    private Span<int> _counters;
    private Span<AllocationCommand> _pendingCommands;
    private Span<OrderIntent> _orderIntents;
    private PagedTensorStore _strategyTensors;

    public readonly StrategyId StrategyId;
    public readonly StrategyId? ParentId;
    public readonly ReadOnlySpan<StrategyId> ChildIds;

    public PortfolioContext(
        StrategyId strategyId,
        StrategyId? parentId,
        ReadOnlySpan<StrategyId> childIds,
        Span<int> counters,
        Span<PositionState> positions,
        Span<OrderState> workingOrders,
        Span<AllocationCommand> pendingCommands,
        PagedTensorStore strategyTensors,
        Money cash,
        decimal allocationWeight,
        Money? maxCapital,
        bool isPaused,
        ReadOnlySpan<PortfolioSnapshot> childSnapshots = default,
        Span<OrderIntent> orderIntents = default)
    {
        StrategyId = strategyId;
        ParentId = parentId;
        ChildIds = childIds;
        if (counters.Length < CounterCount)
            throw new ArgumentException($"Portfolio context counter span must have at least {CounterCount} entries.", nameof(counters));

        _counters = counters[..CounterCount];
        _counters.Clear();
        _positions = positions;
        _workingOrders = workingOrders;
        _pendingCommands = pendingCommands;
        _orderIntents = orderIntents;
        _strategyTensors = strategyTensors;
        _cash = cash;
        _childSnapshots = childSnapshots;
        AllocationWeight = allocationWeight;
        MaxCapital = maxCapital;
        IsPaused = isPaused;
    }

    public Money Cash => _cash;
    public decimal AllocationWeight { get; internal set; }
    public Money? MaxCapital { get; internal set; }
    public bool IsPaused { get; internal set; }

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

    public void EnqueueCommand(AllocationCommand command)
        => AddCommand(command);

    public ReadOnlySpan<AllocationCommand> DrainCommands()
    {
        var count = _counters[PendingCommandCounter];
        var commands = _pendingCommands[..count];
        _counters[PendingCommandCounter] = 0;
        return commands;
    }

    public ReadOnlySpan<OrderIntent> DrainOrderIntents()
    {
        var count = _counters[OrderIntentCounter];
        var intents = _orderIntents[..count];
        _counters[OrderIntentCounter] = 0;
        return intents;
    }

    internal void InjectSnapshots(ReadOnlySpan<PortfolioSnapshot> snapshots)
        => _childSnapshots = snapshots;

    public PortfolioContextFrame AsFrame()
        => new(
            StrategyId,
            ParentId,
            ChildIds,
            _counters,
            _positions,
            _pendingCommands,
            _orderIntents,
            _strategyTensors,
            _cash,
            AllocationWeight,
            MaxCapital,
            IsPaused,
            _childSnapshots);

    private ref PositionState Slot(AssetId id)
        => ref _positions[id.VirtualIndex];

    private void AddCommand(AllocationCommand command)
    {
        var count = _counters[PendingCommandCounter];
        if (count >= _pendingCommands.Length)
            throw new InvalidOperationException("Allocation command buffer is full.");

        _pendingCommands[count] = command;
        _counters[PendingCommandCounter] = count + 1;
    }

    private void AddOrderIntent(AssetId id, Side side, Qty qty, ExecutionSpec execution)
    {
        var count = _counters[OrderIntentCounter];
        if (count >= _orderIntents.Length)
            throw new InvalidOperationException("Order intent buffer is full.");

        _orderIntents[count] = new OrderIntent(StrategyId, id, side, qty, execution);
        _counters[OrderIntentCounter] = count + 1;
    }

    private void ApplySyntheticFill(AssetId id, Side side, Qty qty, in MarketKernel market)
    {
        var price = new Price((decimal)market.GetScalar(Field.Close, id), market.GetMetadata(id).Currency);
        Slot(id).ApplyFill(side, qty, price, Money.Zero(price.Currency));
    }
}
