using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform;

/// <summary>
/// Stack-only context for group and meta strategies that allocate, pause, and cap child strategies.
/// </summary>
public ref struct GroupContext
{
    private PortfolioContextFrame _portfolio;

    internal GroupContext(ref PortfolioContext portfolio)
    {
        _portfolio = portfolio.AsFrame();
    }

    public StrategyId StrategyId => _portfolio.StrategyId;
    public StrategyId? ParentId => _portfolio.ParentId;
    public ReadOnlySpan<StrategyId> ChildIds => _portfolio.ChildIds;
    public ReadOnlySpan<PortfolioSnapshot> Children => _portfolio.GetChildSnapshots();
    public decimal AllocationWeight => _portfolio.AllocationWeight;
    public Money? MaxCapital => _portfolio.MaxCapital;
    public bool IsPaused => _portfolio.IsPaused;

    public ChildContext Child(int index)
    {
        var children = _portfolio.GetChildSnapshots();
        if ((uint)index >= (uint)children.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        return new ChildContext(children[index]);
    }

    public bool TryGetChild(StrategyId strategyId, out ChildContext child)
    {
        var children = _portfolio.GetChildSnapshots();
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i].StrategyId != strategyId)
                continue;

            child = new ChildContext(children[i]);
            return true;
        }

        child = default;
        return false;
    }

    public void SetAllocation(StrategyId childId, decimal weight)
        => _portfolio.SetChildAllocation(childId, weight);

    public void SetMaxCapital(StrategyId childId, Money maxCapital)
        => _portfolio.SetChildMaxCapital(childId, maxCapital);

    public void Pause(StrategyId childId)
        => _portfolio.PauseChild(childId);

    public void Resume(StrategyId childId)
        => _portfolio.ResumeChild(childId);

    public void Apply(AllocationCommand command)
    {
        if (command.HasAllocationWeight)
            _portfolio.SetChildAllocation(command.TargetStrategy, command.AllocationWeight);
        if (command.HasMaxCapital)
            _portfolio.SetChildMaxCapital(command.TargetStrategy, Money.USD(command.MaxCapitalAmount));
        if (command.HasPause)
        {
            if (command.Pause)
                _portfolio.PauseChild(command.TargetStrategy);
            else
                _portfolio.ResumeChild(command.TargetStrategy);
        }
    }

    public void AllocateEqual()
    {
        var childIds = _portfolio.ChildIds;
        if (childIds.IsEmpty) return;

        var weight = 1m / childIds.Length;
        for (var i = 0; i < childIds.Length; i++)
            _portfolio.SetChildAllocation(childIds[i], weight);
    }

    public void CapGrossExposure(decimal maxGrossExposure)
    {
        var children = _portfolio.GetChildSnapshots();
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i].GrossExposure > maxGrossExposure)
                _portfolio.PauseChild(children[i].StrategyId);
        }
    }

    public void AllocateInverseVolatility()
    {
        var children = _portfolio.GetChildSnapshots();
        if (children.IsEmpty) return;

        Span<decimal> inverseVolatility = stackalloc decimal[children.Length];
        var total = 0m;
        for (var i = 0; i < children.Length; i++)
        {
            var volatility = children[i].RollingStats.Volatility;
            inverseVolatility[i] = volatility > 0m ? 1m / volatility : 0m;
            total += inverseVolatility[i];
        }

        if (total <= 0m)
        {
            AllocateEqual();
            return;
        }

        for (var i = 0; i < children.Length; i++)
            _portfolio.SetChildAllocation(children[i].StrategyId, inverseVolatility[i] / total);
    }
}

public ref struct ChildContext
{
    private readonly PortfolioSnapshot _snapshot;

    internal ChildContext(PortfolioSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public StrategyId StrategyId => _snapshot.StrategyId;
    public Money NetLiquidation => _snapshot.NetLiquidation;
    public Money UnrealizedPnL => _snapshot.UnrealizedPnL;
    public Money RealizedPnL => _snapshot.RealizedPnL;
    public decimal GrossExposure => _snapshot.GrossExposure;
    public decimal NetExposure => _snapshot.NetExposure;
    public RollingStats RollingStats => _snapshot.RollingStats;
    public ReadOnlySpan<Position> Positions => _snapshot.GetPositions();

    public AllocationCommand Allocate(decimal weight)
        => new()
        {
            TargetStrategy = _snapshot.StrategyId,
            AllocationWeight = weight,
            HasAllocationWeight = true
        };

    public AllocationCommand Cap(Money maxCapital)
        => new()
        {
            TargetStrategy = _snapshot.StrategyId,
            MaxCapitalAmount = maxCapital.Amount,
            HasMaxCapital = true
        };

    public AllocationCommand Pause()
        => new() { TargetStrategy = _snapshot.StrategyId, Pause = true, HasPause = true };

    public AllocationCommand Resume()
        => new() { TargetStrategy = _snapshot.StrategyId, Pause = false, HasPause = true };
}
