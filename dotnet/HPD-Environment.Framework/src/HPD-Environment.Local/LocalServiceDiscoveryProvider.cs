namespace HPD.Environment.Local;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class LocalServiceDiscoveryProvider(
    LocalProviderState state) : IServiceDiscoveryProvider
{
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("service-discovery"),
        TargetRouteSegmentKind.Network,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe |
        TargetHandleAuthority.Read,
        new SchemaId(
            "hpd.execution.local.service-discovery.handle.v1"));

    public ProviderId ProviderId =>
        LocalEnvironmentProviderDescriptor.ProviderId;

    public ValueTask<ServiceDiscoveryStatus>
        EnsureServiceDiscoveryAsync(
            ResourceMetadata<ServiceDiscovery> metadata,
            ServiceDiscoverySpec spec,
            ServiceDiscoveryStatus? observed,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();
        Diagnostic? invalid = Validate(spec);
        if (invalid is not null)
            return ValueTask.FromResult(Failed(
                metadata,
                invalid.Code.Value,
                invalid.Message));

        if (spec.Network is { } network)
        {
            ProviderLedgerLookup<
                ProviderResourceEntry<
                    Network,
                    NetworkSpec,
                    NetworkStatus>> networkLookup =
                state.Ledger.TryGet<
                    Network,
                    NetworkSpec,
                    NetworkStatus>(network);
            if (!networkLookup.Succeeded ||
                networkLookup.Entry!.Status.NetworkPhase !=
                    NetworkPhase.Ready)
                return ValueTask.FromResult(Failed(
                    metadata,
                    networkLookup.Diagnostic?.Code.Value ??
                        "LocalEnvironment.ServiceDiscoveryNetworkNotReady",
                    networkLookup.Diagnostic?.Message ??
                        "The discovery network is not ready."));
        }

        IReadOnlyList<DiscoveryRecord> membershipRecords =
            state.Ledger.List<
                    NetworkMembership,
                    NetworkMembershipSpec,
                    NetworkMembershipStatus>(metadata.Scope)
                .Where(entry =>
                    entry.Status.MembershipPhase ==
                        NetworkMembershipPhase.Ready &&
                    (spec.Network is null ||
                     entry.Spec.Network.Id ==
                        spec.Network.Value.Id) &&
                    state.IsNetworkResourceBoundToCurrentEngine(
                        NetworkKey(
                            entry.Resource.Scope,
                            entry.Resource.Id.Value)))
                .SelectMany(static entry =>
                    entry.Status.RegisteredRecords)
                .ToArray();
        IReadOnlyList<DiscoveryRecord> records =
            DerivedServiceDiscovery.Build(
                spec,
                membershipRecords);
        bool truncated =
            spec.Records.Count + membershipRecords.Count >
                DerivedServiceDiscovery.MaxRecords;
        IReadOnlyList<NetworkLimitation> limitations = truncated
            ?
            [
                new NetworkLimitation(
                    NetworkDegradedFeature.ServiceRecords,
                    CapabilityDegradationMode.PartiallyAvailable,
                    "LocalEnvironment.ServiceDiscoveryRecordsTruncated",
                    "Service discovery records were truncated to the bounded provider limit.")
            ]
            : [];
        var status = new ServiceDiscoveryStatus
        {
            Phase = truncated
                ? ResourcePhase.Degraded
                : ResourcePhase.Ready,
            DiscoveryPhase = truncated
                ? ServiceDiscoveryPhase.Degraded
                : ServiceDiscoveryPhase.Ready,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            RealizedCapabilities =
                DiscoveryCapabilitySet.ServiceRecords |
                (records.Any(record =>
                    record.Kind == DiscoveryRecordKind.A)
                    ? DiscoveryCapabilitySet.ARecords
                    : DiscoveryCapabilitySet.None) |
                (records.Any(record =>
                    record.Kind == DiscoveryRecordKind.AAAA)
                    ? DiscoveryCapabilitySet.AaaaRecords
                    : DiscoveryCapabilitySet.None) |
                (records.Any(record =>
                    record.Kind == DiscoveryRecordKind.CName)
                    ? DiscoveryCapabilitySet.CNameRecords
                    : DiscoveryCapabilitySet.None),
            Records = records,
            Limitations = limitations,
        };
        ProviderResourceEntry<
            ServiceDiscovery,
            ServiceDiscoverySpec,
            ServiceDiscoveryStatus> entry = state.Ledger.Upsert(
            metadata,
            spec,
            status,
            Shape);
        status = status with { Handle = entry.TargetHandle };
        state.Ledger.Upsert(
            metadata,
            spec,
            status,
            Shape);
        state.BindNetworkResourceToCurrentEngine(
            DiscoveryKey(metadata.Scope, metadata.Id.Value));
        return ValueTask.FromResult(status);
    }

    public ValueTask<ServiceDiscoveryStatus> GetStatusAsync(
        ResourceRef<ServiceDiscovery> discovery,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ServiceDiscovery,
                ServiceDiscoverySpec,
                ServiceDiscoveryStatus>> lookup =
            state.Ledger.TryGet<
                ServiceDiscovery,
                ServiceDiscoverySpec,
                ServiceDiscoveryStatus>(discovery);
        if (!lookup.Succeeded)
            return ValueTask.FromResult(Failed(
                Metadata(discovery),
                lookup.Diagnostic!.Code.Value,
                lookup.Diagnostic.Message));
        if (!state.IsNetworkResourceBoundToCurrentEngine(
                DiscoveryKey(
                    discovery.Scope,
                    discovery.Id.Value)))
            return ValueTask.FromResult(lookup.Entry!.Status with
            {
                Phase = ResourcePhase.Failed,
                DiscoveryPhase = ServiceDiscoveryPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics =
                [
                    Error(
                        "LocalEnvironment.ServiceDiscoveryEngineIncarnationStale",
                        "Service discovery belongs to a stale Local engine incarnation.")
                ],
            });
        return ValueTask.FromResult(lookup.Entry!.Status);
    }

    public ValueTask<IReadOnlyList<DiscoveryRecord>> ResolveAsync(
        ServiceDiscoveryQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ServiceDiscovery,
                ServiceDiscoverySpec,
                ServiceDiscoveryStatus>> lookup =
            state.Ledger.TryGet<
                ServiceDiscovery,
                ServiceDiscoverySpec,
                ServiceDiscoveryStatus>(query.Discovery);
        if (!lookup.Succeeded ||
            !state.IsNetworkResourceBoundToCurrentEngine(
                DiscoveryKey(
                    query.Discovery.Scope,
                    query.Discovery.Id.Value)))
            return ValueTask.FromResult<
                IReadOnlyList<DiscoveryRecord>>([]);
        return ValueTask.FromResult(
            DerivedServiceDiscovery.Resolve(
                lookup.Entry!.Status.Records,
                query));
    }

    public ValueTask ReleaseAsync(
        ResourceRef<ServiceDiscovery> discovery,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.ReleaseNetworkResource(
            DiscoveryKey(
                discovery.Scope,
                discovery.Id.Value));
        state.Ledger.Remove<
            ServiceDiscovery,
            ServiceDiscoverySpec,
            ServiceDiscoveryStatus>(discovery);
        return ValueTask.CompletedTask;
    }

    private Diagnostic? Validate(ServiceDiscoverySpec spec)
    {
        if (spec.Scope == DiscoveryScope.Network &&
            spec.Network is null)
            return Error(
                "LocalEnvironment.ServiceDiscoveryNetworkRequired",
                "Network-scoped discovery requires an owned network.");
        if (spec.Scope is
            DiscoveryScope.HostExported or
            DiscoveryScope.HostResolverImported or
            DiscoveryScope.ProviderDefined)
            return Error(
                "LocalEnvironment.ServiceDiscoveryScopeUnsupported",
                "The Local provider does not export to or import from the host resolver.");
        if (spec.RequestHostExport ||
            spec.RequestHostResolverImport)
            return Error(
                "LocalEnvironment.ServiceDiscoveryHostResolverBoundaryRejected",
                "The Local provider does not modify or import the host resolver.");
        if (spec.Records.Count >
            DerivedServiceDiscovery.MaxRecords * 2)
            return Error(
                "LocalEnvironment.ServiceDiscoveryRecordInputTooLarge",
                "The requested discovery record set exceeds the bounded input limit.");
        if (spec.SearchDomains.Count > 16)
            return Error(
                "LocalEnvironment.ServiceDiscoverySearchDomainsTooLarge",
                "At most 16 bounded search domains are accepted.");
        return null;
    }

    private ServiceDiscoveryStatus Failed(
        ResourceMetadata<ServiceDiscovery> metadata,
        string code,
        string message) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            DiscoveryPhase = ServiceDiscoveryPhase.Failed,
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

    private static string NetworkKey(
        ResourceScope scope,
        string id) =>
        $"{nameof(NetworkMembership)}:{scope}:{id}";

    private static string DiscoveryKey(
        ResourceScope scope,
        string id) =>
        $"{nameof(ServiceDiscovery)}:{scope}:{id}";

    private static ResourceMetadata<ServiceDiscovery> Metadata(
        ResourceRef<ServiceDiscovery> resource) =>
        new()
        {
            Id = resource.Id,
            Kind = new ResourceKind("ServiceDiscovery"),
            Scope = resource.Scope,
            Generation =
                resource.Generation ?? new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };
}
