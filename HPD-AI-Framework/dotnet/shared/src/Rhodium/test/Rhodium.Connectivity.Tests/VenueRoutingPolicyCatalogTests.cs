using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests;

public class VenueRoutingPolicyCatalogTests
{
    [Fact]
    public void CryptoSpot_IncludesBinanceAndCoinbasePolicies()
    {
        var policies = VenueRoutingPolicyCatalog.CryptoSpot();

        Assert.True(policies.ContainsKey(Venue.Binance));
        Assert.True(policies.ContainsKey(Venue.Coinbase));
        Assert.Equal(Money.USD(5m), policies[Venue.Binance].MinMarketRoutingNotional);
        Assert.Equal(Money.USD(1m), policies[Venue.Coinbase].MinMarketRoutingNotional);
    }

    [Fact]
    public void BinanceCrypto_AllowsImmediateMarketTimeInForce()
    {
        var policy = VenueRoutingPolicyCatalog.BinanceCrypto();

        Assert.NotNull(policy.AllowedMarketTimeInForce);
        Assert.Contains(TimeInForce.IOC, policy.AllowedMarketTimeInForce);
        Assert.Contains(TimeInForce.FOK, policy.AllowedMarketTimeInForce);
        Assert.True(policy.AllowBestVenueMarketRouting);
        Assert.True(policy.AllowMarketSweepRouting);
    }

    [Fact]
    public void InteractiveBrokersListedEquity_RequiresWholeShareRouting()
    {
        var policy = VenueRoutingPolicyCatalog.InteractiveBrokersListedEquity();

        Assert.Equal(new Qty(1m), policy.MinMarketRoutingQuantity);
        Assert.Equal(Money.USD(1m), policy.MinMarketRoutingNotional);
        Assert.NotNull(policy.AllowedMarketTimeInForce);
        Assert.DoesNotContain(TimeInForce.GTC, policy.AllowedMarketTimeInForce);
    }

    [Fact]
    public void KnownVenues_MergesCryptoAndListedEquityPolicies()
    {
        var policies = VenueRoutingPolicyCatalog.KnownVenues();

        Assert.True(policies.ContainsKey(Venue.Binance));
        Assert.True(policies.ContainsKey(Venue.Coinbase));
        Assert.True(policies.ContainsKey(Venue.NASDAQ));
        Assert.True(policies.ContainsKey(Venue.NYSE));
    }

    [Fact]
    public void BundledDatasetIds_IncludesRoutingPolicyFeeds()
    {
        Assert.Contains("routing-crypto-spot", VenueRoutingPolicyCatalog.BundledDatasetIds);
        Assert.Contains("routing-us-listed-equities", VenueRoutingPolicyCatalog.BundledDatasetIds);
    }

    [Fact]
    public void FromBundledPolicyFeed_LoadsCryptoSpotDataset()
    {
        var policies = VenueRoutingPolicyCatalog.FromBundledPolicyFeed("routing-crypto-spot");

        Assert.Equal(Money.USD(5m), policies[Venue.Binance].MinMarketRoutingNotional);
        Assert.Equal(Money.USD(1m), policies[Venue.Coinbase].MinMarketRoutingNotional);
        Assert.Contains(TimeInForce.IOC, policies[Venue.Binance].AllowedMarketTimeInForce!);
    }

    [Fact]
    public void BundledPolicyFeedDataset_ReturnsEmbeddedCsv()
    {
        var feed = VenueRoutingPolicyCatalog.BundledPolicyFeedDataset("routing-us-listed-equities");

        Assert.Contains("NASDAQ", feed);
        Assert.Contains("NYSE", feed);
        Assert.Contains("min_notional", feed);
    }

    [Fact]
    public void FromPolicyFeed_LoadsProviderPolicies()
    {
        const string feed = """
            # venue,allow_best,allow_sweep,allowed_tif,min_qty,min_notional,currency,max_sweep_qty
            venue,allow_best,allow_sweep,allowed_tif,min_qty,min_notional,currency,max_sweep_qty
            Binance,true,false,IOC;FOK,0.01,25,USD,3
            ARCA,false,true,DAY|IOC,1,1,USD,500
            """;

        var policies = VenueRoutingPolicyCatalog.FromPolicyFeed(feed);

        Assert.True(policies[Venue.Binance].AllowBestVenueMarketRouting);
        Assert.False(policies[Venue.Binance].AllowMarketSweepRouting);
        Assert.Equal(new Qty(0.01m), policies[Venue.Binance].MinMarketRoutingQuantity);
        Assert.Equal(Money.USD(25m), policies[Venue.Binance].MinMarketRoutingNotional);
        Assert.Equal(new Qty(3m), policies[Venue.Binance].MaxMarketSweepQuantity);
        Assert.Contains(TimeInForce.IOC, policies[Venue.Binance].AllowedMarketTimeInForce!);
        Assert.Contains(TimeInForce.FOK, policies[Venue.Binance].AllowedMarketTimeInForce!);

        Assert.False(policies["ARCA"].AllowBestVenueMarketRouting);
        Assert.True(policies["ARCA"].AllowMarketSweepRouting);
        Assert.Contains(TimeInForce.Day, policies["ARCA"].AllowedMarketTimeInForce!);
    }

    [Fact]
    public void FromPolicyFeed_OverlaysBasePolicies()
    {
        const string feed = "venue,allow_best,allow_sweep,allowed_tif,min_qty,min_notional,currency,max_sweep_qty\nCoinbase,,false,,,,,";

        var policies = VenueRoutingPolicyCatalog.FromPolicyFeed(
            feed,
            VenueRoutingPolicyCatalog.CryptoSpot());

        Assert.Equal(Money.USD(1m), policies[Venue.Coinbase].MinMarketRoutingNotional);
        Assert.False(policies[Venue.Coinbase].AllowMarketSweepRouting);
        Assert.True(policies[Venue.Coinbase].AllowBestVenueMarketRouting);
    }

    [Fact]
    public void FromPolicyFeedFile_LoadsProviderPolicies()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-routing-policy.csv");
        File.WriteAllText(
            path,
            "venue,allow_best,allow_sweep,allowed_tif,min_qty,min_notional,currency,max_sweep_qty\nNASDAQ,true,true,DAY|IOC,1,10,USD,1000\n");

        try
        {
            var policies = VenueRoutingPolicyCatalog.FromPolicyFeedFile(path);

            Assert.Equal(new Qty(1m), policies[Venue.NASDAQ].MinMarketRoutingQuantity);
            Assert.Equal(Money.USD(10m), policies[Venue.NASDAQ].MinMarketRoutingNotional);
            Assert.Equal(new Qty(1000m), policies[Venue.NASDAQ].MaxMarketSweepQuantity);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void FromPolicyFeed_RejectsInvalidTimeInForce()
    {
        const string feed = "venue,allow_best,allow_sweep,allowed_tif\nBinance,true,true,NEVER";

        Assert.Throws<FormatException>(() => VenueRoutingPolicyCatalog.FromPolicyFeed(feed));
    }

    [Fact]
    public void FromBundledPolicyFeed_RejectsUnknownDataset()
    {
        Assert.Throws<ArgumentException>(() => VenueRoutingPolicyCatalog.FromBundledPolicyFeed("missing"));
    }
}
