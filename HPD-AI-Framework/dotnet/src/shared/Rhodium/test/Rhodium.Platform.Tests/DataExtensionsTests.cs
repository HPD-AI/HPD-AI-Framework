using Rhodium.Kernel;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform.Tests;

/// <summary>
/// Tests for DataExtensions scalar data accessors.
/// </summary>
public class DataExtensionsTests
{
    private TradingEngine CreateEngine()
    {
        var engine = new TradingEngine();

        // Add a test instrument
        var instrument = new Instrument(new Asset("TEST", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(instrument, 1);
        engine.Tensors.Grow();

        return engine;
    }

    [Fact]
    public void GetClose_ReturnsClosePrice()
    {
        var engine = CreateEngine();
        var id = new AssetId(0);

        // Set a close price
        engine.Tensors.GetScalar(Field.Close, 0) = new PriceF64(123.45);

        var close = engine.GetClose(id);
        Assert.Equal(123.45, close);
    }

    [Fact]
    public void GetOpen_ReturnsOpenPrice()
    {
        var engine = CreateEngine();
        var id = new AssetId(0);

        engine.Tensors.GetScalar(Field.Open, 0) = new PriceF64(100.50);

        var open = engine.GetOpen(id);
        Assert.Equal(100.50, open);
    }

    [Fact]
    public void GetHigh_ReturnsHighPrice()
    {
        var engine = CreateEngine();
        var id = new AssetId(0);

        engine.Tensors.GetScalar(Field.High, 0) = new PriceF64(125.75);

        var high = engine.GetHigh(id);
        Assert.Equal(125.75, high);
    }

    [Fact]
    public void GetLow_ReturnsLowPrice()
    {
        var engine = CreateEngine();
        var id = new AssetId(0);

        engine.Tensors.GetScalar(Field.Low, 0) = new PriceF64(98.25);

        var low = engine.GetLow(id);
        Assert.Equal(98.25, low);
    }

    [Fact]
    public void GetVolume_ReturnsVolume()
    {
        var engine = CreateEngine();
        var id = new AssetId(0);

        engine.Tensors.GetScalar(Field.Volume, 0) = new SizeF64(1000000.0);

        var volume = engine.GetVolume(id);
        Assert.Equal(1000000.0, volume);
    }

    [Fact]
    public void GetRsi14_ReturnsIndicatorValue()
    {
        var engine = CreateEngine();
        var id = new AssetId(0);

        // Register and set RSI indicator
        engine.EnsureColumn(Fields.RSI_14);
        engine.Tensors.GetScalar(Fields.RSI_14, 0) = new FactorF64(65.5);

        var rsi = engine.GetRsi14(id);
        Assert.Equal(65.5, rsi);
    }

    [Fact]
    public void GetClose_MultipleAssets_ReturnsCorrectValues()
    {
        var engine = CreateEngine();

        // Add more instruments
        var inst2 = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var inst3 = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(inst2, 1);
        engine.BatchMap.AddInstrument(inst3, 1);
        engine.Tensors.Grow();

        var id0 = new AssetId(0);
        var id1 = new AssetId(1);
        var id2 = new AssetId(2);

        engine.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100.0);
        engine.Tensors.GetScalar(Field.Close, 1) = new PriceF64(200.0);
        engine.Tensors.GetScalar(Field.Close, 2) = new PriceF64(300.0);

        Assert.Equal(100.0, engine.GetClose(id0));
        Assert.Equal(200.0, engine.GetClose(id1));
        Assert.Equal(300.0, engine.GetClose(id2));
    }

    [Fact]
    public void GetOHLCV_AllValues_ConsistentForBar()
    {
        var engine = CreateEngine();
        var id = new AssetId(0);

        // Set bar data
        engine.Tensors.GetScalar(Field.Open, 0) = new PriceF64(100.0);
        engine.Tensors.GetScalar(Field.High, 0) = new PriceF64(105.0);
        engine.Tensors.GetScalar(Field.Low, 0) = new PriceF64(99.0);
        engine.Tensors.GetScalar(Field.Close, 0) = new PriceF64(103.0);
        engine.Tensors.GetScalar(Field.Volume, 0) = new SizeF64(50000.0);

        Assert.Equal(100.0, engine.GetOpen(id));
        Assert.Equal(105.0, engine.GetHigh(id));
        Assert.Equal(99.0, engine.GetLow(id));
        Assert.Equal(103.0, engine.GetClose(id));
        Assert.Equal(50000.0, engine.GetVolume(id));
    }

    [Fact]
    public void GetClose_WithVariantId_ReturnsCorrectValue()
    {
        // Create engine without calling CreateEngine() to avoid duplicate instrument
        var engine = new TradingEngine();

        // Add instrument with variants
        var inst = new Instrument(new Asset("TEST", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(inst, 3); // 3 variants
        engine.Tensors.Grow();

        var base1 = new AssetId(0);
        var variant1 = base1.WithVariant(1);
        var variant2 = base1.WithVariant(2);

        engine.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100.0);
        engine.Tensors.GetScalar(Field.Close, 1) = new PriceF64(101.0);
        engine.Tensors.GetScalar(Field.Close, 2) = new PriceF64(102.0);

        Assert.Equal(100.0, engine.GetClose(base1));
        Assert.Equal(101.0, engine.GetClose(variant1));
        Assert.Equal(102.0, engine.GetClose(variant2));
    }

    [Fact]
    public void GetRsi14_RequiresColumnRegistration()
    {
        var engine = CreateEngine();
        var id = new AssetId(0);

        // Register column first
        engine.EnsureColumn(Fields.RSI_14);
        engine.Tensors.GetScalar(Fields.RSI_14, 0) = new FactorF64(50.0);

        var rsi = engine.GetRsi14(id);
        Assert.Equal(50.0, rsi);
    }

    [Fact]
    public void DataExtensions_ZeroValues_ReturnZero()
    {
        var engine = CreateEngine();
        var id = new AssetId(0);

        // Default values should be zero
        Assert.Equal(0.0, engine.GetClose(id));
        Assert.Equal(0.0, engine.GetOpen(id));
        Assert.Equal(0.0, engine.GetHigh(id));
        Assert.Equal(0.0, engine.GetLow(id));
        Assert.Equal(0.0, engine.GetVolume(id));
    }

    [Fact]
    public void DataExtensions_NegativeValues_Supported()
    {
        var engine = CreateEngine();
        var id = new AssetId(0);

        // Some indicators can be negative
        engine.EnsureColumn(Fields.RSI_14);
        engine.Tensors.GetScalar(Fields.RSI_14, 0) = new FactorF64(-5.5);

        var rsi = engine.GetRsi14(id);
        Assert.Equal(-5.5, rsi);
    }

    [Fact]
    public void DataExtensions_LargeValues_Supported()
    {
        var engine = CreateEngine();
        var id = new AssetId(0);

        engine.Tensors.GetScalar(Field.Volume, 0) = new SizeF64(1e9); // 1 billion

        var volume = engine.GetVolume(id);
        Assert.Equal(1e9, volume);
    }
}
