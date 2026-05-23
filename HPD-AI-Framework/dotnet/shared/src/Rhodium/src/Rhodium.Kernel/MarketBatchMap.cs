using Rhodium.HFT;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Kernel;

/// <summary>
/// Market-state batch map for order-book tensor spaces.
/// </summary>
public sealed class MarketBatchMap : IBatchMap
{
    private readonly MarketTensorSpaceConfig _config;
    private int _version;

    public MarketBatchMap(MarketTensorSpaceConfig? config = null)
    {
        _config = config ?? new MarketTensorSpaceConfig();
    }

    public int Version => _version;
    public int TotalSize => _config.TotalMarketVIs;
    public TensorBasis CurrentBasis => new(0, 0);

    public (int Start, int Length) GetInstrumentRange(Instrument instrument) => (0, 0);
    public (Instrument Inst, int VariantId) GetContext(int virtualIndex) => (Instrument.Unknown, 0);
    public (Instrument Inst, int VariantId) SafeGetContext(int virtualIndex) => (Instrument.Unknown, 0);
}
