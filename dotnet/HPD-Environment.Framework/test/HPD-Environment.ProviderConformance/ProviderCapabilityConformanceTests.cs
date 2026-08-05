using HPD.Environment.Contracts;
using Xunit;

namespace HPD.Environment.ProviderConformance;

public abstract class ProviderCapabilityConformanceTests
{
    protected abstract ProviderCapabilityConformanceFixture
        CreateCapabilityFixture();

    [Fact]
    public async Task Standard_environment_capabilities_are_explicit_and_unique()
    {
        ProviderCapabilityConformanceFixture fixture =
            CreateCapabilityFixture();
        ProviderCapabilityReport report =
            await fixture.GetReportAsync();

        Assert.Equal(fixture.ProviderId, report.ProviderId);
        foreach ((CapabilityId id, CapabilityState expected) in
                 fixture.ExpectedStates)
        {
            CapabilityFact fact = Assert.Single(
                report.Capabilities,
                candidate => candidate.Id == id);
            Assert.Equal(expected, fact.State);
            Assert.NotEqual(ProviderContractKind.None, fact.AppliesTo);
            Assert.False(string.IsNullOrWhiteSpace(fact.Detail));
        }
    }

    [Fact]
    public async Task Capability_report_contains_every_standard_environment_fact()
    {
        ProviderCapabilityConformanceFixture fixture =
            CreateCapabilityFixture();
        ProviderCapabilityReport report =
            await fixture.GetReportAsync();
        CapabilityId[] standard =
        [
            StandardEnvironmentCapabilities.ProcessIsolation,
            StandardEnvironmentCapabilities.ContainerIsolation,
            StandardEnvironmentCapabilities.SharedHostKernel,
            StandardEnvironmentCapabilities.HardwareVirtualization,
            StandardEnvironmentCapabilities.GuestAgentBoundary,
            StandardEnvironmentCapabilities.MediatedEngineAuthority,
            StandardEnvironmentCapabilities.HostLocalEndpointPublication,
        ];

        Assert.All(standard, id =>
            Assert.Single(
                report.Capabilities,
                fact => fact.Id == id));
    }
}

public sealed class ProviderCapabilityConformanceFixture(
    IProviderCapabilityReporter reporter,
    ProviderId providerId,
    ProviderCapabilityQuery query,
    IReadOnlyDictionary<CapabilityId, CapabilityState> expectedStates)
{
    public ProviderId ProviderId { get; } = providerId;
    public IReadOnlyDictionary<CapabilityId, CapabilityState>
        ExpectedStates { get; } = expectedStates;

    public ValueTask<ProviderCapabilityReport> GetReportAsync(
        CancellationToken cancellationToken = default) =>
        reporter.GetCapabilitiesAsync(
            ProviderId,
            query,
            cancellationToken);
}
