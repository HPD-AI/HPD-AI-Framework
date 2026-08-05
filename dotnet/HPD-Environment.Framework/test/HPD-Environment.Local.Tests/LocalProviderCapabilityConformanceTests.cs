using HPD.Environment.Contracts;
using HPD.Environment.ProviderConformance;

namespace HPD.Environment.Local.Tests;

public sealed class LocalProviderCapabilityConformanceTests
    : ProviderCapabilityConformanceTests
{
    protected override ProviderCapabilityConformanceFixture
        CreateCapabilityFixture() =>
        new(
            new LocalEnvironmentCapabilityReporter(
                new LocalEnvironmentProviderOptions
                {
                    EngineSocketPath = "/test/docker.sock",
                }),
            LocalEnvironmentProviderDescriptor.ProviderId,
            new ProviderCapabilityQuery(
                HostPlatform:
                    LocalEnvironmentProviderDescriptor
                        .CurrentPlatform()),
            new Dictionary<CapabilityId, CapabilityState>
            {
                [StandardEnvironmentCapabilities.ProcessIsolation] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.ContainerIsolation] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.SharedHostKernel] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.HardwareVirtualization] =
                    CapabilityState.Unsupported,
                [StandardEnvironmentCapabilities.GuestAgentBoundary] =
                    CapabilityState.Unsupported,
                [StandardEnvironmentCapabilities.MediatedEngineAuthority] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.HostLocalEndpointPublication] =
                    CapabilityState.Supported,
            });
}
