using System.Globalization;
using HPD.Events;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Platform.Patterns;
using Rhodium.Primitives;
using Rhodium.Simulation;

namespace Rhodium.Connectivity;

/// <summary>
/// Unified Kernel host. Owns strategy registration and drives event processing.
/// </summary>
public sealed class TradingHost : IDisposable
{
    private readonly IReadOnlyList<IConnector> _connectors;
    private readonly IReadOnlyDictionary<Venue, IConnector> _connectorsByVenue;
    private readonly IConnector? _defaultConnector;
    private readonly IEventInboxSource _inboxes;
    private readonly IEventPublisher _publisher;
    private readonly RhodiumRuntime _runtime;
    private readonly StrategyTree _tree = new();
    private readonly StrategyEventProcessor _processor;
    private readonly Dictionary<(Asset Asset, Venue Venue), Quote> _latestQuotes = [];

    public bool UseParallelDispatch { get; set; }
    public int ParallelThreshold { get; set; } = 128;
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    public Duration CrossVenueQuoteMaxAge { get; set; } = Duration.FromSeconds(1);
    public bool UseCrossVenueBestMarketRouting { get; set; }
    public bool UseCrossVenueMarketSweepRouting { get; set; }
    public IReadOnlyDictionary<Venue, FeeParams> CrossVenueRoutingFees { get; set; } = new Dictionary<Venue, FeeParams>();
    public IReadOnlyDictionary<Venue, VenueRoutingPolicy> CrossVenueRoutingPolicies { get; set; } = new Dictionary<Venue, VenueRoutingPolicy>();

    public TradingHost(
        IConnector connector,
        IEventBus events,
        RhodiumRuntime runtime)
        : this([connector], new Dictionary<Venue, IConnector>(), connector, events, runtime)
    {
    }

    public TradingHost(
        IReadOnlyDictionary<Venue, IConnector> connectorsByVenue,
        IEventBus events,
        RhodiumRuntime runtime)
        : this(
            connectorsByVenue.Values.Distinct().ToArray(),
            new Dictionary<Venue, IConnector>(connectorsByVenue),
            defaultConnector: null,
            events,
            runtime)
    {
    }

    private TradingHost(
        IReadOnlyList<IConnector> connectors,
        IReadOnlyDictionary<Venue, IConnector> connectorsByVenue,
        IConnector? defaultConnector,
        IEventBus events,
        RhodiumRuntime runtime)
    {
        if (connectors.Count == 0)
            throw new ArgumentException("At least one connector is required.", nameof(connectors));

        _connectors = connectors;
        _connectorsByVenue = connectorsByVenue;
        _defaultConnector = defaultConnector;
        _inboxes = events;
        _publisher = events;
        _runtime = runtime;
        _processor = new StrategyEventProcessor(runtime, _tree, SubmitOrderIntent);
    }

    public StrategyId RegisterStrategy<TStrategy>(
        int depth,
        IReadOnlyList<StrategyId>? children = null)
        where TStrategy : Strategy, new()
    {
        return _tree.Register(new TStrategy(), depth, children);
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _processor.UseParallelDispatch = UseParallelDispatch;
        _processor.ParallelThreshold = ParallelThreshold;
        _processor.MaxDegreeOfParallelism = MaxDegreeOfParallelism;
        _processor.Initialize();

        await using var inbox = _inboxes.CreateInbox<FinanceEvent>(
            EventInboxOptions.Deterministic());

        var connectorTasks = StartConnectors(ct);
        var connectorsDone = Task.WhenAll(connectorTasks);
        try
        {
            while (true)
            {
                while (inbox.Reader.TryRead(out var evt))
                    ProcessHostEvent(evt);

                if (connectorsDone.IsCompleted)
                    break;

                var waitTask = inbox.Reader.WaitToReadAsync(ct).AsTask();
                var completed = await Task.WhenAny(waitTask, connectorsDone).ConfigureAwait(false);
                if (completed == waitTask && !await waitTask.ConfigureAwait(false))
                    break;
            }

            while (inbox.Reader.TryRead(out var evt))
                ProcessHostEvent(evt);

            await connectorsDone.ConfigureAwait(false);
        }
        catch
        {
            if (connectorsDone.IsCompleted)
                await connectorsDone.ConfigureAwait(false);

            throw;
        }
    }

    private void ProcessHostEvent(FinanceEvent evt)
    {
        ProcessHostMarketDiagnostics(evt);
        _processor.ProcessEvent(evt);
    }

    private void ProcessHostMarketDiagnostics(FinanceEvent evt)
    {
        if (evt is QuoteReceived quote)
            TrackCrossVenueQuote(quote);
    }

    private void TrackCrossVenueQuote(QuoteReceived evt)
    {
        var instrument = evt.Instrument;
        var quote = evt.Quote;
        var key = (instrument.Asset, instrument.Venue);

        if (!IsUsableQuote(quote))
        {
            _latestQuotes.Remove(key);
            return;
        }

        foreach (var ((asset, venue), otherQuote) in _latestQuotes)
        {
            if (asset != instrument.Asset || venue == instrument.Venue || !IsUsableQuote(otherQuote))
                continue;

            if (!IsWithinCrossVenueQuoteAge(quote, otherQuote))
                continue;

            EmitCrossVenueOpportunity(
                asset,
                buyVenue: instrument.Venue,
                sellVenue: venue,
                buyAsk: quote.Ask,
                sellBid: otherQuote.Bid,
                executableQuantity: Min(quote.AskSize, otherQuote.BidSize),
                detectedAt: quote.Time.ExchangeTime);

            EmitCrossVenueOpportunity(
                asset,
                buyVenue: venue,
                sellVenue: instrument.Venue,
                buyAsk: otherQuote.Ask,
                sellBid: quote.Bid,
                executableQuantity: Min(otherQuote.AskSize, quote.BidSize),
                detectedAt: quote.Time.ExchangeTime);
        }

        if (!_latestQuotes.TryGetValue(key, out var latestQuote)
            || quote.Time.ExchangeTime >= latestQuote.Time.ExchangeTime)
        {
            _latestQuotes[key] = quote;
        }
    }

    private void EmitCrossVenueOpportunity(
        Asset asset,
        Venue buyVenue,
        Venue sellVenue,
        Price buyAsk,
        Price sellBid,
        Qty executableQuantity,
        Instant detectedAt)
    {
        if (buyAsk.Currency != sellBid.Currency || buyAsk.Value <= 0m || sellBid.Value <= buyAsk.Value || executableQuantity.Value <= 0m)
            return;

        var spread = sellBid.Value - buyAsk.Value;
        _publisher.Emit(new CrossVenueArbitrageOpportunity(
            asset,
            buyVenue,
            sellVenue,
            buyAsk,
            sellBid,
            executableQuantity,
            new Money(spread, buyAsk.Currency),
            spread / buyAsk.Value * 10_000m,
            detectedAt)
        {
            Time = detectedAt
        });
    }

    private static bool IsUsableQuote(Quote quote)
        => quote.Bid.Currency == quote.Ask.Currency
           && quote.Bid.Value > 0m
           && quote.Ask.Value > 0m
           && quote.Bid.Value <= quote.Ask.Value
           && quote.BidSize.Value > 0m
           && quote.AskSize.Value > 0m;

    private bool IsWithinCrossVenueQuoteAge(Quote current, Quote other)
    {
        if (CrossVenueQuoteMaxAge <= Duration.Zero)
            return false;

        return Abs(current.Time.ExchangeTime - other.Time.ExchangeTime) <= CrossVenueQuoteMaxAge;
    }

    private static Duration Abs(Duration duration)
        => duration.Nanos >= 0 ? duration : new Duration(-duration.Nanos);

    private static Qty Min(Qty left, Qty right)
        => left.Value <= right.Value ? left : right;

    private Task[] StartConnectors(CancellationToken ct)
    {
        var subscriptionsByConnector = BuildSubscriptions()
            .GroupBy(subscription => ResolveConnector(subscription.Instrument))
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());

        return _connectors
            .Select(connector =>
            {
                subscriptionsByConnector.TryGetValue(connector, out var subscriptions);
                return connector.StartAsync(subscriptions ?? [], _publisher, ct);
            })
            .ToArray();
    }

    private void SubmitOrderIntent(in OrderIntent intent, in MarketKernel market)
    {
        if (TryBuildMarketSweepOrders(intent, out var sweepOrders))
        {
            foreach (var sweepOrder in sweepOrders)
                SubmitOrder(sweepOrder);

            return;
        }

        var command = BuildSubmitOrder(intent, in market);
        SubmitOrder(command);
    }

    private void SubmitOrder(SubmitOrder command)
    {
        ResolveConnector(command.Instrument)
            .SubmitOrderAsync(command, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private SubmitOrder BuildSubmitOrder(OrderIntent intent, in MarketKernel market)
    {
        var (instrument, variantId) = ResolveOrderRoute(intent);
        var execution = intent.Execution;
        var limitPrice = ResolveLimitPrice(intent.AssetId, execution, in market);
        var orderType = execution.OrderType;
        if (orderType == OrderType.Limit && !limitPrice.HasValue)
            orderType = OrderType.Market;

        return CreateSubmitOrder(intent, instrument, variantId, intent.Quantity, orderType, limitPrice);
    }

    private SubmitOrder CreateSubmitOrder(
        OrderIntent intent,
        Instrument instrument,
        int variantId,
        Qty quantity,
        OrderType orderType,
        Price? limitPrice)
    {
        var execution = intent.Execution;
        return new SubmitOrder(
            OrderId.New(),
            intent.StrategyId,
            instrument,
            intent.Side,
            quantity,
            orderType,
            LimitPrice: limitPrice,
            StopPrice: execution.StopPrice,
            TimeInForce: execution.TimeInForce,
            VariantId: variantId,
            GoodTilDate: execution.GoodTilDate,
            ExecAlgorithmId: execution.Algorithm switch
            {
                ExecutionAlgorithm.Twap => "TWAP",
                ExecutionAlgorithm.Vwap => "VWAP",
                ExecutionAlgorithm.Pov => "POV",
                _ => null
            },
            ExecAlgorithmParams: BuildExecutionAlgorithmParams(execution),
            PostOnly: execution.PostOnly,
            MaxSlippageTicks: execution.MaxSlippageTicks,
            DisplayQuantity: execution.DisplayQuantity);
    }

    private bool TryBuildMarketSweepOrders(OrderIntent intent, out List<SubmitOrder> orders)
    {
        orders = [];
        if (!UseCrossVenueMarketSweepRouting
            || intent.Execution.OrderType != OrderType.Market
            || intent.Quantity.Value <= 0m)
        {
            return false;
        }

        var (sourceInstrument, variantId) = _runtime.BatchMap.GetContext(intent.AssetId.VirtualIndex);
        var candidates = GetMarketRouteCandidates(sourceInstrument.Asset, variantId, intent.Side, intent.Execution, intent.Quantity);
        if (candidates.Count == 0)
            return false;

        var remaining = intent.Quantity.Value;
        foreach (var candidate in candidates)
        {
            if (remaining <= 0m)
                break;

            var available = SweepAvailableQuantity(candidate.Instrument, intent.Side, candidate.Quote).Value;
            if (available <= 0m)
                continue;

            var slice = Math.Min(remaining, available);
            orders.Add(CreateSubmitOrder(
                intent,
                candidate.Instrument,
                variantId,
                new Qty(slice),
                OrderType.Market,
                limitPrice: null));
            remaining -= slice;
        }

        if (orders.Count == 0)
            return false;

        if (remaining > 0m)
        {
            var residual = orders[0];
            orders[0] = residual with { Quantity = residual.Quantity + new Qty(remaining) };
        }

        return true;
    }

    private (Instrument Instrument, int VariantId) ResolveOrderRoute(OrderIntent intent)
    {
        var (instrument, variantId) = _runtime.BatchMap.GetContext(intent.AssetId.VirtualIndex);
        if (!UseCrossVenueBestMarketRouting || intent.Execution.OrderType != OrderType.Market)
            return (instrument, variantId);

        return TryResolveBestMarketRoute(instrument.Asset, variantId, intent.Side, intent.Execution, intent.Quantity, out var routed)
            ? (routed, variantId)
            : (instrument, variantId);
    }

    private bool TryResolveBestMarketRoute(Asset asset, int variantId, Side side, ExecutionSpec execution, Qty quantity, out Instrument routed)
    {
        routed = default;
        var latest = LatestQuoteTime(asset, variantId);
        if (latest == default)
            return false;

        var found = false;
        Quote bestQuote = default;
        decimal bestScore = 0m;
        for (var i = 0; i < _runtime.BatchMap.TotalSize; i++)
        {
            var (candidate, candidateVariantId) = _runtime.BatchMap.GetContext(i);
            if (candidateVariantId != variantId || candidate.Asset != asset)
                continue;

            if (!CanRouteSmart(candidate, quantity, execution, isSweep: false)
                || !_latestQuotes.TryGetValue((candidate.Asset, candidate.Venue), out var quote)
                || !IsUsableQuote(quote)
                || !IsWithinCrossVenueQuoteAge(latest, quote)
                || !MeetsMinMarketRoutingNotional(candidate, side, quantity, quote))
            {
                continue;
            }

            if (found && quote.Bid.Currency != bestQuote.Bid.Currency)
                continue;

            var score = MarketRoutingScore(candidate, side, quantity, quote);
            if (!found || IsBetterMarketScore(side, score, bestScore))
            {
                found = true;
                routed = candidate;
                bestQuote = quote;
                bestScore = score;
            }
        }

        return found;
    }

    private List<MarketRouteCandidate> GetMarketRouteCandidates(
        Asset asset,
        int variantId,
        Side side,
        ExecutionSpec execution,
        Qty quantity)
    {
        var latest = LatestQuoteTime(asset, variantId);
        if (latest == default)
            return [];

        var candidates = new List<MarketRouteCandidate>();
        Currency? currency = null;
        for (var i = 0; i < _runtime.BatchMap.TotalSize; i++)
        {
            var (candidate, candidateVariantId) = _runtime.BatchMap.GetContext(i);
            if (candidateVariantId != variantId || candidate.Asset != asset)
                continue;

            if (!CanRouteSmart(candidate, quantity, execution, isSweep: true)
                || !_latestQuotes.TryGetValue((candidate.Asset, candidate.Venue), out var quote)
                || !IsUsableQuote(quote)
                || !IsWithinCrossVenueQuoteAge(latest, quote)
                || SweepAvailableQuantity(candidate, side, quote) is not { Value: > 0m } available
                || !MeetsMinMarketRoutingNotional(candidate, side, available, quote))
            {
                continue;
            }

            currency ??= quote.Bid.Currency;
            if (quote.Bid.Currency != currency.Value)
                continue;

            candidates.Add(new MarketRouteCandidate(
                candidate,
                quote,
                MarketRoutingScore(candidate, side, quantity, quote)));
        }

        candidates.Sort((left, right) => side == Side.Buy
            ? left.Score.CompareTo(right.Score)
            : right.Score.CompareTo(left.Score));
        return candidates;
    }

    private Instant LatestQuoteTime(Asset asset, int variantId)
    {
        var latest = default(Instant);
        for (var i = 0; i < _runtime.BatchMap.TotalSize; i++)
        {
            var (candidate, candidateVariantId) = _runtime.BatchMap.GetContext(i);
            if (candidateVariantId != variantId || candidate.Asset != asset)
                continue;

            if (_latestQuotes.TryGetValue((candidate.Asset, candidate.Venue), out var quote)
                && IsUsableQuote(quote)
                && quote.Time.ExchangeTime > latest)
            {
                latest = quote.Time.ExchangeTime;
            }
        }

        return latest;
    }

    private bool IsWithinCrossVenueQuoteAge(Instant latest, Quote quote)
    {
        if (CrossVenueQuoteMaxAge <= Duration.Zero)
            return false;

        return latest >= quote.Time.ExchangeTime && latest - quote.Time.ExchangeTime <= CrossVenueQuoteMaxAge;
    }

    private decimal MarketRoutingScore(Instrument instrument, Side side, Qty quantity, Quote quote)
    {
        var price = side == Side.Buy ? quote.Ask : quote.Bid;
        var fee = CrossVenueRoutingFees.TryGetValue(instrument.Venue, out var fees)
            ? fees.Calculate(quantity, price, side, isMaker: false)
            : Money.Zero(price.Currency);

        if (fee.Currency != price.Currency)
            return side == Side.Buy ? decimal.MaxValue : decimal.MinValue;

        return side == Side.Buy
            ? price.Value * quantity.Value + fee.Amount
            : price.Value * quantity.Value - fee.Amount;
    }

    private bool MeetsMinMarketRoutingNotional(Instrument instrument, Side side, Qty quantity, Quote quote)
    {
        var policy = GetRoutingPolicy(instrument.Venue);
        if (policy.MinMarketRoutingNotional is not { } minimum || minimum.Amount <= 0m)
            return true;

        var price = side == Side.Buy ? quote.Ask : quote.Bid;
        return price.Currency == minimum.Currency
               && price.Value * quantity.Value >= minimum.Amount;
    }

    private static bool IsBetterMarketScore(Side side, decimal candidate, decimal current)
        => side == Side.Buy
            ? candidate < current
            : candidate > current;

    private static Qty AvailableTopOfBookQuantity(Side side, Quote quote)
        => side == Side.Buy ? quote.AskSize : quote.BidSize;

    private Qty SweepAvailableQuantity(Instrument instrument, Side side, Quote quote)
    {
        var available = AvailableTopOfBookQuantity(side, quote);
        var policy = GetRoutingPolicy(instrument.Venue);
        if (policy.MaxMarketSweepQuantity is { } max && max.Value >= 0m && max < available)
            return max;

        return available;
    }

    private bool CanRouteSmart(Instrument instrument, Qty quantity, ExecutionSpec execution, bool isSweep)
    {
        if (_defaultConnector is null && !_connectorsByVenue.ContainsKey(instrument.Venue))
            return false;

        var policy = GetRoutingPolicy(instrument.Venue);
        if (!AllowsMarketTimeInForce(policy, execution.TimeInForce))
            return false;

        if (policy.MinMarketRoutingQuantity is { } min && quantity < min)
            return false;

        if (isSweep)
            return policy.AllowMarketSweepRouting;

        if (!policy.AllowBestVenueMarketRouting)
            return false;

        return policy.MaxMarketSweepQuantity is not { } max || max.Value < 0m || quantity <= max;
    }

    private static bool AllowsMarketTimeInForce(VenueRoutingPolicy policy, TimeInForce timeInForce)
        => policy.AllowedMarketTimeInForce is null
           || policy.AllowedMarketTimeInForce.Contains(timeInForce);

    private VenueRoutingPolicy GetRoutingPolicy(Venue venue)
        => CrossVenueRoutingPolicies.TryGetValue(venue, out var policy)
            ? policy
            : VenueRoutingPolicy.Default;

    private readonly record struct MarketRouteCandidate(
        Instrument Instrument,
        Quote Quote,
        decimal Score);

    private static IReadOnlyDictionary<string, string>? BuildExecutionAlgorithmParams(ExecutionSpec execution)
    {
        if (execution.Algorithm == ExecutionAlgorithm.None)
            return null;

        var parameters = new Dictionary<string, string>(capacity: 3);
        if (execution.Horizon > Duration.Zero)
            parameters["horizon_secs"] = Seconds(execution.Horizon);
        if (execution.Interval > Duration.Zero)
            parameters["interval_secs"] = Seconds(execution.Interval);
        if (execution.ParticipationRate > 0m)
            parameters["participation_rate"] = execution.ParticipationRate.ToString(CultureInfo.InvariantCulture);

        return parameters;
    }

    private static string Seconds(Duration duration)
        => Math.Ceiling(duration.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    private static Price? ResolveLimitPrice(AssetId id, ExecutionSpec execution, in MarketKernel market)
    {
        if (execution.LimitPrice.HasValue)
            return execution.LimitPrice;

        var metadata = market.GetMetadata(id);
        return execution.LimitPriceMode switch
        {
            ExecutionLimitPriceMode.Bid => ResolveTickPrice(market.GetBestBidTick(id), metadata),
            ExecutionLimitPriceMode.Ask => ResolveTickPrice(market.GetBestAskTick(id), metadata),
            ExecutionLimitPriceMode.Mid => ResolveMidPrice(id, in market, metadata),
            _ => null
        };
    }

    private static Price? ResolveTickPrice(long? tick, SecurityMetadata metadata)
        => tick.HasValue ? new Price(tick.Value * metadata.TickSize, metadata.Currency) : null;

    private static Price? ResolveMidPrice(AssetId id, in MarketKernel market, SecurityMetadata metadata)
    {
        var bid = market.GetBestBidTick(id);
        var ask = market.GetBestAskTick(id);
        return bid.HasValue && ask.HasValue
            ? new Price(((bid.Value + ask.Value) * metadata.TickSize) / 2m, metadata.Currency)
            : null;
    }

    private IEnumerable<Subscription> BuildSubscriptions()
    {
        var seen = new HashSet<Instrument>();
        for (var i = 0; i < _runtime.BatchMap.TotalSize; i++)
        {
            var (instrument, variantId) = _runtime.BatchMap.GetContext(i);
            if (variantId != 0 || !seen.Add(instrument)) continue;

            yield return new Subscription(instrument, SubscriptionType.Trades);
            yield return new Subscription(instrument, SubscriptionType.Quotes);
            yield return new Subscription(instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20);
            yield return new Subscription(instrument, SubscriptionType.Bars);
        }
    }

    private IConnector ResolveConnector(Instrument instrument)
    {
        if (_defaultConnector is not null)
            return _defaultConnector;

        if (_connectorsByVenue.TryGetValue(instrument.Venue, out var connector))
            return connector;

        throw new InvalidOperationException($"No connector is registered for venue {instrument.Venue}.");
    }

    public void Dispose()
    {
        _processor.Dispose();
        foreach (var connector in _connectors)
            connector.Dispose();
    }
}
