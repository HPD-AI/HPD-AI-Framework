namespace HPD.Environment.AppleVirtualization.State;

using System.Globalization;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.Contracts;

internal enum AppleVirtualizationLedgerResourceKind
{
    RuntimeHost,
    ExecutionUnit,
    ContentProjection,
    ProcessInvocation,
    Network,
    NetworkMembership,
    ServiceDiscovery,
    PublishedEndpoint,
    AuthorityBinding,
    EngineControlPlane,
}

internal enum AppleVirtualizationHostEmptyPolicyAction
{
    Active,
    Retain,
    IdleRetentionPending,
    StopNow,
}

internal readonly record struct AppleVirtualizationResourceKey(ResourceScope Scope, string Id);
internal readonly record struct AppleVirtualizationHostEngineKey(
    ResourceScope Scope,
    string RuntimeHostId,
    string EngineId);

internal sealed record AppleVirtualizationNetworkMembershipSnapshot(
    ResourceRef<NetworkMembership> Resource,
    NetworkMembershipStatus Status,
    NetworkMembershipSpec Spec);

internal readonly record struct AppleVirtualizationRuntimeHostPolicySnapshot(
    LifecyclePolicy LifecyclePolicy,
    RuntimeTopologyPolicy TopologyPolicy)
{
    public static AppleVirtualizationRuntimeHostPolicySnapshot Default { get; } = new(
        LifecyclePolicy.Default,
        new RuntimeTopologyPolicy());
}

internal sealed record AppleVirtualizationHostEmptyPolicyEvaluation(
    AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>? Host,
    AppleVirtualizationRuntimeHostPolicySnapshot Policy,
    AppleVirtualizationHostEmptyPolicyAction Action,
    int ActiveUnitCount,
    Diagnostic? Diagnostic);

internal sealed record AppleVirtualizationLedgerEntry<TResource, TStatus>(
    ResourceRef<TResource> Resource,
    TargetHandle<TResource> TargetHandle,
    ProviderOpaqueHandle ProviderHandle,
    TStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    AppleVirtualizationLedgerResourceKind Kind,
    ulong ProviderGeneration)
    where TResource : IExecutionResourceMarker, IOperationTargetMarker
    where TStatus : ResourceStatus;

internal readonly record struct AppleVirtualizationLedgerLookup<TEntry>(TEntry? Entry, Diagnostic? Diagnostic)
    where TEntry : class
{
    public bool Succeeded => Diagnostic is null && Entry is not null;
}

internal sealed class AppleVirtualizationProviderStateLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> _hostsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> _unitsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>> _projectionsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus>> _processesByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<Network, NetworkStatus>> _networksByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>> _membershipsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<ServiceDiscovery, ServiceDiscoveryStatus>> _serviceDiscoveriesByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<PublishedEndpoint, PublishedEndpointStatus>> _publishedEndpointsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus>> _authorityBindingsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<EngineControlPlane, EngineControlPlaneStatus>> _enginesByResource = [];
    private readonly Dictionary<string, HandleIndexEntry> _handlesByToken = new(StringComparer.Ordinal);
    private readonly Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationRuntimeHostPolicySnapshot> _hostPoliciesByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, string> _hostConfigurationFingerprintsByResource = [];
    private readonly Dictionary<AppleVirtualizationHostEngineKey, AppleVirtualizationGuestAgentEngineGenerationStamp> _hostEngineGenerationsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, ExecutionUnitSpec> _unitSpecsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, ContentProjectionSpec> _projectionSpecsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, NetworkSpec> _networkSpecsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, NetworkMembershipSpec> _membershipSpecsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, ServiceDiscoverySpec> _serviceDiscoverySpecsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, PublishedEndpointSpec> _publishedEndpointSpecsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, AuthorityBindingSpec> _authorityBindingSpecsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, EngineControlPlaneSpec> _engineSpecsByResource = [];
    private readonly Dictionary<AppleVirtualizationResourceKey, AuthorityAuditEvent[]> _authorityAuditEventsByResource = [];
    private ulong _providerGeneration = 1;
    private long _tokenSequence;
    private const int MaxAuthorityAuditEvents = 32;

    public AppleVirtualizationProviderStateLedger()
        : this(AppleVirtualizationProviderDescriptor.ProviderId)
    {
    }

    public AppleVirtualizationProviderStateLedger(ProviderId providerId)
    {
        ProviderId = providerId;
    }

    public ProviderId ProviderId { get; }

    public ulong ProviderGeneration
    {
        get
        {
            lock (_gate)
            {
                return _providerGeneration;
            }
        }
    }

    public ulong AdvanceProviderGeneration()
    {
        lock (_gate)
        {
            return ++_providerGeneration;
        }
    }

    public string? GetRuntimeHostConfigurationFingerprint(ResourceId<RuntimeHost> id, ResourceScope scope)
    {
        lock (_gate)
        {
            return _hostConfigurationFingerprintsByResource.GetValueOrDefault(ToKey(id, scope));
        }
    }

    public void SetRuntimeHostConfigurationFingerprint(
        ResourceId<RuntimeHost> id,
        ResourceScope scope,
        string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        lock (_gate)
        {
            _hostConfigurationFingerprintsByResource[ToKey(id, scope)] = fingerprint;
        }
    }

    public bool TryAcceptRuntimeHostEngineGeneration(
        ResourceId<RuntimeHost> id,
        ResourceScope scope,
        string engineId,
        AppleVirtualizationGuestAgentEngineGenerationStamp generation,
        ulong expectedProviderGeneration,
        ulong expectedHostStartGeneration,
        string? expectedGuestBootId,
        ulong? expectedGuestBootGeneration,
        bool requireEngineGeneration,
        out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        ArgumentNullException.ThrowIfNull(generation);
        lock (_gate)
        {
            if (expectedProviderGeneration != _providerGeneration)
            {
                reason = "The expected provider generation is no longer current.";
                return false;
            }
            if (generation.ProviderGeneration != expectedProviderGeneration)
            {
                reason = "The engine response provider generation does not match the current provider.";
                return false;
            }
            if (generation.HostStartGeneration != expectedHostStartGeneration)
            {
                reason = "The engine response host-start generation does not match the runtime host.";
                return false;
            }
            if (generation.GuestBootGeneration == 0 || generation.GuestAgentGeneration == 0)
            {
                reason = "Guest boot and guest-agent generations must be positive.";
                return false;
            }
            if (requireEngineGeneration && generation.EngineGeneration == 0)
            {
                reason = "A ready engine must report a positive engine generation.";
                return false;
            }
            if (expectedGuestBootGeneration is { } expectedGeneration &&
                generation.GuestBootGeneration != expectedGeneration)
            {
                reason = "The engine response guest-boot generation does not match the runtime host.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(expectedGuestBootId) &&
                !string.Equals(generation.GuestBootId, expectedGuestBootId, StringComparison.Ordinal))
            {
                reason = "The engine response guest-boot identity does not match the runtime host.";
                return false;
            }

            var key = new AppleVirtualizationHostEngineKey(scope, id.Value, engineId);
            if (_hostEngineGenerationsByResource.TryGetValue(key, out AppleVirtualizationGuestAgentEngineGenerationStamp? previous))
            {
                bool sameBoot = previous.GuestBootGeneration == generation.GuestBootGeneration &&
                    string.Equals(previous.GuestBootId, generation.GuestBootId, StringComparison.Ordinal);
                if (!sameBoot && expectedGuestBootGeneration is null)
                {
                    reason = "The engine response changed guest-boot identity without a matching runtime-host observation.";
                    return false;
                }
                if (sameBoot && generation.GuestAgentGeneration < previous.GuestAgentGeneration)
                {
                    reason = "The engine response guest-agent generation is stale.";
                    return false;
                }
                if (sameBoot &&
                    generation.EngineGeneration > 0 &&
                    generation.EngineGeneration < previous.EngineGeneration)
                {
                    reason = "The engine response engine generation is stale.";
                    return false;
                }
            }

            if (generation.EngineGeneration > 0)
            {
                _hostEngineGenerationsByResource[key] = generation;
            }
            reason = string.Empty;
            return true;
        }
    }

    public AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> UpsertRuntimeHost(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostStatus status,
        RuntimeHostSpec? spec = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(metadata.Id, metadata.Scope);
            if (spec is not null)
            {
                _hostPoliciesByResource[key] = new AppleVirtualizationRuntimeHostPolicySnapshot(
                    spec.LifecyclePolicy,
                    spec.TopologyPolicy);
            }

            AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>? previous = GetExisting(_hostsByResource, key);
            HandlePair<RuntimeHost> handles = GetOrCreateHandles(previous, AppleVirtualizationLedgerResourceKind.RuntimeHost, metadata);
            RuntimeHostStatus stored = status with
            {
                Handle = handles.TargetHandle,
                ProviderHandle = handles.ProviderHandle,
                ExecutionUnits = ActiveExecutionUnitsForRuntimeHostNoLock(
                    new ResourceRef<RuntimeHost>(metadata.Id, metadata.Scope, metadata.Generation)),
            };

            return Store(_hostsByResource, key, previous, handles, stored, metadata);
        }
    }

    public AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> UpsertExecutionUnit(
        ResourceMetadata<ExecutionUnit> metadata,
        ExecutionUnitStatus status,
        ExecutionUnitSpec? spec = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(metadata.Id, metadata.Scope);
            if (spec is not null)
            {
                _unitSpecsByResource[key] = spec;
            }

            AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? previous = GetExisting(_unitsByResource, key);
            HandlePair<ExecutionUnit> handles = GetOrCreateHandles(previous, AppleVirtualizationLedgerResourceKind.ExecutionUnit, metadata);
            ExecutionUnitStatus stored = status with
            {
                Handle = handles.TargetHandle,
                NamespaceHandle = handles.ProviderHandle,
            };

            AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> entry =
                Store(_unitsByResource, key, previous, handles, stored, metadata);
            RefreshHostExecutionUnitsForUnitTransitionNoLock(previous?.Status, stored);
            return entry;
        }
    }

    public AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> UpsertContentProjection(
        ResourceMetadata<ContentProjection> metadata,
        ContentProjectionStatus status,
        ContentProjectionSpec? spec = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(metadata.Id, metadata.Scope);
            if (spec is not null)
            {
                _projectionSpecsByResource[key] = spec;
            }

            AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>? previous = GetExisting(_projectionsByResource, key);
            HandlePair<ContentProjection> handles = GetOrCreateHandles(previous, AppleVirtualizationLedgerResourceKind.ContentProjection, metadata);
            ContentProjectionStatus stored = status with
            {
                ProviderHandle = handles.ProviderHandle,
            };

            return Store(_projectionsByResource, key, previous, handles, stored, metadata);
        }
    }

    public AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> UpsertProcessInvocation(
        ResourceMetadata<ProcessInvocation> metadata,
        ProcessInvocationStatus status)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(metadata.Id, metadata.Scope);
            AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus>? previous = GetExisting(_processesByResource, key);
            HandlePair<ProcessInvocation> handles = GetOrCreateHandles(previous, AppleVirtualizationLedgerResourceKind.ProcessInvocation, metadata);
            ProcessInvocationStatus stored = status with
            {
                Handle = handles.TargetHandle,
            };

            return Store(_processesByResource, key, previous, handles, stored, metadata);
        }
    }

    public AppleVirtualizationLedgerEntry<Network, NetworkStatus> UpsertNetwork(
        ResourceMetadata<Network> metadata,
        NetworkStatus status,
        NetworkSpec? spec = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(metadata.Id, metadata.Scope);
            if (spec is not null)
            {
                _networkSpecsByResource[key] = spec;
            }

            AppleVirtualizationLedgerEntry<Network, NetworkStatus>? previous = GetExisting(_networksByResource, key);
            HandlePair<Network> handles = GetOrCreateHandles(previous, AppleVirtualizationLedgerResourceKind.Network, metadata);
            NetworkStatus stored = status with
            {
                Handle = handles.TargetHandle,
            };

            return Store(_networksByResource, key, previous, handles, stored, metadata);
        }
    }

    public AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus> UpsertNetworkMembership(
        ResourceMetadata<NetworkMembership> metadata,
        NetworkMembershipStatus status,
        NetworkMembershipSpec? spec = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(metadata.Id, metadata.Scope);
            if (spec is not null)
            {
                _membershipSpecsByResource[key] = spec;
            }

            AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>? previous = GetExisting(_membershipsByResource, key);
            HandlePair<NetworkMembership> handles = GetOrCreateHandles(previous, AppleVirtualizationLedgerResourceKind.NetworkMembership, metadata);
            NetworkMembershipStatus stored = status with
            {
                Handle = handles.TargetHandle,
            };

            AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus> entry =
                Store(_membershipsByResource, key, previous, handles, stored, metadata);
            RefreshExecutionUnitNetworkMembershipsNoLock(spec?.Target.Unit);
            return entry;
        }
    }

    public AppleVirtualizationLedgerEntry<ServiceDiscovery, ServiceDiscoveryStatus> UpsertServiceDiscovery(
        ResourceMetadata<ServiceDiscovery> metadata,
        ServiceDiscoveryStatus status,
        ServiceDiscoverySpec? spec = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(metadata.Id, metadata.Scope);
            if (spec is not null)
            {
                _serviceDiscoverySpecsByResource[key] = spec;
            }

            AppleVirtualizationLedgerEntry<ServiceDiscovery, ServiceDiscoveryStatus>? previous = GetExisting(_serviceDiscoveriesByResource, key);
            HandlePair<ServiceDiscovery> handles = GetOrCreateHandles(previous, AppleVirtualizationLedgerResourceKind.ServiceDiscovery, metadata);
            return Store(_serviceDiscoveriesByResource, key, previous, handles, status, metadata);
        }
    }

    public AppleVirtualizationLedgerEntry<PublishedEndpoint, PublishedEndpointStatus> UpsertPublishedEndpoint(
        ResourceMetadata<PublishedEndpoint> metadata,
        PublishedEndpointStatus status,
        PublishedEndpointSpec? spec = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(metadata.Id, metadata.Scope);
            if (spec is not null)
            {
                _publishedEndpointSpecsByResource[key] = spec;
            }

            AppleVirtualizationLedgerEntry<PublishedEndpoint, PublishedEndpointStatus>? previous = GetExisting(_publishedEndpointsByResource, key);
            HandlePair<PublishedEndpoint> handles = GetOrCreateHandles(previous, AppleVirtualizationLedgerResourceKind.PublishedEndpoint, metadata);
            PublishedEndpointStatus stored = status with
            {
                RouterHandle = handles.TargetHandle,
            };

            return Store(_publishedEndpointsByResource, key, previous, handles, stored, metadata);
        }
    }

    public AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus> UpsertAuthorityBinding(
        ResourceMetadata<AuthorityBinding> metadata,
        AuthorityBindingStatus status,
        AuthorityBindingSpec? spec = null,
        IReadOnlyList<AuthorityAuditEvent>? auditEvents = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(metadata.Id, metadata.Scope);
            if (spec is not null)
            {
                _authorityBindingSpecsByResource[key] = spec;
            }

            AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus>? previous = GetExisting(_authorityBindingsByResource, key);
            HandlePair<AuthorityBinding> handles = GetOrCreateHandles(previous, AppleVirtualizationLedgerResourceKind.AuthorityBinding, metadata);
            AuthorityBindingStatus stored = status with
            {
                ProviderHandle = handles.TargetHandle,
            };

            if (auditEvents is not null && auditEvents.Count > 0)
            {
                AppendAuthorityAuditEventsNoLock(key, auditEvents);
            }

            return Store(_authorityBindingsByResource, key, previous, handles, stored, metadata);
        }
    }

    public AppleVirtualizationLedgerEntry<EngineControlPlane, EngineControlPlaneStatus> UpsertEngineControlPlane(
        ResourceMetadata<EngineControlPlane> metadata,
        EngineControlPlaneStatus status,
        EngineControlPlaneSpec? spec = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(metadata.Id, metadata.Scope);
            if (spec is not null)
            {
                _engineSpecsByResource[key] = spec;
            }

            AppleVirtualizationLedgerEntry<EngineControlPlane, EngineControlPlaneStatus>? previous = GetExisting(_enginesByResource, key);
            HandlePair<EngineControlPlane> handles = GetOrCreateHandles(previous, AppleVirtualizationLedgerResourceKind.EngineControlPlane, metadata);
            EngineControlPlaneStatus stored = status with
            {
                ProviderHandle = handles.ProviderHandle,
            };

            return Store(_enginesByResource, key, previous, handles, stored, metadata);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> TryGetRuntimeHost(ResourceRef<RuntimeHost> resource)
    {
        lock (_gate)
        {
            return TryGetByResource(_hostsByResource, resource, AppleVirtualizationLedgerResourceKind.RuntimeHost);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> TryGetExecutionUnit(ResourceRef<ExecutionUnit> resource)
    {
        lock (_gate)
        {
            return TryGetByResource(_unitsByResource, resource, AppleVirtualizationLedgerResourceKind.ExecutionUnit);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>> TryGetContentProjection(ResourceRef<ContentProjection> resource)
    {
        lock (_gate)
        {
            return TryGetByResource(_projectionsByResource, resource, AppleVirtualizationLedgerResourceKind.ContentProjection);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus>> TryGetProcessInvocation(ResourceRef<ProcessInvocation> resource)
    {
        lock (_gate)
        {
            return TryGetByResource(_processesByResource, resource, AppleVirtualizationLedgerResourceKind.ProcessInvocation);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<Network, NetworkStatus>> TryGetNetwork(ResourceRef<Network> resource)
    {
        lock (_gate)
        {
            return TryGetByResource(_networksByResource, resource, AppleVirtualizationLedgerResourceKind.Network);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>> TryGetNetworkMembership(ResourceRef<NetworkMembership> resource)
    {
        lock (_gate)
        {
            return TryGetByResource(_membershipsByResource, resource, AppleVirtualizationLedgerResourceKind.NetworkMembership);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ServiceDiscovery, ServiceDiscoveryStatus>> TryGetServiceDiscovery(ResourceRef<ServiceDiscovery> resource)
    {
        lock (_gate)
        {
            return TryGetByResource(_serviceDiscoveriesByResource, resource, AppleVirtualizationLedgerResourceKind.ServiceDiscovery);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<PublishedEndpoint, PublishedEndpointStatus>> TryGetPublishedEndpoint(ResourceRef<PublishedEndpoint> resource)
    {
        lock (_gate)
        {
            return TryGetByResource(_publishedEndpointsByResource, resource, AppleVirtualizationLedgerResourceKind.PublishedEndpoint);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus>> TryGetAuthorityBinding(ResourceRef<AuthorityBinding> resource)
    {
        lock (_gate)
        {
            return TryGetByResource(_authorityBindingsByResource, resource, AppleVirtualizationLedgerResourceKind.AuthorityBinding);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<EngineControlPlane, EngineControlPlaneStatus>> TryGetEngineControlPlane(ResourceRef<EngineControlPlane> resource)
    {
        lock (_gate)
        {
            return TryGetByResource(_enginesByResource, resource, AppleVirtualizationLedgerResourceKind.EngineControlPlane);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> TryGetRuntimeHost(TargetHandle<RuntimeHost> handle)
    {
        lock (_gate)
        {
            return TryGetRuntimeHost(handle.Route, handle.ProviderGeneration);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> TryGetRuntimeHost(TargetRoute route, ulong providerGeneration)
    {
        lock (_gate)
        {
            return TryGetByHandle(_hostsByResource, route, providerGeneration, AppleVirtualizationLedgerResourceKind.RuntimeHost);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> TryGetExecutionUnit(TargetHandle<ExecutionUnit> handle)
    {
        lock (_gate)
        {
            return TryGetByHandle(_unitsByResource, handle.Route, handle.ProviderGeneration, AppleVirtualizationLedgerResourceKind.ExecutionUnit);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>> TryGetContentProjection(TargetHandle<ContentProjection> handle)
    {
        lock (_gate)
        {
            return TryGetByHandle(_projectionsByResource, handle.Route, handle.ProviderGeneration, AppleVirtualizationLedgerResourceKind.ContentProjection);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus>> TryGetProcessInvocation(TargetHandle<ProcessInvocation> handle)
    {
        lock (_gate)
        {
            return TryGetByHandle(_processesByResource, handle.Route, handle.ProviderGeneration, AppleVirtualizationLedgerResourceKind.ProcessInvocation);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>> TryGetNetworkMembership(TargetHandle<NetworkMembership> handle)
    {
        lock (_gate)
        {
            return TryGetByHandle(_membershipsByResource, handle.Route, handle.ProviderGeneration, AppleVirtualizationLedgerResourceKind.NetworkMembership);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ServiceDiscovery, ServiceDiscoveryStatus>> TryGetServiceDiscovery(TargetHandle<ServiceDiscovery> handle)
    {
        lock (_gate)
        {
            return TryGetByHandle(_serviceDiscoveriesByResource, handle.Route, handle.ProviderGeneration, AppleVirtualizationLedgerResourceKind.ServiceDiscovery);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<PublishedEndpoint, PublishedEndpointStatus>> TryGetPublishedEndpoint(TargetHandle<PublishedEndpoint> handle)
    {
        lock (_gate)
        {
            return TryGetByHandle(_publishedEndpointsByResource, handle.Route, handle.ProviderGeneration, AppleVirtualizationLedgerResourceKind.PublishedEndpoint);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus>> TryGetAuthorityBinding(TargetHandle<AuthorityBinding> handle)
    {
        lock (_gate)
        {
            return TryGetByHandle(_authorityBindingsByResource, handle.Route, handle.ProviderGeneration, AppleVirtualizationLedgerResourceKind.AuthorityBinding);
        }
    }

    public AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<EngineControlPlane, EngineControlPlaneStatus>> TryGetEngineControlPlane(TargetHandle<EngineControlPlane> handle)
    {
        lock (_gate)
        {
            return TryGetByHandle(_enginesByResource, handle.Route, handle.ProviderGeneration, AppleVirtualizationLedgerResourceKind.EngineControlPlane);
        }
    }

    public bool RemoveRuntimeHost(ResourceRef<RuntimeHost> resource)
    {
        lock (_gate)
        {
            bool removed = Remove(_hostsByResource, resource);
            if (removed)
            {
                AppleVirtualizationResourceKey hostKey = ToKey(resource);
                _hostPoliciesByResource.Remove(hostKey);
                _hostConfigurationFingerprintsByResource.Remove(hostKey);
                foreach (AppleVirtualizationHostEngineKey engineKey in _hostEngineGenerationsByResource.Keys
                    .Where(key => key.Scope == resource.Scope &&
                        string.Equals(key.RuntimeHostId, resource.Id.Value, StringComparison.Ordinal))
                    .ToArray())
                {
                    _hostEngineGenerationsByResource.Remove(engineKey);
                }
            }

            return removed;
        }
    }

    public bool RemoveExecutionUnit(ResourceRef<ExecutionUnit> resource)
    {
        lock (_gate)
        {
            ReleaseMembershipsForExecutionUnitNoLock(resource);
            AppleVirtualizationResourceKey key = ToKey(resource);
            if (!_unitsByResource.Remove(key, out AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? entry))
            {
                return false;
            }

            _unitSpecsByResource.Remove(key);
            _handlesByToken.Remove(entry.ProviderHandle.Token);
            RefreshHostExecutionUnitsNoLock(entry.Status.AssignedHost);
            return true;
        }
    }

    public bool RemoveContentProjection(ResourceRef<ContentProjection> resource)
    {
        lock (_gate)
        {
            bool removed = Remove(_projectionsByResource, resource);
            if (removed)
            {
                _projectionSpecsByResource.Remove(ToKey(resource));
                DetachContentProjectionFromAllExecutionUnits(resource);
            }

            return removed;
        }
    }

    public ExecutionUnitSpec? TryGetExecutionUnitSpec(ResourceRef<ExecutionUnit> resource)
    {
        lock (_gate)
        {
            return _unitSpecsByResource.TryGetValue(ToKey(resource), out ExecutionUnitSpec? spec)
                ? spec
                : null;
        }
    }

    public ContentProjectionSpec? TryGetContentProjectionSpec(ResourceRef<ContentProjection> resource)
    {
        lock (_gate)
        {
            return _projectionSpecsByResource.TryGetValue(ToKey(resource), out ContentProjectionSpec? spec)
                ? spec
                : null;
        }
    }

    public AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>[] GetContentProjections(ResourceScope scope)
    {
        lock (_gate)
        {
            int count = 0;
            foreach (AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> projection in _projectionsByResource.Values)
            {
                if (string.Equals(projection.Resource.Scope.Value, scope.Value, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return [];
            }

            var projections = new AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>[count];
            int index = 0;
            foreach (AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> projection in _projectionsByResource.Values)
            {
                if (string.Equals(projection.Resource.Scope.Value, scope.Value, StringComparison.Ordinal))
                {
                    projections[index++] = projection;
                }
            }

            return projections;
        }
    }

    public bool RemoveProcessInvocation(ResourceRef<ProcessInvocation> resource)
    {
        lock (_gate)
        {
            bool removed = Remove(_processesByResource, resource);
            if (removed)
            {
                DetachProcessFromAllExecutionUnits(resource);
            }

            return removed;
        }
    }

    public bool RemoveNetwork(ResourceRef<Network> resource)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(resource);
            bool removed = Remove(_networksByResource, resource);
            if (removed)
            {
                _networkSpecsByResource.Remove(key);
                ReleaseMembershipsForNetworkNoLock(resource);
            }

            return removed;
        }
    }

    public bool RemoveNetworkMembership(ResourceRef<NetworkMembership> resource)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(resource);
            if (!_membershipsByResource.Remove(key, out AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>? entry))
            {
                return false;
            }

            NetworkMembershipSpec? spec = _membershipSpecsByResource.TryGetValue(key, out NetworkMembershipSpec? storedSpec)
                ? storedSpec
                : null;
            _membershipSpecsByResource.Remove(key);
            _handlesByToken.Remove(entry.ProviderHandle.Token);
            RefreshExecutionUnitNetworkMembershipsNoLock(spec?.Target.Unit);
            return true;
        }
    }

    public bool RemovePublishedEndpoint(ResourceRef<PublishedEndpoint> resource)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(resource);
            bool removed = Remove(_publishedEndpointsByResource, resource);
            if (removed)
            {
                _publishedEndpointSpecsByResource.Remove(key);
            }

            return removed;
        }
    }

    public bool RemoveAuthorityBinding(ResourceRef<AuthorityBinding> resource)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(resource);
            bool removed = Remove(_authorityBindingsByResource, resource);
            if (removed)
            {
                _authorityBindingSpecsByResource.Remove(key);
                _authorityAuditEventsByResource.Remove(key);
            }

            return removed;
        }
    }

    public bool RemoveEngineControlPlane(ResourceRef<EngineControlPlane> resource)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(resource);
            bool removed = Remove(_enginesByResource, resource);
            if (removed)
            {
                _engineSpecsByResource.Remove(key);
            }

            return removed;
        }
    }

    public AuthorityBindingSpec? TryGetAuthorityBindingSpec(ResourceRef<AuthorityBinding> resource)
    {
        lock (_gate)
        {
            return _authorityBindingSpecsByResource.TryGetValue(ToKey(resource), out AuthorityBindingSpec? spec)
                ? spec
                : null;
        }
    }

    public EngineControlPlaneSpec? TryGetEngineControlPlaneSpec(ResourceRef<EngineControlPlane> resource)
    {
        lock (_gate)
        {
            return _engineSpecsByResource.TryGetValue(ToKey(resource), out EngineControlPlaneSpec? spec)
                ? spec
                : null;
        }
    }

    public AuthorityAuditEvent[] GetAuthorityAuditEvents(ResourceRef<AuthorityBinding> resource)
    {
        lock (_gate)
        {
            return _authorityAuditEventsByResource.TryGetValue(ToKey(resource), out AuthorityAuditEvent[]? events)
                ? events.ToArray()
                : [];
        }
    }

    public ResourceRef<AuthorityBinding>[] GetAuthorityBindings(ResourceScope scope)
    {
        lock (_gate)
        {
            int count = 0;
            foreach (AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus> entry in _authorityBindingsByResource.Values)
            {
                if (string.Equals(entry.Resource.Scope.Value, scope.Value, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return [];
            }

            var bindings = new ResourceRef<AuthorityBinding>[count];
            int index = 0;
            foreach (AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus> entry in _authorityBindingsByResource.Values)
            {
                if (string.Equals(entry.Resource.Scope.Value, scope.Value, StringComparison.Ordinal))
                {
                    bindings[index++] = entry.Resource;
                }
            }

            return bindings;
        }
    }

    public AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus>? UpdateAuthorityBindingStatus(
        ResourceRef<AuthorityBinding> resource,
        AuthorityBindingStatus status,
        IReadOnlyList<AuthorityAuditEvent>? auditEvents = null)
    {
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(resource);
            if (!_authorityBindingsByResource.TryGetValue(key, out AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus>? entry))
            {
                return null;
            }

            if (auditEvents is not null && auditEvents.Count > 0)
            {
                AppendAuthorityAuditEventsNoLock(key, auditEvents);
            }

            AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus> updated = entry with
            {
                Status = status with { ProviderHandle = entry.TargetHandle },
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _authorityBindingsByResource[key] = updated;
            return updated;
        }
    }

    public AppleVirtualizationNetworkMembershipSnapshot[] GetActiveNetworkMemberships(
        ResourceRef<Network>? network = null,
        ResourceRef<RuntimeHost>? host = null)
    {
        lock (_gate)
        {
            int count = 0;
            foreach (KeyValuePair<AppleVirtualizationResourceKey, NetworkMembershipSpec> pair in _membershipSpecsByResource)
            {
                if (MembershipMatchesFilterNoLock(pair.Key, pair.Value, network, host))
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return [];
            }

            var snapshots = new AppleVirtualizationNetworkMembershipSnapshot[count];
            int index = 0;
            foreach (KeyValuePair<AppleVirtualizationResourceKey, NetworkMembershipSpec> pair in _membershipSpecsByResource)
            {
                if (!MembershipMatchesFilterNoLock(pair.Key, pair.Value, network, host))
                {
                    continue;
                }

                AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus> entry = _membershipsByResource[pair.Key];
                snapshots[index++] = new AppleVirtualizationNetworkMembershipSnapshot(entry.Resource, entry.Status, pair.Value);
            }

            return snapshots;
        }
    }

    public void InvalidateExecutionUnitsForRuntimeHost(
        ResourceRef<RuntimeHost> host,
        ResourcePhase unitResourcePhase,
        ExecutionUnitPhase unitPhase,
        Diagnostic diagnostic)
    {
        lock (_gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (AppleVirtualizationResourceKey unitKey in _unitsByResource.Keys.ToArray())
            {
                AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = _unitsByResource[unitKey];
                if (unit.Status.AssignedHost is not { } assignedHost || !SameResource(assignedHost, host))
                {
                    continue;
                }

                IReadOnlyList<ResourceRef<ProcessInvocation>> activeProcesses = unit.Status.ActiveProcesses;
                for (int i = 0; i < activeProcesses.Count; i++)
                {
                    MarkProcessStoppedByHost(activeProcesses[i], diagnostic, now);
                }

                _unitsByResource[unitKey] = unit with
                {
                    Status = unit.Status with
                    {
                        Phase = unitResourcePhase,
                        UnitPhase = unitPhase,
                        ActiveProcesses = Array.Empty<ResourceRef<ProcessInvocation>>(),
                        RealizedContentProjections = Array.Empty<ResourceRef<ContentProjection>>(),
                        NetworkMemberships = Array.Empty<ResourceRef<NetworkMembership>>(),
                        Diagnostics = AppendDiagnostic(unit.Status.Diagnostics, diagnostic),
                        LastTransitionAt = now,
                    },
                    UpdatedAt = now,
                };
            }

            RefreshHostExecutionUnitsNoLock(host);
        }
    }

    public void ReleaseMembershipsForRuntimeHost(ResourceRef<RuntimeHost> host, Diagnostic diagnostic)
    {
        lock (_gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (AppleVirtualizationResourceKey membershipKey in _membershipsByResource.Keys.ToArray())
            {
                AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus> membership = _membershipsByResource[membershipKey];
                if (!_membershipSpecsByResource.TryGetValue(membershipKey, out NetworkMembershipSpec? spec))
                {
                    continue;
                }

                if (!MembershipTargetsHostNoLock(spec, host))
                {
                    continue;
                }

                _membershipsByResource[membershipKey] = membership with
                {
                    Status = membership.Status with
                    {
                        Phase = ResourcePhase.Ready,
                        MembershipPhase = NetworkMembershipPhase.Released,
                        Diagnostics = AppendDiagnostic(membership.Status.Diagnostics, diagnostic),
                        LastTransitionAt = now,
                    },
                    UpdatedAt = now,
                };

                RefreshExecutionUnitNetworkMembershipsNoLock(spec.Target.Unit);
            }
        }
    }

    public bool IsContentProjectionReferencedByOtherUnit(
        ResourceRef<ContentProjection> projection,
        ResourceRef<ExecutionUnit> owner)
    {
        lock (_gate)
        {
            foreach (AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit in _unitsByResource.Values)
            {
                if (SameResource(unit.Resource, owner))
                {
                    continue;
                }

                IReadOnlyList<ResourceRef<ContentProjection>> realized = unit.Status.RealizedContentProjections;
                for (int i = 0; i < realized.Count; i++)
                {
                    if (SameResource(realized[i], projection))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public AppleVirtualizationHostEmptyPolicyEvaluation RefreshRuntimeHostEmptyPolicy(
        ResourceRef<RuntimeHost> host)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(host);
            if (!_hostsByResource.TryGetValue(key, out AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>? entry))
            {
                return new AppleVirtualizationHostEmptyPolicyEvaluation(
                    Host: null,
                    AppleVirtualizationRuntimeHostPolicySnapshot.Default,
                    AppleVirtualizationHostEmptyPolicyAction.Retain,
                    ActiveUnitCount: 0,
                    Diagnostic: AppleVirtualizationHandleDiagnostics.Missing(
                        ProviderId,
                        "runtime-host/" + host.Id.Value));
            }

            AppleVirtualizationRuntimeHostPolicySnapshot policy = PolicyForHostNoLock(key);
            ResourceRef<ExecutionUnit>[] activeUnits = ActiveExecutionUnitsForRuntimeHostNoLock(host);
            AppleVirtualizationHostEmptyPolicyAction action = EmptyPolicyAction(policy, activeUnits.Length);
            RuntimeHostStatus status = entry.Status with { ExecutionUnits = activeUnits };
            Diagnostic? diagnostic = EmptyPolicyDiagnostic(action, policy, host.Id.Value);
            if (diagnostic is not null)
            {
                status = status with
                {
                    Conditions = ReplaceCondition(
                        status.Conditions,
                        EmptyPolicyCondition(action, policy, entry.Resource.Generation ?? default, activeUnits.Length)),
                    Diagnostics = AppendDiagnosticIfMissing(status.Diagnostics, diagnostic),
                    LastTransitionAt = DateTimeOffset.UtcNow,
                };
            }

            AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> stored = entry with
            {
                Status = status,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _hostsByResource[key] = stored;

            return new AppleVirtualizationHostEmptyPolicyEvaluation(stored, policy, action, activeUnits.Length, diagnostic);
        }
    }

    public bool AttachProcessToExecutionUnit(
        ResourceRef<ExecutionUnit> unit,
        ResourceRef<ProcessInvocation> process)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(unit);
            if (!_unitsByResource.TryGetValue(key, out AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? entry))
            {
                return false;
            }

            if (Contains(entry.Status.ActiveProcesses, process))
            {
                return true;
            }

            ResourceRef<ProcessInvocation>[] active = Append(entry.Status.ActiveProcesses, process);
            _unitsByResource[key] = entry with
            {
                Status = entry.Status with
                {
                    ActiveProcesses = active,
                    UnitPhase = entry.Status.UnitPhase == ExecutionUnitPhase.Ready
                        ? ExecutionUnitPhase.Running
                        : entry.Status.UnitPhase,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                },
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return true;
        }
    }

    public bool DetachProcessFromExecutionUnit(
        ResourceRef<ExecutionUnit> unit,
        ResourceRef<ProcessInvocation> process)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(unit);
            if (!_unitsByResource.TryGetValue(key, out AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? entry))
            {
                return false;
            }

            if (!TryRemove(entry.Status.ActiveProcesses, process, out ResourceRef<ProcessInvocation>[] active))
            {
                return true;
            }

            _unitsByResource[key] = entry with
            {
                Status = entry.Status with
                {
                    ActiveProcesses = active,
                    UnitPhase = active.Length == 0 && entry.Status.UnitPhase == ExecutionUnitPhase.Running
                        ? ExecutionUnitPhase.Ready
                        : entry.Status.UnitPhase,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                },
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return true;
        }
    }

    public void DetachProcessFromAllExecutionUnits(ResourceRef<ProcessInvocation> process)
    {
        lock (_gate)
        {
            foreach (AppleVirtualizationResourceKey key in _unitsByResource.Keys.ToArray())
            {
                AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> entry = _unitsByResource[key];
                if (!TryRemove(entry.Status.ActiveProcesses, process, out ResourceRef<ProcessInvocation>[] active))
                {
                    continue;
                }

                _unitsByResource[key] = entry with
                {
                    Status = entry.Status with
                    {
                        ActiveProcesses = active,
                        UnitPhase = active.Length == 0 && entry.Status.UnitPhase == ExecutionUnitPhase.Running
                            ? ExecutionUnitPhase.Ready
                            : entry.Status.UnitPhase,
                        LastTransitionAt = DateTimeOffset.UtcNow,
                    },
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
            }
        }
    }

    public bool AttachContentProjectionToExecutionUnit(
        ResourceRef<ExecutionUnit> unit,
        ResourceRef<ContentProjection> projection)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(unit);
            if (!_unitsByResource.TryGetValue(key, out AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? entry))
            {
                return false;
            }

            if (Contains(entry.Status.RealizedContentProjections, projection))
            {
                return true;
            }

            ResourceRef<ContentProjection>[] projections = Append(entry.Status.RealizedContentProjections, projection);
            _unitsByResource[key] = entry with
            {
                Status = entry.Status with
                {
                    RealizedContentProjections = projections,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                },
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return true;
        }
    }

    public bool AttachNetworkMembershipToExecutionUnit(
        ResourceRef<ExecutionUnit> unit,
        ResourceRef<NetworkMembership> membership)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(unit);
            if (!_unitsByResource.TryGetValue(key, out AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? entry))
            {
                return false;
            }

            if (Contains(entry.Status.NetworkMemberships, membership))
            {
                return true;
            }

            ResourceRef<NetworkMembership>[] memberships = Append(entry.Status.NetworkMemberships, membership);
            _unitsByResource[key] = entry with
            {
                Status = entry.Status with
                {
                    NetworkMemberships = memberships,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                },
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return true;
        }
    }

    public bool AttachAuthorityBindingToExecutionUnit(
        TargetHandle<ExecutionUnit> unit,
        ResourceRef<AuthorityBinding> binding)
    {
        lock (_gate)
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> lookup =
                TryGetByHandle(_unitsByResource, unit.Route, unit.ProviderGeneration, AppleVirtualizationLedgerResourceKind.ExecutionUnit);
            if (!lookup.Succeeded || lookup.Entry is null)
            {
                return false;
            }

            return AttachAuthorityBindingToExecutionUnitNoLock(lookup.Entry.Resource, binding);
        }
    }

    public bool AttachAuthorityBindingToExecutionUnit(
        ResourceRef<ExecutionUnit> unit,
        ResourceRef<AuthorityBinding> binding)
    {
        lock (_gate)
        {
            return AttachAuthorityBindingToExecutionUnitNoLock(unit, binding);
        }
    }

    public bool DetachAuthorityBindingFromExecutionUnit(
        ResourceRef<ExecutionUnit> unit,
        ResourceRef<AuthorityBinding> binding)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(unit);
            if (!_unitsByResource.TryGetValue(key, out AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? entry))
            {
                return false;
            }

            if (!TryRemove(entry.Status.AuthorityBindings, binding, out ResourceRef<AuthorityBinding>[] authorityBindings))
            {
                return true;
            }

            _unitsByResource[key] = entry with
            {
                Status = entry.Status with
                {
                    AuthorityBindings = authorityBindings,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                },
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return true;
        }
    }

    public ResourceRef<AuthorityBinding>[] GetAuthorityBindingsForExecutionUnit(ResourceRef<ExecutionUnit> unit)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey unitKey = ToKey(unit);
            if (!_unitsByResource.TryGetValue(unitKey, out AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? entry))
            {
                return [];
            }

            if (entry.Status.AuthorityBindings.Count == 0)
            {
                return [];
            }

            var result = new ResourceRef<AuthorityBinding>[entry.Status.AuthorityBindings.Count];
            for (int i = 0; i < entry.Status.AuthorityBindings.Count; i++)
            {
                result[i] = entry.Status.AuthorityBindings[i];
            }

            return result;
        }
    }

    public bool AuthorityBindingTargetsExecutionUnit(
        ResourceRef<AuthorityBinding> binding,
        ResourceRef<ExecutionUnit> unit)
    {
        lock (_gate)
        {
            if (!_authorityBindingSpecsByResource.TryGetValue(ToKey(binding), out AuthorityBindingSpec? spec) ||
                spec.Target.Kind != AuthorityTargetKind.ExecutionUnit ||
                spec.Target.Unit is not { } targetUnit)
            {
                return false;
            }

            return HandleTargetsResource(targetUnit.Route, unit.Id.Value, unit.Scope);
        }
    }

    public bool DetachNetworkMembershipFromExecutionUnit(
        ResourceRef<ExecutionUnit> unit,
        ResourceRef<NetworkMembership> membership)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(unit);
            if (!_unitsByResource.TryGetValue(key, out AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? entry))
            {
                return false;
            }

            if (!TryRemove(entry.Status.NetworkMemberships, membership, out ResourceRef<NetworkMembership>[] memberships))
            {
                return true;
            }

            _unitsByResource[key] = entry with
            {
                Status = entry.Status with
                {
                    NetworkMemberships = memberships,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                },
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return true;
        }
    }

    public bool DetachContentProjectionFromExecutionUnit(
        ResourceRef<ExecutionUnit> unit,
        ResourceRef<ContentProjection> projection)
    {
        lock (_gate)
        {
            AppleVirtualizationResourceKey key = ToKey(unit);
            if (!_unitsByResource.TryGetValue(key, out AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? entry))
            {
                return false;
            }

            if (!TryRemove(entry.Status.RealizedContentProjections, projection, out ResourceRef<ContentProjection>[] projections))
            {
                return true;
            }

            _unitsByResource[key] = entry with
            {
                Status = entry.Status with
                {
                    RealizedContentProjections = projections,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                },
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return true;
        }
    }

    public void DetachContentProjectionFromAllExecutionUnits(ResourceRef<ContentProjection> projection)
    {
        lock (_gate)
        {
            foreach (AppleVirtualizationResourceKey key in _unitsByResource.Keys.ToArray())
            {
                AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> entry = _unitsByResource[key];
                if (!TryRemove(entry.Status.RealizedContentProjections, projection, out ResourceRef<ContentProjection>[] projections))
                {
                    continue;
                }

                _unitsByResource[key] = entry with
                {
                    Status = entry.Status with
                    {
                        RealizedContentProjections = projections,
                        LastTransitionAt = DateTimeOffset.UtcNow,
                    },
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
            }
        }
    }

    private bool AttachAuthorityBindingToExecutionUnitNoLock(
        ResourceRef<ExecutionUnit> unit,
        ResourceRef<AuthorityBinding> binding)
    {
        AppleVirtualizationResourceKey key = ToKey(unit);
        if (!_unitsByResource.TryGetValue(key, out AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? entry))
        {
            return false;
        }

        if (Contains(entry.Status.AuthorityBindings, binding))
        {
            return true;
        }

        ResourceRef<AuthorityBinding>[] authorityBindings = Append(entry.Status.AuthorityBindings, binding);
        _unitsByResource[key] = entry with
        {
            Status = entry.Status with
            {
                AuthorityBindings = authorityBindings,
                LastTransitionAt = DateTimeOffset.UtcNow,
            },
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return true;
    }

    private void RefreshHostExecutionUnitsForUnitTransitionNoLock(
        ExecutionUnitStatus? previous,
        ExecutionUnitStatus current)
    {
        if (previous?.AssignedHost is { } previousHost &&
            (current.AssignedHost is null || !SameResource(previousHost, current.AssignedHost.Value)))
        {
            RefreshHostExecutionUnitsNoLock(previousHost);
        }

        RefreshHostExecutionUnitsNoLock(current.AssignedHost);
    }

    private void RefreshHostExecutionUnitsNoLock(ResourceRef<RuntimeHost>? host)
    {
        if (host is null)
        {
            return;
        }

        AppleVirtualizationResourceKey hostKey = ToKey(host.Value);
        if (!_hostsByResource.TryGetValue(hostKey, out AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>? entry))
        {
            return;
        }

        _hostsByResource[hostKey] = entry with
        {
            Status = entry.Status with
            {
                ExecutionUnits = ActiveExecutionUnitsForRuntimeHostNoLock(host.Value),
            },
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private void RefreshExecutionUnitNetworkMembershipsNoLock(TargetHandle<ExecutionUnit>? unitHandle)
    {
        if (unitHandle is null)
        {
            return;
        }

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> lookup =
            TryGetByHandle(_unitsByResource, unitHandle.Value.Route, unitHandle.Value.ProviderGeneration, AppleVirtualizationLedgerResourceKind.ExecutionUnit);
        if (!lookup.Succeeded || lookup.Entry is null)
        {
            return;
        }

        RefreshExecutionUnitNetworkMembershipsNoLock(lookup.Entry.Resource);
    }

    private void RefreshExecutionUnitNetworkMembershipsNoLock(ResourceRef<ExecutionUnit> unit)
    {
        AppleVirtualizationResourceKey unitKey = ToKey(unit);
        if (!_unitsByResource.TryGetValue(unitKey, out AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>? entry))
        {
            return;
        }

        int count = 0;
        foreach (KeyValuePair<AppleVirtualizationResourceKey, NetworkMembershipSpec> pair in _membershipSpecsByResource)
        {
            if (pair.Value.Target.Unit is { } unitHandle &&
                HandleTargetsResource(unitHandle.Route, unit.Id.Value, unit.Scope) &&
                _membershipsByResource.TryGetValue(pair.Key, out AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>? membership) &&
                IsActiveMembership(membership.Status))
            {
                count++;
            }
        }

        ResourceRef<NetworkMembership>[] memberships = count == 0 ? [] : new ResourceRef<NetworkMembership>[count];
        if (count != 0)
        {
            int index = 0;
            foreach (KeyValuePair<AppleVirtualizationResourceKey, NetworkMembershipSpec> pair in _membershipSpecsByResource)
            {
                if (pair.Value.Target.Unit is { } unitHandle &&
                    HandleTargetsResource(unitHandle.Route, unit.Id.Value, unit.Scope) &&
                    _membershipsByResource.TryGetValue(pair.Key, out AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>? membership) &&
                    IsActiveMembership(membership.Status))
                {
                    memberships[index++] = membership.Resource;
                }
            }
        }

        _unitsByResource[unitKey] = entry with
        {
            Status = entry.Status with
            {
                NetworkMemberships = memberships,
                LastTransitionAt = DateTimeOffset.UtcNow,
            },
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private ResourceRef<ExecutionUnit>[] ActiveExecutionUnitsForRuntimeHostNoLock(ResourceRef<RuntimeHost> host)
    {
        int count = 0;
        foreach (AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit in _unitsByResource.Values)
        {
            if (unit.Status.AssignedHost is { } assignedHost &&
                SameResource(assignedHost, host) &&
                IsActiveForHost(unit.Status))
            {
                count++;
            }
        }

        if (count == 0)
        {
            return [];
        }

        ResourceRef<ExecutionUnit>[] units = new ResourceRef<ExecutionUnit>[count];
        int index = 0;
        foreach (AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit in _unitsByResource.Values)
        {
            if (unit.Status.AssignedHost is { } assignedHost &&
                SameResource(assignedHost, host) &&
                IsActiveForHost(unit.Status))
            {
                units[index++] = unit.Resource;
            }
        }

        return units;
    }

    private AppleVirtualizationRuntimeHostPolicySnapshot PolicyForHostNoLock(AppleVirtualizationResourceKey key) =>
        _hostPoliciesByResource.TryGetValue(key, out AppleVirtualizationRuntimeHostPolicySnapshot policy)
            ? policy
            : AppleVirtualizationRuntimeHostPolicySnapshot.Default;

    private static bool IsActiveForHost(ExecutionUnitStatus status) =>
        status.Phase != ResourcePhase.Deleted &&
        status.UnitPhase is not ExecutionUnitPhase.Stopped and not ExecutionUnitPhase.Deleted;

    private static bool IsActiveMembership(NetworkMembershipStatus status) =>
        status.Phase != ResourcePhase.Deleted &&
        status.MembershipPhase is not NetworkMembershipPhase.Released and not NetworkMembershipPhase.Failed;

    public bool IsAuthorityTargetStopped(AuthorityBindingSpec spec)
    {
        lock (_gate)
        {
            return spec.Target.Kind switch
            {
                AuthorityTargetKind.ExecutionUnit when spec.Target.Unit is { } unit =>
                    TryGetByHandle(_unitsByResource, unit.Route, unit.ProviderGeneration, AppleVirtualizationLedgerResourceKind.ExecutionUnit) is { Succeeded: true, Entry: { } entry } &&
                    entry.Status.UnitPhase is ExecutionUnitPhase.Stopping or ExecutionUnitPhase.Stopped or ExecutionUnitPhase.Deleting or ExecutionUnitPhase.Deleted,
                AuthorityTargetKind.ProcessInvocation when spec.Target.Process is { } process =>
                    TryGetByHandle(_processesByResource, process.Route, process.ProviderGeneration, AppleVirtualizationLedgerResourceKind.ProcessInvocation) is { Succeeded: true, Entry: { } entry } &&
                    entry.Status.ProcessPhase is ProcessInvocationPhase.Stopping or ProcessInvocationPhase.Stopped or ProcessInvocationPhase.Exited or ProcessInvocationPhase.Failed,
                _ => false,
            };
        }
    }

    private void AppendAuthorityAuditEventsNoLock(
        AppleVirtualizationResourceKey key,
        IReadOnlyList<AuthorityAuditEvent> auditEvents)
    {
        _authorityAuditEventsByResource.TryGetValue(key, out AuthorityAuditEvent[]? existing);
        int existingCount = existing?.Length ?? 0;
        int sourceStart = Math.Max(0, existingCount + auditEvents.Count - MaxAuthorityAuditEvents);
        int keptExisting = existingCount == 0 ? 0 : Math.Max(0, existingCount - sourceStart);
        int keptNew = Math.Min(auditEvents.Count, MaxAuthorityAuditEvents - keptExisting);
        var updated = new AuthorityAuditEvent[keptExisting + keptNew];
        if (existing is not null && keptExisting > 0)
        {
            Array.Copy(existing, existingCount - keptExisting, updated, 0, keptExisting);
        }

        int newStart = auditEvents.Count - keptNew;
        for (int i = 0; i < keptNew; i++)
        {
            updated[keptExisting + i] = auditEvents[newStart + i];
        }

        _authorityAuditEventsByResource[key] = updated;
    }

    private void ReleaseMembershipsForExecutionUnitNoLock(ResourceRef<ExecutionUnit> unit)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (AppleVirtualizationResourceKey membershipKey in _membershipsByResource.Keys.ToArray())
        {
            if (!_membershipSpecsByResource.TryGetValue(membershipKey, out NetworkMembershipSpec? spec) ||
                spec.Target.Unit is not { } unitHandle ||
                !HandleTargetsResource(unitHandle.Route, unit.Id.Value, unit.Scope))
            {
                continue;
            }

            AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus> membership = _membershipsByResource[membershipKey];
            _membershipsByResource[membershipKey] = membership with
            {
                Status = membership.Status with
                {
                    Phase = ResourcePhase.Ready,
                    MembershipPhase = NetworkMembershipPhase.Released,
                    LastTransitionAt = now,
                },
                UpdatedAt = now,
            };
        }
    }

    private void ReleaseMembershipsForNetworkNoLock(ResourceRef<Network> network)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (AppleVirtualizationResourceKey membershipKey in _membershipsByResource.Keys.ToArray())
        {
            if (!_membershipSpecsByResource.TryGetValue(membershipKey, out NetworkMembershipSpec? spec) ||
                !SameResource(spec.Network, network))
            {
                continue;
            }

            AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus> membership = _membershipsByResource[membershipKey];
            _membershipsByResource[membershipKey] = membership with
            {
                Status = membership.Status with
                {
                    Phase = ResourcePhase.Ready,
                    MembershipPhase = NetworkMembershipPhase.Released,
                    LastTransitionAt = now,
                },
                UpdatedAt = now,
            };
            RefreshExecutionUnitNetworkMembershipsNoLock(spec.Target.Unit);
        }
    }

    private bool MembershipTargetsHostNoLock(NetworkMembershipSpec spec, ResourceRef<RuntimeHost> host)
    {
        if (spec.Target.Host is { } hostHandle &&
            HandleTargetsResource(hostHandle.Route, host.Id.Value, host.Scope))
        {
            return true;
        }

        if (spec.Target.Unit is { } unitHandle)
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> unitLookup =
                TryGetByHandle(_unitsByResource, unitHandle.Route, unitHandle.ProviderGeneration, AppleVirtualizationLedgerResourceKind.ExecutionUnit);
            return unitLookup.Succeeded &&
                unitLookup.Entry!.Status.AssignedHost is { } assignedHost &&
                SameResource(assignedHost, host);
        }

        return false;
    }

    private bool MembershipMatchesFilterNoLock(
        AppleVirtualizationResourceKey key,
        NetworkMembershipSpec spec,
        ResourceRef<Network>? network,
        ResourceRef<RuntimeHost>? host)
    {
        if (!_membershipsByResource.TryGetValue(key, out AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>? membership) ||
            !IsActiveMembership(membership.Status))
        {
            return false;
        }

        if (network is { } networkRef && !SameResource(spec.Network, networkRef))
        {
            return false;
        }

        return host is null || MembershipTargetsHostNoLock(spec, host.Value);
    }

    private static AppleVirtualizationHostEmptyPolicyAction EmptyPolicyAction(
        AppleVirtualizationRuntimeHostPolicySnapshot policy,
        int activeUnitCount)
    {
        if (activeUnitCount > 0)
        {
            return AppleVirtualizationHostEmptyPolicyAction.Active;
        }

        if (!policy.LifecyclePolicy.StopHostWhenEmpty || policy.TopologyPolicy.RetainEmptyHost)
        {
            return AppleVirtualizationHostEmptyPolicyAction.Retain;
        }

        if (policy.LifecyclePolicy.IdleRetention is { } idleRetention && idleRetention > TimeSpan.Zero)
        {
            return AppleVirtualizationHostEmptyPolicyAction.IdleRetentionPending;
        }

        return AppleVirtualizationHostEmptyPolicyAction.StopNow;
    }

    private static Diagnostic? EmptyPolicyDiagnostic(
        AppleVirtualizationHostEmptyPolicyAction action,
        AppleVirtualizationRuntimeHostPolicySnapshot policy,
        string hostId) =>
        action switch
        {
            AppleVirtualizationHostEmptyPolicyAction.Retain => new Diagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Code = new DiagnosticCode("AppleVirtualization.RuntimeHostEmptyRetained"),
                Message = policy.TopologyPolicy.RetainEmptyHost
                    ? "The Apple Virtualization runtime host is empty and retained because RuntimeTopologyPolicy.RetainEmptyHost is enabled."
                    : "The Apple Virtualization runtime host is empty and retained because LifecyclePolicy.StopHostWhenEmpty is disabled.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "runtime-host/" + hostId,
            },
            AppleVirtualizationHostEmptyPolicyAction.IdleRetentionPending => new Diagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Code = new DiagnosticCode("AppleVirtualization.RuntimeHostIdleRetentionPending"),
                Message = "The Apple Virtualization runtime host is empty, but IdleRetention is represented conservatively without a background stop timer in this provider slice.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "runtime-host/" + hostId,
            },
            AppleVirtualizationHostEmptyPolicyAction.StopNow => new Diagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Code = new DiagnosticCode("AppleVirtualization.RuntimeHostStopWhenEmpty"),
                Message = "The Apple Virtualization runtime host is empty and LifecyclePolicy.StopHostWhenEmpty requested a host stop.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "runtime-host/" + hostId,
            },
            _ => null,
        };

    private static Condition EmptyPolicyCondition(
        AppleVirtualizationHostEmptyPolicyAction action,
        AppleVirtualizationRuntimeHostPolicySnapshot policy,
        ResourceGeneration generation,
        int activeUnitCount)
    {
        string reason = action.ToString();
        string message = action switch
        {
            AppleVirtualizationHostEmptyPolicyAction.Active =>
                "The Apple Virtualization runtime host has active execution units.",
            AppleVirtualizationHostEmptyPolicyAction.Retain when policy.TopologyPolicy.RetainEmptyHost =>
                "The Apple Virtualization runtime host is empty and retained by topology policy.",
            AppleVirtualizationHostEmptyPolicyAction.Retain =>
                "The Apple Virtualization runtime host is empty and retained by lifecycle policy.",
            AppleVirtualizationHostEmptyPolicyAction.IdleRetentionPending =>
                "The Apple Virtualization runtime host is empty and idle retention is pending without a background stop timer.",
            AppleVirtualizationHostEmptyPolicyAction.StopNow =>
                "The Apple Virtualization runtime host is empty and stop-on-empty is requested.",
            _ => "The Apple Virtualization runtime host empty policy was evaluated.",
        };

        return new Condition(
            "AppleVirtualization.RuntimeHostEmptyPolicy",
            activeUnitCount == 0 ? ConditionStatus.True : ConditionStatus.False,
            reason,
            message,
            DateTimeOffset.UtcNow,
            generation,
            DiagnosticSeverity.Info);
    }

    private static IReadOnlyList<Condition> ReplaceCondition(IReadOnlyList<Condition> existing, Condition condition)
    {
        int index = -1;
        for (int i = 0; i < existing.Count; i++)
        {
            if (string.Equals(existing[i].Type, condition.Type, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            Condition[] appended = new Condition[existing.Count + 1];
            for (int i = 0; i < existing.Count; i++)
            {
                appended[i] = existing[i];
            }

            appended[^1] = condition;
            return appended;
        }

        Condition[] updated = new Condition[existing.Count];
        for (int i = 0; i < existing.Count; i++)
        {
            updated[i] = i == index ? condition : existing[i];
        }

        return updated;
    }

    private static IReadOnlyList<Diagnostic> AppendDiagnosticIfMissing(IReadOnlyList<Diagnostic> existing, Diagnostic diagnostic)
    {
        for (int i = 0; i < existing.Count; i++)
        {
            if (existing[i].Code == diagnostic.Code &&
                string.Equals(existing[i].TargetPath, diagnostic.TargetPath, StringComparison.Ordinal))
            {
                return existing;
            }
        }

        return AppendDiagnostic(existing, diagnostic);
    }

    private static bool SameResource<TResource>(ResourceRef<TResource> left, ResourceRef<TResource> right)
        where TResource : IExecutionResourceMarker =>
        string.Equals(left.Id.Value, right.Id.Value, StringComparison.Ordinal) &&
        string.Equals(left.Scope.Value, right.Scope.Value, StringComparison.Ordinal);

    private static bool HandleTargetsResource(TargetRoute route, string id, ResourceScope scope) =>
        string.Equals(route.BackingResourceId, id, StringComparison.Ordinal) &&
        string.Equals(route.Scope.Value, scope.Value, StringComparison.Ordinal);

    private void MarkProcessStoppedByHost(
        ResourceRef<ProcessInvocation> process,
        Diagnostic diagnostic,
        DateTimeOffset now)
    {
        AppleVirtualizationResourceKey processKey = ToKey(process);
        if (!_processesByResource.TryGetValue(processKey, out AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus>? entry))
        {
            return;
        }

        if (entry.Status.Result is not null ||
            entry.Status.ProcessPhase is ProcessInvocationPhase.Exited or ProcessInvocationPhase.Failed or ProcessInvocationPhase.Stopped)
        {
            return;
        }

        ProcessInvocationResult result = new()
        {
            ProcessId = entry.Resource.Id,
            SystemProcessId = entry.Status.SystemProcessId,
            ProviderProcessId = entry.Status.ProviderProcessId,
            CompletionKind = ProcessCompletionKind.Stopped,
            StartedAt = entry.Status.StartedAt,
            ExitedAt = now,
            Output = EmptyProcessOutput(),
        };

        _processesByResource[processKey] = entry with
        {
            Status = entry.Status with
            {
                Phase = ResourcePhase.Ready,
                ProcessPhase = ProcessInvocationPhase.Stopped,
                IoState = ProcessIoState.Closed,
                Result = result,
                Diagnostics = AppendDiagnostic(entry.Status.Diagnostics, diagnostic),
                ExitedAt = now,
                LastTransitionAt = now,
            },
            UpdatedAt = now,
        };
    }

    private static IReadOnlyList<Diagnostic> AppendDiagnostic(IReadOnlyList<Diagnostic> existing, Diagnostic diagnostic)
    {
        Diagnostic[] diagnostics = new Diagnostic[existing.Count + 1];
        for (int i = 0; i < existing.Count; i++)
        {
            diagnostics[i] = existing[i];
        }

        diagnostics[^1] = diagnostic;
        return diagnostics;
    }

    private static ProcessCapturedOutput EmptyProcessOutput() =>
        new()
        {
            Stdout = new ProcessStreamOutput(),
            Stderr = new ProcessStreamOutput(),
        };

    private static bool Contains<TResource>(IReadOnlyList<ResourceRef<TResource>> resources, ResourceRef<TResource> resource)
        where TResource : IExecutionResourceMarker
    {
        for (int i = 0; i < resources.Count; i++)
        {
            if (SameResource(resources[i], resource))
            {
                return true;
            }
        }

        return false;
    }

    private static ResourceRef<TResource>[] Append<TResource>(IReadOnlyList<ResourceRef<TResource>> resources, ResourceRef<TResource> resource)
        where TResource : IExecutionResourceMarker
    {
        ResourceRef<TResource>[] appended = new ResourceRef<TResource>[resources.Count + 1];
        for (int i = 0; i < resources.Count; i++)
        {
            appended[i] = resources[i];
        }

        appended[^1] = resource;
        return appended;
    }

    private static bool TryRemove<TResource>(
        IReadOnlyList<ResourceRef<TResource>> resources,
        ResourceRef<TResource> resource,
        out ResourceRef<TResource>[] updated)
        where TResource : IExecutionResourceMarker
    {
        int index = -1;
        for (int i = 0; i < resources.Count; i++)
        {
            if (SameResource(resources[i], resource))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            updated = resources.Count == 0 ? [] : resources.ToArray();
            return false;
        }

        if (resources.Count == 1)
        {
            updated = [];
            return true;
        }

        updated = new ResourceRef<TResource>[resources.Count - 1];
        for (int i = 0; i < index; i++)
        {
            updated[i] = resources[i];
        }

        for (int i = index + 1; i < resources.Count; i++)
        {
            updated[i - 1] = resources[i];
        }

        return true;
    }

    private static AppleVirtualizationResourceKey ToKey<TResource>(ResourceId<TResource> id, ResourceScope scope)
        where TResource : IExecutionResourceMarker =>
        new(scope, id.Value);

    private static AppleVirtualizationResourceKey ToKey<TResource>(ResourceRef<TResource> resource)
        where TResource : IExecutionResourceMarker =>
        new(resource.Scope, resource.Id.Value);

    private static AppleVirtualizationLedgerEntry<TResource, TStatus>? GetExisting<TResource, TStatus>(
        Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<TResource, TStatus>> entries,
        AppleVirtualizationResourceKey key)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TStatus : ResourceStatus =>
        entries.TryGetValue(key, out AppleVirtualizationLedgerEntry<TResource, TStatus>? entry) ? entry : null;

    private HandlePair<TResource> GetOrCreateHandles<TResource, TStatus>(
        AppleVirtualizationLedgerEntry<TResource, TStatus>? previous,
        AppleVirtualizationLedgerResourceKind kind,
        ResourceMetadata<TResource> metadata)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TStatus : ResourceStatus
    {
        if (previous is not null && previous.ProviderGeneration == _providerGeneration)
        {
            return new HandlePair<TResource>(previous.TargetHandle, previous.ProviderHandle);
        }

        string token = CreateToken(kind, metadata);
        ProviderOpaqueHandle providerHandle = new(
            ProviderId,
            token,
            SchemaIdFor(kind),
            _providerGeneration);
        TargetHandle<TResource> targetHandle = new(
            new TargetRoute
            {
                Kind = TargetKindFor(kind),
                Scope = metadata.Scope,
                Segments = [new TargetRouteSegment(SegmentKindFor(kind), metadata.Id.Value)],
                BackingResourceKind = metadata.Kind,
                BackingResourceId = metadata.Id.Value,
                ProviderId = ProviderId,
                ProviderHandle = providerHandle,
            },
            LifetimeFor(kind),
            AuthorityFor(kind),
            ProviderGeneration: _providerGeneration);

        if (previous is not null)
        {
            _handlesByToken.Remove(previous.ProviderHandle.Token);
        }

        return new HandlePair<TResource>(targetHandle, providerHandle);
    }

    private AppleVirtualizationLedgerEntry<TResource, TStatus> Store<TResource, TStatus>(
        Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<TResource, TStatus>> entries,
        AppleVirtualizationResourceKey key,
        AppleVirtualizationLedgerEntry<TResource, TStatus>? previous,
        HandlePair<TResource> handles,
        TStatus status,
        ResourceMetadata<TResource> metadata)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TStatus : ResourceStatus
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var entry = new AppleVirtualizationLedgerEntry<TResource, TStatus>(
            new ResourceRef<TResource>(metadata.Id, metadata.Scope, metadata.Generation),
            handles.TargetHandle,
            handles.ProviderHandle,
            status,
            previous?.CreatedAt ?? now,
            now,
            KindFrom(typeof(TResource)),
            _providerGeneration);

        entries[key] = entry;
        _handlesByToken[handles.ProviderHandle.Token] = new HandleIndexEntry(entry.Kind, key, _providerGeneration);
        return entry;
    }

    private AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<TResource, TStatus>> TryGetByResource<TResource, TStatus>(
        Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<TResource, TStatus>> entries,
        ResourceRef<TResource> resource,
        AppleVirtualizationLedgerResourceKind expectedKind)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TStatus : ResourceStatus
    {
        AppleVirtualizationResourceKey key = ToKey(resource);
        if (!entries.TryGetValue(key, out AppleVirtualizationLedgerEntry<TResource, TStatus>? entry))
        {
            return Failure<AppleVirtualizationLedgerEntry<TResource, TStatus>>(
                AppleVirtualizationHandleDiagnostics.Missing(ProviderId, TargetPath(expectedKind, resource.Id.Value)));
        }

        if (resource.Generation is { } requestedGeneration &&
            entry.Resource.Generation is { } observedGeneration &&
            requestedGeneration.Value != observedGeneration.Value)
        {
            return Failure<AppleVirtualizationLedgerEntry<TResource, TStatus>>(
                AppleVirtualizationHandleDiagnostics.GenerationMismatch(
                    ProviderId,
                    TargetPath(expectedKind, resource.Id.Value),
                    observedGeneration.Value,
                    requestedGeneration.Value));
        }

        return Success(entry);
    }

    private AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<TResource, TStatus>> TryGetByHandle<TResource, TStatus>(
        Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<TResource, TStatus>> entries,
        TargetRoute route,
        ulong handleProviderGeneration,
        AppleVirtualizationLedgerResourceKind expectedKind)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TStatus : ResourceStatus
    {
        ProviderOpaqueHandle? providerHandle = route.ProviderHandle;
        if (providerHandle is null)
        {
            return Failure<AppleVirtualizationLedgerEntry<TResource, TStatus>>(
                AppleVirtualizationHandleDiagnostics.Missing(ProviderId, TargetPath(expectedKind, route.BackingResourceId)));
        }

        if (providerHandle.Value.ProviderId != ProviderId)
        {
            return Failure<AppleVirtualizationLedgerEntry<TResource, TStatus>>(
                AppleVirtualizationHandleDiagnostics.Missing(ProviderId, TargetPath(expectedKind, route.BackingResourceId)));
        }

        if (handleProviderGeneration != _providerGeneration || providerHandle.Value.Generation != _providerGeneration)
        {
            ulong observed = handleProviderGeneration == providerHandle.Value.Generation ? handleProviderGeneration : providerHandle.Value.Generation;
            return Failure<AppleVirtualizationLedgerEntry<TResource, TStatus>>(
                AppleVirtualizationHandleDiagnostics.Stale(
                    ProviderId,
                    TargetPath(expectedKind, route.BackingResourceId),
                    _providerGeneration,
                    observed));
        }

        if (!_handlesByToken.TryGetValue(providerHandle.Value.Token, out HandleIndexEntry index))
        {
            return Failure<AppleVirtualizationLedgerEntry<TResource, TStatus>>(
                AppleVirtualizationHandleDiagnostics.Missing(ProviderId, TargetPath(expectedKind, route.BackingResourceId)));
        }

        if (index.Kind != expectedKind)
        {
            return Failure<AppleVirtualizationLedgerEntry<TResource, TStatus>>(
                AppleVirtualizationHandleDiagnostics.WrongKind(
                    ProviderId,
                    TargetPath(expectedKind, route.BackingResourceId),
                    KindName(expectedKind),
                    KindName(index.Kind)));
        }

        if (!entries.TryGetValue(index.Key, out AppleVirtualizationLedgerEntry<TResource, TStatus>? entry))
        {
            return Failure<AppleVirtualizationLedgerEntry<TResource, TStatus>>(
                AppleVirtualizationHandleDiagnostics.Missing(ProviderId, TargetPath(expectedKind, route.BackingResourceId)));
        }

        return Success(entry);
    }

    private bool Remove<TResource, TStatus>(
        Dictionary<AppleVirtualizationResourceKey, AppleVirtualizationLedgerEntry<TResource, TStatus>> entries,
        ResourceRef<TResource> resource)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TStatus : ResourceStatus
    {
        AppleVirtualizationResourceKey key = ToKey(resource);
        if (!entries.Remove(key, out AppleVirtualizationLedgerEntry<TResource, TStatus>? entry))
        {
            return false;
        }

        _handlesByToken.Remove(entry.ProviderHandle.Token);
        return true;
    }

    private string CreateToken<TResource>(AppleVirtualizationLedgerResourceKind kind, ResourceMetadata<TResource> metadata)
        where TResource : IExecutionResourceMarker
    {
        long sequence = ++_tokenSequence;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{KindName(kind)}:{metadata.Scope.Value}:{metadata.Id.Value}:g{_providerGeneration}:h{sequence}");
    }

    private static AppleVirtualizationLedgerLookup<TEntry> Success<TEntry>(TEntry entry)
        where TEntry : class =>
        new(entry, Diagnostic: null);

    private static AppleVirtualizationLedgerLookup<TEntry> Failure<TEntry>(Diagnostic diagnostic)
        where TEntry : class =>
        new(Entry: null, diagnostic);

    private static TargetKind TargetKindFor(AppleVirtualizationLedgerResourceKind kind) =>
        new($"apple-virtualization.{KindName(kind)}");

    private static SchemaId SchemaIdFor(AppleVirtualizationLedgerResourceKind kind) =>
        new($"hpd.execution.apple-virtualization.handle.{KindName(kind)}.v1");

    private static string KindName(AppleVirtualizationLedgerResourceKind kind) =>
        kind switch
        {
            AppleVirtualizationLedgerResourceKind.RuntimeHost => "runtime-host",
            AppleVirtualizationLedgerResourceKind.ExecutionUnit => "execution-unit",
            AppleVirtualizationLedgerResourceKind.ContentProjection => "content-projection",
            AppleVirtualizationLedgerResourceKind.ProcessInvocation => "process-invocation",
            AppleVirtualizationLedgerResourceKind.Network => "network",
            AppleVirtualizationLedgerResourceKind.NetworkMembership => "network-membership",
            AppleVirtualizationLedgerResourceKind.ServiceDiscovery => "service-discovery",
            AppleVirtualizationLedgerResourceKind.PublishedEndpoint => "published-endpoint",
            AppleVirtualizationLedgerResourceKind.AuthorityBinding => "authority-binding",
            AppleVirtualizationLedgerResourceKind.EngineControlPlane => "engine-control-plane",
            _ => "unknown",
        };

    private static string TargetPath(AppleVirtualizationLedgerResourceKind kind, string? resourceId) =>
        resourceId is null ? KindName(kind) : string.Create(CultureInfo.InvariantCulture, $"{KindName(kind)}/{resourceId}");

    private static TargetRouteSegmentKind SegmentKindFor(AppleVirtualizationLedgerResourceKind kind) =>
        kind switch
        {
            AppleVirtualizationLedgerResourceKind.RuntimeHost => TargetRouteSegmentKind.RuntimeHost,
            AppleVirtualizationLedgerResourceKind.ExecutionUnit => TargetRouteSegmentKind.ExecutionUnit,
            AppleVirtualizationLedgerResourceKind.ContentProjection => TargetRouteSegmentKind.ContentProjection,
            AppleVirtualizationLedgerResourceKind.ProcessInvocation => TargetRouteSegmentKind.ProcessInvocation,
            AppleVirtualizationLedgerResourceKind.Network => TargetRouteSegmentKind.Network,
            AppleVirtualizationLedgerResourceKind.NetworkMembership => TargetRouteSegmentKind.Network,
            AppleVirtualizationLedgerResourceKind.ServiceDiscovery => TargetRouteSegmentKind.Network,
            AppleVirtualizationLedgerResourceKind.PublishedEndpoint => TargetRouteSegmentKind.Endpoint,
            AppleVirtualizationLedgerResourceKind.AuthorityBinding => TargetRouteSegmentKind.ProviderOpaque,
            AppleVirtualizationLedgerResourceKind.EngineControlPlane => TargetRouteSegmentKind.ProviderOpaque,
            _ => TargetRouteSegmentKind.ProviderOpaque,
        };

    private static TargetHandleLifetime LifetimeFor(AppleVirtualizationLedgerResourceKind kind) =>
        kind == AppleVirtualizationLedgerResourceKind.ProcessInvocation
            ? TargetHandleLifetime.LiveCapability
            : TargetHandleLifetime.DurableAddress;

    private static TargetHandleAuthority AuthorityFor(AppleVirtualizationLedgerResourceKind kind) =>
        kind switch
        {
            AppleVirtualizationLedgerResourceKind.RuntimeHost => TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
            AppleVirtualizationLedgerResourceKind.ExecutionUnit => TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Invoke,
            AppleVirtualizationLedgerResourceKind.ContentProjection => TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Read | TargetHandleAuthority.Write,
            AppleVirtualizationLedgerResourceKind.ProcessInvocation => TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Read | TargetHandleAuthority.Write | TargetHandleAuthority.Invoke,
            AppleVirtualizationLedgerResourceKind.Network => TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
            AppleVirtualizationLedgerResourceKind.NetworkMembership => TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
            AppleVirtualizationLedgerResourceKind.ServiceDiscovery => TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
            AppleVirtualizationLedgerResourceKind.PublishedEndpoint => TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
            AppleVirtualizationLedgerResourceKind.AuthorityBinding => TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
            AppleVirtualizationLedgerResourceKind.EngineControlPlane => TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
            _ => TargetHandleAuthority.None,
        };

    private static AppleVirtualizationLedgerResourceKind KindFrom(Type resourceType)
    {
        if (resourceType == typeof(RuntimeHost))
        {
            return AppleVirtualizationLedgerResourceKind.RuntimeHost;
        }

        if (resourceType == typeof(ExecutionUnit))
        {
            return AppleVirtualizationLedgerResourceKind.ExecutionUnit;
        }

        if (resourceType == typeof(ContentProjection))
        {
            return AppleVirtualizationLedgerResourceKind.ContentProjection;
        }

        if (resourceType == typeof(ProcessInvocation))
        {
            return AppleVirtualizationLedgerResourceKind.ProcessInvocation;
        }

        if (resourceType == typeof(Network))
        {
            return AppleVirtualizationLedgerResourceKind.Network;
        }

        if (resourceType == typeof(NetworkMembership))
        {
            return AppleVirtualizationLedgerResourceKind.NetworkMembership;
        }

        if (resourceType == typeof(ServiceDiscovery))
        {
            return AppleVirtualizationLedgerResourceKind.ServiceDiscovery;
        }

        if (resourceType == typeof(PublishedEndpoint))
        {
            return AppleVirtualizationLedgerResourceKind.PublishedEndpoint;
        }

        if (resourceType == typeof(AuthorityBinding))
        {
            return AppleVirtualizationLedgerResourceKind.AuthorityBinding;
        }

        if (resourceType == typeof(EngineControlPlane))
        {
            return AppleVirtualizationLedgerResourceKind.EngineControlPlane;
        }

        throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "Unsupported Apple Virtualization ledger resource type.");
    }

    private readonly record struct HandlePair<TResource>(
        TargetHandle<TResource> TargetHandle,
        ProviderOpaqueHandle ProviderHandle)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker;

    private readonly record struct HandleIndexEntry(
        AppleVirtualizationLedgerResourceKind Kind,
        AppleVirtualizationResourceKey Key,
        ulong ProviderGeneration);
}
