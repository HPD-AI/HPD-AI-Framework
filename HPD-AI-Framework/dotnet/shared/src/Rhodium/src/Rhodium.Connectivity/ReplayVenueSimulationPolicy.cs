namespace Rhodium.Connectivity;

/// <summary>
/// Venue-level replay behavior rules for deterministic matching and account simulation.
/// Separate from order admission policy so exchange shape and simulation behavior stay distinct.
/// </summary>
public sealed record ReplayVenueSimulationPolicy
{
    public static readonly ReplayVenueSimulationPolicy Default = new();

    public bool BarExecution { get; init; } = true;
    public bool TradeExecution { get; init; } = true;
    public bool LiquidityConsumption { get; init; } = true;
    public bool RejectTriggeredOrdersInMarket { get; init; }
    public bool SupportContingentOrders { get; init; } = true;
    public bool UseMarketOrderAcks { get; init; }
    public bool AllowCashBorrowing { get; init; }
    public bool FrozenAccount { get; init; }
    public int PriceProtectionTicks { get; init; }
    public bool UseReduceOnly { get; init; } = true;
    public bool OtoFullTrigger { get; init; } = true;
}
