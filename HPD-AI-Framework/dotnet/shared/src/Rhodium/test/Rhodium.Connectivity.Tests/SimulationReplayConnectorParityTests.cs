using HPD.Events;
using HPD.Events.Core;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Primitives;
using Rhodium.Simulation;
using Rhodium.Simulation.Diagnostics;
using Rhodium.Simulation.Exchange;
using Rhodium.Simulation.Identity;
using Rhodium.Simulation.Modules;

namespace Rhodium.Connectivity.Tests;

public sealed class SimulationReplayConnectorParityTests
{
    private static readonly Instrument Instrument = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
    private static readonly Instrument PerpetualInstrument = new(new Asset("BTCUSD-PERP", AssetClass.Crypto), Venue.Binance);
    private static readonly Instrument ForexInstrument = new(new Asset("EURUSD", AssetClass.Forex), "IDEALPRO");
    private static readonly StrategyId StrategyId = new(7);
    private static readonly AssetId AssetId = new(0);

    [Fact]
    public async Task MarketOrderFromQuote_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(101);
        var simulationOrderId = new OrderId(101);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Market()),
                    GetEventTime(evt));
            });

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));

        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());

        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketOrderAckPolicy_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(111);
        var simulationOrderId = new OrderId(111);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
                {
                    [Instrument.Venue] = ReplayVenueSimulationPolicy.Default with
                    {
                        UseMarketOrderAcks = true
                    }
                };
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Market()),
                    GetEventTime(evt));
            },
            SimulationVenuePolicy.Default with
            {
                UseMarketOrderAcks = true
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task FrozenAccountPolicy_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(121);
        var simulationOrderId = new OrderId(121);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
                {
                    [Instrument.Venue] = ReplayVenueSimulationPolicy.Default with
                    {
                        FrozenAccount = true
                    }
                };
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Market()),
                    GetEventTime(evt));
            },
            SimulationVenuePolicy.Default with
            {
                FrozenAccount = true
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "frozen");
    }

    [Fact]
    public async Task AllowCashBorrowingPolicy_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var initialCash = Money.USD(50m);
        var replayOrderId = new OrderId(131);
        var simulationOrderId = new OrderId(131);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
                {
                    [Instrument.Venue] = ReplayVenueSimulationPolicy.Default with
                    {
                        AllowCashBorrowing = true
                    }
                };
            },
            initialCash);

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Market()),
                    GetEventTime(evt));
            },
            SimulationVenuePolicy.Default with
            {
                AllowCashBorrowing = true
            },
            initialCash);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task InsufficientCashBuyingPowerRejectsOrder_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var initialCash = Money.USD(50m);
        var replayOrderId = new OrderId(132);
        var simulationOrderId = new OrderId(132);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: initialCash);

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Market()),
                        quote.Time);
                }
            },
            initialCash: initialCash);

        AssertRejectedOnly(replayEvents, simulationEvents, "insufficient", "cash");
    }

    [Fact]
    public async Task CashSecondBuyRejectedAfterFirstDebit_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(10);
        var config = SimulationConfig.Instant();
        var initialCash = Money.USD(600m);
        var firstOrderId = new OrderId(10_132);
        var secondOrderId = new OrderId(10_133);

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 100m, ask: 100.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        StrategyId,
                        Instrument,
                        Side.Buy,
                        replayQuoteCount == 1 ? new Qty(5m) : new Qty(1m)) with
                    {
                        OrderId = replayQuoteCount == 1 ? firstOrderId : secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: initialCash);

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 100m, ask: 100.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                simulationQuoteCount++;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationQuoteCount == 1 ? firstOrderId : secondOrderId,
                        Side.Buy,
                        simulationQuoteCount == 1 ? new Qty(5m) : new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            },
            initialCash: initialCash);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(firstOrderId, replayFill.OrderId);
        Assert.Equal(new Qty(5m), replayFill.FilledQty);

        var replayRejected = Assert.Single(replayEvents.OfType<OrderRejected>());
        var simulationRejected = Assert.Single(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(NormalizeRejected(replayRejected), NormalizeRejected(simulationRejected));
        Assert.Equal(secondOrderId, replayRejected.OrderId);
        Assert.Contains("Insufficient cash", replayRejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Insufficient cash", simulationRejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
    }

    [Fact]
    public async Task CashOpenBuyReservationReducesAvailableCash_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(11);
        var config = SimulationConfig.Instant();
        var initialCash = Money.USD(150m);
        var restingOrderId = new OrderId(10_134);
        var rejectedOrderId = new OrderId(10_135);

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 100m, ask: 100.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                if (replayQuoteCount == 1)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = restingOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                    return;
                }

                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = rejectedOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: initialCash);

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 100m, ask: 100.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                simulationQuoteCount++;
                if (simulationQuoteCount == 1)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            restingOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Limit().At(new Price(99m, Currency.USD))),
                        quote.Time);
                    return;
                }

                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        rejectedOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            },
            initialCash: initialCash);

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayRejected = Assert.Single(replayEvents.OfType<OrderRejected>());
        var simulationRejected = Assert.Single(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(NormalizeRejected(replayRejected), NormalizeRejected(simulationRejected));
        Assert.Equal(rejectedOrderId, replayRejected.OrderId);
        Assert.Contains("Insufficient cash", replayRejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Insufficient cash", simulationRejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
    }

    [Fact]
    public async Task ReservedCashAccountStatementForOpenOrders_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(12);
        var config = SimulationConfig.Instant();
        var initialCash = Money.USD(200m);
        var orderId = new OrderId(10_136);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = orderId,
                            TimeInForce = TimeInForce.GTC
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: initialCash);

        var venue = new SimulatedVenueExchange(Instrument.Venue, config, initialCash);
        var executionBuffer = new ExecutionEvent[16];
        var simulationBuffer = new FinanceEvent[16];
        var simulationEvents = new List<Event>();
        var quote = CreateQuote(now, bid: 100m, ask: 100.05m);
        venue.OnMarketEvent(quote);
        venue.Submit(
            new SimulationOrderCommand(
                StrategyId,
                VariantId: 0,
                AssetId,
                Instrument,
                Instrument.Venue,
                orderId,
                Side.Buy,
                new Qty(1m),
                Execution.Limit().At(new Price(99m, Currency.USD)).GoodTilCancelled()),
            quote.Time);
        venue.DrainDueWork(now);
        DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());

        var replayStatement = Assert.Single(replayEvents.OfType<AccountStatementSnapshot>());
        var simulationStatement = venue.CreateAccountStatement(StrategyId, 0, Currency.USD, now);
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
        Assert.Equal(Money.USD(99m), simulationStatement.ReservedCash);
        Assert.Equal(Money.USD(101m), simulationStatement.AvailableCash);
        Assert.Equal(1, simulationStatement.OpenOrders);
    }

    [Fact]
    public async Task CashAccountSellWithoutInventoryRejectsOrder_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(133);
        var simulationOrderId = new OrderId(133);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Sell, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Sell,
                            new Qty(1m),
                            Execution.Market()),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents);
    }

    [Fact]
    public async Task CashSellAfterExistingLongPosition_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var buyOrderId = new OrderId(10_193);
        var sellOrderId = new OrderId(10_194);
        var history =
            new[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 101m, ask: 101.05m)
            };

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        StrategyId,
                        Instrument,
                        replayQuoteCount == 1 ? Side.Buy : Side.Sell,
                        new Qty(1m)) with
                    {
                        OrderId = replayQuoteCount == 1 ? buyOrderId : sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                simulationQuoteCount++;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationQuoteCount == 1 ? buyOrderId : sellOrderId,
                        simulationQuoteCount == 1 ? Side.Buy : Side.Sell,
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            });

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray());
        Assert.Equal(
            [buyOrderId, sellOrderId],
            replayEvents.OfType<OrderFilled>().Select(static fill => fill.OrderId).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
    }

    [Fact]
    public async Task CashAccountSellBeforeAssetDeliveryRejectsOrder_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.CalendarDays(1)
        };
        var buyReplayOrderId = new OrderId(170);
        var sellReplayOrderId = new OrderId(171);
        var buySimulationOrderId = new OrderId(170);
        var sellSimulationOrderId = new OrderId(171);

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        StrategyId,
                        Instrument,
                        replayQuoteCount == 1 ? Side.Buy : Side.Sell,
                        new Qty(1m)) with
                    {
                        OrderId = replayQuoteCount == 1 ? buyReplayOrderId : sellReplayOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 101m, ask: 101.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                simulationQuoteCount++;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationQuoteCount == 1 ? buySimulationOrderId : sellSimulationOrderId,
                        simulationQuoteCount == 1 ? Side.Buy : Side.Sell,
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            });

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));

        var replayRejected = Assert.Single(replayEvents.OfType<OrderRejected>());
        var simulationRejected = Assert.Single(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(NormalizeRejected(replayRejected), NormalizeRejected(simulationRejected));
        Assert.Equal(sellReplayOrderId, replayRejected.OrderId);
        Assert.Equal(sellSimulationOrderId, simulationRejected.OrderId);
    }

    [Fact]
    public async Task CashAccountSellAfterAssetDeliverySettles_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.CalendarDays(1)
        };
        var buyReplayOrderId = new OrderId(172);
        var sellReplayOrderId = new OrderId(173);
        var buySimulationOrderId = new OrderId(172);
        var sellSimulationOrderId = new OrderId(173);

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromDays(2), bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        StrategyId,
                        Instrument,
                        replayQuoteCount == 1 ? Side.Buy : Side.Sell,
                        new Qty(1m)) with
                    {
                        OrderId = replayQuoteCount == 1 ? buyReplayOrderId : sellReplayOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromDays(2), bid: 101m, ask: 101.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                simulationQuoteCount++;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationQuoteCount == 1 ? buySimulationOrderId : sellSimulationOrderId,
                        simulationQuoteCount == 1 ? Side.Buy : Side.Sell,
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            });

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray();
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal(2, replayFills.Length);
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());

        var replayScheduled = Assert.Single(replayEvents.OfType<AssetDeliveryScheduled>());
        var simulationScheduled = Assert.Single(simulationEvents.OfType<AssetDeliveryScheduled>());
        Assert.Equal(NormalizeAssetDeliveryScheduled(replayScheduled), NormalizeAssetDeliveryScheduled(simulationScheduled));

        var replayDelivered = Assert.Single(replayEvents.OfType<AssetDelivered>());
        var simulationDelivered = Assert.Single(simulationEvents.OfType<AssetDelivered>());
        Assert.Equal(replayScheduled.DeliveryId, replayDelivered.DeliveryId);
        Assert.Equal(simulationScheduled.DeliveryId, simulationDelivered.DeliveryId);
        Assert.Equal(NormalizeAssetDelivered(replayDelivered), NormalizeAssetDelivered(simulationDelivered));

        Assert.Equal(
            replayEvents.OfType<AssetDeliveryStatusSnapshot>().Select(NormalizeAssetDeliveryStatus).ToArray(),
            simulationEvents.OfType<AssetDeliveryStatusSnapshot>().Select(NormalizeAssetDeliveryStatus).ToArray());
    }

    [Fact]
    public async Task CashAccountBoughtAssetDeliveryUpdatesSettledCustody_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var deliveryTime = t0 + Duration.FromDays(2);
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.CalendarDays(1)
        };
        var orderId = new OrderId(774);

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(deliveryTime, bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not QuoteReceived)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: Money.USD(200m));

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(deliveryTime, bid: 101m, ask: 101.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not QuoteReceived quote)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            },
            initialCash: Money.USD(200m));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));

        var replayScheduled = Assert.Single(replayEvents.OfType<AssetDeliveryScheduled>());
        var simulationScheduled = Assert.Single(simulationEvents.OfType<AssetDeliveryScheduled>());
        Assert.Equal(NormalizeAssetDeliveryScheduled(replayScheduled), NormalizeAssetDeliveryScheduled(simulationScheduled));

        var replayDelivered = Assert.Single(replayEvents.OfType<AssetDelivered>());
        var simulationDelivered = Assert.Single(simulationEvents.OfType<AssetDelivered>());
        Assert.Equal(NormalizeAssetDelivered(replayDelivered), NormalizeAssetDelivered(simulationDelivered));

        Assert.Equal(
            replayEvents.OfType<AssetDeliveryStatusSnapshot>().Select(NormalizeAssetDeliveryStatus).ToArray(),
            simulationEvents.OfType<AssetDeliveryStatusSnapshot>().Select(NormalizeAssetDeliveryStatus).ToArray());

        var replayCustody = replayEvents
            .OfType<CustodyPositionSnapshot>()
            .Where(snapshot => snapshot.StrategyId == StrategyId)
            .Select(NormalizeCustody)
            .ToArray();
        var simulationCustody = simulationEvents
            .OfType<CustodyPositionSnapshot>()
            .Where(snapshot => snapshot.StrategyId == StrategyId)
            .Select(NormalizeCustody)
            .ToArray();
        Assert.Contains(replayCustody, snapshot => snapshot.PendingDeliveryQuantity == new Qty(1m));
        Assert.Equal(replayCustody, simulationCustody);

        var finalCustody = simulationEvents
            .OfType<CustodyPositionSnapshot>()
            .Last(snapshot => snapshot.StrategyId == StrategyId);
        Assert.True(finalCustody.IsOpen);
        Assert.Equal(new Qty(1m), finalCustody.Quantity);
        Assert.Equal(new Qty(1m), finalCustody.SettledQuantity);
        Assert.Equal(Qty.Zero, finalCustody.PendingDeliveryQuantity);
        Assert.Equal(new Price(101m, Currency.USD), finalCustody.MarkPrice);
    }

    [Fact]
    public async Task UnsettledSaleAllowedCancelsPendingAssetDelivery_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.CalendarDays(1)
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var buyReplayOrderId = new OrderId(174);
        var sellReplayOrderId = new OrderId(175);
        var buySimulationOrderId = new OrderId(174);
        var sellSimulationOrderId = new OrderId(175);

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 101m, ask: 101.05m),
                CreateQuote(t0 + Duration.FromDays(2), bid: 102m, ask: 102.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived || replayQuoteCount >= 2)
                    return;

                replayQuoteCount++;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        StrategyId,
                        Instrument,
                        replayQuoteCount == 1 ? Side.Buy : Side.Sell,
                        new Qty(1m)) with
                    {
                        OrderId = replayQuoteCount == 1 ? buyReplayOrderId : sellReplayOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 101m, ask: 101.05m),
                CreateQuote(t0 + Duration.FromDays(2), bid: 102m, ask: 102.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote || simulationQuoteCount >= 2)
                    return;

                simulationQuoteCount++;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationQuoteCount == 1 ? buySimulationOrderId : sellSimulationOrderId,
                        simulationQuoteCount == 1 ? Side.Buy : Side.Sell,
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            });

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray();
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal(2, replayFills.Length);
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());

        var replayScheduled = Assert.Single(replayEvents.OfType<AssetDeliveryScheduled>());
        var replayCanceled = Assert.Single(replayEvents.OfType<AssetDeliveryCanceled>());
        Assert.Equal(replayScheduled.DeliveryId, replayCanceled.DeliveryId);
        Assert.Empty(replayEvents.OfType<AssetDelivered>());

        var simulationScheduled = Assert.Single(simulationEvents.OfType<AssetDeliveryScheduled>());
        var simulationCanceled = Assert.Single(simulationEvents.OfType<AssetDeliveryCanceled>());
        Assert.Equal(simulationScheduled.DeliveryId, simulationCanceled.DeliveryId);
        Assert.Empty(simulationEvents.OfType<AssetDelivered>());

        Assert.Equal(NormalizeAssetDeliveryScheduled(replayScheduled), NormalizeAssetDeliveryScheduled(simulationScheduled));
        Assert.Equal(NormalizeAssetDeliveryCanceled(replayCanceled), NormalizeAssetDeliveryCanceled(simulationCanceled));
        Assert.Equal(
            replayEvents.OfType<AssetDeliveryStatusSnapshot>().Select(NormalizeAssetDeliveryStatus).ToArray(),
            simulationEvents.OfType<AssetDeliveryStatusSnapshot>().Select(NormalizeAssetDeliveryStatus).ToArray());
    }

    [Fact]
    public async Task CashSaleProceedsSettleBeforeLaterBuy_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1)
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var firstBuyOrderId = new OrderId(176);
        var sellOrderId = new OrderId(177);
        var rejectedBuyOrderId = new OrderId(178);
        var settledBuyOrderId = new OrderId(179);
        var history = new[]
        {
            CreateQuote(t0, bid: 100m, ask: 100.05m),
            CreateQuote(t0 + Duration.FromMinutes(1), bid: 100.01m, ask: 100.06m),
            CreateQuote(t0 + Duration.FromDays(1) + Duration.FromMinutes(1), bid: 100.02m, ask: 100.07m)
        };

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                if (replayQuoteCount == 1)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                        {
                            OrderId = firstBuyOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                    return;
                }

                if (replayQuoteCount == 2)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Sell, new Qty(1m)) with
                        {
                            OrderId = sellOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                        {
                            OrderId = rejectedBuyOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                    return;
                }

                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = settledBuyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: Money.USD(200m));

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                simulationQuoteCount++;
                if (simulationQuoteCount == 1)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            firstBuyOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Market()),
                        quote.Time);
                    return;
                }

                if (simulationQuoteCount == 2)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            sellOrderId,
                            Side.Sell,
                            new Qty(1m),
                            Execution.Market()),
                        quote.Time);
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            rejectedBuyOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Market()),
                        quote.Time);
                    return;
                }

                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        settledBuyOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            },
            initialCash: Money.USD(200m));

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray());

        var replayRejected = Assert.Single(replayEvents.OfType<OrderRejected>());
        var simulationRejected = Assert.Single(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(NormalizeRejected(replayRejected), NormalizeRejected(simulationRejected));
        Assert.Equal(rejectedBuyOrderId, replayRejected.OrderId);

        var replayScheduled = Assert.Single(replayEvents.OfType<SettlementScheduled>());
        var simulationScheduled = Assert.Single(simulationEvents.OfType<SettlementScheduled>());
        Assert.Equal(NormalizeSettlementScheduled(replayScheduled), NormalizeSettlementScheduled(simulationScheduled));
        Assert.Equal(t0 + Duration.FromDays(1) + Duration.FromMinutes(1), replayScheduled.SettlesAt);

        var replayReleased = Assert.Single(replayEvents.OfType<SettlementReleased>());
        var simulationReleased = Assert.Single(simulationEvents.OfType<SettlementReleased>());
        Assert.Equal(NormalizeSettlementReleased(replayReleased), NormalizeSettlementReleased(simulationReleased));
        Assert.Equal(replayScheduled.SettlementId, replayReleased.SettlementId);
        Assert.Equal(simulationScheduled.SettlementId, simulationReleased.SettlementId);

        Assert.Equal(
            replayEvents.OfType<SettlementStatusSnapshot>().Select(NormalizeSettlementStatus).ToArray(),
            simulationEvents.OfType<SettlementStatusSnapshot>().Select(NormalizeSettlementStatus).ToArray());
    }

    [Fact]
    public async Task SettlementAcrossWeekend_HasReplayConnectorParity()
    {
        var t0 = Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 5, 14, 30, 0, TimeSpan.Zero));
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1)
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var firstBuyOrderId = new OrderId(10_180);
        var sellOrderId = new OrderId(10_181);
        var weekendBuyOrderId = new OrderId(10_182);
        var settledBuyOrderId = new OrderId(10_183);

        var (replayEvents, simulationEvents) = await RunCashSettlementScenarioAsync(
            [
                t0,
                t0 + Duration.FromMinutes(1),
                Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 7, 14, 32, 0, TimeSpan.Zero)),
                Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 8, 14, 32, 0, TimeSpan.Zero))
            ],
            config,
            Money.USD(200m),
            [firstBuyOrderId, sellOrderId, weekendBuyOrderId, settledBuyOrderId],
            [Side.Buy, Side.Sell, Side.Buy, Side.Buy]);

        var replayRejected = Assert.Single(replayEvents.OfType<OrderRejected>());
        var simulationRejected = Assert.Single(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(NormalizeRejected(replayRejected), NormalizeRejected(simulationRejected));
        Assert.Equal(weekendBuyOrderId, replayRejected.OrderId);
        Assert.Contains("Insufficient cash buying power", replayRejected.Reason);
        Assert.Contains("Insufficient cash buying power", simulationRejected.Reason);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray());
        Assert.Equal(
            [firstBuyOrderId, sellOrderId, settledBuyOrderId],
            replayEvents.OfType<OrderFilled>().Select(static fill => fill.OrderId).ToArray());

        Assert.Equal(
            replayEvents.OfType<SettlementScheduled>().Select(NormalizeSettlementScheduled).ToArray(),
            simulationEvents.OfType<SettlementScheduled>().Select(NormalizeSettlementScheduled).ToArray());
        Assert.Equal(
            replayEvents.OfType<SettlementReleased>().Select(NormalizeSettlementReleased).ToArray(),
            simulationEvents.OfType<SettlementReleased>().Select(NormalizeSettlementReleased).ToArray());
        Assert.Equal(
            replayEvents.OfType<SettlementStatusSnapshot>().Select(NormalizeSettlementStatus).ToArray(),
            simulationEvents.OfType<SettlementStatusSnapshot>().Select(NormalizeSettlementStatus).ToArray());

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().Last();
        var simulationStatement = simulationEvents.OfType<AccountStatementSnapshot>().Last();
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
        Assert.Equal(Money.USD(99.88m), replayStatement.Cash);
        Assert.Equal(Money.USD(0m), replayStatement.PendingSettlement);
        Assert.Equal(1, replayStatement.OpenPositions);
    }

    [Fact]
    public async Task ConfiguredClearingCalendarSettlement_HasReplayConnectorParity()
    {
        var t0 = Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 5, 14, 30, 0, TimeSpan.Zero));
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1, ClearingCalendar.Crypto())
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var firstBuyOrderId = new OrderId(10_184);
        var sellOrderId = new OrderId(10_185);
        var weekendBuyOrderId = new OrderId(10_186);

        var (replayEvents, simulationEvents) = await RunCashSettlementScenarioAsync(
            [
                t0,
                t0 + Duration.FromMinutes(1),
                Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 6, 14, 32, 0, TimeSpan.Zero))
            ],
            config,
            Money.USD(200m),
            [firstBuyOrderId, sellOrderId, weekendBuyOrderId],
            [Side.Buy, Side.Sell, Side.Buy]);

        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray());
        Assert.Equal(
            [firstBuyOrderId, sellOrderId, weekendBuyOrderId],
            replayEvents.OfType<OrderFilled>().Select(static fill => fill.OrderId).ToArray());

        Assert.Equal(
            replayEvents.OfType<SettlementScheduled>().Select(NormalizeSettlementScheduled).ToArray(),
            simulationEvents.OfType<SettlementScheduled>().Select(NormalizeSettlementScheduled).ToArray());
        Assert.Equal(
            replayEvents.OfType<SettlementReleased>().Select(NormalizeSettlementReleased).ToArray(),
            simulationEvents.OfType<SettlementReleased>().Select(NormalizeSettlementReleased).ToArray());
        Assert.Equal(
            replayEvents.OfType<SettlementStatusSnapshot>().Select(NormalizeSettlementStatus).ToArray(),
            simulationEvents.OfType<SettlementStatusSnapshot>().Select(NormalizeSettlementStatus).ToArray());

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().Last();
        var simulationStatement = simulationEvents.OfType<AccountStatementSnapshot>().Last();
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
        Assert.Equal(Money.USD(99.89m), replayStatement.Cash);
        Assert.Equal(Money.USD(0m), replayStatement.PendingSettlement);
        Assert.Equal(1, replayStatement.OpenPositions);
    }

    [Fact]
    public async Task ConfiguredHolidaySettlementDelay_HasReplayConnectorParity()
    {
        var t0 = Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 5, 14, 30, 0, TimeSpan.Zero));
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1, [new DateOnly(2024, 1, 8)])
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var firstBuyOrderId = new OrderId(10_187);
        var sellOrderId = new OrderId(10_188);
        var holidayBuyOrderId = new OrderId(10_189);
        var settledBuyOrderId = new OrderId(10_190);

        var (replayEvents, simulationEvents) = await RunCashSettlementScenarioAsync(
            [
                t0,
                t0 + Duration.FromMinutes(1),
                Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 8, 14, 32, 0, TimeSpan.Zero)),
                Instant.FromDateTimeOffset(new DateTimeOffset(2024, 1, 9, 14, 32, 0, TimeSpan.Zero))
            ],
            config,
            Money.USD(200m),
            [firstBuyOrderId, sellOrderId, holidayBuyOrderId, settledBuyOrderId],
            [Side.Buy, Side.Sell, Side.Buy, Side.Buy]);

        var replayRejected = Assert.Single(replayEvents.OfType<OrderRejected>());
        var simulationRejected = Assert.Single(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(NormalizeRejected(replayRejected), NormalizeRejected(simulationRejected));
        Assert.Equal(holidayBuyOrderId, replayRejected.OrderId);
        Assert.Contains("Insufficient cash buying power", replayRejected.Reason);
        Assert.Contains("Insufficient cash buying power", simulationRejected.Reason);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray());
        Assert.Equal(
            [firstBuyOrderId, sellOrderId, settledBuyOrderId],
            replayEvents.OfType<OrderFilled>().Select(static fill => fill.OrderId).ToArray());

        Assert.Equal(
            replayEvents.OfType<SettlementScheduled>().Select(NormalizeSettlementScheduled).ToArray(),
            simulationEvents.OfType<SettlementScheduled>().Select(NormalizeSettlementScheduled).ToArray());
        Assert.Equal(
            replayEvents.OfType<SettlementReleased>().Select(NormalizeSettlementReleased).ToArray(),
            simulationEvents.OfType<SettlementReleased>().Select(NormalizeSettlementReleased).ToArray());
        Assert.Equal(
            replayEvents.OfType<SettlementStatusSnapshot>().Select(NormalizeSettlementStatus).ToArray(),
            simulationEvents.OfType<SettlementStatusSnapshot>().Select(NormalizeSettlementStatus).ToArray());

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().Last();
        var simulationStatement = simulationEvents.OfType<AccountStatementSnapshot>().Last();
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
        Assert.Equal(Money.USD(99.88m), replayStatement.Cash);
        Assert.Equal(Money.USD(0m), replayStatement.PendingSettlement);
        Assert.Equal(1, replayStatement.OpenPositions);
    }

    [Fact]
    public async Task PendingSettlementAccountStatementAtReplayEnd_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.TPlus(1)
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var buyOrderId = new OrderId(10_191);
        var sellOrderId = new OrderId(10_192);

        var (replayEvents, simulationEvents) = await RunCashSettlementScenarioAsync(
            [t0, t0 + Duration.FromMinutes(1)],
            config,
            Money.USD(200m),
            [buyOrderId, sellOrderId],
            [Side.Buy, Side.Sell]);

        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray());

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().Last();
        var simulationStatement = simulationEvents.OfType<AccountStatementSnapshot>().Last();
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
        Assert.Equal(Money.USD(99.95m), replayStatement.Cash);
        Assert.Equal(Money.USD(99.95m), replayStatement.AvailableCash);
        Assert.Equal(Money.USD(100.01m), replayStatement.PendingSettlement);
        Assert.Equal(Money.USD(0m), replayStatement.ReservedCash);
        Assert.Equal(Money.USD(0m), replayStatement.MarketValue);
        Assert.Equal(Money.USD(199.96m), replayStatement.Equity);
        Assert.Equal(Money.USD(-0.04m), replayStatement.RealizedPnL);
        Assert.Equal(0, replayStatement.OpenPositions);
        Assert.Equal(0, replayStatement.OpenOrders);

        Assert.Empty(replayEvents.OfType<SettlementReleased>());
        Assert.Empty(simulationEvents.OfType<SettlementReleased>());
        Assert.Equal(
            replayEvents.OfType<SettlementStatusSnapshot>().Select(NormalizeSettlementStatus).ToArray(),
            simulationEvents.OfType<SettlementStatusSnapshot>().Select(NormalizeSettlementStatus).ToArray());
        Assert.Equal(
            [SettlementStatus.Scheduled, SettlementStatus.Pending],
            replayEvents.OfType<SettlementStatusSnapshot>().Select(static snapshot => snapshot.Status).ToArray());
    }

    [Fact]
    public async Task CashFillEmitsAccountAndPerformanceSnapshots_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        using var session = new SimulationSession();
        SnapshotBuyOnceStrategy.Reset();
        var strategyId = session.RegisterStrategy<SnapshotBuyOnceStrategy>();

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(strategyId, Instrument, Side.Buy, new Qty(1m)),
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(1_000m));

        var result = session.Run(
            SharedHistory.Load([CreateQuote(t0, bid: 100m, ask: 100.05m)]),
            new SimulationRunOptions
            {
                Config = config,
                InitialCash = Money.USD(1_000m)
            });

        var replayPerformance = Assert.Single(replayEvents.OfType<PerformanceSnapshot>());
        var simulationPerformance = Assert.Single(result.SimulatorEvents.OfType<PerformanceSnapshot>());
        Assert.Equal(NormalizePerformance(replayPerformance), NormalizePerformance(simulationPerformance));

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().First(statement => statement.StrategyId == strategyId);
        var simulationStatement = Assert.Single(result.AccountStatements);
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
    }

    [Fact]
    public async Task ClosingCashPositionRealizesPnLInPerformanceSnapshot_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        using var session = new SimulationSession();
        SnapshotRoundTripStrategy.Reset();
        var strategyId = session.RegisterStrategy<SnapshotRoundTripStrategy>();
        var history = new[]
        {
            CreateQuote(t0, bid: 100m, ask: 100.05m),
            CreateQuote(t0 + Duration.FromMinutes(1), bid: 100.01m, ask: 100.06m)
        };

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        strategyId,
                        Instrument,
                        replayQuoteCount == 1 ? Side.Buy : Side.Sell,
                        new Qty(1m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: Money.USD(1_000m));

        var result = session.Run(
            SharedHistory.Load(history),
            new SimulationRunOptions
            {
                Config = config,
                InitialCash = Money.USD(1_000m)
            });

        var replayPerformance = replayEvents.OfType<PerformanceSnapshot>().Last();
        var simulationPerformance = result.SimulatorEvents.OfType<PerformanceSnapshot>().Last();
        Assert.Equal(NormalizePerformance(replayPerformance), NormalizePerformance(simulationPerformance));

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().Last(statement => statement.StrategyId == strategyId);
        var simulationStatement = result.AccountStatements.Last();
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
    }

    [Fact]
    public async Task StockSplitCorporateAction_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var actionTime = t0 + Duration.FromSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(176);
        var simulationOrderId = new OrderId(176);
        var corporateActionId = new CorporateActionId(501);
        var replayAction = new CorporateActionCommand(
            corporateActionId,
            CorporateActionType.StockSplit,
            Instrument,
            actionTime,
            SplitRatio: 2m,
            ExternalReference: "split-2-for-1");
        var simulationAction = new CorporateActionApplied(
            corporateActionId,
            CorporateActionType.StockSplit,
            Instrument,
            actionTime,
            SplitRatio: 2m,
            ExternalReference: "split-2-for-1")
        {
            Time = actionTime
        };

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(actionTime, bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(3m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                else if (evt is QuoteReceived quoteAtAction && quoteAtAction.Time == actionTime && !replayApplied)
                {
                    replayApplied = true;
                    connector.ApplyCorporateActionAsync(replayAction, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(1_000m));

        var simulationApplied = false;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(actionTime, bid: 101m, ask: 101.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(3m),
                            Execution.Market()),
                        quote.Time);
                }
                else if (evt is QuoteReceived quoteAtAction && quoteAtAction.Time == actionTime && !simulationApplied)
                {
                    simulationApplied = true;
                    venue.OnMarketEvent(simulationAction);
                }
            },
            initialCash: Money.USD(1_000m));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());

        var replayEffect = Assert.Single(replayEvents.OfType<CorporateActionEffectSnapshot>());
        var simulationEffect = Assert.Single(simulationEvents.OfType<CorporateActionEffectSnapshot>());
        Assert.Equal(NormalizeCorporateActionEffect(replayEffect), NormalizeCorporateActionEffect(simulationEffect));
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task CashDividendCorporateAction_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var actionTime = t0 + Duration.FromSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(177);
        var simulationOrderId = new OrderId(177);
        var corporateActionId = new CorporateActionId(502);
        var replayAction = new CorporateActionCommand(
            corporateActionId,
            CorporateActionType.CashDividend,
            Instrument,
            actionTime,
            DividendPerShare: Money.USD(2m),
            ExternalReference: "dividend-2");
        var simulationAction = new CorporateActionApplied(
            corporateActionId,
            CorporateActionType.CashDividend,
            Instrument,
            actionTime,
            DividendPerShare: Money.USD(2m),
            ExternalReference: "dividend-2")
        {
            Time = actionTime
        };

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(actionTime, bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(3m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                else if (evt is QuoteReceived quoteAtAction && quoteAtAction.Time == actionTime && !replayApplied)
                {
                    replayApplied = true;
                    connector.ApplyCorporateActionAsync(replayAction, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(1_000m));

        var simulationApplied = false;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(actionTime, bid: 101m, ask: 101.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(3m),
                            Execution.Market()),
                        quote.Time);
                }
                else if (evt is QuoteReceived quoteAtAction && quoteAtAction.Time == actionTime && !simulationApplied)
                {
                    simulationApplied = true;
                    venue.OnMarketEvent(simulationAction);
                }
            },
            initialCash: Money.USD(1_000m));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());

        var replayEffect = Assert.Single(replayEvents.OfType<CorporateActionEffectSnapshot>());
        var simulationEffect = Assert.Single(simulationEvents.OfType<CorporateActionEffectSnapshot>());
        Assert.Equal(NormalizeCorporateActionEffect(replayEffect), NormalizeCorporateActionEffect(simulationEffect));
        Assert.Equal(Money.USD(6m), replayEffect.CashAmount);
        Assert.Equal(Money.USD(6m), simulationEffect.CashAmount);
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task StockSplitAfterAssetDepositUpdatesSettledCustody_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var actionTime = t0 + Duration.FromSeconds(1);
        var config = SimulationConfig.Instant();
        var initialCash = Money.USD(1_000_000m);
        var deposit = new AccountTransferCommand(
            new AccountTransferId(721),
            StrategyId,
            VariantId: 0,
            AccountTransferType.AssetDeposit,
            Instrument: Instrument,
            Quantity: new Qty(3m),
            CarryingPrice: new Price(90m, Currency.USD),
            ExternalReference: "custody-in");
        var replayAction = new CorporateActionCommand(
            new CorporateActionId(503),
            CorporateActionType.StockSplit,
            Instrument,
            actionTime,
            SplitRatio: 2m,
            ExternalReference: "split-2-for-1");
        var simulationAction = new CorporateActionApplied(
            replayAction.CorporateActionId,
            replayAction.ActionType,
            replayAction.Instrument,
            replayAction.EffectiveAt,
            SplitRatio: replayAction.SplitRatio,
            ExternalReference: replayAction.ExternalReference)
        {
            Time = actionTime
        };

        var replayDepositApplied = false;
        var replayActionApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(actionTime, bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0 && !replayDepositApplied)
                {
                    replayDepositApplied = true;
                    connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
                }
                else if (evt is QuoteReceived quoteAtAction && quoteAtAction.Time == actionTime && !replayActionApplied)
                {
                    replayActionApplied = true;
                    connector.ApplyCorporateActionAsync(replayAction, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: initialCash);

        var simulationEvents = RunSimulatedCorporateActionAfterAssetDeposit(
            deposit,
            simulationAction,
            config,
            initialCash,
            t0,
            actionTime);

        var replayEffect = Assert.Single(replayEvents.OfType<CorporateActionEffectSnapshot>());
        var simulationEffect = Assert.Single(simulationEvents.OfType<CorporateActionEffectSnapshot>());
        Assert.Equal(NormalizeCorporateActionEffect(replayEffect), NormalizeCorporateActionEffect(simulationEffect));

        var replayCustody = replayEvents
            .OfType<CustodyPositionSnapshot>()
            .Last(snapshot => snapshot.StrategyId == StrategyId);
        var simulationCustody = simulationEvents
            .OfType<CustodyPositionSnapshot>()
            .Last(snapshot => snapshot.StrategyId == StrategyId);
        Assert.Equal(NormalizeCustody(replayCustody), NormalizeCustody(simulationCustody));

        var replayStatement = replayEvents
            .OfType<AccountStatementSnapshot>()
            .Last(snapshot => snapshot.StrategyId == StrategyId);
        var simulationStatement = simulationEvents
            .OfType<AccountStatementSnapshot>()
            .Last(snapshot => snapshot.StrategyId == StrategyId);
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
    }

    [Fact]
    public async Task CashDividendCreditsStatementCashAndAvailableCash_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var actionTime = t0 + Duration.FromSeconds(1);
        var config = SimulationConfig.Instant();
        var initialCash = Money.USD(100m);
        var deposit = new AccountTransferCommand(
            new AccountTransferId(722),
            StrategyId,
            VariantId: 0,
            AccountTransferType.AssetDeposit,
            Instrument: Instrument,
            Quantity: new Qty(3m),
            CarryingPrice: new Price(90m, Currency.USD),
            ExternalReference: "custody-in");
        var replayAction = new CorporateActionCommand(
            new CorporateActionId(504),
            CorporateActionType.CashDividend,
            Instrument,
            actionTime,
            DividendPerShare: Money.USD(2m),
            ExternalReference: "dividend-2");
        var simulationAction = new CorporateActionApplied(
            replayAction.CorporateActionId,
            replayAction.ActionType,
            replayAction.Instrument,
            replayAction.EffectiveAt,
            DividendPerShare: replayAction.DividendPerShare,
            ExternalReference: replayAction.ExternalReference)
        {
            Time = actionTime
        };

        var replayDepositApplied = false;
        var replayActionApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(actionTime, bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0 && !replayDepositApplied)
                {
                    replayDepositApplied = true;
                    connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
                }
                else if (evt is QuoteReceived quoteAtAction && quoteAtAction.Time == actionTime && !replayActionApplied)
                {
                    replayActionApplied = true;
                    connector.ApplyCorporateActionAsync(replayAction, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: initialCash);

        var simulationEvents = RunSimulatedCorporateActionAfterAssetDeposit(
            deposit,
            simulationAction,
            config,
            initialCash,
            t0,
            actionTime);

        var replayEffect = Assert.Single(replayEvents.OfType<CorporateActionEffectSnapshot>());
        var simulationEffect = Assert.Single(simulationEvents.OfType<CorporateActionEffectSnapshot>());
        Assert.Equal(NormalizeCorporateActionEffect(replayEffect), NormalizeCorporateActionEffect(simulationEffect));
        Assert.Equal(Money.USD(6m), replayEffect.CashAmount);
        Assert.Equal(Money.USD(6m), simulationEffect.CashAmount);

        var replayStatement = replayEvents
            .OfType<AccountStatementSnapshot>()
            .Last(snapshot => snapshot.StrategyId == StrategyId);
        var simulationStatement = simulationEvents
            .OfType<AccountStatementSnapshot>()
            .Last(snapshot => snapshot.StrategyId == StrategyId);
        Assert.Equal(Money.USD(106m), replayStatement.Cash);
        Assert.Equal(Money.USD(106m), replayStatement.AvailableCash);
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
    }

    [Fact]
    public async Task CashInterestCreditFinancing_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var effectiveAt = t0 + Duration.FromSeconds(1);
        var config = SimulationConfig.Instant();
        var financing = new FinancingChargeCommand(
            new FinancingChargeId(601),
            FinancingChargeType.CashInterestCredit,
            StrategyId,
            VariantId: 0,
            Money.USD(1.25m),
            effectiveAt,
            Rate: 0.05m,
            ExternalReference: "broker-interest");

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(effectiveAt, bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == effectiveAt && !replayApplied)
                {
                    replayApplied = true;
                    connector.ApplyFinancingChargeAsync(financing, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(100m));

        var simulationEvents = RunSimulatedFinancing(financing, config, Money.USD(100m));

        var replayAppliedEvent = Assert.Single(replayEvents.OfType<FinancingChargeApplied>());
        var simulationAppliedEvent = Assert.Single(simulationEvents.OfType<FinancingChargeApplied>());
        Assert.Equal(NormalizeFinancing(replayAppliedEvent), NormalizeFinancing(simulationAppliedEvent));

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().Last(statement => statement.StrategyId == StrategyId);
        var simulationStatement = Assert.Single(simulationEvents.OfType<AccountStatementSnapshot>());
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
    }

    [Fact]
    public async Task CashInterestDebitFinancing_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var effectiveAt = t0 + Duration.FromSeconds(1);
        var config = SimulationConfig.Instant();
        var financing = new FinancingChargeCommand(
            new FinancingChargeId(602),
            FinancingChargeType.CashInterestDebit,
            StrategyId,
            VariantId: 0,
            Money.USD(-0.75m),
            effectiveAt,
            Rate: 0.03m,
            ExternalReference: "broker-interest-debit");

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(effectiveAt, bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == effectiveAt && !replayApplied)
                {
                    replayApplied = true;
                    connector.ApplyFinancingChargeAsync(financing, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(100m));

        var simulationEvents = RunSimulatedFinancing(financing, config, Money.USD(100m));

        var replayAppliedEvent = Assert.Single(replayEvents.OfType<FinancingChargeApplied>());
        var simulationAppliedEvent = Assert.Single(simulationEvents.OfType<FinancingChargeApplied>());
        Assert.Equal(NormalizeFinancing(replayAppliedEvent), NormalizeFinancing(simulationAppliedEvent));

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().Last(statement => statement.StrategyId == StrategyId);
        var simulationStatement = Assert.Single(simulationEvents.OfType<AccountStatementSnapshot>());
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
    }

    [Fact]
    public async Task BorrowFeeFinancing_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var effectiveAt = t0 + Duration.FromSeconds(1);
        var config = SimulationConfig.Instant();
        var financing = new FinancingChargeCommand(
            new FinancingChargeId(603),
            FinancingChargeType.BorrowFee,
            StrategyId,
            VariantId: 0,
            Money.USD(-3.50m),
            effectiveAt,
            Instrument,
            new Qty(10m),
            Rate: 0.02m,
            ExternalReference: "short-borrow");

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(effectiveAt, bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == effectiveAt && !replayApplied)
                {
                    replayApplied = true;
                    connector.ApplyFinancingChargeAsync(financing, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(100m));

        var simulationEvents = RunSimulatedFinancing(financing, config, Money.USD(100m));

        var replayAppliedEvent = Assert.Single(replayEvents.OfType<FinancingChargeApplied>());
        var simulationAppliedEvent = Assert.Single(simulationEvents.OfType<FinancingChargeApplied>());
        Assert.Equal(NormalizeFinancing(replayAppliedEvent), NormalizeFinancing(simulationAppliedEvent));

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().Last(statement => statement.StrategyId == StrategyId);
        var simulationStatement = Assert.Single(simulationEvents.OfType<AccountStatementSnapshot>());
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
    }

    [Fact]
    public async Task PerpetualFundingFinancing_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var effectiveAt = t0 + Duration.FromSeconds(1);
        var config = SimulationConfig.Instant();
        var paid = new FinancingChargeCommand(
            new FinancingChargeId(604),
            FinancingChargeType.PerpetualFunding,
            StrategyId,
            VariantId: 0,
            Money.USD(-2m),
            effectiveAt,
            PerpetualInstrument,
            new Qty(5m),
            Rate: 0.0001m,
            ExternalReference: "perp-funding-paid");
        var received = new FinancingChargeCommand(
            new FinancingChargeId(605),
            FinancingChargeType.PerpetualFunding,
            StrategyId,
            VariantId: 0,
            Money.USD(4m),
            effectiveAt,
            PerpetualInstrument,
            new Qty(5m),
            Rate: -0.0002m,
            ExternalReference: "perp-funding-received");

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(effectiveAt, bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == effectiveAt && !replayApplied)
                {
                    replayApplied = true;
                    connector.ApplyFinancingChargeAsync(paid, CancellationToken.None).GetAwaiter().GetResult();
                    connector.ApplyFinancingChargeAsync(received, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(100m));

        var simulationEvents = RunSimulatedFinancings([paid, received], config, Money.USD(100m));

        Assert.Equal(
            replayEvents.OfType<FinancingChargeApplied>().Select(NormalizeFinancing).ToArray(),
            simulationEvents.OfType<FinancingChargeApplied>().Select(NormalizeFinancing).ToArray());

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().Last(statement => statement.StrategyId == StrategyId);
        var simulationStatement = simulationEvents.OfType<AccountStatementSnapshot>().Last();
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
    }

    [Fact]
    public async Task ForexRolloverFinancing_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var effectiveAt = t0 + Duration.FromSeconds(1);
        var config = SimulationConfig.Instant();
        var financing = new FinancingChargeCommand(
            new FinancingChargeId(606),
            FinancingChargeType.ForexRollover,
            StrategyId,
            VariantId: 0,
            Money.USD(-1.10m),
            effectiveAt,
            ForexInstrument,
            new Qty(100_000m),
            Rate: -0.000011m,
            ExternalReference: "fx-rollover");

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(effectiveAt, bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == effectiveAt && !replayApplied)
                {
                    replayApplied = true;
                    connector.ApplyFinancingChargeAsync(financing, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(100m));

        var simulationEvents = RunSimulatedFinancing(financing, config, Money.USD(100m));

        var replayAppliedEvent = Assert.Single(replayEvents.OfType<FinancingChargeApplied>());
        var simulationAppliedEvent = Assert.Single(simulationEvents.OfType<FinancingChargeApplied>());
        Assert.Equal(NormalizeFinancing(replayAppliedEvent), NormalizeFinancing(simulationAppliedEvent));

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().Last(statement => statement.StrategyId == StrategyId);
        var simulationStatement = Assert.Single(simulationEvents.OfType<AccountStatementSnapshot>());
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
    }

    [Fact]
    public async Task CashDepositTransfer_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var transfer = new AccountTransferCommand(
            new AccountTransferId(701),
            StrategyId,
            VariantId: 0,
            AccountTransferType.CashDeposit,
            CashAmount: Money.USD(25m),
            ExternalReference: "ach-1");

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived && !replayApplied)
                {
                    replayApplied = true;
                    connector.CompleteAccountTransferAsync(transfer, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(100m));

        var simulationEvents = RunSimulatedTransfer(transfer, config, Money.USD(100m));

        var replayCompleted = Assert.Single(replayEvents.OfType<AccountTransferCompleted>());
        var simulationCompleted = Assert.Single(simulationEvents.OfType<AccountTransferCompleted>());
        Assert.Equal(NormalizeTransferCompleted(replayCompleted), NormalizeTransferCompleted(simulationCompleted));

        var replayStatus = Assert.Single(replayEvents.OfType<AccountTransferStatusSnapshot>());
        var simulationStatus = Assert.Single(simulationEvents.OfType<AccountTransferStatusSnapshot>());
        Assert.Equal(NormalizeTransferStatus(replayStatus), NormalizeTransferStatus(simulationStatus));

        var replayStatement = replayEvents.OfType<AccountStatementSnapshot>().Last(statement => statement.StrategyId == StrategyId);
        var simulationStatement = Assert.Single(simulationEvents.OfType<AccountStatementSnapshot>());
        Assert.Equal(NormalizeAccountStatement(replayStatement), NormalizeAccountStatement(simulationStatement));
    }

    [Fact]
    public async Task CashWithdrawalFailureTransfer_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var transfer = new AccountTransferCommand(
            new AccountTransferId(702),
            StrategyId,
            VariantId: 0,
            AccountTransferType.CashWithdrawal,
            CashAmount: Money.USD(25m),
            ExternalReference: "ach-2");

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived && !replayApplied)
                {
                    replayApplied = true;
                    connector.CompleteAccountTransferAsync(transfer, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(10m));

        var simulationEvents = RunSimulatedTransfer(transfer, config, Money.USD(10m));

        var replayFailed = Assert.Single(replayEvents.OfType<AccountTransferFailed>());
        var simulationFailed = Assert.Single(simulationEvents.OfType<AccountTransferFailed>());
        Assert.Equal(NormalizeTransferFailure(replayFailed), NormalizeTransferFailure(simulationFailed));

        var replayStatus = Assert.Single(replayEvents.OfType<AccountTransferStatusSnapshot>());
        var simulationStatus = Assert.Single(simulationEvents.OfType<AccountTransferStatusSnapshot>());
        Assert.Equal(NormalizeTransferStatus(replayStatus), NormalizeTransferStatus(simulationStatus));
        Assert.Empty(simulationEvents.OfType<AccountStatementSnapshot>());
    }

    [Fact]
    public async Task AccountTransferRequestCancelAndFailLifecycle_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var requested = new AccountTransferCommand(
            new AccountTransferId(711),
            StrategyId,
            VariantId: 0,
            AccountTransferType.CashDeposit,
            CashAmount: Money.USD(10m),
            ExternalReference: "request-only");
        var canceled = new AccountTransferCommand(
            new AccountTransferId(712),
            StrategyId,
            VariantId: 0,
            AccountTransferType.CashDeposit,
            CashAmount: Money.USD(15m),
            ExternalReference: "cancelled");
        var failed = new AccountTransferCommand(
            new AccountTransferId(713),
            StrategyId,
            VariantId: 0,
            AccountTransferType.CashWithdrawal,
            CashAmount: Money.USD(20m),
            ExternalReference: "failed");

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived && !replayApplied)
                {
                    replayApplied = true;
                    connector.RequestAccountTransferAsync(requested, CancellationToken.None).GetAwaiter().GetResult();
                    connector.CancelAccountTransferAsync(canceled, "customer canceled", CancellationToken.None).GetAwaiter().GetResult();
                    connector.FailAccountTransferAsync(failed, "bank rejected", CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(100m));

        var simulationEvents = RunSimulatedTransferLifecycle(
            [
                CreateTransferRequested(requested, t0),
                CreateTransferStatus(requested, AccountTransferStatus.Requested, t0),
                CreateTransferCanceled(canceled, t0, "customer canceled"),
                CreateTransferStatus(canceled, AccountTransferStatus.Canceled, t0, "customer canceled"),
                CreateTransferFailed(failed, t0, "bank rejected"),
                CreateTransferStatus(failed, AccountTransferStatus.Failed, t0, "bank rejected")
            ],
            config,
            Money.USD(100m));

        Assert.Equal(
            replayEvents.OfType<AccountTransferRequested>().Select(NormalizeTransferRequested).ToArray(),
            simulationEvents.OfType<AccountTransferRequested>().Select(NormalizeTransferRequested).ToArray());
        Assert.Equal(
            replayEvents.OfType<AccountTransferCanceled>().Select(NormalizeTransferCanceled).ToArray(),
            simulationEvents.OfType<AccountTransferCanceled>().Select(NormalizeTransferCanceled).ToArray());
        Assert.Equal(
            replayEvents.OfType<AccountTransferFailed>().Select(NormalizeTransferFailure).ToArray(),
            simulationEvents.OfType<AccountTransferFailed>().Select(NormalizeTransferFailure).ToArray());
        Assert.Equal(
            replayEvents.OfType<AccountTransferStatusSnapshot>().Select(NormalizeTransferStatus).ToArray(),
            simulationEvents.OfType<AccountTransferStatusSnapshot>().Select(NormalizeTransferStatus).ToArray());
        Assert.Empty(simulationEvents.OfType<AccountStatementSnapshot>());
    }

    [Fact]
    public async Task InternalCashTransfer_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var destinationStrategyId = new StrategyId(8);
        var transfer = new AccountTransferCommand(
            new AccountTransferId(703),
            StrategyId,
            VariantId: 0,
            AccountTransferType.InternalTransfer,
            CashAmount: Money.USD(25m),
            ExternalReference: "internal-cash",
            DestinationStrategyId: destinationStrategyId,
            DestinationVariantId: 0);

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived && !replayApplied)
                {
                    replayApplied = true;
                    connector.CompleteAccountTransferAsync(transfer, CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(100m));

        var simulationEvents = RunSimulatedTransfer(transfer, config, Money.USD(100m));

        var replayCompleted = Assert.Single(replayEvents.OfType<AccountTransferCompleted>());
        var simulationCompleted = Assert.Single(simulationEvents.OfType<AccountTransferCompleted>());
        Assert.Equal(NormalizeTransferCompleted(replayCompleted), NormalizeTransferCompleted(simulationCompleted));

        var replayStatus = Assert.Single(replayEvents.OfType<AccountTransferStatusSnapshot>());
        var simulationStatus = Assert.Single(simulationEvents.OfType<AccountTransferStatusSnapshot>());
        Assert.Equal(NormalizeTransferStatus(replayStatus), NormalizeTransferStatus(simulationStatus));

        var replaySourceStatement = replayEvents
            .OfType<AccountStatementSnapshot>()
            .Last(statement => statement.StrategyId == StrategyId);
        var simulationSourceStatement = simulationEvents
            .OfType<AccountStatementSnapshot>()
            .Last(statement => statement.StrategyId == StrategyId);
        Assert.Equal(NormalizeAccountStatement(replaySourceStatement), NormalizeAccountStatement(simulationSourceStatement));

        var replayDestinationStatement = replayEvents
            .OfType<AccountStatementSnapshot>()
            .Last(statement => statement.StrategyId == destinationStrategyId);
        var simulationDestinationStatement = simulationEvents
            .OfType<AccountStatementSnapshot>()
            .Last(statement => statement.StrategyId == destinationStrategyId);
        Assert.Equal(NormalizeAccountStatement(replayDestinationStatement), NormalizeAccountStatement(simulationDestinationStatement));
    }

    [Fact]
    public async Task AssetDepositAndWithdrawalTransfer_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var deposit = new AccountTransferCommand(
            new AccountTransferId(704),
            StrategyId,
            VariantId: 0,
            AccountTransferType.AssetDeposit,
            Instrument: Instrument,
            Quantity: new Qty(3m),
            CarryingPrice: new Price(100m, Currency.USD),
            ExternalReference: "acat-in");
        var withdrawal = new AccountTransferCommand(
            new AccountTransferId(705),
            StrategyId,
            VariantId: 0,
            AccountTransferType.AssetWithdrawal,
            Instrument: Instrument,
            Quantity: new Qty(2m),
            CarryingPrice: new Price(100m, Currency.USD),
            ExternalReference: "acat-out");

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived && !replayApplied)
                {
                    replayApplied = true;
                    connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
                    connector.CompleteAccountTransferAsync(withdrawal, CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedTransfers([deposit, withdrawal], config, Money.USD(1_000_000m));

        Assert.Equal(
            replayEvents.OfType<AccountTransferCompleted>().Select(NormalizeTransferCompleted).ToArray(),
            simulationEvents.OfType<AccountTransferCompleted>().Select(NormalizeTransferCompleted).ToArray());

        Assert.Equal(
            replayEvents.OfType<AccountTransferStatusSnapshot>().Select(NormalizeTransferStatus).ToArray(),
            simulationEvents.OfType<AccountTransferStatusSnapshot>().Select(NormalizeTransferStatus).ToArray());

        var replayCustody = replayEvents.OfType<CustodyPositionSnapshot>().Last();
        var simulationCustody = simulationEvents.OfType<CustodyPositionSnapshot>().Last();
        Assert.Equal(NormalizeCustody(replayCustody), NormalizeCustody(simulationCustody));
    }

    [Fact]
    public async Task InternalAssetTransfer_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var destinationStrategyId = new StrategyId(8);
        var deposit = new AccountTransferCommand(
            new AccountTransferId(706),
            StrategyId,
            VariantId: 0,
            AccountTransferType.AssetDeposit,
            Instrument: Instrument,
            Quantity: new Qty(3m),
            CarryingPrice: new Price(100m, Currency.USD),
            ExternalReference: "internal-acat-in");
        var transfer = new AccountTransferCommand(
            new AccountTransferId(707),
            StrategyId,
            VariantId: 0,
            AccountTransferType.InternalTransfer,
            Instrument: Instrument,
            Quantity: new Qty(2m),
            CarryingPrice: new Price(100m, Currency.USD),
            ExternalReference: "internal-asset",
            DestinationStrategyId: destinationStrategyId,
            DestinationVariantId: 0);

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived && !replayApplied)
                {
                    replayApplied = true;
                    connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
                    connector.CompleteAccountTransferAsync(transfer, CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedTransfers([deposit, transfer], config, Money.USD(1_000_000m));

        Assert.Equal(
            replayEvents.OfType<AccountTransferCompleted>().Select(NormalizeTransferCompleted).ToArray(),
            simulationEvents.OfType<AccountTransferCompleted>().Select(NormalizeTransferCompleted).ToArray());

        var replayTransferStatus = replayEvents
            .OfType<AccountTransferStatusSnapshot>()
            .Single(status => status.TransferId == transfer.TransferId);
        var simulationTransferStatus = simulationEvents
            .OfType<AccountTransferStatusSnapshot>()
            .Single(status => status.TransferId == transfer.TransferId);
        Assert.Equal(NormalizeTransferStatus(replayTransferStatus), NormalizeTransferStatus(simulationTransferStatus));

        var replaySourceCustody = replayEvents
            .OfType<CustodyPositionSnapshot>()
            .Last(custody => custody.StrategyId == StrategyId);
        var simulationSourceCustody = simulationEvents
            .OfType<CustodyPositionSnapshot>()
            .Last(custody => custody.StrategyId == StrategyId);
        Assert.Equal(NormalizeCustody(replaySourceCustody), NormalizeCustody(simulationSourceCustody));

        var replayDestinationCustody = replayEvents
            .OfType<CustodyPositionSnapshot>()
            .Last(custody => custody.StrategyId == destinationStrategyId);
        var simulationDestinationCustody = simulationEvents
            .OfType<CustodyPositionSnapshot>()
            .Last(custody => custody.StrategyId == destinationStrategyId);
        Assert.Equal(NormalizeCustody(replayDestinationCustody), NormalizeCustody(simulationDestinationCustody));
    }

    [Fact]
    public async Task MarginRehypothecationAllowedCustodyTransfer_HasReplayConnectorParity()
    {
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(2m)
                .WithRehypothecationPolicy(RehypothecationPolicy.Allowed)
        };

        await AssertRehypothecationTransferParityAsync(new AccountTransferId(708), config);
    }

    [Fact]
    public async Task MarginRehypothecationAvailabilityCapCustodyTransfer_HasReplayConnectorParity()
    {
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(2m)
                .WithRehypothecationPolicy(RehypothecationPolicy.Allowed)
                .WithRehypothecationAvailability(Instrument, new Qty(1m))
        };

        await AssertRehypothecationTransferParityAsync(new AccountTransferId(709), config);
    }

    [Fact]
    public async Task MarginRehypothecationProhibitedCustodyTransfer_HasReplayConnectorParity()
    {
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.RegT()
                .WithRehypothecationPolicy(RehypothecationPolicy.Prohibited)
        };

        await AssertRehypothecationTransferParityAsync(new AccountTransferId(710), config);
    }

    [Fact]
    public async Task MarginShortWithLocatedBorrow_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.RegT().WithBorrowAvailability(Instrument, new Qty(2m))
        };
        var replayOrderId = new OrderId(134);
        var simulationOrderId = new OrderId(134);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Sell, new Qty(2m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Sell,
                            new Qty(2m),
                            Execution.Market()),
                        quote.Time);
                }
            });

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarginNakedSellWithoutBorrowRequirement_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = MarginConfig();
        var replayOrderId = new OrderId(10_134);
        var simulationOrderId = new OrderId(10_134);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Sell, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Sell,
                            new Qty(1m),
                            Execution.Market()),
                        quote.Time);
                }
            });

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
    }

    [Fact]
    public async Task MarginBorrowRequiredWithoutLocateRejects_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.RegT()
        };
        var replayOrderId = new OrderId(10_135);
        var simulationOrderId = new OrderId(10_135);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Sell, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Sell,
                            new Qty(1m),
                            Execution.Market()),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "borrow", "locate");
    }

    [Fact]
    public async Task MarginSellAgainstExistingLongWhenBorrowRequired_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.RegT()
        };
        var buyOrderId = new OrderId(10_136);
        var sellOrderId = new OrderId(10_137);

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(
                        StrategyId,
                        Instrument,
                        replayQuoteCount == 1 ? Side.Buy : Side.Sell,
                        new Qty(1m)) with
                    {
                        OrderId = replayQuoteCount == 1 ? buyOrderId : sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 101m, ask: 101.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                simulationQuoteCount++;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationQuoteCount == 1 ? buyOrderId : sellOrderId,
                        simulationQuoteCount == 1 ? Side.Buy : Side.Sell,
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            });

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray();
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal([buyOrderId, sellOrderId], simulationFills.Select(static fill => fill.OrderId).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
    }

    [Fact]
    public async Task MarginShortRejectsWhenLocatedBorrowIsExhausted_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.RegT().WithBorrowAvailability(Instrument, new Qty(1m))
        };
        var firstReplayOrderId = new OrderId(135);
        var secondReplayOrderId = new OrderId(136);
        var firstSimulationOrderId = new OrderId(135);
        var secondSimulationOrderId = new OrderId(136);

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Sell, new Qty(1m)) with
                    {
                        OrderId = replayQuoteCount == 1 ? firstReplayOrderId : secondReplayOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 101m, ask: 101.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                simulationQuoteCount++;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationQuoteCount == 1 ? firstSimulationOrderId : secondSimulationOrderId,
                        Side.Sell,
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            });

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));

        var replayRejected = Assert.Single(replayEvents.OfType<OrderRejected>());
        var simulationRejected = Assert.Single(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(NormalizeRejected(replayRejected), NormalizeRejected(simulationRejected));
        Assert.Contains("borrow", replayRejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("borrow", simulationRejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(secondReplayOrderId, replayRejected.OrderId);
        Assert.Equal(secondSimulationOrderId, simulationRejected.OrderId);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
    }

    [Fact]
    public async Task MarginBuyRejectsWhenInitialMarginExceedsCash_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(2m)
        };
        var initialCash = Money.USD(100m);
        var replayOrderId = new OrderId(137);
        var simulationOrderId = new OrderId(137);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(3m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: initialCash);

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(3m),
                            Execution.Market()),
                        quote.Time);
                }
            },
            initialCash: initialCash);

        AssertRejectedOnly(replayEvents, simulationEvents, "Insufficient margin buying power");
    }

    [Fact]
    public async Task MarginBuyWithinLeverage_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(4m)
        };
        var initialCash = Money.USD(100m);
        var replayOrderId = new OrderId(138);
        var simulationOrderId = new OrderId(138);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(3m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: initialCash);

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(3m),
                            Execution.Market()),
                        quote.Time);
                }
            },
            initialCash: initialCash);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarginOpenOrderReservation_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = MarginConfig(leverage: 2m);
        var initialCash = Money.USD(90m);
        var restingOrderId = new OrderId(10_155);
        var rejectedOrderId = new OrderId(10_156);
        var history =
            new[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(1), bid: 100m, ask: 100.05m)
            };

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                if (replayQuoteCount == 1)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = restingOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                    return;
                }

                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = rejectedOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: initialCash);

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                simulationQuoteCount++;
                if (simulationQuoteCount == 1)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            restingOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Limit().At(new Price(99m, Currency.USD))),
                        quote.Time);
                    return;
                }

                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        rejectedOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            },
            initialCash: initialCash);

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));
        Assert.Equal(restingOrderId, replayAccepted.OrderId);
        Assert.Equal(restingOrderId, simulationAccepted.OrderId);

        var replayRejected = Assert.Single(replayEvents.OfType<OrderRejected>());
        var simulationRejected = Assert.Single(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(NormalizeRejected(replayRejected), NormalizeRejected(simulationRejected));
        Assert.Equal(rejectedOrderId, replayRejected.OrderId);
        Assert.Equal(rejectedOrderId, simulationRejected.OrderId);
        Assert.Contains("Insufficient margin buying power", replayRejected.Reason);
        Assert.Contains("Insufficient margin buying power", simulationRejected.Reason);

        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
    }

    [Fact]
    public async Task MarginMaintenanceBreachLiquidatesAtBid_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = MarginConfig(leverage: 10m);
        var orderId = new OrderId(741);
        var history =
            new[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromMinutes(1), bid: 75m, ask: 75.05m)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not QuoteReceived)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(5m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: Money.USD(100m));

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not QuoteReceived quote)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderId,
                        Side.Buy,
                        new Qty(5m),
                        Execution.Market()),
                    quote.Time);
            },
            initialCash: Money.USD(100m));

        Assert.Equal(
            replayEvents.OfType<MarginStatusSnapshot>().Select(NormalizeMarginStatus).ToArray(),
            simulationEvents.OfType<MarginStatusSnapshot>().Select(NormalizeMarginStatus).ToArray());
        Assert.Equal(
            replayEvents.OfType<MarginCallIssued>().Select(NormalizeMarginCallIssued).ToArray(),
            simulationEvents.OfType<MarginCallIssued>().Select(NormalizeMarginCallIssued).ToArray());
        Assert.Empty(replayEvents.OfType<MarginCallResolved>());
        Assert.Empty(simulationEvents.OfType<MarginCallResolved>());
        Assert.Equal(
            replayEvents.OfType<RiskLimitBreached>().Select(NormalizeRiskLimit).ToArray(),
            simulationEvents.OfType<RiskLimitBreached>().Select(NormalizeRiskLimit).ToArray());

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFillEconomics).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFillEconomics).ToArray();
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal(2, simulationFills.Length);
        Assert.Equal(Side.Sell, simulationFills[1].Side);
        Assert.Equal(75m, simulationFills[1].PriceValue);
    }

    [Fact]
    public async Task MarginCancelOpenOrdersOnlyPolicyDoesNotFlattenPosition_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = MarginConfig(leverage: 10m) with
        {
            Margin = MarginParams.Leverage(10m)
                .WithLiquidationPolicy(LiquidationPolicy.CancelOpenOrdersOnly)
        };
        var buyOrderId = new OrderId(743);
        var restingOrderId = new OrderId(744);
        var history =
            new[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromMinutes(1), bid: 75m, ask: 75.05m)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not QuoteReceived)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        StrategyId,
                        Instrument,
                        Side.Sell,
                        new Qty(1m),
                        new Price(109m, Currency.USD)) with
                    {
                        OrderId = restingOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(5m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: Money.USD(100m));

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not QuoteReceived quote)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        restingOrderId,
                        Side.Sell,
                        new Qty(1m),
                        Execution.Limit().At(new Price(109m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        buyOrderId,
                        Side.Buy,
                        new Qty(5m),
                        Execution.Market()),
                    quote.Time);
            },
            initialCash: Money.USD(100m));

        Assert.Equal(
            replayEvents.OfType<MarginStatusSnapshot>().Select(NormalizeMarginStatus).ToArray(),
            simulationEvents.OfType<MarginStatusSnapshot>().Select(NormalizeMarginStatus).ToArray());
        Assert.Equal(
            replayEvents.OfType<RiskLimitBreached>().Select(NormalizeRiskLimit).ToArray(),
            simulationEvents.OfType<RiskLimitBreached>().Select(NormalizeRiskLimit).ToArray());

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Equal(restingOrderId, simulationCancelled.OrderId);
        Assert.Contains("margin liquidation", simulationCancelled.Reason, StringComparison.OrdinalIgnoreCase);

        var replayRawFills = replayEvents.OfType<OrderFilled>().ToArray();
        var simulationRawFills = simulationEvents.OfType<OrderFilled>().ToArray();
        var replayFills = replayRawFills.Select(NormalizeFillEconomics).ToArray();
        var simulationFills = simulationRawFills.Select(NormalizeFillEconomics).ToArray();
        Assert.Equal(replayFills, simulationFills);
        var simulationFill = Assert.Single(simulationRawFills);
        Assert.Equal(buyOrderId, simulationFill.OrderId);
        Assert.Equal(Side.Buy, simulationFill.Side);
    }

    [Fact]
    public async Task MarginReduceToMaintenancePolicyKeepsResidualPosition_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = MarginConfig(leverage: 10m) with
        {
            Margin = MarginParams.Leverage(10m)
                .WithLiquidationPolicy(LiquidationPolicy.CancelOpenOrdersAndReduceToMaintenance)
        };
        var buyOrderId = new OrderId(745);
        var history =
            new[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromMinutes(1), bid: 83m, ask: 83.05m)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not QuoteReceived)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(5m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: Money.USD(100m));

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not QuoteReceived quote)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        buyOrderId,
                        Side.Buy,
                        new Qty(5m),
                        Execution.Market()),
                    quote.Time);
            },
            initialCash: Money.USD(100m));

        Assert.Equal(
            replayEvents.OfType<RiskLimitBreached>().Select(NormalizeRiskLimit).ToArray(),
            simulationEvents.OfType<RiskLimitBreached>().Select(NormalizeRiskLimit).ToArray());

        var replayRawFills = replayEvents.OfType<OrderFilled>().ToArray();
        var simulationRawFills = simulationEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, simulationRawFills.Length);
        Assert.Equal(buyOrderId, simulationRawFills[0].OrderId);
        Assert.Equal(Side.Buy, simulationRawFills[0].Side);
        Assert.Equal(Side.Sell, simulationRawFills[1].Side);
        Assert.Equal(83m, simulationRawFills[1].FillPrice.Value);
        Assert.True(simulationRawFills[1].FilledQty.Value > 0m);
        Assert.True(simulationRawFills[1].FilledQty.Value < 5m);
        Assert.Equal(
            replayRawFills.Select(NormalizeFillEconomics).ToArray(),
            simulationRawFills.Select(NormalizeFillEconomics).ToArray());
    }

    [Fact]
    public async Task MarginMaintenanceBreachRecoversBeforeGracePeriod_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = MarginConfig(
            leverage: 10m,
            marginCallGracePeriod: Duration.FromMinutes(10));
        var orderId = new OrderId(742);
        var history =
            new[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromMinutes(1), bid: 75m, ask: 75.05m),
                CreateQuote(t0 + Duration.FromMinutes(2), bid: 100m, ask: 100.05m)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not QuoteReceived)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(4m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: Money.USD(100m));

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not QuoteReceived quote)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderId,
                        Side.Buy,
                        new Qty(4m),
                        Execution.Market()),
                    quote.Time);
            },
            initialCash: Money.USD(100m));

        Assert.Equal(
            replayEvents.OfType<MarginStatusSnapshot>().Select(NormalizeMarginStatus).ToArray(),
            simulationEvents.OfType<MarginStatusSnapshot>().Select(NormalizeMarginStatus).ToArray());
        Assert.Equal(
            replayEvents.OfType<MarginCallIssued>().Select(NormalizeMarginCallIssued).ToArray(),
            simulationEvents.OfType<MarginCallIssued>().Select(NormalizeMarginCallIssued).ToArray());
        Assert.Equal(
            replayEvents.OfType<MarginCallResolved>().Select(NormalizeMarginCallResolved).ToArray(),
            simulationEvents.OfType<MarginCallResolved>().Select(NormalizeMarginCallResolved).ToArray());
        Assert.Empty(replayEvents.OfType<RiskLimitBreached>());
        Assert.Empty(simulationEvents.OfType<RiskLimitBreached>());

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillEconomics(replayFill), NormalizeFillEconomics(simulationFill));
    }

    [Fact]
    public async Task MarginLiquidationCancelsOpenOrders_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_100);
        var config = MarginConfig(leverage: 10m);
        var buyOrderId = new OrderId(10_152);
        var restingOrderId = new OrderId(10_153);
        var history =
            new[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(30), bid: 75m, ask: 75.05m)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not QuoteReceived)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        StrategyId,
                        Instrument,
                        Side.Sell,
                        new Qty(1m),
                        new Price(109m, Currency.USD)) with
                    {
                        OrderId = restingOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(5m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: Money.USD(100m));

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not QuoteReceived quote)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        restingOrderId,
                        Side.Sell,
                        new Qty(1m),
                        Execution.Limit().At(new Price(109m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        buyOrderId,
                        Side.Buy,
                        new Qty(5m),
                        Execution.Market()),
                    quote.Time);
            },
            initialCash: Money.USD(100m));

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Equal(restingOrderId, replayCancelled.OrderId);
        Assert.Equal(new Qty(1m), replayCancelled.RemainingQty);
        Assert.Contains("margin liquidation", replayCancelled.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("margin liquidation", simulationCancelled.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            replayEvents.OfType<RiskLimitBreached>().Select(NormalizeRiskLimit).ToArray(),
            simulationEvents.OfType<RiskLimitBreached>().Select(NormalizeRiskLimit).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>()
                .Where(state => state.OrderId == buyOrderId || state.OrderId == restingOrderId)
                .Select(NormalizeOrderState)
                .ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>()
                .Where(state => state.OrderId == buyOrderId || state.OrderId == restingOrderId)
                .Select(NormalizeOrderState)
                .ToArray());
    }

    [Fact]
    public async Task MarginCallLiquidatesWhenGraceExpiresStillBreached_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = MarginConfig(leverage: 10m, marginCallGracePeriod: Duration.FromMinutes(1));
        var orderId = new OrderId(10_154);
        var history =
            new[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(30), bid: 75m, ask: 75.05m),
                CreateQuote(t0 + Duration.FromMinutes(2), bid: 74m, ask: 74.05m)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not QuoteReceived)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(5m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: Money.USD(100m));

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not QuoteReceived quote)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderId,
                        Side.Buy,
                        new Qty(5m),
                        Execution.Market()),
                    quote.Time);
            },
            initialCash: Money.USD(100m));

        Assert.Equal(
            replayEvents.OfType<MarginCallIssued>().Select(NormalizeMarginCallIssued).ToArray(),
            simulationEvents.OfType<MarginCallIssued>().Select(NormalizeMarginCallIssued).ToArray());
        Assert.Empty(replayEvents.OfType<MarginCallResolved>());
        Assert.Empty(simulationEvents.OfType<MarginCallResolved>());
        Assert.Equal(
            replayEvents.OfType<RiskLimitBreached>().Select(NormalizeRiskLimit).ToArray(),
            simulationEvents.OfType<RiskLimitBreached>().Select(NormalizeRiskLimit).ToArray());

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFillEconomics).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFillEconomics).ToArray();
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal([Side.Buy, Side.Sell], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.Side).ToArray());
        Assert.Equal(74m, simulationEvents.OfType<OrderFilled>().Last().FillPrice.Value);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>()
                .Where(state => state.OrderId == orderId)
                .Select(NormalizeOrderState)
                .ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>()
                .Where(state => state.OrderId == orderId)
                .Select(NormalizeOrderState)
                .ToArray());
    }

    [Fact]
    public async Task UnsupportedContingentOrderPolicy_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var orderListId = new OrderListId("oco-parity");
        var replayOrderId = new OrderId(136);
        var simulationOrderId = new OrderId(136);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Sell,
                            new Qty(1m),
                            new Price(101m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            OrderListId = orderListId,
                            ContingencyType = ContingencyType.OCO
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
                {
                    [Instrument.Venue] = ReplayVenueSimulationPolicy.Default with
                    {
                        SupportContingentOrders = false
                    }
                };
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Sell,
                            new Qty(1m),
                            Execution.Limit().At(new Price(101m, Currency.USD)),
                            orderListId,
                            ContingencyType.OCO),
                        quote.Time);
                }
            },
            SimulationVenuePolicy.Default with
            {
                SupportContingentOrders = false
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "contingent");
    }

    [Fact]
    public async Task RejectTriggeredOrdersInMarketPolicy_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(138);
        var simulationOrderId = new OrderId(138);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 99.95m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.StopMarket(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(100m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
                {
                    [Instrument.Venue] = ReplayVenueSimulationPolicy.Default with
                    {
                        RejectTriggeredOrdersInMarket = true
                    }
                };
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 99.95m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.StopMarket(new Price(100m, Currency.USD))),
                        quote.Time);
                }
            },
            SimulationVenuePolicy.Default with
            {
                RejectTriggeredOrdersInMarket = true
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "trigger");
    }

    [Fact]
    public async Task PriceProtectionStopsMarketBookWalk_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(139);
        var simulationOrderId = new OrderId(139);
        var book = CreateBookSnapshot(
            now,
            bids: [new Level(new Price(99.99m, Currency.USD), new Qty(100m))],
            asks:
            [
                new Level(new Price(100.01m, Currency.USD), new Qty(40m)),
                new Level(new Price(100.03m, Currency.USD), new Qty(80m))
            ]);

        var replayEvents = await RunReplayConnectorAsync(
            [book],
            config,
            (connector, evt) =>
            {
                if (evt is BookSnapshotReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(60m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
                {
                    [Instrument.Venue] = ReplayVenueSimulationPolicy.Default with
                    {
                        PriceProtectionTicks = 1
                    }
                };
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationEvents = RunSimulatedVenue(
            [book],
            config,
            (venue, evt) =>
            {
                if (evt is BookSnapshotReceived snapshot)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(60m),
                            Execution.Market()),
                        snapshot.Time);
                }
            },
            SimulationVenuePolicy.Default with
            {
                PriceProtectionTicks = 1
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task TakerPriceImprovementOnMarketDepthFill_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            PriceImprovement = PriceImprovementParams.FixedBps(takerBps: 10m)
        };
        var replayOrderId = new OrderId(140);
        var simulationOrderId = new OrderId(140);
        var book = CreateBookSnapshot(
            now,
            bids: [new Level(new Price(99.99m, Currency.USD), new Qty(100m))],
            asks: [new Level(new Price(100.01m, Currency.USD), new Qty(100m))]);

        var replayEvents = await RunReplayConnectorAsync(
            [book],
            config,
            (connector, evt) =>
            {
                if (evt is BookSnapshotReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(10m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationEvents = RunSimulatedVenue(
            [book],
            config,
            (venue, evt) =>
            {
                if (evt is BookSnapshotReceived snapshot)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Market()),
                        snapshot.Time);
                }
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(99.90999m, simulationFill.FillPrice.Value);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketBuyWalksAskBookLevels_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(141);
        var simulationOrderId = new OrderId(141);
        var book = CreateBookSnapshot(
            now,
            bids: [new Level(new Price(99.99m, Currency.USD), new Qty(100m))],
            asks:
            [
                new Level(new Price(100.01m, Currency.USD), new Qty(40m)),
                new Level(new Price(100.02m, Currency.USD), new Qty(80m))
            ]);

        var replayEvents = await RunReplayConnectorAsync(
            [book],
            config,
            (connector, evt) =>
            {
                if (evt is BookSnapshotReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(60m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationEvents = RunSimulatedVenue(
            [book],
            config,
            (venue, evt) =>
            {
                if (evt is BookSnapshotReceived snapshot)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(60m),
                            Execution.Market()),
                        snapshot.Time);
                }
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
    }

    [Fact]
    public async Task MarketBuyCancelsRemainderWhenBookLiquidityIsExhausted_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(151);
        var simulationOrderId = new OrderId(151);
        var book = CreateBookSnapshot(
            now,
            bids: [new Level(new Price(99.99m, Currency.USD), new Qty(100m))],
            asks:
            [
                new Level(new Price(100.01m, Currency.USD), new Qty(40m)),
                new Level(new Price(100.02m, Currency.USD), new Qty(80m))
            ]);

        var replayEvents = await RunReplayConnectorAsync(
            [book],
            config,
            (connector, evt) =>
            {
                if (evt is BookSnapshotReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(150m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationEvents = RunSimulatedVenue(
            [book],
            config,
            (venue, evt) =>
            {
                if (evt is BookSnapshotReceived snapshot)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(150m),
                            Execution.Market()),
                        snapshot.Time);
                }
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Equal(new Qty(30m), simulationCancelled.RemainingQty);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketOrdersConsumeFiniteExternalBookLiquidity_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var firstReplayOrderId = new OrderId(152);
        var secondReplayOrderId = new OrderId(153);
        var firstSimulationOrderId = new OrderId(152);
        var secondSimulationOrderId = new OrderId(153);
        var book = CreateBookSnapshot(
            now,
            bids: [new Level(new Price(99.99m, Currency.USD), new Qty(100m))],
            asks:
            [
                new Level(new Price(100.01m, Currency.USD), new Qty(40m)),
                new Level(new Price(100.02m, Currency.USD), new Qty(80m))
            ]);

        var replayEvents = await RunReplayConnectorAsync(
            [book],
            config,
            (connector, evt) =>
            {
                if (evt is BookSnapshotReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(70m)) with
                        {
                            OrderId = firstReplayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(60m)) with
                        {
                            OrderId = secondReplayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationEvents = RunSimulatedVenue(
            [book],
            config,
            (venue, evt) =>
            {
                if (evt is BookSnapshotReceived snapshot)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            firstSimulationOrderId,
                            Side.Buy,
                            new Qty(70m),
                            Execution.Market()),
                        snapshot.Time);
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            secondSimulationOrderId,
                            Side.Buy,
                            new Qty(60m),
                            Execution.Market()),
                        snapshot.Time);
                }
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Equal(new Qty(10m), simulationCancelled.RemainingQty);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task FokMarketOrderSeesPreviouslyConsumedExternalDepth_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var firstReplayOrderId = new OrderId(142);
        var fokReplayOrderId = new OrderId(143);
        var firstSimulationOrderId = new OrderId(142);
        var fokSimulationOrderId = new OrderId(143);
        var book = CreateBookSnapshot(
            now,
            bids: [new Level(new Price(99.99m, Currency.USD), new Qty(100m))],
            asks:
            [
                new Level(new Price(100.01m, Currency.USD), new Qty(40m)),
                new Level(new Price(100.02m, Currency.USD), new Qty(80m))
            ]);

        var replayEvents = await RunReplayConnectorAsync(
            [book],
            config,
            (connector, evt) =>
            {
                if (evt is BookSnapshotReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(40m)) with
                        {
                            OrderId = firstReplayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(90m)) with
                        {
                            OrderId = fokReplayOrderId,
                            TimeInForce = TimeInForce.FOK
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationEvents = RunSimulatedVenue(
            [book],
            config,
            (venue, evt) =>
            {
                if (evt is BookSnapshotReceived snapshot)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            firstSimulationOrderId,
                            Side.Buy,
                            new Qty(40m),
                            Execution.Market()),
                        snapshot.Time);
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            fokSimulationOrderId,
                            Side.Buy,
                            new Qty(90m),
                            new ExecutionSpec(OrderType.Market, timeInForce: TimeInForce.FOK)),
                        snapshot.Time);
                }
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray());

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Equal(fokSimulationOrderId, simulationCancelled.OrderId);
        Assert.Equal(new Qty(90m), simulationCancelled.RemainingQty);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketSellsConsumeFiniteExternalBidLiquidity_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = MarginConfig();
        var firstReplayOrderId = new OrderId(144);
        var secondReplayOrderId = new OrderId(145);
        var firstSimulationOrderId = new OrderId(144);
        var secondSimulationOrderId = new OrderId(145);
        var book = CreateBookSnapshot(
            now,
            bids:
            [
                new Level(new Price(99.99m, Currency.USD), new Qty(50m)),
                new Level(new Price(99.98m, Currency.USD), new Qty(75m))
            ],
            asks: [new Level(new Price(100.01m, Currency.USD), new Qty(100m))]);

        var replayEvents = await RunReplayConnectorAsync(
            [book],
            config,
            (connector, evt) =>
            {
                if (evt is BookSnapshotReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Sell, new Qty(80m)) with
                        {
                            OrderId = firstReplayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Sell, new Qty(60m)) with
                        {
                            OrderId = secondReplayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationEvents = RunSimulatedVenue(
            [book],
            config,
            (venue, evt) =>
            {
                if (evt is BookSnapshotReceived snapshot)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            firstSimulationOrderId,
                            Side.Sell,
                            new Qty(80m),
                            Execution.Market()),
                        snapshot.Time);
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            secondSimulationOrderId,
                            Side.Sell,
                            new Qty(60m),
                            Execution.Market()),
                        snapshot.Time);
                }
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Equal(new Qty(15m), simulationCancelled.RemainingQty);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task FokMarketSellSeesPreviouslyConsumedExternalBidDepth_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = MarginConfig();
        var firstReplayOrderId = new OrderId(154);
        var fokReplayOrderId = new OrderId(155);
        var firstSimulationOrderId = new OrderId(154);
        var fokSimulationOrderId = new OrderId(155);
        var book = CreateBookSnapshot(
            now,
            bids:
            [
                new Level(new Price(99.99m, Currency.USD), new Qty(50m)),
                new Level(new Price(99.98m, Currency.USD), new Qty(75m))
            ],
            asks: [new Level(new Price(100.01m, Currency.USD), new Qty(100m))]);

        var replayEvents = await RunReplayConnectorAsync(
            [book],
            config,
            (connector, evt) =>
            {
                if (evt is BookSnapshotReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Sell, new Qty(80m)) with
                        {
                            OrderId = firstReplayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Sell, new Qty(60m)) with
                        {
                            OrderId = fokReplayOrderId,
                            TimeInForce = TimeInForce.FOK
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationEvents = RunSimulatedVenue(
            [book],
            config,
            (venue, evt) =>
            {
                if (evt is BookSnapshotReceived snapshot)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            firstSimulationOrderId,
                            Side.Sell,
                            new Qty(80m),
                            Execution.Market()),
                        snapshot.Time);
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            fokSimulationOrderId,
                            Side.Sell,
                            new Qty(60m),
                            new ExecutionSpec(OrderType.Market, timeInForce: TimeInForce.FOK)),
                        snapshot.Time);
                }
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFill).ToArray());

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Equal(fokSimulationOrderId, simulationCancelled.OrderId);
        Assert.Equal(new Qty(60m), simulationCancelled.RemainingQty);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task NewBookSnapshotRefreshesExternalBookLiquidity_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var firstReplayOrderId = new OrderId(146);
        var secondReplayOrderId = new OrderId(147);
        var firstSimulationOrderId = new OrderId(146);
        var secondSimulationOrderId = new OrderId(147);
        var firstBook = CreateBookSnapshot(
            t0,
            bids: [new Level(new Price(99.99m, Currency.USD), new Qty(100m))],
            asks:
            [
                new Level(new Price(100.01m, Currency.USD), new Qty(40m)),
                new Level(new Price(100.02m, Currency.USD), new Qty(80m))
            ]);
        var secondBook = CreateBookSnapshot(
            t0 + Duration.FromSeconds(1),
            bids: [new Level(new Price(99.99m, Currency.USD), new Qty(100m))],
            asks:
            [
                new Level(new Price(100.01m, Currency.USD), new Qty(40m)),
                new Level(new Price(100.02m, Currency.USD), new Qty(80m))
            ]);

        var replayBookCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            [firstBook, secondBook],
            config,
            (connector, evt) =>
            {
                if (evt is BookSnapshotReceived)
                {
                    replayBookCount++;
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(120m)) with
                        {
                            OrderId = replayBookCount == 1 ? firstReplayOrderId : secondReplayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationBookCount = 0;
        var simulationEvents = RunSimulatedVenue(
            [firstBook, secondBook],
            config,
            (venue, evt) =>
            {
                if (evt is BookSnapshotReceived snapshot)
                {
                    simulationBookCount++;
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationBookCount == 1 ? firstSimulationOrderId : secondSimulationOrderId,
                            Side.Buy,
                            new Qty(120m),
                            Execution.Market()),
                        snapshot.Time);
                }
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task BookLevelDeltaUpdatesExternalDepthForMarketOrder_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(148);
        var simulationOrderId = new OrderId(148);
        var delta = new BookLevelDeltaReceived(
            Instrument,
            new BookLevelDelta(
                Side.Sell,
                new Price(101m, Currency.USD),
                new Qty(10m),
                BookAction.Add))
        {
            Time = now
        };

        var replayEvents = await RunReplayConnectorAsync(
            [delta],
            config,
            (connector, evt) =>
            {
                if (evt is BookLevelDeltaReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(5m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth)]);

        var simulationEvents = RunSimulatedVenue(
            [delta],
            config,
            (venue, evt) =>
            {
                if (evt is BookLevelDeltaReceived levelDelta)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(5m),
                            Execution.Market()),
                        levelDelta.Time);
                }
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task BookLevelDeltasApplyInSourceOrderAndDeleteBestAsk_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(149);
        var simulationOrderId = new OrderId(149);
        var deltas = new BookLevelDeltasReceived(
            Instrument,
            [
                new BookLevelDelta(Side.Sell, new Price(101m, Currency.USD), new Qty(10m), BookAction.Add, VenueSequence: 1),
                new BookLevelDelta(Side.Sell, new Price(102m, Currency.USD), new Qty(10m), BookAction.Add, VenueSequence: 2),
                new BookLevelDelta(Side.Sell, new Price(101m, Currency.USD), Qty.Zero, BookAction.Delete, VenueSequence: 3)
            ])
        {
            Time = now
        };

        var replayEvents = await RunReplayConnectorAsync(
            [deltas],
            config,
            (connector, evt) =>
            {
                if (evt is BookLevelDeltasReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(5m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth)]);

        var simulationEvents = RunSimulatedVenue(
            [deltas],
            config,
            (venue, evt) =>
            {
                if (evt is BookLevelDeltasReceived batch)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(5m),
                            Execution.Market()),
                        batch.Time);
                }
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(102m, simulationFill.FillPrice.Value);
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task FixedDepthSnapshotReplacesVisibleDepth_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(150);
        var simulationOrderId = new OrderId(150);
        var initialBook = CreateBookSnapshot(
            t0,
            bids: [new Level(new Price(99m, Currency.USD), new Qty(10m))],
            asks: [new Level(new Price(101m, Currency.USD), new Qty(10m))]);
        var depth = new BookDepthSnapshotReceived(
            Instrument,
            Bids: [new Level(new Price(98m, Currency.USD), new Qty(10m))],
            Asks: [new Level(new Price(103m, Currency.USD), new Qty(10m))],
            Depth: 1)
        {
            Time = t0 + Duration.FromMillis(1)
        };

        var replayEvents = await RunReplayConnectorAsync(
            [initialBook, depth],
            config,
            (connector, evt) =>
            {
                if (evt is BookDepthSnapshotReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(5m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth)]);

        var simulationEvents = RunSimulatedVenue(
            [initialBook, depth],
            config,
            (venue, evt) =>
            {
                if (evt is BookDepthSnapshotReceived snapshot)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(5m),
                            Execution.Market()),
                        snapshot.Time);
                }
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(103m, simulationFill.FillPrice.Value);
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task StopMarketBuyFillsWhenReplayBarTouchesStop_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(870);
        var simulationOrderId = new OrderId(870);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 120m, ask: 121m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 120m, high: 125m, low: 119m, close: 124m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.StopMarket(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(124m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 120m, ask: 121m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 120m, high: 125m, low: 119m, close: 124m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.StopMarket(new Price(124m, Currency.USD))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task StopLimitBuyFillsWhenReplayBarTouchesStopAndLimit_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(871);
        var simulationOrderId = new OrderId(871);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 105m, low: 102m, close: 104m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.StopLimit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(104m, Currency.USD),
                            new Price(103m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 105m, low: 102m, close: 104m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.StopLimit(
                                new Price(104m, Currency.USD),
                                new Price(103m, Currency.USD))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketIfTouchedBuyFillsWhenReplayBarTouchesTrigger_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(880);
        var simulationOrderId = new OrderId(880);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 101m, low: 98m, close: 99m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.MarketIfTouched(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 101m, low: 98m, close: 99m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.MarketIfTouched(new Price(99m, Currency.USD))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketIfTouchedBuyRemainsOpenWhenReplayBarDoesNotTouchTrigger_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(881);
        var simulationOrderId = new OrderId(881);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 101m, low: 99m, close: 100m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.MarketIfTouched(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(98m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 101m, low: 99m, close: 100m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.MarketIfTouched(new Price(98m, Currency.USD))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task LimitIfTouchedBuyFillsWhenTriggerAndLimitAreTouched_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(882);
        var simulationOrderId = new OrderId(882);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 101m, low: 98m, close: 99m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.LimitIfTouched(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(99m, Currency.USD),
                            new Price(98.50m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 101m, low: 98m, close: 99m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.LimitIfTouched(
                                new Price(99m, Currency.USD),
                                new Price(98.50m, Currency.USD))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task LimitIfTouchedBuyRemainsOpenUntilTriggerIsTouched_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(10_882);
        var simulationOrderId = new OrderId(10_882);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 101m, low: 99m, close: 100m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.LimitIfTouched(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(98m, Currency.USD),
                            new Price(99.50m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 101m, low: 99m, close: 100m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.LimitIfTouched(
                                new Price(98m, Currency.USD),
                                new Price(99.50m, Currency.USD))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketToLimitFillsAtCurrentMarketPrice_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(883);
        var simulationOrderId = new OrderId(883);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        new SubmitOrder(
                            replayOrderId,
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            OrderType.MarketToLimit),
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.MarketToLimit()),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task TrailingStopMarketSellTracksHighAndTriggersOnReversal_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(2m)
        };
        var replayOrderId = new OrderId(884);
        var simulationOrderId = new OrderId(884);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 110m, low: 106m, close: 108m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.TrailingStop(
                            StrategyId,
                            Instrument,
                            Side.Sell,
                            new Qty(10m),
                            3m,
                            TrailingOffsetType.Price) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 110m, low: 106m, close: 108m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Sell,
                            new Qty(10m),
                            Execution.TrailingStop(3m, TrailingOffsetType.Price)),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task TrailingStopLimitSellTracksHighAndFillsLimitAfterTrigger_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(2m)
        };
        var replayOrderId = new OrderId(885);
        var simulationOrderId = new OrderId(885);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 110m, low: 106m, close: 108m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.TrailingStopLimit(
                            StrategyId,
                            Instrument,
                            Side.Sell,
                            new Qty(10m),
                            3m,
                            TrailingOffsetType.Price,
                            new Price(106.50m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 110m, low: 106m, close: 108m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Sell,
                            new Qty(10m),
                            Execution.TrailingStopLimit(
                                3m,
                                TrailingOffsetType.Price,
                                new Price(106.50m, Currency.USD))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task TrailingStopMarketBuyTracksLowAndTriggersOnBounce_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(886);
        var simulationOrderId = new OrderId(886);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 94m, low: 90m, close: 92m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.TrailingStop(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            3m,
                            TrailingOffsetType.Price) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 94m, low: 90m, close: 92m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.TrailingStop(3m, TrailingOffsetType.Price)),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task TrailingStopMarketSellSupportsTickOffsets_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(2m)
        };
        var replayOrderId = new OrderId(889);
        var simulationOrderId = new OrderId(889);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 110m, low: 106m, close: 108m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.TrailingStop(
                            StrategyId,
                            Instrument,
                            Side.Sell,
                            new Qty(10m),
                            300m,
                            TrailingOffsetType.Ticks) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 110m, low: 106m, close: 108m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Sell,
                            new Qty(10m),
                            Execution.TrailingStop(300m, TrailingOffsetType.Ticks)),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task TrailingStopMarketSellSupportsPercentOffsets_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(2m)
        };
        var replayOrderId = new OrderId(890);
        var simulationOrderId = new OrderId(890);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 110m, low: 98m, close: 108m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.TrailingStop(
                            StrategyId,
                            Instrument,
                            Side.Sell,
                            new Qty(10m),
                            10m,
                            TrailingOffsetType.Percent) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 110m, low: 98m, close: 108m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Sell,
                            new Qty(10m),
                            Execution.TrailingStop(10m, TrailingOffsetType.Percent)),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task TrailingStopMarketRejectsMissingOffsetMetadata_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(891);
        var simulationOrderId = new OrderId(891);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        new SubmitOrder(
                            replayOrderId,
                            StrategyId,
                            Instrument,
                            Side.Sell,
                            new Qty(10m),
                            OrderType.TrailingStopMarket),
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Sell,
                            new Qty(10m),
                            new ExecutionSpec(OrderType.TrailingStopMarket)),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "requires trailing offset");
    }

    [Fact]
    public async Task IcebergLimitRefreshesDisplayQuantityAcrossTradeFills_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var replayOrderId = new OrderId(887);
        var simulationOrderId = new OrderId(887);
        var history = new FinanceEvent[]
        {
            CreateQuote(t0, bid: 100m, ask: 100.05m),
            CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 10m, Side.Sell),
            CreateTrade(t0 + Duration.FromSeconds(2), price: 100m, size: 10m, Side.Sell),
            CreateTrade(t0 + Duration.FromSeconds(3), price: 100m, size: 10m, Side.Sell)
        };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.IcebergLimit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(100m, Currency.USD),
                            new Qty(4m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Quotes)]);

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Limit().At(new Price(100m, Currency.USD)).Display(new Qty(4m))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));
        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal([4m, 4m, 2m], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.FilledQty.Value).ToArray());

        var replayStates = replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray();
        var simulationStates = simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray();
        Assert.Equal(replayStates, simulationStates);
        Assert.Equal(
            [OrderStatus.Open, OrderStatus.PartiallyFilled, OrderStatus.PartiallyFilled, OrderStatus.Filled],
            simulationStates.Select(static state => state.Status).ToArray());
        Assert.Equal([null, 4m, 8m, 10m], simulationStates.Select(static state => state.FilledQuantity?.Value).ToArray());
        Assert.Equal([null, 6m, 2m, 0m], simulationStates.Select(static state => state.RemainingQuantity?.Value).ToArray());

        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task InvalidIcebergDisplayQuantityRejectsBeforeAcceptance_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(888);
        var simulationOrderId = new OrderId(888);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 99m, ask: 101m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.IcebergLimit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(100m, Currency.USD),
                            new Qty(10m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 99m, ask: 101m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Limit().At(new Price(100m, Currency.USD)).Display(new Qty(10m))),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "display quantity");
    }

    [Fact]
    public async Task RestingLimitSamePriceTradeAllocationUsesFifo_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_100);
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var firstOrderId = new OrderId(941);
        var secondOrderId = new OrderId(942);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 6m, Side.Sell),
                CreateTrade(t0 + Duration.FromSeconds(2), price: 100m, size: 4m, Side.Sell)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not TradeOccurred)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        StrategyId,
                        Instrument,
                        Side.Buy,
                        new Qty(5m),
                        new Price(100m, Currency.USD)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        StrategyId,
                        Instrument,
                        Side.Buy,
                        new Qty(5m),
                        new Price(100m, Currency.USD)) with
                    {
                        OrderId = secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Trades)
            ]);

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not TradeOccurred trade)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        firstOrderId,
                        Side.Buy,
                        new Qty(5m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    trade.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        secondOrderId,
                        Side.Buy,
                        new Qty(5m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    trade.Time);
            },
            invokeOnEventBeforeMarketEvent: true);

        Assert.Equal(
            replayEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray(),
            simulationEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal([firstOrderId, secondOrderId, secondOrderId], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([5m, 1m, 4m], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.FilledQty.Value).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task RestingBuyLimitsAllocateBetterPriceBeforeFifo_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_200);
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var worseOrderId = new OrderId(943);
        var betterOrderId = new OrderId(944);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 1m, Side.Sell)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not TradeOccurred)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        StrategyId,
                        Instrument,
                        Side.Buy,
                        new Qty(1m),
                        new Price(100m, Currency.USD)) with
                    {
                        OrderId = worseOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        StrategyId,
                        Instrument,
                        Side.Buy,
                        new Qty(1m),
                        new Price(101m, Currency.USD)) with
                    {
                        OrderId = betterOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Trades)
            ]);

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not TradeOccurred trade)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        worseOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    trade.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        betterOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(101m, Currency.USD))),
                    trade.Time);
            },
            invokeOnEventBeforeMarketEvent: true);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));
        Assert.Equal(betterOrderId, simulationFill.OrderId);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task RestingBuyLimitsAllocateByPriceLevelThenFifoWithinLevel_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_225);
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var worseOrderId = new OrderId(10_943);
        var firstBetterOrderId = new OrderId(10_944);
        var secondBetterOrderId = new OrderId(10_945);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 2m, Side.Sell)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not TradeOccurred)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Buy, new Qty(1m), new Price(100m, Currency.USD)) with
                    {
                        OrderId = worseOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Buy, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = firstBetterOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Buy, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = secondBetterOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Trades)
            ]);

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not TradeOccurred trade)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        worseOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    trade.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        firstBetterOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(101m, Currency.USD))),
                    trade.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        secondBetterOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(101m, Currency.USD))),
                    trade.Time);
            },
            invokeOnEventBeforeMarketEvent: true);

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal([firstBetterOrderId, secondBetterOrderId], simulationFills.Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([1m, 1m], simulationFills.Select(static fill => fill.Quantity.Value).ToArray());
        Assert.DoesNotContain(simulationFills, fill => fill.OrderId == worseOrderId);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task RestingSellLimitsAllocateBetterPriceBeforeFifo_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_250);
        var config = MarginConfig() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var worseOrderId = new OrderId(10_946);
        var betterOrderId = new OrderId(10_947);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 1m, Side.Buy)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not TradeOccurred)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        StrategyId,
                        Instrument,
                        Side.Sell,
                        new Qty(1m),
                        new Price(100m, Currency.USD)) with
                    {
                        OrderId = worseOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        StrategyId,
                        Instrument,
                        Side.Sell,
                        new Qty(1m),
                        new Price(99m, Currency.USD)) with
                    {
                        OrderId = betterOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Trades)
            ]);

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not TradeOccurred trade)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        worseOrderId,
                        Side.Sell,
                        new Qty(1m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    trade.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        betterOrderId,
                        Side.Sell,
                        new Qty(1m),
                        Execution.Limit().At(new Price(99m, Currency.USD))),
                    trade.Time);
            },
            invokeOnEventBeforeMarketEvent: true);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));
        Assert.Equal(betterOrderId, simulationFill.OrderId);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task IncreasingRestingQuantityLosesFifoPriority_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_300);
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var firstOrderId = new OrderId(945);
        var secondOrderId = new OrderId(946);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 1m, Side.Sell)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not TradeOccurred)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Buy, new Qty(1m), new Price(100m, Currency.USD)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Buy, new Qty(1m), new Price(100m, Currency.USD)) with
                    {
                        OrderId = secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.ModifyOrderAsync(
                    new ModifyOrder(firstOrderId, NewQuantity: new Qty(2m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Trades)
            ]);

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not TradeOccurred trade)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        firstOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    trade.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        secondOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    trade.Time);
                venue.Modify(
                    new SimulationModifyCommand(
                        StrategyId,
                        0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        firstOrderId,
                        NewQuantity: new Qty(2m)),
                    trade.Time);
            },
            invokeOnEventBeforeMarketEvent: true);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));
        Assert.Equal(secondOrderId, simulationFill.OrderId);
        Assert.Equal(
            replayEvents.OfType<OrderModified>().Select(NormalizeModified).ToArray(),
            simulationEvents.OfType<OrderModified>().Select(NormalizeModified).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task ReducingRestingQuantityPreservesFifoPriority_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_400);
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var firstOrderId = new OrderId(947);
        var secondOrderId = new OrderId(948);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 1m, Side.Sell)
            };

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not TradeOccurred)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Buy, new Qty(2m), new Price(100m, Currency.USD)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Buy, new Qty(1m), new Price(100m, Currency.USD)) with
                    {
                        OrderId = secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.ModifyOrderAsync(
                    new ModifyOrder(firstOrderId, NewQuantity: new Qty(1m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Trades)
            ]);

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not TradeOccurred trade)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        firstOrderId,
                        Side.Buy,
                        new Qty(2m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    trade.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        secondOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    trade.Time);
                venue.Modify(
                    new SimulationModifyCommand(
                        StrategyId,
                        0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        firstOrderId,
                        NewQuantity: new Qty(1m)),
                    trade.Time);
            },
            invokeOnEventBeforeMarketEvent: true);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));
        Assert.Equal(firstOrderId, simulationFill.OrderId);
        Assert.Equal(
            replayEvents.OfType<OrderModified>().Select(NormalizeModified).ToArray(),
            simulationEvents.OfType<OrderModified>().Select(NormalizeModified).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task CrossedLimitMatchesRestingBook_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_500);
        var config = MarginConfig();
        var sellOrderId = new OrderId(949);
        var buyOrderId = new OrderId(950);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 99m, ask: 102m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Sell, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(8), Instrument, Side.Buy, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        sellOrderId,
                        Side.Sell,
                        new Qty(1m),
                        Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        new StrategyId(8),
                        0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        buyOrderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
            });

        Assert.Equal(
            replayEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray(),
            simulationEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal([sellOrderId, buyOrderId], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([Side.Sell, Side.Buy], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.Side).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task CrossedLimitRemainderRestsAndMatchesLaterOrder_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_600);
        var config = MarginConfig();
        var firstSellOrderId = new OrderId(951);
        var buyOrderId = new OrderId(952);
        var secondSellOrderId = new OrderId(953);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 99m, ask: 102m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Sell, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = firstSellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(8), Instrument, Side.Buy, new Qty(2m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(9), Instrument, Side.Sell, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = secondSellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, firstSellOrderId, Side.Sell, new Qty(1m), Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(new StrategyId(8), 0, AssetId, Instrument, Instrument.Venue, buyOrderId, Side.Buy, new Qty(2m), Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(new StrategyId(9), 0, AssetId, Instrument, Instrument.Venue, secondSellOrderId, Side.Sell, new Qty(1m), Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
            });

        Assert.Equal(
            replayEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray(),
            simulationEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal([firstSellOrderId, buyOrderId, buyOrderId, secondSellOrderId], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.OrderId).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketOrderMatchesRestingBookBeforeExternalDepth_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_700);
        var config = MarginConfig();
        var sellOrderId = new OrderId(954);
        var buyOrderId = new OrderId(955);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 99m, ask: 102m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Sell, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(8), Instrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, sellOrderId, Side.Sell, new Qty(1m), Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(new StrategyId(8), 0, AssetId, Instrument, Instrument.Venue, buyOrderId, Side.Buy, new Qty(1m), Execution.Market()),
                    quote.Time);
            });

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal([sellOrderId, buyOrderId], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([101m, 101m], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.FillPrice.Value).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task IocCrossedLimitCancelsOnlyUnfilledRemainder_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_650);
        var config = MarginConfig();
        var sellOrderId = new OrderId(960);
        var buyOrderId = new OrderId(961);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 99m, ask: 102m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Sell, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(8), Instrument, Side.Buy, new Qty(2m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = buyOrderId,
                        TimeInForce = TimeInForce.IOC
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, sellOrderId, Side.Sell, new Qty(1m), Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(new StrategyId(8), 0, AssetId, Instrument, Instrument.Venue, buyOrderId, Side.Buy, new Qty(2m), Execution.Limit().At(new Price(101m, Currency.USD)).ImmediateOrCancel()),
                    quote.Time);
            });

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray(),
            simulationEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task FokCrossedLimitCancelsWithoutPartialFillWhenRestingLiquidityInsufficient_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_675);
        var config = MarginConfig();
        var sellOrderId = new OrderId(962);
        var buyOrderId = new OrderId(963);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 99m, ask: 102m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Sell, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(new StrategyId(8), Instrument, Side.Buy, new Qty(2m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = buyOrderId,
                        TimeInForce = TimeInForce.FOK
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, sellOrderId, Side.Sell, new Qty(1m), Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(new StrategyId(8), 0, AssetId, Instrument, Instrument.Venue, buyOrderId, Side.Buy, new Qty(2m), FillOrKillLimit(new Price(101m, Currency.USD))),
                    quote.Time);
            });

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray(),
            simulationEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketOrderUsesExternalDepthAfterRestingBook_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_800);
        var config = MarginConfig();
        var sellOrderId = new OrderId(956);
        var buyOrderId = new OrderId(957);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 99m, ask: 102m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Sell, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(8), Instrument, Side.Buy, new Qty(2m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, sellOrderId, Side.Sell, new Qty(1m), Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(new StrategyId(8), 0, AssetId, Instrument, Instrument.Venue, buyOrderId, Side.Buy, new Qty(2m), Execution.Market()),
                    quote.Time);
            });

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal([sellOrderId, buyOrderId, buyOrderId], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([101m, 101m, 102m], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.FillPrice.Value).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketOrderDoesNotSelfMatchSameStrategyRestingOrder_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_900);
        var config = MarginConfig();
        var sellOrderId = new OrderId(958);
        var buyOrderId = new OrderId(959);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 99m, ask: 102m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Sell, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, sellOrderId, Side.Sell, new Qty(1m), Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, buyOrderId, Side.Buy, new Qty(1m), Execution.Market()),
                    quote.Time);
            });

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(buyOrderId, simulationFill.OrderId);
        Assert.Equal(102m, simulationFill.FillPrice.Value);
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task FokMarketOrderCancelsWithoutPartialFillWhenReplayLiquidityInsufficient_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(6_000);
        var config = MarginConfig();
        var sellOrderId = new OrderId(964);
        var buyOrderId = new OrderId(965);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 99m, ask: 102m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Sell, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(8), Instrument, Side.Buy, new Qty(2m)) with
                    {
                        OrderId = buyOrderId,
                        TimeInForce = TimeInForce.FOK
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            subscriptions: []);

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, sellOrderId, Side.Sell, new Qty(1m), Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(new StrategyId(8), 0, AssetId, Instrument, Instrument.Venue, buyOrderId, Side.Buy, new Qty(2m), new ExecutionSpec(OrderType.Market, timeInForce: TimeInForce.FOK)),
                    quote.Time);
            },
            invokeOnEventBeforeMarketEvent: true);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray(),
            simulationEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task FokMarketOrderUsesRestingBookAndExternalDepthWhenFullyAvailable_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(6_100);
        var config = MarginConfig();
        var sellOrderId = new OrderId(966);
        var buyOrderId = new OrderId(967);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 99m, ask: 102m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Sell, new Qty(1m), new Price(101m, Currency.USD)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(8), Instrument, Side.Buy, new Qty(2m)) with
                    {
                        OrderId = buyOrderId,
                        TimeInForce = TimeInForce.FOK
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, sellOrderId, Side.Sell, new Qty(1m), Execution.Limit().At(new Price(101m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(new StrategyId(8), 0, AssetId, Instrument, Instrument.Venue, buyOrderId, Side.Buy, new Qty(2m), new ExecutionSpec(OrderType.Market, timeInForce: TimeInForce.FOK)),
                    quote.Time);
            });

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal([sellOrderId, buyOrderId, buyOrderId], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([101m, 101m, 102m], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.FillPrice.Value).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketOrderUsesRestingBookThenFiniteExternalDepth_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(6_200);
        var config = MarginConfig();
        var sellOrderId = new OrderId(968);
        var buyOrderId = new OrderId(969);
        var book = CreateBookSnapshot(
            t0,
            bids: [new Level(new Price(99.99m, Currency.USD), new Qty(100m))],
            asks:
            [
                new Level(new Price(100.01m, Currency.USD), new Qty(40m)),
                new Level(new Price(100.02m, Currency.USD), new Qty(80m))
            ]);

        var replayEvents = await RunReplayConnectorAsync(
            [book],
            config,
            (connector, evt) =>
            {
                if (evt is not BookSnapshotReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Sell, new Qty(10m), new Price(100.005m, Currency.USD)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(8), Instrument, Side.Buy, new Qty(70m)) with
                    {
                        OrderId = buyOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationEvents = RunSimulatedVenue(
            [book],
            config,
            (venue, evt) =>
            {
                if (evt is not BookSnapshotReceived snapshot)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, sellOrderId, Side.Sell, new Qty(10m), Execution.Limit().At(new Price(100.005m, Currency.USD))),
                    snapshot.Time);
                venue.Submit(
                    new SimulationOrderCommand(new StrategyId(8), 0, AssetId, Instrument, Instrument.Venue, buyOrderId, Side.Buy, new Qty(70m), Execution.Market()),
                    snapshot.Time);
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal([sellOrderId, buyOrderId, buyOrderId, buyOrderId], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.OrderId).ToArray());
        Assert.Equal([100.005m, 100.005m, 100.01m, 100.02m], simulationEvents.OfType<OrderFilled>().Select(static fill => fill.FillPrice.Value).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task FokMarketOrderUsesRestingBookAndFiniteExternalDepthOnlyWhenCombinedLiquidityIsEnough_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(6_300);
        var config = MarginConfig();
        var sellOrderId = new OrderId(970);
        var buyOrderId = new OrderId(971);
        var book = CreateBookSnapshot(
            t0,
            bids: [new Level(new Price(99.99m, Currency.USD), new Qty(100m))],
            asks: [new Level(new Price(100.01m, Currency.USD), new Qty(40m))]);

        var replayEvents = await RunReplayConnectorAsync(
            [book],
            config,
            (connector, evt) =>
            {
                if (evt is not BookSnapshotReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(StrategyId, Instrument, Side.Sell, new Qty(10m), new Price(100.005m, Currency.USD)) with
                    {
                        OrderId = sellOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(8), Instrument, Side.Buy, new Qty(60m)) with
                    {
                        OrderId = buyOrderId,
                        TimeInForce = TimeInForce.FOK
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationEvents = RunSimulatedVenue(
            [book],
            config,
            (venue, evt) =>
            {
                if (evt is not BookSnapshotReceived snapshot)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, sellOrderId, Side.Sell, new Qty(10m), Execution.Limit().At(new Price(100.005m, Currency.USD))),
                    snapshot.Time);
                venue.Submit(
                    new SimulationOrderCommand(new StrategyId(8), 0, AssetId, Instrument, Instrument.Venue, buyOrderId, Side.Buy, new Qty(60m), new ExecutionSpec(OrderType.Market, timeInForce: TimeInForce.FOK)),
                    snapshot.Time);
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray(),
            simulationEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task OcoSiblingCancelsWhenOneOrderFills_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(4_500);
        var config = MarginConfig();
        var orderListId = OrderListId.New();
        var takeProfitId = new OrderId(901);
        var stopLossId = new OrderId(902);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 106m, low: 94m, close: 100m)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
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
                            StrategyId,
                            Instrument,
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
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            takeProfitId,
                            Side.Sell,
                            new Qty(1m),
                            Execution.Limit().At(new Price(105m, Currency.USD)),
                            OrderListId: orderListId,
                            ContingencyType: ContingencyType.OCO),
                        quote.Time);
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            stopLossId,
                            Side.Sell,
                            new Qty(1m),
                            Execution.StopMarket(new Price(95m, Currency.USD)),
                            OrderListId: orderListId,
                            ContingencyType: ContingencyType.OCO),
                        quote.Time);
                }
            });

        Assert.Equal(
            replayEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray(),
            simulationEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray(),
            simulationEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task OtoChildActivatesOnlyAfterParentFills_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(4_600);
        var config = SimulationConfig.Instant();
        var orderListId = OrderListId.New();
        var entryId = new OrderId(911);
        var stopId = new OrderId(912);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 102m, low: 98m, close: 101m),
                CreateBar(t0 + Duration.FromSeconds(2), open: 101m, high: 101m, low: 98m, close: 99m)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.StopMarket(
                            StrategyId,
                            Instrument,
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
                            StrategyId,
                            Instrument,
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
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            entryId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.StopMarket(new Price(101m, Currency.USD)),
                            OrderListId: orderListId,
                            ContingencyType: ContingencyType.OTO),
                        quote.Time);
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            stopId,
                            Side.Sell,
                            new Qty(1m),
                            Execution.StopMarket(new Price(99m, Currency.USD)),
                            OrderListId: orderListId,
                            ContingencyType: ContingencyType.OTO),
                        quote.Time);
                }
            });

        Assert.Equal(
            replayEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray(),
            simulationEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
    }

    [Fact]
    public async Task CancelStagedOtoChildBeforeParentFills_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(4_700);
        var config = SimulationConfig.Instant();
        var orderListId = OrderListId.New();
        var entryId = new OrderId(913);
        var stopId = new OrderId(914);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 102m, low: 98m, close: 101m),
                CreateBar(t0 + Duration.FromSeconds(2), open: 101m, high: 101m, low: 98m, close: 99m)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.StopMarket(
                            StrategyId,
                            Instrument,
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
                            StrategyId,
                            Instrument,
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
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            entryId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.StopMarket(new Price(101m, Currency.USD)),
                            OrderListId: orderListId,
                            ContingencyType: ContingencyType.OTO),
                        quote.Time);
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            stopId,
                            Side.Sell,
                            new Qty(1m),
                            Execution.StopMarket(new Price(99m, Currency.USD)),
                            OrderListId: orderListId,
                            ContingencyType: ContingencyType.OTO),
                        quote.Time);
                    venue.Cancel(
                        new SimulationCancelCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            stopId),
                        quote.Time);
                }
            });

        Assert.Equal(
            replayEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray(),
            simulationEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray(),
            simulationEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.DoesNotContain(replayEvents, evt => evt is OrderAccepted accepted && accepted.OrderId == stopId);
        Assert.DoesNotContain(simulationEvents, evt => evt is OrderAccepted accepted && accepted.OrderId == stopId);
        Assert.DoesNotContain(replayEvents, evt => evt is OrderFilled filled && filled.OrderId == stopId);
        Assert.DoesNotContain(simulationEvents, evt => evt is OrderFilled filled && filled.OrderId == stopId);
    }

    [Fact]
    public async Task ModifyStagedOtoChildBeforeParentFills_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(4_800);
        var config = SimulationConfig.Instant();
        var orderListId = OrderListId.New();
        var entryId = new OrderId(915);
        var childId = new OrderId(916);
        var modifiedQuantity = new Qty(0.5m);
        var modifiedLimit = new Price(99m, Currency.USD);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 102m, low: 98m, close: 101m),
                CreateBar(t0 + Duration.FromSeconds(2), open: 101m, high: 100m, low: 98m, close: 99m)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.StopMarket(
                            StrategyId,
                            Instrument,
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
                            StrategyId,
                            Instrument,
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
                        new ModifyOrder(childId, modifiedQuantity, modifiedLimit),
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            entryId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.StopMarket(new Price(101m, Currency.USD)),
                            OrderListId: orderListId,
                            ContingencyType: ContingencyType.OTO),
                        quote.Time);
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            childId,
                            Side.Sell,
                            new Qty(1m),
                            Execution.Limit().At(new Price(110m, Currency.USD)),
                            OrderListId: orderListId,
                            ContingencyType: ContingencyType.OTO),
                        quote.Time);
                    venue.Modify(
                        new SimulationModifyCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            childId,
                            modifiedQuantity,
                            modifiedLimit),
                        quote.Time);
                }
            });

        Assert.Equal(
            replayEvents.OfType<OrderModified>().Select(NormalizeModified).ToArray(),
            simulationEvents.OfType<OrderModified>().Select(NormalizeModified).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray(),
            simulationEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task OuoSiblingQuantityShrinksAfterPartialParentFill_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = MarginConfig() with
        {
            FillBehavior = FillBehavior.PartialFillOnTrade
        };
        var orderListId = OrderListId.New();
        var parentOrderId = new OrderId(921);
        var siblingOrderId = new OrderId(922);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 4m, Side.Sell)
            };

        var replayConnector = new ReplayConnector(ReadHistoryAsync(history), config);
        var replayPublisher = new TestEventPublisher();
        var replaySubmitted = false;
        replayPublisher.OnEmit = evt =>
        {
            if (replaySubmitted || evt is not TradeOccurred)
                return;

            replaySubmitted = true;
            replayConnector.SubmitOrderAsync(
                SubmitOrder.Limit(
                    StrategyId,
                    Instrument,
                    Side.Buy,
                    new Qty(10m),
                    new Price(100m, Currency.USD)) with
                {
                    OrderId = parentOrderId,
                    OrderListId = orderListId,
                    ContingencyType = ContingencyType.OUO
                },
                CancellationToken.None).GetAwaiter().GetResult();
            replayConnector.SubmitOrderAsync(
                SubmitOrder.StopMarket(
                    StrategyId,
                    Instrument,
                    Side.Sell,
                    new Qty(10m),
                    new Price(90m, Currency.USD)) with
                {
                    OrderId = siblingOrderId,
                    OrderListId = orderListId,
                    ContingencyType = ContingencyType.OUO
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await replayConnector.StartAsync(
            [new Subscription(Instrument, SubscriptionType.Quotes)],
            replayPublisher,
            CancellationToken.None);
        await replayConnector.CancelOrderAsync(new CancelOrder(siblingOrderId), CancellationToken.None);
        var replayEvents = replayPublisher.ToArray();

        var venue = new SimulatedVenueExchange(Instrument.Venue, config, Money.USD(100_000m));
        var simulationEvents = new List<Event>();
        var executionBuffer = new ExecutionEvent[16];
        var simulationBuffer = new FinanceEvent[16];
        var simulationSubmitted = false;
        for (var i = 0; i < history.Length; i++)
        {
            var evt = history[i];
            if (!simulationSubmitted && evt is TradeOccurred trade)
            {
                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        parentOrderId,
                        Side.Buy,
                        new Qty(10m),
                        Execution.Limit().At(new Price(100m, Currency.USD)),
                        OrderListId: orderListId,
                        ContingencyType: ContingencyType.OUO),
                    trade.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        siblingOrderId,
                        Side.Sell,
                        new Qty(10m),
                        Execution.StopMarket(new Price(90m, Currency.USD)),
                        OrderListId: orderListId,
                        ContingencyType: ContingencyType.OUO),
                    trade.Time);
                venue.DrainDueWork(trade.Time);
                DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);
            }

            venue.OnMarketEvent(evt);
            venue.DrainDueWork(GetEventTime(evt));
            DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);
        }

        venue.Cancel(
            new SimulationCancelCommand(
                StrategyId,
                VariantId: 0,
                AssetId,
                Instrument,
                Instrument.Venue,
                siblingOrderId),
            GetEventTime(history[^1]));
        venue.DrainDueWork(GetEventTime(history[^1]));
        DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Equal(4m, replayCancelled.RemainingQty.Value);
        Assert.Equal(4m, simulationCancelled.RemainingQty.Value);
    }

    [Fact]
    public async Task CancelAfterPartialReplayFillReportsRemainingQuantity_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(4_900);
        var config = SimulationConfig.Instant() with { FillBehavior = FillBehavior.PartialFillOnTrade };
        var replayOrderId = new OrderId(917);
        var simulationOrderId = new OrderId(917);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 4m, Side.Sell)
            };

        var replayConnector = new ReplayConnector(ReadHistoryAsync(history), config);
        var replayPublisher = new TestEventPublisher();
        var replaySubmitted = false;
        replayPublisher.OnEmit = evt =>
        {
            if (replaySubmitted || evt is not TradeOccurred)
                return;

            replaySubmitted = true;
            replayConnector.SubmitOrderAsync(
                SubmitOrder.Limit(
                    StrategyId,
                    Instrument,
                    Side.Buy,
                    new Qty(10m),
                    new Price(100m, Currency.USD)) with
                {
                    OrderId = replayOrderId
                },
                CancellationToken.None).GetAwaiter().GetResult();
        };

        await replayConnector.StartAsync(
            [new Subscription(Instrument, SubscriptionType.Quotes)],
            replayPublisher,
            CancellationToken.None);
        await replayConnector.CancelOrderAsync(new CancelOrder(replayOrderId), CancellationToken.None);
        var replayEvents = replayPublisher.ToArray();

        var venue = new SimulatedVenueExchange(Instrument.Venue, config, Money.USD(100_000m));
        var simulationEvents = new List<Event>();
        var executionBuffer = new ExecutionEvent[16];
        var simulationBuffer = new FinanceEvent[16];
        var simulationSubmitted = false;
        for (var i = 0; i < history.Length; i++)
        {
            var evt = history[i];
            if (!simulationSubmitted && evt is TradeOccurred trade)
            {
                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationOrderId,
                        Side.Buy,
                        new Qty(10m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    trade.Time);
            }

            venue.OnMarketEvent(evt);
            venue.DrainDueWork(GetEventTime(evt));
            DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);
        }

        venue.Cancel(
            new SimulationCancelCommand(
                StrategyId,
                VariantId: 0,
                AssetId,
                Instrument,
                Instrument.Venue,
                simulationOrderId),
            GetEventTime(history[^1]));
        venue.DrainDueWork(GetEventTime(history[^1]));
        DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray(),
            simulationEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task DefaultFillBehaviorFillsFullRemainingQuantityFromTrade_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(5_000);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(918);
        var simulationOrderId = new OrderId(918);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 4m, Side.Sell)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is TradeOccurred)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(100m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationVenue = new SimulatedVenueExchange(Instrument.Venue, config, Money.USD(100_000m));
        var simulationEvents = new List<Event>();
        var executionBuffer = new ExecutionEvent[16];
        var simulationBuffer = new FinanceEvent[16];
        var simulationSubmitted = false;
        for (var i = 0; i < history.Length; i++)
        {
            var evt = history[i];
            if (!simulationSubmitted && evt is TradeOccurred trade)
            {
                simulationSubmitted = true;
                simulationVenue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        simulationOrderId,
                        Side.Buy,
                        new Qty(10m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    trade.Time);
            }

            simulationVenue.OnMarketEvent(evt);
            simulationVenue.DrainDueWork(GetEventTime(evt));
            DrainVenueEvents(simulationVenue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);
        }

        simulationVenue.DrainDueWork(GetEventTime(history[^1]));
        simulationVenue.CompleteReplay(GetEventTime(history[^1]));
        DrainVenueEvents(simulationVenue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);

        Assert.Equal(
            replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray(),
            simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task TwapExpandsIntoScheduledMarketSlices_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(931);
        var simulationOrderId = new OrderId(931);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromMinutes(1), bid: 101m, ask: 101.05m),
                CreateQuote(t0 + Duration.FromMinutes(2), bid: 102m, ask: 102.05m),
                CreateQuote(t0 + Duration.FromMinutes(3), bid: 103m, ask: 103.05m)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Twap(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(9m),
                            TimeSpan.FromMinutes(3),
                            TimeSpan.FromMinutes(1)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(9m),
                            Execution.Twap()
                                .Over(Duration.FromMinutes(3))
                                .Every(Duration.FromMinutes(1))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal([3m, 3m, 3m], simulationFills.Select(static fill => fill.Quantity.Value).ToArray());
        Assert.Equal([101.05m, 102.05m, 103.05m], simulationFills.Select(static fill => fill.PriceValue).ToArray());
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task TwapForceCompletesAtHorizonWhenIntervalIsLongerThanHorizon_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(939);
        var simulationOrderId = new OrderId(939);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromMinutes(3), bid: 103m, ask: 103.05m)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Twap(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(9m),
                            TimeSpan.FromMinutes(3),
                            TimeSpan.FromMinutes(10)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(9m),
                            Execution.Twap()
                                .Over(Duration.FromMinutes(3))
                                .Every(Duration.FromMinutes(10))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));
        Assert.Equal(9m, simulationFill.FilledQty.Value);
        Assert.Equal(103.05m, simulationFill.FillPrice.Value);
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task VwapExpandsByReplayEventParticipation_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(932);
        var simulationOrderId = new OrderId(932);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromMinutes(1), price: 100m, size: 4m, Side.Sell),
                CreateTrade(t0 + Duration.FromMinutes(2), price: 100m, size: 6m, Side.Sell),
                CreateTrade(t0 + Duration.FromMinutes(3), price: 100m, size: 20m, Side.Sell)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Vwap(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            TimeSpan.FromMinutes(3),
                            participationRate: 0.5m) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Vwap()
                                .Over(Duration.FromMinutes(3))
                                .MaxParticipation(0.5m)),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal([2m, 3m, 5m], simulationFills.Select(static fill => fill.Quantity.Value).ToArray());
        Assert.All(simulationFills, static fill => Assert.Equal(100.05m, fill.PriceValue));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task VwapForceCompletesAtHorizon_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(938);
        var simulationOrderId = new OrderId(938);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromMinutes(3), bid: 103m, ask: 103.05m)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Vwap(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            TimeSpan.FromMinutes(3),
                            participationRate: 0.1m) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Vwap()
                                .Over(Duration.FromMinutes(3))
                                .MaxParticipation(0.1m)),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));
        Assert.Equal(10m, simulationFill.FilledQty.Value);
        Assert.Equal(103.05m, simulationFill.FillPrice.Value);
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task PovExpandsByReplayEventParticipationUntilFilled_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(933);
        var simulationOrderId = new OrderId(933);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromMinutes(1), price: 100m, size: 10m, Side.Sell),
                CreateTrade(t0 + Duration.FromMinutes(2), price: 100m, size: 6m, Side.Sell),
                CreateTrade(t0 + Duration.FromMinutes(3), price: 100m, size: 20m, Side.Sell)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Pov(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(8m),
                            participationRate: 0.25m) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(8m),
                            Execution.Pov(0.25m)),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal([2.5m, 1.5m, 4m], simulationFills.Select(static fill => fill.Quantity.Value).ToArray());
        Assert.All(simulationFills, static fill => Assert.Equal(100.05m, fill.PriceValue));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task PovCancelsUnfilledRemainderAtReplayEnd_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(934);
        var simulationOrderId = new OrderId(934);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateTrade(t0 + Duration.FromMinutes(1), price: 100m, size: 4m, Side.Sell)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Pov(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            participationRate: 0.5m) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Pov(0.5m)),
                        quote.Time);
                }
            });

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Equal(8m, replayCancelled.RemainingQty.Value);
        Assert.Equal(8m, simulationCancelled.RemainingQty.Value);
        Assert.Contains("Replay ended", replayCancelled.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Replay ended", simulationCancelled.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task TwapRejectsWithoutPositiveHorizon_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(935);
        var simulationOrderId = new OrderId(935);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 100m, ask: 100.05m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Twap(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(9m),
                            TimeSpan.Zero,
                            TimeSpan.FromMinutes(1)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(9m),
                            Execution.Twap().Every(Duration.FromMinutes(1))),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "TWAP", "horizon_secs");
    }

    [Fact]
    public async Task VwapRejectsWithoutPositiveHorizon_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(936);
        var simulationOrderId = new OrderId(936);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 100m, ask: 100.05m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Vwap(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(9m),
                            TimeSpan.Zero,
                            participationRate: 0.25m) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(9m),
                            Execution.Vwap().MaxParticipation(0.25m)),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "VWAP", "horizon_secs");
    }

    [Fact]
    public async Task PovRejectsWithoutPositiveParticipationRate_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(937);
        var simulationOrderId = new OrderId(937);
        var history = new FinanceEvent[] { CreateQuote(t0, bid: 100m, ask: 100.05m) };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Pov(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(9m),
                            participationRate: 0m) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(9m),
                            Execution.Pov(0m)),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "POV", "participation_rate");
    }

    [Fact]
    public async Task ExecutionResponseLatencyDelaysVisibleFill_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.Zero, Duration.FromMinutes(1))
        };
        var replayOrderId = new OrderId(941);
        var simulationOrderId = new OrderId(941);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromSeconds(30), bid: 101m, ask: 101.05m),
                CreateQuote(t0 + Duration.FromMinutes(2), bid: 102m, ask: 102.05m)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Market()),
                        quote.Time);
                }
            });

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));
        Assert.Equal(t0 + Duration.FromMinutes(1), replayFill.Time);
        Assert.Equal(t0 + Duration.FromMinutes(1), simulationFill.Time);
    }

    [Fact]
    public async Task PendingExecutionResponseFlushesAtReplayEnd_HasReplayConnectorParity()
    {
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.Zero, Duration.FromMinutes(10))
        };
        var replayOrderId = new OrderId(942);
        var simulationOrderId = new OrderId(942);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m)
            };

        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == t0)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Market()),
                        quote.Time);
                }
            });

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));
        Assert.Equal(t0 + Duration.FromMinutes(10), replayFill.Time);
        Assert.Equal(t0 + Duration.FromMinutes(10), simulationFill.Time);
    }

    [Fact]
    public async Task TieredFeesUseRollingReplayVolume_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
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
        var firstOrderId = new OrderId(951);
        var secondOrderId = new OrderId(952);
        var history =
            new FinanceEvent[]
            {
                CreateBookSnapshot(
                    t0,
                    bids: [new Level(new Price(99.99m, Currency.USD), new Qty(50m))],
                    asks: [new Level(new Price(100.01m, Currency.USD), new Qty(40m))]),
                CreateBookSnapshot(
                    t0 + Duration.FromMinutes(1),
                    bids: [new Level(new Price(99.99m, Currency.USD), new Qty(50m))],
                    asks: [new Level(new Price(100.01m, Currency.USD), new Qty(40m))])
            };

        var replaySubmissions = 0;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is BookSnapshotReceived && replaySubmissions < 2)
                {
                    replaySubmissions++;
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(10m)) with
                        {
                            OrderId = replaySubmissions == 1 ? firstOrderId : secondOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            initialCash: Money.USD(10_000m),
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationSubmissions = 0;
        var simulationEvents = RunSimulatedVenue(
            history,
            config,
            (venue, evt) =>
            {
                if (evt is BookSnapshotReceived snapshot && simulationSubmissions < 2)
                {
                    simulationSubmissions++;
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationSubmissions == 1 ? firstOrderId : secondOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Market()),
                        snapshot.Time);
                }
            },
            initialCash: Money.USD(10_000m));

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFillCommission).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFillCommission).ToArray();
        Assert.Equal(2, replayFills.Length);
        Assert.Equal(2, simulationFills.Length);
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal(Money.USD(1.0001m), simulationFills[0].Commission);
        Assert.Equal(Money.USD(0.10001m), simulationFills[1].Commission);
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketableImmediateOrCancelLimit_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(143);
        var simulationOrderId = new OrderId(143);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(100.05m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            TimeInForce = TimeInForce.IOC
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Limit().At(new Price(100.05m, Currency.USD)).ImmediateOrCancel()),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task NonMarketableImmediateOrCancelLimitCancelsWithoutResting_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(144);
        var simulationOrderId = new OrderId(144);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 101m, high: 102m, low: 98m, close: 99m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.StopLimit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(100.50m, Currency.USD),
                            new Price(100.50m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            TimeInForce = TimeInForce.IOC
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 101m, high: 102m, low: 98m, close: 99m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Limit().At(new Price(99m, Currency.USD)).ImmediateOrCancel()),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Contains("IOC", replayCancelled.Reason);
        Assert.Contains("IOC", simulationCancelled.Reason);

        var replayStates = replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray();
        var simulationStates = simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray();
        Assert.Equal(replayStates, simulationStates);
        Assert.Equal(
            [OrderStatus.Open, OrderStatus.Cancelled],
            simulationStates.Select(static state => state.Status).ToArray());
        Assert.Equal(new Qty(10m), simulationStates[^1].RemainingQuantity);
        Assert.Contains("IOC", simulationStates[^1].Reason);

        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task MarketableFillOrKillLimit_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(145);
        var simulationOrderId = new OrderId(145);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(100.05m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            TimeInForce = TimeInForce.FOK
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            FillOrKillLimit(new Price(100.05m, Currency.USD))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task NonMarketableFillOrKillLimitCancelsWithoutResting_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(146);
        var simulationOrderId = new OrderId(146);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 101m, high: 102m, low: 98m, close: 99m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.StopLimit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(100.50m, Currency.USD),
                            new Price(100.50m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            TimeInForce = TimeInForce.FOK
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateBar(t0 + Duration.FromSeconds(1), open: 101m, high: 102m, low: 98m, close: 99m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            FillOrKillLimit(new Price(99m, Currency.USD))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Contains("FOK", replayCancelled.Reason);
        Assert.Contains("FOK", simulationCancelled.Reason);

        var replayStates = replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray();
        var simulationStates = simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray();
        Assert.Equal(replayStates, simulationStates);
        Assert.Equal(
            [OrderStatus.Open, OrderStatus.Cancelled],
            simulationStates.Select(static state => state.Status).ToArray());
        Assert.Equal(new Qty(10m), simulationStates[^1].RemainingQuantity);
        Assert.Contains("FOK", simulationStates[^1].Reason);

        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task GoodTilDateStopLimitExpiresBeforeExpiredEventCanFillIt_HasReplayConnectorParity()
    {
        var quoteTime = Instant.FromUnixSeconds(1_000);
        var expiry = quoteTime + Duration.FromMinutes(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(147);
        var simulationOrderId = new OrderId(147);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(quoteTime, bid: 100m, ask: 101m),
                CreateBar(expiry, open: 101m, high: 102m, low: 98m, close: 99m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.StopLimit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(100.50m, Currency.USD),
                            new Price(100.50m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            TimeInForce = TimeInForce.GTD,
                            GoodTilDate = expiry
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(quoteTime, bid: 100m, ask: 101m),
                CreateBar(expiry, open: 101m, high: 102m, low: 98m, close: 99m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.StopLimit(
                                new Price(100.50m, Currency.USD),
                                new Price(100.50m, Currency.USD)).GoodTil(expiry)),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayExpired = Assert.Single(replayEvents.OfType<OrderExpired>());
        var simulationExpired = Assert.Single(simulationEvents.OfType<OrderExpired>());
        Assert.Equal(NormalizeExpired(replayExpired), NormalizeExpired(simulationExpired));

        var replayStates = replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray();
        var simulationStates = simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray();
        Assert.Equal(replayStates, simulationStates);
        Assert.Equal(
            [OrderStatus.Open, OrderStatus.Expired],
            simulationStates.Select(static state => state.Status).ToArray());

        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task GoodTilDateStopLimitFillsBeforeExpiry_HasReplayConnectorParity()
    {
        var quoteTime = Instant.FromUnixSeconds(2_000);
        var barTime = quoteTime + Duration.FromSeconds(30);
        var expiry = quoteTime + Duration.FromMinutes(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(148);
        var simulationOrderId = new OrderId(148);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(quoteTime, bid: 100m, ask: 101m),
                CreateBar(barTime, open: 101m, high: 102m, low: 98m, close: 99m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.StopLimit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(100.50m, Currency.USD),
                            new Price(100.50m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            TimeInForce = TimeInForce.GTD,
                            GoodTilDate = expiry
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(quoteTime, bid: 100m, ask: 101m),
                CreateBar(barTime, open: 101m, high: 102m, low: 98m, close: 99m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.StopLimit(
                                new Price(100.50m, Currency.USD),
                                new Price(100.50m, Currency.USD)).GoodTil(expiry)),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderExpired>());
        Assert.Empty(simulationEvents.OfType<OrderExpired>());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task DayOrderExpiresBeforeNextSessionEventCanFillIt_HasReplayConnectorParity()
    {
        var quoteTime = Instant.FromDateTimeOffset(new DateTimeOffset(2026, 5, 21, 20, 59, 0, TimeSpan.Zero));
        var barTime = Instant.FromDateTimeOffset(new DateTimeOffset(2026, 5, 22, 13, 30, 0, TimeSpan.Zero));
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(149);
        var simulationOrderId = new OrderId(149);

        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(quoteTime, bid: 100m, ask: 101m),
                CreateBar(barTime, open: 101m, high: 102m, low: 98m, close: 99m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.StopLimit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(100.50m, Currency.USD),
                            new Price(100.50m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            TimeInForce = TimeInForce.Day
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(quoteTime, bid: 100m, ask: 101m),
                CreateBar(barTime, open: 101m, high: 102m, low: 98m, close: 99m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.StopLimit(
                                new Price(100.50m, Currency.USD),
                                new Price(100.50m, Currency.USD))),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayExpired = Assert.Single(replayEvents.OfType<OrderExpired>());
        var simulationExpired = Assert.Single(simulationEvents.OfType<OrderExpired>());
        Assert.Equal(NormalizeExpired(replayExpired), NormalizeExpired(simulationExpired));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task GoodTilDateOrderWithoutExpiryRejectsBeforeAcceptance_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(3_000);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(150);
        var simulationOrderId = new OrderId(150);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            TimeInForce = TimeInForce.GTD
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            GoodTilDateWithoutExpiryLimit(new Price(99m, Currency.USD))),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "GoodTilDate");
        Assert.Empty(replayEvents.OfType<OrderExpired>());
        Assert.Empty(simulationEvents.OfType<OrderExpired>());
    }

    [Fact]
    public async Task PostOnlyMarketOrderRejectsBeforeAcceptance_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(3_100);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(151);
        var simulationOrderId = new OrderId(151);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m)) with
                        {
                            OrderId = replayOrderId,
                            PostOnly = true
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Market().WithPostOnly()),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "Post");
    }

    [Fact]
    public async Task PostOnlyLimitThatWouldTakeLiquidityRejectsBeforeAcceptance_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(3_200);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(152);
        var simulationOrderId = new OrderId(152);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(101m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            PostOnly = true
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Limit().At(new Price(101m, Currency.USD)).WithPostOnly()),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "Post");
    }

    [Fact]
    public async Task PostOnlyLimitThatWouldRestIsAccepted_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(3_300);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(153);
        var simulationOrderId = new OrderId(153);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            PostOnly = true
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Limit().At(new Price(99m, Currency.USD)).WithPostOnly()),
                        quote.Time);
                }
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
    }

    [Fact]
    public async Task VenueOrderPolicyDisallowsPostOnly_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(3_400);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(154);
        var simulationOrderId = new OrderId(154);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(10m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            PostOnly = true
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueOrderPolicies = new Dictionary<Venue, ReplayVenueOrderPolicy>
                {
                    [Instrument.Venue] = ReplayVenueOrderPolicy.Default with
                    {
                        AllowPostOnly = false
                    }
                };
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(10m),
                            Execution.Limit().At(new Price(99m, Currency.USD)).WithPostOnly()),
                        quote.Time);
                }
            },
            orderPolicy: SimulationOrderPolicy.Default with
            {
                AllowPostOnly = false
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "does not allow post-only");
    }

    [Fact]
    public async Task VenueOrderPolicyDisallowsOrderType_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(3_500);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(155);
        var simulationOrderId = new OrderId(155);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueOrderPolicies = new Dictionary<Venue, ReplayVenueOrderPolicy>
                {
                    [Instrument.Venue] = ReplayVenueOrderPolicy.Default with
                    {
                        AllowedOrderTypes = new HashSet<OrderType> { OrderType.Market }
                    }
                };
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Limit().At(new Price(99m, Currency.USD))),
                        quote.Time);
                }
            },
            orderPolicy: SimulationOrderPolicy.Default with
            {
                AllowedOrderTypes = new HashSet<OrderType> { OrderType.Market }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "does not allow", "Limit");
    }

    [Fact]
    public async Task VenueOrderPolicyDisallowsTimeInForce_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(3_600);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(156);
        var simulationOrderId = new OrderId(156);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = replayOrderId,
                            TimeInForce = TimeInForce.GTC
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueOrderPolicies = new Dictionary<Venue, ReplayVenueOrderPolicy>
                {
                    [Instrument.Venue] = ReplayVenueOrderPolicy.Default with
                    {
                        AllowedTimeInForce = new HashSet<TimeInForce> { TimeInForce.Day }
                    }
                };
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Limit().At(new Price(99m, Currency.USD)).GoodTilCancelled()),
                        quote.Time);
                }
            },
            orderPolicy: SimulationOrderPolicy.Default with
            {
                AllowedTimeInForce = new HashSet<TimeInForce> { TimeInForce.Day }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "does not allow", "GTC");
    }

    [Fact]
    public async Task VenueOrderPolicyRequiresMinimumQuantity_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(3_700);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(157);
        var simulationOrderId = new OrderId(157);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueOrderPolicies = new Dictionary<Venue, ReplayVenueOrderPolicy>
                {
                    [Instrument.Venue] = ReplayVenueOrderPolicy.Default with
                    {
                        MinOrderQuantity = new Qty(2m)
                    }
                };
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Limit().At(new Price(99m, Currency.USD))),
                        quote.Time);
                }
            },
            orderPolicy: SimulationOrderPolicy.Default with
            {
                MinOrderQuantity = new Qty(2m)
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "minimum order quantity");
    }

    [Fact]
    public async Task VenueOrderPolicyRequiresMinimumNotional_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(3_800);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(158);
        var simulationOrderId = new OrderId(158);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueOrderPolicies = new Dictionary<Venue, ReplayVenueOrderPolicy>
                {
                    [Instrument.Venue] = ReplayVenueOrderPolicy.Default with
                    {
                        MinOrderNotional = Money.USD(100m)
                    }
                };
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Limit().At(new Price(99m, Currency.USD))),
                        quote.Time);
                }
            },
            orderPolicy: SimulationOrderPolicy.Default with
            {
                MinOrderNotional = Money.USD(100m)
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "minimum order notional");
    }

    [Fact]
    public async Task MarketOrderMinimumNotionalUsesCurrentMarketPrice_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(3_850);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(10_158);
        var simulationOrderId = new OrderId(10_158);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(5m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueOrderPolicies = new Dictionary<Venue, ReplayVenueOrderPolicy>
                {
                    [Instrument.Venue] = ReplayVenueOrderPolicy.Default with
                    {
                        MinOrderNotional = Money.USD(1_000m)
                    }
                };
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(now, bid: 100m, ask: 101m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(5m),
                            Execution.Market()),
                        quote.Time);
                }
            },
            orderPolicy: SimulationOrderPolicy.Default with
            {
                MinOrderNotional = Money.USD(1_000m)
            });

        AssertRejectedOnly(replayEvents, simulationEvents, "minimum order notional");
    }

    [Fact]
    public async Task DisabledLiquidityConsumptionPolicy_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            FillBehavior = FillBehavior.PartialFillOnTrade
        };
        var firstOrderId = new OrderId(141);
        var secondOrderId = new OrderId(142);

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 4m, Side.Sell)
            ],
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not QuoteReceived)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        StrategyId,
                        Instrument,
                        Side.Buy,
                        new Qty(4m),
                        new Price(100m, Currency.USD)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        new StrategyId(8),
                        Instrument,
                        Side.Buy,
                        new Qty(4m),
                        new Price(100m, Currency.USD)) with
                    {
                        OrderId = secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            connector =>
            {
                connector.VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
                {
                    [Instrument.Venue] = ReplayVenueSimulationPolicy.Default with
                    {
                        LiquidityConsumption = false
                    }
                };
            });

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 4m, Side.Sell)
            ],
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not QuoteReceived quote)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        firstOrderId,
                        Side.Buy,
                        new Qty(4m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        new StrategyId(8),
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        secondOrderId,
                        Side.Buy,
                        new Qty(4m),
                        Execution.Limit().At(new Price(100m, Currency.USD))),
                    quote.Time);
            },
            SimulationVenuePolicy.Default with
            {
                LiquidityConsumption = false
            });

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal(2, replayFills.Length);
        Assert.All(replayFills, fill =>
        {
            Assert.Equal(new Qty(4m), fill.Quantity);
            Assert.Equal(100m, fill.PriceValue);
        });
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task DisabledLiquidityConsumptionAllowsRepeatedMarketBookFills_HasReplayConnectorParity()
    {
        var now = Instant.FromUnixSeconds(3_900);
        var config = SimulationConfig.Instant();
        var firstOrderId = new OrderId(10_141);
        var secondOrderId = new OrderId(10_142);
        var book = CreateBookSnapshot(
            now,
            bids: [new Level(new Price(99.99m, Currency.USD), new Qty(40m))],
            asks: [new Level(new Price(100.01m, Currency.USD), new Qty(40m))]);

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            [book],
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not BookSnapshotReceived)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(40m)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(8), Instrument, Side.Buy, new Qty(40m)) with
                    {
                        OrderId = secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            connector =>
            {
                connector.VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
                {
                    [Instrument.Venue] = ReplayVenueSimulationPolicy.Default with
                    {
                        LiquidityConsumption = false
                    }
                };
            },
            subscriptions: [new Subscription(Instrument, SubscriptionType.Depth, SubscriptionDepth.L2_20)]);

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            [book],
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not BookSnapshotReceived snapshot)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        firstOrderId,
                        Side.Buy,
                        new Qty(40m),
                        Execution.Market()),
                    snapshot.Time);
                venue.Submit(
                    new SimulationOrderCommand(
                        new StrategyId(8),
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        secondOrderId,
                        Side.Buy,
                        new Qty(40m),
                        Execution.Market()),
                    snapshot.Time);
            },
            SimulationVenuePolicy.Default with
            {
                LiquidityConsumption = false
            },
            matchingFidelity: MatchingFidelity.MarketByOrder);

        var replayFills = replayEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        var simulationFills = simulationEvents.OfType<OrderFilled>().Select(NormalizeFillValue).ToArray();
        Assert.Equal(replayFills, simulationFills);
        Assert.Equal(2, replayFills.Length);
        Assert.All(replayFills, fill =>
        {
            Assert.Equal(new Qty(40m), fill.Quantity);
            Assert.Equal(100.01m, fill.PriceValue);
        });
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task DisabledTradeExecutionPolicyLeavesRestingOrderOpen_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            FillBehavior = FillBehavior.PartialFillOnTrade
        };
        var replayOrderId = new OrderId(143);
        var simulationOrderId = new OrderId(143);
        var quote = CreateQuote(t0, bid: 99m, ask: 101m);
        var trade = CreateTrade(t0 + Duration.FromSeconds(1), price: 100m, size: 1m, Side.Sell);

        var replayEvents = await RunReplayConnectorAsync(
            [quote, trade],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(100m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
                {
                    [Instrument.Venue] = ReplayVenueSimulationPolicy.Default with
                    {
                        TradeExecution = false
                    }
                };
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Trades)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [quote, trade],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quoteEvent)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Limit().At(new Price(100m, Currency.USD))),
                        quoteEvent.Time);
                }
            },
            SimulationVenuePolicy.Default with
            {
                TradeExecution = false
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
    }

    [Fact]
    public async Task DisabledBarExecutionPolicyLeavesRestingOrderOpen_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(144);
        var simulationOrderId = new OrderId(144);
        var quote = CreateQuote(t0, bid: 99m, ask: 101m);
        var bar = CreateBar(t0 + Duration.FromSeconds(1), open: 100m, high: 101m, low: 99m, close: 100m);

        var replayEvents = await RunReplayConnectorAsync(
            [quote, bar],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(100m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            connector =>
            {
                connector.VenueSimulationPolicies = new Dictionary<Venue, ReplayVenueSimulationPolicy>
                {
                    [Instrument.Venue] = ReplayVenueSimulationPolicy.Default with
                    {
                        BarExecution = false
                    }
                };
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(Instrument, SubscriptionType.Bars)
            ]);

        var simulationEvents = RunSimulatedVenue(
            [quote, bar],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quoteEvent)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Limit().At(new Price(100m, Currency.USD))),
                        quoteEvent.Time);
                }
            },
            SimulationVenuePolicy.Default with
            {
                BarExecution = false
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
    }

    [Fact]
    public async Task SameArrivalInflightSubmissionsUseSubmissionSequence_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var t1 = t0 + Duration.FromMillis(1);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMillis(1), Duration.Zero)
        };
        var firstOrderId = new OrderId(1901);
        var secondOrderId = new OrderId(1902);

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateQuote(t1, bid: 100m, ask: 101m)
            ],
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not QuoteReceived)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.BuyLimit(StrategyId, Instrument, new Qty(1m), new Price(95m, Currency.USD)) with
                    {
                        OrderId = firstOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.SubmitOrderAsync(
                    SubmitOrder.BuyLimit(StrategyId, Instrument, new Qty(1m), new Price(96m, Currency.USD)) with
                    {
                        OrderId = secondOrderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateQuote(t1, bid: 100m, ask: 101m)
            ],
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not QuoteReceived quote)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, firstOrderId, Side.Buy, new Qty(1m), Execution.Limit().At(new Price(95m, Currency.USD))),
                    quote.Time);
                venue.Submit(
                    new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, secondOrderId, Side.Buy, new Qty(1m), Execution.Limit().At(new Price(96m, Currency.USD))),
                    quote.Time);
            });

        Assert.Equal(
            replayEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray(),
            simulationEvents.OfType<OrderAccepted>().Select(NormalizeAccepted).ToArray());
    }

    [Fact]
    public async Task SameArrivalCancelBeatsModify_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var t1 = t0 + Duration.FromMillis(1);
        var t2 = t0 + Duration.FromMillis(2);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMillis(1), Duration.Zero)
        };
        var orderId = new OrderId(1903);

        var replaySubmitted = false;
        var replayAccepted = false;
        var replayQueued = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 101m),
                CreateQuote(t1, bid: 100m, ask: 101m),
                CreateQuote(t2, bid: 100m, ask: 101m)
            ],
            config,
            (connector, evt) =>
            {
                if (!replaySubmitted && evt is QuoteReceived)
                {
                    replaySubmitted = true;
                    connector.SubmitOrderAsync(
                        SubmitOrder.BuyLimit(StrategyId, Instrument, new Qty(1m), new Price(95m, Currency.USD)) with
                        {
                            OrderId = orderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                    return;
                }

                if (evt is OrderAccepted accepted && accepted.OrderId == orderId)
                {
                    replayAccepted = true;
                    return;
                }

                if (!replayAccepted || replayQueued || evt is not QuoteReceived)
                    return;

                replayQueued = true;
                connector.ModifyOrderAsync(
                    new ModifyOrder(orderId, NewLimitPrice: new Price(96m, Currency.USD)),
                    CancellationToken.None).GetAwaiter().GetResult();
                connector.CancelOrderAsync(new CancelOrder(orderId), CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            });

        var venue = new SimulatedVenueExchange(Instrument.Venue, config, Money.USD(100_000m));
        var simulationEvents = new List<Event>();
        var executionBuffer = new ExecutionEvent[16];
        var simulationBuffer = new FinanceEvent[16];
        venue.OnMarketEvent(CreateQuote(t0, bid: 100m, ask: 101m));
        venue.Submit(
            new SimulationOrderCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, orderId, Side.Buy, new Qty(1m), Execution.Limit().At(new Price(95m, Currency.USD))),
            t0);
        venue.DrainDueWork(t0);
        DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);

        venue.OnMarketEvent(CreateQuote(t1, bid: 100m, ask: 101m));
        venue.DrainDueWork(t1);
        DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);
        venue.Modify(
            new SimulationModifyCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, orderId, NewLimitPrice: new Price(96m, Currency.USD)),
            t1);
        venue.Cancel(
            new SimulationCancelCommand(StrategyId, 0, AssetId, Instrument, Instrument.Venue, orderId),
            t1);

        venue.OnMarketEvent(CreateQuote(t2, bid: 100m, ask: 101m));
        venue.DrainDueWork(t2);
        DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);

        Assert.Equal(
            replayEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray(),
            simulationEvents.OfType<OrderCancelled>().Select(NormalizeCancelled).ToArray());
        Assert.Empty(replayEvents.OfType<OrderModified>());
        Assert.Empty(simulationEvents.OfType<OrderModified>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task EntryLatencyDelaysSubmissionUntilLaterMarketState_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var t1 = t0 + Duration.FromSeconds(30);
        var t2 = t0 + Duration.FromMinutes(2);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMinutes(1), Duration.Zero)
        };
        var orderId = new OrderId(1904);

        var replaySubmitted = false;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t1, bid: 101m, ask: 101.05m),
                CreateQuote(t2, bid: 102m, ask: 102.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (replaySubmitted || evt is not QuoteReceived)
                    return;

                replaySubmitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                    {
                        OrderId = orderId
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            });

        var simulationSubmitted = false;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t1, bid: 101m, ask: 101.05m),
                CreateQuote(t2, bid: 102m, ask: 102.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (simulationSubmitted || evt is not QuoteReceived quote)
                    return;

                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            });

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFill(replayFill), NormalizeFill(simulationFill));
        Assert.Equal(102.05m, replayFill.FillPrice.Value);
        Assert.Equal(102.05m, simulationFill.FillPrice.Value);
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task DelayedModifyForOpenOrderAppliesBeforeLaterMarketEvent_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var t10 = t0 + Duration.FromMinutes(10);
        var t11 = t0 + Duration.FromMinutes(11);
        var t12 = t0 + Duration.FromMinutes(12);
        var t22 = t0 + Duration.FromMinutes(22);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMinutes(10), Duration.Zero)
        };
        var orderId = new OrderId(1905);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t10, bid: 100m, ask: 100.05m),
                CreateQuote(t11, bid: 101m, ask: 101.05m),
                CreateQuote(t12, bid: 101m, ask: 101.05m),
                CreateQuote(t22, bid: 102m, ask: 102.05m)
            };

        var replaySubmitted = false;
        var replayAccepted = false;
        var replayModifyRequested = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived && !replaySubmitted)
                {
                    replaySubmitted = true;
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = orderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }

                if (evt is OrderAccepted accepted && accepted.OrderId == orderId)
                {
                    replayAccepted = true;
                    return;
                }

                if (evt is QuoteReceived quote
                    && quote.Quote.Bid.Value == 101m
                    && replayAccepted
                    && !replayModifyRequested)
                {
                    replayModifyRequested = true;
                    connector.ModifyOrderAsync(
                        new ModifyOrder(orderId, new Qty(2m), new Price(102.05m, Currency.USD)),
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var venue = new SimulatedVenueExchange(Instrument.Venue, config, Money.USD(100_000m))
        {
            ProcessZeroLatencyCommandsImmediately = false
        };
        var simulationEvents = new List<Event>();
        var executionBuffer = new ExecutionEvent[16];
        var simulationBuffer = new FinanceEvent[16];
        var simulationSubmitted = false;
        var simulationModifyRequested = false;
        foreach (var evt in history)
        {
            var now = GetEventTime(evt);
            venue.DrainDueWork(now);
            DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);
            venue.OnMarketEvent(evt);
            DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);

            if (evt is QuoteReceived && !simulationSubmitted)
            {
                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(99m, Currency.USD))),
                    now);
                continue;
            }

            if (evt is QuoteReceived quote
                && quote.Quote.Bid.Value == 101m
                && simulationEvents.OfType<OrderAccepted>().Any(accepted => accepted.OrderId == orderId)
                && !simulationModifyRequested)
            {
                simulationModifyRequested = true;
                venue.Modify(
                    new SimulationModifyCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderId,
                        NewQuantity: new Qty(2m),
                        NewLimitPrice: new Price(102.05m, Currency.USD)),
                    now);
            }
        }

        venue.DrainDueWork(t22);
        DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));
        Assert.Equal(new Qty(2m), replayFill.FilledQty);
        Assert.Equal(new Qty(2m), simulationFill.FilledQty);
        Assert.Equal(102.05m, replayFill.FillPrice.Value);
        Assert.Equal(102.05m, simulationFill.FillPrice.Value);
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task ModifyOpenOrderEmitsOrderModified_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var orderId = new OrderId(1907);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                connector.SubmitOrderAsync(
                    SubmitOrder.Limit(
                        StrategyId,
                        Instrument,
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
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(99m, Currency.USD))),
                    quote.Time);
                venue.Modify(
                    new SimulationModifyCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderId,
                        NewQuantity: new Qty(2m),
                        NewLimitPrice: new Price(98m, Currency.USD)),
                    quote.Time);
            });

        var replayAccepted = Assert.Single(replayEvents.OfType<OrderAccepted>());
        var simulationAccepted = Assert.Single(simulationEvents.OfType<OrderAccepted>());
        Assert.Equal(NormalizeAccepted(replayAccepted), NormalizeAccepted(simulationAccepted));

        var replayModified = Assert.Single(replayEvents.OfType<OrderModified>());
        var simulationModified = Assert.Single(simulationEvents.OfType<OrderModified>());
        Assert.Equal(NormalizeModified(replayModified), NormalizeModified(simulationModified));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
    }

    [Fact]
    public async Task DelayedCancelForOpenOrderAllowsRaceFill_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var t10 = t0 + Duration.FromMinutes(10);
        var t11 = t0 + Duration.FromMinutes(11);
        var t12 = t0 + Duration.FromMinutes(12);
        var t21 = t0 + Duration.FromMinutes(21);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMinutes(10), Duration.Zero)
        };
        var orderId = new OrderId(1906);
        var history =
            new FinanceEvent[]
            {
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t10, bid: 100m, ask: 100.05m),
                CreateQuote(t11, bid: 100m, ask: 100.05m),
                CreateQuote(t12, bid: 98.95m, ask: 99m),
                CreateQuote(t21, bid: 98m, ask: 98.05m)
            };

        var replaySubmitted = false;
        var replayAccepted = false;
        var replayCancelRequested = false;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived && !replaySubmitted)
                {
                    replaySubmitted = true;
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = orderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }

                if (evt is OrderAccepted accepted && accepted.OrderId == orderId)
                {
                    replayAccepted = true;
                    return;
                }

                if (evt is QuoteReceived quote
                    && quote.Quote.Bid.Value == 100m
                    && replayAccepted
                    && !replayCancelRequested)
                {
                    replayCancelRequested = true;
                    connector.CancelOrderAsync(new CancelOrder(orderId), CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
            });

        var venue = new SimulatedVenueExchange(Instrument.Venue, config, Money.USD(100_000m))
        {
            ProcessZeroLatencyCommandsImmediately = false
        };
        var simulationEvents = new List<Event>();
        var executionBuffer = new ExecutionEvent[16];
        var simulationBuffer = new FinanceEvent[16];
        var simulationSubmitted = false;
        var simulationCancelRequested = false;
        foreach (var evt in history)
        {
            var now = GetEventTime(evt);
            venue.DrainDueWork(now);
            DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);
            venue.OnMarketEvent(evt);
            DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);

            if (evt is QuoteReceived && !simulationSubmitted)
            {
                simulationSubmitted = true;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderId,
                        Side.Buy,
                        new Qty(1m),
                        Execution.Limit().At(new Price(99m, Currency.USD))),
                    now);
                continue;
            }

            if (evt is QuoteReceived quote
                && quote.Quote.Bid.Value == 100m
                && simulationEvents.OfType<OrderAccepted>().Any(accepted => accepted.OrderId == orderId)
                && !simulationCancelRequested)
            {
                simulationCancelRequested = true;
                venue.Cancel(
                    new SimulationCancelCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderId),
                    now);
            }
        }

        venue.DrainDueWork(t21);
        DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);

        var replayFill = Assert.Single(replayEvents.OfType<OrderFilled>());
        var simulationFill = Assert.Single(simulationEvents.OfType<OrderFilled>());
        Assert.Equal(NormalizeFillValue(replayFill), NormalizeFillValue(simulationFill));
        Assert.Equal(99m, replayFill.FillPrice.Value);
        Assert.Equal(99m, simulationFill.FillPrice.Value);
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task CancelBeforeExchangeArrival_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMinutes(10), Duration.Zero)
        };
        var replayOrderId = new OrderId(202);
        var simulationOrderId = new OrderId(202);

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromMinutes(1), bid: 101m, ask: 101.05m),
                CreateQuote(t0 + Duration.FromMinutes(11), bid: 102m, ask: 102.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                if (replayQuoteCount == 1)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(StrategyId, Instrument, Side.Buy, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                else if (replayQuoteCount == 2)
                {
                    connector.CancelOrderAsync(
                        new CancelOrder { OrderId = replayOrderId },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromMinutes(1), bid: 101m, ask: 101.05m),
                CreateQuote(t0 + Duration.FromMinutes(11), bid: 102m, ask: 102.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                simulationQuoteCount++;
                if (simulationQuoteCount == 1)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Market()),
                        quote.Time);
                }
                else if (simulationQuoteCount == 2)
                {
                    venue.Cancel(
                        new SimulationCancelCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId),
                        quote.Time);
                }
            });

        var replayCancelled = Assert.Single(replayEvents.OfType<OrderCancelled>());
        var simulationCancelled = Assert.Single(simulationEvents.OfType<OrderCancelled>());
        Assert.Equal(NormalizeCancelled(replayCancelled), NormalizeCancelled(simulationCancelled));
        Assert.Empty(replayEvents.OfType<OrderAccepted>());
        Assert.Empty(simulationEvents.OfType<OrderAccepted>());
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Fact]
    public async Task ModifyBeforeExchangeArrival_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMinutes(10), Duration.Zero)
        };
        var replayOrderId = new OrderId(303);
        var simulationOrderId = new OrderId(303);
        var modifiedQuantity = new Qty(2m);
        var modifiedLimit = new Price(98m, Currency.USD);

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromMinutes(1), bid: 101m, ask: 101.05m),
                CreateQuote(t0 + Duration.FromMinutes(11), bid: 102m, ask: 102.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived)
                    return;

                replayQuoteCount++;
                if (replayQuoteCount == 1)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Limit(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m),
                            new Price(99m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                else if (replayQuoteCount == 2)
                {
                    connector.ModifyOrderAsync(
                        new ModifyOrder(replayOrderId, modifiedQuantity, modifiedLimit),
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationQuoteCount = 0;
        var simulationEvents = RunSimulatedVenue(
            [
                CreateQuote(t0, bid: 100m, ask: 100.05m),
                CreateQuote(t0 + Duration.FromMinutes(1), bid: 101m, ask: 101.05m),
                CreateQuote(t0 + Duration.FromMinutes(11), bid: 102m, ask: 102.05m)
            ],
            config,
            (venue, evt) =>
            {
                if (evt is not QuoteReceived quote)
                    return;

                simulationQuoteCount++;
                if (simulationQuoteCount == 1)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Limit().At(new Price(99m, Currency.USD))),
                        quote.Time);
                }
                else if (simulationQuoteCount == 2)
                {
                    venue.Modify(
                        new SimulationModifyCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            modifiedQuantity,
                            modifiedLimit),
                        quote.Time);
                }
            });

        var replayModified = Assert.Single(replayEvents.OfType<OrderModified>());
        var simulationModified = Assert.Single(simulationEvents.OfType<OrderModified>());
        Assert.Equal(NormalizeModified(replayModified), NormalizeModified(simulationModified));
        Assert.Single(replayEvents.OfType<OrderAccepted>());
        Assert.Single(simulationEvents.OfType<OrderAccepted>());

        var replayStates = replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray();
        var simulationStates = simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray();
        Assert.Equal(replayStates, simulationStates);
        Assert.Equal(
            [OrderStatus.Open, OrderStatus.Open],
            simulationStates.Select(static state => state.Status).ToArray());
        Assert.Equal(modifiedQuantity, simulationStates[0].RemainingQuantity);
        Assert.Null(simulationStates[1].RemainingQuantity);

        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Empty(replayEvents.OfType<OrderRejected>());
        Assert.Empty(simulationEvents.OfType<OrderRejected>());
    }

    [Theory]
    [InlineData(MarketStatus.PreOpen)]
    [InlineData(MarketStatus.Closed)]
    [InlineData(MarketStatus.Halted)]
    public async Task InitialNonOpenMarketStatusRejectsSubmission_HasReplayConnectorParity(MarketStatus status)
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            InitialMarketStatus = status
        };
        var replayOrderId = new OrderId(403);
        var simulationOrderId = new OrderId(403);

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(
                            StrategyId,
                            Instrument,
                            Side.Buy,
                            new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Market()),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, status.ToString());
    }

    [Fact]
    public async Task VenueCloseRejectsDelayedSubmissionOnArrival_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMillis(1), Duration.Zero)
        };
        var replayOrderId = new OrderId(404);
        var simulationOrderId = new OrderId(404);
        var close = new VenueStatusChanged(Venue.NASDAQ, MarketStatus.Closed, "session closed")
        {
            Time = t0 + Duration.FromMillis(1)
        };

        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m), close],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.BuyLimit(
                            StrategyId,
                            Instrument,
                            new Qty(1m),
                            new Price(95m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedVenue(
            [CreateQuote(t0, bid: 100m, ask: 100.05m), close],
            config,
            (venue, evt) =>
            {
                if (evt is QuoteReceived quote)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Limit().At(new Price(95m, Currency.USD))),
                        quote.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, nameof(MarketStatus.Closed));
    }

    [Fact]
    public async Task InstrumentHaltRejectsOnlyThatInstrument_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var otherInstrument = new Instrument(new Asset("MSFT", AssetClass.Equity), Venue.NYSE);
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromMillis(1), Duration.Zero)
        };
        var haltedOrderId = new OrderId(408);
        var openOrderId = new OrderId(409);
        var haltedCommand = new SimulationOrderCommand(
            StrategyId,
            VariantId: 0,
            AssetId,
            Instrument,
            Instrument.Venue,
            haltedOrderId,
            Side.Buy,
            new Qty(1m),
            Execution.Limit().At(new Price(95m, Currency.USD)));
        var openCommand = new SimulationOrderCommand(
            StrategyId,
            VariantId: 0,
            AssetId,
            otherInstrument,
            otherInstrument.Venue,
            openOrderId,
            Side.Buy,
            new Qty(1m),
            Execution.Limit().At(new Price(95m, Currency.USD)));
        var otherQuote = CreateQuote(otherInstrument, t0, bid: 100m, ask: 100.05m);
        var haltedQuote = CreateQuote(t0, bid: 100m, ask: 100.05m);
        var halt = new InstrumentStatusChanged(Instrument, MarketStatus.Halted, "halt")
        {
            Time = t0 + Duration.FromMillis(1)
        };

        var replayEvents = await RunReplayConnectorAsync(
            [otherQuote, haltedQuote, halt],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Instrument == Instrument)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.BuyLimit(
                            StrategyId,
                            Instrument,
                            new Qty(1m),
                            new Price(95m, Currency.USD)) with
                        {
                            OrderId = haltedOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                    connector.SubmitOrderAsync(
                        SubmitOrder.BuyLimit(
                            StrategyId,
                            otherInstrument,
                            new Qty(1m),
                            new Price(95m, Currency.USD)) with
                        {
                            OrderId = openOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions:
            [
                new Subscription(Instrument, SubscriptionType.Quotes),
                new Subscription(otherInstrument, SubscriptionType.Quotes)
            ]);

        var registry = new SimulatedExchangeRegistry(
            config,
            Money.USD(100_000m),
            identity: new SimulationIdentityGenerator());
        var executionBuffer = new ExecutionEvent[16];
        var simulatorBuffer = new FinanceEvent[16];
        var simulationEvents = new List<Event>();
        var submitted = false;
        foreach (var evt in new FinanceEvent[] { otherQuote, haltedQuote, halt })
        {
            registry.OnMarketEvent(evt);
            if (!submitted && evt is QuoteReceived quote && quote.Instrument == Instrument)
            {
                submitted = true;
                registry.Submit(in haltedCommand, quote.Time);
                registry.Submit(in openCommand, quote.Time);
            }

            registry.DrainDueWork(GetEventTime(evt));
            DrainRegistryEvents(registry, executionBuffer.AsSpan(), simulatorBuffer.AsSpan(), simulationEvents);
        }

        var replayRejected = Assert.Single(replayEvents.OfType<OrderRejected>());
        var simulationRejected = Assert.Single(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(NormalizeRejected(replayRejected), NormalizeRejected(simulationRejected));
        Assert.Contains(nameof(MarketStatus.Halted), simulationRejected.Reason);
        Assert.Contains(replayEvents, evt => evt is OrderAccepted accepted && accepted.OrderId == openOrderId);
        Assert.Contains(simulationEvents, evt => evt is OrderAccepted accepted && accepted.OrderId == openOrderId);
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
    }

    [Fact]
    public async Task MarketClosedLifecycleUpdatesVenueStatus_HasReplayConnectorParity()
    {
        var t0 = Instant.FromUnixSeconds(1);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(405);
        var simulationOrderId = new OrderId(405);
        var closed = new MarketClosed(Instrument.Venue)
        {
            Time = t0
        };

        var replayEvents = await RunReplayConnectorAsync(
            [closed],
            config,
            (connector, evt) =>
            {
                if (evt is MarketClosed)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.BuyLimit(
                            StrategyId,
                            Instrument,
                            new Qty(1m),
                            new Price(95m, Currency.USD)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            subscriptions: []);

        var simulationEvents = RunSimulatedVenue(
            [closed],
            config,
            (venue, evt) =>
            {
                if (evt is MarketClosed)
                {
                    venue.Submit(
                        new SimulationOrderCommand(
                            StrategyId,
                            VariantId: 0,
                            AssetId,
                            Instrument,
                            Instrument.Venue,
                            simulationOrderId,
                            Side.Buy,
                            new Qty(1m),
                            Execution.Limit().At(new Price(95m, Currency.USD))),
                        evt.Time);
                }
            });

        AssertRejectedOnly(replayEvents, simulationEvents, nameof(MarketStatus.Closed));
    }

    [Fact]
    public async Task ModuleGeneratedInstrumentStatusFeedsSimulationSessionPath_HasReplayConnectorParity()
    {
        var first = Instant.FromUnixSeconds(1);
        var second = Instant.FromUnixSeconds(2);
        var config = SimulationConfig.Instant();
        var replayOrderId = new OrderId(1);
        ModuleParityBuyOnSecondQuoteStrategy.Reset();
        using var session = new SimulationSession(defaultConfig: config);
        var simulationStrategyId = session.RegisterStrategy<ModuleParityBuyOnSecondQuoteStrategy>();

        var replayModule = new ClosingReplayModule(Instrument, first);
        var replayEvents = await RunReplayConnectorAsync(
            [
                CreateQuote(first, bid: 100m, ask: 100.05m),
                CreateQuote(second, bid: 101m, ask: 101.05m)
            ],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived quote && quote.Time == second)
                {
                    connector.SubmitOrderAsync(
                        SubmitOrder.Market(simulationStrategyId, Instrument, Side.Buy, new Qty(1m)) with
                        {
                            OrderId = replayOrderId
                        },
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            },
            modules: [replayModule]);

        var simulationModule = new ClosingSimulationModule(Instrument, first);
        var result = session.Run(
            SharedHistory.Load(
            [
                CreateQuote(first, bid: 100m, ask: 100.05m),
                CreateQuote(second, bid: 101m, ask: 101.05m)
            ]),
            new SimulationRunOptions { SessionModules = [simulationModule] });
        var simulationEvents = result.ExecutionEvents.Cast<Event>()
            .Concat(result.SimulatorEvents)
            .ToArray();

        Assert.Equal(2, replayModule.PreProcessCount);
        Assert.Equal(2, replayModule.ProcessCount);
        Assert.Equal(2, simulationModule.PreProcessCount);
        Assert.Equal(2, simulationModule.ProcessCount);
        Assert.Contains(replayEvents, static evt => evt is InstrumentStatusChanged);
        Assert.Contains(simulationEvents, static evt => evt is InstrumentStatusChanged);
        AssertRejectedOnly(replayEvents, simulationEvents, nameof(MarketStatus.Closed));
    }

    private static async Task<Event[]> RunReplayConnectorAsync(
        FinanceEvent[] history,
        SimulationConfig config,
        Action<ReplayConnector, Event> onEmit,
        Action<ReplayConnector>? configure = null,
        Money? initialCash = null,
        Subscription[]? subscriptions = null,
        IReadOnlyList<IReplaySimulationModule>? modules = null)
    {
        var connector = new ReplayConnector(ReadHistoryAsync(history), config, initialCash: initialCash)
        {
            Modules = modules ?? []
        };
        configure?.Invoke(connector);
        var events = new TestEventPublisher();
        events.OnEmit = evt => onEmit(connector, evt);

        await connector.StartAsync(
            subscriptions ?? [new Subscription(Instrument, SubscriptionType.Quotes)],
            events,
            CancellationToken.None);

        return events.ToArray();
    }

    private static Event[] RunSimulatedVenue(
        FinanceEvent[] history,
        SimulationConfig config,
        Action<SimulatedVenueExchange, FinanceEvent> onEvent,
        SimulationVenuePolicy? policy = null,
        Money? initialCash = null,
        MatchingFidelity matchingFidelity = MatchingFidelity.QueueAccurate,
        SimulationOrderPolicy? orderPolicy = null,
        bool invokeOnEventBeforeMarketEvent = false)
    {
        var venue = new SimulatedVenueExchange(
            Instrument.Venue,
            config,
            initialCash ?? Money.USD(100_000m),
            defaultMatchingFidelity: matchingFidelity,
            orderPolicy: orderPolicy,
            simulationPolicy: policy);
        var buffer = new ExecutionEvent[16];
        var simulationBuffer = new FinanceEvent[16];
        var events = new List<Event>();

        for (var i = 0; i < history.Length; i++)
        {
            var evt = history[i];
            if (invokeOnEventBeforeMarketEvent)
                onEvent(venue, evt);
            venue.OnMarketEvent(evt);
            if (!invokeOnEventBeforeMarketEvent)
                onEvent(venue, evt);
            venue.DrainDueWork(GetEventTime(evt));
            DrainVenueEvents(venue, buffer.AsSpan(), simulationBuffer.AsSpan(), events);
        }

        venue.DrainDueWork(GetEventTime(history[^1]));
        venue.CompleteReplay(GetEventTime(history[^1]));
        DrainVenueEvents(venue, buffer.AsSpan(), simulationBuffer.AsSpan(), events);
        return [.. events];
    }

    private static async Task<(Event[] ReplayEvents, Event[] SimulationEvents)> RunCashSettlementScenarioAsync(
        Instant[] quoteTimes,
        SimulationConfig config,
        Money initialCash,
        OrderId[] orderIds,
        Side[] sides)
    {
        if (orderIds.Length != sides.Length)
            throw new ArgumentException("Order IDs and sides must have the same length.", nameof(orderIds));

        var history = quoteTimes
            .Select(static (time, index) => CreateQuote(time, 100m + index * 0.01m, 100.05m + index * 0.01m))
            .Cast<FinanceEvent>()
            .ToArray();

        var replayQuoteCount = 0;
        var replayEvents = await RunReplayConnectorAsync(
            history,
            config,
            (connector, evt) =>
            {
                if (evt is not QuoteReceived || replayQuoteCount >= orderIds.Length)
                    return;

                var index = replayQuoteCount++;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(StrategyId, Instrument, sides[index], new Qty(1m)) with
                    {
                        OrderId = orderIds[index]
                    },
                    CancellationToken.None).GetAwaiter().GetResult();
            },
            initialCash: initialCash);

        var venue = new SimulatedVenueExchange(Instrument.Venue, config, initialCash);
        var executionBuffer = new ExecutionEvent[16];
        var simulationBuffer = new FinanceEvent[16];
        var simulationEvents = new List<Event>();
        var simulationQuoteCount = 0;
        for (var i = 0; i < history.Length; i++)
        {
            var evt = history[i];
            venue.OnMarketEvent(evt);
            if (evt is QuoteReceived quote && simulationQuoteCount < orderIds.Length)
            {
                var index = simulationQuoteCount++;
                venue.Submit(
                    new SimulationOrderCommand(
                        StrategyId,
                        VariantId: 0,
                        AssetId,
                        Instrument,
                        Instrument.Venue,
                        orderIds[index],
                        sides[index],
                        new Qty(1m),
                        Execution.Market()),
                    quote.Time);
            }

            venue.DrainDueWork(GetEventTime(evt));
            DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);
        }

        var lastTime = GetEventTime(history[^1]);
        venue.DrainDueWork(lastTime);
        venue.CompleteReplay(lastTime);
        DrainVenueEvents(venue, executionBuffer.AsSpan(), simulationBuffer.AsSpan(), simulationEvents);
        simulationEvents.Add(venue.CreateAccountStatement(StrategyId, 0, Currency.USD, lastTime));

        return (replayEvents, [.. simulationEvents]);
    }

    private static Event[] RunSimulatedFinancing(
        FinancingChargeCommand command,
        SimulationConfig config,
        Money initialCash)
        => RunSimulatedFinancings([command], config, initialCash);

    private static Event[] RunSimulatedFinancings(
        FinancingChargeCommand[] commands,
        SimulationConfig config,
        Money initialCash)
    {
        var venue = new SimulatedVenueExchange(Instrument.Venue, config, initialCash);
        var events = new List<Event>(commands.Length * 2);
        for (var i = 0; i < commands.Length; i++)
        {
            var command = commands[i];
            if (command.Instrument is { } instrument)
            {
                venue = venue.Venue == instrument.Venue
                    ? venue
                    : new SimulatedVenueExchange(instrument.Venue, config, initialCash);
                venue.RegisterContract(CreateFinancingContract(command));
            }

            var financing = new FinancingChargeApplied(
                command.FinancingChargeId,
                command.ChargeType,
                command.StrategyId,
                command.VariantId,
                command.Amount,
                command.EffectiveAt,
                command.Instrument,
                command.Quantity,
                command.Rate,
                command.ExternalReference)
            {
                Time = command.EffectiveAt
            };
            events.Add(financing);
            events.Add(venue.ApplyFinancing(financing, command.EffectiveAt));
        }

        return [.. events];
    }

    private static InstrumentContract CreateFinancingContract(FinancingChargeCommand command)
    {
        if (command.Instrument is not { } instrument)
            throw new InvalidOperationException("Instrument-level financing commands require an instrument.");

        return command.ChargeType switch
        {
            FinancingChargeType.BorrowFee => Contracts.Equity(
                instrument.Asset.Symbol,
                instrument.Venue,
                command.Amount.Currency),
            FinancingChargeType.PerpetualFunding => Contracts.CryptoPerpetual(
                instrument.Asset.Symbol,
                instrument.Venue,
                Currency.BTC,
                Currency.USD,
                command.Amount.Currency,
                tick: 0.1m,
                lot: 0.001m,
                multiplier: 1m,
                inverse: false),
            FinancingChargeType.ForexRollover => Contracts.CurrencyPair(
                instrument.Asset.Symbol,
                instrument.Venue,
                Currency.EUR,
                command.Amount.Currency,
                pip: 0.0001m,
                lot: 1000m),
            _ => throw new InvalidOperationException(
                $"Financing charge type {command.ChargeType} is not an instrument-level financing command.")
        };
    }

    private static Event[] RunSimulatedTransfer(
        AccountTransferCommand command,
        SimulationConfig config,
        Money initialCash)
        => RunSimulatedTransfers([command], config, initialCash);

    private static Event[] RunSimulatedCorporateActionAfterAssetDeposit(
        AccountTransferCommand deposit,
        CorporateActionApplied corporateAction,
        SimulationConfig config,
        Money initialCash,
        Instant depositQuoteTime,
        Instant actionQuoteTime)
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<ParityUniverseStrategy>();
        var completed = new AccountTransferCompleted(
            deposit.TransferId,
            deposit.StrategyId,
            deposit.VariantId,
            deposit.TransferType,
            deposit.CashAmount,
            deposit.Instrument,
            deposit.Quantity,
            depositQuoteTime,
            deposit.ExternalReference,
            deposit.DestinationStrategyId,
            deposit.DestinationVariantId,
            Venue: deposit.Instrument?.Venue ?? Instrument.Venue,
            CarryingPrice: deposit.CarryingPrice)
        {
            Time = depositQuoteTime
        };

        var result = session.Run(
            SharedHistory.Load(
            [
                CreateQuote(depositQuoteTime, bid: 100m, ask: 100.05m),
                completed,
                CreateQuote(actionQuoteTime, bid: 101m, ask: 101.05m),
                corporateAction
            ]),
            new SimulationRunOptions
            {
                Config = config,
                InitialCash = initialCash
            });

        var events = new List<Event>();
        events.AddRange(result.AccountStatements);
        events.AddRange(result.SimulatorEvents);
        return [.. events];
    }

    private static Event[] RunSimulatedTransfers(
        AccountTransferCommand[] commands,
        SimulationConfig config,
        Money initialCash)
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<ParityUniverseStrategy>();
        var completed = new AccountTransferCompleted[commands.Length];
        for (var i = 0; i < commands.Length; i++)
        {
            var command = commands[i];
            completed[i] = new AccountTransferCompleted(
                command.TransferId,
                command.StrategyId,
                command.VariantId,
                command.TransferType,
                command.CashAmount,
                command.Instrument,
                command.Quantity,
                Instant.FromUnixSeconds(1),
                command.ExternalReference,
                command.DestinationStrategyId,
                command.DestinationVariantId,
                Venue: command.Instrument?.Venue ?? Instrument.Venue,
                CarryingPrice: command.CarryingPrice)
            {
                Time = Instant.FromUnixSeconds(1)
            };
        }

        var result = session.Run(
            SharedHistory.Load(completed),
            new SimulationRunOptions
            {
                Config = config,
                InitialCash = initialCash
            });

        var events = new List<Event>();
        events.AddRange(result.SimulatorEvents);
        events.AddRange(result.AccountStatements);
        return [.. events];
    }

    private static Event[] RunSimulatedTransferLifecycle(
        FinanceEvent[] lifecycleEvents,
        SimulationConfig config,
        Money initialCash)
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<ParityUniverseStrategy>();

        var result = session.Run(
            SharedHistory.Load(lifecycleEvents),
            new SimulationRunOptions
            {
                Config = config,
                InitialCash = initialCash
            });

        var events = new List<Event>();
        events.AddRange(result.SimulatorEvents);
        events.AddRange(result.AccountStatements);
        return [.. events];
    }

    private static AccountTransferRequested CreateTransferRequested(
        AccountTransferCommand command,
        Instant now)
        => new(
            command.TransferId,
            command.StrategyId,
            command.VariantId,
            command.TransferType,
            command.CashAmount,
            command.Instrument,
            command.Quantity,
            now,
            command.ExternalReference,
            command.DestinationStrategyId,
            command.DestinationVariantId,
            command.Instrument?.Venue,
            command.CarryingPrice)
        {
            Time = now
        };

    private static AccountTransferCanceled CreateTransferCanceled(
        AccountTransferCommand command,
        Instant now,
        string? reason)
        => new(
            command.TransferId,
            command.StrategyId,
            command.VariantId,
            command.TransferType,
            command.CashAmount,
            command.Instrument,
            command.Quantity,
            now,
            reason,
            command.ExternalReference,
            command.DestinationStrategyId,
            command.DestinationVariantId,
            command.Instrument?.Venue,
            command.CarryingPrice)
        {
            Time = now
        };

    private static AccountTransferFailed CreateTransferFailed(
        AccountTransferCommand command,
        Instant now,
        string reason)
        => new(
            command.TransferId,
            command.StrategyId,
            command.VariantId,
            command.TransferType,
            command.CashAmount,
            command.Instrument,
            command.Quantity,
            now,
            reason,
            command.ExternalReference,
            command.DestinationStrategyId,
            command.DestinationVariantId,
            command.Instrument?.Venue,
            command.CarryingPrice)
        {
            Time = now
        };

    private static AccountTransferStatusSnapshot CreateTransferStatus(
        AccountTransferCommand command,
        AccountTransferStatus status,
        Instant now,
        string? reason = null)
        => new(
            command.TransferId,
            command.StrategyId,
            command.VariantId,
            command.TransferType,
            status,
            command.CashAmount,
            command.Instrument,
            command.Quantity,
            now,
            reason,
            command.ExternalReference,
            command.DestinationStrategyId,
            command.DestinationVariantId,
            command.Instrument?.Venue,
            command.CarryingPrice)
        {
            Time = now
        };

    private static void DrainVenueEvents(
        SimulatedVenueExchange venue,
        Span<ExecutionEvent> buffer,
        Span<FinanceEvent> simulationBuffer,
        List<Event> events)
    {
        while (true)
        {
            var count = venue.DrainExecutionEvents(buffer);
            if (count == 0)
                break;

            for (var i = 0; i < count; i++)
                events.Add(buffer[i]);
        }

        while (true)
        {
            var count = venue.DrainSimulationEvents(simulationBuffer);
            if (count == 0)
                return;

            for (var i = 0; i < count; i++)
                events.Add(simulationBuffer[i]);
        }
    }

    private static void DrainRegistryEvents(
        SimulatedExchangeRegistry registry,
        Span<ExecutionEvent> buffer,
        Span<FinanceEvent> simulationBuffer,
        List<Event> events)
    {
        while (true)
        {
            var count = registry.DrainExecutionEvents(buffer);
            if (count == 0)
                break;

            for (var i = 0; i < count; i++)
                events.Add(buffer[i]);
        }

        while (true)
        {
            var count = registry.DrainSimulationEvents(simulationBuffer);
            if (count == 0)
                return;

            for (var i = 0; i < count; i++)
                events.Add(simulationBuffer[i]);
        }
    }

    private static void AssertRejectedOnly(Event[] replayEvents, Event[] simulationEvents, params string[] expectedReasonParts)
    {
        var replayRejected = Assert.Single(replayEvents.OfType<OrderRejected>());
        var simulationRejected = Assert.Single(simulationEvents.OfType<OrderRejected>());
        Assert.Equal(NormalizeRejected(replayRejected), NormalizeRejected(simulationRejected));
        Assert.Equal(
            replayEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray(),
            simulationEvents.OfType<OrderStateSnapshot>().Select(NormalizeOrderState).ToArray());
        for (var i = 0; i < expectedReasonParts.Length; i++)
        {
            Assert.Contains(expectedReasonParts[i], replayRejected.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedReasonParts[i], simulationRejected.Reason, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Empty(replayEvents.OfType<OrderAccepted>());
        Assert.Empty(simulationEvents.OfType<OrderAccepted>());
        Assert.Empty(replayEvents.OfType<OrderFilled>());
        Assert.Empty(simulationEvents.OfType<OrderFilled>());
        Assert.Empty(replayEvents.OfType<OrderCancelled>());
        Assert.Empty(simulationEvents.OfType<OrderCancelled>());
        Assert.Empty(replayEvents.OfType<OrderExpired>());
        Assert.Empty(simulationEvents.OfType<OrderExpired>());
    }

    private static QuoteReceived CreateQuote(Instant time, decimal bid, decimal ask)
        => CreateQuote(Instrument, time, bid, ask);

    private static QuoteReceived CreateQuote(Instrument instrument, Instant time, decimal bid, decimal ask)
        => new(
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

    private static TradeOccurred CreateTrade(Instant time, decimal price, decimal size, Side aggressorSide)
        => new(
            Instrument,
            new Trade(
                new Price(price, Currency.USD),
                new Qty(size),
                aggressorSide,
                DualTimestamp.Synchronized(time)))
        {
            Time = time
        };

    private static BarClosed CreateBar(Instant time, decimal open, decimal high, decimal low, decimal close)
        => new(
            Instrument,
            new Bar(
                new Price(open, Currency.USD),
                new Price(high, Currency.USD),
                new Price(low, Currency.USD),
                new Price(close, Currency.USD),
                new Qty(100m),
                time,
                Duration.FromMinutes(1)))
        {
            Time = time
        };

    private static BookSnapshotReceived CreateBookSnapshot(
        Instant time,
        Level[] bids,
        Level[] asks)
        => new(
            Instrument,
            new Book
            {
                Instrument = Instrument,
                Time = time,
                Bids = bids,
                Asks = asks
            })
        {
            Time = time
        };

    private static ExecutionSpec FillOrKillLimit(Price limit)
        => new(
            OrderType.Limit,
            limit,
            ExecutionLimitPriceMode.Explicit,
            TimeInForce.FOK);

    private static ExecutionSpec GoodTilDateWithoutExpiryLimit(Price limit)
        => new(
            OrderType.Limit,
            limit,
            ExecutionLimitPriceMode.Explicit,
            TimeInForce.GTD);

    private static SimulationConfig MarginConfig(decimal leverage = 2m, Duration marginCallGracePeriod = default)
        => SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(leverage).WithMarginCallGracePeriod(marginCallGracePeriod)
        };

    private static FillSummary NormalizeFill(OrderFilled fill)
        => new(fill.StrategyId, fill.OrderId, fill.Side, fill.FilledQty, fill.FillPrice);

    private static FillValueSummary NormalizeFillValue(OrderFilled fill)
        => new(fill.StrategyId, fill.OrderId, fill.Side, fill.FilledQty, fill.FillPrice.Value);

    private static FillEconomicSummary NormalizeFillEconomics(OrderFilled fill)
        => new(fill.StrategyId, fill.Side, fill.FilledQty, fill.FillPrice.Value);

    private static FillCommissionSummary NormalizeFillCommission(OrderFilled fill)
        => new(fill.StrategyId, fill.OrderId, fill.Side, fill.FilledQty, fill.FillPrice.Value, fill.Commission);

    private static AcceptedSummary NormalizeAccepted(OrderAccepted accepted)
        => new(accepted.StrategyId, accepted.OrderId);

    private static ModifiedSummary NormalizeModified(OrderModified modified)
        => new(modified.StrategyId, modified.OrderId, modified.NewQuantity, modified.NewLimitPrice);

    private static CancelledSummary NormalizeCancelled(OrderCancelled cancelled)
        => new(cancelled.StrategyId, cancelled.OrderId, cancelled.RemainingQty);

    private static ExpiredSummary NormalizeExpired(OrderExpired expired)
        => new(expired.StrategyId, expired.OrderId);

    private static RejectedSummary NormalizeRejected(OrderRejected rejected)
        => new(rejected.StrategyId, rejected.OrderId);

    private static OrderStateSummary NormalizeOrderState(OrderStateSnapshot snapshot)
        => new(
            snapshot.OrderId,
            snapshot.StrategyId,
            snapshot.VariantId,
            snapshot.Status,
            snapshot.FilledQty,
            snapshot.RemainingQty,
            snapshot.Reason is null ? null : NormalizeReason(snapshot.Reason));

    private static PerformanceSummary NormalizePerformance(PerformanceSnapshot performance)
        => new(
            performance.Equity,
            performance.Cash,
            performance.UnrealizedPnL,
            performance.RealizedPnL,
            performance.OpenPositions,
            performance.OpenOrders);

    private static MarginStatusSummary NormalizeMarginStatus(MarginStatusSnapshot status)
        => new(
            status.StrategyId,
            status.VariantId,
            status.Equity,
            status.MaintenanceRequirement,
            status.IsMaintenanceBreached);

    private static MarginCallIssuedSummary NormalizeMarginCallIssued(MarginCallIssued issued)
        => new(
            issued.StrategyId,
            issued.VariantId,
            issued.Equity,
            issued.MaintenanceRequirement,
            issued.DueAt);

    private static MarginCallResolvedSummary NormalizeMarginCallResolved(MarginCallResolved resolved)
        => new(
            resolved.StrategyId,
            resolved.VariantId,
            resolved.Equity,
            resolved.MaintenanceRequirement);

    private static RiskLimitSummary NormalizeRiskLimit(RiskLimitBreached breach)
        => new(breach.LimitName, breach.CurrentValue, breach.LimitValue);

    private static CorporateActionEffectSummary NormalizeCorporateActionEffect(CorporateActionEffectSnapshot effect)
        => new(
            effect.CorporateActionId,
            effect.ActionType,
            effect.StrategyId,
            effect.VariantId,
            effect.Instrument,
            effect.QuantityBefore,
            effect.QuantityAfter,
            effect.AvgEntryPriceBefore,
            effect.AvgEntryPriceAfter,
            effect.CashAmount,
            effect.EffectiveAt);

    private static FinancingSummary NormalizeFinancing(FinancingChargeApplied financing)
        => new(
            financing.FinancingChargeId,
            financing.ChargeType,
            financing.StrategyId,
            financing.VariantId,
            financing.Amount,
            financing.EffectiveAt,
            financing.Instrument,
            financing.Quantity,
            financing.Rate,
            financing.ExternalReference);

    private static AccountStatementSummary NormalizeAccountStatement(AccountStatementSnapshot statement)
        => new(
            statement.StrategyId,
            statement.VariantId,
            statement.Currency,
            statement.Cash,
            statement.AvailableCash,
            statement.PendingSettlement,
            statement.ReservedCash,
            statement.MarketValue,
            statement.Equity,
            statement.UnrealizedPnL,
            statement.RealizedPnL,
            statement.OpenPositions,
            statement.OpenOrders);

    private static SettlementScheduledSummary NormalizeSettlementScheduled(SettlementScheduled settlement)
        => new(
            settlement.StrategyId,
            settlement.VariantId,
            settlement.Amount,
            settlement.SettlesAt);

    private static SettlementReleasedSummary NormalizeSettlementReleased(SettlementReleased settlement)
        => new(
            settlement.StrategyId,
            settlement.VariantId,
            settlement.Amount,
            settlement.SettledAt);

    private static SettlementStatusSummary NormalizeSettlementStatus(SettlementStatusSnapshot settlement)
        => new(
            settlement.StrategyId,
            settlement.VariantId,
            settlement.Status,
            settlement.Amount,
            settlement.SettlesAt,
            settlement.StatusAt);

    private static AssetDeliveryScheduledSummary NormalizeAssetDeliveryScheduled(AssetDeliveryScheduled delivery)
        => new(
            delivery.StrategyId,
            delivery.VariantId,
            delivery.Instrument,
            delivery.Quantity,
            delivery.DeliversAt);

    private static AssetDeliveredSummary NormalizeAssetDelivered(AssetDelivered delivery)
        => new(
            delivery.StrategyId,
            delivery.VariantId,
            delivery.Instrument,
            delivery.Quantity,
            delivery.DeliveredAt);

    private static AssetDeliveryCanceledSummary NormalizeAssetDeliveryCanceled(AssetDeliveryCanceled delivery)
        => new(
            delivery.StrategyId,
            delivery.VariantId,
            delivery.Instrument,
            delivery.Quantity,
            delivery.CanceledAt);

    private static AssetDeliveryStatusSummary NormalizeAssetDeliveryStatus(AssetDeliveryStatusSnapshot delivery)
        => new(
            delivery.StrategyId,
            delivery.VariantId,
            delivery.Instrument,
            delivery.Quantity,
            delivery.Status,
            delivery.DeliversAt,
            delivery.StatusAt);

    private static CustodySummary NormalizeCustody(CustodyPositionSnapshot custody)
        => new(
            custody.StrategyId,
            custody.VariantId,
            custody.Instrument,
            custody.Quantity,
            custody.SettledQuantity,
            custody.PendingDeliveryQuantity,
            custody.RehypothecatableQuantity,
            custody.AvgEntryPrice,
            custody.MarkPrice,
            custody.MarketValue,
            custody.UnrealizedPnL,
            custody.RealizedPnL,
            custody.IsOpen);

    private static TransferStatusSummary NormalizeTransferStatus(AccountTransferStatusSnapshot status)
        => new(
            status.TransferId,
            status.StrategyId,
            status.VariantId,
            status.TransferType,
            status.Status,
            status.CashAmount,
            status.Instrument,
            status.Quantity,
            status.Reason is null ? null : NormalizeReason(status.Reason),
            status.ExternalReference,
            status.DestinationStrategyId,
            status.DestinationVariantId,
            status.CarryingPrice);

    private static TransferCompletedSummary NormalizeTransferCompleted(AccountTransferCompleted completed)
        => new(
            completed.TransferId,
            completed.StrategyId,
            completed.VariantId,
            completed.TransferType,
            completed.CashAmount,
            completed.Instrument,
            completed.Quantity,
            completed.ExternalReference,
            completed.DestinationStrategyId,
            completed.DestinationVariantId,
            completed.CarryingPrice);

    private static TransferRequestedSummary NormalizeTransferRequested(AccountTransferRequested requested)
        => new(
            requested.TransferId,
            requested.StrategyId,
            requested.VariantId,
            requested.TransferType,
            requested.CashAmount,
            requested.Instrument,
            requested.Quantity,
            requested.ExternalReference,
            requested.DestinationStrategyId,
            requested.DestinationVariantId,
            requested.CarryingPrice);

    private static TransferCanceledSummary NormalizeTransferCanceled(AccountTransferCanceled canceled)
        => new(
            canceled.TransferId,
            canceled.StrategyId,
            canceled.VariantId,
            canceled.TransferType,
            canceled.CashAmount,
            canceled.Instrument,
            canceled.Quantity,
            canceled.Reason is null ? null : NormalizeReason(canceled.Reason),
            canceled.ExternalReference,
            canceled.DestinationStrategyId,
            canceled.DestinationVariantId,
            canceled.CarryingPrice);

    private static TransferFailureSummary NormalizeTransferFailure(AccountTransferFailed failed)
        => new(
            failed.TransferId,
            failed.StrategyId,
            failed.VariantId,
            failed.TransferType,
            failed.CashAmount,
            failed.Instrument,
            failed.Quantity,
            NormalizeReason(failed.Reason),
            failed.ExternalReference,
            failed.DestinationStrategyId,
            failed.DestinationVariantId,
            failed.CarryingPrice);

    private static string NormalizeReason(string reason)
    {
        if (reason.Contains("Cash withdrawal requires", StringComparison.OrdinalIgnoreCase))
            return "Cash withdrawal requires";
        if (reason.Contains("IOC", StringComparison.OrdinalIgnoreCase))
            return "IOC";
        if (reason.Contains("FOK", StringComparison.OrdinalIgnoreCase))
            return "FOK";
        if (reason.Contains("PostOnly", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("Post-only", StringComparison.OrdinalIgnoreCase))
            return "PostOnly";
        if (reason.Contains("simulation order policy", StringComparison.OrdinalIgnoreCase))
            return reason.Replace("simulation order policy", "replay policy", StringComparison.OrdinalIgnoreCase);

        return reason;
    }

    private static async Task AssertRehypothecationTransferParityAsync(
        AccountTransferId transferId,
        SimulationConfig config)
    {
        var t0 = Instant.FromUnixSeconds(1);
        var transfer = new AccountTransferCommand(
            transferId,
            StrategyId,
            VariantId: 0,
            AccountTransferType.AssetDeposit,
            Instrument: Instrument,
            Quantity: new Qty(3m),
            CarryingPrice: new Price(100m, Currency.USD));

        var replayApplied = false;
        var replayEvents = await RunReplayConnectorAsync(
            [CreateQuote(t0, bid: 100m, ask: 100.05m)],
            config,
            (connector, evt) =>
            {
                if (evt is QuoteReceived && !replayApplied)
                {
                    replayApplied = true;
                    connector.CompleteAccountTransferAsync(transfer, CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        var simulationEvents = RunSimulatedTransfer(transfer, config, Money.USD(1_000_000m));

        var replayCustody = replayEvents.OfType<CustodyPositionSnapshot>().Last();
        var simulationCustody = simulationEvents.OfType<CustodyPositionSnapshot>().Last();
        Assert.Equal(NormalizeCustody(replayCustody), NormalizeCustody(simulationCustody));
    }

    private static Instant GetEventTime(FinanceEvent evt)
        => evt switch
        {
            QuoteReceived quote => quote.Time,
            TradeOccurred trade => trade.Trade.Time.ExchangeTime,
            BarClosed bar => bar.Bar.Time,
            BookSnapshotReceived book => book.Book.Time,
            BookLevelDeltaReceived delta => delta.Time,
            BookLevelDeltasReceived deltas => deltas.Time,
            BookDepthSnapshotReceived depth => depth.Time,
            BookDepth10Received depth => depth.Time,
            _ => evt.Time
        };

    private static async IAsyncEnumerable<FinanceEvent> ReadHistoryAsync(FinanceEvent[] events)
    {
        for (var i = 0; i < events.Length; i++)
        {
            yield return events[i];
            await Task.Yield();
        }
    }

    private readonly record struct FillSummary(
        StrategyId StrategyId,
        OrderId OrderId,
        Side Side,
        Qty Quantity,
        Price Price);

    private readonly record struct FillValueSummary(
        StrategyId StrategyId,
        OrderId OrderId,
        Side Side,
        Qty Quantity,
        decimal PriceValue);

    private readonly record struct FillEconomicSummary(
        StrategyId StrategyId,
        Side Side,
        Qty Quantity,
        decimal PriceValue);

    private readonly record struct FillCommissionSummary(
        StrategyId StrategyId,
        OrderId OrderId,
        Side Side,
        Qty Quantity,
        decimal PriceValue,
        Money Commission);

    private readonly record struct AcceptedSummary(
        StrategyId StrategyId,
        OrderId OrderId);

    private readonly record struct ModifiedSummary(
        StrategyId StrategyId,
        OrderId OrderId,
        Qty? Quantity,
        Price? LimitPrice);

    private readonly record struct CancelledSummary(
        StrategyId StrategyId,
        OrderId OrderId,
        Qty RemainingQuantity);

    private readonly record struct ExpiredSummary(
        StrategyId StrategyId,
        OrderId OrderId);

    private readonly record struct RejectedSummary(
        StrategyId StrategyId,
        OrderId OrderId);

    private readonly record struct OrderStateSummary(
        OrderId OrderId,
        StrategyId StrategyId,
        int VariantId,
        OrderStatus Status,
        Qty? FilledQuantity,
        Qty? RemainingQuantity,
        string? Reason);

    private readonly record struct PerformanceSummary(
        Money Equity,
        Money Cash,
        Money UnrealizedPnL,
        Money RealizedPnL,
        int OpenPositions,
        int OpenOrders);

    private readonly record struct MarginStatusSummary(
        StrategyId StrategyId,
        int VariantId,
        Money Equity,
        Money MaintenanceRequirement,
        bool IsMaintenanceBreached);

    private readonly record struct MarginCallIssuedSummary(
        StrategyId StrategyId,
        int VariantId,
        Money Equity,
        Money MaintenanceRequirement,
        Instant DueAt);

    private readonly record struct MarginCallResolvedSummary(
        StrategyId StrategyId,
        int VariantId,
        Money Equity,
        Money MaintenanceRequirement);

    private readonly record struct RiskLimitSummary(
        string LimitName,
        decimal CurrentValue,
        decimal LimitValue);

    private readonly record struct CorporateActionEffectSummary(
        CorporateActionId CorporateActionId,
        CorporateActionType ActionType,
        StrategyId StrategyId,
        int VariantId,
        Instrument Instrument,
        Qty QuantityBefore,
        Qty QuantityAfter,
        Price AvgEntryPriceBefore,
        Price AvgEntryPriceAfter,
        Money? CashAmount,
        Instant EffectiveAt);

    private readonly record struct FinancingSummary(
        FinancingChargeId FinancingChargeId,
        FinancingChargeType ChargeType,
        StrategyId StrategyId,
        int VariantId,
        Money Amount,
        Instant EffectiveAt,
        Instrument? Instrument,
        Qty Quantity,
        decimal Rate,
        string? ExternalReference);

    private readonly record struct AccountStatementSummary(
        StrategyId StrategyId,
        int VariantId,
        Currency Currency,
        Money Cash,
        Money AvailableCash,
        Money PendingSettlement,
        Money ReservedCash,
        Money MarketValue,
        Money Equity,
        Money UnrealizedPnL,
        Money RealizedPnL,
        int OpenPositions,
        int OpenOrders);

    private readonly record struct SettlementScheduledSummary(
        StrategyId StrategyId,
        int VariantId,
        Money Amount,
        Instant SettlesAt);

    private readonly record struct SettlementReleasedSummary(
        StrategyId StrategyId,
        int VariantId,
        Money Amount,
        Instant SettledAt);

    private readonly record struct SettlementStatusSummary(
        StrategyId StrategyId,
        int VariantId,
        SettlementStatus Status,
        Money Amount,
        Instant SettlesAt,
        Instant StatusAt);

    private readonly record struct AssetDeliveryScheduledSummary(
        StrategyId StrategyId,
        int VariantId,
        Instrument Instrument,
        Qty Quantity,
        Instant DeliversAt);

    private readonly record struct AssetDeliveredSummary(
        StrategyId StrategyId,
        int VariantId,
        Instrument Instrument,
        Qty Quantity,
        Instant DeliveredAt);

    private readonly record struct AssetDeliveryCanceledSummary(
        StrategyId StrategyId,
        int VariantId,
        Instrument Instrument,
        Qty Quantity,
        Instant CanceledAt);

    private readonly record struct AssetDeliveryStatusSummary(
        StrategyId StrategyId,
        int VariantId,
        Instrument Instrument,
        Qty Quantity,
        AssetDeliveryStatus Status,
        Instant DeliversAt,
        Instant StatusAt);

    private readonly record struct CustodySummary(
        StrategyId StrategyId,
        int VariantId,
        Instrument Instrument,
        Qty Quantity,
        Qty SettledQuantity,
        Qty PendingDeliveryQuantity,
        Qty RehypothecatableQuantity,
        Price AvgEntryPrice,
        Price MarkPrice,
        Money MarketValue,
        Money UnrealizedPnL,
        Money RealizedPnL,
        bool IsOpen);

    private readonly record struct TransferStatusSummary(
        AccountTransferId TransferId,
        StrategyId StrategyId,
        int VariantId,
        AccountTransferType TransferType,
        AccountTransferStatus Status,
        Money? CashAmount,
        Instrument? Instrument,
        Qty Quantity,
        string? Reason,
        string? ExternalReference,
        StrategyId? DestinationStrategyId,
        int DestinationVariantId,
        Price? CarryingPrice);

    private readonly record struct TransferCompletedSummary(
        AccountTransferId TransferId,
        StrategyId StrategyId,
        int VariantId,
        AccountTransferType TransferType,
        Money? CashAmount,
        Instrument? Instrument,
        Qty Quantity,
        string? ExternalReference,
        StrategyId? DestinationStrategyId,
        int DestinationVariantId,
        Price? CarryingPrice);

    private readonly record struct TransferRequestedSummary(
        AccountTransferId TransferId,
        StrategyId StrategyId,
        int VariantId,
        AccountTransferType TransferType,
        Money? CashAmount,
        Instrument? Instrument,
        Qty Quantity,
        string? ExternalReference,
        StrategyId? DestinationStrategyId,
        int DestinationVariantId,
        Price? CarryingPrice);

    private readonly record struct TransferCanceledSummary(
        AccountTransferId TransferId,
        StrategyId StrategyId,
        int VariantId,
        AccountTransferType TransferType,
        Money? CashAmount,
        Instrument? Instrument,
        Qty Quantity,
        string? Reason,
        string? ExternalReference,
        StrategyId? DestinationStrategyId,
        int DestinationVariantId,
        Price? CarryingPrice);

    private readonly record struct TransferFailureSummary(
        AccountTransferId TransferId,
        StrategyId StrategyId,
        int VariantId,
        AccountTransferType TransferType,
        Money? CashAmount,
        Instrument? Instrument,
        Qty Quantity,
        string Reason,
        string? ExternalReference,
        StrategyId? DestinationStrategyId,
        int DestinationVariantId,
        Price? CarryingPrice);

    private sealed class TestEventPublisher : IEventPublisher
    {
        private readonly List<Event> _events = [];

        public Action<Event>? OnEmit { get; set; }

        public void Emit(Event evt)
        {
            _events.Add(evt);
            OnEmit?.Invoke(evt);
        }

        public ValueTask EmitAsync(Event evt, CancellationToken ct = default)
        {
            Emit(evt);
            return ValueTask.CompletedTask;
        }

        public Event[] ToArray()
        {
            var copied = new Event[_events.Count];
            for (var i = 0; i < _events.Count; i++)
                copied[i] = _events[i];
            return copied;
        }
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

    private sealed class ClosingSimulationModule : ISessionSimulationModule
    {
        private readonly Instrument _instrument;
        private readonly Instant _closeAt;
        private bool _emitted;

        public ClosingSimulationModule(Instrument instrument, Instant closeAt)
        {
            _instrument = instrument;
            _closeAt = closeAt;
        }

        public int PreProcessCount { get; private set; }
        public int ProcessCount { get; private set; }

        public void Reset()
        {
            _emitted = false;
        }

        public void PreProcess(
            in FinanceEvent evt,
            ref SimulationModuleContext context,
            ref SimulationModuleSinks sinks)
        {
            if (evt is QuoteReceived)
                PreProcessCount++;
        }

        public void Process(
            Instant now,
            ref SimulationModuleContext context,
            ref SimulationModuleSinks sinks)
        {
            ProcessCount++;
            if (_emitted || now != _closeAt)
                return;

            _emitted = true;
            sinks.Events.Emit(new InstrumentStatusChanged(_instrument, MarketStatus.Closed, "module close")
            {
                Time = now
            });
        }

        public void AppendDiagnostics(ref SimulationDiagnosticsBuilder diagnostics)
        {
        }
    }

    private sealed partial class ParityUniverseStrategy : Strategy
    {
        protected override void OnInitialize(in SetupContext setup)
            => setup.AddEquity("AAPL");
    }

    private sealed class ModuleParityBuyOnSecondQuoteStrategy : Strategy
    {
        private static int s_quoteCount;

        public static void Reset()
            => s_quoteCount = 0;

        protected override void OnInitialize(in SetupContext setup)
            => setup.AddEquity("AAPL");

        protected override void __GeneratedRunQuote(
            in MarketKernel market,
            ref PortfolioContext portfolio,
            QuoteReceived evt,
            int assetRangeStart,
            int assetRangeLength)
        {
            s_quoteCount++;
            if (s_quoteCount == 2)
                portfolio.Buy(new AssetId(assetRangeStart), new Qty(1m), Execution.Market());
        }
    }

    private sealed class SnapshotBuyOnceStrategy : Strategy
    {
        private static bool s_submitted;

        public static void Reset()
            => s_submitted = false;

        protected override void OnInitialize(in SetupContext setup)
            => setup.AddEquity("AAPL");

        protected override void __GeneratedRunQuote(
            in MarketKernel market,
            ref PortfolioContext portfolio,
            QuoteReceived evt,
            int assetRangeStart,
            int assetRangeLength)
        {
            if (s_submitted)
                return;

            s_submitted = true;
            portfolio.Buy(new AssetId(assetRangeStart), new Qty(1m), Execution.Market());
        }
    }

    private sealed class SnapshotRoundTripStrategy : Strategy
    {
        private static int s_quoteCount;

        public static void Reset()
            => s_quoteCount = 0;

        protected override void OnInitialize(in SetupContext setup)
            => setup.AddEquity("AAPL");

        protected override void __GeneratedRunQuote(
            in MarketKernel market,
            ref PortfolioContext portfolio,
            QuoteReceived evt,
            int assetRangeStart,
            int assetRangeLength)
        {
            s_quoteCount++;
            if (s_quoteCount == 1)
            {
                portfolio.Buy(new AssetId(assetRangeStart), new Qty(1m), Execution.Market());
                return;
            }

            if (s_quoteCount == 2)
                portfolio.Sell(new AssetId(assetRangeStart), new Qty(1m), Execution.Market());
        }
    }
}
