using Rhodium.Platform.Examples;
using Rhodium.Primitives;

namespace Rhodium.Platform.Tests;

public sealed class StrategyTestToolHarnessTests
{
    [Fact]
    public void Run_CanExerciseGeneratedStrategyWithoutManualKernelSetup()
    {
        using var result = StrategyTest
            .For<RsiMeanReversion>()
            .WithCloseSeries(100, 99, 98, 97, 96, 95, 94, 93, 92, 91, 90, 89, 88, 87, 86, 85, 84, 83, 82, 81)
            .Run();

        Assert.Equal(0.5m, result.PositionQuantity(new AssetId(0)));
        Assert.Equal(new StrategyId(0), result.Snapshot.StrategyId);
    }
}
