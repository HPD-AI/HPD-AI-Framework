namespace HPD.Execution.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Execution.AppleVirtualization.Handles;
using HPD.Execution.AppleVirtualization.Networks;
using HPD.Execution.AppleVirtualization.State;
using HPD.Execution.AppleVirtualization.Tests.Fixtures;
using HPD.Execution.Contracts;
using Xunit;

public sealed class AppleVirtualizationServiceDiscoveryProviderTests
{
    [Fact]
    public async Task EnsureServiceDiscovery_derives_membership_host_alias_and_service_records()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedNetwork(ledger);
        SeedMembership(ledger, "membership-1", hostname: "unit-1", aliases: ["worker"], services: ["app"]);
        var provider = new AppleVirtualizationServiceDiscoveryProvider(ledger);

        ServiceDiscoveryStatus status = await provider.EnsureServiceDiscoveryAsync(
            Metadata<ServiceDiscovery>("discovery-1", "service-discovery"),
            DiscoverySpec(DefaultDiscoveryRecordPolicy.MembershipHostnamesAndAliases),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.DiscoveryPhase.Should().Be(ServiceDiscoveryPhase.Ready);
        status.RealizedCapabilities.Should().HaveFlag(DiscoveryCapabilitySet.ARecords);
        status.RealizedCapabilities.Should().HaveFlag(DiscoveryCapabilitySet.ServiceRecords);
        status.Records.Should().Contain(record =>
            record.Name.Value == "unit-1" &&
            record.Kind == DiscoveryRecordKind.A &&
            record.Target.Address.HasValue &&
            record.Target.Address.Value.LowBits == 0x0a000002 &&
            record.IsDerivedFromMembership);
        status.Records.Should().Contain(record => record.Name.Value == "worker" && record.Kind == DiscoveryRecordKind.A);
        status.Records.Should().Contain(record => record.Name.Value == "app" && record.Kind == DiscoveryRecordKind.Service);

        IReadOnlyList<DiscoveryRecord> resolved = await provider.ResolveAsync(new ServiceDiscoveryQuery(
            new ResourceRef<ServiceDiscovery>(new ResourceId<ServiceDiscovery>("discovery-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1)),
            new DnsName("UNIT-1"),
            DiscoveryRecordKind.A));
        resolved.Should().ContainSingle(record => record.Name.Value == "unit-1");
    }

    [Fact]
    public async Task Resolve_filters_by_name_and_kind()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedNetwork(ledger);
        SeedMembership(ledger, "membership-1", hostname: "unit-1", aliases: ["worker"], services: ["app"]);
        var provider = new AppleVirtualizationServiceDiscoveryProvider(ledger);
        var discovery = new ResourceRef<ServiceDiscovery>(
            new ResourceId<ServiceDiscovery>("discovery-1"),
            AppleVirtualizationContractFixtures.RuntimeScope,
            new ResourceGeneration(1));

        await provider.EnsureServiceDiscoveryAsync(
            Metadata<ServiceDiscovery>("discovery-1", "service-discovery"),
            DiscoverySpec(DefaultDiscoveryRecordPolicy.MembershipHostnamesAndAliases),
            observed: null);

        (await provider.ResolveAsync(new ServiceDiscoveryQuery(discovery, new DnsName("worker"), DiscoveryRecordKind.Service)))
            .Should().BeEmpty();
        (await provider.ResolveAsync(new ServiceDiscoveryQuery(discovery, new DnsName("app"), DiscoveryRecordKind.Service)))
            .Should().ContainSingle(record => record.Target.ServiceName.HasValue && record.Target.ServiceName.Value.Value == "app");
    }

    [Fact]
    public async Task Host_dns_export_and_resolver_import_are_limitations_not_effective_resolvers()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedNetwork(ledger);
        SeedMembership(ledger, "membership-1", hostname: "unit-1", aliases: [], services: []);
        var provider = new AppleVirtualizationServiceDiscoveryProvider(ledger);

        ServiceDiscoveryStatus status = await provider.EnsureServiceDiscoveryAsync(
            Metadata<ServiceDiscovery>("discovery-1", "service-discovery"),
            DiscoverySpec(DefaultDiscoveryRecordPolicy.MembershipHostnames) with
            {
                RequestHostExport = true,
                RequestHostResolverImport = true,
                SearchDomains = [new DnsName("hpd.local")],
            },
            observed: null);

        status.DiscoveryPhase.Should().Be(ServiceDiscoveryPhase.Degraded);
        status.HostExportedDomains.Should().BeEmpty();
        status.EffectiveResolvers.Should().BeEmpty();
        status.Limitations.Should().Contain(limitation => limitation.Feature == NetworkDegradedFeature.HostDnsExport);
        status.Limitations.Should().Contain(limitation => limitation.Feature == NetworkDegradedFeature.HostResolverImport);
        status.Limitations.Should().Contain(limitation => limitation.ReasonCode == "AppleVirtualization.SearchDomainsDiagnosticOnly");
    }

    [Fact]
    public async Task Explicit_only_discovery_uses_explicit_records_without_membership_records()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedNetwork(ledger);
        SeedMembership(ledger, "membership-1", hostname: "unit-1", aliases: ["worker"], services: ["app"]);
        var provider = new AppleVirtualizationServiceDiscoveryProvider(ledger);

        ServiceDiscoveryStatus status = await provider.EnsureServiceDiscoveryAsync(
            Metadata<ServiceDiscovery>("discovery-1", "service-discovery"),
            DiscoverySpec(DefaultDiscoveryRecordPolicy.ExplicitOnly) with
            {
                Records =
                [
                    new DiscoveryRecordSpec(
                        new DnsName("manual"),
                        DiscoveryRecordKind.A,
                        new DiscoveryRecordTarget(new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000064), null, null, null, null, null)),
                ],
            },
            observed: null);

        status.Records.Should().ContainSingle(record => record.Name.Value == "manual");
        status.Records.Should().NotContain(record => record.Name.Value == "unit-1");
        status.Records.Should().NotContain(record => record.Name.Value == "worker");
        status.Records.Should().NotContain(record => record.Name.Value == "app");
    }

    [Fact]
    public async Task Discovery_records_are_bounded_and_report_truncation()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedNetwork(ledger);
        for (int i = 0; i < 140; i++)
        {
            SeedMembership(ledger, "membership-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), hostname: "unit-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), aliases: [], services: []);
        }

        var provider = new AppleVirtualizationServiceDiscoveryProvider(ledger);

        ServiceDiscoveryStatus status = await provider.EnsureServiceDiscoveryAsync(
            Metadata<ServiceDiscovery>("discovery-1", "service-discovery"),
            DiscoverySpec(DefaultDiscoveryRecordPolicy.MembershipHostnames),
            observed: null);

        status.Records.Should().HaveCount(128);
        status.DiscoveryPhase.Should().Be(ServiceDiscoveryPhase.Degraded);
        status.Limitations.Should().Contain(limitation =>
            limitation.ReasonCode == "AppleVirtualization.ServiceDiscoveryRecordsTruncated");
    }

    [Fact]
    public async Task Missing_network_fails_discovery_deterministically()
    {
        var provider = new AppleVirtualizationServiceDiscoveryProvider(new AppleVirtualizationProviderStateLedger());

        ServiceDiscoveryStatus status = await provider.EnsureServiceDiscoveryAsync(
            Metadata<ServiceDiscovery>("discovery-1", "service-discovery"),
            DiscoverySpec(DefaultDiscoveryRecordPolicy.MembershipHostnames),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.DiscoveryPhase.Should().Be(ServiceDiscoveryPhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == AppleVirtualizationHandleDiagnostics.MissingHandle);
    }

    [Fact]
    public async Task Disabled_discovery_returns_structured_disabled_status()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedNetwork(ledger);
        var provider = new AppleVirtualizationServiceDiscoveryProvider(ledger);

        ServiceDiscoveryStatus status = await provider.EnsureServiceDiscoveryAsync(
            Metadata<ServiceDiscovery>("discovery-1", "service-discovery"),
            DiscoverySpec(DefaultDiscoveryRecordPolicy.None),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.DiscoveryPhase.Should().Be(ServiceDiscoveryPhase.Disabled);
        status.Records.Should().BeEmpty();
        status.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.ServiceDiscoveryDisabled" &&
            condition.Status == ConditionStatus.True);
    }

    private static ResourceMetadata<TResource> Metadata<TResource>(string id, string kind)
        where TResource : IExecutionResourceMarker =>
        AppleVirtualizationContractFixtures.Metadata<TResource>(id, kind);

    private static ServiceDiscoverySpec DiscoverySpec(DefaultDiscoveryRecordPolicy policy) =>
        new()
        {
            Scope = DiscoveryScope.Network,
            Network = NetworkRef(),
            DefaultRecordPolicy = policy,
            DefaultTtl = TimeSpan.FromSeconds(15),
        };

    private static ResourceRef<Network> NetworkRef() =>
        new(new ResourceId<Network>("network-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));

    private static void SeedNetwork(AppleVirtualizationProviderStateLedger ledger) =>
        ledger.UpsertNetwork(
            Metadata<Network>("network-1", "network"),
            new NetworkStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                NetworkPhase = NetworkPhase.Ready,
                RealizedCapabilities = NetworkCapabilitySet.IPv4 | NetworkCapabilitySet.NatEgress,
            },
            new NetworkSpec
            {
                Scope = NetworkScope.Runtime,
                ConnectivityIntent = NetworkConnectivityIntent.NatEgress,
                AddressFamilies = AddressFamilyRequirement.IPv4Required,
            });

    private static void SeedMembership(
        AppleVirtualizationProviderStateLedger ledger,
        string id,
        string hostname,
        IReadOnlyList<string> aliases,
        IReadOnlyList<string> services)
    {
        ScopedName[] aliasValues = aliases.Select(alias => new ScopedName(alias)).ToArray();
        ServiceName[] serviceValues = services.Select(service => new ServiceName(service)).ToArray();
        ledger.UpsertNetworkMembership(
            Metadata<NetworkMembership>(id, "network-membership"),
            new NetworkMembershipStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                MembershipPhase = NetworkMembershipPhase.Ready,
                Addresses =
                [
                    new NetworkAddressAssignment(
                        new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000002),
                        24,
                        AddressAssignmentKind.ProviderAssigned,
                        IsPrimary: true),
                ],
                InterfaceName = "en0",
                Mtu = 1500,
            },
            new NetworkMembershipSpec
            {
                Network = NetworkRef(),
                Target = new NetworkMembershipTarget(NetworkMembershipTargetKind.ProviderDefined, Host: null, Unit: null, Process: null),
                Hostname = new ScopedName(hostname),
                Aliases = aliasValues,
                ServiceNames = serviceValues,
            });
    }
}
