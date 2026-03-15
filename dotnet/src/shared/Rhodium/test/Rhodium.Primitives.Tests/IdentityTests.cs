using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class AssetTests
{
    [Fact]
    public void Asset_ShouldStoreSymbolAndClass()
    {
        // Arrange & Act
        var asset = new Asset("AAPL", AssetClass.Equity);

        // Assert
        Assert.Equal("AAPL", asset.Symbol);
        Assert.Equal(AssetClass.Equity, asset.Class);
    }

    [Fact]
    public void Asset_ShouldSupportImplicitConversionFromString()
    {
        // Act
        Asset asset = "MSFT";

        // Assert
        Assert.Equal("MSFT", asset.Symbol);
        Assert.Equal(AssetClass.Equity, asset.Class);
    }

    [Fact]
    public void Asset_ShouldSupportDerivativeUnderlying()
    {
        // Arrange & Act
        var option = new Asset("AAPL250117C00150000", AssetClass.Option, Underlying: "AAPL");

        // Assert
        Assert.Equal("AAPL", option.Underlying);
    }

    [Fact]
    public void Asset_ShouldCompareAlphabetically()
    {
        // Arrange
        var aapl = new Asset("AAPL", AssetClass.Equity);
        var msft = new Asset("MSFT", AssetClass.Equity);

        // Act & Assert
        Assert.True(aapl.CompareTo(msft) < 0);
        Assert.True(msft.CompareTo(aapl) > 0);
    }

    [Fact]
    public void Asset_ToString_ShouldReturnSymbol()
    {
        // Arrange
        var asset = new Asset("TSLA", AssetClass.Equity);

        // Act
        var str = asset.ToString();

        // Assert
        Assert.Equal("TSLA", str);
    }
}

public class VenueTests
{
    [Fact]
    public void Venue_ShouldHavePredefinedVenues()
    {
        // Assert
        Assert.Equal("NYSE", Venue.NYSE.Name);
        Assert.Equal("NASDAQ", Venue.NASDAQ.Name);
        Assert.Equal("CME", Venue.CME.Name);
        Assert.Equal("Binance", Venue.Binance.Name);
        Assert.Equal("Coinbase", Venue.Coinbase.Name);
    }

    [Fact]
    public void Venue_ShouldSupportImplicitConversionFromString()
    {
        // Act
        Venue venue = "CBOE";

        // Assert
        Assert.Equal("CBOE", venue.Name);
    }

    [Fact]
    public void Venue_ToString_ShouldReturnName()
    {
        // Arrange
        Venue venue = "KRAKEN";

        // Act
        var str = venue.ToString();

        // Assert
        Assert.Equal("KRAKEN", str);
    }
}

public class InstrumentTests
{
    [Fact]
    public void Instrument_ShouldCombineAssetAndVenue()
    {
        // Arrange
        var asset = new Asset("BTC", AssetClass.Crypto);
        var venue = Venue.Binance;

        // Act
        var instrument = new Instrument(asset, venue);

        // Assert
        Assert.Equal(asset, instrument.Asset);
        Assert.Equal(venue, instrument.Venue);
    }

    [Fact]
    public void Instrument_ToString_ShouldFormatAssetAtVenue()
    {
        // Arrange
        var instrument = new Instrument(new Asset("ETH", AssetClass.Crypto), Venue.Coinbase);

        // Act
        var str = instrument.ToString();

        // Assert
        Assert.Equal("ETH@Coinbase", str);
    }

    [Fact]
    public void Instrument_ShouldHaveUnknownConstant()
    {
        // Act
        var unknown = Instrument.Unknown;

        // Assert
        Assert.Equal("UNKNOWN", unknown.Asset.Symbol);
        Assert.Equal("UNKNOWN", unknown.Venue.Name);
    }
}

public class AssetClassTests
{
    [Fact]
    public void AssetClass_ShouldHaveCorrectValues()
    {
        // Assert
        Assert.Equal((byte)1, (byte)AssetClass.Equity);
        Assert.Equal((byte)2, (byte)AssetClass.Option);
        Assert.Equal((byte)3, (byte)AssetClass.Future);
        Assert.Equal((byte)4, (byte)AssetClass.Forex);
        Assert.Equal((byte)5, (byte)AssetClass.Crypto);
        Assert.Equal((byte)6, (byte)AssetClass.Bond);
        Assert.Equal((byte)7, (byte)AssetClass.Index);
        Assert.Equal((byte)8, (byte)AssetClass.Commodity);
    }
}
