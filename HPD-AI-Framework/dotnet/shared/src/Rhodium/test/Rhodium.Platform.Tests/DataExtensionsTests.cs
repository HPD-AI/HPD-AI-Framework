using Rhodium.Kernel;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform.Tests;

public class DataExtensionsTests
{
    [Fact]
    public void MarketDataExtensions_ReadScalarFields()
    {
        using var runtime = CreateRuntime(1);
        runtime.Tensors.GetScalar(Field.Open, 0) = new PriceF64(100);
        runtime.Tensors.GetScalar(Field.High, 0) = new PriceF64(105);
        runtime.Tensors.GetScalar(Field.Low, 0) = new PriceF64(99);
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(103);
        runtime.Tensors.GetScalar(Field.Volume, 0) = new SizeF64(50_000);

        var market = runtime.CreateMarketKernel();
        var id = new AssetId(0);

        Assert.Equal(100, market.GetOpen(id));
        Assert.Equal(105, market.GetHigh(id));
        Assert.Equal(99, market.GetLow(id));
        Assert.Equal(103, market.GetClose(id));
        Assert.Equal(50_000, market.GetVolume(id));
    }

    [Fact]
    public void MarketDataExtensions_ReadIndicatorField()
    {
        using var runtime = CreateRuntime(1);
        runtime.Tensors.GetScalar(Fields.RSI_14, 0) = new FactorF64(65.5);

        var market = runtime.CreateMarketKernel();

        Assert.Equal(65.5, market.GetRsi14(new AssetId(0)));
    }

    [Fact]
    public void MarketDataExtensions_SupportVariants()
    {
        using var runtime = new RhodiumRuntime();
        var inst = new Instrument(new Asset("TEST", AssetClass.Equity), Venue.NASDAQ);
        runtime.BatchMap.AddInstrument(inst, 3);
        runtime.Tensors.Grow();
        runtime.Tensors.Grow();
        runtime.Tensors.Grow();

        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);
        runtime.Tensors.GetScalar(Field.Close, 1) = new PriceF64(101);
        runtime.Tensors.GetScalar(Field.Close, 2) = new PriceF64(102);

        var market = runtime.CreateMarketKernel();
        var baseId = new AssetId(0);

        Assert.Equal(100, market.GetClose(baseId));
        Assert.Equal(101, market.GetClose(baseId.WithVariant(1)));
        Assert.Equal(102, market.GetClose(baseId.WithVariant(2)));
    }

    private static RhodiumRuntime CreateRuntime(int count)
    {
        var runtime = new RhodiumRuntime();
        for (var i = 0; i < count; i++)
        {
            runtime.BatchMap.AddInstrument(new Instrument(new Asset($"TEST{i}", AssetClass.Equity), Venue.NASDAQ));
            runtime.Tensors.Grow();
        }

        return runtime;
    }
}
