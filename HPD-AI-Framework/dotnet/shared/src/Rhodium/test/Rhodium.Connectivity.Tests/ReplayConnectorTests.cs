using HPD.Events;
using HPD.Events.Core;
using Rhodium.Simulation;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests;

public class ReplayConnectorTests
{
    private static Instrument TestInstrument => new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    private static async IAsyncEnumerable<FinanceEvent> CreateEmptyHistory()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistory(params FinanceEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithQuotes(int count)
    {
        var time = DualTimestamp.Synchronized(Instant.Now);
        for (int i = 0; i < count; i++)
        {
            yield return new QuoteReceived(
                TestInstrument,
                new Quote(
                    new Price(100m + i * 0.01m, Currency.USD),
                    new Price(100.05m + i * 0.01m, Currency.USD),
                    new Qty(100m),
                    new Qty(100m),
                    time));
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithQuotes(params (decimal Bid, decimal Ask)[] quotes)
    {
        foreach (var (bid, ask) in quotes)
        {
            yield return new QuoteReceived(
                TestInstrument,
                new Quote(
                    new Price(bid, Currency.USD),
                    new Price(ask, Currency.USD),
                    new Qty(100m),
                    new Qty(100m),
                    DualTimestamp.Synchronized(Instant.Now)));
            await Task.Yield();
        }
    }

    private static QuoteReceived QuoteAt(Instrument instrument, Instant time, decimal bid = 100m, decimal ask = 101m) =>
        new(
            instrument,
            new Quote(
                new Price(bid, Currency.USD),
                new Price(ask, Currency.USD),
                new Qty(100m),
                new Qty(100m),
                DualTimestamp.Synchronized(time)))
        {
            Time = time
        };

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithTimedQuotes(
        params (Instant Time, decimal Bid, decimal Ask)[] quotes)
    {
        foreach (var (time, bid, ask) in quotes)
        {
            yield return new QuoteReceived(
                TestInstrument,
                new Quote(
                    new Price(bid, Currency.USD),
                    new Price(ask, Currency.USD),
                    new Qty(100m),
                    new Qty(100m),
                    DualTimestamp.Synchronized(time)))
            {
                Time = time
            };
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithBook()
    {
        yield return new BookSnapshotReceived(
            TestInstrument,
            new Book
            {
                Instrument = TestInstrument,
                Time = Instant.Now,
                Bids =
                [
                    new Level(new Price(99.99m, Currency.USD), new Qty(50m)),
                    new Level(new Price(99.98m, Currency.USD), new Qty(75m))
                ],
                Asks =
                [
                    new Level(new Price(100.01m, Currency.USD), new Qty(40m)),
                    new Level(new Price(100.02m, Currency.USD), new Qty(80m))
                ]
            });
        await Task.Yield();
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithBooks(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new BookSnapshotReceived(
                TestInstrument,
                new Book
                {
                    Instrument = TestInstrument,
                    Time = Instant.Now + Duration.FromMinutes(i),
                    Bids =
                    [
                        new Level(new Price(99.99m, Currency.USD), new Qty(50m)),
                        new Level(new Price(99.98m, Currency.USD), new Qty(75m))
                    ],
                    Asks =
                    [
                        new Level(new Price(100.01m, Currency.USD), new Qty(40m)),
                        new Level(new Price(100.02m, Currency.USD), new Qty(80m))
                    ]
                });
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithQuoteThenBar(
        decimal open,
        decimal high,
        decimal low,
        decimal close)
    {
        var time = DualTimestamp.Synchronized(Instant.Now);
        yield return new QuoteReceived(
            TestInstrument,
            new Quote(
                new Price(100m, Currency.USD),
                new Price(100.05m, Currency.USD),
                new Qty(100m),
                new Qty(100m),
                time));

        yield return new BarClosed(
            TestInstrument,
            new Bar(
                new Price(open, Currency.USD),
                new Price(high, Currency.USD),
                new Price(low, Currency.USD),
                new Price(close, Currency.USD),
                new Qty(1000m),
                Instant.Now,
                Duration.FromMinutes(1)));
        await Task.Yield();
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithQuoteThenBars(
        params (decimal Open, decimal High, decimal Low, decimal Close)[] bars)
    {
        var time = DualTimestamp.Synchronized(Instant.Now);
        yield return new QuoteReceived(
            TestInstrument,
            new Quote(
                new Price(100m, Currency.USD),
                new Price(100.05m, Currency.USD),
                new Qty(100m),
                new Qty(100m),
                time));

        foreach (var (open, high, low, close) in bars)
        {
            yield return new BarClosed(
                TestInstrument,
                new Bar(
                    new Price(open, Currency.USD),
                    new Price(high, Currency.USD),
                    new Price(low, Currency.USD),
                    new Price(close, Currency.USD),
                    new Qty(1000m),
                    Instant.Now,
                    Duration.FromMinutes(1)));
        }

        await Task.Yield();
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithQuoteThenBarAt(
        Instant quoteTime,
        Instant barTime,
        decimal open,
        decimal high,
        decimal low,
        decimal close)
    {
        yield return new QuoteReceived(
            TestInstrument,
            new Quote(
                new Price(100m, Currency.USD),
                new Price(100.05m, Currency.USD),
                new Qty(100m),
                new Qty(100m),
                DualTimestamp.Synchronized(quoteTime)))
        {
            Time = quoteTime
        };

        yield return new BarClosed(
            TestInstrument,
            new Bar(
                new Price(open, Currency.USD),
                new Price(high, Currency.USD),
                new Price(low, Currency.USD),
                new Price(close, Currency.USD),
                new Qty(1000m),
                barTime,
                Duration.FromMinutes(1)))
        {
            Time = barTime
        };
        await Task.Yield();
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithQuoteThenTrades(params decimal[] tradeSizes)
    {
        var time = DualTimestamp.Synchronized(Instant.Now);
        yield return new QuoteReceived(
            TestInstrument,
            new Quote(
                new Price(100m, Currency.USD),
                new Price(100.05m, Currency.USD),
                new Qty(100m),
                new Qty(100m),
                time));

        foreach (var size in tradeSizes)
        {
            yield return new TradeOccurred(
                TestInstrument,
                new Trade(
                    new Price(100m, Currency.USD),
                    new Qty(size),
                    Side.Sell,
                    time));
        }

        await Task.Yield();
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithQuoteThenBuyTrade(decimal tradeSize)
    {
        var time = DualTimestamp.Synchronized(Instant.Now);
        yield return new QuoteReceived(
            TestInstrument,
            new Quote(
                new Price(100m, Currency.USD),
                new Price(100.05m, Currency.USD),
                new Qty(100m),
                new Qty(100m),
                time));

        yield return new TradeOccurred(
            TestInstrument,
            new Trade(
                new Price(100m, Currency.USD),
                new Qty(tradeSize),
                Side.Buy,
                time));

        await Task.Yield();
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithTimedQuoteThenTrades(
        Instant quoteTime,
        params (Instant Time, decimal Size)[] trades)
    {
        yield return new QuoteReceived(
            TestInstrument,
            new Quote(
                new Price(100m, Currency.USD),
                new Price(100.05m, Currency.USD),
                new Qty(100m),
                new Qty(100m),
                DualTimestamp.Synchronized(quoteTime)))
        {
            Time = quoteTime
        };

        foreach (var (time, size) in trades)
        {
            yield return new TradeOccurred(
                TestInstrument,
                new Trade(
                    new Price(100m, Currency.USD),
                    new Qty(size),
                    Side.Sell,
                    DualTimestamp.Synchronized(time)))
            {
                Time = time
            };
        }

        await Task.Yield();
    }

    [Fact]
    public void Constructor_SetsDefaultConfig()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());

        Assert.Equal(ExchangeId.Replay, connector.Exchange);
        Assert.IsType<NoopRateLimiter>(connector.RateLimiter);
        Assert.False(connector.IsConnected);
    }

    [Fact]
    public void Constructor_AcceptsCustomConfig()
    {
        var config = SimulationConfig.Instant();
        var fillModel = new DefaultFillModel();
        var riskGuard = new DefaultRiskGuard { MaxOrderSize = 500m };

        var connector = new ReplayConnector(
            CreateEmptyHistory(),
            config,
            fillModel,
            riskGuard);

        Assert.Equal(ExchangeId.Replay, connector.Exchange);
    }

    [Fact]
    public async Task StartAsync_CompletesWithEmptyHistory()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());
        var events = new TestEventPublisher();

        await connector.StartAsync([], events, CancellationToken.None);

        Assert.False(connector.IsConnected); // Should be false after completion
        Assert.Empty(events.EmittedEvents);
    }

    [Fact]
    public async Task StartAsync_EmitsAllHistoryEvents()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(5));
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Equal(5, events.EmittedEvents.Count);
        Assert.All(events.EmittedEvents, e => Assert.IsType<QuoteReceived>(e));
    }

    [Fact]
    public async Task StartAsync_UpdatesDepthFromQuotesBeforeOrdersUseIt()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(10m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(new StrategyId(7), fill.StrategyId);
        Assert.Equal(100.05m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_SameArrivalInflightSubmissionsUseSubmissionSequence()
    {
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMillis(1), Duration.Zero)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (Instant.FromUnixSeconds(1), 100m, 101m),
                (Instant.FromUnixSeconds(1) + Duration.FromMillis(1), 100m, 101m)),
            config);
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        var first = SubmitOrder.BuyLimit(new StrategyId(7), TestInstrument, new Qty(1m), new Price(95m, Currency.USD));
        var second = SubmitOrder.BuyLimit(new StrategyId(7), TestInstrument, new Qty(1m), new Price(96m, Currency.USD));
        var submitted = false;

        events.OnEmit = evt =>
        {
            if (submitted || evt is not QuoteReceived)
                return;

            submitted = true;
            connector.SubmitOrderAsync(first, CancellationToken.None).GetAwaiter().GetResult();
            connector.SubmitOrderAsync(second, CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var acceptedOrderIds = events.EmittedEvents
            .OfType<OrderAccepted>()
            .Select(static evt => evt.OrderId)
            .ToArray();
        Assert.Equal([first.OrderId, second.OrderId], acceptedOrderIds);
    }

    [Fact]
    public async Task StartAsync_SameArrivalInflightCancelBeatsModify()
    {
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMillis(1), Duration.Zero)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (Instant.FromUnixSeconds(1), 100m, 101m),
                (Instant.FromUnixSeconds(1) + Duration.FromMillis(1), 100m, 101m),
                (Instant.FromUnixSeconds(1) + Duration.FromMillis(2), 100m, 101m)),
            config);
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        var order = SubmitOrder.BuyLimit(new StrategyId(7), TestInstrument, new Qty(1m), new Price(95m, Currency.USD));
        var submitted = false;
        var accepted = false;
        var queuedModifyAndCancel = false;

        events.OnEmit = evt =>
        {
            if (!submitted && evt is QuoteReceived)
            {
                submitted = true;
                connector.SubmitOrderAsync(order, CancellationToken.None).GetAwaiter().GetResult();
                return;
            }

            if (evt is OrderAccepted acceptedEvent && acceptedEvent.OrderId == order.OrderId)
            {
                accepted = true;
                return;
            }

            if (!accepted || queuedModifyAndCancel || evt is not QuoteReceived)
                return;

            queuedModifyAndCancel = true;
            connector.ModifyOrderAsync(
                    new ModifyOrder(order.OrderId, NewLimitPrice: new Price(96m, Currency.USD)),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            connector.CancelOrderAsync(new CancelOrder(order.OrderId), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Contains(events.EmittedEvents, evt => evt is OrderCancelled cancelled && cancelled.OrderId == order.OrderId);
        Assert.DoesNotContain(events.EmittedEvents, evt => evt is OrderModified modified && modified.OrderId == order.OrderId);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsOrderTypeDisallowedByVenuePolicy()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1))
        {
            VenueOrderPolicies = new Dictionary<Venue, ReplayVenueOrderPolicy>
            {
                [Venue.NASDAQ] = ReplayVenueOrderPolicy.Default with
                {
                    AllowedOrderTypes = new HashSet<OrderType> { OrderType.Limit }
                }
            }
        };
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(10m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.Single(events.EmittedEvents.OfType<OrderRejected>());
        Assert.Contains("does not allow Market", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderAccepted or OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsTimeInForceDisallowedByVenuePolicy()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1))
        {
            VenueOrderPolicies = new Dictionary<Venue, ReplayVenueOrderPolicy>
            {
                [Venue.NASDAQ] = ReplayVenueOrderPolicy.Default with
                {
                    AllowedTimeInForce = new HashSet<TimeInForce> { TimeInForce.Day, TimeInForce.IOC }
                }
            }
        };
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.BuyLimit(
                        new StrategyId(7),
                        TestInstrument,
                        new Qty(10m),
                        new Price(99m, Currency.USD)) with
                    {
                        TimeInForce = TimeInForce.GTC
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.Single(events.EmittedEvents.OfType<OrderRejected>());
        Assert.Contains("does not allow GTC", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderAccepted);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsPostOnlyWhenVenuePolicyDisallows()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1))
        {
            VenueOrderPolicies = new Dictionary<Venue, ReplayVenueOrderPolicy>
            {
                [Venue.NASDAQ] = ReplayVenueOrderPolicy.Default with
                {
                    AllowPostOnly = false
                }
            }
        };
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.BuyLimit(
                        new StrategyId(7),
                        TestInstrument,
                        new Qty(10m),
                        new Price(99m, Currency.USD)) with
                    {
                        PostOnly = true
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.Single(events.EmittedEvents.OfType<OrderRejected>());
        Assert.Contains("does not allow post-only", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderAccepted);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsQuantityBelowVenuePolicyMinimum()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1))
        {
            VenueOrderPolicies = new Dictionary<Venue, ReplayVenueOrderPolicy>
            {
                [Venue.NASDAQ] = ReplayVenueOrderPolicy.Default with
                {
                    MinOrderQuantity = new Qty(10m)
                }
            }
        };
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.BuyLimit(
                        new StrategyId(7),
                        TestInstrument,
                        new Qty(5m),
                        new Price(99m, Currency.USD)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.Single(events.EmittedEvents.OfType<OrderRejected>());
        Assert.Contains("minimum order quantity", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderAccepted);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsMarketOrderBelowVenuePolicyNotionalMinimum()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1))
        {
            VenueOrderPolicies = new Dictionary<Venue, ReplayVenueOrderPolicy>
            {
                [Venue.NASDAQ] = ReplayVenueOrderPolicy.Default with
                {
                    MinOrderNotional = Money.USD(1_000m)
                }
            }
        };
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(5m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.Single(events.EmittedEvents.OfType<OrderRejected>());
        Assert.Contains("minimum order notional", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderAccepted or OrderFilled);
    }

    [Fact]
    public async Task StartAsync_UpdatesDepthFromBookBeforeOrdersUseIt()
    {
        var connector = new ReplayConnector(CreateHistoryWithBook());
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth, SubscriptionDepth.L2_20) };
        events.OnEmit = evt =>
        {
            if (evt is BookSnapshotReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(10m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(new StrategyId(7), fill.StrategyId);
        Assert.Equal(100.01m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_ModuleGeneratedStatusEventFeedsConnectorPath()
    {
        var first = Instant.Now;
        var second = first + Duration.FromSeconds(1);
        var module = new ClosingReplayModule(TestInstrument, first);
        var connector = new ReplayConnector(CreateHistory(
            QuoteAt(TestInstrument, first),
            QuoteAt(TestInstrument, second)))
        {
            Modules = [module]
        };
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes, SubscriptionDepth.Top) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived quote && quote.Time == second)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Equal(2, module.PreProcessCount);
        Assert.Equal(2, module.ProcessCount);
        Assert.Contains(events.EmittedEvents, static evt => evt is InstrumentStatusChanged);
        Assert.Contains(events.EmittedEvents, static evt => evt is OrderRejected rejected
            && rejected.Reason.Contains("Market is Closed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_VenuePolicyDisablesTradeExecution()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(10m), MarginConfig())
        {
            VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
            {
                [TestInstrument.Venue] = ReplayVenueSimulationPolicy.Default with
                {
                    TradeExecution = false
                }
            }
        };
        var events = new TestEventPublisher();
        var submitted = false;
        events.OnEmit = evt =>
        {
            if (submitted || evt is not QuoteReceived)
                return;

            submitted = true;
            connector.SubmitOrderAsync(
                SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m), P(100m)),
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(
            [new Subscription(TestInstrument, SubscriptionType.Trades, SubscriptionDepth.Top)],
            events,
            CancellationToken.None);

        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_VenuePolicyCanDisableLiquidityConsumption()
    {
        var connector = new ReplayConnector(CreateHistoryWithBook())
        {
            VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
            {
                [TestInstrument.Venue] = ReplayVenueSimulationPolicy.Default with
                {
                    LiquidityConsumption = false
                }
            }
        };
        var events = new TestEventPublisher();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth, SubscriptionDepth.L2_20) };
        events.OnEmit = evt =>
        {
            if (submitted || evt is not BookSnapshotReceived)
                return;

            submitted = true;
            connector.SubmitOrderAsync(
                SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(40m)),
                CancellationToken.None).GetAwaiter().GetResult();
            connector.SubmitOrderAsync(
                SubmitOrder.Market(new StrategyId(8), TestInstrument, Side.Buy, new Qty(40m)),
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.All(fills, fill => Assert.Equal(100.01m, fill.FillPrice.Value));
    }

    [Fact]
    public async Task SubmitOrderAsync_VenuePolicyCanEmitMarketOrderAck()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1))
        {
            VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
            {
                [TestInstrument.Venue] = ReplayVenueSimulationPolicy.Default with
                {
                    UseMarketOrderAcks = true
                }
            }
        };
        var events = new TestEventPublisher();
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(
            [new Subscription(TestInstrument, SubscriptionType.Quotes, SubscriptionDepth.Top)],
            events,
            CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static evt => evt is OrderAccepted);
        Assert.Contains(events.EmittedEvents, static evt => evt is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_VenuePolicyCanAllowCashBorrowing()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            initialCash: Money.USD(50m))
        {
            VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
            {
                [TestInstrument.Venue] = ReplayVenueSimulationPolicy.Default with
                {
                    AllowCashBorrowing = true
                }
            }
        };
        var events = new TestEventPublisher();
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(
            [new Subscription(TestInstrument, SubscriptionType.Quotes, SubscriptionDepth.Top)],
            events,
            CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static evt => evt is OrderFilled);
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderRejected);
    }

    [Fact]
    public async Task SubmitOrderAsync_VenuePolicyCanFreezeAccount()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1))
        {
            VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
            {
                [TestInstrument.Venue] = ReplayVenueSimulationPolicy.Default with
                {
                    FrozenAccount = true
                }
            }
        };
        var events = new TestEventPublisher();
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(
            [new Subscription(TestInstrument, SubscriptionType.Quotes, SubscriptionDepth.Top)],
            events,
            CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static evt => evt is OrderRejected rejected
            && rejected.Reason.Contains("frozen", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_VenuePolicyCanRejectContingentOrders()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1))
        {
            VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
            {
                [TestInstrument.Venue] = ReplayVenueSimulationPolicy.Default with
                {
                    SupportContingentOrders = false
                }
            }
        };
        var events = new TestEventPublisher();
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.BuyLimit(new StrategyId(7), TestInstrument, new Qty(1m), P(99m)) with
                    {
                        OrderListId = OrderListId.New(),
                        ContingencyType = ContingencyType.OCO
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(
            [new Subscription(TestInstrument, SubscriptionType.Quotes, SubscriptionDepth.Top)],
            events,
            CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static evt => evt is OrderRejected rejected
            && rejected.Reason.Contains("contingent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SubmitOrderAsync_VenuePolicyCanRejectTriggeredOrdersInOpenMarket()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1))
        {
            VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
            {
                [TestInstrument.Venue] = ReplayVenueSimulationPolicy.Default with
                {
                    RejectTriggeredOrdersInMarket = true
                }
            }
        };
        var events = new TestEventPublisher();
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m), P(101m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(
            [new Subscription(TestInstrument, SubscriptionType.Quotes, SubscriptionDepth.Top)],
            events,
            CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static evt => evt is OrderRejected rejected
            && rejected.Reason.Contains("triggered", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SubmitOrderAsync_VenuePolicyPriceProtectionStopsMarketWalk()
    {
        var time = Instant.Now;
        var wideBook = new BookSnapshotReceived(
            TestInstrument,
            new Book
            {
                Instrument = TestInstrument,
                Time = time,
                Bids = [new Level(P(99.99m), new Qty(100m))],
                Asks =
                [
                    new Level(P(100.01m), new Qty(40m)),
                    new Level(P(100.03m), new Qty(80m))
                ]
            })
        {
            Time = time
        };
        var connector = new ReplayConnector(CreateHistory(wideBook))
        {
            VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
            {
                [TestInstrument.Venue] = ReplayVenueSimulationPolicy.Default with
                {
                    PriceProtectionTicks = 1
                }
            }
        };
        var events = new TestEventPublisher();
        events.OnEmit = evt =>
        {
            if (evt is BookSnapshotReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(60m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(
            [new Subscription(TestInstrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)],
            events,
            CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(40m, fill.FilledQty.Value);
        Assert.Equal(100.01m, fill.FillPrice.Value);
        Assert.Contains(events.EmittedEvents, static evt => evt is OrderCancelled cancelled
            && cancelled.RemainingQty.Value == 20m);
    }

    [Fact]
    public async Task StartAsync_AppliesTakerPriceImprovementToMarketDepthFill()
    {
        var config = SimulationConfig.Instant() with
        {
            PriceImprovement = PriceImprovementParams.FixedBps(takerBps: 10m)
        };
        var connector = new ReplayConnector(CreateHistoryWithBook(), config);
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth, SubscriptionDepth.L2_20) };
        events.OnEmit = evt =>
        {
            if (evt is BookSnapshotReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(10m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(99.90999m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_MarketBuyWalksAskBookLevels()
    {
        var connector = new ReplayConnector(CreateHistoryWithBook());
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth, SubscriptionDepth.L2_20) };
        events.OnEmit = evt =>
        {
            if (evt is BookSnapshotReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(60m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.All(fills, fill => Assert.Equal(orderId, fill.OrderId));
        Assert.Equal([40m, 20m], fills.Select(static fill => fill.FilledQty.Value).ToArray());
        Assert.Equal([100.01m, 100.02m], fills.Select(static fill => fill.FillPrice.Value).ToArray());
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderCancelled);
    }

    [Fact]
    public async Task StartAsync_MarketBuyCancelsRemainderWhenBookLiquidityIsExhausted()
    {
        var connector = new ReplayConnector(CreateHistoryWithBook());
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth, SubscriptionDepth.L2_20) };
        events.OnEmit = evt =>
        {
            if (evt is BookSnapshotReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(150m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal([40m, 80m], fills.Select(static fill => fill.FilledQty.Value).ToArray());

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(orderId, cancellation.OrderId);
        Assert.Equal(30m, cancellation.RemainingQty.Value);
        Assert.Contains("exhausted", cancellation.Reason);
    }

    [Fact]
    public async Task StartAsync_MarketOrdersConsumeFiniteExternalBookLiquidity()
    {
        var connector = new ReplayConnector(CreateHistoryWithBook());
        var events = new TestEventPublisher();
        var firstOrderId = OrderId.New();
        var secondOrderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth, SubscriptionDepth.L2_20) };
        events.OnEmit = evt =>
        {
            if (evt is BookSnapshotReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(70m)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(60m)) with
                    {
                        OrderId = secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(3, fills.Length);
        Assert.Equal([firstOrderId, firstOrderId, secondOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([40m, 30m, 50m], fills.Select(static fill => fill.FilledQty.Value).ToArray());
        Assert.Equal([100.01m, 100.02m, 100.02m], fills.Select(static fill => fill.FillPrice.Value).ToArray());

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(secondOrderId, cancellation.OrderId);
        Assert.Equal(10m, cancellation.RemainingQty.Value);
        Assert.Contains("exhausted", cancellation.Reason);
    }

    [Fact]
    public async Task StartAsync_FokMarketOrderSeesPreviouslyConsumedExternalDepth()
    {
        var connector = new ReplayConnector(CreateHistoryWithBook());
        var events = new TestEventPublisher();
        var firstOrderId = OrderId.New();
        var fokOrderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth, SubscriptionDepth.L2_20) };
        events.OnEmit = evt =>
        {
            if (evt is BookSnapshotReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(70m)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(60m)) with
                    {
                        OrderId = fokOrderId,
                        TimeInForce = TimeInForce.FOK
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([firstOrderId, firstOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([40m, 30m], fills.Select(static fill => fill.FilledQty.Value).ToArray());

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(fokOrderId, cancellation.OrderId);
        Assert.Equal(60m, cancellation.RemainingQty.Value);
        Assert.Contains("FOK market", cancellation.Reason);
    }

    [Fact]
    public async Task StartAsync_MarketSellsConsumeFiniteExternalBidLiquidity()
    {
        var connector = new ReplayConnector(CreateHistoryWithBook(), MarginConfig());
        var events = new TestEventPublisher();
        var firstOrderId = OrderId.New();
        var secondOrderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth, SubscriptionDepth.L2_20) };
        events.OnEmit = evt =>
        {
            if (evt is BookSnapshotReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Sell, new Qty(80m)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Sell, new Qty(60m)) with
                    {
                        OrderId = secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(3, fills.Length);
        Assert.Equal([firstOrderId, firstOrderId, secondOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([50m, 30m, 45m], fills.Select(static fill => fill.FilledQty.Value).ToArray());
        Assert.Equal([99.99m, 99.98m, 99.98m], fills.Select(static fill => fill.FillPrice.Value).ToArray());

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(secondOrderId, cancellation.OrderId);
        Assert.Equal(15m, cancellation.RemainingQty.Value);
        Assert.Contains("exhausted", cancellation.Reason);
    }

    [Fact]
    public async Task StartAsync_FokMarketSellSeesPreviouslyConsumedExternalBidDepth()
    {
        var connector = new ReplayConnector(CreateHistoryWithBook(), MarginConfig());
        var events = new TestEventPublisher();
        var firstOrderId = OrderId.New();
        var fokOrderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth, SubscriptionDepth.L2_20) };
        events.OnEmit = evt =>
        {
            if (evt is BookSnapshotReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Sell, new Qty(80m)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Sell, new Qty(60m)) with
                    {
                        OrderId = fokOrderId,
                        TimeInForce = TimeInForce.FOK
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([firstOrderId, firstOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([50m, 30m], fills.Select(static fill => fill.FilledQty.Value).ToArray());

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(fokOrderId, cancellation.OrderId);
        Assert.Equal(60m, cancellation.RemainingQty.Value);
        Assert.Contains("FOK market", cancellation.Reason);
    }

    [Fact]
    public async Task StartAsync_NewBookUpdateRefreshesExternalBookLiquidity()
    {
        var connector = new ReplayConnector(CreateHistoryWithBooks(2));
        var events = new TestEventPublisher();
        var firstOrderId = OrderId.New();
        var secondOrderId = OrderId.New();
        var bookCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth, SubscriptionDepth.L2_20) };
        events.OnEmit = evt =>
        {
            if (evt is BookSnapshotReceived)
            {
                bookCount++;
                var orderId = bookCount == 1 ? firstOrderId : secondOrderId;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(120m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([firstOrderId, firstOrderId, secondOrderId, secondOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([40m, 80m, 40m, 80m], fills.Select(static fill => fill.FilledQty.Value).ToArray());
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderCancelled);
    }

    [Fact]
    public async Task StartAsync_BookDeltaUpdatesExternalDepthForMarketOrder()
    {
        var time = Instant.FromUnixSeconds(1);
        var connector = new ReplayConnector(
            CreateHistory(new BookLevelDeltaReceived(
                TestInstrument,
                new BookLevelDelta(Side.Sell, new Price(101m, Currency.USD), new Qty(10m), BookAction.Add))
            {
                Time = time
            }));
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth) };
        events.OnEmit = evt =>
        {
            if (evt is BookLevelDeltaReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(5m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(101m, fill.FillPrice.Value);
        Assert.Equal(new Qty(5m), fill.FilledQty);
    }

    [Fact]
    public async Task StartAsync_BookDeltasApplyInSourceOrderAndDeleteBestAsk()
    {
        var time = Instant.FromUnixSeconds(1);
        var connector = new ReplayConnector(
            CreateHistory(new BookLevelDeltasReceived(
                TestInstrument,
                [
                    new BookLevelDelta(Side.Sell, new Price(101m, Currency.USD), new Qty(10m), BookAction.Add, VenueSequence: 1),
                    new BookLevelDelta(Side.Sell, new Price(102m, Currency.USD), new Qty(10m), BookAction.Add, VenueSequence: 2),
                    new BookLevelDelta(Side.Sell, new Price(101m, Currency.USD), Qty.Zero, BookAction.Delete, VenueSequence: 3)
                ])
            {
                Time = time
            }));
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth) };
        events.OnEmit = evt =>
        {
            if (evt is BookLevelDeltasReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(5m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(102m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_FixedDepthSnapshotReplacesVisibleDepth()
    {
        var time = Instant.FromUnixSeconds(1);
        var initialBook = new Book
        {
            Instrument = TestInstrument,
            Time = time,
            Bids = [new Level(new Price(99m, Currency.USD), new Qty(10m))],
            Asks = [new Level(new Price(101m, Currency.USD), new Qty(10m))]
        };
        var connector = new ReplayConnector(
            CreateHistory(
                new BookSnapshotReceived(TestInstrument, initialBook) { Time = time },
                new BookDepthSnapshotReceived(
                    TestInstrument,
                    Bids: [new Level(new Price(98m, Currency.USD), new Qty(10m))],
                    Asks: [new Level(new Price(103m, Currency.USD), new Qty(10m))],
                    Depth: 1)
                {
                    Time = time + Duration.FromMillis(1)
                }));
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth) };
        events.OnEmit = evt =>
        {
            if (evt is BookDepthSnapshotReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(5m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(103m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_FillsStopMarketWhenReplayBarTouchesStop()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBar(120m, 125m, 119m, 124m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        new Price(124m, Currency.USD)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(new StrategyId(7), fill.StrategyId);
        Assert.Equal(124m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_FillsStopLimitWhenReplayBarTouchesStopAndLimit()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBar(100m, 105m, 102m, 104m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.StopLimit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        new Price(104m, Currency.USD),
                        new Price(103m, Currency.USD)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(new StrategyId(7), fill.StrategyId);
        Assert.Equal(103m, fill.FillPrice.Value);
    }

    [Theory]
    [InlineData(MarketStatus.PreOpen)]
    [InlineData(MarketStatus.Closed)]
    [InlineData(MarketStatus.Halted)]
    public async Task SubmitOrderAsync_RejectsWhenInitialMarketStatusIsNotOpen(MarketStatus status)
    {
        var config = SimulationConfig.Instant() with { InitialMarketStatus = status };
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1), config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Equal(new StrategyId(7), rejection.StrategyId);
        Assert.Contains(status.ToString(), rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderAccepted);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task StartAsync_VenueStatusCloseRejectsDelayedSubmissionOnArrival()
    {
        var start = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMillis(1), Duration.Zero)
        };
        var orderId = OrderId.New();
        var connector = new ReplayConnector(
            CreateHistory(
                QuoteAt(TestInstrument, start),
                new VenueStatusChanged(Venue.NASDAQ, MarketStatus.Closed, "session closed") { Time = start + Duration.FromMillis(1) }),
            config);
        var events = new TestEventPublisher();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.BuyLimit(new StrategyId(7), TestInstrument, new Qty(1m), new Price(95m, Currency.USD)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Contains(nameof(MarketStatus.Closed), rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderAccepted);
    }

    [Fact]
    public async Task StartAsync_InstrumentHaltRejectsOnlyThatInstrument()
    {
        var otherInstrument = new Instrument(new Asset("MSFT", AssetClass.Equity), Venue.NYSE);
        var start = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMillis(1), Duration.Zero)
        };
        var haltedOrder = SubmitOrder.BuyLimit(new StrategyId(7), TestInstrument, new Qty(1m), new Price(95m, Currency.USD));
        var openOrder = SubmitOrder.BuyLimit(new StrategyId(7), otherInstrument, new Qty(1m), new Price(95m, Currency.USD));
        var connector = new ReplayConnector(
            CreateHistory(
                QuoteAt(otherInstrument, start),
                QuoteAt(TestInstrument, start),
                new InstrumentStatusChanged(TestInstrument, MarketStatus.Halted, "halt") { Time = start + Duration.FromMillis(1) }),
            config);
        var events = new TestEventPublisher();
        var subscriptions = new[]
        {
            new Subscription(TestInstrument, SubscriptionType.Quotes),
            new Subscription(otherInstrument, SubscriptionType.Quotes)
        };
        var submitted = false;
        events.OnEmit = evt =>
        {
            if (submitted || evt is not QuoteReceived quote || quote.Instrument != TestInstrument)
                return;

            submitted = true;
            connector.SubmitOrderAsync(haltedOrder, CancellationToken.None).GetAwaiter().GetResult();
            connector.SubmitOrderAsync(openOrder, CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(haltedOrder.OrderId, rejection.OrderId);
        Assert.Contains(nameof(MarketStatus.Halted), rejection.Reason);
        Assert.Contains(events.EmittedEvents, e => e is OrderAccepted accepted && accepted.OrderId == openOrder.OrderId);
    }

    [Fact]
    public async Task StartAsync_MarketClosedLifecycleUpdatesVenueStatus()
    {
        var start = Instant.FromUnixSeconds(1);
        var orderId = OrderId.New();
        var connector = new ReplayConnector(
            CreateHistory(new MarketClosed(Venue.NASDAQ) { Time = start }));
        var events = new TestEventPublisher();
        events.OnEmit = evt =>
        {
            if (evt is MarketClosed)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.BuyLimit(new StrategyId(7), TestInstrument, new Qty(1m), new Price(95m, Currency.USD)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync([], events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Contains(nameof(MarketStatus.Closed), rejection.Reason);
    }

    [Fact]
    public async Task SubmitOrderAsync_FillsMarketableImmediateOrCancelLimitImmediately()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        new Price(100.05m, Currency.USD)) with
                    {
                        OrderId = orderId,
                        TimeInForce = TimeInForce.IOC
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static e => e is OrderAccepted);
        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(100.05m, fill.FillPrice.Value);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderCancelled);
    }

    [Fact]
    public async Task SubmitOrderAsync_FillsMarketableFillOrKillLimitImmediately()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        new Price(100.05m, Currency.USD)) with
                    {
                        OrderId = orderId,
                        TimeInForce = TimeInForce.FOK
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static e => e is OrderAccepted);
        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(100.05m, fill.FillPrice.Value);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderCancelled);
    }

    [Fact]
    public async Task SubmitOrderAsync_CancelsNonMarketableImmediateOrCancelLimitWithoutResting()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBar(100m, 101m, 98m, 99m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        new Price(99m, Currency.USD)) with
                    {
                        OrderId = orderId,
                        TimeInForce = TimeInForce.IOC
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static e => e is OrderAccepted);
        var cancellation = Assert.IsType<OrderCancelled>(
            events.EmittedEvents.Single(e => e is OrderCancelled));
        Assert.Equal(orderId, cancellation.OrderId);
        Assert.Equal(new Qty(10m), cancellation.RemainingQty);
        Assert.Contains("IOC", cancellation.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_CancelsNonMarketableFillOrKillLimitWithoutResting()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBar(100m, 101m, 98m, 99m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        new Price(99m, Currency.USD)) with
                    {
                        OrderId = orderId,
                        TimeInForce = TimeInForce.FOK
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static e => e is OrderAccepted);
        var cancellation = Assert.IsType<OrderCancelled>(
            events.EmittedEvents.Single(e => e is OrderCancelled));
        Assert.Equal(orderId, cancellation.OrderId);
        Assert.Equal(new Qty(10m), cancellation.RemainingQty);
        Assert.Contains("FOK", cancellation.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task StartAsync_ExpiresGoodTilDateOrderBeforeExpiredEventCanFillIt()
    {
        var quoteTime = Instant.FromUnixSeconds(1_000);
        var expiry = quoteTime + Duration.FromMinutes(1);
        var barTime = expiry;
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBarAt(quoteTime, barTime, 100m, 101m, 98m, 99m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(100.50m)) with
                    {
                        OrderId = orderId,
                        TimeInForce = TimeInForce.GTD,
                        GoodTilDate = expiry
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var expired = Assert.IsType<OrderExpired>(
            events.EmittedEvents.Single(e => e is OrderExpired));
        Assert.Equal(orderId, expired.OrderId);
        Assert.Contains(events.EmittedEvents, static e => e is OrderAccepted);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderCancelled);
    }

    [Fact]
    public async Task StartAsync_FillsGoodTilDateOrderBeforeExpiry()
    {
        var quoteTime = Instant.FromUnixSeconds(2_000);
        var barTime = quoteTime + Duration.FromSeconds(30);
        var expiry = quoteTime + Duration.FromMinutes(1);
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBarAt(quoteTime, barTime, 100m, 101m, 98m, 99m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(100.50m)) with
                    {
                        OrderId = orderId,
                        TimeInForce = TimeInForce.GTD,
                        GoodTilDate = expiry
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(100.50m, fill.FillPrice.Value);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderExpired);
    }

    [Fact]
    public async Task StartAsync_ExpiresDayOrderBeforeNextSessionEventCanFillIt()
    {
        var quoteTime = Instant.FromDateTimeOffset(new DateTimeOffset(2026, 5, 21, 20, 59, 0, TimeSpan.Zero));
        var barTime = Instant.FromDateTimeOffset(new DateTimeOffset(2026, 5, 22, 13, 30, 0, TimeSpan.Zero));
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBarAt(quoteTime, barTime, 100m, 101m, 98m, 99m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(100.50m)) with
                    {
                        OrderId = orderId,
                        TimeInForce = TimeInForce.Day
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var expired = Assert.Single(events.EmittedEvents.OfType<OrderExpired>());
        Assert.Equal(orderId, expired.OrderId);
        Assert.Contains(events.EmittedEvents, static e => e is OrderAccepted);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderCancelled);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsGoodTilDateOrderWithoutExpiryTimestamp()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(99m)) with
                    {
                        OrderId = orderId,
                        TimeInForce = TimeInForce.GTD
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Contains("GoodTilDate", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderAccepted);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsPostOnlyMarketOrder()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m)) with
                    {
                        OrderId = orderId,
                        PostOnly = true
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Contains("PostOnly market", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderAccepted);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsPostOnlyLimitThatWouldTakeLiquidity()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(100.05m)) with
                    {
                        OrderId = orderId,
                        PostOnly = true
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Contains("PostOnly", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderAccepted);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_AcceptsPostOnlyLimitThatWouldRest()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(99m)) with
                    {
                        OrderId = orderId,
                        PostOnly = true
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Contains(events.EmittedEvents, e => e is OrderAccepted accepted && accepted.OrderId == orderId);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderRejected);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsCashAccountBuyWhenInsufficientCash()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Contains("Insufficient cash", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_DebitsCashSoLaterBuyCanBeRejected()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(2),
            initialCash: Money.USD(600m));
        var events = new TestEventPublisher();
        var firstOrderId = OrderId.New();
        var secondOrderId = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            var orderId = quoteCount == 1 ? firstOrderId : secondOrderId;
            var quantity = quoteCount == 1 ? new Qty(5m) : new Qty(1m);
            connector.SubmitOrderAsync(
                SubmitOrder.Market(
                    new StrategyId(7),
                    TestInstrument,
                    Side.Buy,
                    quantity) with
                {
                    OrderId = orderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(firstOrderId, fill.OrderId);
        Assert.Equal(5m, fill.FilledQty.Value);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(secondOrderId, rejection.OrderId);
        Assert.Contains("Insufficient cash", rejection.Reason);
    }

    [Fact]
    public async Task SubmitOrderAsync_ReservesCashForOpenBuyOrders()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(2),
            initialCash: Money.USD(150m));
        var events = new TestEventPublisher();
        var restingOrderId = OrderId.New();
        var rejectedOrderId = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            if (quoteCount == 1)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        P(99m)) with
                    {
                        OrderId = restingOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                return;
            }

            connector.SubmitOrderAsync(
                SubmitOrder.Market(
                    new StrategyId(7),
                    TestInstrument,
                    Side.Buy,
                    new Qty(1m)) with
                {
                    OrderId = rejectedOrderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Contains(events.EmittedEvents, e => e is OrderAccepted accepted && accepted.OrderId == restingOrderId);
        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(rejectedOrderId, rejection.OrderId);
        Assert.Contains("Insufficient cash", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsCashAccountNakedSell()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Contains("Cash account cannot sell", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_AllowsCashAccountSellAfterLongPositionExists()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(2));
        var events = new TestEventPublisher();
        var buyOrderId = OrderId.New();
        var sellOrderId = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            var side = quoteCount == 1 ? Side.Buy : Side.Sell;
            var orderId = quoteCount == 1 ? buyOrderId : sellOrderId;
            connector.SubmitOrderAsync(
                SubmitOrder.Market(
                    new StrategyId(7),
                    TestInstrument,
                    side,
                    new Qty(1m)) with
                {
                    OrderId = orderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal(buyOrderId, fills[0].OrderId);
        Assert.Equal(sellOrderId, fills[1].OrderId);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderRejected);
    }

    [Fact]
    public async Task SubmitOrderAsync_AllowsMarginAccountNakedSell()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1), MarginConfig());
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderRejected);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsMarginShortWhenBorrowIsRequired()
    {
        var config = MarginConfig() with
        {
            Margin = MarginParams.RegT()
        };
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1), config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Contains("requires borrow/locate", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_AllowsMarginSellAgainstExistingLongWhenBorrowIsRequired()
    {
        var config = MarginConfig() with
        {
            Margin = MarginParams.RegT()
        };
        var connector = new ReplayConnector(CreateHistoryWithQuotes(2), config);
        var events = new TestEventPublisher();
        var buyOrderId = OrderId.New();
        var sellOrderId = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            var orderId = quoteCount == 1 ? buyOrderId : sellOrderId;
            var side = quoteCount == 1 ? Side.Buy : Side.Sell;
            connector.SubmitOrderAsync(
                SubmitOrder.Market(
                    new StrategyId(7),
                    TestInstrument,
                    side,
                    new Qty(1m)) with
                {
                    OrderId = orderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal(buyOrderId, fills[0].OrderId);
        Assert.Equal(sellOrderId, fills[1].OrderId);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderRejected);
    }

    [Fact]
    public async Task SubmitOrderAsync_AllowsMarginShortWhenBorrowInventoryIsLocated()
    {
        var config = MarginConfig() with
        {
            Margin = MarginParams.RegT()
                .WithBorrowAvailability(TestInstrument, new Qty(2m))
        };
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1), config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(2m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderRejected);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsMarginShortWhenLocatedBorrowIsExhausted()
    {
        var config = MarginConfig() with
        {
            Margin = MarginParams.RegT()
                .WithBorrowAvailability(TestInstrument, new Qty(1m))
        };
        var connector = new ReplayConnector(CreateHistoryWithQuotes(2), config);
        var events = new TestEventPublisher();
        var firstOrderId = OrderId.New();
        var secondOrderId = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            connector.SubmitOrderAsync(
                SubmitOrder.Market(
                    new StrategyId(7),
                    TestInstrument,
                    Side.Sell,
                    new Qty(1m)) with
                {
                    OrderId = quoteCount == 1 ? firstOrderId : secondOrderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(firstOrderId, fill.OrderId);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(secondOrderId, rejection.OrderId);
        Assert.Contains("requires borrow/locate", rejection.Reason);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsMarginAccountBuyWhenInitialMarginExceedsCash()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            MarginConfig(leverage: 2m),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(3m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Contains("Insufficient margin buying power", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task SubmitOrderAsync_AllowsMarginAccountBuyWithinLeverage()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            MarginConfig(leverage: 4m),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(3m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderRejected);
    }

    [Fact]
    public async Task SubmitOrderAsync_ReservesMarginForOpenOrders()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(2),
            MarginConfig(leverage: 2m),
            initialCash: Money.USD(90m));
        var events = new TestEventPublisher();
        var restingOrderId = OrderId.New();
        var rejectedOrderId = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            if (quoteCount == 1)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        P(99m)) with
                    {
                        OrderId = restingOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                return;
            }

            connector.SubmitOrderAsync(
                SubmitOrder.Market(
                    new StrategyId(7),
                    TestInstrument,
                    Side.Buy,
                    new Qty(1m)) with
                {
                    OrderId = rejectedOrderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Contains(events.EmittedEvents, e => e is OrderAccepted accepted && accepted.OrderId == restingOrderId);
        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(rejectedOrderId, rejection.OrderId);
        Assert.Contains("Insufficient margin buying power", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task StartAsync_EmitsMarginStatusAndBreachWhenMarkedEquityFallsBelowMaintenance()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes((100m, 100.05m), (75m, 75.05m)),
            MarginConfig(leverage: 10m),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(5m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var snapshots = events.EmittedEvents.OfType<MarginStatusSnapshot>().ToArray();
        Assert.Equal(2, snapshots.Length);
        Assert.False(snapshots[0].IsMaintenanceBreached);
        Assert.Equal(99.75m, snapshots[0].Equity.Amount);
        Assert.Equal(25.00m, snapshots[0].MaintenanceRequirement.Amount);
        Assert.True(
            snapshots[1].IsMaintenanceBreached,
            $"Equity={snapshots[1].Equity.Amount}, Maintenance={snapshots[1].MaintenanceRequirement.Amount}");
        Assert.Equal(-25.25m, snapshots[1].Equity.Amount);
        Assert.Equal(18.75m, snapshots[1].MaintenanceRequirement.Amount);

        var breach = Assert.Single(events.EmittedEvents.OfType<RiskLimitBreached>());
        Assert.Equal("MaintenanceMargin:7:0", breach.LimitName);
        Assert.Equal(-25.25m, breach.CurrentValue);
        Assert.Equal(18.75m, breach.LimitValue);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal(Side.Buy, fills[0].Side);
        Assert.Equal(Side.Sell, fills[1].Side);
        Assert.Equal(75m, fills[1].FillPrice.Value);

        var finalPerformance = events.EmittedEvents.OfType<PerformanceSnapshot>().Last();
        Assert.Equal(0, finalPerformance.OpenPositions);
        Assert.Equal(-25.25m, finalPerformance.Cash.Amount);
        Assert.Equal(-25.25m, finalPerformance.Equity.Amount);
    }

    [Fact]
    public async Task StartAsync_CancelsOpenOrdersWhenMarginLiquidates()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes((100m, 100.05m), (75m, 75.05m)),
            MarginConfig(leverage: 10m),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var buyOrderId = OrderId.New();
        var restingOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m),
                        P(109m)) with
                    {
                        OrderId = restingOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(5m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(restingOrderId, cancellation.OrderId);
        Assert.Equal(new Qty(1m), cancellation.RemainingQty);
        Assert.Equal("Cancelled by margin liquidation.", cancellation.Reason);
        Assert.Contains(events.EmittedEvents, static evt => evt is RiskLimitBreached);
    }

    [Fact]
    public async Task StartAsync_IssuesAndResolvesMarginCallBeforeGraceExpires()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromSeconds(30), 75m, 75.05m),
                (t0 + Duration.FromSeconds(45), 90m, 90.05m)),
            MarginConfig(leverage: 10m, marginCallGracePeriod: Duration.FromMinutes(1)),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(5m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var call = Assert.Single(events.EmittedEvents.OfType<MarginCallIssued>());
        Assert.Equal(new StrategyId(7), call.StrategyId);
        Assert.Equal(0, call.VariantId);
        Assert.Equal(-25.25m, call.Equity.Amount);
        Assert.Equal(18.75m, call.MaintenanceRequirement.Amount);
        Assert.Equal(t0 + Duration.FromSeconds(90), call.DueAt);

        var resolved = Assert.Single(events.EmittedEvents.OfType<MarginCallResolved>());
        Assert.Equal(new StrategyId(7), resolved.StrategyId);
        Assert.Equal(49.75m, resolved.Equity.Amount);
        Assert.Equal(22.50m, resolved.MaintenanceRequirement.Amount);

        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is RiskLimitBreached);
        Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
    }

    [Fact]
    public async Task StartAsync_LiquidatesMarginCallWhenGraceExpiresStillBreached()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromSeconds(30), 75m, 75.05m),
                (t0 + Duration.FromMinutes(2), 74m, 74.05m)),
            MarginConfig(leverage: 10m, marginCallGracePeriod: Duration.FromMinutes(1)),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(5m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Single(events.EmittedEvents.OfType<MarginCallIssued>());
        Assert.Empty(events.EmittedEvents.OfType<MarginCallResolved>());

        var breach = Assert.Single(events.EmittedEvents.OfType<RiskLimitBreached>());
        Assert.Equal("MaintenanceMargin:7:0", breach.LimitName);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal(Side.Buy, fills[0].Side);
        Assert.Equal(Side.Sell, fills[1].Side);
        Assert.Equal(74m, fills[1].FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_CanCancelOpenOrdersWithoutFlatteningWhenMarginPolicyRequiresIt()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = MarginConfig(leverage: 10m) with
        {
            Margin = MarginParams.Leverage(10m)
                .WithLiquidationPolicy(LiquidationPolicy.CancelOpenOrdersOnly)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromSeconds(30), 75m, 75.05m)),
            config,
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var buyOrderId = OrderId.New();
        var restingOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m),
                        P(109m)) with
                    {
                        OrderId = restingOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(5m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var breach = Assert.Single(events.EmittedEvents.OfType<RiskLimitBreached>());
        Assert.Equal("MaintenanceMargin:7:0", breach.LimitName);

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(restingOrderId, cancellation.OrderId);
        Assert.Equal("Cancelled by margin liquidation.", cancellation.Reason);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Single(fills);
        Assert.Equal(buyOrderId, fills[0].OrderId);

        var breachedStatus = events.EmittedEvents
            .OfType<MarginStatusSnapshot>()
            .Single(static snapshot => snapshot.IsMaintenanceBreached);
        Assert.Equal(-25.25m, breachedStatus.Equity.Amount);
        Assert.Equal(18.75m, breachedStatus.MaintenanceRequirement.Amount);

        var finalPerformance = events.EmittedEvents.OfType<PerformanceSnapshot>().Last();
        Assert.Equal(1, finalPerformance.OpenPositions);
    }

    [Fact]
    public async Task StartAsync_CanReducePositionsOnlyUntilMaintenanceIsCovered()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = MarginConfig(leverage: 10m) with
        {
            Margin = MarginParams.Leverage(10m)
                .WithLiquidationPolicy(LiquidationPolicy.CancelOpenOrdersAndReduceToMaintenance)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromSeconds(30), 83m, 83.05m)),
            config,
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var buyOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(5m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Single(events.EmittedEvents.OfType<RiskLimitBreached>());

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal(buyOrderId, fills[0].OrderId);
        Assert.Equal(Side.Buy, fills[0].Side);
        Assert.Equal(Side.Sell, fills[1].Side);
        Assert.Equal(83m, fills[1].FillPrice.Value);
        Assert.True(fills[1].FilledQty.Value > 0m);
        Assert.True(fills[1].FilledQty.Value < 5m);

        var finalCustody = events.EmittedEvents
            .OfType<CustodyPositionSnapshot>()
            .Last(static snapshot => snapshot.StrategyId == new StrategyId(7));
        Assert.True(finalCustody.Quantity.Value > 0m);
        Assert.True(finalCustody.Quantity.Value < 5m);

        var finalPerformance = events.EmittedEvents.OfType<PerformanceSnapshot>().Last();
        Assert.Equal(1, finalPerformance.OpenPositions);
    }

    [Fact]
    public async Task CompleteAccountTransferAsync_AppliesCashDepositAndEmitsLifecycle()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var command = AccountTransferCommand.CashDeposit(
            new StrategyId(7),
            Money.USD(25m),
            externalReference: "ach-1");
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.RequestAccountTransferAsync(command, CancellationToken.None).GetAwaiter().GetResult();
                connector.CompleteAccountTransferAsync(command, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Single(events.EmittedEvents.OfType<AccountTransferRequested>());
        Assert.Single(events.EmittedEvents.OfType<AccountTransferCompleted>());
        Assert.Equal(
            [AccountTransferStatus.Requested, AccountTransferStatus.Completed],
            events.EmittedEvents.OfType<AccountTransferStatusSnapshot>().Select(static snapshot => snapshot.Status).ToArray());

        var statement = events.EmittedEvents.OfType<AccountStatementSnapshot>().Last();
        Assert.Equal(Money.USD(125m), statement.Cash);
        Assert.Equal(Money.USD(125m), statement.AvailableCash);
    }

    [Fact]
    public async Task CompleteAccountTransferAsync_FailsCashWithdrawalWhenCashIsUnavailable()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            initialCash: Money.USD(10m));
        var events = new TestEventPublisher();
        var command = AccountTransferCommand.CashWithdrawal(
            new StrategyId(7),
            Money.USD(25m),
            externalReference: "ach-2");
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.CompleteAccountTransferAsync(command, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var failed = Assert.Single(events.EmittedEvents.OfType<AccountTransferFailed>());
        Assert.Contains("Cash withdrawal requires", failed.Reason);

        var status = Assert.Single(events.EmittedEvents.OfType<AccountTransferStatusSnapshot>());
        Assert.Equal(AccountTransferStatus.Failed, status.Status);
        Assert.Contains("Cash withdrawal requires", status.Reason);
    }

    [Fact]
    public async Task CompleteAccountTransferAsync_AppliesAssetDepositAndWithdrawalToSettledCustody()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var deposit = AccountTransferCommand.AssetDeposit(
            new StrategyId(7),
            TestInstrument,
            new Qty(3m),
            new Price(100m, Currency.USD),
            externalReference: "acat-in");
        var withdrawal = AccountTransferCommand.AssetWithdrawal(
            new StrategyId(7),
            TestInstrument,
            new Qty(2m),
            new Price(100m, Currency.USD),
            externalReference: "acat-out");
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
                connector.CompleteAccountTransferAsync(withdrawal, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Equal(2, events.EmittedEvents.OfType<AccountTransferCompleted>().Count());
        Assert.Equal(
            [AccountTransferType.AssetDeposit, AccountTransferType.AssetWithdrawal],
            events.EmittedEvents.OfType<AccountTransferStatusSnapshot>().Select(static snapshot => snapshot.TransferType).ToArray());
        Assert.All(
            events.EmittedEvents.OfType<AccountTransferStatusSnapshot>(),
            static snapshot => Assert.Equal(AccountTransferStatus.Completed, snapshot.Status));

        var custody = events.EmittedEvents.OfType<CustodyPositionSnapshot>().Last();
        Assert.Equal(new Qty(1m), custody.Quantity);
        Assert.Equal(new Qty(1m), custody.SettledQuantity);
        Assert.Equal(Qty.Zero, custody.PendingDeliveryQuantity);
        Assert.Equal(Qty.Zero, custody.RehypothecatableQuantity);
    }

    [Fact]
    public async Task CompleteAccountTransferAsync_MovesCashBetweenInternalAccounts()
    {
        var sourceStrategyId = new StrategyId(7);
        var destinationStrategyId = new StrategyId(8);
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var command = AccountTransferCommand.InternalCashTransfer(
            sourceStrategyId,
            destinationStrategyId,
            Money.USD(25m),
            externalReference: "internal-cash");
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.CompleteAccountTransferAsync(command, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var completed = Assert.Single(events.EmittedEvents.OfType<AccountTransferCompleted>());
        Assert.Equal(destinationStrategyId, completed.DestinationStrategyId);

        var sourceStatement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .Last(statement => statement.StrategyId == sourceStrategyId);
        var destinationStatement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .Last(statement => statement.StrategyId == destinationStrategyId);

        Assert.Equal(Money.USD(75m), sourceStatement.Cash);
        Assert.Equal(Money.USD(75m), sourceStatement.AvailableCash);
        Assert.Equal(Money.USD(125m), destinationStatement.Cash);
        Assert.Equal(Money.USD(125m), destinationStatement.AvailableCash);
    }

    [Fact]
    public async Task CompleteAccountTransferAsync_MovesSettledCustodyBetweenInternalAccounts()
    {
        var sourceStrategyId = new StrategyId(7);
        var destinationStrategyId = new StrategyId(8);
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var deposit = AccountTransferCommand.AssetDeposit(
            sourceStrategyId,
            TestInstrument,
            new Qty(3m),
            new Price(100m, Currency.USD),
            externalReference: "acat-in");
        var transfer = AccountTransferCommand.InternalAssetTransfer(
            sourceStrategyId,
            destinationStrategyId,
            TestInstrument,
            new Qty(2m),
            new Price(100m, Currency.USD),
            externalReference: "internal-asset");
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
                connector.CompleteAccountTransferAsync(transfer, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Equal(2, events.EmittedEvents.OfType<AccountTransferCompleted>().Count());
        var internalStatus = events.EmittedEvents
            .OfType<AccountTransferStatusSnapshot>()
            .Single(snapshot => snapshot.TransferId == transfer.TransferId);
        Assert.Equal(AccountTransferStatus.Completed, internalStatus.Status);
        Assert.Equal(destinationStrategyId, internalStatus.DestinationStrategyId);

        var sourceCustody = events.EmittedEvents
            .OfType<CustodyPositionSnapshot>()
            .Last(snapshot => snapshot.StrategyId == sourceStrategyId);
        var destinationCustody = events.EmittedEvents
            .OfType<CustodyPositionSnapshot>()
            .Last(snapshot => snapshot.StrategyId == destinationStrategyId);

        Assert.Equal(new Qty(1m), sourceCustody.Quantity);
        Assert.Equal(new Qty(1m), sourceCustody.SettledQuantity);
        Assert.Equal(new Qty(2m), destinationCustody.Quantity);
        Assert.Equal(new Qty(2m), destinationCustody.SettledQuantity);
    }

    [Fact]
    public async Task ApplyCorporateActionAsync_AppliesStockSplitToPositionAndSettledCustody()
    {
        var strategyId = new StrategyId(7);
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var deposit = AccountTransferCommand.AssetDeposit(
            strategyId,
            TestInstrument,
            new Qty(3m),
            new Price(90m, Currency.USD),
            externalReference: "custody-in");
        var split = CorporateActionCommand.StockSplit(
            TestInstrument,
            splitRatio: 2m,
            externalReference: "split-2-for-1");
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
                connector.ApplyCorporateActionAsync(split, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Single(events.EmittedEvents.OfType<CorporateActionApplied>());
        var effect = Assert.Single(events.EmittedEvents.OfType<CorporateActionEffectSnapshot>());
        Assert.Equal(CorporateActionType.StockSplit, effect.ActionType);
        Assert.Equal(new Qty(3m), effect.QuantityBefore);
        Assert.Equal(new Qty(6m), effect.QuantityAfter);
        Assert.Equal(90m, effect.AvgEntryPriceBefore.Value);
        Assert.Equal(45m, effect.AvgEntryPriceAfter.Value);
        Assert.Null(effect.CashAmount);

        var custody = events.EmittedEvents
            .OfType<CustodyPositionSnapshot>()
            .Last(snapshot => snapshot.StrategyId == strategyId);
        Assert.Equal(new Qty(6m), custody.Quantity);
        Assert.Equal(new Qty(6m), custody.SettledQuantity);
        Assert.Equal(45m, custody.AvgEntryPrice.Value);
    }

    [Fact]
    public async Task ApplyCorporateActionAsync_CreditsCashDividendForSettledCustody()
    {
        var strategyId = new StrategyId(7);
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var deposit = AccountTransferCommand.AssetDeposit(
            strategyId,
            TestInstrument,
            new Qty(3m),
            new Price(90m, Currency.USD),
            externalReference: "custody-in");
        var dividend = CorporateActionCommand.CashDividend(
            TestInstrument,
            Money.USD(2m),
            externalReference: "dividend-2");
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
                connector.ApplyCorporateActionAsync(dividend, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Single(events.EmittedEvents.OfType<CorporateActionApplied>());
        var effect = Assert.Single(events.EmittedEvents.OfType<CorporateActionEffectSnapshot>());
        Assert.Equal(CorporateActionType.CashDividend, effect.ActionType);
        Assert.Equal(Money.USD(6m), effect.CashAmount);

        var statement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .Last(snapshot => snapshot.StrategyId == strategyId);
        Assert.Equal(Money.USD(106m), statement.Cash);
        Assert.Equal(Money.USD(106m), statement.AvailableCash);
    }

    [Fact]
    public async Task ApplyFinancingChargeAsync_CreditsCashInterest()
    {
        var strategyId = new StrategyId(7);
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var command = FinancingChargeCommand.CashInterestCredit(
            strategyId,
            Money.USD(1.25m),
            rate: 0.05m,
            externalReference: "broker-interest");
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.ApplyFinancingChargeAsync(command, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var applied = Assert.Single(events.EmittedEvents.OfType<FinancingChargeApplied>());
        Assert.Equal(FinancingChargeType.CashInterestCredit, applied.ChargeType);
        Assert.Equal(Money.USD(1.25m), applied.Amount);
        Assert.Equal(0.05m, applied.Rate);
        Assert.Equal("broker-interest", applied.ExternalReference);

        var statement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .Last(snapshot => snapshot.StrategyId == strategyId);
        Assert.Equal(Money.USD(101.25m), statement.Cash);
        Assert.Equal(Money.USD(101.25m), statement.AvailableCash);
    }

    [Fact]
    public async Task ApplyFinancingChargeAsync_DebitsBorrowFee()
    {
        var strategyId = new StrategyId(7);
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var command = FinancingChargeCommand.BorrowFee(
            strategyId,
            TestInstrument,
            Money.USD(3.50m),
            new Qty(10m),
            rate: 0.02m,
            externalReference: "short-borrow");
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.ApplyFinancingChargeAsync(command, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var applied = Assert.Single(events.EmittedEvents.OfType<FinancingChargeApplied>());
        Assert.Equal(FinancingChargeType.BorrowFee, applied.ChargeType);
        Assert.Equal(TestInstrument, applied.Instrument);
        Assert.Equal(new Qty(10m), applied.Quantity);
        Assert.Equal(Money.USD(-3.50m), applied.Amount);

        var statement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .Last(snapshot => snapshot.StrategyId == strategyId);
        Assert.Equal(Money.USD(96.50m), statement.Cash);
        Assert.Equal(Money.USD(96.50m), statement.AvailableCash);
    }

    [Fact]
    public async Task ApplyFinancingChargeAsync_AppliesSignedPerpetualFunding()
    {
        var strategyId = new StrategyId(7);
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var paid = FinancingChargeCommand.PerpetualFunding(
            strategyId,
            TestInstrument,
            Money.USD(-2m),
            new Qty(5m),
            rate: 0.0001m,
            externalReference: "perp-funding-paid");
        var received = FinancingChargeCommand.PerpetualFunding(
            strategyId,
            TestInstrument,
            Money.USD(4m),
            new Qty(5m),
            rate: -0.0002m,
            externalReference: "perp-funding-received");
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.ApplyFinancingChargeAsync(paid, CancellationToken.None).GetAwaiter().GetResult();
                connector.ApplyFinancingChargeAsync(received, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Equal(2, events.EmittedEvents.OfType<FinancingChargeApplied>().Count());
        var statement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .Last(snapshot => snapshot.StrategyId == strategyId);
        Assert.Equal(Money.USD(102m), statement.Cash);
        Assert.Equal(Money.USD(102m), statement.AvailableCash);
    }

    [Fact]
    public async Task SubmitOrderAsync_TieredFeesUseRollingReplayVolume()
    {
        var strategyId = new StrategyId(7);
        var config = SimulationConfig.Instant() with
        {
            Fees = FeeParams.Tiered(new TieredFeeSchedule
            {
                Tiers =
                [
                    new FeeTier(Money.USD(0m), MakerBps: 2m, TakerBps: 10m),
                    new FeeTier(Money.USD(1_000m), MakerBps: -1m, TakerBps: 1m)
                ]
            })
        };
        var connector = new ReplayConnector(
            CreateHistoryWithBooks(2),
            config,
            initialCash: Money.USD(10_000m));
        var events = new TestEventPublisher();
        var submissions = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Depth) };
        events.OnEmit = evt =>
        {
            if (evt is BookSnapshotReceived && submissions < 2)
            {
                submissions++;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(strategyId, TestInstrument, Side.Buy, new Qty(10m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal(Money.USD(1.0001m), fills[0].Commission);
        Assert.Equal(Money.USD(0.10001m), fills[1].Commission);

        var statement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .Last(snapshot => snapshot.StrategyId == strategyId);
        Assert.Equal(Money.USD(7_998.69989m), statement.Cash);
    }

    [Fact]
    public async Task CompleteAccountTransferAsync_ReportsRehypothecatableQuantityWhenMarginPolicyAllowsIt()
    {
        var config = MarginConfig() with
        {
            Margin = MarginParams.Leverage(2m)
                .WithRehypothecationPolicy(RehypothecationPolicy.Allowed)
        };
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1), config);
        var events = new TestEventPublisher();
        var deposit = AccountTransferCommand.AssetDeposit(
            new StrategyId(7),
            TestInstrument,
            new Qty(3m),
            new Price(100m, Currency.USD));
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var custody = events.EmittedEvents.OfType<CustodyPositionSnapshot>().Last();
        Assert.Equal(new Qty(3m), custody.SettledQuantity);
        Assert.Equal(new Qty(3m), custody.RehypothecatableQuantity);
    }

    [Fact]
    public async Task CompleteAccountTransferAsync_CapsRehypothecatableQuantityByInstrumentPolicy()
    {
        var config = MarginConfig() with
        {
            Margin = MarginParams.Leverage(2m)
                .WithRehypothecationPolicy(RehypothecationPolicy.Allowed)
                .WithRehypothecationAvailability(TestInstrument, new Qty(1m))
        };
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1), config);
        var events = new TestEventPublisher();
        var deposit = AccountTransferCommand.AssetDeposit(
            new StrategyId(7),
            TestInstrument,
            new Qty(3m),
            new Price(100m, Currency.USD));
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var custody = events.EmittedEvents.OfType<CustodyPositionSnapshot>().Last();
        Assert.Equal(new Qty(3m), custody.SettledQuantity);
        Assert.Equal(new Qty(1m), custody.RehypothecatableQuantity);
    }

    [Fact]
    public async Task CompleteAccountTransferAsync_DoesNotReportRehypothecatableQuantityWhenPolicyProhibitsIt()
    {
        var config = MarginConfig() with
        {
            Margin = MarginParams.RegT()
                .WithRehypothecationPolicy(RehypothecationPolicy.Prohibited)
        };
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1), config);
        var events = new TestEventPublisher();
        var deposit = AccountTransferCommand.AssetDeposit(
            new StrategyId(7),
            TestInstrument,
            new Qty(3m),
            new Price(100m, Currency.USD));
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var custody = events.EmittedEvents.OfType<CustodyPositionSnapshot>().Last();
        Assert.Equal(new Qty(3m), custody.SettledQuantity);
        Assert.Equal(Qty.Zero, custody.RehypothecatableQuantity);
    }

    [Fact]
    public async Task SubmitOrderAsync_EmitsPerformanceSnapshotAfterCashFill()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            initialCash: Money.USD(1_000m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var snapshot = Assert.Single(events.EmittedEvents.OfType<PerformanceSnapshot>());
        Assert.Equal(899.95m, snapshot.Cash.Amount);
        Assert.Equal(999.95m, snapshot.Equity.Amount);
        Assert.Equal(-0.05m, snapshot.UnrealizedPnL.Amount);
        Assert.Equal(0m, snapshot.RealizedPnL.Amount);
        Assert.Equal(1, snapshot.OpenPositions);

        var statements = events.EmittedEvents.OfType<AccountStatementSnapshot>().ToArray();
        Assert.Equal(2, statements.Length);
        Assert.Equal(899.95m, statements[0].Cash.Amount);
        Assert.Equal(999.95m, statements[0].Equity.Amount);
        Assert.Equal(-0.05m, statements[0].UnrealizedPnL.Amount);
        Assert.Equal(1, statements[0].OpenPositions);
    }

    [Fact]
    public async Task StartAsync_EmitsAccountStatementWithReservedCashForOpenOrders()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(1),
            initialCash: Money.USD(200m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        new Price(99m, Currency.USD)) with
                    {
                        OrderId = orderId,
                        TimeInForce = TimeInForce.GTC
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var statement = Assert.Single(events.EmittedEvents.OfType<AccountStatementSnapshot>());
        Assert.Equal(new StrategyId(7), statement.StrategyId);
        Assert.Equal(Currency.USD, statement.Currency);
        Assert.Equal(200m, statement.Cash.Amount);
        Assert.Equal(99m, statement.ReservedCash.Amount);
        Assert.Equal(101m, statement.AvailableCash.Amount);
        Assert.Equal(0m, statement.PendingSettlement.Amount);
        Assert.Equal(200m, statement.Equity.Amount);
        Assert.Equal(0, statement.OpenPositions);
        Assert.Equal(1, statement.OpenOrders);
    }

    [Fact]
    public async Task SubmitOrderAsync_RealizesPnLWhenCashPositionCloses()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes(2),
            initialCash: Money.USD(1_000m));
        var events = new TestEventPublisher();
        var buyOrderId = OrderId.New();
        var sellOrderId = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            connector.SubmitOrderAsync(
                SubmitOrder.Market(
                    new StrategyId(7),
                    TestInstrument,
                    quoteCount == 1 ? Side.Buy : Side.Sell,
                    new Qty(1m)) with
                {
                    OrderId = quoteCount == 1 ? buyOrderId : sellOrderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var snapshots = events.EmittedEvents.OfType<PerformanceSnapshot>().ToArray();
        Assert.Equal(2, snapshots.Length);
        var final = snapshots[^1];
        Assert.Equal(999.96m, final.Cash.Amount);
        Assert.Equal(999.96m, final.Equity.Amount);
        Assert.Equal(0m, final.UnrealizedPnL.Amount);
        Assert.Equal(-0.04m, final.RealizedPnL.Amount);
        Assert.Equal(0, final.OpenPositions);
    }

    [Fact]
    public async Task StartAsync_DelaysCashAccountSellProceedsUntilSettlement()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1)
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromMinutes(1), 100.01m, 100.06m),
                (t0 + Duration.FromDays(1) + Duration.FromMinutes(1), 100.02m, 100.07m)),
            config,
            initialCash: Money.USD(200m));
        var events = new TestEventPublisher();
        var firstBuy = OrderId.New();
        var sell = OrderId.New();
        var rejectedBuy = OrderId.New();
        var settledBuy = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            if (quoteCount == 1)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = firstBuy
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                return;
            }

            if (quoteCount == 2)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Sell, new Qty(1m)) with
                    {
                        OrderId = sell
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = rejectedBuy
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                return;
            }

            connector.SubmitOrderAsync(
                SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)) with
                {
                    OrderId = settledBuy
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.Single(events.EmittedEvents.OfType<OrderRejected>());
        Assert.Equal(rejectedBuy, rejection.OrderId);
        Assert.Contains("Insufficient cash buying power", rejection.Reason);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([firstBuy, sell, settledBuy], fills.Select(static fill => fill.OrderId).ToArray());

        var snapshots = events.EmittedEvents.OfType<PerformanceSnapshot>().ToArray();
        Assert.Contains(snapshots, static snapshot =>
            snapshot.Cash.Amount == 99.95m && snapshot.Equity.Amount == 199.96m && snapshot.OpenPositions == 0);
        var final = snapshots[^1];
        Assert.Equal(99.89m, final.Cash.Amount);
        Assert.Equal(199.91m, final.Equity.Amount);
        Assert.Equal(1, final.OpenPositions);

        var statements = events.EmittedEvents.OfType<AccountStatementSnapshot>().ToArray();
        Assert.Contains(statements, static statement =>
            statement.Cash.Amount == 99.95m
            && statement.PendingSettlement.Amount == 100.01m
            && statement.OpenPositions == 0);
        Assert.Contains(statements, static statement =>
            statement.Cash.Amount == 199.96m
            && statement.PendingSettlement.Amount == 0m
            && statement.OpenPositions == 0);

        var scheduled = Assert.Single(events.EmittedEvents.OfType<SettlementScheduled>());
        Assert.True(scheduled.SettlementId.Value > 0);
        Assert.Equal(new StrategyId(7), scheduled.StrategyId);
        Assert.Equal(100.01m, scheduled.Amount.Amount);
        Assert.Equal(t0 + Duration.FromDays(1) + Duration.FromMinutes(1), scheduled.SettlesAt);

        var released = Assert.Single(events.EmittedEvents.OfType<SettlementReleased>());
        Assert.Equal(scheduled.SettlementId, released.SettlementId);
        Assert.Equal(new StrategyId(7), released.StrategyId);
        Assert.Equal(100.01m, released.Amount.Amount);
        Assert.Equal(t0 + Duration.FromDays(1) + Duration.FromMinutes(1), released.SettledAt);

        var settlementStatuses = events.EmittedEvents.OfType<SettlementStatusSnapshot>().ToArray();
        Assert.Equal(
            [SettlementStatus.Scheduled, SettlementStatus.Released],
            settlementStatuses.Select(static snapshot => snapshot.Status).ToArray());
        Assert.All(settlementStatuses, snapshot => Assert.Equal(scheduled.SettlementId, snapshot.SettlementId));
        Assert.Equal(t0 + Duration.FromMinutes(1), settlementStatuses[0].StatusAt);
        Assert.Equal(t0 + Duration.FromDays(1) + Duration.FromMinutes(1), settlementStatuses[0].SettlesAt);
        Assert.Equal(t0 + Duration.FromDays(1) + Duration.FromMinutes(1), settlementStatuses[1].StatusAt);
        Assert.Equal(t0 + Duration.FromDays(1) + Duration.FromMinutes(1), settlementStatuses[1].SettlesAt);
    }

    [Fact]
    public async Task StartAsync_DelaysCashAccountSettlementAcrossWeekend()
    {
        var t0 = Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 5, 14, 30, 0, TimeSpan.Zero));
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1)
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromMinutes(1), 100.01m, 100.06m),
                (Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 7, 14, 32, 0, TimeSpan.Zero)), 100.02m, 100.07m),
                (Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 8, 14, 32, 0, TimeSpan.Zero)), 100.03m, 100.08m)),
            config,
            initialCash: Money.USD(200m));
        var events = new TestEventPublisher();
        var firstBuy = OrderId.New();
        var sell = OrderId.New();
        var weekendBuy = OrderId.New();
        var settledBuy = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            var orderId = quoteCount switch
            {
                1 => firstBuy,
                2 => sell,
                3 => weekendBuy,
                _ => settledBuy
            };
            var side = quoteCount == 2 ? Side.Sell : Side.Buy;
            connector.SubmitOrderAsync(
                SubmitOrder.Market(new StrategyId(7), TestInstrument, side, new Qty(1m)) with
                {
                    OrderId = orderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.Single(events.EmittedEvents.OfType<OrderRejected>());
        Assert.Equal(weekendBuy, rejection.OrderId);
        Assert.Contains("Insufficient cash buying power", rejection.Reason);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([firstBuy, sell, settledBuy], fills.Select(static fill => fill.OrderId).ToArray());

        var statement = events.EmittedEvents.OfType<AccountStatementSnapshot>().Last();
        Assert.Equal(99.88m, statement.Cash.Amount);
        Assert.Equal(99.88m, statement.AvailableCash.Amount);
        Assert.Equal(0m, statement.PendingSettlement.Amount);
        Assert.Equal(1, statement.OpenPositions);
    }

    [Fact]
    public async Task StartAsync_UsesConfiguredClearingCalendarForSettlement()
    {
        var t0 = Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 5, 14, 30, 0, TimeSpan.Zero));
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1, ClearingCalendar.Crypto())
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromMinutes(1), 100.01m, 100.06m),
                (Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 6, 14, 32, 0, TimeSpan.Zero)), 100.02m, 100.07m)),
            config,
            initialCash: Money.USD(200m));
        var events = new TestEventPublisher();
        var firstBuy = OrderId.New();
        var sell = OrderId.New();
        var weekendBuy = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            var orderId = quoteCount switch
            {
                1 => firstBuy,
                2 => sell,
                _ => weekendBuy
            };
            var side = quoteCount == 2 ? Side.Sell : Side.Buy;
            connector.SubmitOrderAsync(
                SubmitOrder.Market(new StrategyId(7), TestInstrument, side, new Qty(1m)) with
                {
                    OrderId = orderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Empty(events.EmittedEvents.OfType<OrderRejected>());
        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([firstBuy, sell, weekendBuy], fills.Select(static fill => fill.OrderId).ToArray());

        var statement = events.EmittedEvents.OfType<AccountStatementSnapshot>().Last();
        Assert.Equal(99.89m, statement.Cash.Amount);
        Assert.Equal(0m, statement.PendingSettlement.Amount);
        Assert.Equal(1, statement.OpenPositions);
    }

    [Fact]
    public async Task StartAsync_DelaysCashAccountSettlementAcrossConfiguredHoliday()
    {
        var t0 = Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 5, 14, 30, 0, TimeSpan.Zero));
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1, [new DateOnly(2024, 1, 8)])
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromMinutes(1), 100.01m, 100.06m),
                (Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 8, 14, 32, 0, TimeSpan.Zero)), 100.02m, 100.07m),
                (Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 9, 14, 32, 0, TimeSpan.Zero)), 100.03m, 100.08m)),
            config,
            initialCash: Money.USD(200m));
        var events = new TestEventPublisher();
        var firstBuy = OrderId.New();
        var sell = OrderId.New();
        var holidayBuy = OrderId.New();
        var settledBuy = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            var orderId = quoteCount switch
            {
                1 => firstBuy,
                2 => sell,
                3 => holidayBuy,
                _ => settledBuy
            };
            var side = quoteCount == 2 ? Side.Sell : Side.Buy;
            connector.SubmitOrderAsync(
                SubmitOrder.Market(new StrategyId(7), TestInstrument, side, new Qty(1m)) with
                {
                    OrderId = orderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.Single(events.EmittedEvents.OfType<OrderRejected>());
        Assert.Equal(holidayBuy, rejection.OrderId);
        Assert.Contains("Insufficient cash buying power", rejection.Reason);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([firstBuy, sell, settledBuy], fills.Select(static fill => fill.OrderId).ToArray());

        var statement = events.EmittedEvents.OfType<AccountStatementSnapshot>().Last();
        Assert.Equal(99.88m, statement.Cash.Amount);
        Assert.Equal(0m, statement.PendingSettlement.Amount);
        Assert.Equal(1, statement.OpenPositions);
    }

    [Fact]
    public async Task StartAsync_EmitsAccountStatementWithPendingSettlementAtReplayEnd()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1)
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromMinutes(1), 100.01m, 100.06m)),
            config,
            initialCash: Money.USD(200m));
        var events = new TestEventPublisher();
        var buyOrderId = OrderId.New();
        var sellOrderId = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            connector.SubmitOrderAsync(
                SubmitOrder.Market(
                    new StrategyId(7),
                    TestInstrument,
                    quoteCount == 1 ? Side.Buy : Side.Sell,
                    new Qty(1m)) with
                {
                    OrderId = quoteCount == 1 ? buyOrderId : sellOrderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var statement = events.EmittedEvents.OfType<AccountStatementSnapshot>().Last();
        Assert.Equal(new StrategyId(7), statement.StrategyId);
        Assert.Equal(Currency.USD, statement.Currency);
        Assert.Equal(99.95m, statement.Cash.Amount);
        Assert.Equal(99.95m, statement.AvailableCash.Amount);
        Assert.Equal(100.01m, statement.PendingSettlement.Amount);
        Assert.Equal(0m, statement.ReservedCash.Amount);
        Assert.Equal(0m, statement.MarketValue.Amount);
        Assert.Equal(199.96m, statement.Equity.Amount);
        Assert.Equal(0m, statement.UnrealizedPnL.Amount);
        Assert.Equal(-0.04m, statement.RealizedPnL.Amount);
        Assert.Equal(0, statement.OpenPositions);
        Assert.Equal(0, statement.OpenOrders);

        var custody = events.EmittedEvents.OfType<CustodyPositionSnapshot>().ToArray();
        Assert.True(custody.Length >= 2);
        var opened = Assert.Single(custody, snapshot => snapshot.IsOpen);
        Assert.Equal(new StrategyId(7), opened.StrategyId);
        Assert.Equal(TestInstrument, opened.Instrument);
        Assert.Equal(new Qty(1m), opened.Quantity);
        Assert.Equal(Qty.Zero, opened.SettledQuantity);
        Assert.Equal(new Qty(1m), opened.PendingDeliveryQuantity);
        Assert.Equal(new Price(100.05m, Currency.USD), opened.AvgEntryPrice);
        Assert.Equal(new Price(100m, Currency.USD), opened.MarkPrice);
        Assert.Equal(Money.USD(100m), opened.MarketValue);
        Assert.Equal(Money.USD(-0.05m), opened.UnrealizedPnL);

        var finalCustody = custody[^1];
        Assert.False(finalCustody.IsOpen);
        Assert.Equal(Qty.Zero, finalCustody.Quantity);
        Assert.Equal(Qty.Zero, finalCustody.SettledQuantity);
        Assert.Equal(Qty.Zero, finalCustody.PendingDeliveryQuantity);
        Assert.Equal(Money.USD(0m), finalCustody.MarketValue);
        Assert.Equal(Money.USD(-0.04m), finalCustody.RealizedPnL);

        var settlementStatuses = events.EmittedEvents.OfType<SettlementStatusSnapshot>().ToArray();
        Assert.Equal(
            [SettlementStatus.Scheduled, SettlementStatus.Pending],
            settlementStatuses.Select(static snapshot => snapshot.Status).ToArray());
        Assert.Equal(t0 + Duration.FromMinutes(1), settlementStatuses[1].StatusAt);
        Assert.Equal(t0 + Duration.FromDays(1) + Duration.FromMinutes(1), settlementStatuses[1].SettlesAt);
    }

    [Fact]
    public async Task StartAsync_DeliversBoughtAssetsIntoSettledCustody()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromDays(1) + Duration.FromMinutes(1), 101m, 101.05m)),
            config,
            initialCash: Money.USD(200m));
        var events = new TestEventPublisher();
        var submitted = false;
        events.OnEmit = evt =>
        {
            if (submitted || evt is not QuoteReceived)
                return;

            submitted = true;
            connector.SubmitOrderAsync(
                SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)),
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(
            [new Subscription(TestInstrument, SubscriptionType.Quotes)],
            events,
            CancellationToken.None);

        var scheduled = Assert.Single(events.EmittedEvents.OfType<AssetDeliveryScheduled>());
        Assert.True(scheduled.DeliveryId.Value > 0);
        Assert.Equal(new StrategyId(7), scheduled.StrategyId);
        Assert.Equal(TestInstrument, scheduled.Instrument);
        Assert.Equal(new Qty(1m), scheduled.Quantity);
        Assert.Equal(t0 + Duration.FromDays(1), scheduled.DeliversAt);

        var delivered = Assert.Single(events.EmittedEvents.OfType<AssetDelivered>());
        Assert.Equal(scheduled.DeliveryId, delivered.DeliveryId);
        Assert.Equal(t0 + Duration.FromDays(1) + Duration.FromMinutes(1), delivered.DeliveredAt);

        var deliveryStatuses = events.EmittedEvents.OfType<AssetDeliveryStatusSnapshot>().ToArray();
        Assert.Equal(
            [AssetDeliveryStatus.Scheduled, AssetDeliveryStatus.Delivered],
            deliveryStatuses.Select(static snapshot => snapshot.Status).ToArray());
        Assert.All(deliveryStatuses, snapshot => Assert.Equal(scheduled.DeliveryId, snapshot.DeliveryId));

        var custody = events.EmittedEvents.OfType<CustodyPositionSnapshot>().ToArray();
        var pending = Assert.Single(custody, snapshot => snapshot.PendingDeliveryQuantity == new Qty(1m));
        Assert.Equal(Qty.Zero, pending.SettledQuantity);
        Assert.Equal(new Qty(1m), pending.Quantity);

        var finalCustody = custody[^1];
        Assert.True(finalCustody.IsOpen);
        Assert.Equal(new Qty(1m), finalCustody.Quantity);
        Assert.Equal(new Qty(1m), finalCustody.SettledQuantity);
        Assert.Equal(Qty.Zero, finalCustody.PendingDeliveryQuantity);
        Assert.Equal(new Price(101m, Currency.USD), finalCustody.MarkPrice);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsCashAccountSaleBeforeAssetDeliverySettles()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromMinutes(1), 100.01m, 100.06m)),
            config,
            initialCash: Money.USD(200m));
        var events = new TestEventPublisher();
        var buyOrderId = OrderId.New();
        var sellOrderId = OrderId.New();
        var quoteCount = 0;
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            connector.SubmitOrderAsync(
                SubmitOrder.Market(
                    new StrategyId(7),
                    TestInstrument,
                    quoteCount == 1 ? Side.Buy : Side.Sell,
                    new Qty(1m)) with
                {
                    OrderId = quoteCount == 1 ? buyOrderId : sellOrderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await connector.StartAsync(
            [new Subscription(TestInstrument, SubscriptionType.Quotes)],
            events,
            CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(buyOrderId, fill.OrderId);
        var rejection = Assert.Single(events.EmittedEvents.OfType<OrderRejected>());
        Assert.Equal(sellOrderId, rejection.OrderId);
        Assert.Contains("only 0 available", rejection.Reason);

        var custody = events.EmittedEvents.OfType<CustodyPositionSnapshot>().Last();
        Assert.Equal(new Qty(1m), custody.Quantity);
        Assert.Equal(Qty.Zero, custody.SettledQuantity);
        Assert.Equal(new Qty(1m), custody.PendingDeliveryQuantity);
    }

    [Fact]
    public async Task StartAsync_DelaysOrderSubmissionUntilEntryLatencyElapsed()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Latency = new(Duration.FromMinutes(1), Duration.Zero)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromSeconds(30), 101m, 101.05m),
                (t0 + Duration.FromMinutes(2), 102m, 102.05m)),
            config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(102.05m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task CancelOrderAsync_CancelsInflightSubmissionBeforeArrival()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Latency = new(Duration.FromMinutes(10), Duration.Zero)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromMinutes(1), 101m, 101.05m),
                (t0 + Duration.FromMinutes(11), 102m, 102.05m)),
            config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            if (quoteCount == 1)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                return;
            }

            if (quoteCount == 2)
            {
                connector.CancelOrderAsync(new CancelOrder { OrderId = orderId }, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(orderId, cancellation.OrderId);
        Assert.Equal("Cancelled before exchange arrival.", cancellation.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderFilled);
    }

    [Fact]
    public async Task ModifyOrderAsync_UpdatesInflightSubmissionBeforeArrival()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Latency = new(Duration.FromMinutes(10), Duration.Zero)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromMinutes(1), 101m, 101.05m),
                (t0 + Duration.FromMinutes(11), 102m, 102.05m)),
            config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var quoteCount = 0;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is not QuoteReceived)
                return;

            quoteCount++;
            if (quoteCount == 1)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        new Price(99m, Currency.USD)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                return;
            }

            if (quoteCount == 2)
            {
                connector.ModifyOrderAsync(
                    new ModifyOrder
                    {
                        OrderId = orderId,
                        NewQuantity = new Qty(2m),
                        NewLimitPrice = new Price(102.05m, Currency.USD)
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(2m, fill.FilledQty.Value);
        Assert.Equal(102.05m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task ModifyOrderAsync_EmitsOrderModifiedForOpenOrder()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        new Price(99m, Currency.USD)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.ModifyOrderAsync(
                    new ModifyOrder(orderId, NewQuantity: new Qty(2m), NewLimitPrice: new Price(98m, Currency.USD)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var accepted = Assert.Single(events.EmittedEvents.OfType<OrderAccepted>());
        Assert.Equal(orderId, accepted.OrderId);

        var modified = Assert.Single(events.EmittedEvents.OfType<OrderModified>());
        Assert.Equal(orderId, modified.OrderId);
        Assert.Equal(new StrategyId(7), modified.StrategyId);
        Assert.Equal(0, modified.VariantId);
        Assert.Equal(new Qty(2m), modified.NewQuantity);
        Assert.Equal(new Price(98m, Currency.USD), modified.NewLimitPrice);
    }

    [Fact]
    public async Task StartAsync_DelaysExecutionResponsesUntilResponseLatencyElapsed()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Latency = new(Duration.Zero, Duration.FromMinutes(1))
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromSeconds(30), 101m, 101.05m),
                (t0 + Duration.FromMinutes(2), 102m, 102.05m)),
            config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(t0 + Duration.FromMinutes(1), fill.Time);

        var secondQuoteIndex = events.EmittedEvents.FindIndex(evt =>
            evt is QuoteReceived quote && quote.Quote.Bid.Value == 101m);
        var fillIndex = events.EmittedEvents.FindIndex(evt => evt is OrderFilled);
        var thirdQuoteIndex = events.EmittedEvents.FindIndex(evt =>
            evt is QuoteReceived quote && quote.Quote.Bid.Value == 102m);
        Assert.True(secondQuoteIndex < fillIndex);
        Assert.True(fillIndex < thirdQuoteIndex);
    }

    [Fact]
    public async Task StartAsync_FlushesPendingResponsesWhenReplayEnds()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Latency = new(Duration.Zero, Duration.FromMinutes(10))
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes((t0, 100m, 100.05m)),
            config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(t0 + Duration.FromMinutes(10), fill.Time);
        Assert.True(events.EmittedEvents.FindIndex(evt => evt is QuoteReceived)
            < events.EmittedEvents.FindIndex(evt => evt is OrderFilled));
    }

    [Fact]
    public async Task CancelOrderAsync_DelaysCancelForOpenOrderAndAllowsRaceFill()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Latency = new(Duration.FromMinutes(10), Duration.Zero)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromMinutes(10), 100m, 100.05m),
                (t0 + Duration.FromMinutes(11), 100m, 100.05m),
                (t0 + Duration.FromMinutes(12), 98.95m, 99m),
                (t0 + Duration.FromMinutes(21), 98m, 98.05m)),
            config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var cancelRequested = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        new Price(99m, Currency.USD)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }

            if (evt is QuoteReceived quote
                && quote.Quote.Bid.Value == 100m
                && submitted
                && events.EmittedEvents.Any(static emitted => emitted is OrderAccepted)
                && !cancelRequested)
            {
                cancelRequested = true;
                connector.CancelOrderAsync(new CancelOrder(orderId), CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(99m, fill.FillPrice.Value);
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderCancelled);
    }

    [Fact]
    public async Task ModifyOrderAsync_DelaysModifyForOpenOrderUntilEntryLatencyElapsed()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Latency = new(Duration.FromMinutes(10), Duration.Zero)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromMinutes(10), 100m, 100.05m),
                (t0 + Duration.FromMinutes(11), 101m, 101.05m),
                (t0 + Duration.FromMinutes(12), 101m, 101.05m),
                (t0 + Duration.FromMinutes(22), 102m, 102.05m)),
            config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var modifyRequested = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        new Price(99m, Currency.USD)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }

            if (evt is QuoteReceived quote
                && quote.Quote.Bid.Value == 101m
                && submitted
                && events.EmittedEvents.Any(static emitted => emitted is OrderAccepted)
                && !modifyRequested)
            {
                modifyRequested = true;
                connector.ModifyOrderAsync(
                    new ModifyOrder(orderId, new Qty(2m), new Price(102.05m, Currency.USD)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(2m, fill.FilledQty.Value);
        Assert.Equal(102.05m, fill.FillPrice.Value);

        var secondQuoteIndex = events.EmittedEvents.FindIndex(evt =>
            evt is QuoteReceived quote && quote.Quote.Bid.Value == 101m);
        var fillIndex = events.EmittedEvents.FindIndex(evt => evt is OrderFilled);
        Assert.True(secondQuoteIndex < fillIndex);
    }

    [Fact]
    public async Task StartAsync_CancelsOpenOcoSiblingWhenOneOrderFills()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuoteThenBar(100m, 106m, 94m, 100m),
            MarginConfig());
        var events = new TestEventPublisher();
        var orderListId = OrderListId.New();
        var takeProfitId = OrderId.New();
        var stopLossId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m),
                        new Price(105m, Currency.USD)) with
                    {
                        OrderId = takeProfitId,
                        OrderListId = orderListId,
                        ContingencyType = ContingencyType.OCO
                    },
                    CancellationToken.None).GetAwaiter().GetResult();

                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m),
                        new Price(95m, Currency.USD)) with
                    {
                        OrderId = stopLossId,
                        OrderListId = orderListId,
                        ContingencyType = ContingencyType.OCO
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var ocoOrderIds = new[] { takeProfitId, stopLossId };
        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Contains(fill.OrderId, ocoOrderIds);

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Contains(cancellation.OrderId, ocoOrderIds);
        Assert.NotEqual(fill.OrderId, cancellation.OrderId);
        Assert.Contains("OCO sibling", cancellation.Reason);
    }

    [Fact]
    public async Task StartAsync_ActivatesOtoChildrenOnlyAfterParentFills()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuoteThenBars(
                (100m, 102m, 98m, 101m),
                (101m, 101m, 98m, 99m)));
        var events = new TestEventPublisher();
        var orderListId = OrderListId.New();
        var entryId = OrderId.New();
        var stopId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        new Price(101m, Currency.USD)) with
                    {
                        OrderId = entryId,
                        OrderListId = orderListId,
                        ContingencyType = ContingencyType.OTO
                    },
                    CancellationToken.None).GetAwaiter().GetResult();

                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m),
                        new Price(99m, Currency.USD)) with
                    {
                        OrderId = stopId,
                        OrderListId = orderListId,
                        ContingencyType = ContingencyType.OTO
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([entryId, stopId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal(101m, fills[0].FillPrice.Value);
        Assert.Equal(99m, fills[1].FillPrice.Value);

        var accepted = events.EmittedEvents.OfType<OrderAccepted>().ToArray();
        Assert.Equal([entryId, stopId], accepted.Select(static evt => evt.OrderId).ToArray());
        Assert.True(events.EmittedEvents.FindIndex(evt => evt is OrderFilled fill && fill.OrderId == entryId)
            < events.EmittedEvents.FindIndex(evt => evt is OrderAccepted acceptedEvent && acceptedEvent.OrderId == stopId));
    }

    [Fact]
    public async Task CancelOrderAsync_CancelsStagedOtoChildBeforeParentFills()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuoteThenBars(
                (100m, 102m, 98m, 101m),
                (101m, 101m, 98m, 99m)));
        var events = new TestEventPublisher();
        var orderListId = OrderListId.New();
        var entryId = OrderId.New();
        var stopId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        new Price(101m, Currency.USD)) with
                    {
                        OrderId = entryId,
                        OrderListId = orderListId,
                        ContingencyType = ContingencyType.OTO
                    },
                    CancellationToken.None).GetAwaiter().GetResult();

                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m),
                        new Price(99m, Currency.USD)) with
                    {
                        OrderId = stopId,
                        OrderListId = orderListId,
                        ContingencyType = ContingencyType.OTO
                    },
                    CancellationToken.None).GetAwaiter().GetResult();

                connector.CancelOrderAsync(new CancelOrder(stopId), CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(entryId, fill.OrderId);

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(stopId, cancellation.OrderId);
        Assert.Contains("staged", cancellation.Reason);

        Assert.DoesNotContain(events.EmittedEvents, evt => evt is OrderAccepted accepted && accepted.OrderId == stopId);
        Assert.DoesNotContain(events.EmittedEvents, evt => evt is OrderFilled filled && filled.OrderId == stopId);
    }

    [Fact]
    public async Task ModifyOrderAsync_UpdatesStagedOtoChildBeforeParentFills()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuoteThenBars(
                (100m, 102m, 98m, 101m),
                (101m, 100m, 98m, 99m)));
        var events = new TestEventPublisher();
        var orderListId = OrderListId.New();
        var entryId = OrderId.New();
        var childId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        new Price(101m, Currency.USD)) with
                    {
                        OrderId = entryId,
                        OrderListId = orderListId,
                        ContingencyType = ContingencyType.OTO
                    },
                    CancellationToken.None).GetAwaiter().GetResult();

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m),
                        new Price(110m, Currency.USD)) with
                    {
                        OrderId = childId,
                        OrderListId = orderListId,
                        ContingencyType = ContingencyType.OTO
                    },
                    CancellationToken.None).GetAwaiter().GetResult();

                connector.ModifyOrderAsync(
                    new ModifyOrder(childId, new Qty(0.5m), new Price(99m, Currency.USD)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([entryId, childId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal(101m, fills[0].FillPrice.Value);
        Assert.Equal(1m, fills[0].FilledQty.Value);
        Assert.Equal(99m, fills[1].FillPrice.Value);
        Assert.Equal(0.5m, fills[1].FilledQty.Value);

        Assert.DoesNotContain(events.EmittedEvents, evt => evt is OrderRejected rejected && rejected.OrderId == childId);
    }

    [Fact]
    public async Task StartAsync_PartiallyFillsRestingLimitFromTradeSizesWhenConfigured()
    {
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(4m, 6m), config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(100m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.All(fills, fill => Assert.Equal(orderId, fill.OrderId));
        Assert.Equal(4m, fills[0].FilledQty.Value);
        Assert.Equal(6m, fills[1].FilledQty.Value);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderCancelled);

        var states = events.EmittedEvents
            .OfType<OrderStateSnapshot>()
            .Where(state => state.OrderId == orderId)
            .ToArray();
        Assert.Equal(3, states.Length);
        Assert.Equal(OrderStatus.Open, states[0].Status);
        Assert.Equal(OrderStatus.PartiallyFilled, states[1].Status);
        Assert.Equal(4m, states[1].FilledQty?.Value);
        Assert.Equal(6m, states[1].RemainingQty?.Value);
        Assert.Equal(OrderStatus.Filled, states[2].Status);
        Assert.Equal(10m, states[2].FilledQty?.Value);
        Assert.Equal(0m, states[2].RemainingQty?.Value);
    }

    [Fact]
    public async Task StartAsync_AllocatesTradeQuantityAcrossSamePriceRestingLimitsInFifoOrder()
    {
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(6m, 4m), config);
        var events = new TestEventPublisher();
        var firstOrderId = OrderId.New();
        var secondOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(5m),
                        P(100m)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(5m),
                        P(100m)) with
                    {
                        OrderId = secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([firstOrderId, secondOrderId, secondOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([5m, 1m, 4m], fills.Select(static fill => fill.FilledQty.Value).ToArray());
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderCancelled);
    }

    [Fact]
    public async Task ModifyOrderAsync_IncreasingRestingQuantityLosesFifoPriority()
    {
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(1m), config);
        var events = new TestEventPublisher();
        var firstOrderId = OrderId.New();
        var secondOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m), P(100m)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m), P(100m)) with
                    {
                        OrderId = secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.ModifyOrderAsync(
                    new ModifyOrder(firstOrderId, NewQuantity: new Qty(2m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(secondOrderId, fill.OrderId);
    }

    [Fact]
    public async Task ModifyOrderAsync_ReducingRestingQuantityPreservesFifoPriority()
    {
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(1m), config);
        var events = new TestEventPublisher();
        var firstOrderId = OrderId.New();
        var secondOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Buy, new Qty(2m), P(100m)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m), P(100m)) with
                    {
                        OrderId = secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.ModifyOrderAsync(
                    new ModifyOrder(firstOrderId, NewQuantity: new Qty(1m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(firstOrderId, fill.OrderId);
    }

    [Fact]
    public async Task StartAsync_AllocatesTradeQuantityToBetterPricedBuyLimitsBeforeFifo()
    {
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(1m), config);
        var events = new TestEventPublisher();
        var worseOrderId = OrderId.New();
        var betterOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        P(100m)) with
                    {
                        OrderId = worseOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(1m),
                        P(101m)) with
                    {
                        OrderId = betterOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(betterOrderId, fill.OrderId);
        Assert.Equal(1m, fill.FilledQty.Value);
    }

    [Fact]
    public async Task StartAsync_AllocatesTradeQuantityByPriceLevelThenFifoWithinLevel()
    {
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(2m), config);
        var events = new TestEventPublisher();
        var worseOrderId = OrderId.New();
        var firstBetterOrderId = OrderId.New();
        var secondBetterOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m), P(100m)) with
                    {
                        OrderId = worseOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m), P(101m)) with
                    {
                        OrderId = firstBetterOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m), P(101m)) with
                    {
                        OrderId = secondBetterOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([firstBetterOrderId, secondBetterOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([1m, 1m], fills.Select(static fill => fill.FilledQty.Value).ToArray());
        Assert.DoesNotContain(fills, fill => fill.OrderId == worseOrderId);
    }

    [Fact]
    public async Task SubmitOrderAsync_MatchesCrossedLimitAgainstRestingReplayBook()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes((99m, 102m)), MarginConfig());
        var events = new TestEventPublisher();
        var sellOrderId = OrderId.New();
        var buyOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Sell, new Qty(1m), P(101m)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(8), TestInstrument, Side.Buy, new Qty(1m), P(101m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([sellOrderId, buyOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([Side.Sell, Side.Buy], fills.Select(static fill => fill.Side).ToArray());
        Assert.Equal([101m, 101m], fills.Select(static fill => fill.FillPrice.Value).ToArray());
        Assert.Equal([new StrategyId(7), new StrategyId(8)], fills.Select(static fill => fill.StrategyId).ToArray());
        Assert.Equal(2, events.EmittedEvents.OfType<OrderAccepted>().Count());
    }

    [Fact]
    public async Task SubmitOrderAsync_RestsCrossedLimitRemainderInReplayBook()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes((99m, 102m)), MarginConfig());
        var events = new TestEventPublisher();
        var firstSellOrderId = OrderId.New();
        var buyOrderId = OrderId.New();
        var secondSellOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Sell, new Qty(1m), P(101m)) with
                    {
                        OrderId = firstSellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(8), TestInstrument, Side.Buy, new Qty(2m), P(101m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(9), TestInstrument, Side.Sell, new Qty(1m), P(101m)) with
                    {
                        OrderId = secondSellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([firstSellOrderId, buyOrderId, buyOrderId, secondSellOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([Side.Sell, Side.Buy, Side.Buy, Side.Sell], fills.Select(static fill => fill.Side).ToArray());
        Assert.All(fills, fill => Assert.Equal(1m, fill.FilledQty.Value));
        Assert.Equal(3, events.EmittedEvents.OfType<OrderAccepted>().Count());
    }

    [Fact]
    public async Task SubmitOrderAsync_IocCrossedLimitCancelsOnlyUnfilledRemainder()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes((99m, 102m)), MarginConfig());
        var events = new TestEventPublisher();
        var sellOrderId = OrderId.New();
        var buyOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Sell, new Qty(1m), P(101m)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(8), TestInstrument, Side.Buy, new Qty(2m), P(101m)) with
                    {
                        OrderId = buyOrderId,
                        TimeInForce = TimeInForce.IOC
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([sellOrderId, buyOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.All(fills, fill => Assert.Equal(1m, fill.FilledQty.Value));

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(buyOrderId, cancellation.OrderId);
        Assert.Equal(new Qty(1m), cancellation.RemainingQty);
        Assert.Contains("IOC", cancellation.Reason);
    }

    [Fact]
    public async Task SubmitOrderAsync_FokCrossedLimitCancelsWithoutPartialFillWhenRestingLiquidityInsufficient()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes((99m, 102m)), MarginConfig());
        var events = new TestEventPublisher();
        var sellOrderId = OrderId.New();
        var buyOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Sell, new Qty(1m), P(101m)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(8), TestInstrument, Side.Buy, new Qty(2m), P(101m)) with
                    {
                        OrderId = buyOrderId,
                        TimeInForce = TimeInForce.FOK
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Empty(events.EmittedEvents.OfType<OrderFilled>());
        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(buyOrderId, cancellation.OrderId);
        Assert.Equal(new Qty(2m), cancellation.RemainingQty);
        Assert.Contains("FOK", cancellation.Reason);
    }

    [Fact]
    public async Task SubmitOrderAsync_MarketOrderMatchesRestingReplayBookBeforeExternalDepth()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes((99m, 102m)), MarginConfig());
        var events = new TestEventPublisher();
        var sellOrderId = OrderId.New();
        var buyOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Sell, new Qty(1m), P(101m)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(8), TestInstrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([sellOrderId, buyOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([Side.Sell, Side.Buy], fills.Select(static fill => fill.Side).ToArray());
        Assert.Equal([101m, 101m], fills.Select(static fill => fill.FillPrice.Value).ToArray());
    }

    [Fact]
    public async Task SubmitOrderAsync_MarketOrderUsesExternalDepthAfterRestingReplayBook()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes((99m, 102m)), MarginConfig());
        var events = new TestEventPublisher();
        var sellOrderId = OrderId.New();
        var buyOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Sell, new Qty(1m), P(101m)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(8), TestInstrument, Side.Buy, new Qty(2m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([sellOrderId, buyOrderId, buyOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([Side.Sell, Side.Buy, Side.Buy], fills.Select(static fill => fill.Side).ToArray());
        Assert.Equal([101m, 101m, 102m], fills.Select(static fill => fill.FillPrice.Value).ToArray());
        Assert.All(fills, fill => Assert.Equal(1m, fill.FilledQty.Value));
    }

    [Fact]
    public async Task SubmitOrderAsync_MarketOrderDoesNotSelfMatchSameStrategyRestingOrder()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes((99m, 102m)), MarginConfig());
        var events = new TestEventPublisher();
        var sellOrderId = OrderId.New();
        var buyOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Sell, new Qty(1m), P(101m)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(buyOrderId, fill.OrderId);
        Assert.Equal(Side.Buy, fill.Side);
        Assert.Equal(102m, fill.FillPrice.Value);
        Assert.DoesNotContain(events.EmittedEvents.OfType<OrderFilled>(), filled => filled.OrderId == sellOrderId);
    }

    [Fact]
    public async Task SubmitOrderAsync_FokMarketOrderCancelsWithoutPartialFillWhenReplayLiquidityInsufficient()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes((99m, 102m)), MarginConfig());
        var events = new TestEventPublisher();
        var sellOrderId = OrderId.New();
        var buyOrderId = OrderId.New();
        var submitted = false;
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Sell, new Qty(1m), P(101m)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(8), TestInstrument, Side.Buy, new Qty(2m)) with
                    {
                        OrderId = buyOrderId,
                        TimeInForce = TimeInForce.FOK
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync([], events, CancellationToken.None);

        Assert.Empty(events.EmittedEvents.OfType<OrderFilled>());
        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(buyOrderId, cancellation.OrderId);
        Assert.Equal(new Qty(2m), cancellation.RemainingQty);
        Assert.Contains("FOK market", cancellation.Reason);
    }

    [Fact]
    public async Task SubmitOrderAsync_FokMarketOrderUsesRestingReplayBookAndExternalDepthWhenFullyAvailable()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes((99m, 102m)), MarginConfig());
        var events = new TestEventPublisher();
        var sellOrderId = OrderId.New();
        var buyOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(7), TestInstrument, Side.Sell, new Qty(1m), P(101m)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(8), TestInstrument, Side.Buy, new Qty(2m)) with
                    {
                        OrderId = buyOrderId,
                        TimeInForce = TimeInForce.FOK
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([sellOrderId, buyOrderId, buyOrderId], fills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([101m, 101m, 102m], fills.Select(static fill => fill.FillPrice.Value).ToArray());
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderCancelled);
    }

    [Fact]
    public async Task StartAsync_AllocatesTradeQuantityToBetterPricedSellLimitsBeforeFifo()
    {
        var config = MarginConfig() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBuyTrade(1m), config);
        var events = new TestEventPublisher();
        var worseOrderId = OrderId.New();
        var betterOrderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m),
                        P(100m)) with
                    {
                        OrderId = worseOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(1m),
                        P(99m)) with
                    {
                        OrderId = betterOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(betterOrderId, fill.OrderId);
        Assert.Equal(1m, fill.FilledQty.Value);
    }

    [Fact]
    public async Task StartAsync_RefreshesIcebergDisplayQuantityAcrossTradeFills()
    {
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(10m, 10m, 10m), config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.IcebergLimit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(100m),
                        new Qty(4m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(3, fills.Length);
        Assert.All(fills, fill => Assert.Equal(orderId, fill.OrderId));
        Assert.Equal([4m, 4m, 2m], fills.Select(static fill => fill.FilledQty.Value).ToArray());
        Assert.Single(events.EmittedEvents.OfType<OrderAccepted>());
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsInvalidIcebergDisplayQuantity()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(10m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.IcebergLimit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(100m),
                        new Qty(10m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.Single(events.EmittedEvents.OfType<OrderRejected>());
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Contains("smaller than total", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderAccepted);
        Assert.DoesNotContain(events.EmittedEvents, static evt => evt is OrderFilled);
    }

    [Fact]
    public async Task StartAsync_UpdatesOuoSiblingQuantityAfterPartialParentFill()
    {
        var config = MarginConfig() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(4m), config);
        var events = new TestEventPublisher();
        var orderListId = OrderListId.New();
        var parentId = OrderId.New();
        var siblingId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(100m)) with
                    {
                        OrderId = parentId,
                        OrderListId = orderListId,
                        ContingencyType = ContingencyType.OUO
                    },
                    CancellationToken.None).GetAwaiter().GetResult();

                connector.SubmitOrderAsync(
                    SubmitOrder.StopMarket(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(10m),
                        P(90m)) with
                    {
                        OrderId = siblingId,
                        OrderListId = orderListId,
                        ContingencyType = ContingencyType.OUO
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);
        await connector.CancelOrderAsync(new CancelOrder(siblingId), CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(parentId, fill.OrderId);
        Assert.Equal(4m, fill.FilledQty.Value);

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(siblingId, cancellation.OrderId);
        Assert.Equal(4m, cancellation.RemainingQty.Value);
    }

    [Fact]
    public async Task StartAsync_ExpandsTwapIntoScheduledMarketSlices()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromMinutes(1), 101m, 101.05m),
                (t0 + Duration.FromMinutes(2), 102m, 102.05m),
                (t0 + Duration.FromMinutes(3), 103m, 103.05m)));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Twap(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(9m),
                        TimeSpan.FromMinutes(3),
                        TimeSpan.FromMinutes(1)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(3, fills.Length);
        Assert.All(fills, fill => Assert.Equal(orderId, fill.OrderId));
        Assert.Equal([3m, 3m, 3m], fills.Select(static fill => fill.FilledQty.Value).ToArray());
        Assert.Equal([101.05m, 102.05m, 103.05m], fills.Select(static fill => fill.FillPrice.Value).ToArray());
        Assert.Single(events.EmittedEvents.OfType<OrderAccepted>());
    }

    [Fact]
    public async Task StartAsync_ExpandsVwapByReplayEventParticipation()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuoteThenTrades(
                t0,
                (t0 + Duration.FromMinutes(1), 4m),
                (t0 + Duration.FromMinutes(2), 6m),
                (t0 + Duration.FromMinutes(3), 20m)));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Vwap(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        TimeSpan.FromMinutes(3),
                        participationRate: 0.5m) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(3, fills.Length);
        Assert.All(fills, fill => Assert.Equal(orderId, fill.OrderId));
        Assert.Equal([2m, 3m, 5m], fills.Select(static fill => fill.FilledQty.Value).ToArray());
        Assert.All(fills, fill => Assert.Equal(100.05m, fill.FillPrice.Value));
        Assert.Single(events.EmittedEvents.OfType<OrderAccepted>());
    }

    [Fact]
    public async Task StartAsync_ExpandsPovByReplayEventParticipationUntilFilled()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuoteThenTrades(
                t0,
                (t0 + Duration.FromMinutes(1), 10m),
                (t0 + Duration.FromMinutes(2), 6m),
                (t0 + Duration.FromMinutes(3), 20m)));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Pov(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(8m),
                        participationRate: 0.25m) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(3, fills.Length);
        Assert.All(fills, fill => Assert.Equal(orderId, fill.OrderId));
        Assert.Equal([2.5m, 1.5m, 4m], fills.Select(static fill => fill.FilledQty.Value).ToArray());
        Assert.All(fills, fill => Assert.Equal(100.05m, fill.FillPrice.Value));
        Assert.Single(events.EmittedEvents.OfType<OrderAccepted>());
        Assert.DoesNotContain(events.EmittedEvents, evt => evt is OrderCancelled cancelled && cancelled.OrderId == orderId);
    }

    [Fact]
    public async Task StartAsync_CancelsUnfilledPovRemainderAtReplayEnd()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuoteThenTrades(
                t0,
                (t0 + Duration.FromMinutes(1), 4m)));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Pov(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        participationRate: 0.5m) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.Single(events.EmittedEvents.OfType<OrderFilled>());
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(2m, fill.FilledQty.Value);

        var cancellation = Assert.Single(events.EmittedEvents.OfType<OrderCancelled>());
        Assert.Equal(orderId, cancellation.OrderId);
        Assert.Equal(8m, cancellation.RemainingQty.Value);
        Assert.Contains("Replay ended", cancellation.Reason);
    }

    [Fact]
    public async Task CancelOrderAsync_ReportsRemainingQuantityAfterPartialReplayFill()
    {
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(4m), config);
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(100m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);
        await connector.CancelOrderAsync(new CancelOrder(orderId), CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(4m, fill.FilledQty.Value);

        var cancellation = Assert.IsType<OrderCancelled>(
            events.EmittedEvents.Single(e => e is OrderCancelled));
        Assert.Equal(orderId, cancellation.OrderId);
        Assert.Equal(6m, cancellation.RemainingQty.Value);

        var states = events.EmittedEvents
            .OfType<OrderStateSnapshot>()
            .Where(state => state.OrderId == orderId)
            .ToArray();
        Assert.Equal(3, states.Length);
        Assert.Equal(OrderStatus.Open, states[0].Status);
        Assert.Equal(OrderStatus.PartiallyFilled, states[1].Status);
        Assert.Equal(4m, states[1].FilledQty?.Value);
        Assert.Equal(6m, states[1].RemainingQty?.Value);
        Assert.Equal(OrderStatus.Cancelled, states[2].Status);
        Assert.Equal(6m, states[2].RemainingQty?.Value);
    }

    [Fact]
    public async Task StartAsync_NoPartialFillBehaviorStillFillsFullRemainingQuantityFromTrade()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenTrades(4m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is TradeOccurred && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(100m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(10m, fill.FilledQty.Value);
    }

    [Fact]
    public async Task StartAsync_FillsMarketIfTouchedBuyWhenReplayBarTouchesTrigger()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBar(100m, 101m, 98m, 99m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.MarketIfTouched(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(99m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static e => e is OrderAccepted);
        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(99m, fill.FillPrice.Value);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderCancelled);
    }

    [Fact]
    public async Task StartAsync_LeavesMarketIfTouchedOpenWhenTriggerIsNotTouched()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBar(100m, 101m, 99m, 100m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.MarketIfTouched(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(98m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static e => e is OrderAccepted);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderCancelled);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderRejected);
    }

    [Fact]
    public async Task StartAsync_FillsLimitIfTouchedWhenTriggerAndLimitAreTouched()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBar(100m, 101m, 98m, 99m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.LimitIfTouched(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(99m),
                        P(98.50m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(98.50m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_DoesNotFillLimitIfTouchedUntilTriggerIsTouched()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBar(100m, 101m, 99m, 100m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.LimitIfTouched(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        P(98m),
                        P(99.50m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        Assert.Contains(events.EmittedEvents, static e => e is OrderAccepted);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderCancelled);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderRejected);
    }

    [Fact]
    public async Task StartAsync_FillsMarketToLimitAtCurrentMarketPrice()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    new SubmitOrder(
                        orderId,
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        OrderType.MarketToLimit),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(100.05m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_TrailingStopMarketSellTracksHighAndTriggersOnReversal()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuoteThenBar(100m, 110m, 106m, 108m),
            MarginConfig());
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.TrailingStop(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(10m),
                        3m,
                        TrailingOffsetType.Price) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(107m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_TrailingStopLimitSellTracksHighAndFillsLimitAfterTrigger()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuoteThenBar(100m, 110m, 106m, 108m),
            MarginConfig());
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.TrailingStopLimit(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(10m),
                        3m,
                        TrailingOffsetType.Price,
                        P(106.50m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(106.50m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_TrailingStopMarketBuyTracksLowAndTriggersOnBounce()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuoteThenBar(100m, 94m, 90m, 92m));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.TrailingStop(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Buy,
                        new Qty(10m),
                        3m,
                        TrailingOffsetType.Price) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(93m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_TrailingStopMarketSupportsTickOffsets()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuoteThenBar(100m, 110m, 106m, 108m),
            MarginConfig());
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.TrailingStop(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(10m),
                        300m,
                        TrailingOffsetType.Ticks) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(107m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task StartAsync_TrailingStopMarketSupportsPercentOffsets()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithQuoteThenBar(100m, 110m, 98m, 108m),
            MarginConfig());
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    SubmitOrder.TrailingStop(
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(10m),
                        10m,
                        TrailingOffsetType.Percent) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var fill = Assert.IsType<OrderFilled>(
            events.EmittedEvents.Single(e => e is OrderFilled));
        Assert.Equal(orderId, fill.OrderId);
        Assert.Equal(99m, fill.FillPrice.Value);
    }

    [Fact]
    public async Task SubmitOrderAsync_RejectsTrailingStopWithoutOffsetMetadata()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1));
        var events = new TestEventPublisher();
        var orderId = OrderId.New();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived)
            {
                connector.SubmitOrderAsync(
                    new SubmitOrder(
                        orderId,
                        new StrategyId(7),
                        TestInstrument,
                        Side.Sell,
                        new Qty(10m),
                        OrderType.TrailingStopMarket),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.IsType<OrderRejected>(
            events.EmittedEvents.Single(e => e is OrderRejected));
        Assert.Equal(orderId, rejection.OrderId);
        Assert.Contains("requires trailing offset", rejection.Reason);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderAccepted);
        Assert.DoesNotContain(events.EmittedEvents, static e => e is OrderFilled);
    }

    [Fact]
    public async Task StartAsync_CanBeCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var connector = new ReplayConnector(CreateHistoryWithQuotes(100));
        var events = new TestEventPublisher();

        // Should either throw OperationCanceledException or complete quickly
        // (behavior depends on how the async enumerable handles cancellation)
        try
        {
            await connector.StartAsync([], events, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        Assert.False(connector.IsConnected);
    }

    [Fact]
    public async Task SubmitOrderAsync_ThrowsWhenNotStarted()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());
        var order = CreateSubmitOrder();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.SubmitOrderAsync(order, CancellationToken.None));
    }

    [Fact]
    public async Task CancelOrderAsync_ThrowsWhenNotStarted()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());
        var cancel = new CancelOrder { OrderId = OrderId.New() };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.CancelOrderAsync(cancel, CancellationToken.None));
    }

    [Fact]
    public async Task ModifyOrderAsync_ThrowsWhenNotStarted()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());
        var modify = new ModifyOrder { OrderId = OrderId.New() };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.ModifyOrderAsync(modify, CancellationToken.None));
    }

    [Fact]
    public void Dispose_ClearsState()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());

        connector.Dispose();

        Assert.False(connector.IsConnected);
    }

    [Fact]
    public void Exchange_ReturnsReplay()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());

        Assert.Equal(ExchangeId.Replay, connector.Exchange);
    }

    [Fact]
    public void RateLimiter_ReturnsNoopRateLimiter()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());

        Assert.IsType<NoopRateLimiter>(connector.RateLimiter);
        Assert.Same(NoopRateLimiter.Instance, connector.RateLimiter);
    }

    private static SubmitOrder CreateSubmitOrder(
        decimal qty = 100m,
        decimal limitPrice = 100m,
        OrderType type = OrderType.Limit)
    {
        return new SubmitOrder
        {
            OrderId = OrderId.New(),
            Instrument = TestInstrument,
            VariantId = 0,
            Side = Side.Buy,
            Quantity = new Qty(qty),
            Type = type,
            LimitPrice = new Price(limitPrice, Currency.USD)
        };
    }

    private static Price P(decimal value) => new(value, Currency.USD);

    private static SimulationConfig MarginConfig(decimal leverage = 2m, Duration marginCallGracePeriod = default)
        => SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(leverage).WithMarginCallGracePeriod(marginCallGracePeriod)
        };

    /// <summary>
    /// Simple test coordinator for capturing emitted events.
    /// </summary>
    private sealed class TestEventPublisher : IEventPublisher
    {
        public List<Event> EmittedEvents { get; } = [];
        public Action<Event>? OnEmit { get; set; }

        public void Emit(Event evt)
        {
            EmittedEvents.Add(evt);
            OnEmit?.Invoke(evt);
        }

        public ValueTask EmitAsync(Event evt, CancellationToken ct = default) { Emit(evt); return ValueTask.CompletedTask; }
    }

    private sealed class ClosingReplayModule : IReplaySimulationModule
    {
        private readonly Instrument _instrument;
        private readonly Instant _closeAt;
        private bool _emitted;

        public ClosingReplayModule(Instrument instrument, Instant closeAt)
        {
            _instrument = instrument;
            _closeAt = closeAt;
        }

        public int PreProcessCount { get; private set; }
        public int ProcessCount { get; private set; }

        public void PreProcess(
            in FinanceEvent evt,
            ReplayModuleContext context,
            ReplayModuleSinks sinks)
        {
            if (evt is QuoteReceived)
                PreProcessCount++;
        }

        public void Process(
            Instant now,
            ReplayModuleContext context,
            ReplayModuleSinks sinks)
        {
            ProcessCount++;
            if (_emitted || now != _closeAt)
                return;

            _emitted = true;
            sinks.Emit(new InstrumentStatusChanged(_instrument, MarketStatus.Closed, "module close")
            {
                Time = now
            });
        }

        public void Reset()
        {
            _emitted = false;
        }
    }

    private sealed class NoopSubscription : IDisposable
    {
        public void Dispose() { }
    }
}
