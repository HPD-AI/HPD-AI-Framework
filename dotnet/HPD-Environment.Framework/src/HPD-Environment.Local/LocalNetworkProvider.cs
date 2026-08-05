namespace HPD.Environment.Local;

using System.Security.Cryptography;
using System.Text;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class LocalNetworkProvider(
    LocalProviderState state,
    ILocalEngineNetworkClient engineClient) :
    INetworkProvider,
    INetworkMembershipProvider
{
    private static readonly ProviderResourceShape NetworkShape = new(
        new TargetKind("network"),
        TargetRouteSegmentKind.Network,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe |
        TargetHandleAuthority.Control,
        new SchemaId("hpd.execution.local.network.handle.v1"));
    private static readonly ProviderResourceShape MembershipShape = new(
        new TargetKind("network-membership"),
        TargetRouteSegmentKind.Network,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe |
        TargetHandleAuthority.Control,
        new SchemaId(
            "hpd.execution.local.network-membership.handle.v1"));

    public ProviderId ProviderId =>
        LocalEnvironmentProviderDescriptor.ProviderId;

    public async ValueTask<NetworkStatus> EnsureNetworkAsync(
        ResourceMetadata<Network> metadata,
        NetworkSpec spec,
        NetworkRealizationContext? realizationContext,
        NetworkStatus? observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();
        if (!state.IsEngineReady)
            return FailedNetwork(
                metadata,
                "LocalEnvironment.NetworkEngineNotReady",
                "The Local engine must be ready before creating an HPD-owned network.");

        Diagnostic? invalid = ValidateNetwork(spec);
        if (invalid is not null)
            return FailedNetwork(
                metadata,
                invalid.Code.Value,
                invalid.Message);
        Diagnostic? authorityFailure =
            ValidateEngineAuthority(realizationContext);
        if (authorityFailure is not null)
            return FailedNetwork(
                metadata,
                authorityFailure.Code.Value,
                authorityFailure.Message);

        string resourceKey =
            Key<Network>(metadata.Scope, metadata.Id.Value);
        string networkName = PhysicalName(metadata, spec);
        IReadOnlyDictionary<string, string> labels =
            OwnershipLabels(metadata, spec);
        LocalEngineNetworkObservation realized;
        try
        {
            realized = await engineClient.EnsureAsync(
                state.CurrentEngineSocketPath,
                networkName,
                labels,
                internalOnly:
                    spec.ConnectivityIntent ==
                    NetworkConnectivityIntent.Isolated,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            return FailedNetwork(
                metadata,
                "LocalEnvironment.NetworkRealizationFailed",
                Bounded(exception.Message));
        }

        NetworkCapabilitySet capabilities =
            NetworkCapabilitySet.IPv4 |
            NetworkCapabilitySet.PeerConnectivity |
            NetworkCapabilitySet.InternalDns |
            NetworkCapabilitySet.ServiceRecords |
            NetworkCapabilitySet.TcpPublish;
        if (spec.ConnectivityIntent ==
            NetworkConnectivityIntent.NatEgress)
            capabilities |= NetworkCapabilitySet.NatEgress;
        var status = new NetworkStatus
        {
            Phase = ResourcePhase.Ready,
            NetworkPhase = NetworkPhase.Ready,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            RealizedCapabilities = capabilities,
            Realization = new NetworkRealizationIdentity(
                new ScopedName(realized.Name),
                realized.Id),
        };
        ProviderResourceEntry<Network, NetworkSpec, NetworkStatus>
            entry = state.Ledger.Upsert(
                metadata,
                spec,
                status,
                NetworkShape);
        status = status with { Handle = entry.TargetHandle };
        state.Ledger.Upsert(
            metadata,
            spec,
            status,
            NetworkShape);
        state.StoreEngineNetwork(resourceKey, realized);
        return status;
    }

    public async ValueTask<NetworkStatus> GetStatusAsync(
        ResourceRef<Network> network,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<Network, NetworkSpec, NetworkStatus>>
            lookup = state.Ledger.TryGet<
                Network,
                NetworkSpec,
                NetworkStatus>(network);
        if (!lookup.Succeeded)
            return FailedNetwork(
                Metadata(network),
                lookup.Diagnostic!.Code.Value,
                lookup.Diagnostic.Message);
        string resourceKey =
            Key<Network>(network.Scope, network.Id.Value);
        if (!state.IsNetworkResourceBoundToCurrentEngine(
                resourceKey))
            return lookup.Entry!.Status with
            {
                Phase = ResourcePhase.Failed,
                NetworkPhase = NetworkPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics =
                [
                    Error(
                        "LocalEnvironment.NetworkEngineIncarnationStale",
                        "The network belongs to a stale Local engine incarnation.")
                ],
            };
        LocalEngineNetworkObservation? expected =
            state.GetEngineNetwork(resourceKey);
        if (expected is null)
            return ExternalMutationFailure(
                lookup.Entry!.Status,
                "The provider lost the physical network observation for a logically owned network.");
        LocalEngineNetworkObservation? current;
        try
        {
            current = await engineClient.ObserveAsync(
                state.CurrentEngineSocketPath,
                expected.Id,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            return ExternalMutationFailure(
                lookup.Entry!.Status,
                Bounded(exception.Message));
        }
        if (current is null || !ExactlyMatches(expected, current))
            return ExternalMutationFailure(
                lookup.Entry!.Status,
                current is null
                    ? "The HPD-owned engine network no longer exists."
                    : "The HPD-owned engine network identity, labels, or immutable intent changed externally.");
        return lookup.Entry!.Status;
    }

    public async ValueTask DeleteNetworkAsync(
        ResourceRef<Network> network,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string networkKey =
            Key<Network>(network.Scope, network.Id.Value);
        LocalEngineNetworkObservation? physical =
            state.GetEngineNetwork(networkKey);
        if (physical is not null)
        {
            if (!state.IsNetworkResourceBoundToCurrentEngine(
                    networkKey))
                throw new InvalidOperationException(
                    "LocalEnvironment.NetworkEngineIncarnationStale: refusing to delete physical network ownership from a stale engine incarnation.");
            await engineClient.DeleteAsync(
                state.CurrentEngineSocketPath,
                physical,
                cancellationToken).ConfigureAwait(false);
        }
        foreach (ProviderResourceEntry<
                     NetworkMembership,
                     NetworkMembershipSpec,
                     NetworkMembershipStatus> membership in
                 state.Ledger.List<
                     NetworkMembership,
                     NetworkMembershipSpec,
                     NetworkMembershipStatus>(network.Scope)
                     .Where(entry =>
                         entry.Spec.Network.Id == network.Id))
        {
            state.ReleaseNetworkResource(
                Key<NetworkMembership>(
                    membership.Resource.Scope,
                    membership.Resource.Id.Value));
            state.Ledger.Remove<
                NetworkMembership,
                NetworkMembershipSpec,
                NetworkMembershipStatus>(membership.Resource);
        }
        state.ForgetEngineNetwork(networkKey);
        state.Ledger.Remove<Network, NetworkSpec, NetworkStatus>(
            network);
    }

    private Diagnostic? ValidateEngineAuthority(
        NetworkRealizationContext? realizationContext)
    {
        if (realizationContext is not
            {
                OwnerExecutionUnit: var owner,
                EngineAuthority: var authority,
            })
            return Error(
                "LocalEnvironment.NetworkAuthorityRequired",
                "Physical Local network realization requires one exact execution-unit owner and current engine-authority binding.");
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> unit = state.Ledger.TryGet<
                    ExecutionUnit,
                    ExecutionUnitSpec,
                    ExecutionUnitStatus>(owner);
        if (!unit.Succeeded ||
            unit.Entry!.Status.UnitPhase is not (
                ExecutionUnitPhase.Ready or
                ExecutionUnitPhase.Running))
            return Error(
                "LocalEnvironment.NetworkOwnerStale",
                unit.Diagnostic?.Message ??
                "The network execution-unit owner is missing or stale.");
        ProviderLedgerLookup<
            ProviderResourceEntry<
                AuthorityBinding,
                AuthorityBindingSpec,
                AuthorityBindingStatus>> binding = state.Ledger.TryGet<
                    AuthorityBinding,
                    AuthorityBindingSpec,
                    AuthorityBindingStatus>(authority);
        if (!binding.Succeeded ||
            binding.Entry!.Status.BindingPhase !=
                AuthorityBindingPhase.Projected ||
            !state.IsAuthorityBoundToCurrentEngine(
                authority.Id.Value) ||
            binding.Entry.Status.BoundAuthority?.ExpiresAt is
                { } expires &&
                expires <= DateTimeOffset.UtcNow ||
            binding.Entry.Spec.Target.Unit is not { } target ||
            !string.Equals(
                target.Route.BackingResourceId,
                owner.Id.Value,
                StringComparison.Ordinal))
            return Error(
                "LocalEnvironment.NetworkAuthorityStale",
                binding.Diagnostic?.Message ??
                "The network engine-authority binding is missing, revoked, stale, or belongs to another execution unit.");
        return null;
    }

    private static string PhysicalName(
        ResourceMetadata<Network> metadata,
        NetworkSpec spec)
    {
        string input = string.Join(
            '\n',
            metadata.Scope.Value,
            metadata.Id.Value,
            metadata.Generation.Value,
            spec.ReconciliationKey?.Value ?? string.Empty);
        string suffix = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant()[..24];
        return $"hpd-{suffix}";
    }

    private IReadOnlyDictionary<string, string> OwnershipLabels(
        ResourceMetadata<Network> metadata,
        NetworkSpec spec) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["io.hpd.owner"] = "hpdos",
            ["io.hpd.provider"] = ProviderId.Value,
            ["io.hpd.scope"] = metadata.Scope.Value,
            ["io.hpd.resource-id"] = metadata.Id.Value,
            ["io.hpd.resource-generation"] =
                metadata.Generation.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            ["io.hpd.network-intent"] =
                spec.ConnectivityIntent.ToString(),
        };

    private static string Bounded(string message) =>
        message.Length <= 512 ? message : message[..512];

    private static bool ExactlyMatches(
        LocalEngineNetworkObservation expected,
        LocalEngineNetworkObservation current) =>
        string.Equals(expected.Id, current.Id, StringComparison.Ordinal) &&
        string.Equals(
            expected.Name,
            current.Name,
            StringComparison.Ordinal) &&
        expected.Internal == current.Internal &&
        expected.Labels.Count == current.Labels.Count &&
        expected.Labels.All(label =>
            current.Labels.TryGetValue(label.Key, out string? value) &&
            string.Equals(value, label.Value, StringComparison.Ordinal));

    private NetworkStatus ExternalMutationFailure(
        NetworkStatus status,
        string message) =>
        status with
        {
            Phase = ResourcePhase.Failed,
            NetworkPhase = NetworkPhase.Failed,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Diagnostics =
            [
                Error(
                    "LocalEnvironment.NetworkExternalMutationDetected",
                    message)
            ],
        };

    public ValueTask<NetworkMembershipStatus> EnsureMembershipAsync(
        ResourceMetadata<NetworkMembership> metadata,
        NetworkMembershipSpec spec,
        NetworkMembershipStatus? observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<Network, NetworkSpec, NetworkStatus>>
            networkLookup = state.Ledger.TryGet<
                Network,
                NetworkSpec,
                NetworkStatus>(spec.Network);
        if (!networkLookup.Succeeded ||
            !state.IsNetworkResourceBoundToCurrentEngine(
                Key<Network>(
                    spec.Network.Scope,
                    spec.Network.Id.Value)))
            return ValueTask.FromResult(FailedMembership(
                metadata,
                networkLookup.Diagnostic?.Code.Value ??
                    "LocalEnvironment.NetworkMembershipNetworkStale",
                networkLookup.Diagnostic?.Message ??
                    "The membership network is missing or belongs to a stale engine incarnation."));
        if (!TargetIsOwnedAndReady(spec.Target))
            return ValueTask.FromResult(FailedMembership(
                metadata,
                "LocalEnvironment.NetworkMembershipTargetNotReady",
                "The network-membership target is not an owned, ready Local resource."));
        if (spec.RequestedAddress is not null ||
            spec.RequestedMacAddress is not null ||
            spec.RequestedMtu is not null)
            return ValueTask.FromResult(FailedMembership(
                metadata,
                "LocalEnvironment.NetworkMembershipStaticConfigurationUnsupported",
                "The first Local network slice does not accept package-selected addresses, MAC addresses, or MTUs."));

        ResourceRef<NetworkMembership> membershipRef = new(
            metadata.Id,
            metadata.Scope,
            metadata.Generation);
        IReadOnlyList<DiscoveryRecord> records =
            spec.ServiceNames
                .OrderBy(
                    static service => service.Value,
                    StringComparer.Ordinal)
                .Select(service => new DiscoveryRecord(
                    new DnsName(service.Value),
                    DiscoveryRecordKind.Service,
                    new DiscoveryRecordTarget(
                        Address: null,
                        ServiceName: service,
                        Membership: membershipRef,
                        Port: null,
                        Transport: NetworkTransport.Tcp,
                        CanonicalName: spec.Hostname is { } hostname
                            ? new DnsName(hostname.Value)
                            : null),
                    DerivedServiceDiscovery.DefaultTtl,
                    IsDerivedFromMembership: true))
                .ToArray();
        var status = new NetworkMembershipStatus
        {
            Phase = ResourcePhase.Ready,
            MembershipPhase = NetworkMembershipPhase.Ready,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            EndpointHandle = new NetworkEndpointHandle(
                $"local-network-membership:{metadata.Id.Value}"),
            RegisteredRecords = records,
        };
        ProviderResourceEntry<
            NetworkMembership,
            NetworkMembershipSpec,
            NetworkMembershipStatus> entry =
            state.Ledger.Upsert(
                metadata,
                spec,
                status,
                MembershipShape);
        status = status with { Handle = entry.TargetHandle };
        state.Ledger.Upsert(
            metadata,
            spec,
            status,
            MembershipShape);
        state.BindNetworkResourceToCurrentEngine(
            Key<NetworkMembership>(
                metadata.Scope,
                metadata.Id.Value));
        return ValueTask.FromResult(status);
    }

    public ValueTask<NetworkMembershipStatus>
        GetMembershipStatusAsync(
            ResourceRef<NetworkMembership> membership,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                NetworkMembership,
                NetworkMembershipSpec,
                NetworkMembershipStatus>> lookup =
            state.Ledger.TryGet<
                NetworkMembership,
                NetworkMembershipSpec,
                NetworkMembershipStatus>(membership);
        if (!lookup.Succeeded)
            return ValueTask.FromResult(FailedMembership(
                MembershipMetadata(membership),
                lookup.Diagnostic!.Code.Value,
                lookup.Diagnostic.Message));
        if (!state.IsNetworkResourceBoundToCurrentEngine(
                Key<NetworkMembership>(
                    membership.Scope,
                    membership.Id.Value)))
            return ValueTask.FromResult(lookup.Entry!.Status with
            {
                Phase = ResourcePhase.Failed,
                MembershipPhase = NetworkMembershipPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics =
                [
                    Error(
                        "LocalEnvironment.NetworkMembershipEngineIncarnationStale",
                        "The network membership belongs to a stale Local engine incarnation.")
                ],
            });
        return ValueTask.FromResult(lookup.Entry!.Status);
    }

    public ValueTask ReleaseMembershipAsync(
        ResourceRef<NetworkMembership> membership,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.ReleaseNetworkResource(
            Key<NetworkMembership>(
                membership.Scope,
                membership.Id.Value));
        state.Ledger.Remove<
            NetworkMembership,
            NetworkMembershipSpec,
            NetworkMembershipStatus>(membership);
        return ValueTask.CompletedTask;
    }

    private bool TargetIsOwnedAndReady(
        NetworkMembershipTarget target) =>
        target.Kind switch
        {
            NetworkMembershipTargetKind.RuntimeHost
                when target.Host is { } host =>
                state.Ledger.TryGet<
                        RuntimeHost,
                        RuntimeHostSpec,
                        RuntimeHostStatus>(host)
                    is { Succeeded: true, Entry.Status.HostPhase:
                        RuntimeHostPhase.Ready },
            NetworkMembershipTargetKind.ExecutionUnit
                when target.Unit is { } unit =>
                state.Ledger.TryGet<
                        ExecutionUnit,
                        ExecutionUnitSpec,
                        ExecutionUnitStatus>(unit)
                    is { Succeeded: true, Entry.Status.UnitPhase:
                        ExecutionUnitPhase.Ready or
                        ExecutionUnitPhase.Running },
            NetworkMembershipTargetKind.ProcessInvocation
                when target.Process is { } process =>
                state.Ledger.TryGet<
                        ProcessInvocation,
                        ProcessInvocationSpec,
                        ProcessInvocationStatus>(process)
                    is { Succeeded: true },
            _ => false,
        };

    private Diagnostic? ValidateNetwork(NetworkSpec spec)
    {
        if (spec.Scope is
            NetworkScope.Host or
            NetworkScope.Shared or
            NetworkScope.ProviderDefined)
            return Error(
                "LocalEnvironment.NetworkScopeUnsupported",
                "Local networks must be runtime, project, or execution-unit scoped.");
        if (spec.ConnectivityIntent is
            NetworkConnectivityIntent.Routed or
            NetworkConnectivityIntent.ProviderDefined)
            return Error(
                "LocalEnvironment.NetworkConnectivityUnsupported",
                "The first Local slice supports isolated, peer-reachable, or NAT-egress networks.");
        if ((spec.AddressFamilies &
             AddressFamilyRequirement.IPv6Required) != 0)
            return Error(
                "LocalEnvironment.NetworkIpv6RequiredUnsupported",
                "The first Local slice cannot guarantee IPv6.");
        if (spec.CidrHints.Count > 0)
            return Error(
                "LocalEnvironment.NetworkCidrHintsUnsupported",
                "Package-selected network CIDRs are not supported.");
        if (spec.DiscoveryPolicy.RequestHostDnsExport ||
            spec.DiscoveryPolicy.RequestHostResolverImport)
            return Error(
                "LocalEnvironment.NetworkHostResolverBoundaryRejected",
                "Local networks do not modify or import the host resolver.");
        if (spec.ExposurePolicy.AllowHostVisibleAddresses ||
            !spec.ExposurePolicy.RequireExplicitPublication)
            return Error(
                "LocalEnvironment.NetworkHostExposureRejected",
                "Local networks require explicit HPD-owned endpoint publication and do not expose engine addresses.");
        return null;
    }

    private NetworkStatus FailedNetwork(
        ResourceMetadata<Network> metadata,
        string code,
        string message) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            NetworkPhase = NetworkPhase.Failed,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Rejected,
            Diagnostics = [Error(code, message)],
        };

    private NetworkMembershipStatus FailedMembership(
        ResourceMetadata<NetworkMembership> metadata,
        string code,
        string message) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            MembershipPhase = NetworkMembershipPhase.Failed,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Rejected,
            Diagnostics = [Error(code, message)],
        };

    private Diagnostic Error(string code, string message) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode(code),
            Message = message,
            ProviderId = ProviderId,
        };

    private static string Key<TResource>(
        ResourceScope scope,
        string id) =>
        $"{typeof(TResource).Name}:{scope}:{id}";

    private static ResourceMetadata<Network> Metadata(
        ResourceRef<Network> resource) =>
        new()
        {
            Id = resource.Id,
            Kind = new ResourceKind("Network"),
            Scope = resource.Scope,
            Generation =
                resource.Generation ?? new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };

    private static ResourceMetadata<NetworkMembership>
        MembershipMetadata(
            ResourceRef<NetworkMembership> resource) =>
        new()
        {
            Id = resource.Id,
            Kind = new ResourceKind("NetworkMembership"),
            Scope = resource.Scope,
            Generation =
                resource.Generation ?? new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };
}
