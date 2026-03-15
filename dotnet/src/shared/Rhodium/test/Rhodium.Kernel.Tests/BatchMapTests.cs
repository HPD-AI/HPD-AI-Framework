using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Kernel.Tests;

public class BatchMapTests
{
    [Fact]
    public void BatchMap_AddInstrument_ReturnsStartIndex()
    {
        var map = new BatchMap();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);

        var start = map.AddInstrument(inst, variantCount: 5);

        Assert.Equal(0, start);
        Assert.Equal(5, map.TotalSize);
    }

    [Fact]
    public void BatchMap_AddInstrument_IncrementsVersion()
    {
        var map = new BatchMap();
        var initialVersion = map.Version;

        map.AddInstrument(new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE));

        Assert.Equal(initialVersion + 1, map.Version);
    }

    [Fact]
    public void BatchMap_AddInstrument_ThrowsOnDuplicate()
    {
        var map = new BatchMap();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);

        map.AddInstrument(inst);

        Assert.Throws<InvalidOperationException>(() => map.AddInstrument(inst));
    }

    [Fact]
    public void BatchMap_GetInstrumentRange_ReturnsCorrectRange()
    {
        var map = new BatchMap();
        var spy = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var qqq = new Instrument(new Asset("QQQ", AssetClass.Equity), Venue.NASDAQ);

        map.AddInstrument(spy, variantCount: 3);
        map.AddInstrument(qqq, variantCount: 2);

        var (start1, length1) = map.GetInstrumentRange(spy);
        var (start2, length2) = map.GetInstrumentRange(qqq);

        Assert.Equal(0, start1);
        Assert.Equal(3, length1);
        Assert.Equal(3, start2);
        Assert.Equal(2, length2);
    }

    [Fact]
    public void BatchMap_GetContext_ReturnsCorrectInstrumentAndVariant()
    {
        var map = new BatchMap();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);

        map.AddInstrument(inst, variantCount: 3);

        var (inst0, var0) = map.GetContext(0);
        var (inst1, var1) = map.GetContext(1);
        var (inst2, var2) = map.GetContext(2);

        Assert.Equal(inst, inst0);
        Assert.Equal(0, var0);
        Assert.Equal(inst, inst1);
        Assert.Equal(1, var1);
        Assert.Equal(inst, inst2);
        Assert.Equal(2, var2);
    }

    [Fact]
    public void BatchMap_GetContext_ThrowsOnOutOfRange()
    {
        var map = new BatchMap();

        Assert.Throws<IndexOutOfRangeException>(() => map.GetContext(0));
        Assert.Throws<IndexOutOfRangeException>(() => map.GetContext(-1));
    }

    [Fact]
    public void BatchMap_SafeGetContext_ReturnsUnknownOnOutOfRange()
    {
        var map = new BatchMap();

        var (inst, variant) = map.SafeGetContext(999);

        Assert.Equal(Instrument.Unknown, inst);
        Assert.Equal(0, variant);
    }

    [Fact]
    public void BatchMap_CurrentBasis_UpdatesOnAdd()
    {
        var map = new BatchMap();

        map.AddInstrument(new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE), 5);

        var basis = map.CurrentBasis;
        Assert.Equal(1, basis.AssetDimension);
        Assert.Equal(5, basis.VariantDimension);
    }
}
