using Rhodium.Tensor;

namespace Rhodium.Tensor.Tests;

public class AotRegistryTests
{
    [Fact]
    public void AotRegistry_RegisterDoesNotThrow()
    {
        // The Register method should complete without throwing
        // This verifies all field and kernel types are valid
        var exception = Record.Exception(() => AotRegistry.Register());

        Assert.Null(exception);
    }

    [Fact]
    public void AotRegistry_CanBeCalledMultipleTimes()
    {
        // Registration should be idempotent
        AotRegistry.Register();
        AotRegistry.Register();
        AotRegistry.Register();

        // No assertion needed - if we get here without throwing, it passed
        Assert.True(true);
    }

    [Fact]
    public void AotRegistry_AllStandardFieldsAccessible()
    {
        // Verify all standard fields referenced in Register() are accessible
        Assert.NotNull(Field.OpenRaw.Name);
        Assert.NotNull(Field.HighRaw.Name);
        Assert.NotNull(Field.LowRaw.Name);
        Assert.NotNull(Field.CloseRaw.Name);
        Assert.NotNull(Field.VolumeRaw.Name);

        Assert.NotNull(Field.SplitFactor.Name);
        Assert.NotNull(Field.DividendScale.Name);
        Assert.NotNull(Field.PriceScale.Name);
        Assert.NotNull(Field.VolumeScale.Name);

        Assert.NotNull(Field.Open.Name);
        Assert.NotNull(Field.High.Name);
        Assert.NotNull(Field.Low.Name);
        Assert.NotNull(Field.Close.Name);
        Assert.NotNull(Field.Volume.Name);
    }
}
