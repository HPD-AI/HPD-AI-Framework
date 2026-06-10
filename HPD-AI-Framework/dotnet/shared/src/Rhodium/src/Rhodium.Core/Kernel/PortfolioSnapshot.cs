using Rhodium.Primitives;

namespace Rhodium.Kernel;

public readonly struct RollingStats
{
    public double SharpeRatio { get; init; }
    public decimal Volatility { get; init; }
}

public readonly struct PortfolioSnapshot
{
    private readonly Position[] _positions;
    private readonly int _positionCount;

    public StrategyId StrategyId { get; init; }
    public Money NetLiquidation { get; init; }
    public Money UnrealizedPnL { get; init; }
    public Money RealizedPnL { get; init; }
    public decimal GrossExposure { get; init; }
    public decimal NetExposure { get; init; }
    public RollingStats RollingStats { get; init; }

    public PortfolioSnapshot()
    {
        _positions = [];
        _positionCount = 0;
        NetLiquidation = Money.Zero(Currency.USD);
        UnrealizedPnL = Money.Zero(Currency.USD);
        RealizedPnL = Money.Zero(Currency.USD);
    }

    public PortfolioSnapshot(
        StrategyId strategyId,
        Money netLiquidation,
        Money unrealizedPnL,
        Money realizedPnL,
        decimal grossExposure,
        decimal netExposure,
        RollingStats rollingStats,
        Position[] positions,
        int positionCount)
    {
        StrategyId = strategyId;
        NetLiquidation = netLiquidation;
        UnrealizedPnL = unrealizedPnL;
        RealizedPnL = realizedPnL;
        GrossExposure = grossExposure;
        NetExposure = netExposure;
        RollingStats = rollingStats;
        _positions = positions;
        _positionCount = positionCount;
    }

    public ReadOnlySpan<Position> GetPositions() => _positions.AsSpan(0, _positionCount);
}
