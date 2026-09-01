using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Agent.Providers;

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

    [Fact]
    public async Task FamilyResolutionIsLazyAndCachedAtTheConsumptionBoundary()
    {
        var calls = 0;
        var realtime = new object();
        var clients = new AgentClientSet();
        clients.SetFamilyResolver((family, _) =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal(ProviderClientFamily.Realtime, family);
            return ValueTask.FromResult<object?>(realtime);
        });

        Assert.Equal(0, calls);
        Assert.Same(realtime, await clients.ResolveFamilyAsync<object>(
            ProviderClientFamily.Realtime));
        Assert.Same(realtime, await clients.ResolveFamilyAsync<object>(
            ProviderClientFamily.Realtime));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void SafeExecutionFingerprintUsesOnlySanitizedRuntimeIdentity()
    {
        var first = ProviderClientExecutionIdentity.CreateSafe(
            "openai", "responses", ProviderClientFamily.Chat, "gpt", "adapter", "usage");
        var second = ProviderClientExecutionIdentity.CreateSafe(
            "openai", "responses", ProviderClientFamily.Chat, "gpt", "adapter", "usage");

        Assert.Equal(first.SafeConfigurationFingerprint, second.SafeConfigurationFingerprint);
        Assert.DoesNotContain("openai", first.SafeConfigurationFingerprint, StringComparison.OrdinalIgnoreCase);
    }

}
