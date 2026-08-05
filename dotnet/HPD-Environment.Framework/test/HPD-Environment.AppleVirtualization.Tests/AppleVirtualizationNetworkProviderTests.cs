namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Networks;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationNetworkProviderTests
{
    [Fact]
    public async Task EnsureNetwork_creates_ready_default_nat_network_with_honest_capabilities()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(NetworkResponse("network-1"));
        var ledger = new AppleVirtualizationProviderStateLedger();
        var provider = new AppleVirtualizationNetworkProvider(ledger, helper);

        NetworkStatus status = await provider.EnsureNetworkAsync(
            Metadata<Network>("network-1", "network"),
            DefaultNetworkSpec(),
            realizationContext: null,
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.NetworkPhase.Should().Be(NetworkPhase.Ready);
        status.RealizedCapabilities.Should().HaveFlag(NetworkCapabilitySet.IPv4);
        status.RealizedCapabilities.Should().HaveFlag(NetworkCapabilitySet.NatEgress);
        status.RealizedCapabilities.Should().NotHaveFlag(NetworkCapabilitySet.TcpPublish);
        status.Handle.Should().NotBeNull();
        helper.Requests.Should().ContainSingle(request =>
            request.Operation == AppleVirtualizationHelperOperation.NetworkStatus &&
            request.NetworkStatusRequest!.RequestedAttachment == AppleVirtualizationNetworkAttachmentKind.Nat);
    }

    [Fact]
    public async Task EnsureNetwork_reports_unsupported_shape_as_failed_with_limitations()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(NetworkResponse("network-1"));
        var provider = new AppleVirtualizationNetworkProvider(new AppleVirtualizationProviderStateLedger(), helper);

        NetworkStatus status = await provider.EnsureNetworkAsync(
            Metadata<Network>("network-1", "network"),
            new NetworkSpec
            {
                Scope = NetworkScope.Shared,
                ConnectivityIntent = NetworkConnectivityIntent.PeerReachable,
                AddressFamilies = AddressFamilyRequirement.IPv6Required,
                CidrHints = [new IpCidr(new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000000), 24)],
            },
            realizationContext: null,
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.NetworkPhase.Should().Be(NetworkPhase.Failed);
        status.Limitations.Should().Contain(limitation => limitation.Feature == NetworkDegradedFeature.IPv6);
        status.Limitations.Should().Contain(limitation => limitation.Feature == NetworkDegradedFeature.PeerConnectivity);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.NetworkRequestUnsupported");
    }

    [Fact]
    public async Task EnsureMembership_requires_ready_unit_target()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedNetwork(ledger);
        SeedHost(ledger, ready: true);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedUnit(ledger, ready: false);
        var provider = new AppleVirtualizationNetworkProvider(ledger, helper);

        NetworkMembershipStatus status = await provider.EnsureMembershipAsync(
            Metadata<NetworkMembership>("membership-1", "network-membership"),
            DefaultMembershipSpec(unit.TargetHandle),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.MembershipPhase.Should().Be(NetworkMembershipPhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.NetworkExecutionUnitNotReady");
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureMembership_maps_guest_interface_address_and_mtu_to_status()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(NetworkResponse("runtime-host-1", includeGuest: true));
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedNetwork(ledger);
        SeedHost(ledger, ready: true);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedUnit(ledger, ready: true);
        var provider = new AppleVirtualizationNetworkProvider(ledger, helper);

        NetworkMembershipStatus status = await provider.EnsureMembershipAsync(
            Metadata<NetworkMembership>("membership-1", "network-membership"),
            DefaultMembershipSpec(unit.TargetHandle),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.MembershipPhase.Should().Be(NetworkMembershipPhase.Ready);
        status.InterfaceName.Should().Be("en0");
        status.Mtu.Should().Be(1500);
        status.Addresses.Should().ContainSingle(address =>
            address.Address.Family == NetworkAddressFamily.IPv4 &&
            address.Address.LowBits == 0x0a000002 &&
            address.IsPrimary);
        status.Gateways.Should().ContainSingle(gateway => gateway.LowBits == 0x0a000001);
        status.RegisteredRecords.Should().Contain(record => record.Name.Value == "unit-1");
        ledger.TryGetExecutionUnit(AppleVirtualizationContractFixtures.ExecutionUnitRef()).Entry!.Status.NetworkMemberships
            .Should().ContainSingle(membership => membership.Id.Value == "membership-1");
    }

    [Fact]
    public async Task EnsureMembership_reports_stale_unit_handle_deterministically()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedNetwork(ledger);
        SeedHost(ledger, ready: true);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedUnit(ledger, ready: true);
        ledger.AdvanceProviderGeneration();
        var provider = new AppleVirtualizationNetworkProvider(ledger, new FakeAppleVirtualizationHelperClient());

        NetworkMembershipStatus status = await provider.EnsureMembershipAsync(
            Metadata<NetworkMembership>("membership-1", "network-membership"),
            DefaultMembershipSpec(unit.TargetHandle),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == AppleVirtualizationHandleDiagnostics.StaleHandle);
    }

    [Fact]
    public async Task ReleaseMembership_is_idempotent_and_detaches_unit_state()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(NetworkResponse("runtime-host-1", includeGuest: true));
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedNetwork(ledger);
        SeedHost(ledger, ready: true);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedUnit(ledger, ready: true);
        var provider = new AppleVirtualizationNetworkProvider(ledger, helper);
        var membership = new ResourceRef<NetworkMembership>(
            new ResourceId<NetworkMembership>("membership-1"),
            AppleVirtualizationContractFixtures.RuntimeScope,
            new ResourceGeneration(1));

        await provider.EnsureMembershipAsync(
            Metadata<NetworkMembership>("membership-1", "network-membership"),
            DefaultMembershipSpec(unit.TargetHandle),
            observed: null);
        await provider.ReleaseMembershipAsync(membership);
        await provider.ReleaseMembershipAsync(membership);

        ledger.TryGetExecutionUnit(AppleVirtualizationContractFixtures.ExecutionUnitRef()).Entry!.Status.NetworkMemberships
            .Should().BeEmpty();
        (await provider.GetMembershipStatusAsync(membership)).Diagnostics
            .Should().ContainSingle(diagnostic => diagnostic.Code == AppleVirtualizationHandleDiagnostics.MissingHandle);
    }

    [Fact]
    public async Task Removing_unit_marks_owned_memberships_released()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(NetworkResponse("runtime-host-1", includeGuest: true));
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedNetwork(ledger);
        SeedHost(ledger, ready: true);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedUnit(ledger, ready: true);
        var provider = new AppleVirtualizationNetworkProvider(ledger, helper);
        var membership = new ResourceRef<NetworkMembership>(
            new ResourceId<NetworkMembership>("membership-1"),
            AppleVirtualizationContractFixtures.RuntimeScope,
            new ResourceGeneration(1));

        await provider.EnsureMembershipAsync(
            Metadata<NetworkMembership>("membership-1", "network-membership"),
            DefaultMembershipSpec(unit.TargetHandle),
            observed: null);
        ledger.RemoveExecutionUnit(AppleVirtualizationContractFixtures.ExecutionUnitRef());

        NetworkMembershipStatus status = (await provider.GetMembershipStatusAsync(membership));
        status.MembershipPhase.Should().Be(NetworkMembershipPhase.Released);
    }

    private static ResourceMetadata<TResource> Metadata<TResource>(string id, string kind)
        where TResource : IExecutionResourceMarker =>
        AppleVirtualizationContractFixtures.Metadata<TResource>(id, kind);

    private static NetworkSpec DefaultNetworkSpec() =>
        new()
        {
            Scope = NetworkScope.Runtime,
            ConnectivityIntent = NetworkConnectivityIntent.NatEgress,
            AddressFamilies = AddressFamilyRequirement.IPv4Required,
            DiscoveryPolicy = new NetworkDiscoveryPolicy { EnableInternalDns = false },
        };

    private static NetworkMembershipSpec DefaultMembershipSpec(TargetHandle<ExecutionUnit> unit) =>
        new()
        {
            Network = new ResourceRef<Network>(
                new ResourceId<Network>("network-1"),
                AppleVirtualizationContractFixtures.RuntimeScope,
                new ResourceGeneration(1)),
            Target = new NetworkMembershipTarget(
                NetworkMembershipTargetKind.ExecutionUnit,
                Host: null,
                Unit: unit,
                Process: null),
            Hostname = new ScopedName("unit-1"),
            Aliases = [new ScopedName("worker")],
            ServiceNames = [new ServiceName("app")],
        };

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
            DefaultNetworkSpec());

    private static void SeedHost(AppleVirtualizationProviderStateLedger ledger, bool ready) =>
        ledger.UpsertRuntimeHost(
            Metadata<RuntimeHost>("runtime-host-1", "runtime-host"),
            new RuntimeHostStatus
            {
                Phase = ready ? ResourcePhase.Ready : ResourcePhase.Reconciling,
                ObservedGeneration = new ResourceGeneration(1),
                HostPhase = ready ? RuntimeHostPhase.Ready : RuntimeHostPhase.Running,
                GuestControl = new GuestControlStatus(Expected: true, Installed: ready, Reachable: ready),
                Readiness = new RuntimeHostReadinessStatus(Ready: ready),
            },
            AppleVirtualizationContractFixtures.RuntimeHostSpec());

    private static AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> SeedUnit(AppleVirtualizationProviderStateLedger ledger, bool ready) =>
        ledger.UpsertExecutionUnit(
            Metadata<ExecutionUnit>("unit-1", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ready ? ResourcePhase.Ready : ResourcePhase.Reconciling,
                ObservedGeneration = new ResourceGeneration(1),
                UnitPhase = ready ? ExecutionUnitPhase.Ready : ExecutionUnitPhase.Declared,
                AssignedHost = AppleVirtualizationContractFixtures.RuntimeHostRef(),
            },
            AppleVirtualizationContractFixtures.ExecutionUnitSpec());

    private static AppleVirtualizationHelperEnvelope NetworkResponse(string hostId, bool includeGuest = false) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.NetworkStatus,
            "network-response",
            1,
            AppleVirtualizationHelperProtocol.NetworkStatusResponseSchema) with
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            NetworkStatusResponse = new AppleVirtualizationNetworkStatusResponse
            {
                HostId = hostId,
                State = AppleVirtualizationNetworkObservationState.Ready,
                DefaultAttachment = AppleVirtualizationNetworkAttachmentKind.Nat,
                RequestedAttachment = AppleVirtualizationNetworkAttachmentKind.Nat,
                RealizedCapabilities = NetworkCapabilitySet.IPv4 | NetworkCapabilitySet.NatEgress,
                VmRunning = true,
                GuestAgentReady = includeGuest,
                GuestNetworkStatus = includeGuest ? GuestNetworkStatus(hostId) : null,
            },
        };

    private static AppleVirtualizationGuestAgentNetworkStatus GuestNetworkStatus(string hostId) =>
        new()
        {
            HostId = hostId,
            GuestAgentReady = true,
            Interfaces =
            [
                new AppleVirtualizationGuestAgentNetworkInterfaceStatus
                {
                    Name = "en0",
                    Mtu = 1500,
                    IsUp = true,
                    Addresses =
                    [
                        new NetworkAddressAssignment(
                            new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000002),
                            24,
                            AddressAssignmentKind.ProviderAssigned,
                            IsPrimary: true),
                    ],
                },
            ],
            Routes =
            [
                new AppleVirtualizationGuestAgentNetworkRouteObservation
                {
                    Gateway = new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000001),
                    InterfaceName = "en0",
                    IsDefault = true,
                },
            ],
        };
}
