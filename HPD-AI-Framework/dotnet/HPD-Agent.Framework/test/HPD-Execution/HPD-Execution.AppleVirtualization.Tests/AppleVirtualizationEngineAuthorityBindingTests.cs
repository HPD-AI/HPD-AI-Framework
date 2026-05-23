namespace HPD.Execution.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Execution.AppleVirtualization.Authority;
using HPD.Execution.AppleVirtualization.Engines;
using HPD.Execution.AppleVirtualization.Networks;
using HPD.Execution.AppleVirtualization.Protocol;
using HPD.Execution.AppleVirtualization.State;
using HPD.Execution.AppleVirtualization.Tests.Fixtures;
using HPD.Execution.Contracts;
using Xunit;

public sealed class AppleVirtualizationEngineAuthorityBindingTests
{
    private static readonly DateTimeOffset ClockStart = new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Engine_api_endpoint_cannot_be_published_through_ordinary_endpoint_publication()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedReadyMembership(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationEndpointPublicationProvider(ledger, helper);

        PublishedEndpointStatus status = await provider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-engine", "published-endpoint"),
            EngineSensitivePublishedEndpointSpec(),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.EndpointPhase.Should().Be(PublishedEndpointPhase.Failed);
        status.BoundListener.Should().BeNull();
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EndpointEngineSocketRequiresAuthorityBinding");
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EndpointSensitiveDeferredToAuthorityBinding" &&
            diagnostic.Message.Contains("not ordinary PublishedEndpoint resources", StringComparison.Ordinal));
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Engine_api_endpoint_requires_authority_binding_lease()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> host = SeedReadyHost(ledger);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        EngineControlPlaneStatus engine = await EnsureReadyEngineAsync(ledger, helper, host.Resource);

        bool created = AppleVirtualizationEngineEndpointAuthority.TryCreateBindingSpec(
            engine,
            EngineApiKind.DockerCompatible,
            unit.TargetHandle,
            new UnixSocketPath("/run/hpd/engine/docker.sock"),
            new SensitiveProvenance(Actor: "agent-82", Reason: "container-smoke"),
            out AuthorityBindingSpec? bindingSpec,
            out Diagnostic? diagnostic);

        created.Should().BeTrue();
        diagnostic.Should().BeNull();
        bindingSpec.Should().NotBeNull();
        bindingSpec!.Source.Kind.Should().Be(AuthoritySourceKind.UnixSocket);
        bindingSpec.Source.Locus.Should().Be(BoundaryLocus.RuntimeHost);
        bindingSpec.Policy.Lease.Lifetime.Should().Be(BindingLifetime.ExecutionUnit);
        bindingSpec.Policy.RequireAudit.Should().BeTrue();
        bindingSpec.Policy.AuthorityClass.Should().Be(SensitiveAuthorityClass.RootlessEngineControl);

        var authority = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        AuthorityBindingStatus status = await authority.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("engine-authority-1", "authority-binding"),
            bindingSpec,
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        status.BoundAuthority!.EffectiveAuthorityClass.Should().Be(SensitiveAuthorityClass.RootlessEngineControl);
        status.BoundAuthority.TargetSocketPath!.Value.Value.Should().Be("/run/hpd/engine/docker.sock");
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings
            .Should().ContainSingle(binding => binding.Id.Value == "engine-authority-1");
    }

    [Fact]
    public async Task Containerd_engine_api_endpoint_projects_containerd_socket_with_rootful_authority()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> host = SeedReadyHost(ledger);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        EngineControlPlaneStatus engine = await EnsureReadyEngineAsync(
            ledger,
            helper,
            host.Resource,
            EngineControlPlaneKind.Containerd,
            EngineApiKind.ContainerdApi,
            EngineAuthorityMode.Rootful);

        bool created = AppleVirtualizationEngineEndpointAuthority.TryCreateBindingSpec(
            engine,
            EngineApiKind.ContainerdApi,
            unit.TargetHandle,
            new UnixSocketPath("/run/hpd/engine/containerd.sock"),
            new SensitiveProvenance(Actor: "agent-82", Reason: "containerd-smoke"),
            out AuthorityBindingSpec? bindingSpec,
            out Diagnostic? diagnostic);

        created.Should().BeTrue(diagnostic?.Message);
        bindingSpec.Should().NotBeNull();
        bindingSpec!.Source.SocketPath!.Value.Value.Should().Be("/run/containerd/containerd.sock");
        bindingSpec.Projection.TargetSocketPath!.Value.Value.Should().Be("/run/hpd/engine/containerd.sock");
        bindingSpec.Policy.AuthorityClass.Should().Be(SensitiveAuthorityClass.RootfulEngineControl);
        bindingSpec.Policy.EffectiveAuthorityClass.Should().Be(SensitiveAuthorityClass.RootfulEngineControl);
        bindingSpec.AuditLabel.Should().Be("engine-api:ContainerdApi");

        var authority = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        AuthorityBindingStatus status = await authority.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("engine-authority-1", "authority-binding"),
            bindingSpec,
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        status.BoundAuthority!.EffectiveAuthorityClass.Should().Be(SensitiveAuthorityClass.RootfulEngineControl);
        status.BoundAuthority.TargetSocketPath!.Value.Value.Should().Be("/run/hpd/engine/containerd.sock");

        AppleVirtualizationHelperEnvelope request = helper.Requests.Single(request =>
            request.Operation == AppleVirtualizationHelperOperation.AuthorityBind);
        request.AuthorityBindingRequest!.Source.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.EngineSocket);
        request.AuthorityBindingRequest.Source.RedactedDisplayName.Should().Be("engine-socket:***");
        request.AuthorityBindingRequest.Source.SocketPath.Should().BeNull();
        request.AuthorityBindingRequest.Projection.TargetSocketPath!.Value.Value.Should().Be("/run/hpd/engine/containerd.sock");
        System.Text.Json.JsonSerializer.Serialize(
                request,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)
            .Should().NotContain("/run/containerd/containerd.sock");
    }

    [Theory]
    [InlineData(EngineAuthorityMode.Rootless, "/run/user/1000/podman/podman.sock", "/run/hpd/engine/podman.sock", SensitiveAuthorityClass.RootlessEngineControl)]
    [InlineData(EngineAuthorityMode.Rootful, "/run/podman/podman.sock", "/run/hpd/engine/podman-rootful.sock", SensitiveAuthorityClass.RootfulEngineControl)]
    public async Task Podman_engine_api_endpoint_projects_podman_socket_with_expected_authority(
        EngineAuthorityMode authorityMode,
        string sourceSocketPath,
        string projectedSocketPath,
        SensitiveAuthorityClass expectedAuthorityClass)
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> host = SeedReadyHost(ledger);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        EngineControlPlaneStatus engine = await EnsureReadyEngineAsync(
            ledger,
            helper,
            host.Resource,
            EngineControlPlaneKind.Podman,
            EngineApiKind.PodmanApi,
            authorityMode);

        bool created = AppleVirtualizationEngineEndpointAuthority.TryCreateBindingSpec(
            engine,
            EngineApiKind.PodmanApi,
            unit.TargetHandle,
            new UnixSocketPath(projectedSocketPath),
            new SensitiveProvenance(Actor: "agent-82", Reason: "podman-smoke"),
            out AuthorityBindingSpec? bindingSpec,
            out Diagnostic? diagnostic);

        created.Should().BeTrue(diagnostic?.Message);
        bindingSpec.Should().NotBeNull();
        bindingSpec!.Source.SocketPath!.Value.Value.Should().Be(sourceSocketPath);
        bindingSpec.Projection.TargetSocketPath!.Value.Value.Should().Be(projectedSocketPath);
        bindingSpec.Policy.AuthorityClass.Should().Be(expectedAuthorityClass);
        bindingSpec.Policy.EffectiveAuthorityClass.Should().Be(expectedAuthorityClass);

        var authority = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        AuthorityBindingStatus status = await authority.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("engine-authority-1", "authority-binding"),
            bindingSpec,
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        status.BoundAuthority!.EffectiveAuthorityClass.Should().Be(expectedAuthorityClass);
        status.BoundAuthority.TargetSocketPath!.Value.Value.Should().Be(projectedSocketPath);

        AppleVirtualizationHelperEnvelope request = helper.Requests.Single(request =>
            request.Operation == AppleVirtualizationHelperOperation.AuthorityBind);
        request.AuthorityBindingRequest!.Source.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.EngineSocket);
        request.AuthorityBindingRequest.Source.RedactedDisplayName.Should().Be("engine-socket:***");
        request.AuthorityBindingRequest.Source.SocketPath.Should().BeNull();
        request.AuthorityBindingRequest.Projection.TargetSocketPath!.Value.Value.Should().Be(projectedSocketPath);
        System.Text.Json.JsonSerializer.Serialize(
                request,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)
            .Should().NotContain(sourceSocketPath);
    }

    [Theory]
    [InlineData(EngineAuthorityMode.Rootless, "/run/user/1000/buildkit-default/buildkitd.sock", "/run/hpd/engine/buildkitd.sock", SensitiveAuthorityClass.RootlessEngineControl)]
    [InlineData(EngineAuthorityMode.Rootful, "/run/buildkit/buildkitd.sock", "/run/hpd/engine/buildkitd-rootful.sock", SensitiveAuthorityClass.RootfulEngineControl)]
    public async Task BuildKit_engine_api_endpoint_projects_buildkit_socket_with_expected_authority(
        EngineAuthorityMode authorityMode,
        string sourceSocketPath,
        string projectedSocketPath,
        SensitiveAuthorityClass expectedAuthorityClass)
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> host = SeedReadyHost(ledger);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        EngineControlPlaneStatus engine = await EnsureReadyEngineAsync(
            ledger,
            helper,
            host.Resource,
            EngineControlPlaneKind.BuildKit,
            EngineApiKind.BuildKitApi,
            authorityMode);

        bool created = AppleVirtualizationEngineEndpointAuthority.TryCreateBindingSpec(
            engine,
            EngineApiKind.BuildKitApi,
            unit.TargetHandle,
            new UnixSocketPath(projectedSocketPath),
            new SensitiveProvenance(Actor: "agent-82", Reason: "buildkit-smoke"),
            out AuthorityBindingSpec? bindingSpec,
            out Diagnostic? diagnostic);

        created.Should().BeTrue(diagnostic?.Message);
        bindingSpec.Should().NotBeNull();
        bindingSpec!.Source.SocketPath!.Value.Value.Should().Be(sourceSocketPath);
        bindingSpec.Projection.TargetSocketPath!.Value.Value.Should().Be(projectedSocketPath);
        bindingSpec.Policy.AuthorityClass.Should().Be(expectedAuthorityClass);
        bindingSpec.Policy.EffectiveAuthorityClass.Should().Be(expectedAuthorityClass);

        var authority = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        AuthorityBindingStatus status = await authority.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("engine-authority-1", "authority-binding"),
            bindingSpec,
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        status.BoundAuthority!.EffectiveAuthorityClass.Should().Be(expectedAuthorityClass);
        status.BoundAuthority.TargetSocketPath!.Value.Value.Should().Be(projectedSocketPath);

        AppleVirtualizationHelperEnvelope request = helper.Requests.Single(request =>
            request.Operation == AppleVirtualizationHelperOperation.AuthorityBind);
        request.AuthorityBindingRequest!.Source.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.EngineSocket);
        request.AuthorityBindingRequest.Source.RedactedDisplayName.Should().Be("engine-socket:***");
        request.AuthorityBindingRequest.Source.SocketPath.Should().BeNull();
        request.AuthorityBindingRequest.Projection.TargetSocketPath!.Value.Value.Should().Be(projectedSocketPath);
        System.Text.Json.JsonSerializer.Serialize(
                request,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)
            .Should().NotContain(sourceSocketPath);
    }

    [Fact]
    public async Task Containerd_engine_socket_remains_vm_internal_unless_authority_bound()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> host = SeedReadyHost(ledger);

        EngineControlPlaneStatus engine = await EnsureReadyEngineAsync(
            ledger,
            helper,
            host.Resource,
            EngineControlPlaneKind.Containerd,
            EngineApiKind.ContainerdApi,
            EngineAuthorityMode.Rootful);

        engine.Endpoints.Should().ContainSingle(endpoint =>
            endpoint.Api == EngineApiKind.ContainerdApi &&
            endpoint.Endpoint.Sensitivity == EndpointSensitivity.Sensitive &&
            endpoint.Endpoint.Endpoint.Path == "/run/containerd/containerd.sock" &&
            endpoint.SensitivePolicy!.Kind == SensitiveEndpointKind.EngineSocket &&
            endpoint.SensitivePolicy.AuthorityClass == SensitiveAuthorityClass.RootfulEngineControl);
        ledger.GetAuthorityBindings(AppleVirtualizationContractFixtures.RuntimeScope).Should().BeEmpty();
        helper.Requests.Should().NotContain(request =>
            request.Operation == AppleVirtualizationHelperOperation.EndpointPublish);
    }

    [Fact]
    public void Engine_api_endpoint_rejects_host_or_provider_locus_socket_metadata()
    {
        EngineControlPlaneStatus engine = ReadyEngineWithEndpointAddress("provider");
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit =
            SeedReadyUnit(new AppleVirtualizationProviderStateLedger());

        bool created = AppleVirtualizationEngineEndpointAuthority.TryCreateBindingSpec(
            engine,
            EngineApiKind.DockerCompatible,
            unit.TargetHandle,
            new UnixSocketPath("/run/hpd/engine/docker.sock"),
            new SensitiveProvenance(Actor: "agent-88", Reason: "host-locus-negative"),
            out AuthorityBindingSpec? bindingSpec,
            out Diagnostic? diagnostic);

        created.Should().BeFalse();
        bindingSpec.Should().BeNull();
        diagnostic!.Code.Value.Should().Be("AppleVirtualization.EngineAuthorityEndpointHostLocusRejected");
    }

    [Fact]
    public async Task Engine_authority_lease_grant_exposes_only_bounded_redacted_metadata()
    {
        const string guestEngineSocketPath = "/run/user/1000/docker.sock";
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var authority = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);

        AuthorityBindingStatus status = await authority.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("engine-authority-1", "authority-binding"),
            RuntimeHostEngineSocketSpec(unit.TargetHandle, guestEngineSocketPath),
            observed: null);

        status.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        AppleVirtualizationHelperEnvelope request = helper.Requests.Single(request =>
            request.Operation == AppleVirtualizationHelperOperation.AuthorityBind);
        request.AuthorityBindingRequest!.Source.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.EngineSocket);
        request.AuthorityBindingRequest.Source.RedactedDisplayName.Should().Be("engine-socket:***");
        request.AuthorityBindingRequest.Source.SocketPath.Should().BeNull();
        request.AuthorityBindingRequest.Source.Credential.Should().BeNull();

        string serialized = System.Text.Json.JsonSerializer.Serialize(
            request,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        serialized.Should().NotContain(guestEngineSocketPath);
    }

    [Fact]
    public async Task Engine_authority_revocation_removes_access_and_records_audit()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var authority = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await authority.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("engine-authority-1", "authority-binding"),
            RuntimeHostEngineSocketSpec(unit.TargetHandle, "/run/user/1000/docker.sock"),
            observed: null);

        await authority.RevokeAuthorityBindingAsync(EngineAuthorityRef());

        AuthorityBindingStatus status = await authority.GetStatusAsync(EngineAuthorityRef());
        status.Phase.Should().Be(ResourcePhase.Deleted);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoked);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.Verified);
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings.Should().BeEmpty();
        ledger.GetAuthorityAuditEvents(EngineAuthorityRef()).Should().Contain(audit =>
            audit.Kind == AuthorityAuditKind.Projected &&
            audit.SourceKind == AuthoritySourceKind.UnixSocket);
        ledger.GetAuthorityAuditEvents(EngineAuthorityRef()).Should().Contain(audit =>
            audit.Kind == AuthorityAuditKind.Revoked);
    }

    [Fact]
    public async Task Host_docker_socket_source_is_rejected_without_helper_dispatch()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var authority = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);

        AuthorityBindingStatus status = await authority.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("engine-authority-1", "authority-binding"),
            HostDockerSocketSpec(unit.TargetHandle),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Failed);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.AuthorityHostEngineSocketPassthroughRejected");
        helper.Requests.Should().BeEmpty();
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings.Should().BeEmpty();
    }

    [Fact]
    public async Task Provider_locus_engine_socket_source_fails_closed_without_helper_dispatch()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var authority = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);

        AuthorityBindingStatus status = await authority.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("engine-authority-1", "authority-binding"),
            RuntimeHostEngineSocketSpec(unit.TargetHandle, "/run/user/1000/docker.sock") with
            {
                Source = new AuthorityBindingSource
                {
                    Kind = AuthoritySourceKind.UnixSocket,
                    Locus = BoundaryLocus.Provider,
                    SocketPath = new UnixSocketPath("/run/user/1000/docker.sock"),
                },
            },
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Failed);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.AuthorityEngineSocketLocusRejected");
        helper.Requests.Should().BeEmpty();
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings.Should().BeEmpty();
    }

    [Fact]
    public async Task Docker_engine_socket_remains_vm_internal_unless_authority_bound()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> host = SeedReadyHost(ledger);

        EngineControlPlaneStatus engine = await EnsureReadyEngineAsync(ledger, helper, host.Resource);

        engine.Endpoints.Should().ContainSingle(endpoint =>
            endpoint.Api == EngineApiKind.DockerCompatible &&
            endpoint.Endpoint.Sensitivity == EndpointSensitivity.Sensitive &&
            endpoint.Endpoint.Endpoint.Path == "/run/user/1000/docker.sock" &&
            endpoint.SensitivePolicy!.Kind == SensitiveEndpointKind.EngineSocket);
        ledger.GetAuthorityBindings(AppleVirtualizationContractFixtures.RuntimeScope).Should().BeEmpty();
        helper.Requests.Should().NotContain(request =>
            request.Operation == AppleVirtualizationHelperOperation.EndpointPublish);
    }

    private static async Task<EngineControlPlaneStatus> EnsureReadyEngineAsync(
        AppleVirtualizationProviderStateLedger ledger,
        FakeAppleVirtualizationHelperClient helper,
        ResourceRef<RuntimeHost> host,
        EngineControlPlaneKind kind = EngineControlPlaneKind.DockerCompatible,
        EngineApiKind api = EngineApiKind.DockerCompatible,
        EngineAuthorityMode authorityMode = EngineAuthorityMode.Rootless)
    {
        var provider = new AppleVirtualizationEngineControlPlaneProvider(
            ledger,
            helper,
            new AppleVirtualizationProviderOptions
            {
                HelperTransportMode = AppleVirtualizationHelperTransportMode.InMemoryFake,
                EngineBootstrap = new AppleVirtualizationEngineBootstrapOptions
                {
                    Enabled = true,
                    AuthorityModeConfigured = true,
                    AuthorityMode = authorityMode,
                    ScriptedObservationState = AppleVirtualizationEngineObservationState.Ready,
                },
                FeatureGates = new AppleVirtualizationProviderFeatureGates
                {
                    EnableEngineControlPlane = true,
                },
            });

        return await provider.EnsureEngineControlPlaneAsync(
            Metadata<EngineControlPlane>("engine-1", "engine-control-plane"),
            new EngineControlPlaneSpec
            {
                Kind = kind,
                Api = api,
                AuthorityMode = authorityMode,
                Host = host,
                EndpointPolicy = new SensitiveEndpointPolicy
                {
                    Kind = SensitiveEndpointKind.EngineSocket,
                    AuthorityClass = authorityMode == EngineAuthorityMode.Rootless
                        ? SensitiveAuthorityClass.RootlessEngineControl
                        : SensitiveAuthorityClass.RootfulEngineControl,
                    Redaction = SensitiveRedactionLevel.RedactIdentifiers,
                    RequireAudit = true,
                    Lease = new SensitiveLeasePolicy
                    {
                        Lifetime = BindingLifetime.ExecutionUnit,
                        RevokeOnTargetStop = true,
                    },
                },
            },
            observed: null);
    }

    private static AuthorityBindingSpec RuntimeHostEngineSocketSpec(
        TargetHandle<ExecutionUnit> unit,
        string socketPath) =>
        new()
        {
            Kind = AuthorityBindingKind.GuestCapability,
            Source = new AuthorityBindingSource
            {
                Kind = AuthoritySourceKind.UnixSocket,
                Locus = BoundaryLocus.RuntimeHost,
                SocketPath = new UnixSocketPath(socketPath),
            },
            Target = new AuthorityBindingTarget(AuthorityTargetKind.ExecutionUnit, Unit: unit),
            Projection = new AuthorityBindingProjection
            {
                Kind = AuthorityProjectionKind.SocketPath,
                TargetSocketPath = new UnixSocketPath("/run/hpd/engine/docker.sock"),
                ReadOnly = false,
            },
            Policy = new AuthorityBindingPolicy
            {
                Direction = AuthorityBindingDirection.ProviderToGuest,
                AuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                EffectiveAuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                Redaction = SensitiveRedactionLevel.RedactIdentifiers,
                RequireAudit = true,
                Lease = new SensitiveLeasePolicy
                {
                    Lifetime = BindingLifetime.ExecutionUnit,
                    RevokeOnTargetStop = true,
                },
                Provenance = new SensitiveProvenance(Actor: "agent-82", Reason: "engine-api"),
            },
            AuditLabel = "engine-api:docker",
        };

    private static AuthorityBindingSpec HostDockerSocketSpec(TargetHandle<ExecutionUnit> unit) =>
        RuntimeHostEngineSocketSpec(unit, "/var/run/docker.sock") with
        {
            Source = new AuthorityBindingSource
            {
                Kind = AuthoritySourceKind.UnixSocket,
                Locus = BoundaryLocus.Host,
                SocketPath = new UnixSocketPath("/var/run/docker.sock"),
            },
        };

    private static EngineControlPlaneStatus ReadyEngineWithEndpointAddress(string address) =>
        new()
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = new ResourceGeneration(1),
            EnginePhase = EngineControlPlanePhase.Ready,
            Endpoints =
            [
                new EngineApiEndpointStatus(
                    EngineApiKind.DockerCompatible,
                    new ProviderNamedEndpoint(
                        "DockerCompatible",
                        ProviderEndpointPurpose.EngineApi,
                        new ProviderEndpoint("unix", address, Path: "/run/user/1000/docker.sock"),
                        ProviderTransportKind.UnixSocket,
                        EndpointSensitivity.Sensitive),
                    new SensitiveEndpointPolicy
                    {
                        Kind = SensitiveEndpointKind.EngineSocket,
                        AuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                        Redaction = SensitiveRedactionLevel.RedactIdentifiers,
                        RequireAudit = true,
                    }),
            ],
        };

    private static PublishedEndpointSpec EngineSensitivePublishedEndpointSpec() =>
        new()
        {
            Listener = new EndpointListenerSpec(
                EndpointListenerKind.HostAddress,
                NetworkTransport.Tcp,
                new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x7f000001),
                new PortRange(new NetworkPort(2375), 1),
                Socket: null),
            Target = new EndpointRouteTarget(
                EndpointTargetKind.NetworkMembership,
                Membership: MembershipRef(),
                Unit: null,
                Process: null,
                ServiceName: null,
                Transport: NetworkTransport.Tcp,
                Port: new NetworkPort(2375),
                SocketPath: null),
            RoutingNetwork = NetworkRef(),
            ExposurePolicy = new EndpointExposurePolicy
            {
                Scope = EndpointExposureScope.HostLocal,
                RequireStableListener = true,
            },
            SensitivePolicy = new SensitiveEndpointPolicy
            {
                Kind = SensitiveEndpointKind.EngineSocket,
                AuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                RequireAudit = true,
            },
        };

    private static AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> SeedReadyHost(
        AppleVirtualizationProviderStateLedger ledger) =>
        ledger.UpsertRuntimeHost(
            Metadata<RuntimeHost>("runtime-host-1", "runtime-host"),
            new RuntimeHostStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                HostPhase = RuntimeHostPhase.Ready,
                Readiness = new RuntimeHostReadinessStatus(true),
                GuestControl = new GuestControlStatus(Expected: true, Installed: true, Reachable: true),
            });

    private static AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> SeedReadyUnit(
        AppleVirtualizationProviderStateLedger ledger) =>
        ledger.UpsertExecutionUnit(
            Metadata<ExecutionUnit>("unit-1", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                UnitPhase = ExecutionUnitPhase.Ready,
            });

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
                Addresses =
                [
                    new NetworkAddressAssignment(
                        new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000002),
                        24,
                        AddressAssignmentKind.ProviderAssigned,
                        IsPrimary: true),
                ],
            },
            new NetworkMembershipSpec
            {
                Network = NetworkRef(),
                Target = new NetworkMembershipTarget(
                    NetworkMembershipTargetKind.ExecutionUnit,
                    Host: null,
                    Unit: AppleVirtualizationContractFixtures.ExecutionUnitHandle(),
                    Process: null),
            });
    }

    private static ResourceMetadata<TResource> Metadata<TResource>(string id, string kind)
        where TResource : IExecutionResourceMarker =>
        AppleVirtualizationContractFixtures.Metadata<TResource>(id, kind);

    private static ResourceRef<AuthorityBinding> EngineAuthorityRef() =>
        new(new ResourceId<AuthorityBinding>("engine-authority-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));

    private static ResourceRef<Network> NetworkRef() =>
        new(new ResourceId<Network>("network-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));

    private static ResourceRef<NetworkMembership> MembershipRef() =>
        new(new ResourceId<NetworkMembership>("membership-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));
}
