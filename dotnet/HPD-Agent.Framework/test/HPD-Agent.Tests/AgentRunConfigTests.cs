using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests;

public class AgentRunConfigTests
{
    [Fact]
    public void Security_DefaultsToReviewAndSandbox()
    {
        var runConfig = new AgentRunConfig();

        Assert.Equal(AgentApprovalPolicy.ReviewProtectedActions, runConfig.Security.Approval);
        Assert.Equal(AgentSandboxPolicy.Enforced, runConfig.Security.Sandbox);
        Assert.Equal(AgentSandboxEscapePolicy.Ask, runConfig.Security.SandboxEscape);
    }

    [Fact]
    public void Security_RoundTripsThroughSourceGeneratedJson()
    {
        var runConfig = new AgentRunConfig
        {
            Security = new AgentSecurityProfile
            {
                Approval = AgentApprovalPolicy.AutoApprove,
                Sandbox = AgentSandboxPolicy.Disabled,
                SandboxEscape = AgentSandboxEscapePolicy.Deny
            }
        };

        var json = JsonSerializer.Serialize(runConfig, HPDJsonContext.Default.AgentRunConfig);
        var deserialized = JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentRunConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(runConfig.Security, deserialized.Security);
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
