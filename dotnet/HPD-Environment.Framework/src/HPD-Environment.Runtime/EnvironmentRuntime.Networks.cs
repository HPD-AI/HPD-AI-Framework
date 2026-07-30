#nullable enable

namespace HPD.Environment.Runtime;

using System.Security.Cryptography;
using System.Text.Json;
using HPD.Environment.Contracts;

public sealed partial class InMemoryEnvironmentRuntime
{
    private readonly Dictionary<string, OwnedNetwork> _networks =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _networkIdsByIdentity =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnedNetworkMembership>
        _networkMemberships = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _membershipIdsByIdentity =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnedServiceDiscovery>
        _serviceDiscoveries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _discoveryIdsByIdentity =
        new(StringComparer.Ordinal);

    public async ValueTask<
        ResourceSnapshot<Network, NetworkSpec, NetworkStatus>>
        EnsureNetworkAsync(
            NetworkSpec spec,
            NetworkRealizationContext? realizationContext = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ProviderId owner = CurrentHostProvider();
            if (spec.PreferredProvider is { } preferred &&
                preferred != owner)
                throw OwnershipFailure(
                    "hpd.environment.network.provider-migration-requires-replacement",
                    $"The current runtime host is owned by '{owner.Value}', not requested network provider '{preferred.Value}'.");
            INetworkProvider provider = ProviderById(
                registry.NetworkProviders,
                owner,
                "network");
            string fingerprint = Fingerprint(spec);
            string identity = spec.ReconciliationKey is { } key
                ? $"{CurrentScope().Value}:key:{RequireIdentityKey(key)}"
                : $"{CurrentScope().Value}:spec:{fingerprint}";
            OwnedNetwork? existing = FindByIdentity(
                _networkIdsByIdentity,
                _networks,
                identity,
                "network");
            ResourceMetadata<Network> metadata = existing is null
                ? Metadata<Network>("network") with
                {
                    Lifetime = ResourceLifetime.Runtime,
                    OwnerRefs =
                        [Untyped(Ref(_host!.Snapshot.Metadata))],
                }
                : existing.Snapshot.Metadata;
            NetworkStatus status =
                await provider.EnsureNetworkAsync(
                        metadata,
                        spec,
                        realizationContext,
                        existing?.Snapshot.Status,
                        cancellationToken)
                    .ConfigureAwait(false);
            var snapshot =
                new ResourceSnapshot<
                    Network,
                    NetworkSpec,
                    NetworkStatus>(
                    metadata,
                    spec,
                    status);
            if (status.ReconciliationOutcome ==
                ResourceReconciliationOutcome.Accepted)
            {
                _networks[metadata.Id.Value] = new(
                    owner,
                    identity,
                    fingerprint,
                    snapshot);
                _networkIdsByIdentity[identity] =
                    metadata.Id.Value;
            }
            return snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<
        ResourceSnapshot<Network, NetworkSpec, NetworkStatus>>
        GetNetworkAsync(
            ResourceRef<Network> network,
            CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            OwnedNetwork owned = FindNetwork(network);
            NetworkStatus status = await ProviderById(
                    registry.NetworkProviders,
                    owned.ProviderId,
                    "network")
                .GetStatusAsync(network, cancellationToken)
                .ConfigureAwait(false);
            owned = owned with
            {
                Snapshot = owned.Snapshot with
                {
                    Status = status,
                },
            };
            _networks[network.Id.Value] = owned;
            return owned.Snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask DeleteNetworkAsync(
        ResourceRef<Network> network,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            OwnedNetwork owned = FindNetwork(network);
            if (_networkMemberships.Values.Any(membership =>
                    SameResource(
                        membership.Snapshot.Spec.Network,
                        network)) ||
                _serviceDiscoveries.Values.Any(discovery =>
                    discovery.Snapshot.Spec.Network is { } candidate &&
                    SameResource(candidate, network)) ||
                _publishedEndpoints.Values.Any(endpoint =>
                    endpoint.Snapshot.Spec.RoutingNetwork is { } candidate &&
                    SameResource(candidate, network)))
                throw OwnershipFailure(
                    "hpd.environment.network.dependents-active",
                    $"Network '{network.Id.Value}' still owns memberships, discovery, or endpoints.");
            await ProviderById(
                    registry.NetworkProviders,
                    owned.ProviderId,
                    "network")
                .DeleteNetworkAsync(network, cancellationToken)
                .ConfigureAwait(false);
            _networks.Remove(network.Id.Value);
            _networkIdsByIdentity.Remove(owned.Identity);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    private static string RequireIdentityKey(NetworkIdentityKey key)
    {
        string value = key.Value;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 256 ||
            value.Any(static character =>
                char.IsControl(character) ||
                char.IsWhiteSpace(character)))
            throw OwnershipFailure(
                "hpd.environment.network.identity-key-invalid",
                "A network reconciliation identity must contain 1-256 non-whitespace, non-control characters.");
        return value;
    }

    public async ValueTask<ResourceSnapshot<
        NetworkMembership,
        NetworkMembershipSpec,
        NetworkMembershipStatus>> EnsureNetworkMembershipAsync(
        NetworkMembershipSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            OwnedNetwork network = FindNetwork(spec.Network);
            ValidateMembershipTarget(spec.Target, network.ProviderId);
            string fingerprint = Fingerprint(spec);
            string identity =
                $"{spec.Network.Scope.Value}:{spec.Network.Id.Value}:{fingerprint}";
            OwnedNetworkMembership? existing = FindByIdentity(
                _membershipIdsByIdentity,
                _networkMemberships,
                identity,
                "network membership");
            ResourceMetadata<NetworkMembership> metadata =
                existing is null
                    ? Metadata<NetworkMembership>(
                        "network-membership") with
                    {
                        Lifetime =
                            ResourceLifetime.ExecutionUnit,
                        OwnerRefs = [Untyped(spec.Network)],
                    }
                    : existing.Snapshot.Metadata;
            NetworkMembershipStatus status =
                await ProviderById(
                        registry.NetworkMembershipProviders,
                        network.ProviderId,
                        "network membership")
                    .EnsureMembershipAsync(
                        metadata,
                        spec,
                        existing?.Snapshot.Status,
                        cancellationToken)
                    .ConfigureAwait(false);
            var snapshot = new ResourceSnapshot<
                NetworkMembership,
                NetworkMembershipSpec,
                NetworkMembershipStatus>(
                metadata,
                spec,
                status);
            if (status.ReconciliationOutcome ==
                ResourceReconciliationOutcome.Accepted)
            {
                _networkMemberships[metadata.Id.Value] =
                    new(
                        network.ProviderId,
                        identity,
                        fingerprint,
                        snapshot);
                _membershipIdsByIdentity[identity] =
                    metadata.Id.Value;
            }
            return snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<
        NetworkMembership,
        NetworkMembershipSpec,
        NetworkMembershipStatus>> GetNetworkMembershipAsync(
        ResourceRef<NetworkMembership> membership,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            OwnedNetworkMembership owned =
                FindNetworkMembership(membership);
            NetworkMembershipStatus status =
                await ProviderById(
                        registry.NetworkMembershipProviders,
                        owned.ProviderId,
                        "network membership")
                    .GetMembershipStatusAsync(
                        membership,
                        cancellationToken)
                    .ConfigureAwait(false);
            owned = owned with
            {
                Snapshot = owned.Snapshot with
                {
                    Status = status,
                },
            };
            _networkMemberships[membership.Id.Value] = owned;
            return owned.Snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask ReleaseNetworkMembershipAsync(
        ResourceRef<NetworkMembership> membership,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            OwnedNetworkMembership owned =
                FindNetworkMembership(membership);
            if (_serviceDiscoveries.Values.Any(discovery =>
                    discovery.Snapshot.Status.Records.Any(record =>
                        record.Target.Membership is { } target &&
                        SameResource(target, membership))) ||
                _publishedEndpoints.Values.Any(endpoint =>
                    endpoint.Snapshot.Spec.Target.Membership is { } target &&
                    SameResource(target, membership)))
                throw OwnershipFailure(
                    "hpd.environment.network-membership.dependents-active",
                    $"Network membership '{membership.Id.Value}' still owns discovery records or endpoints.");
            await ProviderById(
                    registry.NetworkMembershipProviders,
                    owned.ProviderId,
                    "network membership")
                .ReleaseMembershipAsync(
                    membership,
                    cancellationToken)
                .ConfigureAwait(false);
            _networkMemberships.Remove(membership.Id.Value);
            _membershipIdsByIdentity.Remove(owned.Identity);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<
        ServiceDiscovery,
        ServiceDiscoverySpec,
        ServiceDiscoveryStatus>> EnsureServiceDiscoveryAsync(
        ServiceDiscoverySpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ProviderId owner = DiscoveryOwner(spec);
            string fingerprint = Fingerprint(spec);
            string identity =
                $"{CurrentScope().Value}:{fingerprint}";
            OwnedServiceDiscovery? existing = FindByIdentity(
                _discoveryIdsByIdentity,
                _serviceDiscoveries,
                identity,
                "service discovery");
            ResourceMetadata<ServiceDiscovery> metadata =
                existing is null
                    ? Metadata<ServiceDiscovery>(
                        "service-discovery") with
                    {
                        Lifetime = ResourceLifetime.Runtime,
                        OwnerRefs = spec.Network is { } network
                            ? [Untyped(network)]
                            : spec.Host is { } host
                                ? [Untyped(host)]
                                : [],
                    }
                    : existing.Snapshot.Metadata;
            ServiceDiscoveryStatus status =
                await ProviderById(
                        registry.ServiceDiscoveryProviders,
                        owner,
                        "service discovery")
                    .EnsureServiceDiscoveryAsync(
                        metadata,
                        spec,
                        existing?.Snapshot.Status,
                        cancellationToken)
                    .ConfigureAwait(false);
            var snapshot = new ResourceSnapshot<
                ServiceDiscovery,
                ServiceDiscoverySpec,
                ServiceDiscoveryStatus>(
                metadata,
                spec,
                status);
            if (status.ReconciliationOutcome ==
                ResourceReconciliationOutcome.Accepted)
            {
                _serviceDiscoveries[metadata.Id.Value] =
                    new(owner, identity, fingerprint, snapshot);
                _discoveryIdsByIdentity[identity] =
                    metadata.Id.Value;
            }
            return snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<
        ServiceDiscovery,
        ServiceDiscoverySpec,
        ServiceDiscoveryStatus>> GetServiceDiscoveryAsync(
        ResourceRef<ServiceDiscovery> discovery,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            OwnedServiceDiscovery owned =
                FindServiceDiscovery(discovery);
            ServiceDiscoveryStatus status =
                await ProviderById(
                        registry.ServiceDiscoveryProviders,
                        owned.ProviderId,
                        "service discovery")
                    .GetStatusAsync(
                        discovery,
                        cancellationToken)
                    .ConfigureAwait(false);
            owned = owned with
            {
                Snapshot = owned.Snapshot with
                {
                    Status = status,
                },
            };
            _serviceDiscoveries[discovery.Id.Value] = owned;
            return owned.Snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<DiscoveryRecord>>
        ResolveServiceDiscoveryAsync(
            ServiceDiscoveryQuery query,
            CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            OwnedServiceDiscovery owned =
                FindServiceDiscovery(query.Discovery);
            return await ProviderById(
                    registry.ServiceDiscoveryProviders,
                    owned.ProviderId,
                    "service discovery")
                .ResolveAsync(query, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask ReleaseServiceDiscoveryAsync(
        ResourceRef<ServiceDiscovery> discovery,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            OwnedServiceDiscovery owned =
                FindServiceDiscovery(discovery);
            await ProviderById(
                    registry.ServiceDiscoveryProviders,
                    owned.ProviderId,
                    "service discovery")
                .ReleaseAsync(discovery, cancellationToken)
                .ConfigureAwait(false);
            _serviceDiscoveries.Remove(discovery.Id.Value);
            _discoveryIdsByIdentity.Remove(owned.Identity);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    private ProviderId CurrentHostProvider() =>
        _host?.ProviderId ??
        throw OwnershipFailure(
            "hpd.environment.runtime-host.unknown",
            "A runtime host must be owned before creating network resources.");

    private ResourceScope CurrentScope() =>
        _host?.Snapshot.Metadata.Scope ??
        throw OwnershipFailure(
            "hpd.environment.runtime-host.unknown",
            "A runtime host must be owned before creating network resources.");

    private OwnedNetwork[] NetworksForCurrentHost() =>
        _host is null
            ? []
            : _networks.Values.Where(network =>
                network.Snapshot.Metadata.Scope ==
                    _host.Snapshot.Metadata.Scope &&
                network.ProviderId == _host.ProviderId)
                .ToArray();

    private OwnedNetworkMembership[]
        NetworkMembershipsForCurrentHost() =>
        _host is null
            ? []
            : _networkMemberships.Values.Where(membership =>
                membership.Snapshot.Metadata.Scope ==
                    _host.Snapshot.Metadata.Scope &&
                membership.ProviderId == _host.ProviderId)
                .ToArray();

    private OwnedServiceDiscovery[]
        ServiceDiscoveriesForCurrentHost() =>
        _host is null
            ? []
            : _serviceDiscoveries.Values.Where(discovery =>
                discovery.Snapshot.Metadata.Scope ==
                    _host.Snapshot.Metadata.Scope &&
                discovery.ProviderId == _host.ProviderId)
                .ToArray();

    private ProviderId DiscoveryOwner(ServiceDiscoverySpec spec)
    {
        if (spec.Network is { } network)
            return FindNetwork(network).ProviderId;
        if (spec.Host is { } host)
            return HostOwner(host);
        return CurrentHostProvider();
    }

    private void ValidateMembershipTarget(
        NetworkMembershipTarget target,
        ProviderId networkProvider)
    {
        ProviderId owner = target.Kind switch
        {
            NetworkMembershipTargetKind.RuntimeHost
                when target.Host is { } host &&
                     _host?.Snapshot.Status.Handle == host =>
                _host.ProviderId,
            NetworkMembershipTargetKind.ExecutionUnit
                when target.Unit is { } unit =>
                FindUnit(unit).ProviderId,
            NetworkMembershipTargetKind.ProcessInvocation
                when target.Process is { } process =>
                _processes.Values.FirstOrDefault(candidate =>
                    candidate.Snapshot.Status.Handle == process)
                    ?.ProviderId ??
                throw OwnershipFailure(
                    "hpd.environment.network-membership.process-unknown",
                    "The network-membership process target is unknown."),
            _ => throw OwnershipFailure(
                "hpd.environment.network-membership.target-unknown",
                "The network-membership target is unknown or unsupported."),
        };
        if (owner != networkProvider)
            throw OwnershipFailure(
                "hpd.environment.network-membership.provider-conflict",
                "A network membership cannot cross provider ownership.");
    }

    private void ValidateEndpointOwnership(
        PublishedEndpointSpec spec,
        ProviderId hostProvider)
    {
        if (spec.RoutingHost is { } host &&
            HostOwner(host) != hostProvider)
            throw OwnershipFailure(
                "hpd.environment.published-endpoint.host-provider-conflict",
                "The endpoint routing host is owned by another provider.");
        if (spec.RoutingNetwork is { } network &&
            FindNetwork(network).ProviderId != hostProvider)
            throw OwnershipFailure(
                "hpd.environment.published-endpoint.network-provider-conflict",
                "The endpoint routing network is owned by another provider.");

        ProviderId? targetProvider = spec.Target.Kind switch
        {
            EndpointTargetKind.NetworkMembership
                when spec.Target.Membership is { } membership =>
                FindNetworkMembership(membership).ProviderId,
            EndpointTargetKind.UnitPort
                when spec.Target.Unit is { } unit =>
                FindUnit(unit).ProviderId,
            EndpointTargetKind.ProcessPort
                when spec.Target.Process is { } process =>
                FindProcess(process).ProviderId,
            _ => null,
        };
        if (targetProvider is { } owner &&
            owner != hostProvider)
            throw OwnershipFailure(
                "hpd.environment.published-endpoint.target-provider-conflict",
                "The endpoint target is owned by another provider.");
        if (spec.Target.Kind ==
                EndpointTargetKind.NetworkMembership &&
            spec.Target.Membership is null ||
            spec.Target.Kind == EndpointTargetKind.UnitPort &&
            spec.Target.Unit is null ||
            spec.Target.Kind == EndpointTargetKind.ProcessPort &&
            spec.Target.Process is null)
            throw OwnershipFailure(
                "hpd.environment.published-endpoint.target-reference-required",
                "The endpoint target kind requires an owned resource reference.");
    }

    private OwnedNetwork FindNetwork(ResourceRef<Network> network)
    {
        if (!_networks.TryGetValue(
                network.Id.Value,
                out OwnedNetwork? owned) ||
            owned.Snapshot.Metadata.Scope != network.Scope)
            throw OwnershipFailure(
                "hpd.environment.network.unknown",
                $"Network '{network.Id.Value}' is not owned by this runtime.");
        ValidateRef(
            network,
            owned.Snapshot.Metadata,
            "network");
        return owned;
    }

    private OwnedNetworkMembership FindNetworkMembership(
        ResourceRef<NetworkMembership> membership)
    {
        if (!_networkMemberships.TryGetValue(
                membership.Id.Value,
                out OwnedNetworkMembership? owned) ||
            owned.Snapshot.Metadata.Scope != membership.Scope)
            throw OwnershipFailure(
                "hpd.environment.network-membership.unknown",
                $"Network membership '{membership.Id.Value}' is not owned by this runtime.");
        ValidateRef(
            membership,
            owned.Snapshot.Metadata,
            "network membership");
        return owned;
    }

    private OwnedServiceDiscovery FindServiceDiscovery(
        ResourceRef<ServiceDiscovery> discovery)
    {
        if (!_serviceDiscoveries.TryGetValue(
                discovery.Id.Value,
                out OwnedServiceDiscovery? owned) ||
            owned.Snapshot.Metadata.Scope != discovery.Scope)
            throw OwnershipFailure(
                "hpd.environment.service-discovery.unknown",
                $"Service discovery '{discovery.Id.Value}' is not owned by this runtime.");
        ValidateRef(
            discovery,
            owned.Snapshot.Metadata,
            "service discovery");
        return owned;
    }

    private static TOwned? FindByIdentity<TOwned>(
        IReadOnlyDictionary<string, string> ids,
        IReadOnlyDictionary<string, TOwned> resources,
        string identity,
        string family)
        where TOwned : class
    {
        if (!ids.TryGetValue(identity, out string? id))
            return null;
        if (resources.TryGetValue(id, out TOwned? owned))
            return owned;
        throw OwnershipFailure(
            $"hpd.environment.{family.Replace(' ', '-')}.identity-corrupt",
            $"The {family} identity refers to missing runtime ownership.");
    }

    private static string Fingerprint(NetworkSpec spec) =>
        Hash(JsonSerializer.SerializeToUtf8Bytes(
            spec,
            RuntimeSpecJsonContext.Default.NetworkSpec));

    private static string Fingerprint(
        NetworkMembershipSpec spec) =>
        Hash(JsonSerializer.SerializeToUtf8Bytes(
            spec,
            RuntimeSpecJsonContext.Default.NetworkMembershipSpec));

    private static string Fingerprint(
        ServiceDiscoverySpec spec) =>
        Hash(JsonSerializer.SerializeToUtf8Bytes(
            spec,
            RuntimeSpecJsonContext.Default.ServiceDiscoverySpec));

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value));

    private sealed record OwnedNetwork(
        ProviderId ProviderId,
        string Identity,
        string SpecFingerprint,
        ResourceSnapshot<Network, NetworkSpec, NetworkStatus>
            Snapshot);

    private sealed record OwnedNetworkMembership(
        ProviderId ProviderId,
        string Identity,
        string SpecFingerprint,
        ResourceSnapshot<
            NetworkMembership,
            NetworkMembershipSpec,
            NetworkMembershipStatus> Snapshot);

    private sealed record OwnedServiceDiscovery(
        ProviderId ProviderId,
        string Identity,
        string SpecFingerprint,
        ResourceSnapshot<
            ServiceDiscovery,
            ServiceDiscoverySpec,
            ServiceDiscoveryStatus> Snapshot);
}
