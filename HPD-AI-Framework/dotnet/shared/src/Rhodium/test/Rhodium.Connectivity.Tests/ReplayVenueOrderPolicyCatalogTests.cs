using HPD.Events;
using HPD.Events.Core;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests;

public class ReplayVenueOrderPolicyCatalogTests
{
    private static Instrument TestInstrument => new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    [Fact]
    public void KnownVenues_MergesCryptoAndListedEquityPolicies()
    {
        var policies = ReplayVenueOrderPolicyCatalog.KnownVenues();

        Assert.True(policies.ContainsKey(Venue.Binance));
        Assert.True(policies.ContainsKey(Venue.Coinbase));
        Assert.True(policies.ContainsKey(Venue.NASDAQ));
        Assert.True(policies.ContainsKey(Venue.NYSE));
    }

    [Fact]
    public void BundledDatasetIds_IncludesReplayOrderPolicyFeeds()
    {
        Assert.Contains("replay-order-crypto-spot", ReplayVenueOrderPolicyCatalog.BundledDatasetIds);
        Assert.Contains("replay-order-us-listed-equities", ReplayVenueOrderPolicyCatalog.BundledDatasetIds);
    }

    [Fact]
    public void FromBundledPolicyFeed_LoadsUsListedEquitiesDataset()
    {
        var policies = ReplayVenueOrderPolicyCatalog.FromBundledPolicyFeed("replay-order-us-listed-equities");

        Assert.Equal(new Qty(1m), policies[Venue.NASDAQ].MinOrderQuantity);
        Assert.Equal(Money.USD(1m), policies[Venue.NYSE].MinOrderNotional);
        Assert.Contains(OrderType.MarketToLimit, policies[Venue.NASDAQ].AllowedOrderTypes!);
        Assert.DoesNotContain(TimeInForce.GTC, policies[Venue.NASDAQ].AllowedTimeInForce!);
    }

    [Fact]
    public void BundledPolicyFeedDataset_ReturnsEmbeddedCsv()
    {
        var feed = ReplayVenueOrderPolicyCatalog.BundledPolicyFeedDataset("replay-order-crypto-spot");

        Assert.Contains("Binance", feed);
        Assert.Contains("Coinbase", feed);
        Assert.Contains("allowed_order_types", feed);
    }

    [Fact]
    public void InteractiveBrokersListedEquity_RequiresWholeShareOrders()
    {
        var policy = ReplayVenueOrderPolicyCatalog.InteractiveBrokersListedEquity();

        Assert.Equal(new Qty(1m), policy.MinOrderQuantity);
        Assert.Equal(Money.USD(1m), policy.MinOrderNotional);
        Assert.NotNull(policy.AllowedTimeInForce);
        Assert.DoesNotContain(TimeInForce.GTC, policy.AllowedTimeInForce);
        Assert.Contains(OrderType.MarketToLimit, policy.AllowedOrderTypes!);
    }

    [Fact]
    public void FromPolicyFeed_LoadsProviderPolicies()
    {
        const string feed = """
            # venue,allowed_order_types,allowed_tif,allow_post_only,min_qty,min_notional,currency
            venue,allowed_order_types,allowed_tif,allow_post_only,min_qty,min_notional,currency
            Binance,Market;Limit;StopLimit,IOC|FOK,true,0.01,25,USD
            ARCA,Limit,DAY|IOC,false,1,1,USD
            """;

        var policies = ReplayVenueOrderPolicyCatalog.FromPolicyFeed(feed);

        Assert.Contains(OrderType.Market, policies[Venue.Binance].AllowedOrderTypes!);
        Assert.Contains(OrderType.StopLimit, policies[Venue.Binance].AllowedOrderTypes!);
        Assert.Contains(TimeInForce.FOK, policies[Venue.Binance].AllowedTimeInForce!);
        Assert.True(policies[Venue.Binance].AllowPostOnly);
        Assert.Equal(new Qty(0.01m), policies[Venue.Binance].MinOrderQuantity);
        Assert.Equal(Money.USD(25m), policies[Venue.Binance].MinOrderNotional);

        Assert.Equal(new HashSet<OrderType> { OrderType.Limit }, policies["ARCA"].AllowedOrderTypes);
        Assert.False(policies["ARCA"].AllowPostOnly);
    }

    [Fact]
    public void FromPolicyFeed_OverlaysBasePolicies()
    {
        const string feed = "venue,allowed_order_types,allowed_tif,allow_post_only,min_qty,min_notional,currency\nCoinbase,,,false,,,";

        var policies = ReplayVenueOrderPolicyCatalog.FromPolicyFeed(
            feed,
            ReplayVenueOrderPolicyCatalog.CryptoSpot());

        Assert.Equal(Money.USD(1m), policies[Venue.Coinbase].MinOrderNotional);
        Assert.False(policies[Venue.Coinbase].AllowPostOnly);
        Assert.Contains(OrderType.TrailingStopMarket, policies[Venue.Coinbase].AllowedOrderTypes!);
    }

    [Fact]
    public void FromPolicyFeedFile_LoadsProviderPolicies()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-replay-order-policy.csv");
        File.WriteAllText(
            path,
            "venue,allowed_order_types,allowed_tif,allow_post_only,min_qty,min_notional,currency\nNASDAQ,Market|Limit,DAY|IOC,false,1,10,USD\n");

        try
        {
            var policies = ReplayVenueOrderPolicyCatalog.FromPolicyFeedFile(path);

            Assert.Contains(OrderType.Market, policies[Venue.NASDAQ].AllowedOrderTypes!);
            Assert.Contains(TimeInForce.IOC, policies[Venue.NASDAQ].AllowedTimeInForce!);
            Assert.False(policies[Venue.NASDAQ].AllowPostOnly);
            Assert.Equal(Money.USD(10m), policies[Venue.NASDAQ].MinOrderNotional);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void FromPolicyFeed_RejectsInvalidOrderType()
    {
        const string feed = "venue,allowed_order_types\nBinance,NeverOrder";

        Assert.Throws<FormatException>(() => ReplayVenueOrderPolicyCatalog.FromPolicyFeed(feed));
    }

    [Fact]
    public void FromBundledPolicyFeed_RejectsUnknownDataset()
    {
        Assert.Throws<ArgumentException>(() => ReplayVenueOrderPolicyCatalog.FromBundledPolicyFeed("missing"));
    }

    [Fact]
    public async Task CatalogPolicy_IsUsableByReplayConnector()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(1))
        {
            VenueOrderPolicies = ReplayVenueOrderPolicyCatalog.USListedEquities()
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
                        new Qty(0.5m),
                        new Price(99m, Currency.USD)),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var rejection = Assert.Single(events.EmittedEvents.OfType<OrderRejected>());
        Assert.Contains("minimum order quantity", rejection.Reason);
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithQuotes(int count)
    {
        var time = DualTimestamp.Synchronized(Instant.Now);
        for (var i = 0; i < count; i++)
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

    private sealed class TestEventPublisher : IEventPublisher
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
}
