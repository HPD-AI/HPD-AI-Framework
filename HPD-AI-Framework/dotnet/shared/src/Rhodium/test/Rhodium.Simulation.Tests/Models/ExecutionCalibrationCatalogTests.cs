using Rhodium.Primitives;
using Rhodium.Simulation;

namespace Rhodium.Simulation.Tests.Models;

public class ExecutionCalibrationCatalogTests
{
    [Fact]
    public void KnownVenues_IncludesCryptoAndListedEquityProfiles()
    {
        var profiles = ExecutionCalibrationCatalog.KnownVenues();

        Assert.Contains(Venue.Binance, profiles.Keys);
        Assert.Contains(Venue.Coinbase, profiles.Keys);
        Assert.Contains(Venue.NYSE, profiles.Keys);
        Assert.Contains(Venue.NASDAQ, profiles.Keys);
    }

    [Fact]
    public void BundledDatasetIds_IncludesCalibrationFeeds()
    {
        Assert.Contains("calibration-crypto-spot", ExecutionCalibrationCatalog.BundledDatasetIds);
        Assert.Contains("calibration-us-listed-equities", ExecutionCalibrationCatalog.BundledDatasetIds);
    }

    [Fact]
    public void FromBundledCalibrationFeed_LoadsCryptoSpotDataset()
    {
        var profiles = ExecutionCalibrationCatalog.FromBundledCalibrationFeed("calibration-crypto-spot");

        Assert.Equal(SlippageModelType.VolumeProportional, profiles[Venue.Binance].Slippage.Model);
        Assert.Equal(0.15m, profiles[Venue.Binance].Slippage.BpsPerLotSize);
        Assert.Equal(10m, profiles[Venue.Binance].Slippage.ReferenceQuantity);
        Assert.Equal(0.20m, profiles[Venue.Binance].PriceImprovement.MakerBps);
    }

    [Fact]
    public void BundledCalibrationFeedDataset_ReturnsEmbeddedCsv()
    {
        var feed = ExecutionCalibrationCatalog.BundledCalibrationFeedDataset("calibration-us-listed-equities");

        Assert.Contains("NASDAQ", feed);
        Assert.Contains("VolatilityAdjusted", feed);
    }

    [Fact]
    public void FromCalibrationFeed_OverlaysBaseProfiles()
    {
        var feed = """
            venue,slippage_model,bps_per_lot_size,reference_quantity,volatility_bps,taker_price_improvement_bps,maker_price_improvement_bps
            Binance,,0.50,,,,0.25
            """;

        var profiles = ExecutionCalibrationCatalog.FromCalibrationFeed(
            feed,
            ExecutionCalibrationCatalog.CryptoSpot());

        Assert.Equal(SlippageModelType.VolumeProportional, profiles[Venue.Binance].Slippage.Model);
        Assert.Equal(0.50m, profiles[Venue.Binance].Slippage.BpsPerLotSize);
        Assert.Equal(10m, profiles[Venue.Binance].Slippage.ReferenceQuantity);
        Assert.Equal(0.25m, profiles[Venue.Binance].PriceImprovement.MakerBps);
    }

    [Fact]
    public void FromCalibrationFeedFile_LoadsProviderProfiles()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                path,
                "venue,slippage_model,bps_per_lot_size,reference_quantity,volatility_bps,taker_price_improvement_bps,maker_price_improvement_bps\nNYSE,VolatilityAdjusted,0.02,500,0.30,0.01,0.05");

            var profiles = ExecutionCalibrationCatalog.FromCalibrationFeedFile(path);

            Assert.Equal(SlippageModelType.VolatilityAdjusted, profiles[Venue.NYSE].Slippage.Model);
            Assert.Equal(0.30m, profiles[Venue.NYSE].Slippage.VolatilityBps);
            Assert.Equal(0.01m, profiles[Venue.NYSE].PriceImprovement.TakerBps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromCalibrationFeed_RejectsInvalidSlippageModel()
    {
        var feed = "venue,slippage_model\nNYSE,Magic";

        Assert.Throws<FormatException>(() => ExecutionCalibrationCatalog.FromCalibrationFeed(feed));
    }

    [Fact]
    public void FromBundledCalibrationFeed_RejectsUnknownDataset()
    {
        Assert.Throws<ArgumentException>(() => ExecutionCalibrationCatalog.FromBundledCalibrationFeed("missing"));
    }

    [Fact]
    public void ProfileValues_AreUsableBySimulationConfig()
    {
        var profile = ExecutionCalibrationCatalog
            .FromBundledCalibrationFeed("calibration-us-listed-equities")[Venue.NASDAQ];

        var config = SimulationConfig.Queue() with
        {
            Slippage = profile.Slippage,
            PriceImprovement = profile.PriceImprovement
        };

        var improved = config.PriceImprovement.Apply(new Price(100m, Currency.USD), Side.Buy, isMaker: false);
        var slipped = config.Slippage.Apply(improved, new Qty(1_000m), Side.Buy);

        Assert.Equal(new Price(99.9995m, Currency.USD), improved);
        Assert.Equal(new Price(100.0049999725m, Currency.USD), slipped);
    }
}
