using Rhodium.Events;
using Rhodium.Primitives;
using Rhodium.Simulation.Frames;
using Rhodium.Simulation.Identity;

namespace Rhodium.Simulation.Exchange;

/// <summary>
/// Routes replay data and simulation commands to venue-scoped simulated exchanges.
/// </summary>
public sealed class SimulatedExchangeRegistry
{
    private readonly Dictionary<Venue, SimulatedVenueExchange> _venues = [];
    private readonly Dictionary<Venue, SimulationVenueConfig> _venueConfigs;
    private readonly SimulationConfig _defaultConfig;
    private readonly Money _initialCash;
    private readonly MatchingFidelity _defaultMatchingFidelity;
    private readonly SimulationIdentityGenerator _identity;
    private readonly bool _processZeroLatencyCommandsImmediately;
    private readonly List<AccountStatementSnapshot> _accountTransferScratch = [];
    private readonly Dictionary<Instrument, SettlementReferencePricePublished> _latestSettlementReferences = [];
    private readonly Dictionary<Instrument, (MarketEvent Event, Instant Time)> _latestMarketMarks = [];
    private readonly Dictionary<SimulationOptionAssignmentKey, OptionAssignmentNoticePublished> _latestAssignmentNotices = [];

    /// <summary>Create a registry with default venue construction settings.</summary>
    public SimulatedExchangeRegistry(
        SimulationConfig defaultConfig,
        Money initialCash,
        MatchingFidelity defaultMatchingFidelity = MatchingFidelity.QueueAccurate,
        IReadOnlyList<SimulationVenueConfig>? venueConfigs = null,
        SimulationIdentityGenerator? identity = null,
        bool processZeroLatencyCommandsImmediately = true)
    {
        _defaultConfig = defaultConfig;
        _initialCash = initialCash;
        _defaultMatchingFidelity = defaultMatchingFidelity;
        _identity = identity ?? new SimulationIdentityGenerator();
        _processZeroLatencyCommandsImmediately = processZeroLatencyCommandsImmediately;
        _venueConfigs = [];
        if (venueConfigs is null)
            return;

        for (var i = 0; i < venueConfigs.Count; i++)
        {
            var config = venueConfigs[i];
            if (!_venueConfigs.TryAdd(config.Venue, config))
                throw new InvalidOperationException($"Duplicate simulation venue config for venue {config.Venue}.");
        }
    }

    /// <summary>Add or replace a simulated venue exchange.</summary>
    public void AddVenue(SimulatedVenueExchange exchange)
    {
        exchange.ProcessZeroLatencyCommandsImmediately = _processZeroLatencyCommandsImmediately;
        HydrateLifecycleContext(exchange);
        _venues[exchange.Venue] = exchange;
    }

    /// <summary>Register an instrument contract with the owning simulated venue.</summary>
    public void RegisterContract(InstrumentContract contract)
        => GetOrCreate(contract.Instrument.Venue).RegisterContract(contract);

    /// <summary>Try to get a previously created venue exchange.</summary>
    public bool TryGet(Venue venue, out SimulatedVenueExchange exchange)
        => _venues.TryGetValue(venue, out exchange!);

    /// <summary>Get a configured venue exchange or throw when no exchange exists.</summary>
    public SimulatedVenueExchange GetRequired(Venue venue)
        => _venues.TryGetValue(venue, out var exchange)
            ? exchange
            : throw new InvalidOperationException($"No simulated exchange has been configured for venue {venue}.");

    /// <summary>Get or lazily create the venue exchange using run and venue configuration.</summary>
    public SimulatedVenueExchange GetOrCreate(Venue venue)
    {
        if (_venues.TryGetValue(venue, out var exchange))
            return exchange;

        var venueConfig = _venueConfigs.TryGetValue(venue, out var configured)
            ? configured
            : SimulationVenueConfig.For(venue);
        var config = venueConfig.Config ?? _defaultConfig;
        if (venueConfig.AccountType.HasValue)
            config = config with { AccountType = venueConfig.AccountType.Value };

        var initialCash = venueConfig.InitialCash
            ?? (venueConfig.BaseCurrency.HasValue
                ? new Money(_initialCash.Amount, venueConfig.BaseCurrency.Value)
                : _initialCash);
        var matchingFidelity = venueConfig.MatchingFidelity ?? _defaultMatchingFidelity;
        exchange = new SimulatedVenueExchange(
            venue,
            config,
            initialCash,
            matchingFidelity,
            _identity,
            venueConfig.OrderPolicy,
            venueConfig.SimulationPolicy,
            venueConfig.InstrumentConfigs);
        exchange.ProcessZeroLatencyCommandsImmediately = _processZeroLatencyCommandsImmediately;
        HydrateLifecycleContext(exchange);
        _venues[venue] = exchange;
        return exchange;
    }

    /// <summary>Number of currently created simulated venue exchanges.</summary>
    public int VenueCount => _venues.Count;

    internal Dictionary<Venue, SimulatedVenueExchange>.ValueCollection VenueValues
        => _venues.Values;

    /// <summary>Route one semantic replay event to the owning venue exchange.</summary>
    public void OnMarketEvent(FinanceEvent evt)
    {
        switch (evt)
        {
            case VenueStatusChanged venue:
                GetOrCreate(venue.Venue).OnMarketEvent(evt);
                break;
            case MarketOpened opened:
                GetOrCreate(opened.Venue).OnMarketEvent(evt);
                break;
            case MarketClosed closed:
                GetOrCreate(closed.Venue).OnMarketEvent(evt);
                break;
            case PreMarketOpened preMarket:
                GetOrCreate(preMarket.Venue).OnMarketEvent(evt);
                break;
            case PostMarketOpened postMarket:
                GetOrCreate(postMarket.Venue).OnMarketEvent(evt);
                break;
            case CorporateActionApplied corporateAction:
                GetOrCreate(corporateAction.Instrument.Venue).OnMarketEvent(evt);
                break;
            case SettlementReferencePricePublished settlementReference:
                var owningSettlementVenue = GetOrCreate(settlementReference.Instrument.Venue);
                owningSettlementVenue.OnMarketEvent(evt);
                _latestSettlementReferences[settlementReference.Instrument] = settlementReference;
                ObserveSettlementReferenceOnOtherVenues(settlementReference, owningSettlementVenue);
                break;
            case OptionAssignmentNoticePublished assignmentNotice:
                GetOrCreate(assignmentNotice.Instrument.Venue).OnMarketEvent(evt);
                _latestAssignmentNotices[new SimulationOptionAssignmentKey(
                    assignmentNotice.StrategyId,
                    assignmentNotice.VariantId,
                    assignmentNotice.Instrument)] = assignmentNotice;
                break;
            case AccountTransferCompleted transfer when TryResolveTransferVenue(transfer, out var venue):
                _accountTransferScratch.Clear();
                GetOrCreate(venue).ApplyAccountTransfer(transfer, GetEventTime(transfer), _accountTransferScratch);
                break;
            case MarketEvent market:
                var marketTime = GetMarketEventTime(market);
                var owningVenue = GetOrCreate(market.Instrument.Venue);
                owningVenue.OnMarketEvent(evt);
                if (HasMarketMark(market))
                    _latestMarketMarks[market.Instrument] = (market, marketTime);
                ObserveMarketMarkOnOtherVenues(market, owningVenue, marketTime);
                break;
        }
    }

    private void HydrateLifecycleContext(SimulatedVenueExchange exchange)
    {
        foreach (var settlementReference in _latestSettlementReferences.Values)
            exchange.ObserveSettlementReference(settlementReference, settlementReference.EffectiveAt);

        foreach (var (market, time) in _latestMarketMarks.Values)
            exchange.ObserveMarketMark(market, time);

        foreach (var assignmentNotice in _latestAssignmentNotices.Values)
        {
            if (assignmentNotice.Instrument.Venue == exchange.Venue)
                exchange.ObserveAssignmentNotice(assignmentNotice, assignmentNotice.EffectiveAt);
        }
    }

    private void ObserveSettlementReferenceOnOtherVenues(
        SettlementReferencePricePublished settlementReference,
        SimulatedVenueExchange owningVenue)
    {
        foreach (var exchange in _venues.Values)
        {
            if (ReferenceEquals(exchange, owningVenue))
                continue;

            exchange.ObserveSettlementReference(settlementReference, settlementReference.EffectiveAt);
        }
    }

    private void ObserveMarketMarkOnOtherVenues(
        MarketEvent market,
        SimulatedVenueExchange owningVenue,
        Instant now)
    {
        foreach (var exchange in _venues.Values)
        {
            if (ReferenceEquals(exchange, owningVenue))
                continue;

            exchange.ObserveMarketMark(market, now);
        }
    }

    /// <summary>Route one frame-native L3 order-add event to the owning venue exchange.</summary>
    public void OnBookOrderAdded(Instrument instrument, in BookOrderAddedFrame frame, bool allowMatching = true)
        => GetOrCreate(instrument.Venue).OnBookOrderAdded(instrument, in frame, allowMatching);

    /// <summary>Route one frame-native L3 order-modify event to the owning venue exchange.</summary>
    public void OnBookOrderModified(Instrument instrument, in BookOrderModifiedFrame frame, bool allowMatching = true)
        => GetOrCreate(instrument.Venue).OnBookOrderModified(instrument, in frame, allowMatching);

    /// <summary>Route one frame-native L3 order-delete event to the owning venue exchange.</summary>
    public void OnBookOrderDeleted(Instrument instrument, in BookOrderDeletedFrame frame, bool allowMatching = true)
        => GetOrCreate(instrument.Venue).OnBookOrderDeleted(instrument, in frame, allowMatching);

    /// <summary>Route one frame-native L3 order-execute event to the owning venue exchange.</summary>
    public void OnBookOrderExecuted(Instrument instrument, in BookOrderExecutedFrame frame, bool allowMatching = true)
        => GetOrCreate(instrument.Venue).OnBookOrderExecuted(instrument, in frame, allowMatching);

    /// <summary>Submit an order command to the command heap of its venue.</summary>
    public void Submit(in SimulationOrderCommand command, Instant now)
        => GetOrCreate(command.Venue).Submit(in command, now);

    /// <summary>Submit a cancel command to the command heap of its venue.</summary>
    public void Cancel(in SimulationCancelCommand command, Instant now)
        => GetOrCreate(command.Venue).Cancel(in command, now);

    /// <summary>Submit a modify command to the command heap of its venue.</summary>
    public void Modify(in SimulationModifyCommand command, Instant now)
        => GetOrCreate(command.Venue).Modify(in command, now);

    /// <summary>Return true when any venue has due commands or pending output at the supplied time.</summary>
    public bool HasDueWork(Instant now)
    {
        foreach (var exchange in _venues.Values)
        {
            if (exchange.HasDueWork(now))
                return true;
        }

        return false;
    }

    /// <summary>Process all due work across created venues at the supplied time.</summary>
    public void DrainDueWork(Instant now)
    {
        foreach (var exchange in _venues.Values)
            exchange.DrainDueWork(now);
    }

    /// <summary>Complete replay for all created venues and cancel unfinished replay-scoped work.</summary>
    public void CompleteReplay(Instant now)
    {
        foreach (var exchange in _venues.Values)
            exchange.CompleteReplay(now);
    }

    /// <summary>Apply a financing event to the venue implied by its instrument.</summary>
    public bool TryApplyFinancing(
        FinancingChargeApplied financing,
        Instant now,
        out AccountStatementSnapshot statement)
    {
        if (financing.Instrument is { } instrument)
        {
            statement = GetOrCreate(instrument.Venue).ApplyFinancing(financing, now);
            return true;
        }

        statement = default!;
        return false;
    }

    /// <summary>Apply an account transfer to its explicit or inferred venue.</summary>
    internal bool TryApplyAccountTransfer(
        AccountTransferCompleted transfer,
        Instant now,
        List<AccountStatementSnapshot> statements,
        out int statementCount)
    {
        if (!TryResolveTransferVenue(transfer, out var venue))
        {
            statementCount = 0;
            return false;
        }

        statementCount = GetOrCreate(venue).ApplyAccountTransfer(transfer, now, statements);
        return true;
    }

    public bool TryCreateAccountStatement(
        OrderFilled fill,
        Instant now,
        out AccountStatementSnapshot statement)
    {
        if (!_venues.TryGetValue(fill.Instrument.Venue, out var exchange))
        {
            statement = default!;
            return false;
        }

        statement = exchange.CreateAccountStatement(
            fill.StrategyId,
            fill.VariantId,
            fill.FillPrice.Currency,
            now);
        return true;
    }

    public bool TryCreateAccountStatement(
        OptionLifecycleApplied lifecycle,
        Instant now,
        out AccountStatementSnapshot statement)
    {
        if (!_venues.TryGetValue(lifecycle.Instrument.Venue, out var exchange))
        {
            statement = default!;
            return false;
        }

        statement = exchange.CreateAccountStatement(
            lifecycle.StrategyId,
            lifecycle.VariantId,
            lifecycle.CashFlow.Currency,
            now);
        return true;
    }

    public bool TryCreatePerformanceSnapshot(
        OrderFilled fill,
        Instant now,
        out PerformanceSnapshot snapshot)
    {
        if (!_venues.TryGetValue(fill.Instrument.Venue, out var exchange))
        {
            snapshot = default!;
            return false;
        }

        snapshot = exchange.CreatePerformanceSnapshot(
            fill.StrategyId,
            fill.VariantId,
            fill.FillPrice.Currency,
            now);
        return true;
    }

    /// <summary>Drain exchange execution events into a caller-owned buffer.</summary>
    public int DrainExecutionEvents(Span<ExecutionEvent> destination)
    {
        var written = 0;
        foreach (var exchange in _venues.Values)
        {
            if (written >= destination.Length)
                break;

            written += exchange.DrainExecutionEvents(destination[written..]);
        }

        return written;
    }

    /// <summary>Drain non-execution simulator events into a caller-owned buffer.</summary>
    public int DrainSimulationEvents(Span<FinanceEvent> destination)
    {
        var written = 0;
        foreach (var exchange in _venues.Values)
        {
            if (written >= destination.Length)
                break;

            written += exchange.DrainSimulationEvents(destination[written..]);
        }

        return written;
    }

    /// <summary>Emit pending account lifecycle status events from all venues.</summary>
    public void EmitPendingAccountLifecycleStatuses(Instant now)
    {
        foreach (var exchange in _venues.Values)
            exchange.EmitPendingAccountLifecycleStatuses(now);
    }

    private bool TryResolveTransferVenue(AccountTransferCompleted transfer, out Venue venue)
    {
        if (transfer.Instrument is { } instrument)
        {
            venue = instrument.Venue;
            return true;
        }

        if (transfer.Venue.HasValue)
        {
            venue = transfer.Venue.Value;
            return true;
        }

        if (_venues.Count == 1)
        {
            venue = _venues.Keys.Single();
            return true;
        }

        venue = default;
        return false;
    }

    private static Instant GetEventTime(AccountTransferCompleted transfer)
        => transfer.CompletedAt == default ? transfer.Time : transfer.CompletedAt;

    private static Instant GetMarketEventTime(MarketEvent evt)
        => evt switch
        {
            QuoteReceived quote => quote.Quote.Time.ExchangeTime,
            TradeOccurred trade => trade.Trade.Time.ExchangeTime,
            BarClosed bar => bar.Bar.Time,
            BookSnapshotReceived book => book.Book.Time,
            BookDepth10Received depth => depth.Time,
            _ => evt.Time
        };

    private static bool HasMarketMark(MarketEvent evt)
        => evt is QuoteReceived or TradeOccurred or BarClosed or BookSnapshotReceived;
}
