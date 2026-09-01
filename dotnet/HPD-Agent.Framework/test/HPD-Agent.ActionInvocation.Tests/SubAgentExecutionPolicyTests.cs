using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Serialization;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class SubAgentExecutionPolicyTests
{
    [Fact]
    public void DefaultPolicyCompilesDeterministicallyAndRoundTripsGeneratedJson()
    {
        var first = SubAgentRunConfig.Inherit().CompilePolicy();
        var second = SubAgentRunConfig.Inherit().CompilePolicy();

        Assert.Equal(first, second);
        var json = JsonSerializer.Serialize(first, AgentEventJsonContext.Default.SubAgentExecutionPolicy);
        var roundTrip = JsonSerializer.Deserialize(json, AgentEventJsonContext.Default.SubAgentExecutionPolicy);
        Assert.Equal(first, roundTrip);
    }

    [Fact]
    public void EverySemanticFamilyChangeChangesFingerprint()
    {
        var baseline = SubAgentRunConfig.Inherit().CompilePolicy();
        var changed = SubAgentRunConfig.Inherit().WithClients(new AgentClientInheritance
        {
            Chat = ClientFamilyInheritanceMode.UseOwn
        }).CompilePolicy();

        Assert.NotEqual(baseline.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public void DisallowedTargetedFamilyOverrideFailsBeforePolicyCreation()
    {
        var declaration = SubAgentRunConfig.Inherit();
        var runOverride = new SubAgentRunPolicyOverride
        {
            CapabilityId = CapabilityId.Create("test:worker"),
            Clients = new AgentClientInheritancePatch { Chat = ClientFamilyInheritanceMode.UseOwn }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => declaration.Compile(runOverride));

        Assert.Equal("subagent_client_inheritance_not_permitted", exception.Message);
    }

    [Fact]
    public void UnsupportedVersionAndFingerprintMismatchFailValidation()
    {
        var policy = SubAgentRunConfig.Inherit().CompilePolicy();

        Assert.Equal("subagent_execution_policy_invalid",
            Assert.Throws<InvalidOperationException>(() => (policy with
            {
                ContractVersion = policy.ContractVersion + 1
            }).Validate()).Message);
        Assert.Equal("subagent_execution_policy_mismatch",
            Assert.Throws<InvalidOperationException>(() => (policy with
            {
                Fingerprint = new string('0', policy.Fingerprint.Length)
            }).Validate()).Message);
    }
}
