namespace HPD.Environment.AppleVirtualization.Networks;

using System.Globalization;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.Contracts;

public sealed class AppleVirtualizationEndpointPublicationProvider : IEndpointPublicationProvider
{
    private readonly AppleVirtualizationProviderStateLedger _ledger;
    private readonly IAppleVirtualizationHelperClient _helper;
    private long _requestSequence;

    internal AppleVirtualizationEndpointPublicationProvider(
        AppleVirtualizationProviderStateLedger ledger,
        IAppleVirtualizationHelperClient helper)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _helper = helper ?? throw new ArgumentNullException(nameof(helper));
    }

    public ProviderId ProviderId => AppleVirtualizationProviderDescriptor.ProviderId;

    public async ValueTask<PublishedEndpointStatus> EnsurePublishedEndpointAsync(
        ResourceMetadata<PublishedEndpoint> metadata,
        PublishedEndpointSpec spec,
        PublishedEndpointStatus? observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        EndpointValidation validation = ValidateSpec(spec);
        RouteResolution route = ResolveRoute(spec);
        if (validation.FatalDiagnostic is { } validationFatal)
        {
            return Store(metadata, spec, FailedStatus(metadata, spec, validation.Limitations, validation.Diagnostics, validationFatal));
        }

        if (route.Diagnostic is { } routeDiagnostic)
        {
            return Store(metadata, spec, FailedStatus(metadata, spec, validation.Limitations, validation.Diagnostics, routeDiagnostic));
        }

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.EndpointPublish, AppleVirtualizationHelperProtocol.EndpointPublicationRequestSchema) with
            {
                ResourceKind = metadata.Kind,
                ResourceId = metadata.Id.Value,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderGeneration = _ledger.ProviderGeneration,
                EndpointPublicationRequest = new AppleVirtualizationEndpointPublicationRequest
                {
                    EndpointId = metadata.Id.Value,
                    Action = AppleVirtualizationEndpointPublicationAction.Publish,
                    ListenerKind = spec.Listener.Kind,
                    Transport = spec.Listener.Transport,
                    ExposureScope = spec.ExposurePolicy.Scope,
                    ListenerAddress = ListenerAddress(spec.Listener.Address),
                    RequestedPort = FirstPort(spec.Listener.Ports),
                    AllowEphemeralPort = spec.ExposurePolicy.AllowEphemeralPort,
                    RequireStableListener = spec.ExposurePolicy.RequireStableListener,
                    TargetKind = spec.Target.Kind,
                    TargetResourceId = route.TargetResourceId,
                    TargetAddress = route.TargetAddress,
                    TargetPort = route.TargetPort,
                    TargetSocketPath = route.TargetSocketPath,
                    ReconcileRouteOnTargetRestart = spec.ReconcileRouteOnTargetRestart,
                    RequireRouteHealth = true,
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            return Store(metadata, spec, FailedStatus(metadata, spec, validation.Limitations, validation.Diagnostics, ToDiagnostic(response.Error, "endpoint.publish")));
        }

        if (response.EndpointPublicationResponse is not { } endpoint)
        {
            return Store(metadata, spec, FailedStatus(metadata, spec, validation.Limitations, validation.Diagnostics, EndpointDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EndpointMissingHelperPayload",
                "The Apple Virtualization helper did not return an endpoint publication payload.",
                "endpoint/" + metadata.Id.Value)));
        }

        PublishedEndpointStatus status = StatusFromHelper(metadata, spec, route, validation.Limitations, validation.Diagnostics, endpoint);
        return Store(metadata, spec, status);
    }

    public ValueTask<PublishedEndpointStatus> GetStatusAsync(
        ResourceRef<PublishedEndpoint> endpoint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<PublishedEndpoint, PublishedEndpointStatus>> lookup =
            _ledger.TryGetPublishedEndpoint(endpoint);
        return ValueTask.FromResult(lookup.Succeeded
            ? lookup.Entry!.Status
            : new PublishedEndpointStatus
            {
                Phase = ResourcePhase.Failed,
                EndpointPhase = PublishedEndpointPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = [lookup.Diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(ProviderId, "published-endpoint/" + endpoint.Id.Value)],
            });
    }

    public async ValueTask ReleasePublishedEndpointAsync(
        ResourceRef<PublishedEndpoint> endpoint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<PublishedEndpoint, PublishedEndpointStatus>> lookup =
            _ledger.TryGetPublishedEndpoint(endpoint);
        if (!lookup.Succeeded)
        {
            return;
        }

        AppleVirtualizationLedgerEntry<PublishedEndpoint, PublishedEndpointStatus> entry = lookup.Entry!;
        await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.EndpointRelease, AppleVirtualizationHelperProtocol.EndpointPublicationRequestSchema) with
            {
                ResourceKind = new ResourceKind("published-endpoint"),
                ResourceId = entry.Resource.Id.Value,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderGeneration = _ledger.ProviderGeneration,
                EndpointPublicationRequest = new AppleVirtualizationEndpointPublicationRequest
                {
                    EndpointId = entry.Resource.Id.Value,
                    Action = AppleVirtualizationEndpointPublicationAction.Release,
                },
            },
            cancellationToken).ConfigureAwait(false);

        _ledger.RemovePublishedEndpoint(endpoint);
    }

    private PublishedEndpointStatus Store(
        ResourceMetadata<PublishedEndpoint> metadata,
        PublishedEndpointSpec spec,
        PublishedEndpointStatus status) =>
        _ledger.UpsertPublishedEndpoint(metadata, status, spec).Status;

    private AppleVirtualizationHelperEnvelope Request(AppleVirtualizationHelperOperation operation, SchemaId schema) =>
        AppleVirtualizationHelperEnvelope.Request(
            operation,
            "apple-vz-endpoint-" + Interlocked.Increment(ref _requestSequence).ToString(CultureInfo.InvariantCulture),
            Interlocked.Read(ref _requestSequence),
            schema);

    private EndpointValidation ValidateSpec(PublishedEndpointSpec spec)
    {
        var limitations = new List<NetworkLimitation>(4);
        var diagnostics = new List<Diagnostic>(4);
        Diagnostic? fatal = null;

        if (spec.Listener.Kind != EndpointListenerKind.HostAddress)
        {
            fatal = EndpointDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EndpointListenerKindUnsupported",
                "The Apple Virtualization L12 endpoint bridge only supports HPD-owned host-address listeners.",
                "endpoint.listener.kind");
        }

        if (spec.Listener.Transport != NetworkTransport.Tcp)
        {
            limitations.Add(Limitation(
                NetworkDegradedFeature.TcpPublish,
                CapabilityDegradationMode.Unsupported,
                "AppleVirtualization.EndpointTransportUnsupported",
                "The Apple Virtualization L12 endpoint bridge supports host-local TCP only; UDP and other transports remain deferred."));
            fatal ??= EndpointDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EndpointTransportUnsupported",
                "The requested endpoint transport is not implemented by the L12 endpoint bridge.",
                "endpoint.listener.transport");
        }

        if (spec.ExposurePolicy.Scope != EndpointExposureScope.HostLocal)
        {
            limitations.Add(Limitation(
                NetworkDegradedFeature.TcpPublish,
                CapabilityDegradationMode.DisabledByPolicy,
                "AppleVirtualization.EndpointExposureUnsupported",
                "The Apple Virtualization L12 endpoint bridge only publishes host-local endpoints. LAN and external exposure are deferred to policy hardening."));
            fatal ??= EndpointDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EndpointExposureUnsupported",
                "The requested endpoint exposure scope is not supported by the L12 endpoint bridge.",
                "endpoint.exposure.scope");
        }

        if (spec.ExposurePolicy.Scope == EndpointExposureScope.HostLocal &&
            spec.Listener.Address.HasValue &&
            !IsLoopback(spec.Listener.Address.Value))
        {
            limitations.Add(Limitation(
                NetworkDegradedFeature.TcpPublish,
                CapabilityDegradationMode.DisabledByPolicy,
                "AppleVirtualization.EndpointHostLocalRequiresLoopback",
                "Host-local endpoint publication only binds loopback listener addresses."));
            fatal ??= EndpointDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EndpointHostLocalRequiresLoopback",
                "Host-local endpoints must use a loopback listener address.",
                "endpoint.listener.address");
        }

        if (spec.AuthorizationPolicy.RequireLoopbackClient)
        {
            diagnostics.Add(EndpointDiagnostic(
                DiagnosticSeverity.Info,
                "AppleVirtualization.EndpointLoopbackClientRequired",
                "Endpoint authorization requires loopback clients; L12 represents this requirement but does not issue credentials.",
                "endpoint.authorization.loopbackClient"));
        }

        if (!string.IsNullOrWhiteSpace(spec.AuthorizationPolicy.TokenAudience))
        {
            diagnostics.Add(EndpointDiagnostic(
                DiagnosticSeverity.Warning,
                "AppleVirtualization.EndpointTokenAudienceDiagnosticOnly",
                "Endpoint authorization requested a token audience, but L12 does not issue endpoint tokens; AuthorityBinding must own token material.",
                "endpoint.authorization.tokenAudience"));
        }

        if (spec.SensitivePolicy is { } sensitive)
        {
            AddSensitiveEndpointDiagnostics(sensitive, limitations, diagnostics);
            fatal ??= EndpointDiagnostic(
                DiagnosticSeverity.Error,
                SensitiveEndpointCode(sensitive.Kind),
                SensitiveEndpointMessage(sensitive.Kind),
                "endpoint.sensitive");
        }

        if (spec.Listener.Ports is null && !spec.ExposurePolicy.AllowEphemeralPort)
        {
            fatal ??= EndpointDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EndpointPortRequired",
                "A host-local endpoint requires an explicit port unless ephemeral ports are allowed.",
                "endpoint.listener.ports");
        }

        return new EndpointValidation(limitations, diagnostics, fatal);
    }

    private RouteResolution ResolveRoute(PublishedEndpointSpec spec) =>
        spec.Target.Kind switch
        {
            EndpointTargetKind.NetworkMembership => ResolveMembershipRoute(spec.Target.Membership, spec.Target.Port),
            EndpointTargetKind.UnitPort => ResolveUnitRoute(spec.Target.Unit, spec.Target.Port),
            EndpointTargetKind.ProcessPort => ResolveProcessRoute(spec.Target.Process, spec.Target.Port),
            EndpointTargetKind.ServiceName => ResolveServiceRoute(spec.RoutingNetwork, spec.Target.ServiceName, spec.Target.Port, spec.Target.Transport),
            EndpointTargetKind.UnixSocket => ResolveUnixSocketRoute(spec.Target.SocketPath),
            _ => RouteFailure("AppleVirtualization.EndpointTargetUnsupported", "The endpoint target kind is not implemented by the L12 endpoint bridge.", "endpoint.target.kind"),
        };

    private RouteResolution ResolveMembershipRoute(ResourceRef<NetworkMembership>? membership, NetworkPort? port)
    {
        if (membership is null)
        {
            return RouteFailure("AppleVirtualization.EndpointMembershipMissing", "Network-membership endpoint targets require a membership resource reference.", "endpoint.target.membership");
        }

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>> lookup =
            _ledger.TryGetNetworkMembership(membership.Value);
        if (!lookup.Succeeded)
        {
            return new RouteResolution(null, null, null, null, lookup.Diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(ProviderId, "network-membership/" + membership.Value.Id.Value));
        }

        return RouteForMembership(lookup.Entry!, port);
    }

    private RouteResolution ResolveUnitRoute(ResourceRef<ExecutionUnit>? unit, NetworkPort? port)
    {
        if (unit is null)
        {
            return RouteFailure("AppleVirtualization.EndpointUnitMissing", "Unit-port endpoint targets require an execution unit resource reference.", "endpoint.target.unit");
        }

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> unitLookup =
            _ledger.TryGetExecutionUnit(unit.Value);
        if (!unitLookup.Succeeded)
        {
            return new RouteResolution(null, null, null, null, unitLookup.Diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(ProviderId, "execution-unit/" + unit.Value.Id.Value));
        }

        ExecutionUnitStatus unitStatus = unitLookup.Entry!.Status;
        if (unitStatus.Phase != ResourcePhase.Ready ||
            unitStatus.UnitPhase is not ExecutionUnitPhase.Ready and not ExecutionUnitPhase.Running)
        {
            return RouteFailure("AppleVirtualization.EndpointUnitNotReady", "The target execution unit must be ready or running before an endpoint can be published.", "endpoint.target.unit");
        }

        if (unitStatus.NetworkMemberships.Count == 0)
        {
            return RouteFailure("AppleVirtualization.EndpointUnitMembershipMissing", "The target execution unit has no ready network membership to route to.", "endpoint.target.unit.network");
        }

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus>> membershipLookup =
            _ledger.TryGetNetworkMembership(unitStatus.NetworkMemberships[0]);
        return membershipLookup.Succeeded
            ? RouteForMembership(membershipLookup.Entry!, port)
            : new RouteResolution(null, null, null, null, membershipLookup.Diagnostic);
    }

    private RouteResolution ResolveProcessRoute(ResourceRef<ProcessInvocation>? process, NetworkPort? port)
    {
        if (process is null)
        {
            return RouteFailure("AppleVirtualization.EndpointProcessMissing", "Process-port endpoint targets require a process target handle.", "endpoint.target.process");
        }

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus>> lookup =
            _ledger.TryGetProcessInvocation(process.Value);
        if (!lookup.Succeeded)
        {
            return new RouteResolution(null, null, null, null, lookup.Diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(ProviderId, "process-invocation"));
        }

        ProcessInvocationStatus status = lookup.Entry!.Status;
        if (status.Phase != ResourcePhase.Ready ||
            status.ProcessPhase != ProcessInvocationPhase.Running)
        {
            return RouteFailure("AppleVirtualization.EndpointProcessNotRunning", "The target process must be running before HPD can publish a route to it.", "endpoint.target.process");
        }

        if (port is null)
        {
            return RouteFailure("AppleVirtualization.EndpointTargetPortMissing", "Process-port endpoint targets require a target port.", "endpoint.target.port");
        }

        return new RouteResolution(lookup.Entry.Resource.Id.Value, null, port.Value.Value, null, null);
    }

    private RouteResolution ResolveServiceRoute(
        ResourceRef<Network>? network,
        ServiceName? serviceName,
        NetworkPort? port,
        NetworkTransport transport)
    {
        if (serviceName is null)
        {
            return RouteFailure("AppleVirtualization.EndpointServiceNameMissing", "Service-name endpoint targets require a service name.", "endpoint.target.serviceName");
        }

        AppleVirtualizationNetworkMembershipSnapshot[] memberships = _ledger.GetActiveNetworkMemberships(network);
        for (int i = 0; i < memberships.Length; i++)
        {
            IReadOnlyList<DiscoveryRecord> records = memberships[i].Status.RegisteredRecords;
            for (int j = 0; j < records.Count; j++)
            {
                DiscoveryRecord record = records[j];
                if (record.Kind == DiscoveryRecordKind.Service &&
                    record.Target.Transport == transport &&
                    string.Equals(record.Name.Value, serviceName.Value.Value, StringComparison.OrdinalIgnoreCase))
                {
                    NetworkPort? resolvedPort = port ?? record.Target.Port;
                    return RouteForMembership(memberships[i], resolvedPort);
                }
            }
        }

        return RouteFailure("AppleVirtualization.EndpointServiceNotResolved", "The requested service name did not resolve to an active network membership.", "endpoint.target.serviceName");
    }

    private RouteResolution ResolveUnixSocketRoute(UnixSocketPath? socketPath) =>
        socketPath is null
            ? RouteFailure("AppleVirtualization.EndpointSocketPathMissing", "Unix-socket endpoint targets require a socket path.", "endpoint.target.socketPath")
            : RouteFailure("AppleVirtualization.EndpointUnixSocketUnsupported", "Unix-socket publication remains deferred until endpoint policy and authority binding define the sensitive boundary.", "endpoint.target.socketPath");

    private static RouteResolution RouteForMembership(
        AppleVirtualizationLedgerEntry<NetworkMembership, NetworkMembershipStatus> membership,
        NetworkPort? port)
    {
        if (membership.Status.Phase != ResourcePhase.Ready ||
            membership.Status.MembershipPhase != NetworkMembershipPhase.Ready)
        {
            return RouteFailure("AppleVirtualization.EndpointMembershipNotReady", "The target network membership must be ready before HPD can publish an endpoint route.", "endpoint.target.membership");
        }

        if (port is null)
        {
            return RouteFailure("AppleVirtualization.EndpointTargetPortMissing", "Network membership endpoint targets require a target port.", "endpoint.target.port");
        }

        IpAddressValue? address = PrimaryAddress(membership.Status.Addresses);
        return address is null
            ? RouteFailure("AppleVirtualization.EndpointAddressMissing", "The target network membership has no primary guest address to route to.", "endpoint.target.membership.address")
            : new RouteResolution(membership.Resource.Id.Value, ToAddressString(address.Value), port.Value.Value, null, null);
    }

    private static RouteResolution RouteForMembership(
        AppleVirtualizationNetworkMembershipSnapshot membership,
        NetworkPort? port)
    {
        if (membership.Status.Phase != ResourcePhase.Ready ||
            membership.Status.MembershipPhase != NetworkMembershipPhase.Ready)
        {
            return RouteFailure("AppleVirtualization.EndpointMembershipNotReady", "The target network membership must be ready before HPD can publish an endpoint route.", "endpoint.target.membership");
        }

        if (port is null)
        {
            return RouteFailure("AppleVirtualization.EndpointTargetPortMissing", "Service endpoint targets require a resolved target port.", "endpoint.target.port");
        }

        IpAddressValue? address = PrimaryAddress(membership.Status.Addresses);
        return address is null
            ? RouteFailure("AppleVirtualization.EndpointAddressMissing", "The target network membership has no primary guest address to route to.", "endpoint.target.membership.address")
            : new RouteResolution(membership.Resource.Id.Value, ToAddressString(address.Value), port.Value.Value, null, null);
    }

    private static PublishedEndpointStatus StatusFromHelper(
        ResourceMetadata<PublishedEndpoint> metadata,
        PublishedEndpointSpec spec,
        RouteResolution route,
        IReadOnlyList<NetworkLimitation> validationLimitations,
        IReadOnlyList<Diagnostic> validationDiagnostics,
        AppleVirtualizationEndpointPublicationResponse endpoint)
    {
        IReadOnlyList<NetworkLimitation> limitations = Combine(validationLimitations, endpoint.Limitations);
        PublishedEndpointPhase phase = endpoint.EndpointPhase;
        if (!endpoint.HpdOwned || !endpoint.RouteHealthy)
        {
            phase = PublishedEndpointPhase.Degraded;
            limitations = Append(limitation: Limitation(
                NetworkDegradedFeature.TcpPublish,
                CapabilityDegradationMode.TemporarilyUnavailable,
                "AppleVirtualization.EndpointRouteUnhealthy",
                "The helper did not verify a healthy HPD-owned route for the published endpoint."), to: limitations);
        }

        BoundEndpoint? bound = endpoint.BoundAddress is not null && endpoint.BoundPort.HasValue
            ? new BoundEndpoint(
                endpoint.ListenerKind,
                endpoint.Transport,
                ParseAddress(endpoint.BoundAddress),
                new PortRange(new NetworkPort(endpoint.BoundPort.Value), 1),
                Socket: null)
            : null;

        EndpointRouteStatus routeStatus = new(
            spec.Target,
            new NetworkEndpointHandle(metadata.Id.Value),
            ParseAddress(endpoint.ResolvedAddress ?? route.TargetAddress),
            endpoint.ResolvedPort.HasValue ? new NetworkPort(endpoint.ResolvedPort.Value) : route.TargetPort.HasValue ? new NetworkPort(route.TargetPort.Value) : null,
            endpoint.ResolvedSocketPath is not null ? new UnixSocketPath(endpoint.ResolvedSocketPath) : route.TargetSocketPath is not null ? new UnixSocketPath(route.TargetSocketPath) : null);

        return new PublishedEndpointStatus
        {
            Phase = phase == PublishedEndpointPhase.Bound ? ResourcePhase.Ready :
                phase == PublishedEndpointPhase.Failed ? ResourcePhase.Failed : ResourcePhase.Degraded,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            EndpointPhase = phase,
            BoundListener = bound,
            Route = routeStatus,
            PublicationOrigin = EndpointPublicationOrigin.Explicit,
            Limitations = limitations,
            Conditions = EndpointConditions(metadata.Generation, phase, endpoint.HpdOwned, endpoint.RouteHealthy, limitations),
            Diagnostics = Combine(validationDiagnostics, endpoint.Diagnostics),
        };
    }

    private static PublishedEndpointStatus FailedStatus(
        ResourceMetadata<PublishedEndpoint> metadata,
        PublishedEndpointSpec spec,
        IReadOnlyList<NetworkLimitation> limitations,
        Diagnostic diagnostic) =>
        FailedStatus(metadata, spec, limitations, Array.Empty<Diagnostic>(), diagnostic);

    private static PublishedEndpointStatus FailedStatus(
        ResourceMetadata<PublishedEndpoint> metadata,
        PublishedEndpointSpec spec,
        IReadOnlyList<NetworkLimitation> limitations,
        IReadOnlyList<Diagnostic> diagnostics,
        Diagnostic diagnostic) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            EndpointPhase = PublishedEndpointPhase.Failed,
            PublicationOrigin = EndpointPublicationOrigin.Explicit,
            Limitations = limitations,
            Route = new EndpointRouteStatus(spec.Target, null, null, null, null),
            Conditions = EndpointConditions(metadata.Generation, PublishedEndpointPhase.Failed, hpdOwned: false, routeHealthy: false, limitations),
            Diagnostics = Append(diagnostic, diagnostics),
        };

    private static IReadOnlyList<Condition> EndpointConditions(
        ResourceGeneration generation,
        PublishedEndpointPhase phase,
        bool hpdOwned,
        bool routeHealthy,
        IReadOnlyList<NetworkLimitation> limitations) =>
    [
        new Condition(
            "AppleVirtualization.PublishedEndpointReady",
            phase == PublishedEndpointPhase.Bound && hpdOwned && routeHealthy ? ConditionStatus.True : ConditionStatus.False,
            phase.ToString(),
            phase == PublishedEndpointPhase.Bound && hpdOwned && routeHealthy
                ? "The endpoint has an HPD-owned host-local listener and a verified route."
                : "The endpoint is not bound to a healthy HPD-owned route.",
            DateTimeOffset.UtcNow,
            generation,
            phase == PublishedEndpointPhase.Failed ? DiagnosticSeverity.Error : limitations.Count > 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info),
    ];

    private static NetworkLimitation Limitation(NetworkDegradedFeature feature, CapabilityDegradationMode mode, string reason, string message) =>
        new(feature, mode, reason, message);

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

        var combined = new NetworkLimitation[left.Count + right.Count];
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

    private static IReadOnlyList<NetworkLimitation> Append(NetworkLimitation limitation, IReadOnlyList<NetworkLimitation> to)
    {
        var result = new NetworkLimitation[to.Count + 1];
        for (int i = 0; i < to.Count; i++)
        {
            result[i] = to[i];
        }

        result[^1] = limitation;
        return result;
    }

    private static IReadOnlyList<Diagnostic> Combine(
        IReadOnlyList<Diagnostic> left,
        IReadOnlyList<Diagnostic> right)
    {
        if (left.Count == 0)
        {
            return right;
        }

        if (right.Count == 0)
        {
            return left;
        }

        var combined = new Diagnostic[left.Count + right.Count];
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

    private static IReadOnlyList<Diagnostic> Append(Diagnostic diagnostic, IReadOnlyList<Diagnostic> to)
    {
        if (to.Count == 0)
        {
            return [diagnostic];
        }

        var result = new Diagnostic[to.Count + 1];
        for (int i = 0; i < to.Count; i++)
        {
            result[i] = to[i];
        }

        result[^1] = diagnostic;
        return result;
    }

    private static void AddSensitiveEndpointDiagnostics(
        SensitiveEndpointPolicy sensitive,
        List<NetworkLimitation> limitations,
        List<Diagnostic> diagnostics)
    {
        NetworkDegradedFeature feature = sensitive.Kind == SensitiveEndpointKind.CredentialProxy
            ? NetworkDegradedFeature.CredentialProjection
            : NetworkDegradedFeature.SocketProjection;

        limitations.Add(Limitation(
            feature,
            CapabilityDegradationMode.RequiresPermission,
            SensitiveEndpointCode(sensitive.Kind),
            SensitiveEndpointMessage(sensitive.Kind)));

        if (sensitive.RequireAudit)
        {
            limitations.Add(Limitation(
                NetworkDegradedFeature.BindingAudit,
                CapabilityDegradationMode.RequiresPermission,
                "AppleVirtualization.EndpointSensitiveAuditRequiresAuthorityBinding",
                "Sensitive endpoint audit requires AuthorityBinding lease and audit support."));
        }

        diagnostics.Add(EndpointDiagnostic(
            DiagnosticSeverity.Warning,
            "AppleVirtualization.EndpointSensitiveDeferredToAuthorityBinding",
            "Sensitive endpoint publication is handled through AuthorityBinding lease, audit, approval, and revocation semantics, not ordinary PublishedEndpoint resources.",
            "endpoint.sensitive"));
        diagnostics.Add(EndpointDiagnostic(
            DiagnosticSeverity.Warning,
            "AppleVirtualization.EndpointSensitiveAuthorityClass",
            "Requested sensitive authority class: " + sensitive.AuthorityClass,
            "endpoint.sensitive.authorityClass"));
    }

    private static string SensitiveEndpointCode(SensitiveEndpointKind kind) =>
        kind switch
        {
            SensitiveEndpointKind.EngineSocket => "AppleVirtualization.EndpointEngineSocketRequiresAuthorityBinding",
            SensitiveEndpointKind.CredentialProxy => "AppleVirtualization.EndpointCredentialProxyRequiresAuthorityBinding",
            SensitiveEndpointKind.TrustService => "AppleVirtualization.EndpointTrustServiceRequiresAuthorityBinding",
            SensitiveEndpointKind.SshAgent => "AppleVirtualization.EndpointSshAgentRequiresAuthorityBinding",
            SensitiveEndpointKind.HostDaemonControl => "AppleVirtualization.EndpointHostDaemonRequiresAuthorityBinding",
            SensitiveEndpointKind.FunctionDebug => "AppleVirtualization.EndpointFunctionDebugRequiresAuthorityBinding",
            _ => "AppleVirtualization.EndpointSensitiveRequiresAuthorityBinding",
        };

    private static string SensitiveEndpointMessage(SensitiveEndpointKind kind) =>
        kind switch
        {
            SensitiveEndpointKind.EngineSocket => "Engine socket endpoints, including Docker, Podman, and containerd APIs, require AuthorityBinding and are not published as ordinary endpoints.",
            SensitiveEndpointKind.CredentialProxy => "Credential proxy endpoints require AuthorityBinding and are not published as ordinary endpoints.",
            SensitiveEndpointKind.TrustService => "Trust service endpoints require AuthorityBinding and are not published as ordinary endpoints.",
            SensitiveEndpointKind.SshAgent => "SSH agent endpoints require AuthorityBinding and are not published as ordinary endpoints.",
            SensitiveEndpointKind.HostDaemonControl => "Host daemon control endpoints require AuthorityBinding and are not published as ordinary endpoints.",
            SensitiveEndpointKind.FunctionDebug => "Function debug endpoints require AuthorityBinding and are not published as ordinary endpoints.",
            _ => "Sensitive endpoint publication requires AuthorityBinding and is not published as an ordinary endpoint.",
        };

    private static Diagnostic ToDiagnostic(AppleVirtualizationHelperError? error, string operation) =>
        error is null
            ? EndpointDiagnostic(DiagnosticSeverity.Error, "AppleVirtualization.EndpointHelperError", "The Apple Virtualization helper failed the endpoint operation.", operation)
            : EndpointDiagnostic(error.Severity, error.Code, error.Message, error.Operation ?? operation);

    private static Diagnostic EndpointDiagnostic(DiagnosticSeverity severity, string code, string message, string targetPath) =>
        new()
        {
            Severity = severity,
            Code = new DiagnosticCode(code),
            Message = message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    private static RouteResolution RouteFailure(string code, string message, string targetPath) =>
        new(null, null, null, null, EndpointDiagnostic(DiagnosticSeverity.Error, code, message, targetPath));

    private static IpAddressValue? PrimaryAddress(IReadOnlyList<NetworkAddressAssignment> addresses)
    {
        for (int i = 0; i < addresses.Count; i++)
        {
            if (addresses[i].IsPrimary)
            {
                return addresses[i].Address;
            }
        }

        return addresses.Count == 0 ? null : addresses[0].Address;
    }

    private static string? ToAddressString(IpAddressValue address)
    {
        if (address.Family == NetworkAddressFamily.IPv4)
        {
            ulong value = address.LowBits;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(value >> 24) & 0xff}.{(value >> 16) & 0xff}.{(value >> 8) & 0xff}.{value & 0xff}");
        }

        return null;
    }

    private static IpAddressValue? ParseAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        ReadOnlySpan<char> span = address.AsSpan();
        ulong value = 0;
        int octet = 0;
        int start = 0;
        for (int i = 0; i <= span.Length; i++)
        {
            if (i != span.Length && span[i] != '.')
            {
                continue;
            }

            if (octet == 4 || !byte.TryParse(span[start..i], NumberStyles.None, CultureInfo.InvariantCulture, out byte parsed))
            {
                return null;
            }

            value = (value << 8) | parsed;
            octet++;
            start = i + 1;
        }

        return octet == 4 ? new IpAddressValue(NetworkAddressFamily.IPv4, 0, value) : null;
    }

    private static string? ListenerAddress(IpAddressValue? address) =>
        address.HasValue ? ToAddressString(address.Value) : "127.0.0.1";

    private static ushort? FirstPort(PortRange? range) =>
        range?.Start.Value;

    private static bool IsLoopback(IpAddressValue address) =>
        address.Family switch
        {
            NetworkAddressFamily.IPv4 => ((address.LowBits >> 24) & 0xff) == 127,
            NetworkAddressFamily.IPv6 => address.HighBits == 0 && address.LowBits == 1,
            _ => false,
        };

    private readonly record struct EndpointValidation(
        IReadOnlyList<NetworkLimitation> Limitations,
        IReadOnlyList<Diagnostic> Diagnostics,
        Diagnostic? FatalDiagnostic);

    private readonly record struct RouteResolution(
        string? TargetResourceId,
        string? TargetAddress,
        ushort? TargetPort,
        string? TargetSocketPath,
        Diagnostic? Diagnostic);
}
