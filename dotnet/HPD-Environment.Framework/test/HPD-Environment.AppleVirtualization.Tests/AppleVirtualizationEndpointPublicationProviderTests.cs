namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.Networks;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationEndpointPublicationProviderTests
{
    [Fact]
    public async Task Publish_endpoint_requires_ready_membership_target()
    {
        var provider = new AppleVirtualizationEndpointPublicationProvider(
            new AppleVirtualizationProviderStateLedger(),
            new FakeAppleVirtualizationHelperClient());

        PublishedEndpointStatus status = await provider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            EndpointSpec(MembershipTarget()),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.EndpointPhase.Should().Be(PublishedEndpointPhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == HPD.Environment.AppleVirtualization.Handles.AppleVirtualizationHandleDiagnostics.MissingHandle);
    }

    [Fact]
    public async Task Host_local_tcp_listener_publishes_with_bound_listener_and_route_status()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(EndpointResponse("endpoint-1"));
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedReadyMembership(ledger);
        var provider = new AppleVirtualizationEndpointPublicationProvider(ledger, helper);

        PublishedEndpointStatus status = await provider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            EndpointSpec(MembershipTarget()),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.EndpointPhase.Should().Be(PublishedEndpointPhase.Bound);
        status.BoundListener.Should().NotBeNull();
        status.BoundListener!.Value.Kind.Should().Be(EndpointListenerKind.HostAddress);
        status.BoundListener.Value.Transport.Should().Be(NetworkTransport.Tcp);
        status.BoundListener.Value.Ports!.Value.Start.Value.Should().Be(8080);
        status.Route.Should().NotBeNull();
        status.Route!.Value.ResolvedAddress!.Value.LowBits.Should().Be(0x0a000002);
        status.Route.Value.ResolvedPort!.Value.Value.Should().Be(8080);
        status.RouterHandle.Should().NotBeNull();
        helper.Requests.Should().ContainSingle(request =>
            request.Operation == AppleVirtualizationHelperOperation.EndpointPublish &&
            request.EndpointPublicationRequest!.EndpointId == "endpoint-1" &&
            request.EndpointPublicationRequest.TargetAddress == "10.0.0.2" &&
            request.EndpointPublicationRequest.TargetPort == 8080);
    }

    [Fact]
    public async Task Default_published_endpoint_policy_is_host_local_only()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(EndpointResponse("endpoint-1"));
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedReadyMembership(ledger);
        var provider = new AppleVirtualizationEndpointPublicationProvider(ledger, helper);

        PublishedEndpointStatus status = await provider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            EndpointSpec(MembershipTarget(), includeExposurePolicy: false),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.EndpointPhase.Should().Be(PublishedEndpointPhase.Bound);
        status.BoundListener!.Value.Address!.Value.LowBits.Should().Be(0x7f000001);
        helper.Requests.Should().ContainSingle(request =>
            request.Operation == AppleVirtualizationHelperOperation.EndpointPublish &&
            request.EndpointPublicationRequest!.ExposureScope == EndpointExposureScope.HostLocal &&
            request.EndpointPublicationRequest.ListenerAddress == "127.0.0.1");
    }

    [Fact]
    public async Task Network_address_target_routes_to_an_in_guest_container()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(EndpointResponse("endpoint-1"));
        var provider = new AppleVirtualizationEndpointPublicationProvider(
            new AppleVirtualizationProviderStateLedger(),
            helper);
        EndpointRouteTarget target = new(
            EndpointTargetKind.NetworkAddress,
            Membership: null,
            Unit: null,
            Process: null,
            ServiceName: null,
            NetworkTransport.Tcp,
            new NetworkPort(8080),
            SocketPath: null,
            new IpAddressValue(
                NetworkAddressFamily.IPv4,
                0,
                0xac120002));

        PublishedEndpointStatus status =
            await provider.EnsurePublishedEndpointAsync(
                Metadata<PublishedEndpoint>(
                    "endpoint-1",
                    "published-endpoint"),
                EndpointSpec(target),
                observed: null);

        status.EndpointPhase.Should().Be(
            PublishedEndpointPhase.Bound);
        helper.Requests.Should().ContainSingle(request =>
            request.EndpointPublicationRequest!.TargetKind ==
                EndpointTargetKind.NetworkAddress &&
            request.EndpointPublicationRequest.TargetAddress ==
                "172.18.0.2" &&
            request.EndpointPublicationRequest.TargetPort == 8080);
    }

    [Fact]
    public async Task Host_local_endpoint_rejects_non_loopback_listener_address()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedReadyMembership(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationEndpointPublicationProvider(ledger, helper);

        PublishedEndpointStatus status = await provider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            EndpointSpec(
                MembershipTarget(),
                listenerAddress: new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0)),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.EndpointPhase.Should().Be(PublishedEndpointPhase.Failed);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EndpointHostLocalRequiresLoopback");
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Authorization_requirements_are_reported_without_issuing_tokens()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(EndpointResponse("endpoint-1"));
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedReadyMembership(ledger);
        var provider = new AppleVirtualizationEndpointPublicationProvider(ledger, helper);

        PublishedEndpointStatus status = await provider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            EndpointSpec(
                MembershipTarget(),
                authorizationPolicy: new EndpointAuthorizationPolicy
                {
                    RequireLoopbackClient = true,
                    TokenAudience = "hpd://endpoint/unit-1/app",
                }),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EndpointLoopbackClientRequired");
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EndpointTokenAudienceDiagnosticOnly");
        helper.Requests.Should().ContainSingle(request =>
            request.Operation == AppleVirtualizationHelperOperation.EndpointPublish);
    }

    [Theory]
    [InlineData(NetworkTransport.Udp, EndpointExposureScope.HostLocal, "AppleVirtualization.EndpointTransportUnsupported")]
    [InlineData(NetworkTransport.Tcp, EndpointExposureScope.HostLan, "AppleVirtualization.EndpointExposureUnsupported")]
    [InlineData(NetworkTransport.Tcp, EndpointExposureScope.External, "AppleVirtualization.EndpointExposureUnsupported")]
    public async Task Unsupported_transport_or_exposure_fails_honestly(
        NetworkTransport transport,
        EndpointExposureScope scope,
        string expectedCode)
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedReadyMembership(ledger);
        var provider = new AppleVirtualizationEndpointPublicationProvider(ledger, new FakeAppleVirtualizationHelperClient());

        PublishedEndpointStatus status = await provider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            EndpointSpec(MembershipTarget(), transport, scope),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code.Value == expectedCode);
    }

    [Theory]
    [InlineData(SensitiveEndpointKind.EngineSocket, SensitiveAuthorityClass.RootlessEngineControl, "AppleVirtualization.EndpointEngineSocketRequiresAuthorityBinding")]
    [InlineData(SensitiveEndpointKind.CredentialProxy, SensitiveAuthorityClass.CredentialDelegation, "AppleVirtualization.EndpointCredentialProxyRequiresAuthorityBinding")]
    [InlineData(SensitiveEndpointKind.SshAgent, SensitiveAuthorityClass.CredentialDelegation, "AppleVirtualization.EndpointSshAgentRequiresAuthorityBinding")]
    [InlineData(SensitiveEndpointKind.HostDaemonControl, SensitiveAuthorityClass.PrivilegedDaemonControl, "AppleVirtualization.EndpointHostDaemonRequiresAuthorityBinding")]
    [InlineData(SensitiveEndpointKind.FunctionDebug, SensitiveAuthorityClass.DebugControl, "AppleVirtualization.EndpointFunctionDebugRequiresAuthorityBinding")]
    [InlineData(SensitiveEndpointKind.TrustService, SensitiveAuthorityClass.TrustMutation, "AppleVirtualization.EndpointTrustServiceRequiresAuthorityBinding")]
    public async Task Sensitive_endpoint_requests_are_deferred_to_authority_binding_without_publication(
        SensitiveEndpointKind kind,
        SensitiveAuthorityClass authorityClass,
        string expectedCode)
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedReadyMembership(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationEndpointPublicationProvider(ledger, helper);

        PublishedEndpointStatus status = await provider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            EndpointSpec(
                MembershipTarget(),
                sensitivePolicy: new SensitiveEndpointPolicy
                {
                    Kind = kind,
                    AuthorityClass = authorityClass,
                    RequireAudit = true,
                    RequireExplicitUserApproval = true,
                }),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.EndpointPhase.Should().Be(PublishedEndpointPhase.Failed);
        status.BoundListener.Should().BeNull();
        status.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == expectedCode);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EndpointSensitiveDeferredToAuthorityBinding");
        status.Limitations.Should().Contain(limitation => limitation.ReasonCode == expectedCode);
        status.Limitations.Should().Contain(limitation =>
            limitation.ReasonCode == "AppleVirtualization.EndpointSensitiveAuditRequiresAuthorityBinding");
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_endpoint_rejects_target_process_that_is_not_running()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> process = ledger.UpsertProcessInvocation(
            Metadata<ProcessInvocation>("process-1", "process-invocation"),
            new ProcessInvocationStatus
            {
                Phase = ResourcePhase.Reconciling,
                ProcessPhase = ProcessInvocationPhase.Created,
            });
        var provider = new AppleVirtualizationEndpointPublicationProvider(ledger, new FakeAppleVirtualizationHelperClient());

        PublishedEndpointStatus status = await provider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            EndpointSpec(ProcessTarget(process.Resource)),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EndpointProcessNotRunning");
    }

    [Fact]
    public async Task Route_health_failure_degrades_published_endpoint_without_claiming_ready()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(EndpointResponse("endpoint-1", routeHealthy: false));
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedReadyMembership(ledger);
        var provider = new AppleVirtualizationEndpointPublicationProvider(ledger, helper);

        PublishedEndpointStatus status = await provider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            EndpointSpec(MembershipTarget()),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.EndpointPhase.Should().Be(PublishedEndpointPhase.Degraded);
        status.Limitations.Should().Contain(limitation =>
            limitation.ReasonCode == "AppleVirtualization.EndpointRouteUnhealthy");
    }

    [Fact]
    public async Task Release_endpoint_is_idempotent_and_removes_listener_state()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(EndpointResponse("endpoint-1"));
        helper.EnqueueResponse(EndpointResponse("endpoint-1", PublishedEndpointPhase.Released, routeHealthy: false));
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedReadyMembership(ledger);
        var provider = new AppleVirtualizationEndpointPublicationProvider(ledger, helper);
        var endpoint = new ResourceRef<PublishedEndpoint>(
            new ResourceId<PublishedEndpoint>("endpoint-1"),
            AppleVirtualizationContractFixtures.RuntimeScope,
            new ResourceGeneration(1));

        await provider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            EndpointSpec(MembershipTarget()),
            observed: null);
        await provider.ReleasePublishedEndpointAsync(endpoint);
        await provider.ReleasePublishedEndpointAsync(endpoint);

        (await provider.GetStatusAsync(endpoint)).Diagnostics
            .Should().ContainSingle(diagnostic => diagnostic.Code == HPD.Environment.AppleVirtualization.Handles.AppleVirtualizationHandleDiagnostics.MissingHandle);
        helper.Requests.Should().Contain(request =>
            request.Operation == AppleVirtualizationHelperOperation.EndpointRelease &&
            request.EndpointPublicationRequest!.Action == AppleVirtualizationEndpointPublicationAction.Release);
    }

    private static ResourceMetadata<TResource> Metadata<TResource>(string id, string kind)
        where TResource : IExecutionResourceMarker =>
        AppleVirtualizationContractFixtures.Metadata<TResource>(id, kind);

    private static PublishedEndpointSpec EndpointSpec(
        EndpointRouteTarget target,
        NetworkTransport transport = NetworkTransport.Tcp,
        EndpointExposureScope exposureScope = EndpointExposureScope.HostLocal,
        IpAddressValue? listenerAddress = null,
        EndpointAuthorizationPolicy? authorizationPolicy = null,
        SensitiveEndpointPolicy? sensitivePolicy = null,
        bool includeExposurePolicy = true)
    {
        var spec = new PublishedEndpointSpec
        {
            Listener = new EndpointListenerSpec(
                EndpointListenerKind.HostAddress,
                transport,
                Address: listenerAddress ?? new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x7f000001),
                Ports: new PortRange(new NetworkPort(8080), 1),
                Socket: null),
            Target = target,
            AuthorizationPolicy = authorizationPolicy ?? EndpointAuthorizationPolicy.None,
            SensitivePolicy = sensitivePolicy,
            RoutingNetwork = NetworkRef(),
            ReconcileRouteOnTargetRestart = true,
        };

        return includeExposurePolicy
            ? spec with
            {
                ExposurePolicy = new EndpointExposurePolicy
                {
                    Scope = exposureScope,
                    RequireStableListener = true,
                },
            }
            : spec;
    }

    private static EndpointRouteTarget MembershipTarget() =>
        new(
            EndpointTargetKind.NetworkMembership,
            Membership: MembershipRef(),
            Unit: null,
            Process: null,
            ServiceName: null,
            Transport: NetworkTransport.Tcp,
            Port: new NetworkPort(8080),
            SocketPath: null);

    private static EndpointRouteTarget ProcessTarget(ResourceRef<ProcessInvocation> process) =>
        new(
            EndpointTargetKind.ProcessPort,
            Membership: null,
            Unit: null,
            Process: process,
            ServiceName: null,
            Transport: NetworkTransport.Tcp,
            Port: new NetworkPort(8080),
            SocketPath: null);

    private static ResourceRef<Network> NetworkRef() =>
        new(new ResourceId<Network>("network-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));

    private static ResourceRef<NetworkMembership> MembershipRef() =>
        new(new ResourceId<NetworkMembership>("membership-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));

    private static void SeedReadyMembership(AppleVirtualizationProviderStateLedger ledger)
    {
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

        ledger.UpsertNetworkMembership(
            Metadata<NetworkMembership>("membership-1", "network-membership"),
            new NetworkMembershipStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                MembershipPhase = NetworkMembershipPhase.Ready,
                EndpointHandle = new NetworkEndpointHandle("membership-1"),
                Addresses =
                [
                    new NetworkAddressAssignment(
                        new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000002),
                        24,
                        AddressAssignmentKind.ProviderAssigned,
                        IsPrimary: true),
                ],
                RegisteredRecords =
                [
                    new DiscoveryRecord(
                        new DnsName("app"),
                        DiscoveryRecordKind.Service,
                        new DiscoveryRecordTarget(null, new ServiceName("app"), MembershipRef(), new NetworkPort(8080), NetworkTransport.Tcp, null),
                        TimeSpan.FromSeconds(30),
                        IsDerivedFromMembership: true),
                ],
            },
            new NetworkMembershipSpec
            {
                Network = NetworkRef(),
                Target = new NetworkMembershipTarget(NetworkMembershipTargetKind.ExecutionUnit, Host: null, Unit: AppleVirtualizationContractFixtures.ExecutionUnitHandle(), Process: null),
                Hostname = new ScopedName("unit-1"),
                ServiceNames = [new ServiceName("app")],
            });
    }

    private static AppleVirtualizationHelperEnvelope EndpointResponse(
        string endpointId,
        PublishedEndpointPhase phase = PublishedEndpointPhase.Bound,
        bool routeHealthy = true) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EndpointPublish,
            "endpoint-response",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.EndpointPublicationResponseSchema).ToResponse(sequenceNumber: 2) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.EndpointPublicationResponseSchema,
            EndpointPublicationResponse = new AppleVirtualizationEndpointPublicationResponse
            {
                EndpointId = endpointId,
                EndpointPhase = phase,
                ListenerKind = EndpointListenerKind.HostAddress,
                Transport = NetworkTransport.Tcp,
                ExposureScope = EndpointExposureScope.HostLocal,
                BoundAddress = phase == PublishedEndpointPhase.Released ? null : "127.0.0.1",
                BoundPort = phase == PublishedEndpointPhase.Released ? null : (ushort)8080,
                HpdOwned = phase != PublishedEndpointPhase.Released,
                RouteHealthy = routeHealthy,
                ResolvedAddress = "10.0.0.2",
                ResolvedPort = 8080,
            },
        };
}
