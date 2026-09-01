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
    public async Task FamilyResolutionDisposalDrainsConcurrentAdmittedFactoriesAndReleasesTheirOwners()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var owners = new List<IAsyncDisposable>();
        var disposed = 0;
        var clients = new AgentClientSet();
        clients.SetLeases(owners);
        clients.SetFamilyResolver(async (_, _) =>
        {
            Interlocked.Increment(ref entered);
            await release.Task;
            lock (owners) owners.Add(new CallbackOwner(() => Interlocked.Increment(ref disposed)));
            return new object();
        });
        var first = clients.ResolveFamilyAsync<object>(ProviderClientFamily.Realtime).AsTask();
        var second = clients.ResolveFamilyAsync<object>(ProviderClientFamily.HostedFiles).AsTask();
        await WaitUntilAsync(() => Volatile.Read(ref entered) == 2);

        var disposal = clients.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        release.TrySetResult();
        await Task.WhenAll(first, second);
        await disposal;

        Assert.Equal(2, disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await clients.ResolveFamilyAsync<object>(ProviderClientFamily.Embeddings));
    }

    [Theory]
    [InlineData(ProviderClientFamily.VoiceActivityDetection)]
    [InlineData(ProviderClientFamily.EndOfTurnDetection)]
    public async Task ComponentFamiliesHonorWholeParentInheritanceAtConsumption(
        ProviderClientFamily family)
    {
        var identity = ProviderClientExecutionIdentity.CreateSafe(
            "provider", "backend", family, null, "adapter", "usage");
        var parentComponent = new object();
        await using var parent = new AgentClientSet();
        Assert.Same(parentComponent, await parent.GetProviderComponentAsync(
            family, identity,
            _ => ValueTask.FromResult(new ProviderClientConstruction<object>
            {
                Client = parentComponent,
                Owner = new CallbackOwner(() => { })
            })));
        await using var child = new AgentClientSet();
        child.SetComponentInheritance(new SubAgentClientInheritanceSource(
            parent,
            family == ProviderClientFamily.VoiceActivityDetection
                ? new AgentClientInheritance { VoiceActivityDetection = ClientFamilyInheritanceMode.InheritResolved }
                : new AgentClientInheritance { EndOfTurnDetection = ClientFamilyInheritanceMode.InheritResolved }));
        var ownCalls = 0;

        var inherited = await child.GetProviderComponentAsync<object>(
            family, identity,
            _ =>
            {
                Interlocked.Increment(ref ownCalls);
                throw new InvalidOperationException();
            });

        Assert.Same(parentComponent, inherited);
        Assert.Equal(0, ownCalls);
    }

    [Theory]
    [InlineData(ProviderClientFamily.VoiceActivityDetection)]
    [InlineData(ProviderClientFamily.EndOfTurnDetection)]
    public async Task ComponentFamiliesUseClosedMissingPlanFallbackAndFailMissingParent(
        ProviderClientFamily family)
    {
        var identity = ProviderClientExecutionIdentity.CreateSafe(
            "provider", "backend", family, null, "adapter", "usage");
        await using var child = new AgentClientSet();
        child.SetComponentInheritance(new SubAgentClientInheritanceSource(
            null,
            family == ProviderClientFamily.VoiceActivityDetection
                ? new AgentClientInheritance { VoiceActivityDetection = ClientFamilyInheritanceMode.FallbackToParent }
                : new AgentClientInheritance { EndOfTurnDetection = ClientFamilyInheritanceMode.FallbackToParent }));

        var exception = await Assert.ThrowsAsync<AgentRunConfigurationException>(async () =>
            await child.GetProviderComponentAsync<object>(family, identity,
                _ => throw new AgentRunConfigurationException(
                    "ProviderDefaultRequired", $"clients.{family}", "missing")));

        Assert.Equal("subagent_parent_client_unavailable", exception.Code);
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

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class CallbackOwner(Action dispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            dispose();
            return ValueTask.CompletedTask;
        }
    }

}
