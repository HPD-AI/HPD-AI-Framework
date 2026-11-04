using HPD.Environment.Contracts;
using HPD.Environment.ProviderConformance;

namespace HPD.Environment.HostContainers.Tests;

public sealed class HostContainersProviderCapabilityConformanceTests
    : ProviderCapabilityConformanceTests
{
    protected override ProviderCapabilityConformanceFixture
        CreateCapabilityFixture() =>
        new(
            new HostContainersCapabilityReporter(
                new HostContainersProviderOptions
                {
                    EngineSocketPath = "/test/docker.sock",
                }),
            HostContainersProviderDescriptor.ProviderId,
            new ProviderCapabilityQuery(
                HostPlatform:
                    HostContainersProviderDescriptor
                        .CurrentPlatform()),
            new Dictionary<CapabilityId, CapabilityState>
            {
                [StandardEnvironmentCapabilities.ProcessIsolation] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.ContainerIsolation] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.SharedHostKernel] =
                    OperatingSystem.IsLinux()
                        ? CapabilityState.Supported
                        : CapabilityState.Degraded,
                [StandardEnvironmentCapabilities.HardwareVirtualization] =
                    OperatingSystem.IsLinux()
                        ? CapabilityState.Unsupported
                        : CapabilityState.Degraded,
                [StandardEnvironmentCapabilities.GuestAgentBoundary] =
                    CapabilityState.Unsupported,
                [StandardEnvironmentCapabilities.MediatedEngineAuthority] =
                    CapabilityState.Supported,
                [StandardEnvironmentCapabilities.HostLocalEndpointPublication] =
                    CapabilityState.Supported,
            });
}
