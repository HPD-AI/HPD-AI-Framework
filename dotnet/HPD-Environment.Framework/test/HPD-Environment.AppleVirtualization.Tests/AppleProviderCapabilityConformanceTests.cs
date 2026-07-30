using HPD.Environment.Contracts;
using HPD.Environment.ProviderConformance;

namespace HPD.Environment.AppleVirtualization.Tests;

public sealed class AppleProviderCapabilityConformanceTests
    : ProviderCapabilityConformanceTests
{
    protected override ProviderCapabilityConformanceFixture
        CreateCapabilityFixture() =>
        new(
            new AppleVirtualizationCapabilityReporter(),
            AppleVirtualizationProviderDescriptor.ProviderId,
            new ProviderCapabilityQuery(
                HostPlatform:
                    new PlatformSpec("macos", "arm64")),
            new Dictionary<CapabilityId, CapabilityState>
            {
                [StandardEnvironmentCapabilities.ProcessIsolation] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.ContainerIsolation] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.SharedHostKernel] =
                    CapabilityState.Unsupported,
                [StandardEnvironmentCapabilities.HardwareVirtualization] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.GuestAgentBoundary] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.MediatedEngineAuthority] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.HostLocalEndpointPublication] =
                    CapabilityState.Supported,
            });
}
