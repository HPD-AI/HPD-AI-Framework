namespace HPD.Agent.Sandbox.VirtualMachine.Tests;

using FluentAssertions;
using HPD.Agent;
using HPD.Environment.Virtualization.Apple;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationSandboxDxTests
{
    [Fact]
    public async Task Expected_builder_dx_builds_agent_with_apple_virtualization_sandbox()
    {
        Agent agent = await new AgentBuilder()
            .WithSandbox(new AppleVirtualizationProviderModule(FakeAppleVirtualizationOptions()),
                new SandboxSessionRequest { Isolation = ProcessIsolationPolicy.Default })
            .BuildAsync();

        agent.Should().NotBeNull();
    }

    [Fact]
    public void Provider_prepares_guest_host_and_activation()
    {
        var provider = new AppleVirtualizationProviderModule(FakeAppleVirtualizationOptions());
        var plan = provider.PrepareSandbox(new SandboxProvisioningRequest
        {
            Isolation = ProcessIsolationPolicy.Default,
        });

        plan.EnforcementLocation.Should().Be(SandboxEnforcementLocation.Guest);
        plan.Host.PreferredProvider.Should().Be(provider.Descriptor.Id);
        plan.Activation.Should().NotBeNull();
    }

    private static AppleVirtualizationProviderOptions FakeAppleVirtualizationOptions() =>
        new()
        {
            HelperTransportMode = AppleVirtualizationHelperTransportMode.InMemoryFake,
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableInMemoryFakeHelper = true,
            },
        };
}
