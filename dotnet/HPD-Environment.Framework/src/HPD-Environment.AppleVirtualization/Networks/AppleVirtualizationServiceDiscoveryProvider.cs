namespace HPD.Environment.AppleVirtualization.Networks;

using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

public sealed class AppleVirtualizationServiceDiscoveryProvider : IServiceDiscoveryProvider
{
    private const int MaxDiscoveryRecords =
        DerivedServiceDiscovery.MaxRecords;

    private readonly AppleVirtualizationProviderStateLedger _ledger;

    internal AppleVirtualizationServiceDiscoveryProvider(AppleVirtualizationProviderStateLedger ledger)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    public ProviderId ProviderId => AppleVirtualizationProviderDescriptor.ProviderId;

    public ValueTask<ServiceDiscoveryStatus> EnsureServiceDiscoveryAsync(
        ResourceMetadata<ServiceDiscovery> metadata,
        ServiceDiscoverySpec spec,
        ServiceDiscoveryStatus? observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        DiscoveryValidation validation = ValidateSpec(spec);
        if (validation.FatalDiagnostic is { } fatal)
        {
            ServiceDiscoveryStatus failed = Status(
                metadata,
                ServiceDiscoveryPhase.Failed,
                validation.Limitations,
                Array.Empty<DiscoveryRecord>(),
                validation.Conditions,
                fatal);
            return ValueTask.FromResult(_ledger.UpsertServiceDiscovery(metadata, failed, spec).Status);
        }

        if (spec.Network is { } networkRef)
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<Network, NetworkStatus>> networkLookup =
                _ledger.TryGetNetwork(networkRef);
            if (!networkLookup.Succeeded)
            {
                ServiceDiscoveryStatus failed = Status(
                    metadata,
                    ServiceDiscoveryPhase.Failed,
                    validation.Limitations,
                    Array.Empty<DiscoveryRecord>(),
                    validation.Conditions,
                    networkLookup.Diagnostic ?? DiscoveryDiagnostic(
                        DiagnosticSeverity.Error,
                        "AppleVirtualization.ServiceDiscoveryNetworkMissing",
                        "The Apple Virtualization service discovery resource could not resolve its network.",
                        "service-discovery.network"));
                return ValueTask.FromResult(_ledger.UpsertServiceDiscovery(metadata, failed, spec).Status);
            }

            if (networkLookup.Entry!.Status.NetworkPhase == NetworkPhase.Failed)
            {
                ServiceDiscoveryStatus failed = Status(
                    metadata,
                    ServiceDiscoveryPhase.Failed,
                    validation.Limitations,
                    Array.Empty<DiscoveryRecord>(),
                    validation.Conditions,
                    DiscoveryDiagnostic(
                        DiagnosticSeverity.Error,
                        "AppleVirtualization.ServiceDiscoveryNetworkFailed",
                        "The Apple Virtualization service discovery resource cannot derive records from a failed network.",
                        "network/" + networkRef.Id.Value));
                return ValueTask.FromResult(_ledger.UpsertServiceDiscovery(metadata, failed, spec).Status);
            }
        }

        AppleVirtualizationNetworkMembershipSnapshot[] memberships = _ledger.GetActiveNetworkMemberships(spec.Network, spec.Host);
        DiscoveryRecord[] records = BuildRecords(spec, memberships, validation.Limitations, out bool truncated);
        IReadOnlyList<NetworkLimitation> limitations = truncated
            ? AppendLimitation(validation.Limitations, Limitation(
                NetworkDegradedFeature.InternalDns,
                CapabilityDegradationMode.PartiallyAvailable,
                "AppleVirtualization.ServiceDiscoveryRecordsTruncated",
                "Service discovery records were truncated to the provider's bounded L12 record limit."))
            : validation.Limitations;

        ServiceDiscoveryPhase phase = PhaseFor(spec, records.Length, limitations);
        ServiceDiscoveryStatus status = Status(
            metadata,
            phase,
            limitations,
            records,
            validation.Conditions,
            diagnostic: null);
        return ValueTask.FromResult(_ledger.UpsertServiceDiscovery(metadata, status, spec).Status);
    }

    public ValueTask<ServiceDiscoveryStatus> GetStatusAsync(
        ResourceRef<ServiceDiscovery> discovery,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ServiceDiscovery, ServiceDiscoveryStatus>> lookup =
            _ledger.TryGetServiceDiscovery(discovery);
        return ValueTask.FromResult(lookup.Succeeded
            ? lookup.Entry!.Status
            : new ServiceDiscoveryStatus
            {
                Phase = ResourcePhase.Failed,
                DiscoveryPhase = ServiceDiscoveryPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = [lookup.Diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(ProviderId, "service-discovery/" + discovery.Id.Value)],
            });
    }

    public ValueTask<IReadOnlyList<DiscoveryRecord>> ResolveAsync(
        ServiceDiscoveryQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ServiceDiscovery, ServiceDiscoveryStatus>> lookup =
            _ledger.TryGetServiceDiscovery(query.Discovery);
        if (!lookup.Succeeded)
        {
            return ValueTask.FromResult<IReadOnlyList<DiscoveryRecord>>(Array.Empty<DiscoveryRecord>());
        }

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
        _ledger.RemoveServiceDiscovery(discovery);
        return ValueTask.CompletedTask;
    }

    private static ServiceDiscoveryStatus Status(
        ResourceMetadata<ServiceDiscovery> metadata,
        ServiceDiscoveryPhase phase,
        IReadOnlyList<NetworkLimitation> limitations,
        IReadOnlyList<DiscoveryRecord> records,
        IReadOnlyList<Condition> extraConditions,
        Diagnostic? diagnostic)
    {
        IReadOnlyList<Condition> conditions = DiscoveryConditions(metadata.Generation, phase, limitations, records.Count, extraConditions);
        return new ServiceDiscoveryStatus
        {
            Phase = phase switch
            {
                ServiceDiscoveryPhase.Ready => ResourcePhase.Ready,
                ServiceDiscoveryPhase.Disabled => ResourcePhase.Ready,
                ServiceDiscoveryPhase.Failed => ResourcePhase.Failed,
                _ => ResourcePhase.Degraded,
            },
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            DiscoveryPhase = phase,
            RealizedCapabilities = Capabilities(records, limitations),
            Records = records,
            HostExportedDomains = Array.Empty<DnsName>(),
            EffectiveResolvers = Array.Empty<ProviderNamedEndpoint>(),
            Limitations = limitations,
            Conditions = conditions,
            Diagnostics = diagnostic is null ? Array.Empty<Diagnostic>() : [diagnostic],
        };
    }

    private static DiscoveryValidation ValidateSpec(ServiceDiscoverySpec spec)
    {
        var limitations = new List<NetworkLimitation>(4);
        var conditions = new List<Condition>(2);
        Diagnostic? fatal = null;

        if (spec.Scope == DiscoveryScope.ProviderDefined)
        {
            fatal = DiscoveryDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.ServiceDiscoveryScopeUnsupported",
                "Provider-defined service discovery scopes are not implemented by the Apple Virtualization L12 service discovery lane.",
                "service-discovery.scope");
        }

        if (spec.Scope == DiscoveryScope.Network && spec.Network is null)
        {
            fatal ??= DiscoveryDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.ServiceDiscoveryNetworkMissing",
                "Network-scoped service discovery requires a network resource reference.",
                "service-discovery.network");
        }

        if (spec.Scope is DiscoveryScope.HostExported or DiscoveryScope.HostResolverImported)
        {
            limitations.Add(Limitation(
                spec.Scope == DiscoveryScope.HostExported ? NetworkDegradedFeature.HostDnsExport : NetworkDegradedFeature.HostResolverImport,
                CapabilityDegradationMode.Unsupported,
                "AppleVirtualization.HostDiscoveryScopeUnsupported",
                "Host DNS export/import scopes are not proven by Apple Virtualization network APIs and remain unsupported in L12."));
        }

        if (spec.RequestHostExport)
        {
            limitations.Add(Limitation(
                NetworkDegradedFeature.HostDnsExport,
                CapabilityDegradationMode.Unsupported,
                "AppleVirtualization.HostDnsExportUnsupported",
                "Apple Virtualization L12 service discovery does not export records to the host DNS resolver."));
        }

        if (spec.RequestHostResolverImport)
        {
            limitations.Add(Limitation(
                NetworkDegradedFeature.HostResolverImport,
                CapabilityDegradationMode.Unsupported,
                "AppleVirtualization.HostResolverImportUnsupported",
                "Apple Virtualization L12 service discovery does not import the host resolver into HPD discovery resources."));
        }

        if (spec.SearchDomains.Count > 0)
        {
            limitations.Add(Limitation(
                NetworkDegradedFeature.InternalDns,
                CapabilityDegradationMode.PartiallyAvailable,
                "AppleVirtualization.SearchDomainsDiagnosticOnly",
                "Search domains are retained as discovery metadata but do not configure host or guest DNS in L12."));
        }

        if (spec.DefaultRecordPolicy == DefaultDiscoveryRecordPolicy.None && spec.Records.Count == 0)
        {
            conditions.Add(Condition(
                "AppleVirtualization.ServiceDiscoveryDisabled",
                ConditionStatus.True,
                "NoRecordsRequested",
                "Service discovery has no default or explicit record policy to publish.",
                DiagnosticSeverity.Info));
        }

        return new DiscoveryValidation(limitations, conditions, fatal);
    }

    private static DiscoveryRecord[] BuildRecords(
        ServiceDiscoverySpec spec,
        AppleVirtualizationNetworkMembershipSnapshot[] memberships,
        IReadOnlyList<NetworkLimitation> limitations,
        out bool truncated)
    {
        truncated = false;
        var records = new List<DiscoveryRecord>(Math.Min(MaxDiscoveryRecords, spec.Records.Count + memberships.Length * 4));
        TimeSpan ttl =
            DerivedServiceDiscovery.BoundTtl(spec.DefaultTtl);

        if (spec.DefaultRecordPolicy is not (DefaultDiscoveryRecordPolicy.None or DefaultDiscoveryRecordPolicy.ExplicitOnly))
        {
            for (int i = 0; i < memberships.Length && records.Count < MaxDiscoveryRecords; i++)
            {
                AddMembershipRecords(spec, memberships[i], ttl, records);
            }

            truncated = memberships.Length != 0 && WouldAddMoreMembershipRecords(spec, memberships, records.Count);
        }

        for (int i = 0; i < spec.Records.Count && records.Count < MaxDiscoveryRecords; i++)
        {
            records.Add(spec.Records[i].Ttl is { } explicitTtl
                ? new DiscoveryRecord(
                    spec.Records[i].Name,
                    spec.Records[i].Kind,
                    spec.Records[i].Target,
                    DerivedServiceDiscovery.BoundTtl(explicitTtl))
                : new DiscoveryRecord(spec.Records[i].Name, spec.Records[i].Kind, spec.Records[i].Target, ttl));
        }

        if (spec.Records.Count > 0 && records.Count == MaxDiscoveryRecords && spec.Records.Count + CountPotentialMembershipRecords(spec, memberships) > MaxDiscoveryRecords)
        {
            truncated = true;
        }

        return records.Count == 0 ? [] : records.ToArray();
    }

    private static void AddMembershipRecords(
        ServiceDiscoverySpec discovery,
        AppleVirtualizationNetworkMembershipSnapshot membership,
        TimeSpan ttl,
        List<DiscoveryRecord> records)
    {
        IReadOnlyList<NetworkAddressAssignment> addresses = membership.Status.Addresses;
        if (addresses.Count == 0)
        {
            return;
        }

        ResourceRef<NetworkMembership> membershipRef = membership.Resource;
        if (membership.Spec.Hostname is { } hostname)
        {
            AddAddressRecords(new DnsName(hostname.Value), addresses, membershipRef, ttl, records);
        }

        if (discovery.DefaultRecordPolicy == DefaultDiscoveryRecordPolicy.MembershipHostnamesAndAliases)
        {
            for (int i = 0; i < membership.Spec.Aliases.Count; i++)
            {
                AddAddressRecords(new DnsName(membership.Spec.Aliases[i].Value), addresses, membershipRef, ttl, records);
            }
        }

        for (int i = 0; i < membership.Spec.ServiceNames.Count && records.Count < MaxDiscoveryRecords; i++)
        {
            records.Add(new DiscoveryRecord(
                new DnsName(membership.Spec.ServiceNames[i].Value),
                DiscoveryRecordKind.Service,
                new DiscoveryRecordTarget(null, membership.Spec.ServiceNames[i], membershipRef, null, NetworkTransport.Tcp, null),
                ttl,
                IsDerivedFromMembership: true));
        }
    }

    private static void AddAddressRecords(
        DnsName name,
        IReadOnlyList<NetworkAddressAssignment> addresses,
        ResourceRef<NetworkMembership> membership,
        TimeSpan ttl,
        List<DiscoveryRecord> records)
    {
        for (int i = 0; i < addresses.Count && records.Count < MaxDiscoveryRecords; i++)
        {
            NetworkAddressAssignment assignment = addresses[i];
            records.Add(new DiscoveryRecord(
                name,
                assignment.Address.Family == NetworkAddressFamily.IPv6 ? DiscoveryRecordKind.AAAA : DiscoveryRecordKind.A,
                new DiscoveryRecordTarget(assignment.Address, null, membership, null, null, null),
                ttl,
                IsDerivedFromMembership: true));
        }
    }

    private static bool WouldAddMoreMembershipRecords(
        ServiceDiscoverySpec spec,
        AppleVirtualizationNetworkMembershipSnapshot[] memberships,
        int emittedCount) =>
        emittedCount >= MaxDiscoveryRecords &&
        CountPotentialMembershipRecords(spec, memberships) > MaxDiscoveryRecords;

    private static int CountPotentialMembershipRecords(
        ServiceDiscoverySpec spec,
        AppleVirtualizationNetworkMembershipSnapshot[] memberships)
    {
        int count = 0;
        for (int i = 0; i < memberships.Length; i++)
        {
            AppleVirtualizationNetworkMembershipSnapshot membership = memberships[i];
            int nameCount = membership.Spec.Hostname is null ? 0 : 1;
            if (spec.DefaultRecordPolicy == DefaultDiscoveryRecordPolicy.MembershipHostnamesAndAliases)
            {
                nameCount += membership.Spec.Aliases.Count;
            }

            count += nameCount * membership.Status.Addresses.Count;
            count += membership.Spec.ServiceNames.Count;
        }

        return count;
    }

    private static ServiceDiscoveryPhase PhaseFor(
        ServiceDiscoverySpec spec,
        int recordCount,
        IReadOnlyList<NetworkLimitation> limitations)
    {
        if (spec.DefaultRecordPolicy == DefaultDiscoveryRecordPolicy.None && spec.Records.Count == 0)
        {
            return ServiceDiscoveryPhase.Disabled;
        }

        return limitations.Count == 0 || recordCount > 0
            ? limitations.Count == 0 ? ServiceDiscoveryPhase.Ready : ServiceDiscoveryPhase.Degraded
            : ServiceDiscoveryPhase.Degraded;
    }

    private static DiscoveryCapabilitySet Capabilities(
        IReadOnlyList<DiscoveryRecord> records,
        IReadOnlyList<NetworkLimitation> limitations)
    {
        DiscoveryCapabilitySet capabilities = DiscoveryCapabilitySet.None;
        for (int i = 0; i < records.Count; i++)
        {
            capabilities |= records[i].Kind switch
            {
                DiscoveryRecordKind.A => DiscoveryCapabilitySet.ARecords,
                DiscoveryRecordKind.AAAA => DiscoveryCapabilitySet.AaaaRecords,
                DiscoveryRecordKind.CName => DiscoveryCapabilitySet.CNameRecords,
                DiscoveryRecordKind.Service => DiscoveryCapabilitySet.ServiceRecords,
                _ => DiscoveryCapabilitySet.None,
            };
        }

        return capabilities;
    }

    private static IReadOnlyList<NetworkLimitation> AppendLimitation(
        IReadOnlyList<NetworkLimitation> existing,
        NetworkLimitation limitation)
    {
        if (existing.Count == 0)
        {
            return [limitation];
        }

        NetworkLimitation[] limitations = new NetworkLimitation[existing.Count + 1];
        for (int i = 0; i < existing.Count; i++)
        {
            limitations[i] = existing[i];
        }

        limitations[^1] = limitation;
        return limitations;
    }

    private static IReadOnlyList<Condition> DiscoveryConditions(
        ResourceGeneration generation,
        ServiceDiscoveryPhase phase,
        IReadOnlyList<NetworkLimitation> limitations,
        int recordCount,
        IReadOnlyList<Condition> extra)
    {
        var conditions = new Condition[extra.Count + 1];
        conditions[0] = new Condition(
            "AppleVirtualization.ServiceDiscoveryRecords",
            phase is ServiceDiscoveryPhase.Ready or ServiceDiscoveryPhase.Degraded ? ConditionStatus.True : ConditionStatus.False,
            phase.ToString(),
            recordCount == 0 ? "No service discovery records are currently published." : "Service discovery records were derived from HPD network membership state.",
            DateTimeOffset.UtcNow,
            generation,
            limitations.Count == 0 ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning);
        for (int i = 0; i < extra.Count; i++)
        {
            conditions[i + 1] = extra[i];
        }

        return conditions;
    }

    private static Condition Condition(
        string type,
        ConditionStatus status,
        string reason,
        string message,
        DiagnosticSeverity severity) =>
        new(type, status, reason, message, DateTimeOffset.UtcNow, default, severity);

    private static NetworkLimitation Limitation(
        NetworkDegradedFeature feature,
        CapabilityDegradationMode mode,
        string reasonCode,
        string message) =>
        new(feature, mode, reasonCode, message);

    private static Diagnostic DiscoveryDiagnostic(
        DiagnosticSeverity severity,
        string code,
        string message,
        string targetPath) =>
        new()
        {
            Severity = severity,
            Code = new DiagnosticCode(code),
            Message = message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    private readonly record struct DiscoveryValidation(
        IReadOnlyList<NetworkLimitation> Limitations,
        IReadOnlyList<Condition> Conditions,
        Diagnostic? FatalDiagnostic);
}
