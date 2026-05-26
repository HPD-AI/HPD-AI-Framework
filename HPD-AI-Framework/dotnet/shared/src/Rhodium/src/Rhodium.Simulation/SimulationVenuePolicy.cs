namespace Rhodium.Simulation;

/// <summary>
/// Venue behavior policy for simulated exchanges.
/// </summary>
public sealed record SimulationVenuePolicy
{
    /// <summary>Default venue simulation behavior.</summary>
    public static readonly SimulationVenuePolicy Default = new();

    /// <summary>Whether bar events can drive execution.</summary>
    public bool BarExecution { get; init; } = true;

    /// <summary>Whether trade events can drive execution.</summary>
    public bool TradeExecution { get; init; } = true;

    /// <summary>Whether fills consume displayed liquidity from simulated depth.</summary>
    public bool LiquidityConsumption { get; init; } = true;

    /// <summary>Whether stop-style order types are rejected while the market is open.</summary>
    public bool RejectTriggeredOrdersInMarket { get; init; }

    /// <summary>Whether OCO, OTO, and related contingent order behavior is enabled.</summary>
    public bool SupportContingentOrders { get; init; } = true;

    /// <summary>Whether market orders emit an acknowledgement before fills.</summary>
    public bool UseMarketOrderAcks { get; init; }

    /// <summary>Whether the simulated account may borrow cash.</summary>
    public bool AllowCashBorrowing { get; init; }

    /// <summary>Whether the venue rejects account-affecting order activity as frozen.</summary>
    public bool FrozenAccount { get; init; }

    /// <summary>Maximum protected price distance in ticks; zero disables this guard.</summary>
    public int PriceProtectionTicks { get; init; }

    /// <summary>Whether reduce-only order semantics are enforced.</summary>
    public bool UseReduceOnly { get; init; } = true;

    /// <summary>Whether OTO children require a full parent fill before triggering.</summary>
    public bool OtoFullTrigger { get; init; } = true;
}
