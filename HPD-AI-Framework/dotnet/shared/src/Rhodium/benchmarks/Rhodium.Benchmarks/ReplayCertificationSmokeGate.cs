using System.Diagnostics;
using System.Text.Json;
using HPD.Events;
using HPD.Events.Core;
using Rhodium.Connectivity;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Primitives;
using Rhodium.Simulation;

namespace Rhodium.Benchmarks;

public static class ReplayCertificationSmokeGate
{
    private const int ReportVersion = 1;
    private const string GateName = "replay-certification-smoke";
    private static readonly Instrument TestInstrument = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        var reportPath = GetOptionValue(args, "--replay-certification-report");
        var certificationRunId = GetOptionValue(args, "--certification-run-id") ?? Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        var scenarioResults = new List<ReplayCertificationScenarioResult>
        {
            CheckBundledCalendarDataset(),
            await CheckInternalCashTransferAsync(),
            await CheckReduceToMaintenanceLiquidationAsync(),
            await CheckCorporateActionsAsync(),
            await CheckFinancingChargesAsync(),
            await CheckCrossVenueDiagnosticsAsync(),
            await CheckCrossVenueSweepRoutingAsync(),
            await CheckProviderPolicyFeedsAsync()
        };
        stopwatch.Stop();

        var passed = scenarioResults.All(static result => result.Passed);
        if (reportPath is not null)
            WriteReport(reportPath, certificationRunId, stopwatch.Elapsed, passed, scenarioResults);

        if (!passed)
        {
            foreach (var failure in scenarioResults.Where(static result => !result.Passed))
                Console.Error.WriteLine($"{failure.Name}: {failure.Failure}");

            return 1;
        }

        Console.WriteLine($"Replay certification smoke gate passed: {scenarioResults.Count:n0} scenarios in {stopwatch.Elapsed}.");
        if (reportPath is not null)
            Console.WriteLine($"Replay certification report: {reportPath}");
        return 0;
    }

    private static ReplayCertificationScenarioResult CheckBundledCalendarDataset()
    {
        var calendar = ClearingCalendarCatalog.ForVenue(Venue.NYSE, 2025);
        return calendar.IsBusinessDay(new DateOnly(2025, 1, 9))
            ? ReplayCertificationScenarioResult.Fail("Bundled calendar dataset", "NYSE 2025-01-09 special closure was treated as a business day.")
            : ReplayCertificationScenarioResult.Pass(
                "Bundled calendar dataset",
                ("venue", Venue.NYSE.ToString()),
                ("date", "2025-01-09"),
                ("business_day", "false"),
                ("dataset", "bundled US market special closures"));
    }

    private static async Task<ReplayCertificationScenarioResult> CheckInternalCashTransferAsync()
    {
        var source = new StrategyId(7);
        var destination = new StrategyId(8);
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes((100m, 100.05m)),
            initialCash: Money.USD(100m));
        var events = new CapturingEventPublisher();
        var command = AccountTransferCommand.InternalCashTransfer(
            source,
            destination,
            Money.USD(25m),
            externalReference: "replay-certification");
        var submitted = false;
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.CompleteAccountTransferAsync(command, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync([new Subscription(TestInstrument, SubscriptionType.Quotes)], events, CancellationToken.None);

        var completed = events.EmittedEvents.OfType<AccountTransferCompleted>().SingleOrDefault();
        var sourceStatement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .LastOrDefault(statement => statement.StrategyId == source);
        var destinationStatement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .LastOrDefault(statement => statement.StrategyId == destination);

        if (completed is null)
            return ReplayCertificationScenarioResult.Fail("Internal cash transfer", "No AccountTransferCompleted event was emitted.");

        if (completed.DestinationStrategyId != destination)
            return ReplayCertificationScenarioResult.Fail("Internal cash transfer", "Completed transfer did not preserve destination account identity.");

        if (sourceStatement.Cash != Money.USD(75m) || destinationStatement.Cash != Money.USD(125m))
            return ReplayCertificationScenarioResult.Fail(
                "Internal cash transfer",
                $"Unexpected balances: source={sourceStatement.Cash.Amount}, destination={destinationStatement.Cash.Amount}.");

        return ReplayCertificationScenarioResult.Pass(
            "Internal cash transfer",
            ("source_strategy_id", source.Value.ToString()),
            ("destination_strategy_id", destination.Value.ToString()),
            ("transfer_amount", command.CashAmount?.Amount.ToString("0.##########") ?? string.Empty),
            ("source_cash", sourceStatement.Cash.Amount.ToString("0.##########")),
            ("destination_cash", destinationStatement.Cash.Amount.ToString("0.##########")),
            ("completed_events", "1"));
    }

    private static async Task<ReplayCertificationScenarioResult> CheckReduceToMaintenanceLiquidationAsync()
    {
        var strategyId = new StrategyId(7);
        var t0 = new Instant(1_700_000_000_000_000_000L);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(10m)
                .WithLiquidationPolicy(LiquidationPolicy.CancelOpenOrdersAndReduceToMaintenance)
        };
        var connector = new ReplayConnector(
            CreateHistoryWithTimedQuotes(
                (t0, 100m, 100.05m),
                (t0 + Duration.FromSeconds(30), 83m, 83.05m)),
            config,
            initialCash: Money.USD(100m));
        var events = new CapturingEventPublisher();
        var submitted = false;
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.SubmitOrderAsync(
                    SubmitOrder.Market(strategyId, TestInstrument, Side.Buy, new Qty(5m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync([new Subscription(TestInstrument, SubscriptionType.Quotes)], events, CancellationToken.None);

        var fills = events.EmittedEvents.OfType<OrderFilled>().ToArray();
        if (fills.Length != 2)
            return ReplayCertificationScenarioResult.Fail("Reduce-to-maintenance liquidation", $"Expected 2 fills, got {fills.Length}.");

        var liquidationFill = fills[1];
        if (liquidationFill.Side != Side.Sell || liquidationFill.FilledQty.Value <= 0m || liquidationFill.FilledQty.Value >= 5m)
        {
            return ReplayCertificationScenarioResult.Fail(
                "Reduce-to-maintenance liquidation",
                $"Unexpected liquidation fill: side={liquidationFill.Side}, qty={liquidationFill.FilledQty.Value}.");
        }

        var finalCustody = events.EmittedEvents
            .OfType<CustodyPositionSnapshot>()
            .LastOrDefault(snapshot => snapshot.StrategyId == strategyId);
        if (finalCustody.Quantity.Value <= 0m || finalCustody.Quantity.Value >= 5m)
        {
            return ReplayCertificationScenarioResult.Fail(
                "Reduce-to-maintenance liquidation",
                $"Expected a reduced open position, got {finalCustody.Quantity.Value}.");
        }

        return ReplayCertificationScenarioResult.Pass(
            "Reduce-to-maintenance liquidation",
            ("initial_order_qty", "5"),
            ("fill_count", fills.Length.ToString()),
            ("liquidation_side", liquidationFill.Side.ToString()),
            ("liquidation_qty", liquidationFill.FilledQty.Value.ToString("0.##########")),
            ("final_open_qty", finalCustody.Quantity.Value.ToString("0.##########")),
            ("liquidation_policy", LiquidationPolicy.CancelOpenOrdersAndReduceToMaintenance.ToString()));
    }

    private static async Task<ReplayCertificationScenarioResult> CheckCorporateActionsAsync()
    {
        var strategyId = new StrategyId(7);
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes((100m, 100.05m)),
            initialCash: Money.USD(100m));
        var events = new CapturingEventPublisher();
        var deposit = AccountTransferCommand.AssetDeposit(
            strategyId,
            TestInstrument,
            new Qty(3m),
            new Price(90m, Currency.USD),
            externalReference: "replay-certification-custody");
        var split = CorporateActionCommand.StockSplit(
            TestInstrument,
            splitRatio: 2m,
            externalReference: "replay-certification-split");
        var dividend = CorporateActionCommand.CashDividend(
            TestInstrument,
            Money.USD(2m),
            externalReference: "replay-certification-dividend");
        var submitted = false;
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                connector.CompleteAccountTransferAsync(deposit, CancellationToken.None).GetAwaiter().GetResult();
                connector.ApplyCorporateActionAsync(split, CancellationToken.None).GetAwaiter().GetResult();
                connector.ApplyCorporateActionAsync(dividend, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync([new Subscription(TestInstrument, SubscriptionType.Quotes)], events, CancellationToken.None);

        var actions = events.EmittedEvents.OfType<CorporateActionApplied>().ToArray();
        if (actions.Length != 2)
            return ReplayCertificationScenarioResult.Fail("Corporate actions", $"Expected 2 applied corporate actions, got {actions.Length}.");

        var custody = events.EmittedEvents
            .OfType<CustodyPositionSnapshot>()
            .LastOrDefault(snapshot => snapshot.StrategyId == strategyId);
        if (custody.Quantity != new Qty(6m) || custody.SettledQuantity != new Qty(6m) || custody.AvgEntryPrice.Value != 45m)
        {
            return ReplayCertificationScenarioResult.Fail(
                "Corporate actions",
                $"Unexpected split-adjusted custody: qty={custody.Quantity.Value}, settled={custody.SettledQuantity.Value}, avg={custody.AvgEntryPrice.Value}.");
        }

        var statement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .LastOrDefault(snapshot => snapshot.StrategyId == strategyId);
        if (statement.Cash != Money.USD(112m))
        {
            return ReplayCertificationScenarioResult.Fail(
                "Corporate actions",
                $"Expected cash 112 after split-adjusted dividend, got {statement.Cash.Amount}.");
        }

        return ReplayCertificationScenarioResult.Pass(
            "Corporate actions",
            ("applied_actions", actions.Length.ToString()),
            ("split_ratio", "2"),
            ("final_qty", custody.Quantity.Value.ToString("0.##########")),
            ("settled_qty", custody.SettledQuantity.Value.ToString("0.##########")),
            ("avg_entry_price", custody.AvgEntryPrice.Value.ToString("0.##########")),
            ("cash_after_dividend", statement.Cash.Amount.ToString("0.##########")));
    }

    private static async Task<ReplayCertificationScenarioResult> CheckFinancingChargesAsync()
    {
        var strategyId = new StrategyId(7);
        var connector = new ReplayConnector(
            CreateHistoryWithQuotes((100m, 100.05m)),
            initialCash: Money.USD(100m));
        var events = new CapturingEventPublisher();
        var commands = FinancingChargeFeed.FromBundledRateCurveFeed("financing-rate-curves");
        var submitted = false;
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                foreach (var command in commands)
                    connector.ApplyFinancingChargeAsync(command, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync([new Subscription(TestInstrument, SubscriptionType.Quotes)], events, CancellationToken.None);

        var charges = events.EmittedEvents.OfType<FinancingChargeApplied>().ToArray();
        if (charges.Length != 3)
            return ReplayCertificationScenarioResult.Fail("Financing charges", $"Expected 3 financing charges, got {charges.Length}.");

        if (charges[0].ChargeType != FinancingChargeType.PerpetualFunding
            || charges[1].ChargeType != FinancingChargeType.BorrowFee)
        {
            return ReplayCertificationScenarioResult.Fail(
                "Financing charges",
                $"Unexpected charge sequence: {string.Join(", ", charges.Select(static charge => charge.ChargeType))}.");
        }

        if (charges[2].ChargeType != FinancingChargeType.CashInterestCredit
            || charges[0].Amount != Money.USD(-12.3400m)
            || charges[1].Amount != Money.USD(-3.500m)
            || charges[2].Amount != Money.USD(1.25000m))
        {
            return ReplayCertificationScenarioResult.Fail(
                "Financing charges",
                $"Unexpected rate-derived amounts: {string.Join(", ", charges.Select(static charge => charge.Amount.Amount))}.");
        }

        var statement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .LastOrDefault(snapshot => snapshot.StrategyId == strategyId);
        if (statement.Cash != Money.USD(85.41000m))
        {
            return ReplayCertificationScenarioResult.Fail(
                "Financing charges",
                $"Expected cash 85.41 after rate-derived financing charges, got {statement.Cash.Amount}.");
        }

        return ReplayCertificationScenarioResult.Pass(
            "Financing charges",
            ("dataset", "financing-rate-curves"),
            ("charge_count", charges.Length.ToString()),
            ("charge_types", string.Join(",", charges.Select(static charge => charge.ChargeType))),
            ("charge_amounts", string.Join(",", charges.Select(static charge => charge.Amount.Amount.ToString("0.##########")))),
            ("ending_cash", statement.Cash.Amount.ToString("0.##########")));
    }

    private static async Task<ReplayCertificationScenarioResult> CheckCrossVenueDiagnosticsAsync()
    {
        var events = new EventBus();
        await using var opportunities = events.CreateInbox<CrossVenueArbitrageOpportunity>(
            EventInboxOptions.Deterministic());
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new SmokeConnector();
        var coinbaseConnector = new SmokeConnector();
        using var host = new TradingHost(
            new Dictionary<Venue, IConnector>
            {
                [Venue.Binance] = binanceConnector,
                [Venue.Coinbase] = coinbaseConnector
            },
            events,
            runtime);
        host.RegisterStrategy<MultiVenueCertificationStrategy>(depth: 0);

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var detectedAt = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(
            binance,
            bid: 99.80m,
            ask: 100.00m,
            bidSize: 5m,
            askSize: 3m,
            detectedAt));
        coinbaseConnector.Events.Add(CreateQuoteReceived(
            coinbase,
            bid: 100.50m,
            ask: 100.70m,
            bidSize: 2m,
            askSize: 4m,
            detectedAt + Duration.FromMillis(1)));

        await host.RunAsync();

        if (!opportunities.Reader.TryRead(out var opportunity))
            return ReplayCertificationScenarioResult.Fail("Cross-venue diagnostics", "No crossed-market opportunity was emitted.");

        if (opportunities.Reader.TryRead(out _))
            return ReplayCertificationScenarioResult.Fail("Cross-venue diagnostics", "Expected exactly one crossed-market opportunity.");

        if (opportunity.Asset != asset
            || opportunity.BuyVenue != Venue.Binance
            || opportunity.SellVenue != Venue.Coinbase
            || opportunity.BuyAsk != new Price(100.00m, Currency.USD)
            || opportunity.SellBid != new Price(100.50m, Currency.USD)
            || opportunity.ExecutableQuantity != new Qty(2m)
            || opportunity.GrossSpreadPerUnit != Money.USD(0.50m))
        {
            return ReplayCertificationScenarioResult.Fail(
                "Cross-venue diagnostics",
                $"Unexpected opportunity: buy={opportunity.BuyVenue}@{opportunity.BuyAsk.Value}, sell={opportunity.SellVenue}@{opportunity.SellBid.Value}, qty={opportunity.ExecutableQuantity.Value}.");
        }

        return ReplayCertificationScenarioResult.Pass(
            "Cross-venue diagnostics",
            ("asset", opportunity.Asset.Symbol),
            ("buy_venue", opportunity.BuyVenue.ToString()),
            ("sell_venue", opportunity.SellVenue.ToString()),
            ("buy_ask", opportunity.BuyAsk.Value.ToString("0.##########")),
            ("sell_bid", opportunity.SellBid.Value.ToString("0.##########")),
            ("executable_qty", opportunity.ExecutableQuantity.Value.ToString("0.##########")),
            ("gross_spread_per_unit", opportunity.GrossSpreadPerUnit.Amount.ToString("0.##########")));
    }

    private static async Task<ReplayCertificationScenarioResult> CheckCrossVenueSweepRoutingAsync()
    {
        var events = new EventBus();
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new SmokeConnector();
        var coinbaseConnector = new SmokeConnector();
        using var host = new TradingHost(
            new Dictionary<Venue, IConnector>
            {
                [Venue.Binance] = binanceConnector,
                [Venue.Coinbase] = coinbaseConnector
            },
            events,
            runtime)
        {
            UseCrossVenueBestMarketRouting = true,
            UseCrossVenueMarketSweepRouting = true
        };
        host.RegisterStrategy<MultiVenueCertificationStrategy>(depth: 0);

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var detectedAt = Instant.FromUnixSeconds(20);
        binanceConnector.Events.Add(CreateQuoteReceived(
            coinbase,
            bid: 98.80m,
            ask: 99.00m,
            bidSize: 10m,
            askSize: 2m,
            detectedAt));
        binanceConnector.Events.Add(CreateQuoteReceived(
            binance,
            bid: 99.70m,
            ask: 100.00m,
            bidSize: 10m,
            askSize: 10m,
            detectedAt + Duration.FromMillis(1)));
        binanceConnector.Events.Add(new Scheduled("cross-venue-sweep-certification"));

        await host.RunAsync();

        if (coinbaseConnector.SubmittedOrders.Count != 1 || binanceConnector.SubmittedOrders.Count != 1)
        {
            return ReplayCertificationScenarioResult.Fail(
                "Cross-venue sweep routing",
                $"Expected one Coinbase slice and one Binance slice, got coinbase={coinbaseConnector.SubmittedOrders.Count}, binance={binanceConnector.SubmittedOrders.Count}.");
        }

        var coinbaseOrder = coinbaseConnector.SubmittedOrders[0];
        var binanceOrder = binanceConnector.SubmittedOrders[0];
        if (coinbaseOrder.Instrument != coinbase
            || binanceOrder.Instrument != binance
            || coinbaseOrder.Quantity != new Qty(2m)
            || binanceOrder.Quantity != new Qty(3m)
            || coinbaseOrder.Side != Side.Buy
            || binanceOrder.Side != Side.Buy
            || coinbaseOrder.Type != OrderType.Market
            || binanceOrder.Type != OrderType.Market)
        {
            return ReplayCertificationScenarioResult.Fail(
                "Cross-venue sweep routing",
                $"Unexpected sweep orders: coinbase={coinbaseOrder.Instrument.Venue}/{coinbaseOrder.Quantity.Value}/{coinbaseOrder.Side}/{coinbaseOrder.Type}, binance={binanceOrder.Instrument.Venue}/{binanceOrder.Quantity.Value}/{binanceOrder.Side}/{binanceOrder.Type}.");
        }

        return ReplayCertificationScenarioResult.Pass(
            "Cross-venue sweep routing",
            ("coinbase_orders", coinbaseConnector.SubmittedOrders.Count.ToString()),
            ("binance_orders", binanceConnector.SubmittedOrders.Count.ToString()),
            ("coinbase_qty", coinbaseOrder.Quantity.Value.ToString("0.##########")),
            ("binance_qty", binanceOrder.Quantity.Value.ToString("0.##########")),
            ("order_type", OrderType.Market.ToString()),
            ("side", Side.Buy.ToString()));
    }

    private static async Task<ReplayCertificationScenarioResult> CheckProviderPolicyFeedsAsync()
    {
        var routingPolicies = VenueRoutingPolicyCatalog.FromBundledPolicyFeed(
            "routing-crypto-spot",
            VenueRoutingPolicyCatalog.CryptoSpot())
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);
        routingPolicies[Venue.Coinbase] = routingPolicies[Venue.Coinbase] with
        {
            AllowMarketSweepRouting = false
        };

        var orderPolicies = ReplayVenueOrderPolicyCatalog.FromBundledPolicyFeed(
            "replay-order-us-listed-equities",
            ReplayVenueOrderPolicyCatalog.USListedEquities())
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);
        orderPolicies[Venue.NASDAQ] = orderPolicies[Venue.NASDAQ] with
        {
            MinOrderNotional = Money.USD(1_000m)
        };

        var replayConnector = new ReplayConnector(CreateHistoryWithQuotes((100m, 100.05m)))
        {
            VenueOrderPolicies = orderPolicies
        };
        var replayEvents = new CapturingEventPublisher();
        var submitted = false;
        replayEvents.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                replayConnector.SubmitOrderAsync(
                    SubmitOrder.Market(new StrategyId(7), TestInstrument, Side.Buy, new Qty(5m)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await replayConnector.StartAsync([new Subscription(TestInstrument, SubscriptionType.Quotes)], replayEvents, CancellationToken.None);

        var rejection = replayEvents.EmittedEvents.OfType<OrderRejected>().SingleOrDefault();
        if (rejection is null || !rejection.Reason.Contains("minimum order notional", StringComparison.OrdinalIgnoreCase))
        {
            return ReplayCertificationScenarioResult.Fail(
                "Provider policy feeds",
                "Replay order policy feed did not reject the sub-minimum notional order.");
        }

        var events = new EventBus();
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new SmokeConnector();
        var coinbaseConnector = new SmokeConnector();
        using var host = new TradingHost(
            new Dictionary<Venue, IConnector>
            {
                [Venue.Binance] = binanceConnector,
                [Venue.Coinbase] = coinbaseConnector
            },
            events,
            runtime)
        {
            UseCrossVenueBestMarketRouting = true,
            UseCrossVenueMarketSweepRouting = true,
            CrossVenueRoutingPolicies = routingPolicies
        };
        host.RegisterStrategy<MultiVenueCertificationStrategy>(depth: 0);

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var detectedAt = Instant.FromUnixSeconds(30);
        binanceConnector.Events.Add(CreateQuoteReceived(
            coinbase,
            bid: 98.80m,
            ask: 99.00m,
            bidSize: 10m,
            askSize: 2m,
            detectedAt));
        binanceConnector.Events.Add(CreateQuoteReceived(
            binance,
            bid: 99.70m,
            ask: 100.00m,
            bidSize: 10m,
            askSize: 10m,
            detectedAt + Duration.FromMillis(1)));
        binanceConnector.Events.Add(new Scheduled("provider-policy-feed-certification"));

        await host.RunAsync();

        if (coinbaseConnector.SubmittedOrders.Count != 0)
        {
            return ReplayCertificationScenarioResult.Fail(
                "Provider policy feeds",
                $"Routing policy feed disabled Coinbase sweep participation, but {coinbaseConnector.SubmittedOrders.Count} Coinbase orders were submitted.");
        }

        var binanceOrder = binanceConnector.SubmittedOrders.SingleOrDefault();
        if (binanceOrder.Instrument != binance || binanceOrder.Quantity != new Qty(5m))
        {
            return ReplayCertificationScenarioResult.Fail(
                "Provider policy feeds",
                $"Expected full residual sweep to Binance after Coinbase exclusion, got {binanceOrder.Instrument}/{binanceOrder.Quantity.Value}.");
        }

        return ReplayCertificationScenarioResult.Pass(
            "Provider policy feeds",
            ("routing_dataset", "routing-crypto-spot"),
            ("order_policy_dataset", "replay-order-us-listed-equities"),
            ("rejection_reason", rejection.Reason),
            ("coinbase_sweep_allowed", "false"),
            ("coinbase_orders", coinbaseConnector.SubmittedOrders.Count.ToString()),
            ("binance_qty", binanceOrder.Quantity.Value.ToString("0.##########")));
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

    private static QuoteReceived CreateQuoteReceived(
        Instrument instrument,
        decimal bid,
        decimal ask,
        decimal bidSize,
        decimal askSize,
        Instant detectedAt)
    {
        return new QuoteReceived(
            instrument,
            new Quote(
                new Price(bid, Currency.USD),
                new Price(ask, Currency.USD),
                new Qty(bidSize),
                new Qty(askSize),
                DualTimestamp.Synchronized(detectedAt)));
    }

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

    private static string? GetOptionValue(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static void WriteReport(
        string path,
        string certificationRunId,
        TimeSpan elapsed,
        bool passed,
        IReadOnlyList<ReplayCertificationScenarioResult> scenarios)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new ReplayCertificationReport(
                    ReportVersion,
                    GateName,
                    certificationRunId,
                    SmokeReportEnvironment.Create(),
                    elapsed,
                    passed,
                    scenarios),
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class CapturingEventPublisher : IEventPublisher
    {
        public List<Event> EmittedEvents { get; } = [];
        public Action<Event>? OnEmit { get; set; }

        public void Emit(Event evt)
        {
            EmittedEvents.Add(evt);
            OnEmit?.Invoke(evt);
        }

        public ValueTask EmitAsync(Event evt, CancellationToken ct = default)
        {
            Emit(evt);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SmokeConnector : IConnector
    {
        public ExchangeId Exchange => ExchangeId.Replay;
        public IRateLimiter RateLimiter => NoopRateLimiter.Instance;
        public bool IsConnected { get; private set; }
        public List<Subscription> Subscriptions { get; } = [];
        public List<FinanceEvent> Events { get; } = [];
        public List<SubmitOrder> SubmittedOrders { get; } = [];

        public Task StartAsync(IEnumerable<Subscription> subscriptions, IEventPublisher events, CancellationToken ct)
        {
            Subscriptions.Clear();
            Subscriptions.AddRange(subscriptions);
            IsConnected = true;
            foreach (var evt in Events)
                events.Emit(evt);

            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task SubmitOrderAsync(SubmitOrder command, CancellationToken ct)
        {
            SubmittedOrders.Add(command);
            return Task.CompletedTask;
        }

        public Task CancelOrderAsync(CancelOrder command, CancellationToken ct) => Task.CompletedTask;

        public Task ModifyOrderAsync(ModifyOrder command, CancellationToken ct) => Task.CompletedTask;

        public void Dispose()
        {
            IsConnected = false;
        }
    }

    private sealed record ReplayCertificationReport(
        int ReportVersion,
        string GateName,
        string CertificationRunId,
        SmokeReportEnvironment Environment,
        TimeSpan Elapsed,
        bool Passed,
        IReadOnlyList<ReplayCertificationScenarioResult> Scenarios);

    private sealed record ReplayCertificationScenarioResult(
        string Name,
        bool Passed,
        string? Failure,
        IReadOnlyDictionary<string, string> Evidence)
    {
        public static ReplayCertificationScenarioResult Pass(
            string name,
            params (string Key, string Value)[] evidence)
        {
            return new(
                name,
                true,
                null,
                evidence.ToDictionary(static item => item.Key, static item => item.Value));
        }

        public static ReplayCertificationScenarioResult Fail(string name, string failure)
            => new(name, false, failure, new Dictionary<string, string>());
    }
}

internal sealed class MultiVenueCertificationStrategy : Strategy
{
    private AssetId _binance;
    private bool _submitted;

    protected override void OnInitialize(in SetupContext setup)
    {
        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        _binance = setup.AddInstrument(new Instrument(asset, Venue.Binance));
        setup.AddInstrument(new Instrument(asset, Venue.Coinbase));
    }

    protected override void OnScheduled(ref TimerContext timer)
    {
        if (_submitted)
            return;

        _submitted = true;
        timer.Buy(_binance, new Qty(5m), Execution.Market());
    }
}
