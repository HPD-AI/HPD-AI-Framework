using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests;

public class AgentRunConfigTests
{
    [Fact]
    public void PermissionMode_DefaultsToAsk()
    {
        var runConfig = new AgentRunConfig();

        Assert.Equal(AgentPermissionMode.Ask, runConfig.PermissionMode);
    }

    [Fact]
    public void PermissionMode_RoundTripsThroughSourceGeneratedJson()
    {
        var runConfig = new AgentRunConfig { PermissionMode = AgentPermissionMode.FullAccess };

        var json = JsonSerializer.Serialize(runConfig, HPDJsonContext.Default.AgentRunConfig);
        var deserialized = JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentRunConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(AgentPermissionMode.FullAccess, deserialized.PermissionMode);
    }

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
