using HPD.Events;
using HPD.Events.Core;
using Rhodium.Events;
using Rhodium.Platform;
using Rhodium.Platform.Attributes;
using Rhodium.Primitives;
using Rhodium.Simulation.Data;
using Rhodium.Simulation.Diagnostics;
using Rhodium.Simulation.Exchange;
using Rhodium.Simulation.Frames;
using Rhodium.Simulation.Identity;
using Rhodium.Simulation.Modules;
using System.Runtime.CompilerServices;

namespace Rhodium.Simulation.Tests;

public sealed class SimulationSessionArchitectureTests
{
    [Fact]
    public void SimulationSource_DoesNotExposeRemovedExecutionModelArchitecture()
    {
        var sourceRoot = GetRhodiumSimulationSourceRoot();
        var removedNames = new[]
        {
            "ISimulationExecutionModel",
            "QueueExecutionModel",
            "VectorExecutionModel",
            "SimulationExecutionContext",
            "SimulationExecutionModelBase",
            "SimulationRuntime",
            "SimulationFidelity"
        };

        var matches = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifactPath(path))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return removedNames
                    .Where(name => source.Contains(name, StringComparison.Ordinal))
                    .Select(name => $"{Path.GetRelativePath(sourceRoot, path)} contains {name}");
            })
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void SimulationSession_RoutesStrategyIntentThroughSimulatedExchange()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionMarketBuyStrategy>();

        var result = session.Run(SharedHistory.Load([CreateBarClosed(123m)]));

        Assert.Single(result.OrderIntents);
        Assert.Empty(result.ExecutionEvents.OfType<OrderAccepted>());
        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new ExecutionId(1), fill.ExecutionId);
        Assert.Equal(new VenueOrderId(1), fill.VenueOrderId);
        Assert.Equal(new Price(123m, Currency.USD), fill.FillPrice);
        Assert.Equal(new Qty(1m), fill.FilledQty);
        Assert.Single(result.Diagnostics.Venues);
        Assert.Equal(Venue.NASDAQ, result.Diagnostics.Venues[0].Venue);
        Assert.Equal(Currency.USD, result.Diagnostics.Venues[0].BaseCurrency);
        Assert.Equal(1, result.Diagnostics.Venues[0].SubmittedCommands);
        Assert.Equal(1, result.Diagnostics.Venues[0].FilledOrders);
        Assert.Equal(Money.USD(99_877m), result.Diagnostics.Venues[0].Cash);
        Assert.Equal(Money.USD(0m), result.Diagnostics.Venues[0].PendingSettlement);
        Assert.Equal(0, result.Diagnostics.Venues[0].PendingSettlementCount);
        Assert.Equal(Qty.Zero, result.Diagnostics.Venues[0].PendingAssetDeliveryQuantity);
        Assert.Equal(0, result.Diagnostics.Venues[0].PendingAssetDeliveryCount);
        Assert.Equal(SimulationOrderPolicy.Default, result.Diagnostics.Venues[0].OrderPolicy);
        Assert.Equal(SimulationVenuePolicy.Default, result.Diagnostics.Venues[0].SimulationPolicy);
        var position = Assert.Single(result.Runs.Single().FinalSnapshot.GetPositions().ToArray());
        Assert.Equal(Instant.FromUnixSeconds(1), position.OpenedAt);
    }

    [Fact]
    public void SimulationIdentityGenerator_GeneratesDeterministicScopedIds()
    {
        var identity = new SimulationIdentityGenerator();
        var spy = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var ibm = new Instrument(new Asset("IBM", AssetClass.Equity), Venue.NYSE);
        var strategyId = new StrategyId(7);

        Assert.Equal(new OrderId(1), identity.NextClientOrderId());
        Assert.Equal(new OrderId(2), identity.NextClientOrderId());
        Assert.Equal(new VenueOrderId(1), identity.NextVenueOrderId(spy));
        Assert.Equal(new VenueOrderId(2), identity.NextVenueOrderId(spy));
        Assert.Equal(new VenueOrderId(1), identity.NextVenueOrderId(ibm));
        Assert.Equal(new ExecutionId(1), identity.NextExecutionId(spy));
        Assert.Equal(new ExecutionId(1), identity.NextExecutionId(ibm));
        Assert.Equal(new PositionId(1), identity.NextPositionId(spy, strategyId));

        identity.Reset();

        Assert.Equal(new OrderId(1), identity.NextClientOrderId());
        Assert.Equal(new VenueOrderId(1), identity.NextVenueOrderId(spy));
        Assert.Equal(new ExecutionId(1), identity.NextExecutionId(spy));
        Assert.Equal(new PositionId(1), identity.NextPositionId(spy, strategyId));
    }

    [Fact]
    public async Task SimulationSession_RunAsync_ConsumesReplayStream()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionMarketBuyStrategy>();

        var result = await session.RunAsync(ReadEventsAsync([CreateBarClosed(123m)]));

        Assert.Single(result.OrderIntents);
        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
    }

    [Fact]
    public void SimulationSession_EmptySharedHistoryCompletesWithoutReplayEffects()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        var result = session.Run(SharedHistory.Load(Array.Empty<FinanceEvent>()));

        Assert.Null(result.Diagnostics.Timing.ReplayStart);
        Assert.Null(result.Diagnostics.Timing.ReplayEnd);
        Assert.Equal(0, result.Diagnostics.Timing.ReplayEventCount);
        Assert.Empty(result.OrderIntents);
        Assert.Empty(result.ExecutionEvents);
        Assert.Empty(result.AccountStatements);
        Assert.Empty(result.SimulatorEvents);
        Assert.Empty(result.Diagnostics.Venues);
        Assert.Single(result.Diagnostics.DataSources);
    }

    [Fact]
    public async Task SimulationSession_EmptyAsyncReplayCompletesWithoutReplayEffects()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        var result = await session.RunAsync(ReadEventsAsync([]));

        Assert.Null(result.Diagnostics.Timing.ReplayStart);
        Assert.Null(result.Diagnostics.Timing.ReplayEnd);
        Assert.Equal(0, result.Diagnostics.Timing.ReplayEventCount);
        Assert.Empty(result.OrderIntents);
        Assert.Empty(result.ExecutionEvents);
        Assert.Empty(result.AccountStatements);
        Assert.Empty(result.SimulatorEvents);
        Assert.Empty(result.Diagnostics.Venues);
        var source = Assert.Single(result.Diagnostics.DataSources);
        Assert.Equal("async-replay", source.SourceKind);
    }

    [Fact]
    public async Task SimulationSession_RunAsyncHonorsCancellationBeforeReplayEffects()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionMarketBuyStrategy>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.RunAsync(ThrowIfCancellationIsObservedAsync(cts.Token), ct: cts.Token));
    }

    [Fact]
    public void SimulationSession_RawReplayMarketEventsAreProjectedNotRetainedAsSimulatorEvents()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();
        var history = SharedHistory.Load([CreateBarClosed(123m)]);

        var result = session.Run(history);

        Assert.Equal(Instant.FromUnixSeconds(1), result.Diagnostics.Timing.ReplayStart);
        Assert.Equal(Instant.FromUnixSeconds(1), result.Diagnostics.Timing.ReplayEnd);
        Assert.Equal(1, result.Diagnostics.Timing.ReplayEventCount);
        Assert.Empty(result.SimulatorEvents.OfType<BarClosed>());
        Assert.Empty(result.ExecutionEvents);
    }

    [Fact]
    public async Task SimulationSession_RunAsync_StopsStreamingAtReadLimit()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionMarketBuyStrategy>();

        var result = await session.RunAsync(
            ThrowIfSecondEventIsReadAsync(),
            new SimulationRunOptions
            {
                ReadOptions = ReplayReadOptions.All with { Limit = 1 }
            });

        Assert.Single(result.OrderIntents);
        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(1, result.Diagnostics.Timing.ReplayEventCount);
    }

    [Fact]
    public async Task SimulationDataIterator_MergesSourcesWithFinanceOrderingAndPreservesProvenance()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var time = Instant.FromUnixSeconds(1);
        var plan = SimulationDataPlan
            .Create()
            .AddSource("bars", [CreateBarClosed(123m, unixSeconds: 1)], priority: 10)
            .AddSource(
                "status",
                [new VenueStatusChanged(Venue.NASDAQ, MarketStatus.Closed, "planned close") { Time = time }],
                priority: 0)
            .AddSource(
                "quotes",
                [new QuoteReceived(
                    instrument,
                    new Quote(
                        new Price(122m, Currency.USD),
                        new Price(124m, Currency.USD),
                        new Qty(100m),
                        new Qty(100m),
                        DualTimestamp.Synchronized(time)))],
                priority: 5);

        var iterator = new SimulationDataIterator(plan);
        var ordered = await SharedHistory.LoadAsync(iterator.ReadAsync());

        Assert.IsType<VenueStatusChanged>(ordered.Span[0]);
        Assert.IsType<QuoteReceived>(ordered.Span[1]);
        Assert.IsType<BarClosed>(ordered.Span[2]);

        var result = await session.RunAsync(iterator);

        Assert.Empty(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Empty(result.ExecutionEvents.OfType<OrderRejected>());
        Assert.Collection(
            result.Diagnostics.DataSources,
            source =>
            {
                Assert.Equal("bars", source.SourceId);
                Assert.Equal(10, source.Priority);
                Assert.Equal(0, source.SourceOrdinal);
            },
            source =>
            {
                Assert.Equal("status", source.SourceId);
                Assert.Equal(0, source.Priority);
                Assert.Equal(1, source.SourceOrdinal);
            },
            source =>
            {
                Assert.Equal("quotes", source.SourceId);
                Assert.Equal(5, source.Priority);
                Assert.Equal(2, source.SourceOrdinal);
            });
    }

    [Fact]
    public void SimulationSession_AppliesRunReadOptionsToSharedHistory()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();
        var options = new SimulationRunOptions
        {
            ReadOptions = new ReplayReadOptions(
                From: Instant.FromUnixSeconds(2).ToDateTimeOffset(),
                To: Instant.FromUnixSeconds(5).ToDateTimeOffset(),
                EventFlowId: "selected",
                Limit: 2)
        };

        var result = session.Run(
            SharedHistory.Load(
            [
                CreateBarClosed(100m, unixSeconds: 1) with { EventFlowId = "selected" },
                CreateBarClosed(101m, unixSeconds: 2) with { EventFlowId = "selected" },
                CreateBarClosed(102m, unixSeconds: 3) with { EventFlowId = "other" },
                CreateBarClosed(103m, unixSeconds: 4) with { EventFlowId = "selected" },
                CreateBarClosed(104m, unixSeconds: 5) with { EventFlowId = "selected" }
            ]),
            options);

        Assert.Equal(Instant.FromUnixSeconds(2), result.Diagnostics.Timing.ReplayStart);
        Assert.Equal(Instant.FromUnixSeconds(4), result.Diagnostics.Timing.ReplayEnd);
        Assert.Equal(2, result.Diagnostics.Timing.ReplayEventCount);
        var source = Assert.Single(result.Diagnostics.DataSources);
        Assert.Equal(options.ReadOptions.From, source.From);
        Assert.Equal(options.ReadOptions.To, source.To);
        Assert.Equal(options.ReadOptions.EventFlowId, source.EventFlowId);
        Assert.Equal(options.ReadOptions.Limit, source.Limit);
    }

    [Fact]
    public void SimulationSession_ReadOptionsLimitZeroSkipsSharedHistoryEvents()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        var result = session.Run(
            SharedHistory.Load(
            [
                CreateBarClosed(100m, unixSeconds: 1),
                CreateBarClosed(101m, unixSeconds: 2)
            ]),
            new SimulationRunOptions
            {
                ReadOptions = new ReplayReadOptions(null, null, null, Limit: 0)
            });

        Assert.Null(result.Diagnostics.Timing.ReplayStart);
        Assert.Null(result.Diagnostics.Timing.ReplayEnd);
        Assert.Equal(0, result.Diagnostics.Timing.ReplayEventCount);
        Assert.Empty(result.OrderIntents);
        Assert.Empty(result.ExecutionEvents);
    }

    [Fact]
    public async Task SimulationSession_ComposesRunReadOptionsBeforeDataIteratorLimit()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();
        var plan = SimulationDataPlan
            .Create(new ReplayReadOptions(null, null, null, Limit: 1))
            .AddSource(
                "bars",
                [
                    CreateBarClosed(100m, unixSeconds: 1),
                    CreateBarClosed(101m, unixSeconds: 2),
                    CreateBarClosed(102m, unixSeconds: 3)
                ]);
        var options = new SimulationRunOptions
        {
            ReadOptions = new ReplayReadOptions(
                From: Instant.FromUnixSeconds(2).ToDateTimeOffset(),
                To: null,
                EventFlowId: null,
                Limit: null)
        };

        var result = await session.RunAsync(new SimulationDataIterator(plan), options);

        Assert.Equal(Instant.FromUnixSeconds(2), result.Diagnostics.Timing.ReplayStart);
        Assert.Equal(1, result.Diagnostics.Timing.ReplayEventCount);
        var source = Assert.Single(result.Diagnostics.DataSources);
        Assert.Equal(options.ReadOptions.From, source.From);
        Assert.Equal(1, source.Limit);
    }

    [Fact]
    public async Task ReplaySourceSimulationCatalogAdapter_FiltersQueriesAndExposesMetadata()
    {
        var spy = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var ibm = new Instrument(new Asset("IBM", AssetClass.Equity), Venue.NYSE);
        var range = new DateRange(Instant.FromUnixSeconds(1), Instant.FromUnixSeconds(3));
        var source = new EnumerableReplaySource<FinanceEvent>(
        [
            CreateBarClosed(spy, close: 101m, unixSeconds: 1),
            CreateTradeOccurred(102m, 10m, Side.Buy),
            CreateBarClosed(ibm, close: 201m, unixSeconds: 1),
            CreateBarClosed(spy, close: 123m, unixSeconds: 3)
        ]);
        var catalog = new ReplaySourceSimulationCatalogAdapter(
            source,
            instruments: [spy, ibm],
            availableRanges: new Dictionary<(Instrument Instrument, Type DataType), DateRange>
            {
                [(spy, typeof(Bar))] = range
            });

        var catalogSource = catalog.CreateReplaySource(new SimulationDataQuery(
            [spy],
            range,
            SimulationDataKind.Bars));
        var history = await SharedHistory.LoadAsync(catalogSource.ReadAsync(HPD.Events.ReplayReadOptions.All));
        var events = history.Span.ToArray();

        var bar = Assert.Single(events.OfType<BarClosed>());
        Assert.Equal(spy, bar.Instrument);
        Assert.Equal(new Price(101m, Currency.USD), bar.Bar.Close);
        Assert.Empty(events.OfType<TradeOccurred>());
        Assert.Equal([spy, ibm], await CollectAsync(catalog.ListInstrumentsAsync()));
        Assert.Equal(range, await catalog.GetAvailableRangeAsync<Bar>(spy));
        Assert.Null(await catalog.GetAvailableRangeAsync<Trade>(spy));
    }

    [Fact]
    public async Task InMemorySimulationCatalog_FiltersSourcesAndBuildsMetadata()
    {
        var spy = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var ibm = new Instrument(new Asset("IBM", AssetClass.Equity), Venue.NASDAQ);
        var catalog = new InMemorySimulationCatalog(
        [
            CreateBarClosed(spy, close: 101m, unixSeconds: 1),
            CreateBarClosed(ibm, close: 201m, unixSeconds: 2),
            CreateBarClosed(spy, close: 103m, unixSeconds: 3)
        ]);

        Assert.Equal([ibm, spy], await CollectAsync(catalog.ListInstrumentsAsync()));
        Assert.Equal(
            new DateRange(Instant.FromUnixSeconds(1), Instant.FromUnixSeconds(3)),
            await catalog.GetAvailableRangeAsync<Bar>(spy));
        Assert.Null(await catalog.GetAvailableRangeAsync<Trade>(spy));

        var plan = SimulationDataPlan
            .Create()
            .AddCatalogSource(
                "memory-spy-bars",
                catalog,
                SimulationDataQuery.ForInstrument(
                    spy,
                    new DateRange(Instant.FromUnixSeconds(2), Instant.FromUnixSeconds(4)),
                    SimulationDataKind.Bars));

        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();
        var result = await session.RunAsync(new SimulationDataIterator(plan));

        Assert.Empty(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(Instant.FromUnixSeconds(3), result.Diagnostics.Timing.ReplayStart);
        Assert.Equal(1, result.Diagnostics.Timing.ReplayEventCount);
        var provenance = Assert.Single(result.Diagnostics.DataSources);
        Assert.Equal("memory-spy-bars", provenance.SourceId);
        Assert.Equal("catalog", provenance.SourceKind);
    }

    [Fact]
    public async Task SimulationDataPlan_CanUseCatalogSources()
    {
        var spy = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var source = new EnumerableReplaySource<FinanceEvent>(
        [
            CreateBarClosed(spy, close: 123m, unixSeconds: 1)
        ]);
        var catalog = new ReplaySourceSimulationCatalogAdapter(source, instruments: [spy]);
        var plan = SimulationDataPlan
            .Create()
            .AddCatalogSource(
                "catalog-bars",
                catalog,
                SimulationDataQuery.ForInstrument(spy, kinds: SimulationDataKind.Bars),
                priority: 4);
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionMarketBuyStrategy>();

        var result = await session.RunAsync(new SimulationDataIterator(plan));

        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        var provenance = Assert.Single(result.Diagnostics.DataSources);
        Assert.Equal("catalog-bars", provenance.SourceId);
        Assert.Equal("catalog", provenance.SourceKind);
        Assert.Equal(4, provenance.Priority);
    }

    [Fact]
    public void SimulationSession_RejectsThroughExchangeWhenVenueIsClosed()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionMarketBuyStrategy>();

        var result = session.Run(
            SharedHistory.Load(
            [
                new VenueStatusChanged(Venue.NASDAQ, MarketStatus.Closed),
                CreateBarClosed(123m)
            ]));

        var rejection = Assert.Single(result.ExecutionEvents.OfType<OrderRejected>());
        Assert.Contains("Closed", rejection.Reason);
        Assert.Empty(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(1, result.Diagnostics.Venues[0].RejectedOrders);
        var diagnostic = Assert.Single(result.Diagnostics.Rejections);
        Assert.Equal(Venue.NASDAQ, diagnostic.Venue);
        Assert.Contains("Closed", diagnostic.Reason);
    }

    [Fact]
    public void SimulationSession_RejectsBuyWhenVenueAccountHasInsufficientCash()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionLargeMarketBuyStrategy>();

        var result = session.Run(
            SharedHistory.Load([CreateBarClosed(123m)]),
            new SimulationRunOptions { InitialCash = Money.USD(100m) });

        var rejection = Assert.Single(result.ExecutionEvents.OfType<OrderRejected>());
        Assert.Contains("Insufficient cash", rejection.Reason);
        Assert.Empty(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(Money.USD(100m), result.Diagnostics.Venues[0].Cash);
        Assert.Equal(Money.USD(100m), result.Diagnostics.Venues[0].AvailableCash);
        Assert.Equal(Money.USD(0m), result.Diagnostics.Venues[0].ReservedCash);
    }

    [Fact]
    public void SimulationSession_AppliesCashTransferToVenueAccountBeforeOrders()
    {
        using var session = new SimulationSession();
        var strategyId = session.RegisterStrategy<SessionMarketBuyStrategy>();
        var transfer = new AccountTransferCompleted(
            new AccountTransferId(1),
            strategyId,
            0,
            AccountTransferType.CashDeposit,
            Money.USD(200m),
            null,
            Qty.Zero,
            Instant.FromUnixSeconds(0),
            Venue: Venue.NASDAQ)
        {
            Time = Instant.FromUnixSeconds(0)
        };

        var result = session.Run(
            SharedHistory.Load([transfer, CreateBarClosed(123m, unixSeconds: 1)]),
            new SimulationRunOptions
            {
                Config = SimulationConfig.Instant(),
                InitialCash = Money.USD(0m)
            });

        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.DoesNotContain(result.ExecutionEvents, static evt => evt is OrderRejected);
        Assert.Contains(result.AccountStatements, statement => statement.Cash == Money.USD(200m));
        Assert.Contains(result.SimulatorEvents.OfType<AccountTransferStatusSnapshot>(), status => status.Status == AccountTransferStatus.Completed);
        Assert.Equal(Money.USD(77m), result.Diagnostics.Venues.Single().Cash);
    }

    [Fact]
    public void SimulationSession_FailsCashTransferWithoutVenueWhenNoVenueAccountExists()
    {
        using var session = new SimulationSession();
        var strategyId = session.RegisterStrategy<SessionNoopUniverseStrategy>();
        var transfer = new AccountTransferCompleted(
            new AccountTransferId(3),
            strategyId,
            0,
            AccountTransferType.CashDeposit,
            Money.USD(200m),
            null,
            Qty.Zero,
            Instant.FromUnixSeconds(0))
        {
            Time = Instant.FromUnixSeconds(0)
        };

        var result = session.Run(SharedHistory.Load([transfer]));

        var failure = Assert.Single(result.SimulatorEvents.OfType<AccountTransferFailed>());
        Assert.Equal(transfer.TransferId, failure.TransferId);
        Assert.Contains("venue", failure.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.AccountStatements);
        Assert.Empty(result.Diagnostics.Venues);
    }

    [Fact]
    public void SimulationSession_AppliesAssetTransferAsSettledCustodyBeforeCashAccountSell()
    {
        using var session = new SimulationSession();
        var strategyId = session.RegisterStrategy<SessionMarketSellStrategy>();
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var transfer = new AccountTransferCompleted(
            new AccountTransferId(2),
            strategyId,
            0,
            AccountTransferType.AssetDeposit,
            null,
            instrument,
            new Qty(1m),
            Instant.FromUnixSeconds(0),
            CarryingPrice: new Price(50m, Currency.USD))
        {
            Time = Instant.FromUnixSeconds(0)
        };

        var result = session.Run(
            SharedHistory.Load([transfer, CreateBarClosed(instrument, 123m, unixSeconds: 1)]),
            new SimulationRunOptions
            {
                Config = SimulationConfig.Instant(),
                InitialCash = Money.USD(0m)
            });

        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(Side.Sell, fill.Side);
        Assert.DoesNotContain(result.ExecutionEvents, static evt => evt is OrderRejected);
        Assert.Contains(result.SimulatorEvents.OfType<AccountTransferStatusSnapshot>(), status => status.Status == AccountTransferStatus.Completed);
        Assert.Empty(result.Runs.Single().FinalSnapshot.GetPositions().ToArray());
        Assert.Equal(Money.USD(123m), result.Diagnostics.Venues.Single().Cash);
    }

    [Fact]
    public void SimulationAccount_MarginAccountAllowsLeveragedBuyWithinInitialMargin()
    {
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(4m)
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(300m));
        var command = CreateCommand(orderId: 40) with
        {
            Quantity = new Qty(10m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderFilled>());
        Assert.Empty(events.OfType<OrderRejected>());
        Assert.Equal(Money.USD(-700m), venue.Account.Cash);
        Assert.Equal(Money.USD(0m), venue.Account.ReservedCash);
    }

    [Fact]
    public void SimulationAccount_MarginAccountRejectsBuyBeyondInitialMargin()
    {
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(4m)
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(200m));
        var command = CreateCommand(orderId: 41) with
        {
            Quantity = new Qty(10m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("Insufficient margin buying power", rejection.Reason);
        Assert.Empty(events.OfType<OrderFilled>());
        Assert.Equal(Money.USD(200m), venue.Account.Cash);
    }

    [Fact]
    public void SimulationAccount_RequireBorrowRejectsShortWithoutInventory()
    {
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.RegT()
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var sell = CreateCommand(orderId: 42) with
        {
            Side = Side.Sell,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(sell, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("requires borrow/locate", rejection.Reason);
        Assert.Empty(events.OfType<OrderFilled>());
    }

    [Fact]
    public void SimulationAccount_RequireBorrowAcceptsShortWithExplicitBorrow()
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.RegT().WithBorrowAvailability(instrument, new Qty(2m))
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var sell = CreateCommand(orderId: 43) with
        {
            Side = Side.Sell,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(instrument, 100m));
        venue.Submit(sell, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderFilled>());
        Assert.Empty(events.OfType<OrderRejected>());
        Assert.Equal(Money.USD(1_100m), venue.Account.Cash);
        Assert.Equal(Money.USD(0m), venue.Account.ReservedCash);
    }

    [Fact]
    public void SimulationSession_EmitsMarginCallAndLiquidatesMaintenanceBreach()
    {
        MarginLiquidationStrategy.Reset();
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(4m)
        };
        var result = Rhodium.Simulate<MarginLiquidationStrategy>()
            .WithHistory(SharedHistory.Load(
            [
                CreateBarClosed(100m, unixSeconds: 1),
                CreateBarClosed(50m, unixSeconds: 2)
            ]))
            .WithConfig(config)
            .WithInitialCash(Money.USD(300m))
            .Run();

        var fills = result.ExecutionEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal(Side.Buy, fills[0].Side);
        Assert.Equal(Side.Sell, fills[1].Side);
        Assert.Equal(new Qty(10m), fills[1].FilledQty);

        Assert.Contains(result.SimulatorEvents.OfType<MarginStatusSnapshot>(), static status => !status.IsMaintenanceBreached);
        Assert.Contains(result.SimulatorEvents.OfType<MarginStatusSnapshot>(), static status => status.IsMaintenanceBreached);
        Assert.Single(result.SimulatorEvents.OfType<MarginCallIssued>());
        Assert.Single(result.SimulatorEvents.OfType<RiskLimitBreached>());
        Assert.Empty(result.Runs.Single().FinalSnapshot.GetPositions().ToArray());
        Assert.Equal(1, MarginLiquidationStrategy.FilledCount);
    }

    [Fact]
    public void SimulationSession_AppliesStockSplitToVenueAccountAndWorldState()
    {
        CorporateActionBuyStrategy.Reset();
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var split = new CorporateActionApplied(
            new CorporateActionId(1),
            CorporateActionType.StockSplit,
            instrument,
            Instant.FromUnixSeconds(2),
            SplitRatio: 2m)
        {
            Time = Instant.FromUnixSeconds(2)
        };

        var result = Rhodium.Simulate<CorporateActionBuyStrategy>()
            .WithHistory(SharedHistory.Load(
            [
                CreateBarClosed(instrument, 100m, unixSeconds: 1),
                split
            ]))
            .WithInitialCash(Money.USD(1_000m))
            .Run();

        var effect = Assert.Single(result.SimulatorEvents.OfType<CorporateActionEffectSnapshot>());
        Assert.Equal(CorporateActionType.StockSplit, effect.ActionType);
        Assert.Equal(new Qty(1m), effect.QuantityBefore);
        Assert.Equal(new Qty(2m), effect.QuantityAfter);
        Assert.Equal(new Price(100m, Currency.USD), effect.AvgEntryPriceBefore);
        Assert.Equal(new Price(50m, Currency.USD), effect.AvgEntryPriceAfter);

        var position = Assert.Single(result.Runs.Single().FinalSnapshot.GetPositions().ToArray());
        Assert.Equal(2m, position.Quantity.Value);
        Assert.Equal(50m, position.AvgEntryPrice.Value);
    }

    [Fact]
    public void SimulationSession_CreditsCashDividendToVenueAccount()
    {
        CorporateActionBuyStrategy.Reset();
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var dividend = new CorporateActionApplied(
            new CorporateActionId(2),
            CorporateActionType.CashDividend,
            instrument,
            Instant.FromUnixSeconds(2),
            DividendPerShare: Money.USD(2m))
        {
            Time = Instant.FromUnixSeconds(2)
        };

        var result = Rhodium.Simulate<CorporateActionBuyStrategy>()
            .WithHistory(SharedHistory.Load(
            [
                CreateBarClosed(instrument, 100m, unixSeconds: 1),
                dividend
            ]))
            .WithInitialCash(Money.USD(1_000m))
            .Run();

        var effect = Assert.Single(result.SimulatorEvents.OfType<CorporateActionEffectSnapshot>());
        Assert.Equal(CorporateActionType.CashDividend, effect.ActionType);
        Assert.Equal(Money.USD(2m), effect.CashAmount);
        Assert.Equal(Money.USD(902m), result.Diagnostics.Venues.Single().Cash);

        var position = Assert.Single(result.Runs.Single().FinalSnapshot.GetPositions().ToArray());
        Assert.Equal(1m, position.Quantity.Value);
        Assert.Equal(100m, position.AvgEntryPrice.Value);
    }

    [Fact]
    public void SimulationVenuePolicy_FrozenAccountRejectsSubmissionsAtVenue()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            simulationPolicy: new SimulationVenuePolicy { FrozenAccount = true });

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(CreateCommand(orderId: 50), Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("frozen", rejection.Reason);
        Assert.Empty(events.OfType<OrderAccepted>());
    }

    [Fact]
    public void SimulationVenuePolicy_DisablesBarExecutionWithoutHidingBarFromStrategy()
    {
        CorporateActionBuyStrategy.Reset();
        var result = Rhodium.Simulate<CorporateActionBuyStrategy>()
            .WithHistory(SharedHistory.Load([CreateBarClosed(100m)]))
            .WithVenue(new SimulationVenueConfig
            {
                Venue = Venue.NASDAQ,
                SimulationPolicy = new SimulationVenuePolicy { BarExecution = false }
            })
            .Run();

        Assert.Single(result.OrderIntents);
        var rejection = Assert.Single(result.ExecutionEvents.OfType<OrderRejected>());
        Assert.Contains("No market price", rejection.Reason);
        Assert.Empty(result.ExecutionEvents.OfType<OrderFilled>());
    }

    [Fact]
    public void SimulationVenuePolicy_DisablesTradeExecutionForRestingOrders()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            simulationPolicy: new SimulationVenuePolicy { TradeExecution = false });
        var command = CreateCommand(orderId: 51) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD))
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);
        venue.OnMarketEvent(CreateTradeOccurred(100m, 1m, Side.Sell));
        var events = DrainEvents(venue);

        Assert.Empty(events.OfType<OrderFilled>());
        Assert.Equal(1, venue.GetOrCreateInstrumentEngine(command.Instrument).OpenOrders);
    }

    [Fact]
    public void SimulationVenuePolicy_ReduceOnlyRejectsExposureIncreasingOrder()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m));
        var reduceOnlyBuy = CreateCommand(orderId: 52) with
        {
            ReduceOnly = true,
            Side = Side.Buy,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(reduceOnlyBuy, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("Reduce-only", rejection.Reason);
        Assert.Empty(events.OfType<OrderFilled>());
    }

    [Fact]
    public void SimulationVenuePolicy_ReduceOnlyAllowsOrderThatReducesExistingExposure()
    {
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(2m)
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var openShort = CreateCommand(orderId: 53) with
        {
            Side = Side.Sell,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };
        var reduceOnlyBuy = CreateCommand(orderId: 54) with
        {
            ReduceOnly = true,
            Side = Side.Buy,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(openShort, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.Submit(reduceOnlyBuy, Instant.FromUnixSeconds(2));
        venue.DrainDueWork(Instant.FromUnixSeconds(2));
        var events = DrainEvents(venue);

        Assert.Empty(events.OfType<OrderAccepted>());
        Assert.Single(events.OfType<OrderFilled>());
        Assert.Empty(events.OfType<OrderRejected>());
        Assert.Equal(0, venue.Account.OpenPositionCount);
    }

    [Fact]
    public void SimulationVenuePolicy_CanDisableReduceOnlyEnforcement()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            simulationPolicy: new SimulationVenuePolicy { UseReduceOnly = false });
        var reduceOnlyBuy = CreateCommand(orderId: 55) with
        {
            ReduceOnly = true,
            Side = Side.Buy,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(reduceOnlyBuy, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        Assert.Empty(events.OfType<OrderAccepted>());
        Assert.Single(events.OfType<OrderFilled>());
        Assert.Empty(events.OfType<OrderRejected>());
    }

    [Fact]
    public void SimulationVenuePolicy_RejectsContingentOrderWhenUnsupported()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            simulationPolicy: new SimulationVenuePolicy { SupportContingentOrders = false });
        var command = CreateCommand(orderId: 56) with
        {
            OrderListId = new OrderListId("bracket-1"),
            ContingencyType = ContingencyType.OCO,
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("contingent", rejection.Reason);
        Assert.Empty(events.OfType<OrderFilled>());
    }

    [Fact]
    public void SimulationVenuePolicy_AllowsContingentOrderByDefault()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m));
        var command = CreateCommand(orderId: 57) with
        {
            OrderListId = new OrderListId("bracket-2"),
            ContingencyType = ContingencyType.OCO,
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        Assert.Empty(events.OfType<OrderAccepted>());
        Assert.Single(events.OfType<OrderFilled>());
        Assert.Empty(events.OfType<OrderRejected>());
    }

    [Fact]
    public void SimulatedInstrumentEngine_CancelsOcoSiblingWhenOneOrderFills()
    {
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(2m)
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var listId = new OrderListId("oco-venue-1");
        var first = CreateCommand(orderId: 69) with
        {
            Side = Side.Sell,
            Quantity = new Qty(1m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OCO
        };
        var second = CreateCommand(orderId: 70) with
        {
            Side = Side.Sell,
            Quantity = new Qty(1m),
            Execution = Execution.Limit().At(new Price(101m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OCO
        };

        venue.OnMarketEvent(CreateBarClosed(90m));
        venue.Submit(first, Instant.FromUnixSeconds(1));
        venue.Submit(second, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.OnMarketEvent(CreateTradeOccurred(100m, 1m, Side.Buy));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(first.ClientOrderId, fill.OrderId);
        var cancelled = Assert.Single(events.OfType<OrderCancelled>());
        Assert.Equal(second.ClientOrderId, cancelled.OrderId);
        Assert.Contains("OCO", cancelled.Reason);
        Assert.Equal(0, venue.GetOrCreateInstrumentEngine(first.Instrument).OpenOrders);
    }

    [Fact]
    public void SimulatedInstrumentEngine_StagesOtoChildrenUntilParentFills()
    {
        var config = SimulationConfig.Instant() with
        {
            FillBehavior = FillBehavior.PartialFillOnTrade,
            QueueModel = QueueParams.AlwaysFront()
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var listId = new OrderListId("oto-venue-1");
        var parent = CreateCommand(orderId: 71) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OTO
        };
        var child = CreateCommand(orderId: 72) with
        {
            Side = Side.Sell,
            Quantity = new Qty(1m),
            Execution = Execution.Limit().At(new Price(110m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OTO
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(parent, Instant.FromUnixSeconds(1));
        venue.Submit(child, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var acceptedBeforeTrigger = DrainEvents(venue).OfType<OrderAccepted>().ToArray();

        Assert.Single(acceptedBeforeTrigger);
        Assert.Equal(parent.ClientOrderId, acceptedBeforeTrigger[0].OrderId);
        Assert.Equal(1, venue.GetOrCreateInstrumentEngine(parent.Instrument).OpenOrders);

        venue.OnMarketEvent(CreateTradeOccurred(100m, 1m, Side.Sell));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(parent.ClientOrderId, fill.OrderId);
        var childAccepted = Assert.Single(events.OfType<OrderAccepted>());
        Assert.Equal(child.ClientOrderId, childAccepted.OrderId);
        Assert.Equal(1, venue.GetOrCreateInstrumentEngine(parent.Instrument).OpenOrders);
    }

    [Fact]
    public void SimulatedInstrumentEngine_OtoFullTriggerWaitsForFullParentFillByDefault()
    {
        var config = SimulationConfig.Instant() with
        {
            FillBehavior = FillBehavior.PartialFillOnTrade,
            QueueModel = QueueParams.AlwaysFront()
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var listId = new OrderListId("oto-venue-2");
        var parent = CreateCommand(orderId: 73) with
        {
            Quantity = new Qty(4m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OTO
        };
        var child = CreateCommand(orderId: 74) with
        {
            Side = Side.Sell,
            Quantity = new Qty(4m),
            Execution = Execution.Limit().At(new Price(110m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OTO
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(parent, Instant.FromUnixSeconds(1));
        venue.Submit(child, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.OnMarketEvent(CreateTradeOccurred(100m, 1m, Side.Sell));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderFilled>());
        Assert.Empty(events.OfType<OrderAccepted>());
        Assert.Equal(1, venue.GetOrCreateInstrumentEngine(parent.Instrument).OpenOrders);
    }

    [Fact]
    public void SimulatedInstrumentEngine_CanTriggerOtoChildrenOnPartialParentFill()
    {
        var config = SimulationConfig.Instant() with
        {
            FillBehavior = FillBehavior.PartialFillOnTrade,
            QueueModel = QueueParams.AlwaysFront()
        };
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            config,
            Money.USD(1_000m),
            simulationPolicy: new SimulationVenuePolicy { OtoFullTrigger = false });
        var listId = new OrderListId("oto-venue-3");
        var parent = CreateCommand(orderId: 75) with
        {
            Quantity = new Qty(4m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OTO
        };
        var child = CreateCommand(orderId: 76) with
        {
            Side = Side.Sell,
            Quantity = new Qty(1m),
            Execution = Execution.Limit().At(new Price(110m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OTO
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(parent, Instant.FromUnixSeconds(1));
        venue.Submit(child, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.OnMarketEvent(CreateTradeOccurred(100m, 1m, Side.Sell));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderFilled>());
        var childAccepted = Assert.Single(events.OfType<OrderAccepted>());
        Assert.Equal(child.ClientOrderId, childAccepted.OrderId);
        Assert.Equal(2, venue.GetOrCreateInstrumentEngine(parent.Instrument).OpenOrders);
    }

    [Fact]
    public void SimulatedInstrumentEngine_UpdatesOuoSiblingQuantityToParentFilledQuantity()
    {
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(2m),
            FillBehavior = FillBehavior.PartialFillOnTrade,
            QueueModel = QueueParams.AlwaysFront()
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var listId = new OrderListId("ouo-venue-1");
        var parent = CreateCommand(orderId: 77) with
        {
            Quantity = new Qty(4m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OUO
        };
        var sibling = CreateCommand(orderId: 78) with
        {
            Side = Side.Sell,
            Quantity = new Qty(4m),
            Execution = Execution.Limit().At(new Price(110m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OUO
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(parent, Instant.FromUnixSeconds(1));
        venue.Submit(sibling, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.OnMarketEvent(CreateTradeOccurred(100m, 1m, Side.Sell));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(parent.ClientOrderId, fill.OrderId);
        var modified = Assert.Single(events.OfType<OrderModified>());
        Assert.Equal(sibling.ClientOrderId, modified.OrderId);
        Assert.Equal(new Qty(1m), modified.NewQuantity);
        Assert.Equal(2, venue.GetOrCreateInstrumentEngine(parent.Instrument).OpenOrders);
    }

    [Fact]
    public void SimulatedInstrumentEngine_UpdatesOuoSiblingQuantityToCumulativeParentFill()
    {
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(2m),
            FillBehavior = FillBehavior.PartialFillOnTrade,
            QueueModel = QueueParams.AlwaysFront()
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var listId = new OrderListId("ouo-venue-2");
        var parent = CreateCommand(orderId: 79) with
        {
            Quantity = new Qty(4m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OUO
        };
        var sibling = CreateCommand(orderId: 80) with
        {
            Side = Side.Sell,
            Quantity = new Qty(4m),
            Execution = Execution.Limit().At(new Price(110m, Currency.USD)),
            OrderListId = listId,
            ContingencyType = ContingencyType.OUO
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(parent, Instant.FromUnixSeconds(1));
        venue.Submit(sibling, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.OnMarketEvent(CreateTradeOccurred(100m, 1m, Side.Sell));
        _ = DrainEvents(venue);
        venue.OnMarketEvent(CreateTradeOccurred(100m, 2m, Side.Sell));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderFilled>());
        var modified = Assert.Single(events.OfType<OrderModified>());
        Assert.Equal(sibling.ClientOrderId, modified.OrderId);
        Assert.Equal(new Qty(3m), modified.NewQuantity);
        Assert.Equal(2, venue.GetOrCreateInstrumentEngine(parent.Instrument).OpenOrders);
    }

    [Fact]
    public void SimulatedVenueExchange_ExecutesTwapAsTimeSlicedMarketOrders()
    {
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, SimulationConfig.Instant(), Money.USD(1_000m));
        var command = CreateCommand(orderId: 81) with
        {
            Quantity = new Qty(3m),
            Execution = Execution.Twap()
                .Over(Duration.FromSeconds(3))
                .Every(Duration.FromSeconds(1))
        };

        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var accepted = Assert.Single(DrainEvents(venue).OfType<OrderAccepted>());
        Assert.Equal(command.ClientOrderId, accepted.OrderId);

        venue.OnMarketEvent(CreateBarClosed(100m, unixSeconds: 2));
        venue.OnMarketEvent(CreateBarClosed(101m, unixSeconds: 3));
        venue.OnMarketEvent(CreateBarClosed(102m, unixSeconds: 4));
        var fills = DrainEvents(venue).OfType<OrderFilled>().ToArray();

        Assert.Equal(3, fills.Length);
        Assert.All(fills, fill => Assert.Equal(command.ClientOrderId, fill.OrderId));
        Assert.All(fills, fill => Assert.Equal(new Qty(1m), fill.FilledQty));
        Assert.Equal([100m, 101m, 102m], fills.Select(fill => fill.FillPrice.Value).ToArray());
    }

    [Fact]
    public void SimulatedVenueExchange_ExecutesVwapFromReplayVolumeParticipation()
    {
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, SimulationConfig.Instant(), Money.USD(1_000m));
        var command = CreateCommand(orderId: 82) with
        {
            Quantity = new Qty(5m),
            Execution = Execution.Vwap()
                .Over(Duration.FromSeconds(10))
                .MaxParticipation(0.5m)
        };

        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.OnMarketEvent(CreateTradeOccurred(100m, 4m, Side.Buy));
        venue.OnMarketEvent(CreateTradeOccurred(101m, 10m, Side.Buy));
        var fills = DrainEvents(venue).OfType<OrderFilled>().ToArray();

        Assert.Equal(2, fills.Length);
        Assert.Equal(new Qty(2m), fills[0].FilledQty);
        Assert.Equal(new Qty(3m), fills[1].FilledQty);
        Assert.All(fills, fill => Assert.Equal(command.ClientOrderId, fill.OrderId));
    }

    [Fact]
    public void SimulatedVenueExchange_ExecutesPovFromReplayVolumeParticipation()
    {
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, SimulationConfig.Instant(), Money.USD(1_000m));
        var command = CreateCommand(orderId: 83) with
        {
            Quantity = new Qty(10m),
            Execution = Execution.Pov(0.25m)
        };

        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.OnMarketEvent(CreateTradeOccurred(100m, 4m, Side.Buy));
        venue.OnMarketEvent(CreateTradeOccurred(101m, 8m, Side.Buy));
        var fills = DrainEvents(venue).OfType<OrderFilled>().ToArray();

        Assert.Equal(2, fills.Length);
        Assert.Equal(new Qty(1m), fills[0].FilledQty);
        Assert.Equal(new Qty(2m), fills[1].FilledQty);
        Assert.All(fills, fill => Assert.Equal(command.ClientOrderId, fill.OrderId));
    }

    [Fact]
    public void SimulatedVenueExchange_CancelsActiveAlgorithmicOrder()
    {
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, SimulationConfig.Instant(), Money.USD(1_000m));
        var command = CreateCommand(orderId: 84) with
        {
            Quantity = new Qty(3m),
            Execution = Execution.Twap()
                .Over(Duration.FromSeconds(3))
                .Every(Duration.FromSeconds(1))
        };

        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);
        venue.Cancel(CreateCancel(84), Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var cancelled = Assert.Single(events.OfType<OrderCancelled>());
        Assert.Equal(command.ClientOrderId, cancelled.OrderId);
        Assert.Equal(new Qty(3m), cancelled.RemainingQty);

        venue.OnMarketEvent(CreateBarClosed(100m, unixSeconds: 2));
        Assert.Empty(DrainEvents(venue).OfType<OrderFilled>());
    }

    [Fact]
    public void SimulatedInstrumentEngine_MarketByOrderConsumesExternalOrdersByPriceTime()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            defaultMatchingFidelity: MatchingFidelity.MarketByOrder);
        var command = CreateCommand(orderId: 85) with
        {
            Quantity = new Qty(3m),
            Execution = Execution.Market()
        };

        ApplyBookOrderAddedFrame(venue, command.Instrument, 1, Side.Sell, 101m, 2m, venueSequence: 1);
        ApplyBookOrderAddedFrame(venue, command.Instrument, 2, Side.Sell, 100m, 1m, venueSequence: 2);
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fills = events.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal(new Qty(1m), fills[0].FilledQty);
        Assert.Equal(new Price(100m, Currency.USD), fills[0].FillPrice);
        Assert.Equal(new Qty(2m), fills[1].FilledQty);
        Assert.Equal(new Price(101m, Currency.USD), fills[1].FillPrice);
        Assert.Empty(events.OfType<OrderCancelled>());
        Assert.InRange(venue.Account.Cash.Amount, 697.999999999999999999999999m, 698.000000000000000000000001m);
        Assert.Equal(Currency.USD, venue.Account.Cash.Currency);
    }

    [Fact]
    public void SimulatedVenueExchange_MarketByOrderConsumesFrameNativeExternalOrders()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            defaultMatchingFidelity: MatchingFidelity.MarketByOrder);
        var command = CreateCommand(orderId: 851) with
        {
            Quantity = new Qty(3m),
            Execution = Execution.Market()
        };

        venue.OnBookOrderAdded(
            command.Instrument,
            new BookOrderAddedFrame(
                InstrumentIndex: 0,
                OrderId: 1,
                Side: Side.Sell,
                PriceTicks: 101_000_000L,
                SizeLots: 2_000_000L,
                VenueSequence: 1,
                TimestampNs: Instant.FromUnixSeconds(1).Nanos));
        venue.OnBookOrderAdded(
            command.Instrument,
            new BookOrderAddedFrame(
                InstrumentIndex: 0,
                OrderId: 2,
                Side: Side.Sell,
                PriceTicks: 100_000_000L,
                SizeLots: 1_000_000L,
                VenueSequence: 2,
                TimestampNs: Instant.FromUnixSeconds(1).Nanos));

        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fills = events.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal(new Qty(1m), fills[0].FilledQty);
        Assert.Equal(new Price(100m, Currency.USD), fills[0].FillPrice);
        Assert.Equal(new Qty(2m), fills[1].FilledQty);
        Assert.Equal(new Price(101m, Currency.USD), fills[1].FillPrice);
        Assert.Empty(events.OfType<OrderCancelled>());
    }

    [Fact]
    public void SimulatedInstrumentEngine_MarketByOrderCancelsUnfilledMarketRemainder()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            defaultMatchingFidelity: MatchingFidelity.MarketByOrder);
        var command = CreateCommand(orderId: 86) with
        {
            Quantity = new Qty(3m),
            Execution = Execution.Market()
        };

        ApplyBookOrderAddedFrame(venue, command.Instrument, 1, Side.Sell, 100m, 1m, venueSequence: 1);
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(new Qty(1m), fill.FilledQty);
        var cancelled = Assert.Single(events.OfType<OrderCancelled>());
        Assert.Equal(command.ClientOrderId, cancelled.OrderId);
        Assert.Equal(new Qty(2m), cancelled.RemainingQty);
        Assert.Contains("replay book liquidity", cancelled.Reason);
    }

    [Fact]
    public void SimulatedInstrumentEngine_MarketByOrderUsesModifiedExternalOrder()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            defaultMatchingFidelity: MatchingFidelity.MarketByOrder);
        var command = CreateCommand(orderId: 861) with
        {
            Quantity = new Qty(3m),
            Execution = Execution.Market()
        };

        ApplyBookOrderAddedFrame(venue, command.Instrument, 1, Side.Sell, 100m, 5m, venueSequence: 1);
        ApplyBookOrderModifiedFrame(venue, command.Instrument, 1, Side.Sell, 101m, 2m, venueSequence: 2);
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(new Qty(2m), fill.FilledQty);
        Assert.Equal(new Price(101m, Currency.USD), fill.FillPrice);
        Assert.Equal(new Qty(1m), Assert.Single(events.OfType<OrderCancelled>()).RemainingQty);
    }

    [Fact]
    public void SimulatedInstrumentEngine_MarketByOrderDeletesExternalOrder()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            defaultMatchingFidelity: MatchingFidelity.MarketByOrder);
        var command = CreateCommand(orderId: 862) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        ApplyBookOrderAddedFrame(venue, command.Instrument, 1, Side.Sell, 100m, 1m, venueSequence: 1);
        ApplyBookOrderAddedFrame(venue, command.Instrument, 2, Side.Sell, 101m, 1m, venueSequence: 2);
        ApplyBookOrderDeletedFrame(venue, command.Instrument, 1, venueSequence: 3);
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(new Qty(1m), fill.FilledQty);
        Assert.Equal(new Price(101m, Currency.USD), fill.FillPrice);
    }

    [Fact]
    public void SimulatedInstrumentEngine_MarketByOrderReducesExecutedExternalOrder()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            defaultMatchingFidelity: MatchingFidelity.MarketByOrder);
        var command = CreateCommand(orderId: 863) with
        {
            Quantity = new Qty(2m),
            Execution = Execution.Market()
        };

        ApplyBookOrderAddedFrame(venue, command.Instrument, 1, Side.Sell, 100m, 3m, venueSequence: 1);
        ApplyBookOrderExecutedFrame(venue, command.Instrument, 1, 2m, venueSequence: 2);
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(new Qty(1m), fill.FilledQty);
        Assert.Equal(new Price(100m, Currency.USD), fill.FillPrice);
        Assert.Equal(new Qty(1m), Assert.Single(events.OfType<OrderCancelled>()).RemainingQty);
    }

    [Fact]
    public void SimulatedInstrumentEngine_MarketByOrderFlatBookGrowsAndKeepsOrderLookup()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(5_000m),
            defaultMatchingFidelity: MatchingFidelity.MarketByOrder);
        var command = CreateCommand(orderId: 864) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        for (var i = 0; i < 1_100; i++)
        {
            ApplyBookOrderAddedFrame(
                venue,
                command.Instrument,
                orderId: i + 1,
                Side.Sell,
                price: 200m + i,
                size: 1m,
                venueSequence: i + 1);
        }

        ApplyBookOrderDeletedFrame(venue, command.Instrument, orderId: 1, venueSequence: 1_101);
        ApplyBookOrderModifiedFrame(venue, command.Instrument, orderId: 2, Side.Sell, price: 99m, size: 1m, venueSequence: 1_102);
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(new Qty(1m), fill.FilledQty);
        Assert.Equal(new Price(99m, Currency.USD), fill.FillPrice);
    }

    [Fact]
    public void SimulatedInstrumentEngine_QueueAccurateMarketOrderFillsFromDepthSnapshotBestAsk()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m));
        var command = CreateCommand(orderId: 87) with
        {
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateDepthSnapshot(
            bids: [new Level(new Price(99m, Currency.USD), new Qty(5m))],
            asks: [new Level(new Price(100m, Currency.USD), new Qty(5m))],
            depth: 1));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(new Price(100m, Currency.USD), fill.FillPrice);
        Assert.Equal(command.AssetId, fill.AssetId);
        Assert.Empty(events.OfType<OrderRejected>());
    }

    [Fact]
    public void SimulatedInstrumentEngine_MarketByOrderConsumesDepthSnapshotTopNAsSyntheticBookOrders()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            defaultMatchingFidelity: MatchingFidelity.MarketByOrder);
        var command = CreateCommand(orderId: 88) with
        {
            Quantity = new Qty(4m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateDepthSnapshot(
            bids: [new Level(new Price(99m, Currency.USD), new Qty(5m))],
            asks:
            [
                new Level(new Price(100m, Currency.USD), new Qty(1m)),
                new Level(new Price(101m, Currency.USD), new Qty(2m)),
                new Level(new Price(102m, Currency.USD), new Qty(5m))
            ],
            depth: 2));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fills = events.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal(new Qty(1m), fills[0].FilledQty);
        Assert.Equal(new Price(100m, Currency.USD), fills[0].FillPrice);
        Assert.Equal(new Qty(2m), fills[1].FilledQty);
        Assert.Equal(new Price(101m, Currency.USD), fills[1].FillPrice);
        var cancelled = Assert.Single(events.OfType<OrderCancelled>());
        Assert.Equal(new Qty(1m), cancelled.RemainingQty);
        Assert.Contains("replay book liquidity", cancelled.Reason);
    }

    [Fact]
    public void SimulationVenuePolicy_CanEmitMarketOrderAckWhenEnabled()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            simulationPolicy: new SimulationVenuePolicy { UseMarketOrderAcks = true });
        var command = CreateCommand(orderId: 60) with
        {
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var accepted = Assert.Single(events.OfType<OrderAccepted>());
        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(accepted.VenueOrderId, fill.VenueOrderId);
        Assert.Equal(command.AssetId, accepted.AssetId);
        Assert.Empty(events.OfType<OrderRejected>());
    }

    [Fact]
    public void SimulationVenuePolicy_AllowsCashBorrowingWhenEnabled()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(50m),
            simulationPolicy: new SimulationVenuePolicy { AllowCashBorrowing = true });
        var command = CreateCommand(orderId: 61) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderFilled>());
        Assert.Empty(events.OfType<OrderRejected>());
        Assert.Equal(Money.USD(-50m), venue.Account.Cash);
    }

    [Fact]
    public void SimulationVenuePolicy_RejectsCashBorrowingByDefault()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(50m));
        var command = CreateCommand(orderId: 62) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("Insufficient cash", rejection.Reason);
        Assert.Empty(events.OfType<OrderFilled>());
        Assert.Equal(Money.USD(50m), venue.Account.Cash);
    }

    [Fact]
    public void SimulationVenuePolicy_RejectsMarketOrderBeyondPriceProtection()
    {
        var config = SimulationConfig.Instant() with
        {
            Slippage = SlippageParams.VolumeProportional(bpsPerLotSize: 10m)
        };
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            config,
            Money.USD(1_000m),
            simulationPolicy: new SimulationVenuePolicy { PriceProtectionTicks = 5 });
        var command = CreateCommand(orderId: 67) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("price protection", rejection.Reason);
        Assert.Empty(events.OfType<OrderFilled>());
        Assert.Equal(Money.USD(1_000m), venue.Account.Cash);
    }

    [Fact]
    public void SimulationVenuePolicy_AllowsMarketOrderWithinPriceProtection()
    {
        var config = SimulationConfig.Instant() with
        {
            Slippage = SlippageParams.VolumeProportional(bpsPerLotSize: 10m)
        };
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            config,
            Money.USD(1_000m),
            simulationPolicy: new SimulationVenuePolicy { PriceProtectionTicks = 10 });
        var command = CreateCommand(orderId: 68) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(new Price(100.1m, Currency.USD), fill.FillPrice);
        Assert.Empty(events.OfType<OrderRejected>());
        Assert.Equal(Money.USD(899.9m), venue.Account.Cash);
    }

    [Fact]
    public void SimulationVenuePolicy_RejectsStopOrderAlreadyTriggeredInCurrentMarket()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            simulationPolicy: new SimulationVenuePolicy { RejectTriggeredOrdersInMarket = true });
        var command = CreateCommand(orderId: 58) with
        {
            Execution = Execution.StopMarket(new Price(101m, Currency.USD))
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("triggered", rejection.Reason);
        Assert.Empty(events.OfType<OrderFilled>());
    }

    [Fact]
    public void SimulationVenuePolicy_CanAllowStopOrderAlreadyTriggeredInCurrentMarket()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            defaultMatchingFidelity: MatchingFidelity.FastVectorApproximation,
            simulationPolicy: new SimulationVenuePolicy { RejectTriggeredOrdersInMarket = false });
        var command = CreateCommand(orderId: 59) with
        {
            Execution = Execution.StopMarket(new Price(101m, Currency.USD))
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderAccepted>());
        Assert.Single(events.OfType<OrderFilled>());
        Assert.Empty(events.OfType<OrderRejected>());
    }

    [Fact]
    public void SimulationSession_UsesConfiguredInitialCashForVenueAccount()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionMarketBuyStrategy>();

        var result = session.Run(
            SharedHistory.Load([CreateBarClosed(123m)]),
            new SimulationRunOptions { InitialCash = Money.USD(1_000m) });

        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(Money.USD(877m), result.Diagnostics.Venues[0].Cash);
        Assert.Equal(Money.USD(877m), result.Diagnostics.Venues[0].AvailableCash);
        Assert.Equal(Money.USD(0m), result.Diagnostics.Venues[0].ReservedCash);
    }

    [Fact]
    public void SimulationSession_ReportsLatencyAndRunTimingDiagnostics()
    {
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionMarketBuyStrategy>();
        var config = SimulationConfig.Instant() with
        {
            Latency = new LatencyParams(Duration.FromSeconds(10), Duration.Zero)
        };

        var result = session.Run(
            SharedHistory.Load(
            [
                CreateBarClosed(123m, unixSeconds: 1),
                CreateBarClosed(124m, unixSeconds: 12)
            ]),
            new SimulationRunOptions { Config = config });

        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(1, result.Diagnostics.Latency.CommandCount);
        Assert.Equal(Duration.FromSeconds(10), result.Diagnostics.Latency.MinEntryLatency);
        Assert.Equal(Duration.FromSeconds(10), result.Diagnostics.Latency.MaxEntryLatency);
        Assert.Equal(Duration.FromSeconds(10), result.Diagnostics.Latency.AverageEntryLatency);
        Assert.Equal(Instant.FromUnixSeconds(1), result.Diagnostics.Timing.ReplayStart);
        Assert.Equal(Instant.FromUnixSeconds(12), result.Diagnostics.Timing.ReplayEnd);
        Assert.Equal(Instant.FromUnixSeconds(12), result.Diagnostics.Timing.FinalClock);
        Assert.Equal(2, result.Diagnostics.Timing.ReplayEventCount);
    }

    [Fact]
    public void SimulationSession_ReportsRichInstrumentDiagnostics()
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();
        var config = SimulationConfig.Instant();
        var depth = new BookDepthSnapshotReceived(
            instrument,
            Bids: [new Level(new Price(99m, Currency.USD), new Qty(5m))],
            Asks: [new Level(new Price(100m, Currency.USD), new Qty(5m))],
            Depth: 1)
        {
            Time = Instant.FromUnixSeconds(1)
        };
        var close = new InstrumentClosed(
            instrument,
            new Price(98m, Currency.USD),
            "session close")
        {
            Time = Instant.FromUnixSeconds(2)
        };

        var result = session.Run(
            SharedHistory.Load([depth, close]),
            new SimulationRunOptions
            {
                Config = config,
                MatchingFidelity = MatchingFidelity.MarketByOrder
            });

        var diagnostic = Assert.Single(result.Diagnostics.Instruments);
        Assert.Equal(instrument, diagnostic.Instrument);
        Assert.Equal(MarketStatus.Closed, diagnostic.Status);
        Assert.Equal(MatchingFidelity.MarketByOrder, diagnostic.MatchingFidelity);
        Assert.Equal(SimulationOrderPolicy.Default, diagnostic.OrderPolicy);
        Assert.Equal(SimulationVenuePolicy.Default, diagnostic.SimulationPolicy);
        Assert.Equal(new Price(100m, Currency.USD), diagnostic.MarkPrice);
        Assert.Equal(new Price(98m, Currency.USD), diagnostic.CloseMark);
        Assert.Equal(0, diagnostic.OpenOrders);
    }

    [Fact]
    public void SimulatedVenueExchange_UsesInstrumentScopedOverridesWhenCreatingEngines()
    {
        var spy = Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD);
        var ibm = Contracts.Equity("IBM", Venue.NASDAQ, Currency.USD);
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            defaultMatchingFidelity: MatchingFidelity.QueueAccurate,
            instrumentConfigs:
            [
                new SimulationInstrumentConfig
                {
                    Instrument = spy.Instrument,
                    Contract = spy,
                    MatchingFidelity = MatchingFidelity.MarketByOrder,
                    InitialStatus = MarketStatus.Halted,
                    SimulationPolicy = new SimulationVenuePolicy { TradeExecution = false }
                },
                new SimulationInstrumentConfig
                {
                    Instrument = ibm.Instrument,
                    Contract = ibm
                }
            ]);

        var spyEngine = venue.GetOrCreateInstrumentEngine(spy.Instrument);
        var ibmEngine = venue.GetOrCreateInstrumentEngine(ibm.Instrument);

        Assert.Equal(MatchingFidelity.MarketByOrder, spyEngine.MatchingFidelity);
        Assert.Equal(MarketStatus.Halted, spyEngine.Status);
        Assert.False(spyEngine.SimulationPolicy.TradeExecution);
        Assert.Equal(MatchingFidelity.QueueAccurate, ibmEngine.MatchingFidelity);
        Assert.Equal(MarketStatus.Open, ibmEngine.Status);
        Assert.True(ibmEngine.SimulationPolicy.TradeExecution);
    }

    [Fact]
    public void SimulatedVenueExchange_UsesInstrumentScopedExecutionPolicy()
    {
        var contract = Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD);
        var instrument = contract.Instrument;
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            instrumentConfigs:
            [
                new SimulationInstrumentConfig
                {
                    Instrument = instrument,
                    Contract = contract,
                    SimulationPolicy = new SimulationVenuePolicy { TradeExecution = false }
                }
            ]);
        var command = CreateCommand(orderId: 601) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD))
        };

        venue.OnMarketEvent(CreateBarClosed(instrument, 120m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.OnMarketEvent(CreateTradeOccurred(price: 99m, size: 10m, aggressorSide: Side.Sell));
        var events = DrainEvents(venue);

        Assert.Empty(events.OfType<OrderFilled>());
        Assert.Equal(1, venue.GetOrCreateInstrumentEngine(instrument).OpenOrders);
    }

    [Fact]
    public void SimulationBuilder_WithInstrumentConfigReportsInstrumentOverrides()
    {
        var contract = Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD);
        var instrument = contract.Instrument;

        var result = Rhodium.Simulate<SessionNoopUniverseStrategy>()
            .WithHistory(SharedHistory.Load([CreateBarClosed(instrument, 123m)]))
            .WithMatchingFidelity(MatchingFidelity.QueueAccurate)
            .WithInstrument(new SimulationInstrumentConfig
            {
                Instrument = instrument,
                Contract = contract,
                MatchingFidelity = MatchingFidelity.MarketByOrder,
                InitialStatus = MarketStatus.Halted
            })
            .Run();

        var diagnostic = Assert.Single(result.Diagnostics.Instruments);
        Assert.Equal(instrument, diagnostic.Instrument);
        Assert.Equal(MatchingFidelity.MarketByOrder, diagnostic.MatchingFidelity);
        Assert.Equal(MarketStatus.Halted, diagnostic.Status);
        Assert.Equal(SimulationOrderPolicy.Default, diagnostic.OrderPolicy);
        Assert.Equal(SimulationVenuePolicy.Default, diagnostic.SimulationPolicy);
    }

    [Fact]
    public void SimulationSession_DeliversSetupSchedulesFromSessionClock()
    {
        SessionScheduledClockStrategy.Reset();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionScheduledClockStrategy>();

        var result = session.Run(SharedHistory.Load(
        [
            CreateBarClosed(100m, unixSeconds: 1),
            CreateBarClosed(101m, unixSeconds: 6),
            CreateBarClosed(102m, unixSeconds: 11)
        ]));

        Assert.Equal(2, SessionScheduledClockStrategy.ScheduledCount);
        Assert.Equal(Instant.FromUnixSeconds(11), SessionScheduledClockStrategy.LastScheduledAt);
        Assert.Empty(result.ExecutionEvents.OfType<OrderRejected>());
    }

    [Fact]
    public void SimulationSession_AppliesVenueFinancingAndRecordsAccountStatement()
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionMarketBuyStrategy>();
        var financing = new FinancingChargeApplied(
            new FinancingChargeId(1),
            FinancingChargeType.BorrowFee,
            new StrategyId(1),
            VariantId: 0,
            Money.USD(-5m),
            Instant.FromUnixSeconds(2),
            instrument,
            new Qty(1m),
            Rate: 0.01m)
        {
            Time = Instant.FromUnixSeconds(2)
        };

        var result = session.Run(SharedHistory.Load(
        [
            CreateBarClosed(instrument, close: 123m, unixSeconds: 1),
            financing
        ]));

        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        var statement = Assert.Single(result.AccountStatements, statement => statement.Time == Instant.FromUnixSeconds(2));
        Assert.Equal(Money.USD(99_872m), statement.Cash);
        Assert.Equal(Money.USD(99_872m), statement.AvailableCash);
        Assert.Equal(Money.USD(99_872m), statement.Equity);
        Assert.Equal(Money.USD(0m), statement.ReservedCash);
        Assert.Equal(Instant.FromUnixSeconds(2), statement.Time);
    }

    [Fact]
    public void SimulationAccount_CreatesStatementsAfterFinancing()
    {
        var account = new SimulationAccount(Money.USD(1_000m));

        account.ApplyFinancing(Money.USD(25m));
        var statement = account.CreateStatement(
            new StrategyId(7),
            variantId: 3,
            Currency.USD,
            Instant.FromUnixSeconds(10),
            openOrders: 2);

        Assert.Equal(new StrategyId(7), statement.StrategyId);
        Assert.Equal(3, statement.VariantId);
        Assert.Equal(Money.USD(1_025m), statement.Cash);
        Assert.Equal(Money.USD(1_025m), statement.AvailableCash);
        Assert.Equal(Money.USD(1_025m), statement.Equity);
        Assert.Equal(2, statement.OpenOrders);
        Assert.Equal(Instant.FromUnixSeconds(10), statement.Time);
    }

    [Fact]
    public void SimulationAccount_AppliesInstrumentFinancingWhenContractTermsMatch()
    {
        var account = new SimulationAccount(Money.USD(1_000m));
        var contract = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD);
        account.RegisterContract(contract);

        account.ApplyFinancing(new FinancingChargeApplied(
            new FinancingChargeId(1),
            FinancingChargeType.BorrowFee,
            new StrategyId(7),
            VariantId: 0,
            Money.USD(-3m),
            Instant.FromUnixSeconds(10),
            contract.Instrument,
            new Qty(2m),
            Rate: 0.01m));

        Assert.Equal(Money.USD(997m), account.Cash);
    }

    [Fact]
    public void SimulationAccount_RejectsInstrumentFinancingWhenContractHasNoFinancingTerms()
    {
        var account = new SimulationAccount(Money.USD(1_000m));
        var contract = Contracts.CryptoSpot("BTCUSD", Venue.Binance, Currency.BTC, Currency.USD, 0.01m, 0.001m);
        account.RegisterContract(contract);

        var error = Assert.Throws<InvalidOperationException>(() => account.ApplyFinancing(new FinancingChargeApplied(
            new FinancingChargeId(1),
            FinancingChargeType.BorrowFee,
            new StrategyId(7),
            VariantId: 0,
            Money.USD(-3m),
            Instant.FromUnixSeconds(10),
            contract.Instrument,
            new Qty(2m),
            Rate: 0.01m)));

        Assert.Contains("does not permit instrument-level financing charges", error.Message);
    }

    [Fact]
    public void SimulationAccount_RejectsFinancingChargeTypeThatDoesNotMatchContractTerms()
    {
        var account = new SimulationAccount(Money.USD(1_000m));
        var contract = Contracts.CryptoPerpetual(
            "BTCUSD-PERP",
            Venue.Binance,
            Currency.BTC,
            Currency.USD,
            Currency.USD,
            tick: 0.1m,
            lot: 0.001m,
            multiplier: 1m,
            inverse: false);
        account.RegisterContract(contract);

        var error = Assert.Throws<InvalidOperationException>(() => account.ApplyFinancing(new FinancingChargeApplied(
            new FinancingChargeId(1),
            FinancingChargeType.BorrowFee,
            new StrategyId(7),
            VariantId: 0,
            Money.USD(-3m),
            Instant.FromUnixSeconds(10),
            contract.Instrument,
            new Qty(2m),
            Rate: 0.01m)));

        Assert.Contains("require PerpetualFunding", error.Message);
    }

    [Fact]
    public void SimulationAccount_ValuesFuturePositionsFromInstrumentContract()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("ES", AssetClass.Index), Venue.CME);
        var contract = Contracts.Future(
            "ESZ6",
            Venue.CME,
            underlying,
            Currency.USD,
            tick: 0.25m,
            lot: 1m,
            multiplier: 50m,
            expiry: Instant.FromUnixSeconds(1_796_016_000));
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 71) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(2m)
        };

        account.ApplyFill(
            buy,
            new Qty(2m),
            new Price(5000m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));

        var statement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            Instant.FromUnixSeconds(2),
            new Dictionary<Instrument, Price>
            {
                [contract.Instrument] = new Price(5010m, Currency.USD)
            });

        Assert.Equal(Money.USD(100_000m), account.Cash);
        Assert.Equal(0, account.PendingAssetDeliveryCount);
        Assert.Equal(Money.USD(501_000m), statement.MarketValue);
        Assert.Equal(Money.USD(1_000m), statement.UnrealizedPnL);
        Assert.Equal(Money.USD(101_000m), statement.Equity);
    }

    [Fact]
    public void SimulationAccount_CreditsDerivativeRealizedPnLWithoutSpotProceeds()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("ES", AssetClass.Index), Venue.CME);
        var contract = Contracts.Future(
            "ESZ6",
            Venue.CME,
            underlying,
            Currency.USD,
            tick: 0.25m,
            lot: 1m,
            multiplier: 50m,
            expiry: Instant.FromUnixSeconds(1_796_016_000));
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 72) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(2m)
        };
        var sell = buy with { ClientOrderId = new OrderId(73), Side = Side.Sell, Quantity = new Qty(1m) };

        account.ApplyFill(buy, new Qty(2m), new Price(5000m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        account.ApplyFill(sell, new Qty(1m), new Price(5010m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(2));

        var statement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            Instant.FromUnixSeconds(3),
            new Dictionary<Instrument, Price>
            {
                [contract.Instrument] = new Price(5010m, Currency.USD)
            });

        Assert.Equal(Money.USD(100_500m), account.Cash);
        Assert.Equal(Money.USD(500m), statement.RealizedPnL);
        Assert.Equal(Money.USD(500m), statement.UnrealizedPnL);
        Assert.Equal(Money.USD(101_000m), statement.Equity);
    }

    [Fact]
    public void SimulationAccount_MarginLongOptionRequiresFullPremium()
    {
        var account = new SimulationAccount(Money.USD(150m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var contract = Contracts.OptionContract(
            "SPY261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionRight.Call,
            ExerciseStyle.European);
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 78) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        var rejected = account.TryReserve(
            buy,
            new Price(2m, Currency.USD),
            MarginParams.RegT(),
            SettlementParams.Immediate(),
            allowCashBorrowing: false,
            out var reason);

        Assert.False(rejected);
        Assert.Contains("required 200.00 USD", reason);

        var funded = new SimulationAccount(Money.USD(250m), AccountType.Margin);
        funded.RegisterContract(contract);
        var accepted = funded.TryReserve(
            buy,
            new Price(2m, Currency.USD),
            MarginParams.RegT(),
            SettlementParams.Immediate(),
            allowCashBorrowing: false,
            out _);

        Assert.True(accepted);
        Assert.Equal(Money.USD(200m), funded.ReservedCash);
    }

    [Fact]
    public void SimulationAccount_MarginShortOptionUsesUnderlyingRisk()
    {
        var account = new SimulationAccount(Money.USD(5_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var contract = Contracts.OptionContract(
            "SPY261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionRight.Call,
            ExerciseStyle.European);
        account.RegisterContract(contract);
        var sell = CreateCommand(orderId: 79) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Side = Side.Sell,
            Quantity = new Qty(1m)
        };
        var margin = MarginParams.RegT().WithShortSalePolicy(ShortSalePolicy.AllowNakedShort);

        var accepted = account.TryReserve(
            sell,
            new Price(2m, Currency.USD),
            margin,
            SettlementParams.Immediate(),
            allowCashBorrowing: false,
            out _);

        Assert.True(accepted);
        Assert.Equal(Money.USD(2_200m), account.ReservedCash);
    }

    [Fact]
    public void SimulationAccount_MarginStatusPricesShortOptionRiskFromUnderlyingMark()
    {
        var account = new SimulationAccount(Money.USD(5_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var contract = Contracts.OptionContract(
            "SPY261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionRight.Call,
            ExerciseStyle.European);
        account.RegisterContract(contract);
        var sell = CreateCommand(orderId: 80) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Side = Side.Sell,
            Quantity = new Qty(1m)
        };
        account.ApplyFill(
            sell,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var statuses = new List<MarginAccountStatus>();
        var accumulators = new Dictionary<(StrategyId StrategyId, int VariantId), SimulationAccount.MarginStatusAccumulator>();

        account.CalculateMarginStatuses(
            new Dictionary<(StrategyId StrategyId, int VariantId, Instrument Instrument), Price>
            {
                [(sell.StrategyId, sell.VariantId, contract.Instrument)] = new Price(2m, Currency.USD)
            },
            new Dictionary<Instrument, Price>
            {
                [underlying] = new Price(150m, Currency.USD)
            },
            MarginParams.RegT().WithShortSalePolicy(ShortSalePolicy.AllowNakedShort),
            Currency.USD,
            statuses,
            accumulators);

        var status = Assert.Single(statuses);
        Assert.Equal(Money.USD(3_200m), status.MaintenanceRequirement);
        Assert.Equal(Money.USD(5_000m), status.Equity);
        Assert.False(status.IsMaintenanceBreached);
    }

    [Fact]
    public void SimulationAccount_MarginRecognizesVerticalSpreadRisk()
    {
        var account = new SimulationAccount(Money.USD(5_000m), AccountType.Margin);
        var optionVenue = new Venue("CBOE");
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var shortCall = Contracts.OptionContract(
            "SPY261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        var longCall = Contracts.OptionContract(
            "SPY261218C00110000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(110m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European);
        account.RegisterContract(shortCall);
        account.RegisterContract(longCall);
        var sell = CreateCommand(orderId: 81) with
        {
            Instrument = shortCall.Instrument,
            Venue = shortCall.Instrument.Venue,
            Side = Side.Sell,
            Quantity = new Qty(1m)
        };
        var buy = CreateCommand(orderId: 82) with
        {
            Instrument = longCall.Instrument,
            Venue = longCall.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(sell, new Qty(1m), new Price(2m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        account.ApplyFill(buy, new Qty(1m), new Price(1m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        var statuses = new List<MarginAccountStatus>();
        var accumulators = new Dictionary<(StrategyId StrategyId, int VariantId), SimulationAccount.MarginStatusAccumulator>();

        account.CalculateMarginStatuses(
            new Dictionary<(StrategyId StrategyId, int VariantId, Instrument Instrument), Price>
            {
                [(sell.StrategyId, sell.VariantId, shortCall.Instrument)] = new Price(2m, Currency.USD),
                [(buy.StrategyId, buy.VariantId, longCall.Instrument)] = new Price(1m, Currency.USD)
            },
            new Dictionary<Instrument, Price>
            {
                [underlying] = new Price(100m, Currency.USD)
            },
            MarginParams.RegT().WithShortSalePolicy(ShortSalePolicy.AllowNakedShort),
            Currency.USD,
            statuses,
            accumulators);

        var status = Assert.Single(statuses);
        Assert.Equal(Money.USD(900m), status.MaintenanceRequirement);
        Assert.Equal(Money.USD(5_000m), status.Equity);
        Assert.False(status.IsMaintenanceBreached);
    }

    [Fact]
    public void SimulationAccount_CashSettlesOptionExpiryAndClosesContract()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 74) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            buy.StrategyId,
            buy.VariantId,
            contract.Instrument,
            LifecycleReference(new Price(105m, Currency.USD)),
            expiry);

        var statement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            expiry,
            new Dictionary<Instrument, Price>());

        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(100_300m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, contract.Instrument));
        Assert.Equal(Money.USD(300m), statement.RealizedPnL);
        Assert.Equal(0, statement.OpenPositions);
        Assert.Equal(Money.USD(100_300m), statement.Equity);
        var lifecycleEvents = DrainAccountEvents(account);
        var exercise = Assert.Single(lifecycleEvents.OfType<OptionLifecycleApplied>(), evt => evt.LifecycleKind == OptionLifecycleKind.Exercise);
        var settlement = Assert.Single(lifecycleEvents.OfType<OptionLifecycleApplied>(), evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(contract.Instrument, exercise.Instrument);
        Assert.Equal(new Qty(1m), exercise.Quantity);
        Assert.Equal(Money.USD(500m), settlement.CashFlow);
    }

    [Fact]
    public void SimulationAccount_CashSettlementOutcomeIsNotReclassifiedWhenNetCashFlowIsZero()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 7401) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            buy.StrategyId,
            buy.VariantId,
            contract.Instrument,
            LifecycleReference(new Price(102m, Currency.USD)),
            expiry);

        var statement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            expiry,
            new Dictionary<Instrument, Price>());
        var lifecycleEvents = DrainAccountEvents(account).OfType<OptionLifecycleApplied>().ToArray();

        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(100_000m), account.Cash);
        Assert.Equal(Money.USD(0m), statement.RealizedPnL);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, contract.Instrument));
        Assert.Contains(lifecycleEvents, evt => evt.LifecycleKind == OptionLifecycleKind.Exercise);
        var settlement = Assert.Single(lifecycleEvents, evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(Money.USD(200m), settlement.CashFlow);
        Assert.DoesNotContain(lifecycleEvents, evt => evt.LifecycleKind == OptionLifecycleKind.ExpireWorthless);
    }

    [Fact]
    public void OptionLifecycleResult_RejectsEmptyOutcomes()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 7401) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));

        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleResult([]));

        Assert.Contains("at least one outcome", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new Qty(1m), account.GetPositionQuantity(buy.StrategyId, buy.VariantId, contract.Instrument));
    }

    [Fact]
    public void SimulationAccount_OptionLifecycleApplyReturnsNoOpenPositionWhenFlat()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        account.RegisterContract(contract);
        var result = new OptionLifecycleResult([
            new OptionLifecycleOutcome.CashSettle(
                OptionLifecycleKind.Exercise,
                new Qty(1m),
                new Price(105m, Currency.USD),
                expiry,
                OptionLifecycleReferenceSource.MarketMark,
                "Cash settled at expiry.")
        ]);

        var status = account.ApplyOptionLifecycleResult(new StrategyId(1), 0, contract.Instrument, result);

        Assert.Equal(OptionLifecycleApplicationStatus.NoOpenPosition, status);
        Assert.Empty(DrainAccountEvents(account).OfType<OptionLifecycleApplied>());
    }

    [Fact]
    public void SimulationAccount_OptionLifecycleApplyReturnsBlockedForBlockedLifecycle()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 7404) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(1m)
        };
        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var result = new OptionLifecycleResult([
            new OptionLifecycleOutcome.Block(
                new Qty(1m),
                expiry,
                "No settlement reference.")
        ]);

        var status = account.ApplyOptionLifecycleResult(buy.StrategyId, buy.VariantId, contract.Instrument, result);

        Assert.Equal(OptionLifecycleApplicationStatus.Blocked, status);
        Assert.Equal(new Qty(1m), account.GetPositionQuantity(buy.StrategyId, buy.VariantId, contract.Instrument));
        var lifecycle = Assert.Single(DrainAccountEvents(account).OfType<OptionLifecycleApplied>());
        Assert.Equal(OptionLifecycleKind.Blocked, lifecycle.LifecycleKind);
    }

    [Fact]
    public void SimulationAccount_RejectsLifecycleResultThatDoesNotCoverOpenPosition()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 7402) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var result = new OptionLifecycleResult([
            new OptionLifecycleOutcome.CashSettle(
                OptionLifecycleKind.Exercise,
                new Qty(0.5m),
                new Price(105m, Currency.USD),
                expiry,
                OptionLifecycleReferenceSource.MarketMark,
                "Cash settled at expiry.")
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => account.ApplyOptionLifecycleResult(
            buy.StrategyId,
            buy.VariantId,
            contract.Instrument,
            result));

        Assert.Contains("cover quantity 0.5", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new Qty(1m), account.GetPositionQuantity(buy.StrategyId, buy.VariantId, contract.Instrument));
        Assert.Equal(Money.USD(99_800m), account.Cash);
    }

    [Fact]
    public void SimulationAccount_RejectsBlockedLifecycleResultThatDoesNotCoverOpenPosition()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 7405) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(2m)
        };

        account.ApplyFill(
            buy,
            new Qty(2m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var result = new OptionLifecycleResult([
            new OptionLifecycleOutcome.Block(
                new Qty(1m),
                expiry,
                "No settlement reference.")
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => account.ApplyOptionLifecycleResult(
            buy.StrategyId,
            buy.VariantId,
            contract.Instrument,
            result));

        Assert.Contains("cover quantity 1", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new Qty(2m), account.GetPositionQuantity(buy.StrategyId, buy.VariantId, contract.Instrument));
        Assert.Empty(DrainAccountEvents(account).OfType<OptionLifecycleApplied>());
    }

    [Fact]
    public void SimulationAccount_RejectsLifecycleResultWithOppositePositionSign()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 7403) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var result = new OptionLifecycleResult([
            new OptionLifecycleOutcome.CashSettle(
                OptionLifecycleKind.Assignment,
                new Qty(-1m),
                new Price(105m, Currency.USD),
                expiry,
                OptionLifecycleReferenceSource.MarketMark,
                "Cash settled at expiry.")
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => account.ApplyOptionLifecycleResult(
            buy.StrategyId,
            buy.VariantId,
            contract.Instrument,
            result));

        Assert.Contains("opposite sign", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new Qty(1m), account.GetPositionQuantity(buy.StrategyId, buy.VariantId, contract.Instrument));
        Assert.Equal(Money.USD(99_800m), account.Cash);
    }

    [Fact]
    public void SimulationAccount_OutOfTheMoneyCashSettledOptionExpiresWorthless()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 7474) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            buy.StrategyId,
            buy.VariantId,
            contract.Instrument,
            LifecycleReference(new Price(95m, Currency.USD)),
            expiry);

        var statement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            expiry,
            new Dictionary<Instrument, Price>());

        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(99_800m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, contract.Instrument));
        Assert.Equal(Money.USD(-200m), statement.RealizedPnL);
        var lifecycleEvent = Assert.Single(DrainAccountEvents(account).OfType<OptionLifecycleApplied>());
        Assert.Equal(OptionLifecycleKind.ExpireWorthless, lifecycleEvent.LifecycleKind);
        Assert.Equal("Out of the money at expiry.", lifecycleEvent.Reason);
    }

    [Fact]
    public void SimulationAccount_ManualOptionDoesNotAutoExerciseAtExpiry()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European);
        account.RegisterContract(contract);
        var buy = CreateCommand(orderId: 874) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            buy.StrategyId,
            buy.VariantId,
            contract.Instrument,
            LifecycleReference(new Price(105m, Currency.USD)),
            expiry);

        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(99_800m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, contract.Instrument));
        var statement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            expiry,
            new Dictionary<Instrument, Price>());
        Assert.Equal(Money.USD(-200m), statement.RealizedPnL);
        var lifecycleEvent = Assert.Single(DrainAccountEvents(account).OfType<OptionLifecycleApplied>());
        Assert.Equal(OptionLifecycleKind.ExpireUnexercised, lifecycleEvent.LifecycleKind);
        Assert.Contains("not exercised by contract policy", lifecycleEvent.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SimulationAccount_RandomAssignmentPolicyWithoutSelectionExpiresShortUnassigned()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry,
            assignmentPolicy: OptionAssignmentPolicy.Random);
        account.RegisterContract(contract);
        var sell = CreateCommand(orderId: 875) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Side = Side.Sell,
            Quantity = new Qty(1m)
        };
        account.ApplyFill(
            sell,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));

        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            sell.StrategyId,
            sell.VariantId,
            contract.Instrument,
            LifecycleReference(new Price(105m, Currency.USD)),
            expiry);

        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(100_200m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(sell.StrategyId, sell.VariantId, contract.Instrument));
        var lifecycle = Assert.Single(DrainAccountEvents(account).OfType<OptionLifecycleApplied>());
        Assert.Equal(OptionLifecycleKind.ExpireUnassigned, lifecycle.LifecycleKind);
        Assert.Contains("Random assignment requires explicit selection input", lifecycle.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SimulationAccount_RandomAssignmentSelectionAssignsShortOption()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry,
            assignmentPolicy: OptionAssignmentPolicy.Random);
        account.RegisterContract(contract);
        var sell = CreateCommand(orderId: 876) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Side = Side.Sell,
            Quantity = new Qty(1m)
        };
        account.ApplyFill(
            sell,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));

        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            sell.StrategyId,
            sell.VariantId,
            contract.Instrument,
            LifecycleReference(new Price(105m, Currency.USD)),
            expiry,
            assignmentInput: new SimulationOptionAssignmentInput(isSelectedForRandomAssignment: true));

        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(99_700m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(sell.StrategyId, sell.VariantId, contract.Instrument));
        var lifecycle = DrainAccountEvents(account).OfType<OptionLifecycleApplied>().ToArray();
        var assignment = Assert.Single(lifecycle, evt => evt.LifecycleKind == OptionLifecycleKind.Assignment);
        var settlement = Assert.Single(lifecycle, evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(new Qty(-1m), assignment.Quantity);
        Assert.Equal(Money.USD(-500m), settlement.CashFlow);
    }

    [Fact]
    public void SimulationAccount_ProRataAssignmentSettlesAssignedQuantityAndExpiresRemainder()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry,
            assignmentPolicy: OptionAssignmentPolicy.ProRata);
        account.RegisterContract(contract);
        var sell = CreateCommand(orderId: 877) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Side = Side.Sell,
            Quantity = new Qty(2m)
        };
        account.ApplyFill(
            sell,
            new Qty(2m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));

        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            sell.StrategyId,
            sell.VariantId,
            contract.Instrument,
            LifecycleReference(new Price(105m, Currency.USD)),
            expiry,
            assignmentInput: new SimulationOptionAssignmentInput(proRataAssignmentRatio: 0.5m));

        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(99_900m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(sell.StrategyId, sell.VariantId, contract.Instrument));
        var lifecycle = DrainAccountEvents(account).OfType<OptionLifecycleApplied>().ToArray();
        var assignment = Assert.Single(lifecycle, evt => evt.LifecycleKind == OptionLifecycleKind.Assignment);
        var settlement = Assert.Single(lifecycle, evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        var expired = Assert.Single(lifecycle, evt => evt.LifecycleKind == OptionLifecycleKind.ExpireUnassigned);
        Assert.Equal(new Qty(-1m), assignment.Quantity);
        Assert.Equal(Money.USD(-500m), settlement.CashFlow);
        Assert.Equal(new Qty(-1m), expired.Quantity);
    }

    [Fact]
    public void SimulatedVenueExchange_ExpiryUsesConfiguredAssignmentInput()
    {
        var venue = new Venue("CBOE");
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), venue);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            venue,
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry,
            assignmentPolicy: OptionAssignmentPolicy.Random);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Lifecycle = SimulationLifecycleConfig.Default
                .WithSettlementReferencePrice(underlying, new Price(105m, Currency.USD))
                .WithAssignmentInput(
                    new StrategyId(1),
                    0,
                    contract.Instrument,
                    new SimulationOptionAssignmentInput(isSelectedForRandomAssignment: true))
        };
        var exchange = new SimulatedVenueExchange(venue, config, Money.USD(100_000m));
        exchange.RegisterContract(contract);
        var sell = CreateCommand(orderId: 878) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Side = Side.Sell,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(
            sell,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        exchange.DrainDueWork(expiry);
        var events = DrainSimulationEvents(exchange).OfType<OptionLifecycleApplied>().ToArray();

        var assignment = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.Assignment);
        var settlement = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(new Qty(-1m), assignment.Quantity);
        Assert.Equal(Money.USD(-500m), settlement.CashFlow);
        Assert.Equal(Money.USD(99_700m), exchange.Account.Cash);
    }

    [Fact]
    public void SimulatedVenueExchange_ExpiryUsesAssignmentNoticeEvent()
    {
        var venue = new Venue("CBOE");
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), venue);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            venue,
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry,
            assignmentPolicy: OptionAssignmentPolicy.Random);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Lifecycle = SimulationLifecycleConfig.Default
                .WithSettlementReferencePrice(underlying, new Price(105m, Currency.USD))
        };
        var exchange = new SimulatedVenueExchange(venue, config, Money.USD(100_000m));
        exchange.RegisterContract(contract);
        var sell = CreateCommand(orderId: 879) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Side = Side.Sell,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(
            sell,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        exchange.OnMarketEvent(new OptionAssignmentNoticePublished(
            sell.StrategyId,
            sell.VariantId,
            contract.Instrument,
            expiry,
            "OCC",
            IsSelectedForRandomAssignment: true,
            Reason: "clearing assignment"));
        var events = DrainSimulationEvents(exchange).OfType<OptionLifecycleApplied>().ToArray();

        var assignment = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.Assignment);
        var settlement = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(new Qty(-1m), assignment.Quantity);
        Assert.Equal("clearing assignment", assignment.Reason);
        Assert.Equal(Money.USD(-500m), settlement.CashFlow);
        Assert.Equal(Money.USD(99_700m), exchange.Account.Cash);
        Assert.Equal(Qty.Zero, exchange.Account.GetPositionQuantity(sell.StrategyId, sell.VariantId, contract.Instrument));
    }

    [Fact]
    public void SimulatedVenueExchange_AssignmentNoticeCanDisableShortAssignment()
    {
        var venue = new Venue("CBOE");
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), venue);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            venue,
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry,
            assignmentPolicy: OptionAssignmentPolicy.Random);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Lifecycle = SimulationLifecycleConfig.Default
                .WithSettlementReferencePrice(underlying, new Price(105m, Currency.USD))
        };
        var exchange = new SimulatedVenueExchange(venue, config, Money.USD(100_000m));
        exchange.RegisterContract(contract);
        var sell = CreateCommand(orderId: 880) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Side = Side.Sell,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(
            sell,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        exchange.OnMarketEvent(new OptionAssignmentNoticePublished(
            sell.StrategyId,
            sell.VariantId,
            contract.Instrument,
            expiry,
            "OCC",
            IsSelectedForRandomAssignment: true,
            AssignShortPositions: false));
        var events = DrainSimulationEvents(exchange).OfType<OptionLifecycleApplied>().ToArray();

        Assert.DoesNotContain(events, evt => evt.LifecycleKind == OptionLifecycleKind.Assignment);
        Assert.DoesNotContain(events, evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        var expired = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.ExpireUnassigned);
        Assert.Equal(new Qty(-1m), expired.Quantity);
        Assert.Equal("Assignment disabled by scenario rule.", expired.Reason);
        Assert.Equal(Money.USD(100_200m), exchange.Account.Cash);
        Assert.Equal(Qty.Zero, exchange.Account.GetPositionQuantity(sell.StrategyId, sell.VariantId, contract.Instrument));
    }

    [Fact]
    public void SimulationAccount_FuturesStyleOptionFillDoesNotExchangeUpfrontPremium()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var venue = new Venue("CME");
        var future = new Instrument(new Asset("ESZ6", AssetClass.Future), venue);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.FutureOption(
            "ESZ6 C5000",
            venue,
            future,
            Currency.USD,
            tick: 0.25m,
            lot: 1m,
            multiplier: 50m,
            strike: new Price(5_000m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American);
        account.RegisterContract(option);
        var buy = CreateCommand(orderId: 871) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };
        var sell = buy with
        {
            ClientOrderId = new OrderId(872),
            Side = Side.Sell
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(10m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));

        Assert.Equal(Money.USD(100_000m), account.Cash);
        Assert.Equal(new Qty(1m), account.GetPositionQuantity(buy.StrategyId, buy.VariantId, option.Instrument));
        var openStatement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            Instant.FromUnixSeconds(1),
            new Dictionary<Instrument, Price>
            {
                [option.Instrument] = new(10m, Currency.USD)
            });
        Assert.Equal(Money.USD(100_000m), openStatement.Equity);

        account.ApplyFill(
            sell,
            new Qty(1m),
            new Price(11m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(2));

        Assert.Equal(Money.USD(100_050m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, option.Instrument));
    }

    [Fact]
    public void SimulationAccount_DeferredOptionPremiumDoesNotExchangeUpfrontCashAndSettlesOnClose()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            premiumStyle: OptionPremiumStyle.Deferred);
        account.RegisterContract(option);
        var buy = CreateCommand(orderId: 873) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };
        var sell = buy with
        {
            ClientOrderId = new OrderId(874),
            Side = Side.Sell
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));

        Assert.Equal(Money.USD(100_000m), account.Cash);

        account.ApplyFill(
            sell,
            new Qty(1m),
            new Price(3m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(2));

        Assert.Equal(Money.USD(100_100m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, option.Instrument));
    }

    [Fact]
    public void SimulationAccount_DeferredOptionPremiumSettlesAtExpiryWhenWorthless()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            premiumStyle: OptionPremiumStyle.Deferred);
        account.RegisterContract(option);
        var buy = CreateCommand(orderId: 875) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            buy.StrategyId,
            buy.VariantId,
            option.Instrument,
            LifecycleReference(new Price(99m, Currency.USD)),
            expiry);

        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(99_800m), account.Cash);
        Assert.Equal(Money.USD(-200m), account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            expiry,
            new Dictionary<Instrument, Price>()).RealizedPnL);
    }

    [Fact]
    public void SimulationAccount_BinaryOptionExpiryReportsNetRealizedPnL()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var eventTime = Instant.FromUnixSeconds(1_796_016_000);
        var binary = Contracts.BinaryOption(
            "EVENT:YES",
            new Venue("PM"),
            "event:yes",
            Currency.USD,
            Money.USD(100m),
            eventTime);
        account.RegisterContract(binary);
        var buy = CreateCommand(orderId: 876) with
        {
            Instrument = binary.Instrument,
            Venue = binary.Instrument.Venue,
            Quantity = new Qty(2m)
        };

        account.ApplyFill(
            buy,
            new Qty(2m),
            new Price(40m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var applied = account.ApplyCashOutcomeContractLifecycle(
            buy.StrategyId,
            buy.VariantId,
            binary.Instrument,
            new Price(0m, Currency.USD),
            eventTime,
            outcomeOccurred: true);

        var statement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            eventTime,
            new Dictionary<Instrument, Price>());

        Assert.True(applied);
        Assert.Equal(Money.USD(100_120m), account.Cash);
        Assert.Equal(Money.USD(120m), statement.RealizedPnL);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, binary.Instrument));
        Assert.Empty(DrainAccountEvents(account).OfType<OptionLifecycleApplied>());
    }

    [Fact]
    public void SimulationAccount_LosingBinaryOptionExpiryRealizesPremiumLoss()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var eventTime = Instant.FromUnixSeconds(1_796_016_000);
        var binary = Contracts.BinaryOption(
            "EVENT:YES",
            new Venue("PM"),
            "event:yes",
            Currency.USD,
            Money.USD(100m),
            eventTime);
        account.RegisterContract(binary);
        var buy = CreateCommand(orderId: 877) with
        {
            Instrument = binary.Instrument,
            Venue = binary.Instrument.Venue,
            Quantity = new Qty(2m)
        };

        account.ApplyFill(
            buy,
            new Qty(2m),
            new Price(40m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var applied = account.ApplyCashOutcomeContractLifecycle(
            buy.StrategyId,
            buy.VariantId,
            binary.Instrument,
            new Price(0m, Currency.USD),
            eventTime,
            outcomeOccurred: false);

        var statement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            eventTime,
            new Dictionary<Instrument, Price>());

        Assert.True(applied);
        Assert.Equal(Money.USD(99_920m), account.Cash);
        Assert.Equal(Money.USD(-80m), statement.RealizedPnL);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, binary.Instrument));
        Assert.Empty(DrainAccountEvents(account).OfType<OptionLifecycleApplied>());
    }

    [Fact]
    public void SimulationAccount_BettingEventSettlementUsesStakeAndEntryOdds()
    {
        var account = new SimulationAccount(Money.USD(1_000m), AccountType.Margin);
        var eventTime = Instant.FromUnixSeconds(1_796_016_000);
        var bet = Contracts.BettingInstrument(
            "MATCH:TEAM-A",
            new Venue("PM"),
            "match-1",
            "team-a",
            Currency.USD,
            tick: 0.01m,
            eventTime);
        account.RegisterContract(bet);
        var buy = CreateCommand(orderId: 878) with
        {
            Instrument = bet.Instrument,
            Venue = bet.Instrument.Venue,
            Quantity = new Qty(10m)
        };

        account.ApplyFill(
            buy,
            new Qty(10m),
            new Price(3.5m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var applied = account.ApplyCashOutcomeContractLifecycle(
            buy.StrategyId,
            buy.VariantId,
            bet.Instrument,
            new Price(0m, Currency.USD),
            eventTime,
            outcomeOccurred: true);

        var statement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            eventTime,
            new Dictionary<Instrument, Price>());

        Assert.True(applied);
        Assert.Equal(Money.USD(1_025m), account.Cash);
        Assert.Equal(Money.USD(25m), statement.RealizedPnL);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, bet.Instrument));
        Assert.Empty(DrainAccountEvents(account).OfType<OptionLifecycleApplied>());
    }

    [Fact]
    public void SimulatedExchangeRegistry_ExpiresCashOptionFromCrossVenueUnderlyingMark()
    {
        var optionVenue = new Venue("CBOE");
        var registry = new SimulatedExchangeRegistry(
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney);
        registry.RegisterContract(option);
        var exchange = registry.GetRequired(optionVenue);
        var buy = CreateCommand(orderId: 77) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        registry.OnMarketEvent(CreateBarClosed(underlying, 105m, unixSeconds: 1_796_016_000));

        var statement = exchange.CreateAccountStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            expiry);

        Assert.Equal(Money.USD(100_300m), exchange.Account.Cash);
        Assert.Equal(Qty.Zero, exchange.Account.GetPositionQuantity(buy.StrategyId, buy.VariantId, option.Instrument));
        Assert.Equal(Money.USD(300m), statement.RealizedPnL);
        Assert.Equal(0, statement.OpenPositions);
    }

    [Fact]
    public void SimulatedVenueExchange_OptionExpiryWithoutReferenceMarkEmitsBlockedLifecycle()
    {
        var optionVenue = new Venue("CBOE");
        var exchange = new SimulatedVenueExchange(
            optionVenue,
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney);
        exchange.RegisterContract(option);
        var buy = CreateCommand(orderId: 7701) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(buy, new Qty(1m), new Price(2m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        exchange.DrainDueWork(expiry);
        var events = DrainSimulationEvents(exchange);

        var blocked = Assert.Single(events.OfType<OptionLifecycleApplied>(), evt => evt.LifecycleKind == OptionLifecycleKind.Blocked);
        Assert.Equal(option.Instrument, blocked.Instrument);
        Assert.Equal(new Qty(1m), blocked.Quantity);
        Assert.Equal(OptionLifecycleReferenceSource.None, blocked.ReferenceSource);
        Assert.Contains("no settlement/reference price", blocked.Reason, StringComparison.Ordinal);
        Assert.Equal(new Qty(1m), exchange.Account.GetPositionQuantity(buy.StrategyId, buy.VariantId, option.Instrument));
    }

    [Fact]
    public void SimulatedVenueExchange_OptionExpiryWithoutReferenceThrowsWhenConfigured()
    {
        var optionVenue = new Venue("CBOE");
        var exchange = new SimulatedVenueExchange(
            optionVenue,
            SimulationConfig.Instant() with
            {
                AccountType = AccountType.Margin,
                Lifecycle = SimulationLifecycleConfig.Default.WithMissingReferencePricePolicy(MissingReferencePricePolicy.Throw)
            },
            Money.USD(100_000m));
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney);
        exchange.RegisterContract(option);
        var buy = CreateCommand(orderId: 7701) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(buy, new Qty(1m), new Price(2m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        var error = Assert.Throws<InvalidOperationException>(() => exchange.DrainDueWork(expiry));

        Assert.Contains("no settlement/reference price", error.Message, StringComparison.Ordinal);
        Assert.Equal(new Qty(1m), exchange.Account.GetPositionQuantity(buy.StrategyId, buy.VariantId, option.Instrument));
    }

    [Fact]
    public void SimulatedVenueExchange_HasDueWorkTracksActionableOptionLifecycle()
    {
        var optionVenue = new Venue("CBOE");
        var exchange = new SimulatedVenueExchange(
            optionVenue,
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney);
        exchange.RegisterContract(option);
        var buy = CreateCommand(orderId: 7704) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(buy, new Qty(1m), new Price(2m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));

        Assert.True(exchange.HasDueWork(expiry));
        exchange.DrainDueWork(expiry);
        var blocked = Assert.Single(DrainSimulationEvents(exchange).OfType<OptionLifecycleApplied>(), evt => evt.LifecycleKind == OptionLifecycleKind.Blocked);
        Assert.Equal(option.Instrument, blocked.Instrument);
        Assert.False(exchange.HasDueWork(expiry));
    }

    [Fact]
    public void SimulatedExchangeRegistry_SettlementReferenceEventUnblocksBlockedOptionLifecycle()
    {
        var optionVenue = new Venue("CBOE");
        var registry = new SimulatedExchangeRegistry(
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney);
        registry.RegisterContract(option);
        var exchange = registry.GetRequired(optionVenue);
        var buy = CreateCommand(orderId: 7705) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(buy, new Qty(1m), new Price(2m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        registry.DrainDueWork(expiry);
        var blocked = Assert.Single(DrainSimulationEvents(registry).OfType<OptionLifecycleApplied>(), evt => evt.LifecycleKind == OptionLifecycleKind.Blocked);
        Assert.Equal(option.Instrument, blocked.Instrument);
        Assert.False(registry.HasDueWork(expiry));

        registry.OnMarketEvent(new SettlementReferencePricePublished(
            underlying,
            new Price(108m, Currency.USD),
            expiry,
            "OCC"));
        var events = DrainSimulationEvents(registry).OfType<OptionLifecycleApplied>().ToArray();

        var exercise = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.Exercise);
        var settlement = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(option.Instrument, exercise.Instrument);
        Assert.Equal(Money.USD(800m), settlement.CashFlow);
        Assert.Equal(OptionLifecycleReferenceSource.UnderlyingSettlementData, settlement.ReferenceSource);
        Assert.Equal(Qty.Zero, exchange.Account.GetPositionQuantity(buy.StrategyId, buy.VariantId, option.Instrument));
        Assert.False(registry.HasDueWork(expiry));
    }

    [Fact]
    public void SimulatedExchangeRegistry_ReplaysPriorSettlementReferenceIntoLateOptionVenue()
    {
        var optionVenue = new Venue("CBOE");
        var registry = new SimulatedExchangeRegistry(
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        registry.OnMarketEvent(new SettlementReferencePricePublished(
            underlying,
            new Price(108m, Currency.USD),
            expiry,
            "OCC"));
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney);
        registry.RegisterContract(option);
        var exchange = registry.GetRequired(optionVenue);
        var buy = CreateCommand(orderId: 7706) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(buy, new Qty(1m), new Price(2m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        registry.DrainDueWork(expiry);
        var events = DrainSimulationEvents(registry).OfType<OptionLifecycleApplied>().ToArray();

        var settlement = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(Money.USD(800m), settlement.CashFlow);
        Assert.Equal(OptionLifecycleReferenceSource.UnderlyingSettlementData, settlement.ReferenceSource);
        Assert.Equal(Qty.Zero, exchange.Account.GetPositionQuantity(buy.StrategyId, buy.VariantId, option.Instrument));
    }

    [Fact]
    public void SimulatedExchangeRegistry_ReplaysPriorMarketMarkIntoLateOptionVenue()
    {
        var optionVenue = new Venue("CBOE");
        var registry = new SimulatedExchangeRegistry(
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        registry.OnMarketEvent(CreateBarClosed(underlying, 108m, 1_796_016_000));
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney);
        registry.RegisterContract(option);
        var exchange = registry.GetRequired(optionVenue);
        var buy = CreateCommand(orderId: 7707) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(buy, new Qty(1m), new Price(2m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        registry.DrainDueWork(expiry);
        var events = DrainSimulationEvents(registry).OfType<OptionLifecycleApplied>().ToArray();

        var settlement = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(Money.USD(800m), settlement.CashFlow);
        Assert.Equal(OptionLifecycleReferenceSource.MarketMark, settlement.ReferenceSource);
        Assert.Equal(Qty.Zero, exchange.Account.GetPositionQuantity(buy.StrategyId, buy.VariantId, option.Instrument));
    }

    [Fact]
    public void SimulatedExchangeRegistry_HydratesExplicitlyAddedVenueWithPriorReferenceData()
    {
        var optionVenue = new Venue("CBOE");
        var registry = new SimulatedExchangeRegistry(
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        registry.OnMarketEvent(new SettlementReferencePricePublished(
            underlying,
            new Price(108m, Currency.USD),
            expiry,
            "OCC"));
        var exchange = new SimulatedVenueExchange(
            optionVenue,
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        registry.AddVenue(exchange);
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney);
        registry.RegisterContract(option);
        var buy = CreateCommand(orderId: 7708) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(buy, new Qty(1m), new Price(2m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        registry.DrainDueWork(expiry);
        var events = DrainSimulationEvents(registry).OfType<OptionLifecycleApplied>().ToArray();

        var settlement = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(Money.USD(800m), settlement.CashFlow);
        Assert.Equal(OptionLifecycleReferenceSource.UnderlyingSettlementData, settlement.ReferenceSource);
    }

    [Fact]
    public void SimulatedExchangeRegistry_HydratesExplicitlyAddedVenueWithPriorAssignmentNotice()
    {
        var optionVenue = new Venue("CBOE");
        var registry = new SimulatedExchangeRegistry(
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), optionVenue);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPX261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry,
            assignmentPolicy: OptionAssignmentPolicy.Random);
        var sell = CreateCommand(orderId: 7709) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Side = Side.Sell,
            Quantity = new Qty(1m)
        };
        registry.OnMarketEvent(new SettlementReferencePricePublished(
            underlying,
            new Price(105m, Currency.USD),
            expiry,
            "OCC"));
        registry.OnMarketEvent(new OptionAssignmentNoticePublished(
            sell.StrategyId,
            sell.VariantId,
            option.Instrument,
            expiry,
            "OCC",
            IsSelectedForRandomAssignment: true));
        var exchange = new SimulatedVenueExchange(
            optionVenue,
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        registry.AddVenue(exchange);
        registry.RegisterContract(option);

        exchange.Account.ApplyFill(sell, new Qty(1m), new Price(2m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        registry.DrainDueWork(expiry);
        var events = DrainSimulationEvents(registry).OfType<OptionLifecycleApplied>().ToArray();

        var assignment = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.Assignment);
        var settlement = Assert.Single(events, evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(new Qty(-1m), assignment.Quantity);
        Assert.Equal(Money.USD(-500m), settlement.CashFlow);
        Assert.Equal(OptionLifecycleReferenceSource.UnderlyingSettlementData, settlement.ReferenceSource);
    }

    [Fact]
    public void SimulatedVenueExchange_OptionExpiryUsesConfiguredSettlementReferenceBeforeMarketMark()
    {
        var optionVenue = new Venue("CBOE");
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Lifecycle = SimulationLifecycleConfig.Default
                .WithSettlementReferencePrice(underlying, new Price(107m, Currency.USD))
        };
        var exchange = new SimulatedVenueExchange(optionVenue, config, Money.USD(100_000m));
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney);
        exchange.RegisterContract(option);
        var buy = CreateCommand(orderId: 7702) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(buy, new Qty(1m), new Price(2m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        exchange.DrainDueWork(expiry);
        var events = DrainSimulationEvents(exchange);

        var settlement = Assert.Single(events.OfType<OptionLifecycleApplied>(), evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(new Price(107m, Currency.USD), settlement.SettlementPrice);
        Assert.Equal(OptionLifecycleReferenceSource.UnderlyingSettlementOverride, settlement.ReferenceSource);
        Assert.Equal(Money.USD(700m), settlement.CashFlow);
        Assert.Equal(Money.USD(100_500m), exchange.Account.Cash);
    }

    [Fact]
    public void SimulatedVenueExchange_OptionExpiryUsesSettlementReferenceEventBeforeConfiguredOverride()
    {
        var optionVenue = new Venue("CBOE");
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var config = SimulationConfig.Instant() with
        {
            AccountType = AccountType.Margin,
            Lifecycle = SimulationLifecycleConfig.Default
                .WithSettlementReferencePrice(underlying, new Price(107m, Currency.USD))
        };
        var exchange = new SimulatedVenueExchange(optionVenue, config, Money.USD(100_000m));
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            optionVenue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney);
        exchange.RegisterContract(option);
        var buy = CreateCommand(orderId: 7703) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        exchange.Account.ApplyFill(buy, new Qty(1m), new Price(2m, Currency.USD), Money.USD(0m), Instant.FromUnixSeconds(1));
        exchange.OnMarketEvent(new SettlementReferencePricePublished(
            underlying,
            new Price(109m, Currency.USD),
            expiry,
            "OCC"));
        var events = DrainSimulationEvents(exchange);

        var settlement = Assert.Single(events.OfType<OptionLifecycleApplied>(), evt => evt.LifecycleKind == OptionLifecycleKind.CashSettlement);
        Assert.Equal(new Price(109m, Currency.USD), settlement.SettlementPrice);
        Assert.Equal(OptionLifecycleReferenceSource.UnderlyingSettlementData, settlement.ReferenceSource);
        Assert.Equal(Money.USD(900m), settlement.CashFlow);
        Assert.Equal(Money.USD(100_700m), exchange.Account.Cash);
    }

    [Fact]
    public void SimulationAccount_PhysicalOptionExpiryDeliversUnderlyingAndStrikeCash()
    {
        var account = new SimulationAccount(Money.USD(20_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var underlyingContract = Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var cashOption = Contracts.OptionContract(
            "SPY261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American);
        var optionTerms = Assert.IsType<PayoffTerms.Option>(cashOption.Payoff).Terms;
        var physicalOption = cashOption with
        {
            Lifecycle = new ContractLifecycle.Expiring(expiry, ExpiryAction.PhysicalDelivery),
            Settlement = new SettlementTerms.Physical(Currency.USD, underlying, SettlementDelay.Immediate()),
            Payoff = new PayoffTerms.Option(optionTerms.With(
                settlementStyle: OptionSettlementStyle.Physical,
                exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney,
                assignmentPolicy: OptionAssignmentPolicy.Random))
        };
        account.RegisterContract(underlyingContract);
        account.RegisterContract(physicalOption);
        var buy = CreateCommand(orderId: 75) with
        {
            Instrument = physicalOption.Instrument,
            Venue = physicalOption.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            buy.StrategyId,
            buy.VariantId,
            physicalOption.Instrument,
            new OptionLifecycleReference(
                new Price(105m, Currency.USD),
                OptionLifecycleReferenceSource.UnderlyingSettlementData),
            expiry);

        var statement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            expiry,
            new Dictionary<Instrument, Price>
            {
                [underlying] = new Price(105m, Currency.USD)
            });

        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(9_800m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, physicalOption.Instrument));
        Assert.Equal(new Qty(100m), account.GetPositionQuantity(buy.StrategyId, buy.VariantId, underlying));
        Assert.Equal(Money.USD(10_500m), statement.MarketValue);
        Assert.Equal(Money.USD(500m), statement.UnrealizedPnL);
        Assert.Equal(Money.USD(-200m), statement.RealizedPnL);
        Assert.Equal(Money.USD(20_300m), statement.Equity);
        Assert.Equal(1, statement.OpenPositions);
        var lifecycleEvents = DrainAccountEvents(account);
        var exercise = Assert.Single(lifecycleEvents.OfType<OptionLifecycleApplied>(), evt => evt.LifecycleKind == OptionLifecycleKind.Exercise);
        var delivery = Assert.Single(lifecycleEvents.OfType<OptionLifecycleApplied>(), evt => evt.LifecycleKind == OptionLifecycleKind.PhysicalDelivery);
        Assert.Equal(physicalOption.Instrument, exercise.Instrument);
        Assert.Equal(OptionLifecycleReferenceSource.UnderlyingSettlementData, exercise.ReferenceSource);
        Assert.Equal(underlying, delivery.Deliverable);
        Assert.Equal(new Qty(100m), delivery.DeliverableQuantity);
        Assert.Equal(Money.USD(-10_000m), delivery.CashFlow);
        Assert.Equal(OptionLifecycleReferenceSource.UnderlyingSettlementData, delivery.ReferenceSource);
    }

    [Fact]
    public void SimulationAccount_PhysicalPutExpiryCreatesShortDeliverableAndStrikeCash()
    {
        var account = new SimulationAccount(Money.USD(20_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var underlyingContract = Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var cashOption = Contracts.OptionContract(
            "SPY261218P00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Put,
            ExerciseStyle.American);
        var optionTerms = Assert.IsType<PayoffTerms.Option>(cashOption.Payoff).Terms;
        var physicalOption = cashOption with
        {
            Lifecycle = new ContractLifecycle.Expiring(expiry, ExpiryAction.PhysicalDelivery),
            Settlement = new SettlementTerms.Physical(Currency.USD, underlying, SettlementDelay.Immediate()),
            Payoff = new PayoffTerms.Option(optionTerms.With(
                settlementStyle: OptionSettlementStyle.Physical,
                exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney,
                assignmentPolicy: OptionAssignmentPolicy.Random))
        };
        account.RegisterContract(underlyingContract);
        account.RegisterContract(physicalOption);
        var buy = CreateCommand(orderId: 7710) with
        {
            Instrument = physicalOption.Instrument,
            Venue = physicalOption.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            buy.StrategyId,
            buy.VariantId,
            physicalOption.Instrument,
            LifecycleReference(new Price(95m, Currency.USD)),
            expiry);

        var statement = account.CreateStatement(
            buy.StrategyId,
            buy.VariantId,
            Currency.USD,
            expiry,
            new Dictionary<Instrument, Price>
            {
                [underlying] = new Price(95m, Currency.USD)
            });

        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(29_800m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, physicalOption.Instrument));
        Assert.Equal(new Qty(-100m), account.GetPositionQuantity(buy.StrategyId, buy.VariantId, underlying));
        Assert.Equal(Money.USD(-9_500m), statement.MarketValue);
        Assert.Equal(Money.USD(500m), statement.UnrealizedPnL);
        Assert.Equal(Money.USD(-200m), statement.RealizedPnL);
        Assert.Equal(Money.USD(20_300m), statement.Equity);
        Assert.Equal(1, statement.OpenPositions);
        var lifecycleEvents = DrainAccountEvents(account);
        var exercise = Assert.Single(lifecycleEvents.OfType<OptionLifecycleApplied>(), evt => evt.LifecycleKind == OptionLifecycleKind.Exercise);
        var delivery = Assert.Single(lifecycleEvents.OfType<OptionLifecycleApplied>(), evt => evt.LifecycleKind == OptionLifecycleKind.PhysicalDelivery);
        Assert.Equal(physicalOption.Instrument, exercise.Instrument);
        Assert.Equal(underlying, delivery.Deliverable);
        Assert.Equal(new Qty(-100m), delivery.DeliverableQuantity);
        Assert.Equal(Money.USD(10_000m), delivery.CashFlow);
    }

    [Fact]
    public void SimulationAccount_OutOfTheMoneyPhysicalOptionExpiryClosesWithoutDelivery()
    {
        var account = new SimulationAccount(Money.USD(20_000m), AccountType.Margin);
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var cashOption = Contracts.OptionContract(
            "SPY261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American);
        var optionTerms = Assert.IsType<PayoffTerms.Option>(cashOption.Payoff).Terms;
        var physicalOption = cashOption with
        {
            Lifecycle = new ContractLifecycle.Expiring(expiry, ExpiryAction.PhysicalDelivery),
            Settlement = new SettlementTerms.Physical(Currency.USD, underlying, SettlementDelay.Immediate()),
            Payoff = new PayoffTerms.Option(optionTerms.With(
                settlementStyle: OptionSettlementStyle.Physical,
                exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney,
                assignmentPolicy: OptionAssignmentPolicy.Random))
        };
        account.RegisterContract(physicalOption);
        var buy = CreateCommand(orderId: 76) with
        {
            Instrument = physicalOption.Instrument,
            Venue = physicalOption.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        account.ApplyFill(
            buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));
        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            buy.StrategyId,
            buy.VariantId,
            physicalOption.Instrument,
            LifecycleReference(new Price(95m, Currency.USD)),
            expiry);

        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(19_800m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, physicalOption.Instrument));
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, underlying));
        var lifecycleEvent = Assert.Single(DrainAccountEvents(account).OfType<OptionLifecycleApplied>());
        Assert.Equal(OptionLifecycleKind.ExpireWorthless, lifecycleEvent.LifecycleKind);
        Assert.Equal(physicalOption.Instrument, lifecycleEvent.Instrument);
    }

    [Fact]
    public void SimulationAccount_InverseCryptoOptionUsesSettlementCurrencyThroughFillAndExpiry()
    {
        var account = new SimulationAccount(new Money(1m, Currency.BTC), AccountType.Margin);
        var underlying = Contracts.CryptoSpot("BTCUSDT", Venue.Binance, Currency.BTC, Currency.USDT, 0.01m, 0.0001m).Instrument;
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.InverseCryptoOption(
            "BTCUSD-C-100K",
            "DERIBIT",
            underlying,
            Currency.BTC,
            Currency.USD,
            Currency.BTC,
            tick: 0.1m,
            lot: 0.001m,
            multiplier: 1m,
            strike: new Price(100_000m, Currency.USD),
            expiry,
            OptionRight.Call,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        account.RegisterContract(option);
        var buy = CreateCommand(orderId: 879) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        var accepted = account.TryReserve(
            buy,
            new Price(50_000m, Currency.USD),
            MarginParams.RegT(),
            SettlementParams.Immediate(),
            allowCashBorrowing: false,
            out var reason);
        account.ApplyFill(buy, new Qty(1m), new Price(50_000m, Currency.USD), Money.Zero(Currency.BTC), Instant.FromUnixSeconds(1));
        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            buy.StrategyId,
            buy.VariantId,
            option.Instrument,
            LifecycleReference(new Price(120_000m, Currency.USD)),
            expiry);

        var premium = 1m / 50_000m;
        var payoff = (1m / 100_000m) - (1m / 120_000m);
        Assert.True(accepted, reason);
        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(1m - premium + payoff, account.Cash.Amount, precision: 18);
        Assert.Equal(Currency.BTC, account.Cash.Currency);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, option.Instrument));
    }

    [Fact]
    public void SimulationAccount_QuantoCryptoOptionUsesSettlementCurrencyThroughFillAndExpiry()
    {
        var account = new SimulationAccount(Money.USD(100_000m), AccountType.Margin);
        var underlying = Contracts.CryptoSpot("BTCUSDT", Venue.Binance, Currency.BTC, Currency.USDT, 0.01m, 0.0001m).Instrument;
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.QuantoCryptoOption(
            "BTCUSDT-C-100K-USD",
            "DERIBIT",
            underlying,
            Currency.BTC,
            Currency.USDT,
            Currency.USD,
            conversionRate: 0.5m,
            tick: 0.1m,
            lot: 0.001m,
            multiplier: 1m,
            strike: new Price(100_000m, Currency.USDT),
            expiry,
            OptionRight.Call,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        account.RegisterContract(option);
        var buy = CreateCommand(orderId: 880) with
        {
            Instrument = option.Instrument,
            Venue = option.Instrument.Venue,
            Quantity = new Qty(2m)
        };

        var accepted = account.TryReserve(
            buy,
            new Price(2_000m, Currency.USDT),
            MarginParams.RegT(),
            SettlementParams.Immediate(),
            allowCashBorrowing: false,
            out var reason);
        account.ApplyFill(buy, new Qty(2m), new Price(2_000m, Currency.USDT), Money.Zero(Currency.USD), Instant.FromUnixSeconds(1));
        var applied = ProcessAndApplyOptionLifecycleResult(
            account,
            buy.StrategyId,
            buy.VariantId,
            option.Instrument,
            LifecycleReference(new Price(120_000m, Currency.USDT)),
            expiry);

        Assert.True(accepted, reason);
        Assert.Equal(OptionLifecycleApplicationStatus.Completed, applied);
        Assert.Equal(Money.USD(118_000m), account.Cash);
        Assert.Equal(Qty.Zero, account.GetPositionQuantity(buy.StrategyId, buy.VariantId, option.Instrument));
    }

    [Fact]
    public void SimulationBuilder_UsesVenueSpecificInitialCash()
    {
        var result = Rhodium.Simulate<SessionMarketBuyStrategy>()
            .WithHistory(SharedHistory.Load([CreateBarClosed(123m)]))
            .WithVenue(Venue.NASDAQ, initialCash: Money.USD(1_000m))
            .Run();

        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        var venue = Assert.Single(result.Diagnostics.Venues);
        Assert.Equal(Venue.NASDAQ, venue.Venue);
        Assert.Equal(MarketStatus.Open, venue.Status);
        Assert.Equal(AccountType.Cash, venue.AccountType);
        Assert.Equal(1, venue.InstrumentCount);
        Assert.Equal(Money.USD(877m), venue.Cash);
    }

    [Fact]
    public void SimulationBuilder_AppliesAccountSeedCashBeforeFirstOrder()
    {
        var result = Rhodium.Simulate<SessionMarketBuyStrategy>()
            .WithInitialCash(Money.USD(0m))
            .WithAccountSeed(new AccountSeed(
                Venue.NASDAQ,
                [Money.USD(200m)],
                []))
            .WithHistory(SharedHistory.Load([CreateBarClosed(123m)]))
            .Run();

        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.DoesNotContain(result.ExecutionEvents, static evt => evt is OrderRejected);
        Assert.Contains(result.SimulatorEvents.OfType<AccountTransferCompleted>(), transfer =>
            transfer.TransferType == AccountTransferType.CashDeposit
            && transfer.CashAmount == Money.USD(200m));
        Assert.Contains(result.AccountStatements, statement => statement.Cash == Money.USD(200m));
        Assert.Equal(Money.USD(77m), result.Diagnostics.Venues.Single().Cash);
    }

    [Fact]
    public void SimulationBuilder_AppliesAccountSeedPositionAsSettledCustodyBeforeFirstOrder()
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);

        var result = Rhodium.Simulate<SessionMarketSellStrategy>()
            .WithInitialCash(Money.USD(0m))
            .WithAccountSeed(new AccountSeed(
                Venue.NASDAQ,
                [],
                [new SeedPosition(instrument, new Qty(1m), new Price(50m, Currency.USD))]))
            .WithHistory(SharedHistory.Load([CreateBarClosed(instrument, 123m)]))
            .Run();

        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(Side.Sell, fill.Side);
        Assert.DoesNotContain(result.ExecutionEvents, static evt => evt is OrderRejected);
        Assert.Contains(result.SimulatorEvents.OfType<AccountTransferCompleted>(), transfer =>
            transfer.TransferType == AccountTransferType.AssetDeposit
            && transfer.Instrument == instrument
            && transfer.Quantity == new Qty(1m)
            && transfer.CarryingPrice == new Price(50m, Currency.USD));
        Assert.Empty(result.Runs.Single().FinalSnapshot.GetPositions().ToArray());
        Assert.Equal(Money.USD(123m), result.Diagnostics.Venues.Single().Cash);
    }

    [Fact]
    public void SimulationBuilder_RunsOptionFillAndExpiryThroughStrategyRuntime()
    {
        RuntimeOptionExpiryStrategy.Reset();

        var result = Rhodium.Simulate<RuntimeOptionExpiryStrategy>()
            .WithInitialCash(Money.USD(100_000m))
            .WithHistory(SharedHistory.Load([
                CreateBarClosed(RuntimeOptionExpiryStrategy.Option.Instrument, 2m, unixSeconds: 1),
                CreateBarClosed(RuntimeOptionExpiryStrategy.Underlying, 105m, unixSeconds: 1_796_016_000)
            ]))
            .Run();

        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.DoesNotContain(result.ExecutionEvents, static evt => evt is OrderRejected);
        Assert.Contains(result.SimulatorEvents.OfType<OptionLifecycleApplied>(), evt =>
            evt.Instrument == RuntimeOptionExpiryStrategy.Option.Instrument
            && evt.LifecycleKind == OptionLifecycleKind.CashSettlement
            && evt.CashFlow == Money.USD(500m));
        Assert.Empty(result.Runs.Single().FinalSnapshot.GetPositions().ToArray());
        Assert.Contains(result.AccountStatements, statement =>
            statement.Cash == Money.USD(100_300m)
            && statement.RealizedPnL == Money.USD(300m));
    }

    [Fact]
    public void SimulationBuilder_RunsOptionSpreadPackageFillAndExpiryThroughStrategyRuntime()
    {
        RuntimeOptionSpreadExpiryStrategy.Reset();

        var result = Rhodium.Simulate<RuntimeOptionSpreadExpiryStrategy>()
            .WithInitialCash(Money.USD(100_000m))
            .WithHistory(SharedHistory.Load([
                CreateBarClosed(RuntimeOptionSpreadExpiryStrategy.ShortCall.Instrument, 5m, unixSeconds: 1),
                CreateBarClosed(RuntimeOptionSpreadExpiryStrategy.LongCall.Instrument, 2m, unixSeconds: 1),
                CreateBarClosed(RuntimeOptionSpreadExpiryStrategy.Spread.Instrument, -3m, unixSeconds: 1),
                CreateBarClosed(RuntimeOptionSpreadExpiryStrategy.Underlying, 103m, unixSeconds: 1_796_016_000)
            ]))
            .Run();

        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(RuntimeOptionSpreadExpiryStrategy.Spread.Instrument, fill.Instrument);
        Assert.Equal(new Price(-3m, Currency.USD), fill.FillPrice);
        Assert.Equal(2, result.ExecutionEvents.OfType<PackageLegFilled>().Count());
        Assert.DoesNotContain(result.ExecutionEvents, static evt => evt is OrderRejected);
        Assert.Contains(result.SimulatorEvents.OfType<OptionLifecycleApplied>(), evt =>
            evt.Instrument == RuntimeOptionSpreadExpiryStrategy.ShortCall.Instrument
            && evt.LifecycleKind == OptionLifecycleKind.CashSettlement
            && evt.CashFlow == Money.USD(-300m));
        Assert.Contains(result.SimulatorEvents.OfType<OptionLifecycleApplied>(), evt =>
            evt.Instrument == RuntimeOptionSpreadExpiryStrategy.LongCall.Instrument
            && evt.LifecycleKind == OptionLifecycleKind.ExpireWorthless);
        Assert.Empty(result.Runs.Single().FinalSnapshot.GetPositions().ToArray());
        Assert.Contains(result.AccountStatements, statement =>
            statement.Cash == Money.USD(100_000m)
            && statement.RealizedPnL == Money.USD(0m));
    }

    [Fact]
    public void SimulationBuilder_UsesVenueSpecificBaseCurrency()
    {
        var result = Rhodium.Simulate<SessionNoopUniverseStrategy>()
            .WithInitialCash(Money.USD(12_345m))
            .WithHistory(SharedHistory.Load([CreateBarClosed(123m)]))
            .WithVenue(Venue.NASDAQ, baseCurrency: Currency.EUR)
            .Run();

        var venue = Assert.Single(result.Diagnostics.Venues);
        Assert.Equal(Currency.EUR, venue.BaseCurrency);
        Assert.Equal(new Money(12_345m, Currency.EUR), venue.Cash);
        Assert.Equal(new Money(12_345m, Currency.EUR), venue.AvailableCash);
    }

    [Fact]
    public void SimulationBuilder_UsesVenueSpecificAccountType()
    {
        var result = Rhodium.Simulate<SessionLargeMarketBuyStrategy>()
            .WithHistory(SharedHistory.Load([CreateBarClosed(123m)]))
            .WithVenue(
                Venue.NASDAQ,
                initialCash: Money.USD(1_000m),
                accountType: AccountType.Margin,
                config: SimulationConfig.Instant() with
                {
                    Margin = MarginParams.Leverage(10m)
                })
            .Run();

        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        var venue = Assert.Single(result.Diagnostics.Venues);
        Assert.Equal(AccountType.Margin, venue.AccountType);
        Assert.True(venue.Cash.Amount < 0m);
    }

    [Fact]
    public void SimulationSession_ModulesPreProcessAndEmitSameTimestampEventsThroughSession()
    {
        var module = new CloseVenueAfterFirstTimestampModule();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionMarketBuyStrategy>();

        var result = session.Run(
            SharedHistory.Load(
            [
                CreateBarClosed(100m, unixSeconds: 1),
                CreateBarClosed(123m, unixSeconds: 2)
            ]),
            new SimulationRunOptions { SessionModules = [module] });

        Assert.Equal(2, module.PreProcessCalls);
        Assert.Equal(2, module.ProcessCalls);
        Assert.True(module.ObservedVenue);
        Assert.True(module.ObservedInstrument);
        var rejection = Assert.Single(result.ExecutionEvents.OfType<OrderRejected>());
        Assert.Contains("Closed", rejection.Reason);
        Assert.Empty(result.ExecutionEvents.OfType<OrderFilled>());

        var venue = Assert.Single(result.Diagnostics.Venues);
        Assert.Equal(MarketStatus.Closed, venue.Status);
        Assert.Equal(1, venue.InstrumentCount);
        var diagnostics = Assert.Single(result.Diagnostics.Modules);
        Assert.Equal(nameof(CloseVenueAfterFirstTimestampModule), diagnostics.ModuleName);
        Assert.Equal(2, diagnostics.PreProcessCalls);
        Assert.Equal(2, diagnostics.ProcessCalls);
        Assert.Equal(1, diagnostics.EmittedEvents);
    }

    [Fact]
    public void SimulationSession_ModulesProcessAfterAllSameTimestampEvents()
    {
        var module = new CountingSameTimestampModule();
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        _ = session.Run(
            SharedHistory.Load(
            [
                CreateBarClosed(instrument, 100m, unixSeconds: 1),
                CreateBarClosed(instrument, 101m, unixSeconds: 1)
            ]),
            new SimulationRunOptions { SessionModules = [module] });

        Assert.Equal(2, module.PreProcessCalls);
        Assert.Equal(1, module.ProcessCalls);
        Assert.Equal(2, module.EventsSeenBeforeFirstProcess);
    }

    [Fact]
    public void SimulationSession_StructFramesAreDisabledByDefault()
    {
        using var session = new SimulationSession();
        using var inbox = session.Frames.Quotes.CreateInbox();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        var result = session.Run(SharedHistory.Load([CreateQuoteReceived(100m, 101m, unixSeconds: 1)]));

        Assert.False(inbox.TryRead(out _));
        Assert.Equal(0, result.Diagnostics.FrameStats.Emitted);
    }

    [Fact]
    public void SimulationSession_ProjectsQuoteIntoStructFrameWhenEnabled()
    {
        using var session = new SimulationSession();
        using var inbox = session.Frames.Quotes.CreateInbox();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        var result = session.Run(
            SharedHistory.Load([CreateQuoteReceived(100m, 101m, unixSeconds: 1)]),
            new SimulationRunOptions { FrameMode = SimulationFrameMode.MarketData });

        Assert.True(inbox.TryRead(out var frame));
        Assert.Equal(0, frame.InstrumentIndex);
        Assert.Equal(100_000_000L, frame.BidTicks);
        Assert.Equal(101_000_000L, frame.AskTicks);
        Assert.Equal(1_000_000L, frame.BidSizeLots);
        Assert.Equal(2_000_000L, frame.AskSizeLots);
        Assert.Equal(Instant.FromUnixSeconds(1).Nanos, frame.TimestampNs);
        Assert.True(frame.SequenceNumber > 0);
        Assert.Equal(1, result.Diagnostics.FrameStats.Emitted);
    }

    [Fact]
    public void SimulationSession_ProjectsBookDepth10IntoDepthLevelFrames()
    {
        var bids = Enumerable.Range(0, 10)
            .Select(i => new Level(new Price(100m - i, Currency.USD), new Qty(i + 1m), i + 2))
            .ToArray();
        var asks = Enumerable.Range(0, 10)
            .Select(i => new Level(new Price(101m + i, Currency.USD), new Qty(i + 11m), i + 12))
            .ToArray();
        using var session = new SimulationSession();
        using var inbox = session.Frames.BookDepthLevels.CreateInbox();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        _ = session.Run(
            SharedHistory.Load([CreateBookDepth10(bids, asks, unixSeconds: 1)]),
            new SimulationRunOptions { FrameMode = SimulationFrameMode.MarketData });

        Span<BookDepthLevelFrame> frames = stackalloc BookDepthLevelFrame[20];
        var count = inbox.TryReadBatch(frames);
        Assert.Equal(20, count);
        Assert.Equal(Side.Buy, frames[0].Side);
        Assert.Equal(0, frames[0].LevelIndex);
        Assert.Equal(100_000_000L, frames[0].PriceTicks);
        Assert.Equal(1_000_000L, frames[0].SizeLots);
        Assert.Equal(2, frames[0].OrderCount);
        Assert.Equal(Side.Sell, frames[10].Side);
        Assert.Equal(0, frames[10].LevelIndex);
        Assert.Equal(101_000_000L, frames[10].PriceTicks);
        Assert.Equal(11_000_000L, frames[10].SizeLots);
    }

    [Fact]
    public void SimulationSession_ProjectsBookOrderEventsIntoL3StructFrames()
    {
        using var session = new SimulationSession();
        using var addedInbox = session.Frames.BookOrderAdds.CreateInbox();
        using var executedInbox = session.Frames.BookOrderExecutions.CreateInbox();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        _ = session.Run(
            SharedHistory.Load(
            [
                CreateBookOrderAdded(7, Side.Sell, 101m, 3m, venueSequence: 1),
                new BookOrderExecuted(
                    new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ),
                    new BookOrderId(7),
                    new Qty(2m),
                    VenueSequence: 2)
                {
                    Time = Instant.FromUnixSeconds(1)
                }
            ]),
            new SimulationRunOptions { FrameMode = SimulationFrameMode.MarketData });

        Assert.True(addedInbox.TryRead(out var added));
        Assert.Equal(0, added.InstrumentIndex);
        Assert.Equal(7, added.OrderId);
        Assert.Equal(Side.Sell, added.Side);
        Assert.Equal(101_000_000L, added.PriceTicks);
        Assert.Equal(3_000_000L, added.SizeLots);
        Assert.Equal(1, added.VenueSequence);

        Assert.True(executedInbox.TryRead(out var executed));
        Assert.Equal(7, executed.OrderId);
        Assert.Equal(2_000_000L, executed.ExecutedLots);
        Assert.Equal(2, executed.VenueSequence);
    }

    [Fact]
    public void SimulationSession_ProjectsFillIntoExecutionFrameWhenEnabled()
    {
        using var session = new SimulationSession();
        using var inbox = session.Frames.Fills.CreateInbox();
        session.RegisterStrategy<SessionMarketBuyStrategy>();

        var result = session.Run(
            SharedHistory.Load([CreateBarClosed(123m)]),
            new SimulationRunOptions { FrameMode = SimulationFrameMode.Execution });

        Assert.True(inbox.TryRead(out var frame));
        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(fill.StrategyId.Value, frame.StrategyIndex);
        Assert.Equal(0, frame.VariantId);
        Assert.Equal(0, frame.InstrumentIndex);
        Assert.Equal(1, frame.ClientOrderId);
        Assert.Equal(1, frame.VenueOrderId);
        Assert.Equal(1, frame.ExecutionId);
        Assert.Equal(Side.Buy, frame.Side);
        Assert.Equal(123_000_000L, frame.FillPriceTicks);
        Assert.Equal(1_000_000L, frame.FillQuantityLots);
    }

    [Fact]
    public void SimulationSession_ModuleFrameSinkEmitsToStructEventInbox()
    {
        var module = new FrameEmittingModule();
        using var session = new SimulationSession();
        using var inbox = session.Frames.Quotes.CreateInbox();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        var result = session.Run(
            SharedHistory.Load([CreateBarClosed(100m)]),
            new SimulationRunOptions
            {
                SessionModules = [module],
                FrameMode = SimulationFrameMode.All
            });

        Assert.True(inbox.TryRead(out var frame));
        Assert.Equal(42, frame.InstrumentIndex);
        Assert.True(frame.SequenceNumber > 0);
        var diagnostics = Assert.Single(result.Diagnostics.Modules);
        Assert.Equal(1, diagnostics.EmittedFrames);
    }

    [Fact]
    public void SimulationSession_ModuleFrameSinkRespectsFrameModeCategories()
    {
        var module = new MixedFrameEmittingModule();
        using var session = new SimulationSession();
        using var quoteInbox = session.Frames.Quotes.CreateInbox();
        using var fillInbox = session.Frames.Fills.CreateInbox();
        using var riskInbox = session.Frames.RiskMetrics.CreateInbox();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        var result = session.Run(
            SharedHistory.Load([CreateBarClosed(100m)]),
            new SimulationRunOptions
            {
                SessionModules = [module],
                FrameMode = SimulationFrameMode.MarketData
            });

        Assert.True(quoteInbox.TryRead(out _));
        Assert.False(fillInbox.TryRead(out _));
        Assert.False(riskInbox.TryRead(out _));
        var diagnostics = Assert.Single(result.Diagnostics.Modules);
        Assert.Equal(1, diagnostics.EmittedFrames);
    }

    [Fact]
    public void SimulationSession_ModuleCommandSinkRoutesThroughExchange()
    {
        var spy = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        using var session = new SimulationSession();
        var strategyId = session.RegisterStrategy<SessionNoopUniverseStrategy>();
        var module = new CommandSubmittingModule(strategyId, spy);

        var result = session.Run(
            SharedHistory.Load([CreateBarClosed(spy, 100m, unixSeconds: 1)]),
            new SimulationRunOptions { SessionModules = [module] });

        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new OrderId(99), fill.OrderId);
        Assert.Equal(strategyId, fill.StrategyId);
        var diagnostics = Assert.Single(result.Diagnostics.Modules);
        Assert.Equal(1, diagnostics.SubmittedCommands);
    }

    [Fact]
    public void SimulationSession_ModuleOwnedDiagnosticsAppearInResult()
    {
        var module = new DiagnosticModule();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        var result = session.Run(
            SharedHistory.Load([CreateBarClosed(100m)]),
            new SimulationRunOptions { SessionModules = [module] });

        var diagnostics = Assert.Single(result.Diagnostics.Modules);
        Assert.Contains(diagnostics.Counters, counter => counter is
        {
            ModuleName: nameof(DiagnosticModule),
            CounterName: "events_seen",
            Value: 1
        });
        Assert.Contains(diagnostics.Metrics, metric => metric is
        {
            ModuleName: nameof(DiagnosticModule),
            MetricName: "health",
            Value: 1.0
        });
        Assert.Contains(diagnostics.Messages, message => message is
        {
            ModuleName: nameof(DiagnosticModule),
            Code: "ok",
            Message: "module healthy"
        });
    }

    [Fact]
    public void SimulationSession_ScopedModulesOnlySeeMatchingEvents()
    {
        var spy = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var sessionModule = new CountingScopedSessionModule();
        var nasdaqModule = new CountingVenueModule(Venue.NASDAQ);
        var spyModule = new CountingInstrumentModule(spy);
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionNoopUniverseStrategy>();

        _ = session.Run(
            SharedHistory.Load(
            [
                new VenueStatusChanged(Venue.NASDAQ, MarketStatus.Open, "test") { Time = Instant.FromUnixSeconds(1) },
                new VenueStatusChanged(Venue.NYSE, MarketStatus.Open, "test") { Time = Instant.FromUnixSeconds(1) },
                CreateBarClosed(spy, 100m, unixSeconds: 1)
            ]),
            new SimulationRunOptions
            {
                SessionModules = [sessionModule],
                VenueModules = [nasdaqModule],
                InstrumentModules = [spyModule]
            });

        Assert.Equal(3, sessionModule.PreProcessCalls);
        Assert.Equal(2, nasdaqModule.PreProcessCalls);
        Assert.Equal(1, spyModule.PreProcessCalls);
        Assert.Equal(1, sessionModule.ProcessCalls);
        Assert.Equal(1, nasdaqModule.ProcessCalls);
        Assert.Equal(1, spyModule.ProcessCalls);
    }

    [Fact]
    public void SimulationSession_RoutesStrategyCancelIntentThroughExchange()
    {
        SessionCancelAcceptedStrategy.Reset();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionCancelAcceptedStrategy>();

        var result = session.Run(SharedHistory.Load([CreateBarClosed(100m)]));

        Assert.Single(result.OrderIntents, static intent => intent.Kind == OrderIntentKind.Submit);
        Assert.Single(result.OrderIntents, static intent => intent.Kind == OrderIntentKind.Cancel);
        Assert.Single(result.ExecutionEvents.OfType<OrderAccepted>());
        var cancelled = Assert.Single(result.ExecutionEvents.OfType<OrderCancelled>());
        Assert.Equal("strategy cancel", cancelled.Reason);
        Assert.Empty(result.ExecutionEvents.OfType<OrderFilled>());
    }

    [Fact]
    public void SimulationSession_RoutesStrategyModifyIntentThroughExchange()
    {
        SessionModifyAcceptedStrategy.Reset();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionModifyAcceptedStrategy>();

        var result = session.Run(SharedHistory.Load(
        [
            CreateBarClosed(100m, unixSeconds: 1),
            CreateBarClosed(100m, unixSeconds: 2)
        ]));

        Assert.Single(result.OrderIntents, static intent => intent.Kind == OrderIntentKind.Submit);
        Assert.Single(result.OrderIntents, static intent => intent.Kind == OrderIntentKind.Modify);
        var modified = Assert.Single(result.ExecutionEvents.OfType<OrderModified>());
        Assert.Equal(new Price(101m, Currency.USD), modified.NewLimitPrice);
        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Price(101m, Currency.USD), fill.FillPrice);
    }

    [Fact]
    public void SimulationSession_RoutesFillCallbackIntentThroughExchangeBeforeTimeAdvances()
    {
        SessionSellOnFillStrategy.Reset();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionSellOnFillStrategy>();

        var result = session.Run(SharedHistory.Load(
        [
            CreateBarClosed(100m, unixSeconds: 1),
            CreateBarClosed(101m, unixSeconds: 2)
        ]));

        Assert.Equal(2, result.OrderIntents.Count(static intent => intent.Kind == OrderIntentKind.Submit));
        var fills = result.ExecutionEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal([Side.Buy, Side.Sell], fills.Select(static fill => fill.Side).ToArray());
        Assert.All(fills, fill => Assert.Equal(Instant.FromUnixSeconds(1), fill.Time));
    }

    [Fact]
    public void SimulationSession_MaxSameTimestampIterationsStopsRunawayFillCascade()
    {
        SessionRunawayFillStrategy.Reset();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionRunawayFillStrategy>();

        var ex = Assert.Throws<InvalidOperationException>(() => session.Run(
            SharedHistory.Load([CreateBarClosed(100m, unixSeconds: 1)]),
            new SimulationRunOptions { MaxSameTimestampIterations = 4 }));

        Assert.Contains("max same-timestamp iterations", ex.Message);
    }

    [Fact]
    public void SimulationSession_ExpiresStrategyGtdOrderBeforeLaterMarketDataCanFill()
    {
        SessionGtdExpiryStrategy.Reset();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionGtdExpiryStrategy>();

        var result = session.Run(SharedHistory.Load(
        [
            CreateBarClosed(100m, unixSeconds: 1),
            CreateBarClosed(100m, unixSeconds: 2),
            CreateBarClosed(90m, unixSeconds: 3)
        ]));

        Assert.Single(result.ExecutionEvents.OfType<OrderAccepted>());
        var expired = Assert.Single(result.ExecutionEvents.OfType<OrderExpired>());
        Assert.Equal(new AssetId(0), expired.AssetId);
        Assert.Empty(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(1, SessionGtdExpiryStrategy.ExpiredCount);
        var instrument = Assert.Single(result.Diagnostics.Instruments);
        Assert.Equal(0, instrument.OpenOrders);
        Assert.Equal(1, instrument.ExpiredOrders);
        var venue = Assert.Single(result.Diagnostics.Venues);
        Assert.Equal(1, venue.ExpiredOrders);
    }

    [Fact]
    public void SimulatedVenueExchange_RejectsInvalidPostOnlyMarketOrder()
    {
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, SimulationConfig.Instant(), Money.USD(1_000m));
        var command = CreateCommand(orderId: 20) with
        {
            Execution = Execution.Market().WithPostOnly()
        };

        venue.OnMarketEvent(CreateBarClosed(123m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("Post-only market", rejection.Reason);
        Assert.Empty(events.OfType<OrderAccepted>());
    }

    [Fact]
    public void SimulatedVenueExchange_OrderPolicyRejectsDisallowedOrderType()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            orderPolicy: new SimulationOrderPolicy
            {
                AllowedOrderTypes = new HashSet<OrderType> { OrderType.Limit }
            });
        var command = CreateCommand(orderId: 120) with
        {
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(123m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("does not allow Market", rejection.Reason);
        Assert.Empty(events.OfType<OrderAccepted>());
        Assert.Empty(events.OfType<OrderFilled>());
    }

    [Fact]
    public void SimulatedVenueExchange_OrderPolicyRejectsBelowMinimumNotional()
    {
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            orderPolicy: new SimulationOrderPolicy
            {
                MinOrderNotional = Money.USD(500m)
            });
        var command = CreateCommand(orderId: 121) with
        {
            Quantity = new Qty(2m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("minimum order notional", rejection.Reason);
        Assert.Empty(events.OfType<OrderFilled>());
    }

    [Fact]
    public void SimulatedVenueExchange_InstrumentOrderPolicyOverridesVenuePolicy()
    {
        var contract = Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD);
        var instrument = contract.Instrument;
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            SimulationConfig.Instant(),
            Money.USD(1_000m),
            orderPolicy: new SimulationOrderPolicy
            {
                AllowedOrderTypes = new HashSet<OrderType> { OrderType.Limit }
            },
            instrumentConfigs:
            [
                new SimulationInstrumentConfig
                {
                    Instrument = instrument,
                    Contract = contract,
                    OrderPolicy = new SimulationOrderPolicy
                    {
                        AllowedOrderTypes = new HashSet<OrderType> { OrderType.Market }
                    }
                }
            ]);
        var command = CreateCommand(orderId: 122) with
        {
            Instrument = instrument,
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(instrument, 123m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        Assert.Empty(events.OfType<OrderRejected>());
        Assert.Single(events.OfType<OrderFilled>());
    }

    [Fact]
    public void SimulatedVenueExchange_CancelsUnfillableIocLimitWithoutResting()
    {
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, SimulationConfig.Instant(), Money.USD(1_000m));
        var command = CreateCommand(orderId: 21) with
        {
            Quantity = new Qty(2m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD)).ImmediateOrCancel()
        };

        venue.OnMarketEvent(CreateBarClosed(123m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderAccepted>());
        var cancelled = Assert.Single(events.OfType<OrderCancelled>());
        Assert.Equal(new Qty(2m), cancelled.RemainingQty);
        Assert.Equal(Money.USD(0m), venue.Account.ReservedCash);
    }

    [Fact]
    public void SimulatedVenueExchange_FillsImmediateLimitWhenCurrentEventCrosses()
    {
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, SimulationConfig.Instant(), Money.USD(1_000m));
        var command = CreateCommand(orderId: 22) with
        {
            Quantity = new Qty(2m),
            Execution = Execution.Limit().At(new Price(125m, Currency.USD)).ImmediateOrCancel()
        };

        venue.OnMarketEvent(CreateBarClosed(123m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderAccepted>());
        Assert.Single(events.OfType<OrderFilled>());
        Assert.Empty(events.OfType<OrderCancelled>());
        Assert.Equal(Money.USD(750m), venue.Account.Cash);
        Assert.Equal(Money.USD(0m), venue.Account.ReservedCash);
    }

    [Fact]
    public void SimulationAccount_KeepsRemainingReservationAfterPartialFill()
    {
        var config = SimulationConfig.Instant() with
        {
            FillBehavior = FillBehavior.PartialFillOnTrade,
            QueueModel = QueueParams.AlwaysFront()
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(2_000m));
        var command = CreateCommand(orderId: 10) with
        {
            Quantity = new Qty(10m),
            Execution = Execution.Limit().At(new Price(123m, Currency.USD))
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(command, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        venue.OnMarketEvent(CreateTradeOccurred(123m, 3m, Side.Sell));

        var buffer = new ExecutionEvent[4];
        var count = venue.DrainExecutionEvents(buffer);
        var events = buffer.AsSpan(0, count).ToArray();

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(new Qty(3m), fill.FilledQty);
        Assert.Equal(Money.USD(1_631m), venue.Account.Cash);
        Assert.Equal(Money.USD(861m), venue.Account.ReservedCash);
        Assert.Equal(Money.USD(770m), venue.Account.AvailableCash);
    }

    [Fact]
    public void SimulationVenuePolicy_ConsumesTradeLiquidityAcrossRestingOrders()
    {
        var config = SimulationConfig.Instant() with
        {
            FillBehavior = FillBehavior.PartialFillOnTrade,
            QueueModel = QueueParams.AlwaysFront()
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(5_000m));
        var first = CreateCommand(orderId: 63) with
        {
            Quantity = new Qty(4m),
            Execution = Execution.Limit().At(new Price(123m, Currency.USD))
        };
        var second = CreateCommand(orderId: 64) with
        {
            Quantity = new Qty(4m),
            Execution = Execution.Limit().At(new Price(123m, Currency.USD))
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(first, Instant.FromUnixSeconds(1));
        venue.Submit(second, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.OnMarketEvent(CreateTradeOccurred(123m, 5m, Side.Sell));
        var events = DrainEvents(venue);
        var fills = events.OfType<OrderFilled>().ToArray();

        Assert.Equal(2, fills.Length);
        Assert.Equal(new Qty(4m), fills[0].FilledQty);
        Assert.Equal(new Qty(1m), fills[1].FilledQty);
        Assert.Equal(Money.USD(4_385m), venue.Account.Cash);
        Assert.Equal(Money.USD(369m), venue.Account.ReservedCash);
    }

    [Fact]
    public void SimulationVenuePolicy_CanDisableTradeLiquidityConsumption()
    {
        var config = SimulationConfig.Instant() with
        {
            FillBehavior = FillBehavior.PartialFillOnTrade,
            QueueModel = QueueParams.AlwaysFront()
        };
        var venue = new SimulatedVenueExchange(
            Venue.NASDAQ,
            config,
            Money.USD(5_000m),
            simulationPolicy: new SimulationVenuePolicy { LiquidityConsumption = false });
        var first = CreateCommand(orderId: 65) with
        {
            Quantity = new Qty(4m),
            Execution = Execution.Limit().At(new Price(123m, Currency.USD))
        };
        var second = CreateCommand(orderId: 66) with
        {
            Quantity = new Qty(4m),
            Execution = Execution.Limit().At(new Price(123m, Currency.USD))
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(first, Instant.FromUnixSeconds(1));
        venue.Submit(second, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.OnMarketEvent(CreateTradeOccurred(123m, 5m, Side.Sell));
        var events = DrainEvents(venue);
        var fills = events.OfType<OrderFilled>().ToArray();

        Assert.Equal(2, fills.Length);
        Assert.All(fills, fill => Assert.Equal(new Qty(4m), fill.FilledQty));
        Assert.Equal(Money.USD(4_016m), venue.Account.Cash);
        Assert.Equal(Money.USD(0m), venue.Account.ReservedCash);
    }

    [Fact]
    public void SimulationAccount_DelaysCashSellProceedsUntilSettlement()
    {
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.CalendarDays(1)
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var buy = CreateCommand(orderId: 29) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };
        var sell = CreateCommand(orderId: 30) with
        {
            Side = Side.Sell,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m, unixSeconds: 1));
        venue.Submit(buy, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);
        var buyAccountEvents = DrainSimulationEvents(venue);

        var scheduledDelivery = Assert.Single(buyAccountEvents.OfType<AssetDeliveryScheduled>());
        Assert.Equal(buy.Instrument, scheduledDelivery.Instrument);
        Assert.Contains(
            buyAccountEvents.OfType<AssetDeliveryStatusSnapshot>(),
            status => status.DeliveryId == scheduledDelivery.DeliveryId
                && status.Status == AssetDeliveryStatus.Scheduled);

        venue.OnMarketEvent(CreateBarClosed(101m, unixSeconds: 90_000));
        var deliveredEvents = DrainSimulationEvents(venue);

        var delivered = Assert.Single(deliveredEvents.OfType<AssetDelivered>());
        Assert.Equal(scheduledDelivery.DeliveryId, delivered.DeliveryId);
        Assert.Contains(
            deliveredEvents.OfType<AssetDeliveryStatusSnapshot>(),
            status => status.DeliveryId == scheduledDelivery.DeliveryId
                && status.Status == AssetDeliveryStatus.Delivered);

        venue.Submit(sell, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(90_000));
        _ = DrainEvents(venue);
        var sellAccountEvents = DrainSimulationEvents(venue);

        var scheduledSettlement = Assert.Single(sellAccountEvents.OfType<SettlementScheduled>());
        Assert.Equal(sell.StrategyId, scheduledSettlement.StrategyId);
        Assert.Equal(Money.USD(101m), scheduledSettlement.Amount);
        Assert.Contains(
            sellAccountEvents.OfType<SettlementStatusSnapshot>(),
            status => status.SettlementId == scheduledSettlement.SettlementId
                && status.Status == SettlementStatus.Scheduled);

        Assert.Equal(Money.USD(900m), venue.Account.Cash);
        Assert.Equal(Money.USD(101m), venue.Account.PendingSettlement);

        var statement = venue.Account.CreateStatement(
            sell.StrategyId,
            sell.VariantId,
            Currency.USD,
            Instant.FromUnixSeconds(90_000));
        Assert.Equal(Money.USD(101m), statement.PendingSettlement);
        Assert.Equal(Money.USD(1_001m), statement.Equity);

        venue.OnMarketEvent(CreateBarClosed(102m, unixSeconds: 180_000));
        var releasedEvents = DrainSimulationEvents(venue);

        var released = Assert.Single(releasedEvents.OfType<SettlementReleased>());
        Assert.Equal(scheduledSettlement.SettlementId, released.SettlementId);
        Assert.Equal(Money.USD(101m), released.Amount);
        Assert.Contains(
            releasedEvents.OfType<SettlementStatusSnapshot>(),
            status => status.SettlementId == scheduledSettlement.SettlementId
                && status.Status == SettlementStatus.Released);

        Assert.Equal(Money.USD(1_001m), venue.Account.Cash);
        Assert.Equal(Money.USD(0m), venue.Account.PendingSettlement);
    }

    [Fact]
    public void SimulationSession_RoutesSettlementAndDeliveryEventsThroughSimulatorEvents()
    {
        SessionBuyThenSellAfterSettlementStrategy.Reset();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionBuyThenSellAfterSettlementStrategy>();

        var result = session.Run(
            SharedHistory.Load(
            [
                CreateBarClosed(100m, unixSeconds: 1),
                CreateBarClosed(101m, unixSeconds: 90_000),
                CreateBarClosed(102m, unixSeconds: 180_000)
            ]),
            new SimulationRunOptions
            {
                Config = SimulationConfig.Instant() with
                {
                    Settlement = SettlementParams.CalendarDays(1)
                }
            });

        Assert.Equal(2, result.ExecutionEvents.OfType<OrderFilled>().Count());
        Assert.Single(result.SimulatorEvents.OfType<AssetDeliveryScheduled>());
        Assert.Single(result.SimulatorEvents.OfType<AssetDelivered>());
        Assert.Single(result.SimulatorEvents.OfType<SettlementScheduled>());
        Assert.Single(result.SimulatorEvents.OfType<SettlementReleased>());
        Assert.Contains(
            result.SimulatorEvents.OfType<SettlementStatusSnapshot>(),
            status => status.Status == SettlementStatus.Scheduled);
        Assert.Contains(
            result.SimulatorEvents.OfType<SettlementStatusSnapshot>(),
            status => status.Status == SettlementStatus.Released);
    }

    [Fact]
    public void SimulationSession_EmitsPendingDeliveryStatusAtRunEnd()
    {
        SessionSingleBuyStrategy.Reset();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionSingleBuyStrategy>();

        var result = session.Run(
            SharedHistory.Load([CreateBarClosed(100m, unixSeconds: 1)]),
            new SimulationRunOptions
            {
                Config = SimulationConfig.Instant() with
                {
                    Settlement = SettlementParams.CalendarDays(1)
                }
            });

        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        var scheduled = Assert.Single(result.SimulatorEvents.OfType<AssetDeliveryScheduled>());
        var pending = Assert.Single(
            result.SimulatorEvents.OfType<AssetDeliveryStatusSnapshot>(),
            static status => status.Status == AssetDeliveryStatus.Pending);
        Assert.Equal(scheduled.DeliveryId, pending.DeliveryId);
        Assert.Empty(result.SimulatorEvents.OfType<AssetDelivered>());
        var venue = Assert.Single(result.Diagnostics.Venues);
        Assert.Equal(1, venue.PendingAssetDeliveryCount);
        Assert.Equal(new Qty(1m), venue.PendingAssetDeliveryQuantity);
        Assert.Equal(Money.USD(0m), venue.PendingSettlement);
        Assert.Equal(0, venue.PendingSettlementCount);
    }

    [Fact]
    public void SimulationSession_EmitsPendingSettlementStatusAndDiagnosticsAtRunEnd()
    {
        SessionBuyThenSellAfterSettlementStrategy.Reset();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionBuyThenSellAfterSettlementStrategy>();

        var result = session.Run(
            SharedHistory.Load(
            [
                CreateBarClosed(100m, unixSeconds: 1),
                CreateBarClosed(101m, unixSeconds: 90_000)
            ]),
            new SimulationRunOptions
            {
                Config = SimulationConfig.Instant() with
                {
                    Settlement = SettlementParams.CalendarDays(1)
                }
            });

        Assert.Equal(2, result.ExecutionEvents.OfType<OrderFilled>().Count());
        var scheduled = Assert.Single(result.SimulatorEvents.OfType<SettlementScheduled>());
        var pending = Assert.Single(
            result.SimulatorEvents.OfType<SettlementStatusSnapshot>(),
            static status => status.Status == SettlementStatus.Pending);
        Assert.Equal(scheduled.SettlementId, pending.SettlementId);
        Assert.Empty(result.SimulatorEvents.OfType<SettlementReleased>());
        var venue = Assert.Single(result.Diagnostics.Venues);
        Assert.Equal(1, venue.PendingSettlementCount);
        Assert.Equal(Money.USD(101m), venue.PendingSettlement);
        Assert.Equal(0, venue.PendingAssetDeliveryCount);
        Assert.Equal(Qty.Zero, venue.PendingAssetDeliveryQuantity);
    }

    [Fact]
    public void SimulationSession_CancelsPendingDeliveryWhenUnsettledSaleIsAllowed()
    {
        SessionBuyThenSellAfterSettlementStrategy.Reset();
        using var session = new SimulationSession();
        session.RegisterStrategy<SessionBuyThenSellAfterSettlementStrategy>();

        var result = session.Run(
            SharedHistory.Load(
            [
                CreateBarClosed(100m, unixSeconds: 1),
                CreateBarClosed(101m, unixSeconds: 2)
            ]),
            new SimulationRunOptions
            {
                Config = SimulationConfig.Instant() with
                {
                    Settlement = SettlementParams.CalendarDays(1)
                        .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
                }
            });

        Assert.Equal(2, result.ExecutionEvents.OfType<OrderFilled>().Count());
        var scheduledDelivery = Assert.Single(result.SimulatorEvents.OfType<AssetDeliveryScheduled>());
        var canceledDelivery = Assert.Single(result.SimulatorEvents.OfType<AssetDeliveryCanceled>());
        Assert.Equal(scheduledDelivery.DeliveryId, canceledDelivery.DeliveryId);
        Assert.Contains(
            result.SimulatorEvents.OfType<AssetDeliveryStatusSnapshot>(),
            status => status.DeliveryId == scheduledDelivery.DeliveryId
                && status.Status == AssetDeliveryStatus.Canceled);
        Assert.Empty(result.SimulatorEvents.OfType<AssetDelivered>());

        var venue = Assert.Single(result.Diagnostics.Venues);
        Assert.Equal(0, venue.PendingAssetDeliveryCount);
        Assert.Equal(Qty.Zero, venue.PendingAssetDeliveryQuantity);
        Assert.Equal(1, venue.PendingSettlementCount);
        Assert.Equal(Money.USD(101m), venue.PendingSettlement);
    }

    [Fact]
    public void SimulationAccount_RejectsCashSellBeforeAssetDeliveryWhenPolicyRejects()
    {
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.CalendarDays(1)
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var buy = CreateCommand(orderId: 32) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };
        var earlySell = CreateCommand(orderId: 33) with
        {
            Side = Side.Sell,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m, unixSeconds: 1));
        venue.Submit(buy, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.Submit(earlySell, Instant.FromUnixSeconds(2));
        venue.DrainDueWork(Instant.FromUnixSeconds(2));
        var rejectedEvents = DrainEvents(venue);

        var rejection = Assert.Single(rejectedEvents.OfType<OrderRejected>());
        Assert.Contains("Cash account cannot sell", rejection.Reason);

        venue.OnMarketEvent(CreateBarClosed(101m, unixSeconds: 90_000));
        var settledSell = earlySell with { ClientOrderId = new OrderId(34) };
        venue.Submit(settledSell, Instant.FromUnixSeconds(90_000));
        venue.DrainDueWork(Instant.FromUnixSeconds(90_000));
        var filledEvents = DrainEvents(venue);

        Assert.Single(filledEvents.OfType<OrderFilled>());
    }

    [Fact]
    public void SimulationAccount_CancelsPendingAssetDeliveryWhenUnsettledSaleIsAllowed()
    {
        var config = SimulationConfig.Instant() with
        {
            Settlement = SettlementParams.CalendarDays(1)
                .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow)
        };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var buy = CreateCommand(orderId: 35) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };
        var earlySell = CreateCommand(orderId: 36) with
        {
            Side = Side.Sell,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.OnMarketEvent(CreateBarClosed(100m, unixSeconds: 1));
        venue.Submit(buy, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);
        var scheduledEvents = DrainSimulationEvents(venue);
        var scheduled = Assert.Single(scheduledEvents.OfType<AssetDeliveryScheduled>());
        Assert.Equal(1, venue.Account.PendingAssetDeliveryCount);

        venue.Submit(earlySell, Instant.FromUnixSeconds(2));
        venue.DrainDueWork(Instant.FromUnixSeconds(2));
        var filledEvents = DrainEvents(venue);
        var lifecycleEvents = DrainSimulationEvents(venue);

        Assert.Single(filledEvents.OfType<OrderFilled>());
        var canceled = Assert.Single(lifecycleEvents.OfType<AssetDeliveryCanceled>());
        Assert.Equal(scheduled.DeliveryId, canceled.DeliveryId);
        Assert.Contains(
            lifecycleEvents.OfType<AssetDeliveryStatusSnapshot>(),
            status => status.DeliveryId == scheduled.DeliveryId
                && status.Status == AssetDeliveryStatus.Canceled);
        Assert.Equal(0, venue.Account.PendingAssetDeliveryCount);
        Assert.Equal(Qty.Zero, venue.Account.PendingAssetDeliveryQuantity);
        Assert.Equal(Money.USD(100m), venue.Account.PendingSettlement);

        venue.OnMarketEvent(CreateBarClosed(102m, unixSeconds: 90_000));
        var laterLifecycleEvents = DrainSimulationEvents(venue);
        Assert.Empty(laterLifecycleEvents.OfType<AssetDelivered>());
    }

    [Fact]
    public void SimulationAccount_CreditsImmediateSellProceedsWhenContractSettlementIsImmediate()
    {
        var account = new SimulationAccount(Money.USD(1_000m));
        var contract = Contracts.CryptoSpot("BTCUSD", Venue.Binance, Currency.BTC, Currency.USD, tick: 0.01m, lot: 0.0001m);
        var sell = CreateCommand(orderId: 31) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Side = Side.Sell
        };
        account.RegisterContract(contract);

        account.ApplyFill(
            sell,
            new Qty(2m),
            new Price(50m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));

        Assert.Equal(Money.USD(1_100m), account.Cash);
        Assert.Equal(Money.USD(0m), account.PendingSettlement);
    }

    [Fact]
    public void SimulationAccount_SchedulesSellProceedsFromContractSettlementTerms()
    {
        var account = new SimulationAccount(Money.USD(1_000m));
        var sell = CreateCommand(orderId: 32) with { Side = Side.Sell };
        account.RegisterContract(Contracts.Equity(
            sell.Instrument.Asset.Symbol,
            sell.Instrument.Venue,
            Currency.USD));

        account.ApplyFill(
            sell,
            new Qty(2m),
            new Price(50m, Currency.USD),
            Money.USD(0m),
            Instant.FromUnixSeconds(1));

        Assert.Equal(Money.USD(1_000m), account.Cash);
        Assert.Equal(Money.USD(100m), account.PendingSettlement);
        Assert.Equal(1, account.PendingSettlementCount);
    }

    [Fact]
    public void SimulatedInstrumentEngine_CanUseContractFeeTerms()
    {
        var config = SimulationConfig.Instant() with { Fees = FeeParams.ContractTerms };
        var venue = new SimulatedVenueExchange(Venue.Binance, config, Money.USD(100_000m));
        var contract = Contracts.CryptoSpot("BTCUSD", Venue.Binance, Currency.BTC, Currency.USD, tick: 0.01m, lot: 0.0001m);
        venue.RegisterContract(contract);
        var buy = CreateCommand(orderId: 33) with
        {
            Instrument = contract.Instrument,
            Venue = contract.Instrument.Venue,
            Quantity = new Qty(1m)
        };

        venue.OnMarketEvent(CreateBarClosed(contract.Instrument, 50_000m));
        venue.Submit(buy, Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(Money.USD(20m), fill.Commission);
        Assert.Equal(Money.USD(49_980m), venue.Account.Cash);
    }

    [Fact]
    public void SimulationCommandHeap_OrdersByArrivalPriorityAndSequence()
    {
        var heap = new SimulationCommandHeap();
        var now = Instant.FromUnixSeconds(1);
        var first = CreateCommand(orderId: 1);
        var second = CreateCommand(orderId: 2);

        heap.EnqueueSubmit(first, now);
        heap.EnqueueSubmit(second, now);

        Assert.True(heap.TryDequeueDue(now, out var firstDue));
        Assert.True(heap.TryDequeueDue(now, out var secondDue));
        Assert.Equal(first.ClientOrderId, firstDue.Submit!.Value.ClientOrderId);
        Assert.Equal(second.ClientOrderId, secondDue.Submit!.Value.ClientOrderId);
        Assert.False(heap.TryDequeueDue(now, out _));
    }

    [Fact]
    public void SimulationCommandHeap_PrioritizesCancelModifySubmitAtSameTime()
    {
        var heap = new SimulationCommandHeap();
        var now = Instant.FromUnixSeconds(1);
        var submit = CreateCommand(orderId: 1);
        var cancel = CreateCancel(orderId: 1);
        var modify = CreateModify(orderId: 1, newQuantity: new Qty(2m), newLimit: new Price(120m, Currency.USD));

        heap.EnqueueSubmit(submit, now);
        heap.EnqueueModify(modify, now);
        heap.EnqueueCancel(cancel, now);

        Assert.True(heap.TryDequeueDue(now, out var first));
        Assert.True(heap.TryDequeueDue(now, out var second));
        Assert.True(heap.TryDequeueDue(now, out var third));
        Assert.NotNull(first.Cancel);
        Assert.NotNull(second.Modify);
        Assert.NotNull(third.Submit);
    }

    [Fact]
    public void SimulationCommandHeap_GrowsArrayAndPreservesArrivalOrdering()
    {
        var heap = new SimulationCommandHeap(capacity: 2);
        var now = Instant.FromUnixSeconds(10);

        for (var i = 0; i < 8; i++)
            heap.EnqueueSubmit(CreateCommand(orderId: 100 + i), now + Duration.FromSeconds(8 - i));

        for (var i = 0; i < 8; i++)
        {
            Assert.True(heap.TryDequeueDue(now + Duration.FromSeconds(8), out var due));
            Assert.Equal(new OrderId(107 - i), due.Submit!.Value.ClientOrderId);
        }

        Assert.Equal(0, heap.Count);
    }

    [Fact]
    public void SimulatedVenueExchange_CancelsRestingOrderAndReleasesReservation()
    {
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, SimulationConfig.Instant(), Money.USD(1_000m));
        var submit = CreateCommand(orderId: 11) with
        {
            Quantity = new Qty(5m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD))
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(submit, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        Assert.Equal(Money.USD(500m), venue.Account.ReservedCash);

        venue.Cancel(CreateCancel(orderId: 11), Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var cancelled = Assert.Single(events.OfType<OrderCancelled>());
        Assert.Equal(new Qty(5m), cancelled.RemainingQty);
        Assert.Equal(Money.USD(0m), venue.Account.ReservedCash);
        Assert.Equal(Money.USD(1_000m), venue.Account.AvailableCash);
    }

    [Fact]
    public void SimulatedVenueExchange_ModifiesRestingOrderAndUpdatesReservation()
    {
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, SimulationConfig.Instant(), Money.USD(1_000m));
        var submit = CreateCommand(orderId: 12) with
        {
            Quantity = new Qty(5m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD))
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(submit, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.Modify(CreateModify(orderId: 12, newQuantity: new Qty(3m), newLimit: new Price(90m, Currency.USD)), Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var modified = Assert.Single(events.OfType<OrderModified>());
        Assert.Equal(new Qty(3m), modified.NewQuantity);
        Assert.Equal(new Price(90m, Currency.USD), modified.NewLimitPrice);
        Assert.Equal(Money.USD(270m), venue.Account.ReservedCash);
        Assert.Equal(Money.USD(730m), venue.Account.AvailableCash);
    }

    [Fact]
    public void SimulatedVenueExchange_CancelsInflightSubmitBeforeArrival()
    {
        var config = SimulationConfig.Instant() with { Latency = new LatencyParams(Duration.FromSeconds(10), Duration.Zero) };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var submit = CreateCommand(orderId: 13) with
        {
            Quantity = new Qty(5m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD))
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(submit, Instant.FromUnixSeconds(1));
        venue.Cancel(CreateCancel(orderId: 13), Instant.FromUnixSeconds(2));
        venue.DrainDueWork(Instant.FromUnixSeconds(11));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderCancelled>());
        Assert.Empty(events.OfType<OrderAccepted>());
        Assert.Equal(Money.USD(0m), venue.Account.ReservedCash);
    }

    [Fact]
    public void SimulatedVenueExchange_ModifiesInflightSubmitBeforeArrival()
    {
        var config = SimulationConfig.Instant() with { Latency = new LatencyParams(Duration.FromSeconds(10), Duration.Zero) };
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, config, Money.USD(1_000m));
        var submit = CreateCommand(orderId: 14) with
        {
            Quantity = new Qty(5m),
            Execution = Execution.Limit().At(new Price(100m, Currency.USD))
        };

        venue.OnMarketEvent(CreateBarClosed(120m));
        venue.Submit(submit, Instant.FromUnixSeconds(1));
        venue.Modify(CreateModify(orderId: 14, newQuantity: new Qty(2m), newLimit: new Price(90m, Currency.USD)), Instant.FromUnixSeconds(2));
        var modifyEvents = DrainEvents(venue);
        venue.DrainDueWork(Instant.FromUnixSeconds(11));
        var arrivalEvents = DrainEvents(venue);

        var modified = Assert.Single(modifyEvents.OfType<OrderModified>());
        Assert.Equal(new Qty(2m), modified.NewQuantity);
        Assert.Contains(arrivalEvents, static evt => evt is OrderAccepted);
        Assert.Equal(Money.USD(180m), venue.Account.ReservedCash);
    }

    [Fact]
    public void SimulatedVenueExchange_ExpiresGtdRestingOrder()
    {
        var venue = new SimulatedVenueExchange(Venue.NASDAQ, SimulationConfig.Instant(), Money.USD(1_000m));
        var submit = CreateCommand(orderId: 15) with
        {
            Quantity = new Qty(1m),
            Execution = Execution.Limit()
                .At(new Price(90m, Currency.USD))
                .GoodTil(Instant.FromUnixSeconds(2))
        };

        venue.OnMarketEvent(CreateBarClosed(100m, unixSeconds: 1));
        venue.Submit(submit, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        _ = DrainEvents(venue);

        venue.DrainDueWork(Instant.FromUnixSeconds(2));
        var events = DrainEvents(venue);

        var expired = Assert.Single(events.OfType<OrderExpired>());
        Assert.Equal(submit.ClientOrderId, expired.OrderId);
        Assert.Equal(new VenueOrderId(1), expired.VenueOrderId);
        Assert.Equal(0, venue.GetOrCreateInstrumentEngine(submit.Instrument).OpenOrders);
        Assert.Equal(Money.USD(0m), venue.Account.ReservedCash);
    }

    [Fact]
    public void SimulatedVenueExchange_OptionSpreadPackageFillBooksLegPositions()
    {
        var venue = new SimulatedVenueExchange(
            new Venue("CBOE"),
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var (spread, shortCall, longCall) = CreateVerticalCallSpreadContracts();
        venue.RegisterContract(shortCall);
        venue.RegisterContract(longCall);
        venue.RegisterContract(spread);
        venue.OnMarketEvent(CreateBarClosed(shortCall.Instrument, 5m));
        venue.OnMarketEvent(CreateBarClosed(longCall.Instrument, 2m));
        _ = DrainEvents(venue);

        var submit = CreateCommand(orderId: 16) with
        {
            AssetId = new AssetId(2),
            Instrument = spread.Instrument,
            Venue = spread.Instrument.Venue,
            Side = Side.Buy,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.Submit(submit, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var fill = Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(spread.Instrument, fill.Instrument);
        Assert.Equal(new Price(-3m, Currency.USD), fill.FillPrice);
        var legFills = events.OfType<PackageLegFilled>().ToArray();
        Assert.Equal(2, legFills.Length);
        Assert.Equal(new Qty(-1m), venue.Account.GetPositionQuantity(submit.StrategyId, submit.VariantId, shortCall.Instrument));
        Assert.Equal(new Qty(1m), venue.Account.GetPositionQuantity(submit.StrategyId, submit.VariantId, longCall.Instrument));
        Assert.Equal(Qty.Zero, venue.Account.GetPositionQuantity(submit.StrategyId, submit.VariantId, spread.Instrument));
        Assert.Equal(Money.USD(100_300m), venue.Account.Cash);
    }

    [Fact]
    public void SimulatedVenueExchange_OptionSpreadPackageRejectsWhenLegMarkMissing()
    {
        var venue = new SimulatedVenueExchange(
            new Venue("CBOE"),
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var (spread, shortCall, longCall) = CreateVerticalCallSpreadContracts();
        venue.RegisterContract(shortCall);
        venue.RegisterContract(longCall);
        venue.RegisterContract(spread);
        venue.OnMarketEvent(CreateBarClosed(shortCall.Instrument, 5m));
        _ = DrainEvents(venue);

        var submit = CreateCommand(orderId: 17) with
        {
            AssetId = new AssetId(2),
            Instrument = spread.Instrument,
            Venue = spread.Instrument.Venue,
            Side = Side.Buy,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.Submit(submit, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("has no current mark", rejection.Reason);
        Assert.Empty(events.OfType<OrderFilled>());
        Assert.Equal(Qty.Zero, venue.Account.GetPositionQuantity(submit.StrategyId, submit.VariantId, shortCall.Instrument));
        Assert.Equal(Qty.Zero, venue.Account.GetPositionQuantity(submit.StrategyId, submit.VariantId, longCall.Instrument));
    }

    [Fact]
    public void SimulatedVenueExchange_OptionSpreadPackageRejectsNonMarketableLimit()
    {
        var venue = new SimulatedVenueExchange(
            new Venue("CBOE"),
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var (spread, shortCall, longCall) = CreateVerticalCallSpreadContracts();
        venue.RegisterContract(shortCall);
        venue.RegisterContract(longCall);
        venue.RegisterContract(spread);
        venue.OnMarketEvent(CreateBarClosed(shortCall.Instrument, 5m));
        venue.OnMarketEvent(CreateBarClosed(longCall.Instrument, 2m));
        _ = DrainEvents(venue);

        var submit = CreateCommand(orderId: 18) with
        {
            AssetId = new AssetId(2),
            Instrument = spread.Instrument,
            Venue = spread.Instrument.Venue,
            Side = Side.Buy,
            Quantity = new Qty(1m),
            Execution = Execution.Limit().At(new Price(-4m, Currency.USD))
        };

        venue.Submit(submit, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        var rejection = Assert.Single(events.OfType<OrderRejected>());
        Assert.Contains("not marketable against atomic package price", rejection.Reason);
        Assert.Empty(events.OfType<OrderAccepted>());
        Assert.Empty(events.OfType<OrderFilled>());
        Assert.Equal(Qty.Zero, venue.Account.GetPositionQuantity(submit.StrategyId, submit.VariantId, shortCall.Instrument));
        Assert.Equal(Qty.Zero, venue.Account.GetPositionQuantity(submit.StrategyId, submit.VariantId, longCall.Instrument));
    }

    [Fact]
    public void SimulatedVenueExchange_FilledOptionSpreadPackageIsTerminalBeforeEventsAreDrained()
    {
        var venue = new SimulatedVenueExchange(
            new Venue("CBOE"),
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var (spread, shortCall, longCall) = CreateVerticalCallSpreadContracts();
        venue.RegisterContract(shortCall);
        venue.RegisterContract(longCall);
        venue.RegisterContract(spread);
        venue.OnMarketEvent(CreateBarClosed(shortCall.Instrument, 5m));
        venue.OnMarketEvent(CreateBarClosed(longCall.Instrument, 2m));
        _ = DrainEvents(venue);
        var submit = CreateCommand(orderId: 19) with
        {
            AssetId = new AssetId(2),
            Instrument = spread.Instrument,
            Venue = spread.Instrument.Venue,
            Side = Side.Buy,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };

        venue.Submit(submit, Instant.FromUnixSeconds(1));
        venue.Cancel(CreateCancel(orderId: 19), Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderAccepted>());
        Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(2, events.OfType<PackageLegFilled>().Count());
        Assert.Empty(events.OfType<OrderCancelled>());
        Assert.Empty(events.OfType<OrderRejected>());
        Assert.Equal(new Qty(-1m), venue.Account.GetPositionQuantity(submit.StrategyId, submit.VariantId, shortCall.Instrument));
        Assert.Equal(new Qty(1m), venue.Account.GetPositionQuantity(submit.StrategyId, submit.VariantId, longCall.Instrument));
    }

    [Fact]
    public void SimulatedVenueExchange_FilledOptionSpreadPackageIgnoresModifyBeforeEventsAreDrained()
    {
        var venue = new SimulatedVenueExchange(
            new Venue("CBOE"),
            SimulationConfig.Instant() with { AccountType = AccountType.Margin },
            Money.USD(100_000m));
        var (spread, shortCall, longCall) = CreateVerticalCallSpreadContracts();
        venue.RegisterContract(shortCall);
        venue.RegisterContract(longCall);
        venue.RegisterContract(spread);
        venue.OnMarketEvent(CreateBarClosed(shortCall.Instrument, 5m));
        venue.OnMarketEvent(CreateBarClosed(longCall.Instrument, 2m));
        _ = DrainEvents(venue);
        var submit = CreateCommand(orderId: 20) with
        {
            AssetId = new AssetId(2),
            Instrument = spread.Instrument,
            Venue = spread.Instrument.Venue,
            Side = Side.Buy,
            Quantity = new Qty(1m),
            Execution = Execution.Market()
        };
        var modify = CreateModify(orderId: 20, newQuantity: new Qty(2m), newLimit: new Price(-3m, Currency.USD)) with
        {
            AssetId = new AssetId(2),
            Instrument = spread.Instrument,
            Venue = spread.Instrument.Venue
        };

        venue.Submit(submit, Instant.FromUnixSeconds(1));
        venue.Modify(modify, Instant.FromUnixSeconds(1));
        venue.DrainDueWork(Instant.FromUnixSeconds(1));
        var events = DrainEvents(venue);

        Assert.Single(events.OfType<OrderAccepted>());
        Assert.Single(events.OfType<OrderFilled>());
        Assert.Equal(2, events.OfType<PackageLegFilled>().Count());
        Assert.Empty(events.OfType<OrderModified>());
        Assert.Empty(events.OfType<OrderRejected>());
        Assert.Equal(new Qty(-1m), venue.Account.GetPositionQuantity(submit.StrategyId, submit.VariantId, shortCall.Instrument));
        Assert.Equal(new Qty(1m), venue.Account.GetPositionQuantity(submit.StrategyId, submit.VariantId, longCall.Instrument));
    }

    private static SimulationOrderCommand CreateCommand(long orderId)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        return new SimulationOrderCommand(
            new StrategyId(1),
            VariantId: 0,
            new AssetId(0),
            instrument,
            instrument.Venue,
            new OrderId(orderId),
            Side.Buy,
            new Qty(1m),
            Execution.Market());
    }

    private static OptionLifecycleReference LifecycleReference(Price price)
        => new(price, OptionLifecycleReferenceSource.MarketMark);

    private static OptionLifecycleApplicationStatus ProcessAndApplyOptionLifecycleResult(
        SimulationAccount account,
        StrategyId strategyId,
        int variantId,
        Instrument instrument,
        OptionLifecycleReference reference,
        Instant now,
        SimulationOptionAssignmentInput? assignmentInput = null)
    {
        Assert.True(account.TryGetContract(instrument, out var contract));
        var quantity = account.GetPositionQuantity(strategyId, variantId, instrument);
        var processor = new OptionLifecycleProcessor();
        var result = processor.Process(new OptionLifecycleRequest(
            contract,
            quantity,
            reference,
            now,
            assignmentInput));
        return account.ApplyOptionLifecycleResult(strategyId, variantId, instrument, result);
    }

    private static (InstrumentContract Spread, InstrumentContract ShortCall, InstrumentContract LongCall) CreateVerticalCallSpreadContracts()
    {
        var venue = new Venue("CBOE");
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var shortCall = Contracts.OptionContract(
            "SPY261218C00100000",
            venue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European);
        var longCall = Contracts.OptionContract(
            "SPY261218C00105000",
            venue,
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(105m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European);
        var spread = Contracts.OptionSpread(
            "SPY261218C100-105",
            venue,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            [
                new InstrumentLeg(shortCall.Instrument, 1m, Side.Sell),
                new InstrumentLeg(longCall.Instrument, 1m, Side.Buy)
            ]);

        return (spread, shortCall, longCall);
    }

    private static string GetRhodiumSimulationSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "HPD-AI-Framework",
                "dotnet",
                "shared",
                "src",
                "Rhodium",
                "src",
                "Rhodium.Simulation");

            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Rhodium.Simulation source root.");
    }

    private static bool IsBuildArtifactPath(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, "bin", StringComparison.Ordinal)
                || string.Equals(part, "obj", StringComparison.Ordinal));

    private static SimulationCancelCommand CreateCancel(long orderId)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        return new SimulationCancelCommand(
            new StrategyId(1),
            VariantId: 0,
            new AssetId(0),
            instrument,
            instrument.Venue,
            new OrderId(orderId));
    }

    private static SimulationModifyCommand CreateModify(long orderId, Qty newQuantity, Price newLimit)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        return new SimulationModifyCommand(
            new StrategyId(1),
            VariantId: 0,
            new AssetId(0),
            instrument,
            instrument.Venue,
            new OrderId(orderId),
            NewQuantity: newQuantity,
            NewLimitPrice: newLimit);
    }

    private static ExecutionEvent[] DrainEvents(SimulatedVenueExchange venue)
    {
        var buffer = new ExecutionEvent[16];
        var count = venue.DrainExecutionEvents(buffer);
        return buffer.AsSpan(0, count).ToArray();
    }

    private static FinanceEvent[] DrainSimulationEvents(SimulatedVenueExchange venue)
    {
        var buffer = new FinanceEvent[16];
        var count = venue.DrainSimulationEvents(buffer);
        return buffer.AsSpan(0, count).ToArray();
    }

    private static FinanceEvent[] DrainSimulationEvents(SimulatedExchangeRegistry registry)
    {
        var buffer = new FinanceEvent[16];
        var count = registry.DrainSimulationEvents(buffer);
        return buffer.AsSpan(0, count).ToArray();
    }

    private static FinanceEvent[] DrainAccountEvents(SimulationAccount account)
    {
        var buffer = new FinanceEvent[16];
        var count = account.DrainEvents(buffer);
        return buffer.AsSpan(0, count).ToArray();
    }

    private static async IAsyncEnumerable<FinanceEvent> ReadEventsAsync(IEnumerable<FinanceEvent> events)
    {
        foreach (var evt in events)
        {
            await Task.Yield();
            yield return evt;
        }
    }

    private static async IAsyncEnumerable<FinanceEvent> ThrowIfSecondEventIsReadAsync()
    {
        await Task.Yield();
        yield return CreateBarClosed(123m);

        throw new InvalidOperationException("RunAsync read past the configured replay limit.");
    }

    private static async IAsyncEnumerable<FinanceEvent> ThrowIfCancellationIsObservedAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        yield return CreateBarClosed(123m);

        throw new InvalidOperationException("RunAsync continued after cancellation.");
    }

    private static async Task<T[]> CollectAsync<T>(IAsyncEnumerable<T> values)
    {
        var buffer = new List<T>();
        await foreach (var value in values)
            buffer.Add(value);

        return buffer.ToArray();
    }

    private static BarClosed CreateBarClosed(decimal close, long unixSeconds = 1)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        return CreateBarClosed(instrument, close, unixSeconds);
    }

    private static BarClosed CreateBarClosed(Instrument instrument, decimal close, long unixSeconds = 1)
    {
        var bar = new Bar(
            new Price(close, Currency.USD),
            new Price(close + 1m, Currency.USD),
            new Price(close - 1m, Currency.USD),
            new Price(close, Currency.USD),
            new Qty(10_000m),
            Instant.FromUnixSeconds(unixSeconds),
            Duration.FromMinutes(1));

        return new BarClosed(instrument, bar);
    }

    private static TradeOccurred CreateTradeOccurred(decimal price, decimal size, Side aggressorSide)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var trade = new Trade(
            new Price(price, Currency.USD),
            new Qty(size),
            aggressorSide,
            DualTimestamp.Synchronized(Instant.FromUnixSeconds(2)));

        return new TradeOccurred(instrument, trade);
    }

    private static QuoteReceived CreateQuoteReceived(decimal bid, decimal ask, long unixSeconds = 1)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var quote = new Quote(
            new Price(bid, Currency.USD),
            new Price(ask, Currency.USD),
            new Qty(1m),
            new Qty(2m),
            DualTimestamp.Synchronized(Instant.FromUnixSeconds(unixSeconds)));

        return new QuoteReceived(instrument, quote);
    }

    private static BookLevelDeltaReceived CreateBookLevelDelta(
        BookAction action,
        Side side,
        decimal price,
        decimal size,
        long sequence)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        return new BookLevelDeltaReceived(
            instrument,
            new BookLevelDelta(
                side,
                new Price(price, Currency.USD),
                new Qty(size),
                action,
                sequence));
    }

    private static BookOrderAdded CreateBookOrderAdded(
        long orderId,
        Side side,
        decimal price,
        decimal size,
        long venueSequence)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        return new BookOrderAdded(
            instrument,
            new BookOrder(
                new BookOrderId(orderId),
                side,
                new Price(price, Currency.USD),
                new Qty(size)),
            venueSequence);
    }

    private static BookOrderModified CreateBookOrderModified(
        long orderId,
        Side side,
        decimal price,
        decimal size,
        long venueSequence)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        return new BookOrderModified(
            instrument,
            new BookOrder(
                new BookOrderId(orderId),
                side,
                new Price(price, Currency.USD),
                new Qty(size)),
            venueSequence);
    }

    private static void ApplyBookOrderAddedFrame(
        SimulatedVenueExchange venue,
        Instrument instrument,
        long orderId,
        Side side,
        decimal price,
        decimal size,
        long venueSequence)
        => venue.OnBookOrderAdded(
            instrument,
            new BookOrderAddedFrame(
                InstrumentIndex: 0,
                orderId,
                side,
                ScaleDecimal(price),
                ScaleDecimal(size),
                venueSequence,
                Instant.FromUnixSeconds(1).Nanos));

    private static void ApplyBookOrderModifiedFrame(
        SimulatedVenueExchange venue,
        Instrument instrument,
        long orderId,
        Side side,
        decimal price,
        decimal size,
        long venueSequence)
        => venue.OnBookOrderModified(
            instrument,
            new BookOrderModifiedFrame(
                InstrumentIndex: 0,
                orderId,
                side,
                ScaleDecimal(price),
                ScaleDecimal(size),
                venueSequence,
                Instant.FromUnixSeconds(1).Nanos));

    private static void ApplyBookOrderDeletedFrame(
        SimulatedVenueExchange venue,
        Instrument instrument,
        long orderId,
        long venueSequence)
        => venue.OnBookOrderDeleted(
            instrument,
            new BookOrderDeletedFrame(
                InstrumentIndex: 0,
                orderId,
                venueSequence,
                Instant.FromUnixSeconds(1).Nanos));

    private static void ApplyBookOrderExecutedFrame(
        SimulatedVenueExchange venue,
        Instrument instrument,
        long orderId,
        decimal executedSize,
        long venueSequence)
        => venue.OnBookOrderExecuted(
            instrument,
            new BookOrderExecutedFrame(
                InstrumentIndex: 0,
                orderId,
                ScaleDecimal(executedSize),
                venueSequence,
                Instant.FromUnixSeconds(1).Nanos));

    private static long ScaleDecimal(decimal value)
        => decimal.ToInt64(value * 1_000_000m);

    private static BookDepthSnapshotReceived CreateDepthSnapshot(
        IReadOnlyList<Level> bids,
        IReadOnlyList<Level> asks,
        int depth)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        return new BookDepthSnapshotReceived(instrument, bids, asks, depth)
        {
            Time = Instant.FromUnixSeconds(1)
        };
    }

    private static BookDepth10Received CreateBookDepth10(
        IReadOnlyList<Level> bids,
        IReadOnlyList<Level> asks,
        long unixSeconds)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        return new BookDepth10Received(instrument, bids, asks)
        {
            Time = Instant.FromUnixSeconds(unixSeconds)
        };
    }
}

internal sealed partial class SessionMarketBuyStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 123.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Market());
    }
}

internal sealed partial class SessionMarketSellStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 123.0)
            return;

        _submitted = true;
        bar.Sell(new Qty(1m), Execution.Market());
    }
}

internal sealed partial class RuntimeOptionExpiryStrategy : Strategy
{
    public static readonly Instrument Underlying = new(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
    public static readonly InstrumentContract UnderlyingContract = Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD);
    public static readonly InstrumentContract Option = Contracts.OptionContract(
        "SPY261218C00100000",
        new Venue("CBOE"),
        Underlying,
        Currency.USD,
        tick: 0.01m,
        lot: 1m,
        multiplier: 100m,
        strike: new Price(100m, Currency.USD),
        Instant.FromUnixSeconds(1_796_016_000),
        OptionRight.Call,
        ExerciseStyle.European,
        exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);

    private AssetId _option;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        setup.AddInstrument(UnderlyingContract);
        _option = setup.AddInstrument(Option);
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _option || bar.Close != 2.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Market());
    }
}

internal sealed partial class RuntimeOptionSpreadExpiryStrategy : Strategy
{
    public static readonly Venue OptionVenue = new("CBOE");
    public static readonly Instrument Underlying = new(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
    public static readonly InstrumentContract UnderlyingContract = Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD);
    public static readonly Instant Expiry = Instant.FromUnixSeconds(1_796_016_000);
    public static readonly InstrumentContract ShortCall = Contracts.OptionContract(
        "SPY261218C00100000",
        OptionVenue,
        Underlying,
        Currency.USD,
        tick: 0.01m,
        lot: 1m,
        multiplier: 100m,
        strike: new Price(100m, Currency.USD),
        Expiry,
        OptionRight.Call,
        ExerciseStyle.European,
        exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
    public static readonly InstrumentContract LongCall = Contracts.OptionContract(
        "SPY261218C00105000",
        OptionVenue,
        Underlying,
        Currency.USD,
        tick: 0.01m,
        lot: 1m,
        multiplier: 100m,
        strike: new Price(105m, Currency.USD),
        Expiry,
        OptionRight.Call,
        ExerciseStyle.European,
        exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
    public static readonly InstrumentContract Spread = Contracts.OptionSpread(
        "SPY261218C100-105",
        OptionVenue,
        Currency.USD,
        tick: 0.01m,
        lot: 1m,
        [
            new InstrumentLeg(ShortCall.Instrument, 1m, Side.Sell),
            new InstrumentLeg(LongCall.Instrument, 1m, Side.Buy)
        ]);

    private AssetId _spread;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        setup.AddInstrument(UnderlyingContract);
        setup.AddInstrument(ShortCall);
        setup.AddInstrument(LongCall);
        _spread = setup.AddInstrument(Spread);
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spread || bar.Close != -3.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Market());
    }
}

internal sealed partial class SessionNoopUniverseStrategy : Strategy
{
    protected override void OnInitialize(in SetupContext setup)
        => setup.AddEquity("SPY");
}

internal sealed partial class SessionLargeMarketBuyStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 123.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(10m), Execution.Market());
    }
}

internal sealed partial class SessionCancelAcceptedStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;
    private bool _cancelled;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 100.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Limit().At(new Price(90m, Currency.USD)));
    }

    protected override void OnOrderAccepted(ref OrderContext order)
    {
        if (_cancelled || order.AssetId != _spy)
            return;

        _cancelled = true;
        order.Cancel("strategy cancel");
    }
}

internal sealed partial class SessionModifyAcceptedStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;
    private bool _modified;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 100.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Limit().At(new Price(90m, Currency.USD)));
    }

    protected override void OnOrderAccepted(ref OrderContext order)
    {
        if (_modified || order.AssetId != _spy)
            return;

        _modified = true;
        order.Modify(newLimitPrice: new Price(101m, Currency.USD));
    }
}

internal sealed partial class SessionSellOnFillStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;
    private bool _sold;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 100.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Market());
    }

    protected override void OnOrderFilled(ref FillContext fill)
    {
        if (_sold || fill.AssetId != _spy || fill.Side != Side.Buy)
            return;

        _sold = true;
        fill.Sell(fill.FilledQty, Execution.Market());
    }
}

internal sealed partial class SessionRunawayFillStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 100.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Market());
    }

    protected override void OnOrderFilled(ref FillContext fill)
        => fill.Buy(new Qty(1m), Execution.Market());
}

internal sealed partial class SessionBuyThenSellAfterSettlementStrategy : Strategy
{
    private AssetId _spy;
    private bool _bought;
    private bool _sold;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId != _spy)
            return;

        if (!_bought && bar.Close == 100.0)
        {
            _bought = true;
            bar.Buy(new Qty(1m), Execution.Market());
            return;
        }

        if (!_sold && bar.Close == 101.0)
        {
            _sold = true;
            bar.Sell(new Qty(1m), Execution.Market());
        }
    }
}

internal sealed partial class SessionSingleBuyStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 100.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Market());
    }
}

internal sealed partial class SessionGtdExpiryStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;

    public static int ExpiredCount { get; private set; }

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
        => ExpiredCount = 0;

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 100.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Limit()
            .At(new Price(90m, Currency.USD))
            .GoodTil(Instant.FromUnixSeconds(2)));
    }

    protected override void OnOrderExpired(ref OrderContext order)
    {
        if (order.AssetId == _spy)
            ExpiredCount++;
    }
}

internal sealed partial class SessionScheduledClockStrategy : Strategy
{
    public static int ScheduledCount { get; private set; }
    public static Instant LastScheduledAt { get; private set; }

    protected override void OnInitialize(in SetupContext setup)
    {
        setup.AddEquity("SPY");
        setup.ScheduleEvery("rebalance", Duration.FromSeconds(5));
    }

    public static void Reset()
    {
        ScheduledCount = 0;
        LastScheduledAt = default;
    }

    protected override void OnScheduled(ref TimerContext timer)
    {
        ScheduledCount++;
        LastScheduledAt = timer.Time;
    }
}

internal sealed partial class MarginLiquidationStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;

    public static int FilledCount { get; private set; }

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
        => FilledCount = 0;

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 100.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(10m), Execution.Market());
    }

    protected override void OnOrderFilled(ref FillContext fill)
    {
        if (fill.Side == Side.Buy)
            FilledCount++;
    }
}

internal sealed partial class CorporateActionBuyStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 100.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Market());
    }
}

internal sealed class CloseVenueAfterFirstTimestampModule : ISessionSimulationModule
{
    private bool _emitted;

    public int PreProcessCalls { get; private set; }
    public int ProcessCalls { get; private set; }
    public bool ObservedVenue { get; private set; }
    public bool ObservedInstrument { get; private set; }

    public void PreProcess(
        in FinanceEvent evt,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
    {
        PreProcessCalls++;
        ObservedVenue |= context.TryGetVenue(Venue.NASDAQ, out var venue)
            && venue.Venue == Venue.NASDAQ
            && Enum.IsDefined(venue.Status);
        if (evt is MarketEvent market)
        {
            ObservedInstrument |= context.TryGetInstrument(market.Instrument, out var instrument)
                && instrument.Instrument == market.Instrument;
        }
    }

    public void Process(
        Instant now,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
    {
        ProcessCalls++;
        if (_emitted)
            return;

        _emitted = true;
        sinks.Events.Emit(new VenueStatusChanged(Venue.NASDAQ, MarketStatus.Closed, "module close"));
    }

    public void AppendDiagnostics(ref SimulationDiagnosticsBuilder diagnostics)
    {
    }

    public void Reset()
    {
        _emitted = false;
        ObservedVenue = false;
        ObservedInstrument = false;
        PreProcessCalls = 0;
        ProcessCalls = 0;
    }
}

internal sealed class CountingSameTimestampModule : ISessionSimulationModule
{
    public int PreProcessCalls { get; private set; }
    public int ProcessCalls { get; private set; }
    public int EventsSeenBeforeFirstProcess { get; private set; }

    public void PreProcess(
        in FinanceEvent evt,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
        => PreProcessCalls++;

    public void Process(
        Instant now,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
    {
        ProcessCalls++;
        if (ProcessCalls == 1)
            EventsSeenBeforeFirstProcess = PreProcessCalls;
    }

    public void AppendDiagnostics(ref SimulationDiagnosticsBuilder diagnostics)
    {
    }

    public void Reset()
    {
        PreProcessCalls = 0;
        ProcessCalls = 0;
        EventsSeenBeforeFirstProcess = 0;
    }
}

internal sealed class FrameEmittingModule : ISessionSimulationModule
{
    private bool _emitted;

    public void Reset()
        => _emitted = false;

    public void PreProcess(
        in FinanceEvent evt,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
    {
        if (_emitted)
            return;

        _emitted = true;
        sinks.Frames.Emit(new QuoteFrame(
            InstrumentIndex: 42,
            BidTicks: 100,
            AskTicks: 101,
            BidSizeLots: 1,
            AskSizeLots: 2,
            TimestampNs: evt.Time.Nanos));
    }

    public void Process(
        Instant now,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
    {
    }

    public void AppendDiagnostics(ref SimulationDiagnosticsBuilder diagnostics)
    {
    }
}

internal sealed class MixedFrameEmittingModule : ISessionSimulationModule
{
    private bool _emitted;

    public void Reset()
        => _emitted = false;

    public void PreProcess(
        in FinanceEvent evt,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
    {
        if (_emitted)
            return;

        _emitted = true;
        sinks.Frames.Emit(new QuoteFrame(
            InstrumentIndex: 42,
            BidTicks: 100,
            AskTicks: 101,
            BidSizeLots: 1,
            AskSizeLots: 2,
            TimestampNs: evt.Time.Nanos));
        sinks.Frames.Emit(new ExecutionFillFrame(
            StrategyIndex: 1,
            VariantId: 0,
            InstrumentIndex: 42,
            ClientOrderId: 7,
            VenueOrderId: 8,
            ExecutionId: 9,
            Side: Side.Buy,
            FillPriceTicks: 100,
            FillQuantityLots: 1,
            FeeAmountScaled: 0,
            FeeCurrencyId: 0,
            TimestampNs: evt.Time.Nanos));
        sinks.Frames.Emit(new RiskMetricFrame(
            VenueId: 1,
            InstrumentIndex: 42,
            MetricId: 3,
            ValueScaled: 4,
            TimestampNs: evt.Time.Nanos));
    }

    public void Process(
        Instant now,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
    {
    }

    public void AppendDiagnostics(ref SimulationDiagnosticsBuilder diagnostics)
    {
    }
}

internal sealed class CountingScopedSessionModule : ISessionSimulationModule
{
    public int PreProcessCalls { get; private set; }
    public int ProcessCalls { get; private set; }

    public void Reset()
    {
        PreProcessCalls = 0;
        ProcessCalls = 0;
    }

    public void PreProcess(
        in FinanceEvent evt,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
        => PreProcessCalls++;

    public void Process(
        Instant now,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
        => ProcessCalls++;

    public void AppendDiagnostics(ref SimulationDiagnosticsBuilder diagnostics)
    {
    }
}

internal sealed class CommandSubmittingModule(
    StrategyId strategyId,
    Instrument instrument) : ISessionSimulationModule
{
    private bool _submitted;

    public void Reset()
        => _submitted = false;

    public void PreProcess(
        in FinanceEvent evt,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
    {
    }

    public void Process(
        Instant now,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
    {
        if (_submitted)
            return;

        _submitted = true;
        var command = new SimulationOrderCommand(
            strategyId,
            0,
            new AssetId(0),
            instrument,
            instrument.Venue,
            new OrderId(99),
            Side.Buy,
            new Qty(1m),
            Execution.Market());
        sinks.Commands.Submit(in command);
    }

    public void AppendDiagnostics(ref SimulationDiagnosticsBuilder diagnostics)
    {
    }
}

internal sealed class DiagnosticModule : ISessionSimulationModule
{
    private int _eventsSeen;

    public void Reset()
        => _eventsSeen = 0;

    public void PreProcess(
        in FinanceEvent evt,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
        => _eventsSeen++;

    public void Process(
        Instant now,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
    {
    }

    public void AppendDiagnostics(ref SimulationDiagnosticsBuilder diagnostics)
    {
        diagnostics.AddModuleCounter(nameof(DiagnosticModule), "events_seen", _eventsSeen);
        diagnostics.AddModuleMetric(nameof(DiagnosticModule), "health", 1.0);
        diagnostics.AddModuleMessage(nameof(DiagnosticModule), "ok", "module healthy");
    }
}

internal sealed class CountingVenueModule(Venue venue) : IVenueSimulationModule
{
    public Venue Venue { get; } = venue;
    public int PreProcessCalls { get; private set; }
    public int ProcessCalls { get; private set; }

    public void Reset()
    {
        PreProcessCalls = 0;
        ProcessCalls = 0;
    }

    public void PreProcess(
        in FinanceEvent evt,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
        => PreProcessCalls++;

    public void Process(
        Instant now,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
        => ProcessCalls++;

    public void AppendDiagnostics(ref SimulationDiagnosticsBuilder diagnostics)
    {
    }
}

internal sealed class CountingInstrumentModule(Instrument instrument) : IInstrumentSimulationModule
{
    public Instrument Instrument { get; } = instrument;
    public int PreProcessCalls { get; private set; }
    public int ProcessCalls { get; private set; }

    public void Reset()
    {
        PreProcessCalls = 0;
        ProcessCalls = 0;
    }

    public void PreProcess(
        in FinanceEvent evt,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
        => PreProcessCalls++;

    public void Process(
        Instant now,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks)
        => ProcessCalls++;

    public void AppendDiagnostics(ref SimulationDiagnosticsBuilder diagnostics)
    {
    }
}
