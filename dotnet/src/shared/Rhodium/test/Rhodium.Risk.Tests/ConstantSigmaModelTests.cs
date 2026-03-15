using Rhodium.Primitives;
using Rhodium.Risk;

namespace Rhodium.Risk.Tests;

public class ConstantSigmaModelTests
{
    [Fact]
    public void ConstantSigmaModel_ReturnsConstantValue()
    {
        var model = new ConstantSigmaModel(0.25);
        var inst1 = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var inst2 = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

        Assert.Equal(0.25, model.Estimate(inst1));
        Assert.Equal(0.25, model.Estimate(inst2));
    }

    [Fact]
    public void ConstantSigmaModel_ThrowsOnNegativeVolatility()
    {
        Assert.Throws<ArgumentException>(() => new ConstantSigmaModel(-0.1));
    }

    [Fact]
    public void ConstantSigmaModel_ThrowsOnZeroVolatility()
    {
        Assert.Throws<ArgumentException>(() => new ConstantSigmaModel(0));
    }

    [Fact]
    public void ConstantSigmaModel_LowVol_Returns10Percent()
    {
        var model = ConstantSigmaModel.LowVol();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);

        Assert.Equal(0.10, model.Estimate(inst));
    }

    [Fact]
    public void ConstantSigmaModel_MediumVol_Returns20Percent()
    {
        var model = ConstantSigmaModel.MediumVol();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);

        Assert.Equal(0.20, model.Estimate(inst));
    }

    [Fact]
    public void ConstantSigmaModel_HighVol_Returns40Percent()
    {
        var model = ConstantSigmaModel.HighVol();
        var inst = new Instrument(new Asset("TSLA", AssetClass.Equity), Venue.NASDAQ);

        Assert.Equal(0.40, model.Estimate(inst));
    }
}
