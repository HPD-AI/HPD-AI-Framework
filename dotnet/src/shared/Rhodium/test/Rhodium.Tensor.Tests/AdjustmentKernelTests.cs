using Rhodium.Tensor;

namespace Rhodium.Tensor.Tests;

public class AdjustmentKernelTests
{
    [Fact]
    public void AdjustmentKernel_AppliesPriceScaleCorrectly()
    {
        using var store = new PagedTensorStore();

        // Grow to create some indices
        for (int i = 0; i < 10; i++)
            store.Grow();

        // Set raw prices
        store.GetScalar(Field.OpenRaw, 0) = new PriceF64(100.0);
        store.GetScalar(Field.HighRaw, 0) = new PriceF64(110.0);
        store.GetScalar(Field.LowRaw, 0) = new PriceF64(90.0);
        store.GetScalar(Field.CloseRaw, 0) = new PriceF64(105.0);

        // Set price scale to 0.5 (2-for-1 split)
        store.GetScalar(Field.PriceScale, 0) = new FactorF64(0.5);
        store.GetScalar(Field.VolumeScale, 0) = new FactorF64(2.0);

        // Execute adjustment kernel
        var kernel = new AdjustmentKernel();
        store.ForEachPage(kernel);

        // Verify adjusted prices
        Assert.Equal(50.0, store.GetScalar(Field.Open, 0).Value);
        Assert.Equal(55.0, store.GetScalar(Field.High, 0).Value);
        Assert.Equal(45.0, store.GetScalar(Field.Low, 0).Value);
        Assert.Equal(52.5, store.GetScalar(Field.Close, 0).Value);
    }

    [Fact]
    public void AdjustmentKernel_AppliesVolumeScaleCorrectly()
    {
        using var store = new PagedTensorStore();

        for (int i = 0; i < 10; i++)
            store.Grow();

        // Set raw volume
        store.GetScalar(Field.VolumeRaw, 0) = new SizeF64(1000.0);

        // Set volume scale to 2.0 (inverse of split factor)
        store.GetScalar(Field.PriceScale, 0) = new FactorF64(1.0);
        store.GetScalar(Field.VolumeScale, 0) = new FactorF64(2.0);

        // Execute adjustment kernel
        var kernel = new AdjustmentKernel();
        store.ForEachPage(kernel);

        // Verify adjusted volume
        Assert.Equal(2000.0, store.GetScalar(Field.Volume, 0).Value);
    }

    [Fact]
    public void AdjustmentKernel_NoAdjustmentWhenScalesAreOne()
    {
        using var store = new PagedTensorStore();

        for (int i = 0; i < 10; i++)
            store.Grow();

        // Set raw values
        store.GetScalar(Field.OpenRaw, 0) = new PriceF64(100.0);
        store.GetScalar(Field.CloseRaw, 0) = new PriceF64(105.0);
        store.GetScalar(Field.VolumeRaw, 0) = new SizeF64(1000.0);

        // Set scales to 1.0 (no adjustment)
        store.GetScalar(Field.PriceScale, 0) = new FactorF64(1.0);
        store.GetScalar(Field.VolumeScale, 0) = new FactorF64(1.0);

        // Execute adjustment kernel
        var kernel = new AdjustmentKernel();
        store.ForEachPage(kernel);

        // Verify no change
        Assert.Equal(100.0, store.GetScalar(Field.Open, 0).Value);
        Assert.Equal(105.0, store.GetScalar(Field.Close, 0).Value);
        Assert.Equal(1000.0, store.GetScalar(Field.Volume, 0).Value);
    }

    [Fact]
    public void AdjustmentKernel_TwoForOneSplitScenario()
    {
        using var store = new PagedTensorStore();

        for (int i = 0; i < 10; i++)
            store.Grow();

        // Pre-split: $100 price, 1000 shares
        store.GetScalar(Field.CloseRaw, 0) = new PriceF64(100.0);
        store.GetScalar(Field.VolumeRaw, 0) = new SizeF64(1000.0);

        // 2-for-1 split: SplitFactor = 0.5
        // PriceScale = 0.5, VolumeScale = 2.0
        store.GetScalar(Field.PriceScale, 0) = new FactorF64(0.5);
        store.GetScalar(Field.VolumeScale, 0) = new FactorF64(2.0);

        var kernel = new AdjustmentKernel();
        store.ForEachPage(kernel);

        // Post-split: $50 price, 2000 shares
        Assert.Equal(50.0, store.GetScalar(Field.Close, 0).Value);
        Assert.Equal(2000.0, store.GetScalar(Field.Volume, 0).Value);
    }

    [Fact]
    public void AdjustmentKernel_DividendAdjustmentScenario()
    {
        using var store = new PagedTensorStore();

        for (int i = 0; i < 10; i++)
            store.Grow();

        // Pre-dividend: $100 price
        store.GetScalar(Field.CloseRaw, 0) = new PriceF64(100.0);

        // 10% dividend: DividendScale = 0.9
        // No split: SplitFactor = 1.0
        // PriceScale = 1.0 * 0.9 = 0.9
        store.GetScalar(Field.PriceScale, 0) = new FactorF64(0.9);
        store.GetScalar(Field.VolumeScale, 0) = new FactorF64(1.0);

        var kernel = new AdjustmentKernel();
        store.ForEachPage(kernel);

        // Post-dividend adjusted: $90
        Assert.Equal(90.0, store.GetScalar(Field.Close, 0).Value);
    }

    [Fact]
    public void AdjustmentKernel_CombinedSplitAndDividend()
    {
        using var store = new PagedTensorStore();

        for (int i = 0; i < 10; i++)
            store.Grow();

        // Raw: $100 price, 1000 volume
        store.GetScalar(Field.CloseRaw, 0) = new PriceF64(100.0);
        store.GetScalar(Field.VolumeRaw, 0) = new SizeF64(1000.0);

        // 2-for-1 split (0.5) + 10% dividend (0.9)
        // PriceScale = 0.5 * 0.9 = 0.45
        // VolumeScale = 1 / 0.5 = 2.0
        store.GetScalar(Field.PriceScale, 0) = new FactorF64(0.45);
        store.GetScalar(Field.VolumeScale, 0) = new FactorF64(2.0);

        var kernel = new AdjustmentKernel();
        store.ForEachPage(kernel);

        // Adjusted: $45 price, 2000 volume
        Assert.Equal(45.0, store.GetScalar(Field.Close, 0).Value);
        Assert.Equal(2000.0, store.GetScalar(Field.Volume, 0).Value);
    }

    [Fact]
    public void AdjustmentKernel_IdempotentRepeatedExecution()
    {
        using var store = new PagedTensorStore();

        for (int i = 0; i < 10; i++)
            store.Grow();

        // Set raw values
        store.GetScalar(Field.CloseRaw, 0) = new PriceF64(100.0);
        store.GetScalar(Field.PriceScale, 0) = new FactorF64(0.5);
        store.GetScalar(Field.VolumeScale, 0) = new FactorF64(1.0);

        var kernel = new AdjustmentKernel();

        // Execute multiple times
        store.ForEachPage(kernel);
        store.ForEachPage(kernel);
        store.ForEachPage(kernel);

        // Should still be 50.0 (idempotent)
        Assert.Equal(50.0, store.GetScalar(Field.Close, 0).Value);
    }

    [Fact]
    public void AdjustmentKernel_WorksAcrossMultiplePages()
    {
        using var store = new PagedTensorStore();

        // Grow to span 2+ pages (page size is 1024)
        for (int i = 0; i < 2500; i++)
            store.Grow();

        // Set different raw prices across pages
        store.GetScalar(Field.CloseRaw, 0) = new PriceF64(100.0);
        store.GetScalar(Field.CloseRaw, 1500) = new PriceF64(200.0);
        store.GetScalar(Field.CloseRaw, 2499) = new PriceF64(300.0);

        // Set uniform scale
        store.Broadcast(Field.PriceScale, new FactorF64(0.5), 0, 2500);
        store.Broadcast(Field.VolumeScale, new FactorF64(1.0), 0, 2500);

        var kernel = new AdjustmentKernel();
        store.ForEachPage(kernel);

        // Verify all pages processed
        Assert.Equal(50.0, store.GetScalar(Field.Close, 0).Value);
        Assert.Equal(100.0, store.GetScalar(Field.Close, 1500).Value);
        Assert.Equal(150.0, store.GetScalar(Field.Close, 2499).Value);
    }

    [Fact]
    public void AdjustmentKernel_AllOHLCVFieldsAdjusted()
    {
        using var store = new PagedTensorStore();

        for (int i = 0; i < 10; i++)
            store.Grow();

        // Set all raw OHLCV
        store.GetScalar(Field.OpenRaw, 5) = new PriceF64(100.0);
        store.GetScalar(Field.HighRaw, 5) = new PriceF64(110.0);
        store.GetScalar(Field.LowRaw, 5) = new PriceF64(95.0);
        store.GetScalar(Field.CloseRaw, 5) = new PriceF64(105.0);
        store.GetScalar(Field.VolumeRaw, 5) = new SizeF64(5000.0);

        // Apply 3-for-1 split
        store.GetScalar(Field.PriceScale, 5) = new FactorF64(1.0 / 3.0);
        store.GetScalar(Field.VolumeScale, 5) = new FactorF64(3.0);

        var kernel = new AdjustmentKernel();
        store.ForEachPage(kernel);

        // Verify all fields adjusted
        Assert.Equal(100.0 / 3.0, store.GetScalar(Field.Open, 5).Value, precision: 10);
        Assert.Equal(110.0 / 3.0, store.GetScalar(Field.High, 5).Value, precision: 10);
        Assert.Equal(95.0 / 3.0, store.GetScalar(Field.Low, 5).Value, precision: 10);
        Assert.Equal(105.0 / 3.0, store.GetScalar(Field.Close, 5).Value, precision: 10);
        Assert.Equal(15000.0, store.GetScalar(Field.Volume, 5).Value);
    }
}
