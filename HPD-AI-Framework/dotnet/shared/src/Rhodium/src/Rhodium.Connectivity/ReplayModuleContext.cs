using Rhodium.Events;
using Rhodium.HFT;
using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Read-only connector context exposed to replay simulation modules.
/// </summary>
public sealed class ReplayModuleContext
{
    private readonly ReplayConnector _connector;

    internal ReplayModuleContext(ReplayConnector connector)
    {
        _connector = connector;
    }

    public Instant Now => _connector.CurrentReplayTime;

    public MarketStatus GetMarketStatus(Instrument instrument)
        => _connector.GetEffectiveMarketStatusForModule(instrument);

    public IHftDepth? GetDepth(Instrument instrument)
        => _connector.GetDepthForModule(instrument);
}

