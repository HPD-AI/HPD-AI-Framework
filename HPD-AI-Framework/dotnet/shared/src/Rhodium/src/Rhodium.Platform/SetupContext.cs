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
    {
        try
        {
            var existing = _runtime.BatchMap.GetInstrumentRange(instrument);
            return _strategy.TrackRegisteredAssetForSetup(new AssetId(existing.Start + variantOffset));
        }
        catch (KeyNotFoundException)
        {
            var variants = Math.Max(variantOffset + 1, 10);
            _runtime.BatchMap.AddInstrument(instrument, variants);
            for (var i = 0; i < variants; i++)
                _runtime.Tensors.Grow();

            var created = _runtime.BatchMap.GetInstrumentRange(instrument);
            return _strategy.TrackRegisteredAssetForSetup(new AssetId(created.Start + variantOffset));
        }
    }
}
