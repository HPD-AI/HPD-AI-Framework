using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform;

/// <summary>
/// Cold-path strategy setup surface. User strategies register instruments here.
/// </summary>
public readonly ref struct SetupContext
{
    private readonly Strategy _strategy;
    private readonly RhodiumRuntime _runtime;
    private readonly MarketKernel _market;

    internal SetupContext(Strategy strategy, RhodiumRuntime runtime, in MarketKernel market)
    {
        _strategy = strategy;
        _runtime = runtime;
        _market = market;
    }

    public int UniverseSize => _market.UniverseSize;

    public TensorBasis Basis => _market.Basis;

    public AssetId AddEquity(string symbol)
        => _strategy.AddEquityForSetup(symbol);

    public AssetId AddEquity(string symbol, int variantOffset)
        => _strategy.AddEquityForSetup(symbol, variantOffset);

    public AssetId AddInstrument(Instrument instrument, int variantOffset = 0)
        => AddInstrument(Contracts.FromIdentity(instrument, Currency.USD), variantOffset);

    public AssetId AddInstrument(InstrumentContract contract, int variantOffset = 0)
        => _strategy.AddInstrumentForSetup(contract, variantOffset);

    public void ScheduleAt(string name, Instant fireAt)
        => _strategy.AddScheduleForSetup(StrategySchedule.At(name, fireAt));

    public void ScheduleEvery(
        string name,
        Duration interval,
        Instant? startAt = null,
        Instant? stopAt = null)
        => _strategy.AddScheduleForSetup(StrategySchedule.Every(name, interval, startAt, stopAt));
}
