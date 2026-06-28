using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests;

public class AgentRunConfigTests
{
    [Fact]
    public void ChatRunConfig_MergeWith_ShouldLetRunSeedOverrideDefaultSeed()
    {
        var defaults = new ChatOptions
        {
            Seed = 1,
            Temperature = 0.2f
        };

        var runConfig = new ChatRunConfig
        {
            Seed = 42
        };

        var merged = runConfig.MergeWith(defaults);

        Assert.NotNull(merged);
        Assert.Equal(42, merged!.Seed);
        Assert.Equal(0.2f, merged.Temperature);
    }
}
