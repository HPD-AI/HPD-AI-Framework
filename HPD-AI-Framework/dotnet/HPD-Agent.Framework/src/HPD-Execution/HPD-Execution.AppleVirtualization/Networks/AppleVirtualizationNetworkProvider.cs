namespace HPD.Execution.AppleVirtualization.Networks;

using HPD.Execution.AppleVirtualization.Handles;
using HPD.Execution.AppleVirtualization.GuestAgent;
using HPD.Execution.AppleVirtualization.Protocol;
using HPD.Execution.AppleVirtualization.State;
using HPD.Execution.Contracts;

public sealed class AppleVirtualizationNetworkProvider : INetworkProvider, INetworkMembershipProvider
{
    private static readonly ResourceKind NetworkMembershipKind = new("network-membership");
    private static readonly SchemaVersion SchemaVersion = new("v1");

    private readonly AppleVirtualizationProviderStateLedger _ledger;
    private readonly IAppleVirtualizationHelperClient _helper;
    private long _requestSequence;

    internal AppleVirtualizationNetworkProvider(
        AppleVirtualizationProviderStateLedger ledger,
        IAppleVirtualizationHelperClient helper)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _helper = helper ?? throw new ArgumentNullException(nameof(helper));
    }

    public ProviderId ProviderId => AppleVirtualizationProviderDescriptor.ProviderId;

    public async ValueTask<NetworkStatus> EnsureNetworkAsync(
        ResourceMetadata<Network> metadata,
        NetworkSpec spec,
        NetworkStatus? observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        NetworkValidation validation = ValidateNetworkSpec(spec);
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.NetworkStatus, AppleVirtualizationHelperProtocol.NetworkStatusRequestSchema) with
            {
                ResourceKind = metadata.Kind,
                ResourceId = metadata.Id.Value,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderGeneration = _ledger.ProviderGeneration,
                NetworkStatusRequest = new AppleVirtualizationNetworkStatusRequest
                {
                    HostId = metadata.Id.Value,
                    RequestedAttachment = AppleVirtualizationNetworkAttachmentKind.Nat,
                    IncludeGuestObservation = false,
                    IncludeSocketObservation = false,
                    ExplicitRealMode = false,
                },
            },
            cancellationToken).ConfigureAwait(false);

        NetworkStatus status;
        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            status = FailedNetworkStatus(metadata, validation.Limitations, ToDiagnostic(response.Error, "network.status"));
        }
        else if (response.NetworkStatusResponse is not { } network)
        {
            status = FailedNetworkStatus(metadata, validation.Limitations, NetworkDiagnostics.MissingHelperPayload("network.status"));
        }
        else
        {
            NetworkCapabilitySet capabilities = network.RealizedCapabilities & (NetworkCapabilitySet.IPv4 | NetworkCapabilitySet.NatEgress);

            IReadOnlyList<NetworkLimitation> limitations = Combine(validation.Limitations, network.Limitations);
            NetworkPhase networkPhase = validation.Fatal ? NetworkPhase.Failed : limitations.Count == 0 ? NetworkPhase.Ready : NetworkPhase.Degraded;
            status = new NetworkStatus
            {
                Phase = networkPhase == NetworkPhase.Failed ? ResourcePhase.Failed : networkPhase == NetworkPhase.Ready ? ResourcePhase.Ready : ResourcePhase.Degraded,
                ObservedGeneration = metadata.Generation,
                LastTransitionAt = DateTimeOffset.UtcNow,
                NetworkPhase = networkPhase,
                RealizedCapabilities = capabilities,
                Gateways = PrimaryGateways(network.GuestNetworkStatus),
                Limitations = limitations,
                Conditions = NetworkConditions(metadata.Generation, networkPhase, limitations),
                Diagnostics = validation.Fatal ? [NetworkDiagnostics.UnsupportedNetworkRequest("network/" + metadata.Id.Value)] : network.Diagnostics,
            };
        }

        return _ledger.UpsertNetwork(metadata, status, spec).Status;
    }

    public ValueTask<NetworkStatus> GetStatusAsync(ResourceRef<Network> network, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<Network, NetworkStatus>> lookup = _ledger.TryGetNetwork(network);
        return ValueTask.FromResult(lookup.Succeeded
            ? lookup.Entry!.Status
            : new NetworkStatus
            {
                Phase = ResourcePhase.Failed,
                NetworkPhase = NetworkPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = [lookup.Diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(ProviderId, "network/" + network.Id.Value)],
            });
    }

    public ValueTask DeleteNetworkAsync(ResourceRef<Network> network, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ledger.RemoveNetwork(network);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<NetworkMembershipStatus> EnsureMembershipAsync(
        ResourceMetadata<NetworkMembership> metadata,
        NetworkMembershipSpec spec,
        NetworkMembershipStatus? observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<Network, NetworkStatus>> networkLookup =
            _ledger.TryGetNetwork(spec.Network);
        if (!networkLookup.Succeeded)
        {
            return StoreMembership(metadata, spec, FailedMembershipStatus(metadata, networkLookup.Diagnostic ?? NetworkDiagnostics.MissingNetwork(spec.Network.Id.Value)));
        }

        TargetResolution target = ResolveTarget(spec.Target);
        if (target.Diagnostic is not null)
        {
            return StoreMembership(metadata, spec, FailedMembershipStatus(metadata, target.Diagnostic));
        }

        MembershipValidation validation = ValidateMembershipSpec(spec);
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.NetworkStatus, AppleVirtualizationHelperProtocol.NetworkStatusRequestSchema) with
            {
                ResourceKind = metadata.Kind,
                ResourceId = metadata.Id.Value,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderGeneration = _ledger.ProviderGeneration,
                NetworkStatusRequest = new AppleVirtualizationNetworkStatusRequest
                {
                    HostId = target.HostId!,
                    RequestedAttachment = AppleVirtualizationNetworkAttachmentKind.Nat,
                    IncludeGuestObservation = true,
                    IncludeSocketObservation = false,
                    ExplicitRealMode = false,
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            return StoreMembership(metadata, spec, FailedMembershipStatus(metadata, ToDiagnostic(response.Error, "network.membership.status")));
        }

        if (response.NetworkStatusResponse?.GuestNetworkStatus is not { } guest)
        {
            return StoreMembership(metadata, spec, FailedMembershipStatus(metadata, NetworkDiagnostics.MissingHelperPayload("network.membership.status")));
        }

        NetworkMembershipStatus status = MembershipStatusFromGuest(metadata, spec, guest, validation);
        NetworkMembershipStatus stored = StoreMembership(metadata, spec, status);
        if (spec.Target.Unit is { } unit)
        {
            _ledger.AttachNetworkMembershipToExecutionUnit(
                new ResourceRef<ExecutionUnit>(
                    new ResourceId<ExecutionUnit>(unit.Route.BackingResourceId ?? metadata.Id.Value),
                    unit.Route.Scope),
                new ResourceRef<NetworkMembership>(metadata.Id, metadata.Scope, metadata.Generation));
        }

        return stored;
    }

    public ValueTask<NetworkMembershipStatus> GetMembershipStatusAsync(
        ResourceRef<NetworkMembership> membership,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>> lookup =
            _ledger.TryGetNetworkMembership(membership);
        return ValueTask.FromResult(lookup.Succeeded
            ? lookup.Entry!.Status
            : new NetworkMembershipStatus
            {
                Phase = ResourcePhase.Failed,
                MembershipPhase = NetworkMembershipPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = [lookup.Diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(ProviderId, "network-membership/" + membership.Id.Value)],
            });
    }

    public ValueTask ReleaseMembershipAsync(ResourceRef<NetworkMembership> membership, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>> lookup =
            _ledger.TryGetNetworkMembership(membership);
        if (!lookup.Succeeded)
        {
            return ValueTask.CompletedTask;
        }

        AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus> entry = lookup.Entry!;
        NetworkMembershipStatus released = entry.Status with
        {
            Phase = ResourcePhase.Ready,
            MembershipPhase = NetworkMembershipPhase.Released,
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        _ledger.UpsertNetworkMembership(ToMembershipMetadata(entry), released);
        _ledger.RemoveNetworkMembership(membership);
        return ValueTask.CompletedTask;
    }

    private AppleVirtualizationHelperEnvelope Request(AppleVirtualizationHelperOperation operation, SchemaId schema) =>
        AppleVirtualizationHelperEnvelope.Request(
            operation,
            "apple-vz-network-" + Interlocked.Increment(ref _requestSequence).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Interlocked.Read(ref _requestSequence),
            schema);

    private NetworkMembershipStatus StoreMembership(
        ResourceMetadata<NetworkMembership> metadata,
        NetworkMembershipSpec spec,
        NetworkMembershipStatus status) =>
        _ledger.UpsertNetworkMembership(metadata, status, spec).Status;

    private TargetResolution ResolveTarget(NetworkMembershipTarget target)
    {
        if (target.Kind == NetworkMembershipTargetKind.RuntimeHost && target.Host is { } hostHandle)
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> lookup =
                _ledger.TryGetRuntimeHost(hostHandle);
            if (!lookup.Succeeded)
            {
                return new TargetResolution(null, lookup.Diagnostic ?? NetworkDiagnostics.MissingHost("runtime-host"));
            }

            Diagnostic? hostDiagnostic = ValidateHostReady(lookup.Entry!);
            return hostDiagnostic is null
                ? new TargetResolution(lookup.Entry!.Resource.Id.Value, null)
                : new TargetResolution(null, hostDiagnostic);
        }

        if (target.Kind == NetworkMembershipTargetKind.ExecutionUnit && target.Unit is { } unitHandle)
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> lookup =
                _ledger.TryGetExecutionUnit(unitHandle);
            if (!lookup.Succeeded)
            {
                return new TargetResolution(null, lookup.Diagnostic ?? NetworkDiagnostics.MissingExecutionUnit("execution-unit"));
            }

            ExecutionUnitStatus unitStatus = lookup.Entry!.Status;
            if (unitStatus.Phase != ResourcePhase.Ready ||
                unitStatus.UnitPhase is not ExecutionUnitPhase.Ready and not ExecutionUnitPhase.Running)
            {
                return new TargetResolution(null, NetworkDiagnostics.ExecutionUnitNotReady("execution-unit/" + lookup.Entry.Resource.Id.Value));
            }

            if (unitStatus.AssignedHost is not { } hostRef)
            {
                return new TargetResolution(null, NetworkDiagnostics.MissingHost("execution-unit/" + lookup.Entry.Resource.Id.Value));
            }

            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> hostLookup =
                _ledger.TryGetRuntimeHost(hostRef);
            if (!hostLookup.Succeeded)
            {
                return new TargetResolution(null, hostLookup.Diagnostic ?? NetworkDiagnostics.MissingHost("runtime-host/" + hostRef.Id.Value));
            }

            Diagnostic? hostDiagnostic = ValidateHostReady(hostLookup.Entry!);
            return hostDiagnostic is null
                ? new TargetResolution(hostRef.Id.Value, null)
                : new TargetResolution(null, hostDiagnostic);
        }

        return new TargetResolution(null, NetworkDiagnostics.UnsupportedMembershipTarget(target.Kind));
    }

    private static Diagnostic? ValidateHostReady(AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> host)
    {
        RuntimeHostStatus status = host.Status;
        if (status.Phase == ResourcePhase.Ready &&
            status.HostPhase == RuntimeHostPhase.Ready &&
            status.Readiness?.Ready == true &&
            status.GuestControl?.Reachable == true)
        {
            return null;
        }

        return NetworkDiagnostics.HostNotReady("runtime-host/" + host.Resource.Id.Value, status.HostPhase, status.Phase);
    }

    private static NetworkStatus FailedNetworkStatus(
        ResourceMetadata<Network> metadata,
        IReadOnlyList<NetworkLimitation> limitations,
        Diagnostic diagnostic) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            NetworkPhase = NetworkPhase.Failed,
            Limitations = limitations,
            Diagnostics = [diagnostic],
        };

    private static NetworkMembershipStatus FailedMembershipStatus(ResourceMetadata<NetworkMembership> metadata, Diagnostic diagnostic) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            MembershipPhase = NetworkMembershipPhase.Failed,
            Diagnostics = [diagnostic],
        };

    private static NetworkMembershipStatus MembershipStatusFromGuest(
        ResourceMetadata<NetworkMembership> metadata,
        NetworkMembershipSpec spec,
        AppleVirtualizationGuestAgentNetworkStatus guest,
        MembershipValidation validation)
    {
        AppleVirtualizationGuestAgentNetworkInterfaceStatus? primary = PrimaryInterface(guest.Interfaces);
        IReadOnlyList<NetworkAddressAssignment> addresses = primary?.Addresses ?? Array.Empty<NetworkAddressAssignment>();
        IReadOnlyList<IpAddressValue> gateways = PrimaryGateways(guest);
        IReadOnlyList<NetworkLimitation> limitations = Combine(validation.Limitations, guest.Limitations);
        NetworkMembershipPhase membershipPhase = validation.Fatal
            ? NetworkMembershipPhase.Failed
            : primary is null
                ? NetworkMembershipPhase.Degraded
                : limitations.Count == 0 ? NetworkMembershipPhase.Ready : NetworkMembershipPhase.Degraded;

        return new NetworkMembershipStatus
        {
            Phase = membershipPhase == NetworkMembershipPhase.Failed ? ResourcePhase.Failed : membershipPhase == NetworkMembershipPhase.Ready ? ResourcePhase.Ready : ResourcePhase.Degraded,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            MembershipPhase = membershipPhase,
            EndpointHandle = new NetworkEndpointHandle(metadata.Id.Value),
            Addresses = addresses,
            Gateways = gateways,
            InterfaceName = primary?.Name,
            MacAddress = primary?.MacAddress,
            Mtu = primary?.Mtu,
            Limitations = limitations,
            RegisteredRecords = DiscoveryRecords(metadata, spec, addresses),
            Conditions = MembershipConditions(metadata.Generation, membershipPhase, primary is not null, limitations),
            Diagnostics = validation.Fatal ? [NetworkDiagnostics.UnsupportedMembershipRequest("network-membership/" + metadata.Id.Value)] : Array.Empty<Diagnostic>(),
        };
    }

    private static NetworkValidation ValidateNetworkSpec(NetworkSpec spec)
    {
        var limitations = new List<NetworkLimitation>(4);
        bool fatal = false;

        if (spec.Scope is not (NetworkScope.Runtime or NetworkScope.ExecutionUnit))
        {
            limitations.Add(Limitation(NetworkDegradedFeature.PeerConnectivity, CapabilityDegradationMode.Unsupported, "AppleVirtualization.NetworkScopeUnsupported", "Only runtime/execution-unit scoped NAT network resources are modeled in L12."));
            fatal = true;
        }

        if (spec.ConnectivityIntent is not (NetworkConnectivityIntent.NatEgress or NetworkConnectivityIntent.Isolated))
        {
            limitations.Add(Limitation(NetworkDegradedFeature.PeerConnectivity, CapabilityDegradationMode.Unsupported, "AppleVirtualization.ConnectivityIntentUnsupported", "Apple Virtualization L12 only models NAT egress or isolated network intent."));
            fatal = true;
        }

        if (spec.AddressFamilies.HasFlag(AddressFamilyRequirement.IPv6Required))
        {
            limitations.Add(Limitation(NetworkDegradedFeature.IPv6, CapabilityDegradationMode.Unsupported, "AppleVirtualization.IPv6RequiredUnsupported", "IPv6 required network resources are not proven by the L12 NAT provider path."));
            fatal = true;
        }
        else if (spec.AddressFamilies.HasFlag(AddressFamilyRequirement.IPv6Optional))
        {
            limitations.Add(Limitation(NetworkDegradedFeature.IPv6, CapabilityDegradationMode.Unsupported, "AppleVirtualization.IPv6OptionalUnsupported", "IPv6 is reported as unsupported until guest/network observations prove it."));
        }

        if (spec.CidrHints.Count > 0)
        {
            limitations.Add(Limitation(NetworkDegradedFeature.StaticAddress, CapabilityDegradationMode.Unsupported, "AppleVirtualization.CidrHintsUnsupported", "Static CIDR selection is not supported by the NAT default path."));
        }

        if (spec.ExposurePolicy.AllowPublishedEndpoints)
        {
            limitations.Add(Limitation(NetworkDegradedFeature.TcpPublish, CapabilityDegradationMode.DisabledByPolicy, "AppleVirtualization.EndpointPublicationExplicit", "Endpoint publication is explicit and handled by PublishedEndpoint resources; NAT networking does not imply automatic host exposure."));
        }

        if (spec.DiscoveryPolicy.SearchDomains.Count > 0)
        {
            limitations.Add(Limitation(NetworkDegradedFeature.InternalDns, CapabilityDegradationMode.PartiallyAvailable, "AppleVirtualization.SearchDomainsDiagnosticOnly", "Search domains are retained for service discovery metadata but do not configure host or guest DNS in L12."));
        }

        return new NetworkValidation(fatal, limitations);
    }

    private static MembershipValidation ValidateMembershipSpec(NetworkMembershipSpec spec)
    {
        var limitations = new List<NetworkLimitation>(4);
        bool fatal = false;

        if (spec.RequestedAddress is not null || spec.ConnectivityPolicy.RequireStaticAddress)
        {
            limitations.Add(Limitation(NetworkDegradedFeature.StaticAddress, CapabilityDegradationMode.Unsupported, "AppleVirtualization.StaticAddressUnsupported", "Static guest addresses are not configured by the L12 NAT membership path."));
            fatal = spec.ConnectivityPolicy.RequireStaticAddress;
        }

        if (spec.RequestedMacAddress is not null)
        {
            limitations.Add(Limitation(NetworkDegradedFeature.StaticMacAddress, CapabilityDegradationMode.DisabledByPolicy, "AppleVirtualization.StaticMacDeferred", "Custom MAC address selection is a VM configuration-time concern and is not mutated by membership reconciliation."));
        }

        if (spec.RequestedMtu is not null)
        {
            limitations.Add(Limitation(NetworkDegradedFeature.CustomMtu, CapabilityDegradationMode.Unsupported, "AppleVirtualization.CustomMtuUnsupported", "Custom MTU is not supported by the default NAT membership path."));
        }

        if (spec.ConnectivityPolicy.RequirePeerConnectivity)
        {
            limitations.Add(Limitation(NetworkDegradedFeature.PeerConnectivity, CapabilityDegradationMode.Unsupported, "AppleVirtualization.PeerConnectivityUnsupported", "Default NAT networking does not prove peer reachability."));
            fatal = true;
        }

        return new MembershipValidation(fatal, limitations);
    }

    private static IReadOnlyList<IpAddressValue> PrimaryGateways(AppleVirtualizationGuestAgentNetworkStatus? guest)
    {
        if (guest is null || guest.Routes.Count == 0)
        {
            return Array.Empty<IpAddressValue>();
        }

        var gateways = new List<IpAddressValue>(guest.Routes.Count);
        for (int i = 0; i < guest.Routes.Count; i++)
        {
            if (guest.Routes[i].IsDefault && guest.Routes[i].Gateway is { } gateway)
            {
                gateways.Add(gateway);
            }
        }

        return gateways.Count == 0 ? Array.Empty<IpAddressValue>() : gateways.ToArray();
    }

    private static AppleVirtualizationGuestAgentNetworkInterfaceStatus? PrimaryInterface(
        IReadOnlyList<AppleVirtualizationGuestAgentNetworkInterfaceStatus> interfaces)
    {
        for (int i = 0; i < interfaces.Count; i++)
        {
            if (interfaces[i].IsUp && HasPrimaryAddress(interfaces[i].Addresses))
            {
                return interfaces[i];
            }
        }

        return interfaces.Count == 0 ? null : interfaces[0];
    }

    private static bool HasPrimaryAddress(IReadOnlyList<NetworkAddressAssignment> addresses)
    {
        for (int i = 0; i < addresses.Count; i++)
        {
            if (addresses[i].IsPrimary)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<DiscoveryRecord> DiscoveryRecords(
        ResourceMetadata<NetworkMembership> metadata,
        NetworkMembershipSpec spec,
        IReadOnlyList<NetworkAddressAssignment> addresses)
    {
        int count = (spec.Hostname is null ? 0 : 1) + spec.Aliases.Count + spec.ServiceNames.Count;
        if (count == 0 || addresses.Count == 0)
        {
            return Array.Empty<DiscoveryRecord>();
        }

        var records = new DiscoveryRecord[count];
        int index = 0;
        ResourceRef<NetworkMembership> membership = new(metadata.Id, metadata.Scope, metadata.Generation);
        NetworkAddressAssignment primaryAddress = addresses[0];
        if (spec.Hostname is { } hostname)
        {
            records[index++] = AddressRecord(new DnsName(hostname.Value), primaryAddress, membership);
        }

        for (int i = 0; i < spec.Aliases.Count; i++)
        {
            records[index++] = AddressRecord(new DnsName(spec.Aliases[i].Value), primaryAddress, membership);
        }

        for (int i = 0; i < spec.ServiceNames.Count; i++)
        {
            records[index++] = new DiscoveryRecord(
                new DnsName(spec.ServiceNames[i].Value),
                DiscoveryRecordKind.Service,
                new DiscoveryRecordTarget(null, spec.ServiceNames[i], membership, null, NetworkTransport.Tcp, null),
                TimeSpan.FromSeconds(30),
                IsDerivedFromMembership: true);
        }

        return records;
    }

    private static DiscoveryRecord AddressRecord(
        DnsName name,
        NetworkAddressAssignment address,
        ResourceRef<NetworkMembership> membership) =>
        new(
            name,
            address.Address.Family == NetworkAddressFamily.IPv6 ? DiscoveryRecordKind.AAAA : DiscoveryRecordKind.A,
            new DiscoveryRecordTarget(address.Address, null, membership, null, null, null),
            TimeSpan.FromSeconds(30),
            IsDerivedFromMembership: true);

    private static IReadOnlyList<Condition> NetworkConditions(
        ResourceGeneration generation,
        NetworkPhase phase,
        IReadOnlyList<NetworkLimitation> limitations) =>
        [
            new Condition(
                "AppleVirtualization.NetworkConfigured",
                phase == NetworkPhase.Ready ? ConditionStatus.True : ConditionStatus.False,
                phase.ToString(),
                limitations.Count == 0 ? "Default NAT network is modeled as ready." : "Default NAT network is modeled with limitations.",
                DateTimeOffset.UtcNow,
                generation,
                phase == NetworkPhase.Ready ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning),
        ];

    private static IReadOnlyList<Condition> MembershipConditions(
        ResourceGeneration generation,
        NetworkMembershipPhase phase,
        bool observedInterface,
        IReadOnlyList<NetworkLimitation> limitations) =>
        [
            new Condition(
                "AppleVirtualization.NetworkMembershipObserved",
                observedInterface && phase == NetworkMembershipPhase.Ready ? ConditionStatus.True : ConditionStatus.False,
                phase.ToString(),
                observedInterface ? "Guest network interface was observed by the guest-agent network status path." : "Guest network interface was not observed.",
                DateTimeOffset.UtcNow,
                generation,
                limitations.Count == 0 ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning),
        ];

    private static IReadOnlyList<NetworkLimitation> Combine(
        IReadOnlyList<NetworkLimitation> left,
        IReadOnlyList<NetworkLimitation> right)
    {
        if (left.Count == 0)
        {
            return right;
        }

        if (right.Count == 0)
        {
            return left;
        }

        NetworkLimitation[] combined = new NetworkLimitation[left.Count + right.Count];
        for (int i = 0; i < left.Count; i++)
        {
            combined[i] = left[i];
        }

        for (int i = 0; i < right.Count; i++)
        {
            combined[left.Count + i] = right[i];
        }

        return combined;
    }

    private static NetworkLimitation Limitation(
        NetworkDegradedFeature feature,
        CapabilityDegradationMode mode,
        string reasonCode,
        string message) =>
        new(feature, mode, reasonCode, message);

    private static ResourceMetadata<NetworkMembership> ToMembershipMetadata(
        AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus> entry) =>
        new()
        {
            Id = entry.Resource.Id,
            Kind = NetworkMembershipKind,
            Scope = entry.Resource.Scope,
            Generation = entry.Resource.Generation ?? default,
            SchemaVersion = SchemaVersion,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
        };

    private static Diagnostic ToDiagnostic(AppleVirtualizationHelperError? error, string operation)
    {
        if (error is null)
        {
            return NetworkDiagnostics.HelperError(operation, "The Apple Virtualization helper returned an error response without an error payload.");
        }

        return new Diagnostic
        {
            Severity = error.Severity,
            Code = new DiagnosticCode(error.Code),
            Message = error.Message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = error.Operation ?? operation,
        };
    }

    private readonly record struct NetworkValidation(bool Fatal, IReadOnlyList<NetworkLimitation> Limitations);
    private readonly record struct MembershipValidation(bool Fatal, IReadOnlyList<NetworkLimitation> Limitations);
    private readonly record struct TargetResolution(string? HostId, Diagnostic? Diagnostic);
}

internal static class NetworkDiagnostics
{
    public static Diagnostic MissingHelperPayload(string targetPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode("AppleVirtualization.NetworkMissingHelperPayload"),
            Message = "The Apple Virtualization helper returned a network response without the expected network payload.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic MissingNetwork(string id) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode("AppleVirtualization.NetworkMissing"),
            Message = "The Apple Virtualization network membership could not resolve its network resource.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "network/" + id,
        };

    public static Diagnostic MissingHost(string targetPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode("AppleVirtualization.NetworkHostMissing"),
            Message = "The Apple Virtualization network membership could not resolve a ready runtime host.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic MissingExecutionUnit(string targetPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode("AppleVirtualization.NetworkExecutionUnitMissing"),
            Message = "The Apple Virtualization network membership could not resolve its execution-unit target.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic HostNotReady(string targetPath, RuntimeHostPhase hostPhase, ResourcePhase phase) =>
        new()
        {
            Severity = DiagnosticSeverity.Warning,
            Code = new DiagnosticCode("AppleVirtualization.NetworkHostNotReady"),
            Message = $"The Apple Virtualization network membership is waiting for its runtime host to be HPD-ready. Host phase: {hostPhase}; resource phase: {phase}.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic ExecutionUnitNotReady(string targetPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Warning,
            Code = new DiagnosticCode("AppleVirtualization.NetworkExecutionUnitNotReady"),
            Message = "The Apple Virtualization network membership target execution unit is not ready.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic UnsupportedMembershipTarget(NetworkMembershipTargetKind kind) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode("AppleVirtualization.NetworkMembershipTargetUnsupported"),
            Message = $"The Apple Virtualization provider does not support network membership target kind '{kind}' in L12.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "network-membership.target",
        };

    public static Diagnostic UnsupportedNetworkRequest(string targetPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode("AppleVirtualization.NetworkRequestUnsupported"),
            Message = "The Apple Virtualization provider cannot satisfy the requested network shape with the conservative NAT default path.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic UnsupportedMembershipRequest(string targetPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode("AppleVirtualization.NetworkMembershipRequestUnsupported"),
            Message = "The Apple Virtualization provider cannot satisfy the requested network membership shape with the conservative NAT default path.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic HelperError(string targetPath, string message) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode("AppleVirtualization.NetworkHelperError"),
            Message = message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };
}
