namespace HPD.Agent.Sandbox.VirtualMachine.Tests;

using FluentAssertions;
using HPD.Environment.Virtualization.Apple;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using Xunit;

public sealed class AppleVirtualizationSandboxRegistrationTests
{
    [Fact]
    public async Task Apple_virtualization_sandbox_registers_execution_module()
    {
        var registry = new EnvironmentProviderRegistry();

        registry.RegisterAppleVirtualizationSandbox(new AppleVirtualizationProviderOptions
        {
            HelperTransportMode = AppleVirtualizationHelperTransportMode.InMemoryFake,
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableInMemoryFakeHelper = true,
            },
        });

        IReadOnlyList<ProviderDescriptor> providers = await registry.ListAsync();
        ProviderCapabilityReport report = await registry.GetCapabilitiesAsync(AppleVirtualizationProviderDescriptor.ProviderId);

        providers.Should().ContainSingle(provider => provider.Id == AppleVirtualizationProviderDescriptor.ProviderId);
        report.ProviderId.Should().Be(AppleVirtualizationProviderDescriptor.ProviderId);
        registry.ProcessProviders.Should().ContainSingle();
        registry.RuntimeHostProviders.Should().ContainSingle();
        registry.ExecutionUnitProviders.Should().ContainSingle();
    }
}
