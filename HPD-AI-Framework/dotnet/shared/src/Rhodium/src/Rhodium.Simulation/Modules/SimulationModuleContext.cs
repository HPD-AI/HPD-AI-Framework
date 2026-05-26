using HPD.Events;
using Rhodium.Kernel;
using Rhodium.Simulation.Exchange;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Modules;

/// <summary>
/// Controlled session context exposed to simulation modules.
/// </summary>
public ref struct SimulationModuleContext
{
    private readonly RhodiumRuntime _runtime;
    private readonly SimulatedExchangeRegistry _exchanges;

    internal SimulationModuleContext(
        RhodiumRuntime runtime,
        SimulatedExchangeRegistry exchanges,
        IClock clock)
    {
        _runtime = runtime;
        _exchanges = exchanges;
        Clock = clock;
    }

    /// <summary>Authoritative simulation clock for the current replay turn.</summary>
    public IClock Clock { get; }

    /// <summary>Create a strategy-facing market kernel snapshot for module inspection.</summary>
    public MarketKernel Market
        => _runtime.CreateMarketKernel();

    /// <summary>Number of simulated venues currently known to the session.</summary>
    public int VenueCount
        => _exchanges.VenueCount;

    /// <summary>Get a read-only venue view by zero-based index.</summary>
    public SimulationVenueModuleView GetVenue(int index)
    {
        if ((uint)index >= (uint)_exchanges.VenueCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        var current = 0;
        foreach (var exchange in _exchanges.VenueValues)
        {
            if (current == index)
                return SimulationVenueModuleView.FromExchange(exchange);

            current++;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>Try to get a read-only venue view by venue.</summary>
    public bool TryGetVenue(Venue venue, out SimulationVenueModuleView view)
    {
        if (_exchanges.TryGet(venue, out var exchange))
        {
            view = SimulationVenueModuleView.FromExchange(exchange);
            return true;
        }

        view = default;
        return false;
    }

    /// <summary>Try to get a read-only instrument-engine view by instrument.</summary>
    public bool TryGetInstrument(Instrument instrument, out SimulationInstrumentModuleView view)
    {
        if (_exchanges.TryGet(instrument.Venue, out var exchange)
            && exchange.TryGetInstrumentEngine(instrument, out var engine))
        {
            view = SimulationInstrumentModuleView.FromEngine(engine);
            return true;
        }

        view = default;
        return false;
    }
}

/// <summary>
/// Read-only venue state exposed to simulation modules.
/// </summary>
/// <param name="Venue">Venue identity.</param>
/// <param name="Status">Current venue trading status.</param>
/// <param name="AccountType">Venue account type.</param>
/// <param name="BaseCurrency">Venue account base currency.</param>
/// <param name="Cash">Current cash balance.</param>
/// <param name="AvailableCash">Cash available for new reservations.</param>
/// <param name="ReservedCash">Cash reserved for active orders.</param>
/// <param name="PendingSettlement">Cash pending settlement.</param>
/// <param name="PendingSettlementCount">Number of pending settlement entries.</param>
/// <param name="PendingAssetDeliveryQuantity">Quantity pending asset delivery.</param>
/// <param name="PendingAssetDeliveryCount">Number of pending asset-delivery entries.</param>
/// <param name="OrderPolicy">Venue order admission policy.</param>
/// <param name="SimulationPolicy">Venue execution behavior policy.</param>
/// <param name="SubmittedCommands">Number of commands submitted to the venue.</param>
/// <param name="InstrumentCount">Number of instrument engines owned by the venue.</param>
public readonly record struct SimulationVenueModuleView(
    Venue Venue,
    MarketStatus Status,
    AccountType AccountType,
    Currency BaseCurrency,
    Money Cash,
    Money AvailableCash,
    Money ReservedCash,
    Money PendingSettlement,
    int PendingSettlementCount,
    Qty PendingAssetDeliveryQuantity,
    int PendingAssetDeliveryCount,
    SimulationOrderPolicy OrderPolicy,
    SimulationVenuePolicy SimulationPolicy,
    int SubmittedCommands,
    int InstrumentCount)
{
    internal static SimulationVenueModuleView FromExchange(SimulatedVenueExchange exchange)
        => new(
            exchange.Venue,
            exchange.Status,
            exchange.Account.AccountType,
            exchange.Account.Cash.Currency,
            exchange.Account.Cash,
            exchange.Account.AvailableCash,
            exchange.Account.ReservedCash,
            exchange.Account.PendingSettlement,
            exchange.Account.PendingSettlementCount,
            exchange.Account.PendingAssetDeliveryQuantity,
            exchange.Account.PendingAssetDeliveryCount,
            exchange.OrderPolicy,
            exchange.SimulationPolicy,
            exchange.SubmittedCommands,
            exchange.InstrumentCount);
}

/// <summary>
/// Read-only instrument-engine state exposed to simulation modules.
/// </summary>
/// <param name="Instrument">Instrument identity.</param>
/// <param name="Status">Current instrument trading status.</param>
/// <param name="MatchingFidelity">Matching fidelity used by the instrument engine.</param>
/// <param name="OrderPolicy">Instrument order admission policy.</param>
/// <param name="SimulationPolicy">Instrument execution behavior policy.</param>
/// <param name="MarkPrice">Current mark price if available.</param>
/// <param name="CloseMark">Most recent close mark if available.</param>
/// <param name="OpenOrders">Current open order count.</param>
/// <param name="AcceptedOrders">Accepted order count.</param>
/// <param name="RejectedOrders">Rejected order count.</param>
/// <param name="FilledOrders">Filled order count.</param>
/// <param name="CancelledOrders">Cancelled order count.</param>
/// <param name="ExpiredOrders">Expired order count.</param>
public readonly record struct SimulationInstrumentModuleView(
    Instrument Instrument,
    MarketStatus Status,
    MatchingFidelity MatchingFidelity,
    SimulationOrderPolicy OrderPolicy,
    SimulationVenuePolicy SimulationPolicy,
    Price? MarkPrice,
    Price? CloseMark,
    int OpenOrders,
    int AcceptedOrders,
    int RejectedOrders,
    int FilledOrders,
    int CancelledOrders,
    int ExpiredOrders)
{
    internal static SimulationInstrumentModuleView FromEngine(SimulatedInstrumentEngine engine)
    {
        var mark = engine.TryGetMarkPrice(out var price) ? price : (Price?)null;
        return new SimulationInstrumentModuleView(
            engine.Instrument,
            engine.Status,
            engine.MatchingFidelity,
            engine.OrderPolicy,
            engine.SimulationPolicy,
            mark,
            engine.CloseMark,
            engine.OpenOrders,
            engine.AcceptedOrders,
            engine.RejectedOrders,
            engine.FilledOrders,
            engine.CancelledOrders,
            engine.ExpiredOrders);
    }
}
