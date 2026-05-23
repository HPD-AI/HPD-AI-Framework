using HPD.Events;
using HPD.Events.Core;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Platform.Attributes;
using Rhodium.Primitives;
using Rhodium.Simulation;
using Rhodium.Tensor;

namespace Rhodium.Connectivity.Tests;

public class TradingHostTests
{
    [Fact]
    public void RegisterStrategy_ReturnsStrategyId()
    {
        using var runtime = new RhodiumRuntime();
        using var host = new TradingHost(new TestConnector(), new EventBus(), runtime);

        var id = host.RegisterStrategy<TestStrategy>(depth: 0);

        Assert.True(id.Value > 0);
    }

    [Fact]
    public async Task RunAsync_StartsConnectorAndInitializesRegisteredStrategies()
    {
        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        host.RegisterStrategy<TestStrategy>(depth: 0);

        await host.RunAsync();

        Assert.True(connector.WasStarted);
        Assert.True(TestStrategy.WasInitialized);
    }

    [Fact]
    public async Task RunAsync_SubscribeToRegisteredStrategyInstruments()
    {
        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        host.RegisterStrategy<InstrumentStrategy>(depth: 0);

        await host.RunAsync();

        Assert.Contains(connector.Subscriptions, s => s.Instrument.Asset.Symbol == "SPY" && s.Type == SubscriptionType.Trades);
        Assert.Contains(connector.Subscriptions, s => s.Instrument.Asset.Symbol == "SPY" && s.Type == SubscriptionType.Quotes);
        Assert.Contains(connector.Subscriptions, s => s.Instrument.Asset.Symbol == "SPY" && s.Type == SubscriptionType.Depth);
        Assert.Contains(connector.Subscriptions, s => s.Instrument.Asset.Symbol == "SPY" && s.Type == SubscriptionType.Bars);
    }

    [Fact]
    public async Task RunAsync_RoutesSubscriptionsAndOrdersToVenueConnectors()
    {
        using var runtime = new RhodiumRuntime();
        var nasdaqConnector = new TestConnector();
        var binanceConnector = new TestConnector();
        using var host = new TradingHost(
            new Dictionary<Venue, IConnector>
            {
                [Venue.NASDAQ] = nasdaqConnector,
                [Venue.Binance] = binanceConnector
            },
            new EventBus(),
            runtime);
        host.RegisterStrategy<MultiVenueGeneratedStrategy>(depth: 0);

        var spy = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var btc = new Instrument(new Asset("BTCUSDT", AssetClass.Crypto), Venue.Binance);
        nasdaqConnector.Events.Add(CreateBarClosed(spy, 123m));
        binanceConnector.Events.Add(CreateBarClosed(btc, 123m));

        await host.RunAsync();

        Assert.True(nasdaqConnector.WasStarted);
        Assert.True(binanceConnector.WasStarted);
        Assert.All(nasdaqConnector.Subscriptions, subscription => Assert.Equal(Venue.NASDAQ, subscription.Instrument.Venue));
        Assert.All(binanceConnector.Subscriptions, subscription => Assert.Equal(Venue.Binance, subscription.Instrument.Venue));

        var nasdaqOrder = Assert.Single(nasdaqConnector.SubmittedOrders);
        var binanceOrder = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Equal(spy, nasdaqOrder.Instrument);
        Assert.Equal(btc, binanceOrder.Instrument);
    }

    [Fact]
    public async Task RunAsync_EmitsCrossVenueArbitrageOpportunityForCrossedSameAssetQuotes()
    {
        using var runtime = new RhodiumRuntime();
        var events = new EventBus();
        await using var opportunities = events.CreateInbox<CrossVenueArbitrageOpportunity>(
            EventInboxOptions.Deterministic());
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = new TradingHost(
            new Dictionary<Venue, IConnector>
            {
                [Venue.Binance] = binanceConnector,
                [Venue.Coinbase] = coinbaseConnector
            },
            events,
            runtime);
        host.RegisterStrategy<CrossVenueQuoteStrategy>(depth: 0);

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var detectedAt = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(
            binance,
            bid: 100.00m,
            ask: 100.10m,
            bidSize: 4m,
            askSize: 3m,
            detectedAt));
        coinbaseConnector.Events.Add(CreateQuoteReceived(
            coinbase,
            bid: 100.50m,
            ask: 100.60m,
            bidSize: 2m,
            askSize: 5m,
            detectedAt + Duration.FromMillis(1)));

        await host.RunAsync();

        Assert.True(opportunities.Reader.TryRead(out var opportunity));
        Assert.False(opportunities.Reader.TryRead(out _));
        Assert.Equal(asset, opportunity.Asset);
        Assert.Equal(Venue.Binance, opportunity.BuyVenue);
        Assert.Equal(Venue.Coinbase, opportunity.SellVenue);
        Assert.Equal(new Price(100.10m, Currency.USD), opportunity.BuyAsk);
        Assert.Equal(new Price(100.50m, Currency.USD), opportunity.SellBid);
        Assert.Equal(new Qty(2m), opportunity.ExecutableQuantity);
        Assert.Equal(new Money(0.40m, Currency.USD), opportunity.GrossSpreadPerUnit);
        Assert.Equal(0.40m / 100.10m * 10_000m, opportunity.GrossSpreadBps);
        Assert.Equal(detectedAt + Duration.FromMillis(1), opportunity.DetectedAt);
    }

    [Fact]
    public async Task RunAsync_DoesNotEmitCrossVenueOpportunityForNonCrossedQuotes()
    {
        using var runtime = new RhodiumRuntime();
        var events = new EventBus();
        await using var opportunities = events.CreateInbox<CrossVenueArbitrageOpportunity>(
            EventInboxOptions.Deterministic());
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = new TradingHost(
            new Dictionary<Venue, IConnector>
            {
                [Venue.Binance] = binanceConnector,
                [Venue.Coinbase] = coinbaseConnector
            },
            events,
            runtime);
        host.RegisterStrategy<CrossVenueQuoteStrategy>(depth: 0);

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        binanceConnector.Events.Add(CreateQuoteReceived(
            new Instrument(asset, Venue.Binance),
            bid: 100.00m,
            ask: 100.10m));
        coinbaseConnector.Events.Add(CreateQuoteReceived(
            new Instrument(asset, Venue.Coinbase),
            bid: 100.05m,
            ask: 100.15m));

        await host.RunAsync();

        Assert.False(opportunities.Reader.TryRead(out _));
    }

    [Fact]
    public async Task RunAsync_DoesNotEmitCrossVenueOpportunityForStaleQuotePair()
    {
        using var runtime = new RhodiumRuntime();
        var events = new EventBus();
        await using var opportunities = events.CreateInbox<CrossVenueArbitrageOpportunity>(
            EventInboxOptions.Deterministic());
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = new TradingHost(
            new Dictionary<Venue, IConnector>
            {
                [Venue.Binance] = binanceConnector,
                [Venue.Coinbase] = coinbaseConnector
            },
            events,
            runtime)
        {
            CrossVenueQuoteMaxAge = Duration.FromMillis(10)
        };
        host.RegisterStrategy<CrossVenueQuoteStrategy>(depth: 0);

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        binanceConnector.Events.Add(CreateQuoteReceived(
            new Instrument(asset, Venue.Binance),
            bid: 100.00m,
            ask: 100.10m,
            bidSize: 4m,
            askSize: 3m,
            Instant.FromUnixSeconds(10)));
        coinbaseConnector.Events.Add(CreateQuoteReceived(
            new Instrument(asset, Venue.Coinbase),
            bid: 100.50m,
            ask: 100.60m,
            bidSize: 2m,
            askSize: 5m,
            Instant.FromUnixSeconds(11)));

        await host.RunAsync();

        Assert.False(opportunities.Reader.TryRead(out _));
    }

    [Fact]
    public async Task RunAsync_RoutesMarketBuyToBestFreshAskVenueWhenEnabled()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<BestVenueBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 101.00m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 99.90m, ask: 100.25m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        Assert.Empty(binanceConnector.SubmittedOrders);
        var order = Assert.Single(coinbaseConnector.SubmittedOrders);
        Assert.Equal(coinbase, order.Instrument);
        Assert.Equal(Side.Buy, order.Side);
        Assert.Equal(OrderType.Market, order.Type);
    }

    [Fact]
    public async Task RunAsync_RoutesMarketBuyToBestNetVenueAfterFeesWhenConfigured()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<BestVenueBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.CrossVenueRoutingFees = new Dictionary<Venue, FeeParams>
        {
            [Venue.Binance] = FeeParams.MakerTaker(makerBps: 0m, takerBps: 1m),
            [Venue.Coinbase] = FeeParams.MakerTaker(makerBps: 0m, takerBps: 50m)
        };

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 100.50m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 99.90m, ask: 100.25m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var order = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Empty(coinbaseConnector.SubmittedOrders);
        Assert.Equal(binance, order.Instrument);
        Assert.Equal(Side.Buy, order.Side);
    }

    [Fact]
    public async Task RunAsync_ExcludesVenueFromBestMarketRoutingWhenPolicyDisallows()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<BestVenueBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.CrossVenueRoutingPolicies = new Dictionary<Venue, VenueRoutingPolicy>
        {
            [Venue.Coinbase] = VenueRoutingPolicy.Default with
            {
                AllowBestVenueMarketRouting = false
            }
        };

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 101.00m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 99.90m, ask: 100.25m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var order = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Empty(coinbaseConnector.SubmittedOrders);
        Assert.Equal(binance, order.Instrument);
        Assert.Equal(Side.Buy, order.Side);
    }

    [Fact]
    public async Task RunAsync_ExcludesVenueFromBestMarketRoutingWhenTimeInForcePolicyDisallows()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<IocBestVenueBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.CrossVenueRoutingPolicies = new Dictionary<Venue, VenueRoutingPolicy>
        {
            [Venue.Coinbase] = VenueRoutingPolicy.Default with
            {
                AllowedMarketTimeInForce = new HashSet<TimeInForce> { TimeInForce.Day }
            }
        };

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 101.00m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 99.90m, ask: 100.25m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var order = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Empty(coinbaseConnector.SubmittedOrders);
        Assert.Equal(binance, order.Instrument);
        Assert.Equal(Side.Buy, order.Side);
        Assert.Equal(TimeInForce.IOC, order.TimeInForce);
    }

    [Fact]
    public async Task RunAsync_ExcludesVenueFromBestMarketRoutingWhenQuantityBelowPolicyMinimum()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<BestVenueBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.CrossVenueRoutingPolicies = new Dictionary<Venue, VenueRoutingPolicy>
        {
            [Venue.Coinbase] = VenueRoutingPolicy.Default with
            {
                MinMarketRoutingQuantity = new Qty(2m)
            }
        };

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 101.00m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 99.90m, ask: 100.25m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var order = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Empty(coinbaseConnector.SubmittedOrders);
        Assert.Equal(binance, order.Instrument);
        Assert.Equal(Side.Buy, order.Side);
    }

    [Fact]
    public async Task RunAsync_ExcludesVenueFromBestMarketRoutingWhenNotionalBelowPolicyMinimum()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<BestVenueBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.CrossVenueRoutingPolicies = new Dictionary<Venue, VenueRoutingPolicy>
        {
            [Venue.Coinbase] = VenueRoutingPolicy.Default with
            {
                MinMarketRoutingNotional = Money.USD(200m)
            }
        };

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 101.00m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 99.90m, ask: 100.25m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var order = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Empty(coinbaseConnector.SubmittedOrders);
        Assert.Equal(binance, order.Instrument);
        Assert.Equal(Side.Buy, order.Side);
    }

    [Fact]
    public async Task RunAsync_RoutesMarketSellToBestFreshBidVenueWhenEnabled()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<BestVenueSellStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 100.50m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 101.00m, ask: 101.50m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        Assert.Empty(binanceConnector.SubmittedOrders);
        var order = Assert.Single(coinbaseConnector.SubmittedOrders);
        Assert.Equal(coinbase, order.Instrument);
        Assert.Equal(Side.Sell, order.Side);
        Assert.Equal(OrderType.Market, order.Type);
    }

    [Fact]
    public async Task RunAsync_RoutesMarketSellToBestNetVenueAfterFeesWhenConfigured()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<BestVenueSellStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.CrossVenueRoutingFees = new Dictionary<Venue, FeeParams>
        {
            [Venue.Binance] = FeeParams.MakerTaker(makerBps: 0m, takerBps: 1m),
            [Venue.Coinbase] = FeeParams.MakerTaker(makerBps: 0m, takerBps: 50m)
        };

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 100.50m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 100.25m, ask: 100.75m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var order = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Empty(coinbaseConnector.SubmittedOrders);
        Assert.Equal(binance, order.Instrument);
        Assert.Equal(Side.Sell, order.Side);
    }

    [Fact]
    public async Task RunAsync_IgnoresStaleBetterVenueWhenRoutingMarketOrder()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<BestVenueBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.CrossVenueQuoteMaxAge = Duration.FromMillis(10);

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        binanceConnector.Events.Add(CreateQuoteReceived(
            coinbase,
            bid: 99.90m,
            ask: 100.25m,
            detectedAt: Instant.FromUnixSeconds(9)));
        binanceConnector.Events.Add(CreateQuoteReceived(
            binance,
            bid: 100.00m,
            ask: 101.00m,
            detectedAt: Instant.FromUnixSeconds(10)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var order = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Empty(coinbaseConnector.SubmittedOrders);
        Assert.Equal(binance, order.Instrument);
        Assert.Equal(Side.Buy, order.Side);
    }

    [Fact]
    public async Task RunAsync_SweepsMarketBuyAcrossFreshVenuesWhenEnabled()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<SweepBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.UseCrossVenueMarketSweepRouting = true;

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 101.00m, bidSize: 4m, askSize: 4m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 99.90m, ask: 100.25m, bidSize: 2m, askSize: 2m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var coinbaseOrder = Assert.Single(coinbaseConnector.SubmittedOrders);
        var binanceOrder = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Equal(coinbase, coinbaseOrder.Instrument);
        Assert.Equal(new Qty(2m), coinbaseOrder.Quantity);
        Assert.Equal(Side.Buy, coinbaseOrder.Side);
        Assert.Equal(binance, binanceOrder.Instrument);
        Assert.Equal(new Qty(3m), binanceOrder.Quantity);
        Assert.Equal(Side.Buy, binanceOrder.Side);
    }

    [Fact]
    public async Task RunAsync_AppliesVenuePolicyCapWhenSweepingMarketOrder()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<SweepBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.UseCrossVenueMarketSweepRouting = true;
        host.CrossVenueRoutingPolicies = new Dictionary<Venue, VenueRoutingPolicy>
        {
            [Venue.Coinbase] = VenueRoutingPolicy.Default with
            {
                MaxMarketSweepQuantity = new Qty(2m)
            }
        };

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 101.00m, bidSize: 10m, askSize: 10m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 99.90m, ask: 100.25m, bidSize: 10m, askSize: 10m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var coinbaseOrder = Assert.Single(coinbaseConnector.SubmittedOrders);
        var binanceOrder = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Equal(coinbase, coinbaseOrder.Instrument);
        Assert.Equal(new Qty(2m), coinbaseOrder.Quantity);
        Assert.Equal(binance, binanceOrder.Instrument);
        Assert.Equal(new Qty(3m), binanceOrder.Quantity);
    }

    [Fact]
    public async Task RunAsync_ExcludesVenueFromMarketSweepWhenQuantityBelowPolicyMinimum()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<SweepBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.UseCrossVenueMarketSweepRouting = true;
        host.CrossVenueRoutingPolicies = new Dictionary<Venue, VenueRoutingPolicy>
        {
            [Venue.Coinbase] = VenueRoutingPolicy.Default with
            {
                MinMarketRoutingQuantity = new Qty(6m)
            }
        };

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 101.00m, bidSize: 10m, askSize: 10m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 99.90m, ask: 100.25m, bidSize: 10m, askSize: 10m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var order = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Empty(coinbaseConnector.SubmittedOrders);
        Assert.Equal(binance, order.Instrument);
        Assert.Equal(new Qty(5m), order.Quantity);
    }

    [Fact]
    public async Task RunAsync_ExcludesVenueFromMarketSweepWhenVisibleNotionalBelowPolicyMinimum()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<SweepBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.UseCrossVenueMarketSweepRouting = true;
        host.CrossVenueRoutingPolicies = new Dictionary<Venue, VenueRoutingPolicy>
        {
            [Venue.Coinbase] = VenueRoutingPolicy.Default with
            {
                MinMarketRoutingNotional = Money.USD(200m)
            }
        };

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 101.00m, bidSize: 10m, askSize: 10m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 99.90m, ask: 100.25m, bidSize: 10m, askSize: 1m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var order = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Empty(coinbaseConnector.SubmittedOrders);
        Assert.Equal(binance, order.Instrument);
        Assert.Equal(new Qty(5m), order.Quantity);
    }

    [Fact]
    public async Task RunAsync_SweepsMarketSellAcrossFreshVenuesWhenEnabled()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<SweepSellStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.UseCrossVenueMarketSweepRouting = true;

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        var coinbase = new Instrument(asset, Venue.Coinbase);
        var now = Instant.FromUnixSeconds(10);
        binanceConnector.Events.Add(CreateQuoteReceived(binance, bid: 100.00m, ask: 100.50m, bidSize: 4m, askSize: 4m, detectedAt: now));
        binanceConnector.Events.Add(CreateQuoteReceived(coinbase, bid: 101.00m, ask: 101.50m, bidSize: 2m, askSize: 2m, detectedAt: now + Duration.FromMillis(1)));
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var coinbaseOrder = Assert.Single(coinbaseConnector.SubmittedOrders);
        var binanceOrder = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Equal(coinbase, coinbaseOrder.Instrument);
        Assert.Equal(new Qty(2m), coinbaseOrder.Quantity);
        Assert.Equal(Side.Sell, coinbaseOrder.Side);
        Assert.Equal(binance, binanceOrder.Instrument);
        Assert.Equal(new Qty(3m), binanceOrder.Quantity);
        Assert.Equal(Side.Sell, binanceOrder.Side);
    }

    [Fact]
    public async Task RunAsync_FallsBackToSingleMarketOrderWhenSweepHasNoFreshQuotes()
    {
        using var runtime = new RhodiumRuntime();
        var binanceConnector = new TestConnector();
        var coinbaseConnector = new TestConnector();
        using var host = CreateCrossVenueRoutingHost<SweepBuyStrategy>(
            runtime,
            binanceConnector,
            coinbaseConnector);
        host.UseCrossVenueMarketSweepRouting = true;

        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        var binance = new Instrument(asset, Venue.Binance);
        binanceConnector.Events.Add(CreateBarClosed(binance, 100.50m));

        await host.RunAsync();

        var order = Assert.Single(binanceConnector.SubmittedOrders);
        Assert.Empty(coinbaseConnector.SubmittedOrders);
        Assert.Equal(binance, order.Instrument);
        Assert.Equal(new Qty(5m), order.Quantity);
        Assert.Equal(Side.Buy, order.Side);
    }

    [Fact]
    public async Task RunAsync_ProcessesConnectorEventsAndDispatchesStrategies()
    {
        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        var strategyId = host.RegisterStrategy<BuyingStrategy>(depth: 0);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        connector.Events.Add(new BarClosed(
            instrument,
            new Bar(
                new Price(120m, Currency.USD),
                new Price(125m, Currency.USD),
                new Price(119m, Currency.USD),
                new Price(123m, Currency.USD),
                new Qty(10_000m),
                default,
                Duration.FromMinutes(1))));

        await host.RunAsync();

        Assert.Single(connector.SubmittedOrders);
        Assert.Equal(strategyId, connector.SubmittedOrders[0].StrategyId);
    }

    [Fact]
    public async Task RunAsync_PreservesGeneratedPovExecutionParameters()
    {
        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        host.RegisterStrategy<PovGeneratedStrategy>(depth: 0);

        connector.Events.Add(CreateBarClosed(123m));

        await host.RunAsync();

        var order = Assert.Single(connector.SubmittedOrders);
        Assert.Equal("POV", order.ExecAlgorithmId);
        Assert.NotNull(order.ExecAlgorithmParams);
        Assert.Equal("60", order.ExecAlgorithmParams["horizon_secs"]);
        Assert.Equal("0.25", order.ExecAlgorithmParams["participation_rate"]);
    }

    [Fact]
    public async Task SameGeneratedStrategy_RunsLiveVectorAndQueueWithoutCodeChanges()
    {
        var liveBar = CreateBarClosed(123m);

        using (var runtime = new RhodiumRuntime())
        {
            var connector = new TestConnector();
            using var host = new TradingHost(connector, new EventBus(), runtime);
            var strategyId = host.RegisterStrategy<CrossModeGeneratedStrategy>(depth: 0);
            connector.Events.Add(liveBar);

            await host.RunAsync();

            var order = Assert.Single(connector.SubmittedOrders);
            Assert.Equal(strategyId, order.StrategyId);
            Assert.Equal(OrderType.Limit, order.Type);
            Assert.Equal(new Price(123m, Currency.USD), order.LimitPrice);
        }

        var vector = Rhodium.Simulation.Rhodium.Simulate<CrossModeGeneratedStrategy>()
            .WithHistory(SharedHistory.Load([CreateBarClosed(123m)]))
            .WithFidelity(SimulationFidelity.Vector)
            .Run();

        Assert.Single(vector.OrderIntents);
        Assert.Single(vector.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Qty(1m), vector.Runs.Single().FinalSnapshot.GetPositions().ToArray().Single().Quantity);

        var queue = Rhodium.Simulation.Rhodium.Simulate<CrossModeGeneratedStrategy>()
            .WithHistory(SharedHistory.Load([CreateBarClosed(123m), CreateBarClosed(123m)]))
            .WithFidelity(SimulationFidelity.Queue)
            .Run();

        Assert.Single(queue.OrderIntents);
        Assert.Single(queue.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Qty(1m), queue.Runs.Single().FinalSnapshot.GetPositions().ToArray().Single().Quantity);
    }

    [Fact]
    public async Task RunAsync_RoutesExecutionEventsOnlyToOwningStrategy()
    {
        ExecutionHookStrategy.Reset();

        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        var owner = host.RegisterStrategy<ExecutionHookStrategy>(depth: 0);
        var other = host.RegisterStrategy<ExecutionHookStrategy>(depth: 0);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        connector.Events.Add(new OrderFilled(
            OrderId.New(),
            instrument,
            VariantId: 0,
            owner,
            Side.Buy,
            new Qty(2m),
            new Price(101m, Currency.USD),
            Money.Zero(Currency.USD)));

        await host.RunAsync();

        Assert.Equal([owner], ExecutionHookStrategy.FillHookIds);
        Assert.Equal([owner], ExecutionHookStrategy.PositionOpenedHookIds);
        Assert.DoesNotContain(other, ExecutionHookStrategy.FillHookIds);
        Assert.Equal(0, ExecutionHookStrategy.MarketTickCount);
    }

    [Fact]
    public async Task RunAsync_RoutesOrderLifecycleHooksToOwningStrategy()
    {
        OrderLifecycleHookStrategy.Reset();

        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        var owner = host.RegisterStrategy<OrderLifecycleHookStrategy>(depth: 0);
        var other = host.RegisterStrategy<OrderLifecycleHookStrategy>(depth: 0);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        connector.Events.Add(new OrderAccepted(OrderId.New(), owner, VariantId: 0));
        connector.Events.Add(new OrderRejected(OrderId.New(), owner, VariantId: 0, "bad price"));
        connector.Events.Add(new OrderCancelled(OrderId.New(), owner, VariantId: 0, new Qty(0m), "user"));
        connector.Events.Add(new OrderExpired(OrderId.New(), owner, VariantId: 0));

        await host.RunAsync();

        Assert.Equal([owner], OrderLifecycleHookStrategy.AcceptedIds);
        Assert.Equal([owner], OrderLifecycleHookStrategy.RejectedIds);
        Assert.Equal([owner], OrderLifecycleHookStrategy.CancelledIds);
        Assert.Equal([owner], OrderLifecycleHookStrategy.ExpiredIds);
        Assert.DoesNotContain(other, OrderLifecycleHookStrategy.AcceptedIds);
    }

    [Fact]
    public async Task RunAsync_SynthesizesPositionOpenedChangedAndClosedOnce()
    {
        ExecutionHookStrategy.Reset();

        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        var owner = host.RegisterStrategy<ExecutionHookStrategy>(depth: 0);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        connector.Events.Add(new OrderFilled(
            OrderId.New(), instrument, VariantId: 0, owner, Side.Buy, new Qty(2m), new Price(101m, Currency.USD), Money.Zero(Currency.USD)));
        connector.Events.Add(new OrderFilled(
            OrderId.New(), instrument, VariantId: 0, owner, Side.Buy, new Qty(1m), new Price(102m, Currency.USD), Money.Zero(Currency.USD)));
        connector.Events.Add(new OrderFilled(
            OrderId.New(), instrument, VariantId: 0, owner, Side.Sell, new Qty(3m), new Price(103m, Currency.USD), Money.Zero(Currency.USD)));

        await host.RunAsync();

        Assert.Equal([owner], ExecutionHookStrategy.PositionOpenedHookIds);
        Assert.Equal([owner], ExecutionHookStrategy.PositionChangedHookIds);
        Assert.Equal([owner], ExecutionHookStrategy.PositionClosedHookIds);
        Assert.Equal(3, ExecutionHookStrategy.FillHookIds.Count);
    }

    [Fact]
    public async Task RunAsync_RunsGroupHookAfterExecutionChangesPortfolioState()
    {
        ExecutionHookStrategy.Reset();
        GroupAfterFillStrategy.Reset();

        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        var leaf = host.RegisterStrategy<ExecutionHookStrategy>(depth: 0);
        host.RegisterStrategy<GroupAfterFillStrategy>(depth: 1, children: [leaf]);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        connector.Events.Add(new OrderFilled(
            OrderId.New(), instrument, VariantId: 0, leaf, Side.Buy, new Qty(2m), new Price(101m, Currency.USD), Money.Zero(Currency.USD)));

        await host.RunAsync();

        Assert.Equal(1, GroupAfterFillStrategy.GroupCount);
        Assert.Equal(1, GroupAfterFillStrategy.LastChildCount);
        Assert.Equal(leaf, GroupAfterFillStrategy.LastChildId);
    }

    [Fact]
    public async Task RunAsync_RoutesQuoteAndBarToMatchingGeneratedHooksOnly()
    {
        EventSeparationStrategy.Reset();

        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        var strategyId = host.RegisterStrategy<EventSeparationStrategy>(depth: 0);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        connector.Events.Add(new QuoteReceived(
            instrument,
            new Quote(
                new Price(100m, Currency.USD),
                new Price(100.01m, Currency.USD),
                new Qty(200m),
                new Qty(100m),
                default)));

        await host.RunAsync();

        Assert.Equal(1, EventSeparationStrategy.QuoteCount);
        Assert.Equal(0, EventSeparationStrategy.BarCount);
        Assert.Single(connector.SubmittedOrders);
        Assert.Equal(strategyId, connector.SubmittedOrders[0].StrategyId);

        EventSeparationStrategy.Reset();
        connector.SubmittedOrders.Clear();
        connector.Events.Clear();
        connector.Events.Add(new BarClosed(
            instrument,
            new Bar(
                new Price(120m, Currency.USD),
                new Price(125m, Currency.USD),
                new Price(119m, Currency.USD),
                new Price(123m, Currency.USD),
                new Qty(10_000m),
                default,
                Duration.FromMinutes(1))));

        await host.RunAsync();

        Assert.Equal(0, EventSeparationStrategy.QuoteCount);
        Assert.Equal(1, EventSeparationStrategy.BarCount);
        Assert.Empty(connector.SubmittedOrders);
    }

    [Fact]
    public async Task RunAsync_RoutesTradeAndBookToMatchingGeneratedHooks()
    {
        TradeBookStrategy.Reset();

        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        host.RegisterStrategy<TradeBookStrategy>(depth: 0);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        connector.Events.Add(new TradeOccurred(
            instrument,
            new Trade(new Price(101m, Currency.USD), new Qty(5m), Side.Buy, default)));
        connector.Events.Add(new BookUpdated(
            instrument,
            new Book
            {
                Instrument = instrument,
                Time = default,
                Bids = [new Level(new Price(100m, Currency.USD), new Qty(20m))],
                Asks = [new Level(new Price(100.01m, Currency.USD), new Qty(10m))]
            }));

        await host.RunAsync();

        Assert.Equal(1, TradeBookStrategy.TradeCount);
        Assert.Equal(1, TradeBookStrategy.BookCount);
    }

    [Fact]
    public async Task RunAsync_BookOnlyStreamResolvesAtBidLimitPrice()
    {
        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        var strategyId = host.RegisterStrategy<BookBidBuyingStrategy>(depth: 0);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        connector.Events.Add(new BookUpdated(
            instrument,
            new Book
            {
                Instrument = instrument,
                Time = default,
                Bids =
                [
                    new Level(new Price(100m, Currency.USD), new Qty(20m)),
                    new Level(new Price(99.99m, Currency.USD), new Qty(50m))
                ],
                Asks =
                [
                    new Level(new Price(100.01m, Currency.USD), new Qty(10m)),
                    new Level(new Price(100.02m, Currency.USD), new Qty(30m))
                ]
            }));

        await host.RunAsync();

        var order = Assert.Single(connector.SubmittedOrders);
        Assert.Equal(strategyId, order.StrategyId);
        Assert.Equal(OrderType.Limit, order.Type);
        Assert.Equal(100m, order.LimitPrice?.Value);
    }

    [Fact]
    public async Task RunAsync_PreservesStopPriceWhenSubmittingGeneratedStopLimit()
    {
        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        var strategyId = host.RegisterStrategy<StopLimitGeneratedStrategy>(depth: 0);

        connector.Events.Add(CreateBarClosed(123m));

        await host.RunAsync();

        var order = Assert.Single(connector.SubmittedOrders);
        Assert.Equal(strategyId, order.StrategyId);
        Assert.Equal(OrderType.StopLimit, order.Type);
        Assert.Equal(new Price(124m, Currency.USD), order.StopPrice);
        Assert.Equal(new Price(123m, Currency.USD), order.LimitPrice);
    }

    [Fact]
    public async Task RunAsync_PreservesGoodTilDateWhenSubmittingGeneratedGtdOrder()
    {
        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        var strategyId = host.RegisterStrategy<GoodTilDateGeneratedStrategy>(depth: 0);

        connector.Events.Add(CreateBarClosed(123m));

        await host.RunAsync();

        var order = Assert.Single(connector.SubmittedOrders);
        Assert.Equal(strategyId, order.StrategyId);
        Assert.Equal(OrderType.Limit, order.Type);
        Assert.Equal(TimeInForce.GTD, order.TimeInForce);
        Assert.Equal(GoodTilDateGeneratedStrategy.Expiry, order.GoodTilDate);
    }

    [Fact]
    public async Task RunAsync_RoutesLifecycleAndScheduledHooks()
    {
        LifecycleHookStrategy.Reset();

        using var runtime = new RhodiumRuntime();
        var connector = new TestConnector();
        using var host = new TradingHost(connector, new EventBus(), runtime);
        var strategyId = host.RegisterStrategy<LifecycleHookStrategy>(depth: 0);

        connector.Events.Add(new SessionStarted());
        connector.Events.Add(new Scheduled("rebalance"));
        connector.Events.Add(new SessionEnded());

        await host.RunAsync();

        Assert.Equal(1, LifecycleHookStrategy.StartCount);
        Assert.Equal(1, LifecycleHookStrategy.ScheduledCount);
        Assert.Equal("rebalance", LifecycleHookStrategy.LastTimerName);
        Assert.Equal(1, LifecycleHookStrategy.StopCount);
        Assert.Single(connector.SubmittedOrders);
        Assert.Equal(strategyId, connector.SubmittedOrders[0].StrategyId);
    }

    [Fact]
    public void Dispose_DisposesConnector()
    {
        var connector = new TestConnector();
        using var runtime = new RhodiumRuntime();
        var host = new TradingHost(connector, new EventBus(), runtime);

        host.Dispose();

        Assert.True(connector.WasDisposed);
    }

    private sealed class TestConnector : IConnector
    {
        public ExchangeId Exchange => ExchangeId.Replay;
        public IRateLimiter RateLimiter => NoopRateLimiter.Instance;
        public bool IsConnected { get; private set; }
        public bool WasStarted { get; private set; }
        public bool WasDisposed { get; private set; }
        public List<Subscription> Subscriptions { get; } = [];
        public List<FinanceEvent> Events { get; } = [];
        public List<SubmitOrder> SubmittedOrders { get; } = [];

        public Task StartAsync(IEnumerable<Subscription> subscriptions, IEventPublisher events, CancellationToken ct)
        {
            WasStarted = true;
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
            WasDisposed = true;
            IsConnected = false;
        }
    }

    private static BarClosed CreateBarClosed(decimal close)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        return CreateBarClosed(instrument, close);
    }

    private static BarClosed CreateBarClosed(Instrument instrument, decimal close)
    {
        return new BarClosed(
            instrument,
            new Bar(
                new Price(close, Currency.USD),
                new Price(close + 1m, Currency.USD),
                new Price(close - 1m, Currency.USD),
                new Price(close, Currency.USD),
                new Qty(10_000m),
                default,
                Duration.FromMinutes(1)));
    }

    private static QuoteReceived CreateQuoteReceived(
        Instrument instrument,
        decimal bid,
        decimal ask,
        decimal bidSize = 10m,
        decimal askSize = 10m,
        Instant detectedAt = default)
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

    private static TradingHost CreateCrossVenueRoutingHost<TStrategy>(
        RhodiumRuntime runtime,
        TestConnector binanceConnector,
        TestConnector coinbaseConnector)
        where TStrategy : Strategy, new()
    {
        var host = new TradingHost(
            new Dictionary<Venue, IConnector>
            {
                [Venue.Binance] = binanceConnector,
                [Venue.Coinbase] = coinbaseConnector
            },
            new EventBus(),
            runtime)
        {
            UseCrossVenueBestMarketRouting = true
        };
        host.RegisterStrategy<TStrategy>(depth: 0);
        return host;
    }

    private sealed class TestStrategy : Strategy
    {
        public static bool WasInitialized { get; private set; }

        protected override void OnInitialize(in SetupContext setup)
        {
            WasInitialized = true;
        }

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
        {
        }
    }

    private sealed class InstrumentStrategy : Strategy
    {
        protected override void OnInitialize(in SetupContext setup)
        {
            setup.AddEquity("SPY");
        }

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
        {
        }
    }

    private sealed class ExecutionHookStrategy : Strategy
    {
        public static List<StrategyId> FillHookIds { get; } = new(capacity: 16);
        public static List<StrategyId> PositionOpenedHookIds { get; } = new(capacity: 16);
        public static List<StrategyId> PositionChangedHookIds { get; } = new(capacity: 16);
        public static List<StrategyId> PositionClosedHookIds { get; } = new(capacity: 16);
        public static int MarketTickCount { get; private set; }

        public static void Reset()
        {
            FillHookIds.Clear();
            PositionOpenedHookIds.Clear();
            PositionChangedHookIds.Clear();
            PositionClosedHookIds.Clear();
            MarketTickCount = 0;
        }

        protected override void OnInitialize(in SetupContext setup)
            => setup.AddEquity("SPY");

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
            => MarketTickCount++;

        protected override void OnOrderFilled(ref FillContext fill)
            => FillHookIds.Add(Id);

        protected override void OnPositionOpened(ref PositionContext position)
            => PositionOpenedHookIds.Add(Id);

        protected override void OnPositionChanged(ref PositionContext position)
            => PositionChangedHookIds.Add(Id);

        protected override void OnPositionClosed(ref PositionContext position)
            => PositionClosedHookIds.Add(Id);
    }

    private sealed class OrderLifecycleHookStrategy : Strategy
    {
        public static List<StrategyId> AcceptedIds { get; } = new(capacity: 16);
        public static List<StrategyId> RejectedIds { get; } = new(capacity: 16);
        public static List<StrategyId> CancelledIds { get; } = new(capacity: 16);
        public static List<StrategyId> ExpiredIds { get; } = new(capacity: 16);

        public static void Reset()
        {
            AcceptedIds.Clear();
            RejectedIds.Clear();
            CancelledIds.Clear();
            ExpiredIds.Clear();
        }

        protected override void OnInitialize(in SetupContext setup)
            => setup.AddEquity("SPY");

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
        {
        }

        protected override void OnOrderAccepted(ref OrderContext order)
            => AcceptedIds.Add(Id);

        protected override void OnOrderRejected(ref OrderContext order)
            => RejectedIds.Add(Id);

        protected override void OnOrderCancelled(ref OrderContext order)
            => CancelledIds.Add(Id);

        protected override void OnOrderExpired(ref OrderContext order)
            => ExpiredIds.Add(Id);
    }

    private sealed class GroupAfterFillStrategy : Strategy
    {
        public static int GroupCount { get; private set; }
        public static int LastChildCount { get; private set; }
        public static StrategyId LastChildId { get; private set; }

        public static void Reset()
        {
            GroupCount = 0;
            LastChildCount = 0;
            LastChildId = default;
        }

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
        {
        }

        protected override void OnGroup(ref GroupContext group)
        {
            GroupCount++;
            LastChildCount = group.Children.Length;
            if (group.Children.Length > 0)
                LastChildId = group.Children[0].StrategyId;
        }
    }

    private sealed class LifecycleHookStrategy : Strategy
    {
        public static int StartCount { get; private set; }
        public static int ScheduledCount { get; private set; }
        public static int StopCount { get; private set; }
        public static string? LastTimerName { get; private set; }
        private AssetId _spy;

        public static void Reset()
        {
            StartCount = 0;
            ScheduledCount = 0;
            StopCount = 0;
            LastTimerName = null;
        }

        protected override void OnInitialize(in SetupContext setup)
        {
            _spy = setup.AddEquity("SPY");
        }

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
        {
        }

        protected override void OnStart(ref LifecycleContext lifecycle)
            => StartCount++;

        protected override void OnScheduled(ref TimerContext timer)
        {
            ScheduledCount++;
            LastTimerName = timer.Name;
            timer.Buy(_spy, new Qty(1m), Execution.Market());
        }

        protected override void OnStop(ref LifecycleContext lifecycle)
            => StopCount++;
    }

}

internal sealed partial class BuyingStrategy : Strategy
{
    private AssetId _spy;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId == _spy && bar.Close == 123.0)
                bar.Buy(new Qty(1m), Execution.Limit().AtBid());
    }
}

internal sealed partial class CrossModeGeneratedStrategy : Strategy
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
        bar.Buy(new Qty(1m), Execution.Limit().At(new Price(123m, Currency.USD)));
    }
}

internal sealed partial class PovGeneratedStrategy : Strategy
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
        bar.Buy(new Qty(10m), Execution.Pov(0.25m).Over(Duration.FromSeconds(60)));
    }
}

internal sealed partial class MultiVenueGeneratedStrategy : Strategy
{
    private AssetId _spy;
    private AssetId _btc;
    private bool _submittedSpy;
    private bool _submittedBtc;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddInstrument(new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ));
        _btc = setup.AddInstrument(new Instrument(new Asset("BTCUSDT", AssetClass.Crypto), Venue.Binance));
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.Close != 123.0)
            return;

        if (bar.AssetId == _spy && !_submittedSpy)
        {
            _submittedSpy = true;
            bar.Buy(new Qty(1m), Execution.Market());
        }
        else if (bar.AssetId == _btc && !_submittedBtc)
        {
            _submittedBtc = true;
            bar.Buy(new Qty(1m), Execution.Market());
        }
    }
}

internal sealed class CrossVenueQuoteStrategy : Strategy
{
    protected override void OnInitialize(in SetupContext setup)
    {
        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        setup.AddInstrument(new Instrument(asset, Venue.Binance));
        setup.AddInstrument(new Instrument(asset, Venue.Coinbase));
    }

    protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
    {
    }
}

internal sealed partial class BestVenueBuyStrategy : Strategy
{
    private AssetId _binance;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        _binance = setup.AddInstrument(new Instrument(asset, Venue.Binance));
        setup.AddInstrument(new Instrument(asset, Venue.Coinbase));
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _binance)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Market());
    }
}

internal sealed partial class IocBestVenueBuyStrategy : Strategy
{
    private AssetId _binance;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        _binance = setup.AddInstrument(new Instrument(asset, Venue.Binance));
        setup.AddInstrument(new Instrument(asset, Venue.Coinbase));
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _binance)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Market().ImmediateOrCancel());
    }
}

internal sealed partial class BestVenueSellStrategy : Strategy
{
    private AssetId _binance;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        _binance = setup.AddInstrument(new Instrument(asset, Venue.Binance));
        setup.AddInstrument(new Instrument(asset, Venue.Coinbase));
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _binance)
            return;

        _submitted = true;
        bar.Sell(new Qty(1m), Execution.Market());
    }
}

internal sealed partial class SweepBuyStrategy : Strategy
{
    private AssetId _binance;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        _binance = setup.AddInstrument(new Instrument(asset, Venue.Binance));
        setup.AddInstrument(new Instrument(asset, Venue.Coinbase));
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _binance)
            return;

        _submitted = true;
        bar.Buy(new Qty(5m), Execution.Market());
    }
}

internal sealed partial class SweepSellStrategy : Strategy
{
    private AssetId _binance;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        var asset = new Asset("BTCUSDT", AssetClass.Crypto);
        _binance = setup.AddInstrument(new Instrument(asset, Venue.Binance));
        setup.AddInstrument(new Instrument(asset, Venue.Coinbase));
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _binance)
            return;

        _submitted = true;
        bar.Sell(new Qty(5m), Execution.Market());
    }
}

internal sealed partial class EventSeparationStrategy : Strategy
{
    public static int QuoteCount { get; private set; }
    public static int BarCount { get; private set; }

    private AssetId _spy;

    [QuoteField(ReadOnly = true)]
    public partial double QuoteClose { get; }

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
        QuoteCount = 0;
        BarCount = 0;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnQuote(ref QuoteContext quote)
    {
        if (quote.AssetId != _spy)
            return;

        QuoteCount++;
        quote.Buy(new Qty(1m), Execution.Market());
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId == _spy)
            BarCount++;
    }
}

internal sealed partial class TradeBookStrategy : Strategy
{
    public static int TradeCount { get; private set; }
    public static int BookCount { get; private set; }

    private AssetId _spy;

    [TradeField(ReadOnly = true)]
    public partial double TradeClose { get; }

    [BookField(ReadOnly = true)]
    public partial double BookClose { get; }

    public static void Reset()
    {
        TradeCount = 0;
        BookCount = 0;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnTrade(ref TradeContext trade)
    {
        if (trade.AssetId == _spy)
            TradeCount++;
    }

    partial void OnBook(ref BookContext book)
    {
        if (book.AssetId == _spy)
            BookCount++;
    }
}

internal sealed partial class BookBidBuyingStrategy : Strategy
{
    private AssetId _spy;

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBook(ref BookContext book)
    {
        if (book.AssetId == _spy)
        book.Buy(new Qty(1m), Execution.Limit().AtBid());
    }
}

internal sealed partial class StopLimitGeneratedStrategy : Strategy
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
        bar.Buy(
            new Qty(1m),
            Execution.StopLimit(
                new Price(124m, Currency.USD),
                new Price(123m, Currency.USD)));
    }
}

internal sealed partial class GoodTilDateGeneratedStrategy : Strategy
{
    public static readonly Instant Expiry = Instant.FromUnixSeconds(10_000);

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
        bar.Buy(
            new Qty(1m),
            Execution.Limit()
                .At(new Price(123m, Currency.USD))
                .GoodTil(Expiry));
    }
}
