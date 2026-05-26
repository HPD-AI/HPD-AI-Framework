using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class AllocationTests
{
    [Fact]
    public void ScalarBlack76PriceAndGreeksDoesNotAllocateAfterInputConstruction()
    {
        var input = new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.97);

        _ = Black76.PriceAndGreeks(input);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
            _ = Black76.PriceAndGreeks(input);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void ScalarBachelierPriceAndGreeksDoesNotAllocateAfterInputConstruction()
    {
        var input = new BachelierInput(OptionRight.Put, 100.0, 100.0, 1.0, 20.0, 0.97);

        _ = Bachelier.PriceAndGreeks(input);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
            _ = Bachelier.PriceAndGreeks(input);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
