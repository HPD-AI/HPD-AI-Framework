namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.Engines;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using Xunit;

public sealed class AppleVirtualizationEngineControlPlaneProviderTests
{
    [Fact]
    public async Task Engine_resource_remains_degraded_when_bootstrap_is_absent()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceRef<RuntimeHost> host = SeedHost(ledger, ready: true).Resource;
        var provider = new AppleVirtualizationEngineControlPlaneProvider(
            ledger,
            helper,
            Options(engineEnabled: false, bootstrapEnabled: false));

        EngineControlPlaneStatus status = await provider.EnsureEngineControlPlaneAsync(
            Metadata("engine-1"),
            Spec(host),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.EnginePhase.Should().Be(EngineControlPlanePhase.Pending);
        status.Endpoints.Should().BeEmpty();
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineBootstrapAbsent");
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Engine_resource_becomes_ready_when_guest_agent_reports_compatible_engine_readiness()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceRef<RuntimeHost> host = SeedHost(ledger, ready: true).Resource;
        var provider = new AppleVirtualizationEngineControlPlaneProvider(
            ledger,
            helper,
            Options(engineEnabled: true, bootstrapEnabled: true, state: AppleVirtualizationEngineObservationState.Ready));

        EngineControlPlaneStatus status = await provider.EnsureEngineControlPlaneAsync(
            Metadata("engine-1"),
            Spec(host),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.EnginePhase.Should().Be(EngineControlPlanePhase.Ready);
        status.EngineGeneration.Should().Be(
            new EngineIncarnationGeneration(1));
        status.ProviderHandle.Should().NotBeNull();
        status.Endpoints.Should().ContainSingle(endpoint =>
            endpoint.Api == EngineApiKind.DockerCompatible &&
            endpoint.Endpoint.Sensitivity == EndpointSensitivity.Sensitive &&
            endpoint.Endpoint.Endpoint.Scheme == "unix" &&
            endpoint.SensitivePolicy!.Kind == SensitiveEndpointKind.EngineSocket);
        helper.Requests.Should().ContainSingle(request =>
            request.Operation == AppleVirtualizationHelperOperation.EngineStatus &&
            request.EngineStatusRequest!.Kind == EngineControlPlaneKind.DockerCompatible &&
            request.EngineStatusRequest.Api == EngineApiKind.DockerCompatible);
    }

    [Fact]
    public async Task Engine_resource_rejects_mismatched_response_engine_identity()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.EngineStatus,
            SequenceNumber = 2,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            PayloadSchema = AppleVirtualizationHelperProtocol.EngineStatusResponseSchema,
            EngineStatusResponse = new AppleVirtualizationEngineStatusResponse
            {
                HostId = "runtime-host-1",
                EngineId = "containerd",
                Ready = true,
                GuestEngineStatus = new AppleVirtualizationGuestAgentEngineStatus
                {
                    HostId = "runtime-host-1",
                    EngineId = "containerd",
                    Ready = true,
                    Generation = new AppleVirtualizationGuestAgentEngineGenerationStamp(
                        ProviderGeneration: 1,
                        HostStartGeneration: 0,
                        GuestBootId: "boot-a",
                        GuestBootGeneration: 1,
                        GuestAgentGeneration: 1,
                        EngineGeneration: 1),
                },
            },
        });
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceRef<RuntimeHost> host = SeedHost(ledger, ready: true).Resource;
        var provider = new AppleVirtualizationEngineControlPlaneProvider(
            ledger,
            helper,
            Options(engineEnabled: true, bootstrapEnabled: true));

        EngineControlPlaneStatus status = await provider.EnsureEngineControlPlaneAsync(
            Metadata("docker"),
            Spec(host),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineStatusStaleGeneration" &&
            diagnostic.Message.Contains("engine identity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Engine_resource_fails_with_bounded_transport_error_diagnostic()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.EngineStatus,
            SequenceNumber = 2,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Error,
            PayloadSchema = AppleVirtualizationHelperProtocol.ErrorSchema,
            Error = new AppleVirtualizationHelperError
            {
                Code = "AppleVirtualization.EngineStatusTransportUnavailable",
                Message = "The guest-agent engine status transport is unavailable.",
                Operation = AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.EngineStatus),
                FailedPhase = "Transport",
                Retryable = true,
                Severity = DiagnosticSeverity.Error,
            },
        });
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceRef<RuntimeHost> host = SeedHost(ledger, ready: true).Resource;
        var provider = new AppleVirtualizationEngineControlPlaneProvider(
            ledger,
            helper,
            Options(engineEnabled: true, bootstrapEnabled: true, state: AppleVirtualizationEngineObservationState.Ready));

        EngineControlPlaneStatus status = await provider.EnsureEngineControlPlaneAsync(
            Metadata("engine-transport-error"),
            Spec(host),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.EnginePhase.Should().Be(EngineControlPlanePhase.Failed);
        status.Endpoints.Should().BeEmpty();
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineStatusTransportUnavailable" &&
            diagnostic.TargetPath == "engine.status");
    }

    [Fact]
    public async Task Docker_and_containerd_kind_and_api_map_to_status()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceRef<RuntimeHost> host = SeedHost(ledger, ready: true).Resource;
        var provider = new AppleVirtualizationEngineControlPlaneProvider(
            ledger,
            helper,
            Options(engineEnabled: true, bootstrapEnabled: true, state: AppleVirtualizationEngineObservationState.Ready));

        EngineControlPlaneStatus docker = await provider.EnsureEngineControlPlaneAsync(
            Metadata("docker-engine"),
            Spec(host, EngineControlPlaneKind.DockerCompatible, EngineApiKind.DockerCompatible),
            observed: null);
        EngineControlPlaneStatus containerd = await provider.EnsureEngineControlPlaneAsync(
            Metadata("containerd-engine"),
            Spec(host, EngineControlPlaneKind.Containerd, EngineApiKind.ContainerdApi),
            observed: null);

        docker.Endpoints.Should().ContainSingle(endpoint => endpoint.Api == EngineApiKind.DockerCompatible);
        containerd.Endpoints.Should().ContainSingle(endpoint => endpoint.Api == EngineApiKind.ContainerdApi);
        helper.Requests.Any(request =>
            request.EngineStatusRequest != null &&
            request.EngineStatusRequest.Kind == EngineControlPlaneKind.DockerCompatible).Should().BeTrue();
        helper.Requests.Any(request =>
            request.EngineStatusRequest != null &&
            request.EngineStatusRequest.Kind == EngineControlPlaneKind.Containerd).Should().BeTrue();
    }

    [Theory]
    [InlineData(EngineAuthorityMode.Rootless, false)]
    [InlineData(EngineAuthorityMode.Rootful, false)]
    [InlineData(EngineAuthorityMode.Mixed, true)]
    public async Task Rootful_rootless_and_mixed_diagnostics_map_correctly(
        EngineAuthorityMode authorityMode,
        bool expectDiagnostic)
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceRef<RuntimeHost> host = SeedHost(ledger, ready: true).Resource;
        var provider = new AppleVirtualizationEngineControlPlaneProvider(
            ledger,
            helper,
            Options(engineEnabled: true, bootstrapEnabled: true, authorityMode: authorityMode, state: AppleVirtualizationEngineObservationState.Ready));

        EngineControlPlaneStatus status = await provider.EnsureEngineControlPlaneAsync(
            Metadata("engine-1"),
            Spec(host, authorityMode: authorityMode),
            observed: null);

        if (expectDiagnostic)
        {
            status.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code.Value == "AppleVirtualization.EngineAuthorityModeUnsupported" &&
                diagnostic.Severity == DiagnosticSeverity.Warning);
        }
        else
        {
            status.Diagnostics.Should().NotContain(diagnostic =>
                diagnostic.Code.Value == "AppleVirtualization.EngineAuthorityModeUnsupported");
        }
    }

    [Fact]
    public async Task Image_store_and_workload_adoption_are_diagnostic_bearing_without_claiming_ownership()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceRef<RuntimeHost> host = SeedHost(ledger, ready: true).Resource;
        var provider = new AppleVirtualizationEngineControlPlaneProvider(
            ledger,
            helper,
            Options(engineEnabled: true, bootstrapEnabled: true, state: AppleVirtualizationEngineObservationState.Ready));

        EngineControlPlaneStatus status = await provider.EnsureEngineControlPlaneAsync(
            Metadata("engine-1"),
            Spec(
                host,
                imageStore: EngineImageStoreMode.Remote,
                workloadAdoption: EngineWorkloadAdoptionMode.ObserveOnly),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.ExternalMutationPossible.Should().BeTrue();
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineImageStoreUnsupported");
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineWorkloadAdoptionUnsupported");
    }

    [Fact]
    public async Task Stale_engine_handle_fails_deterministically()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceRef<RuntimeHost> host = SeedHost(ledger, ready: true).Resource;
        var provider = new AppleVirtualizationEngineControlPlaneProvider(
            ledger,
            helper,
            Options(engineEnabled: true, bootstrapEnabled: true, state: AppleVirtualizationEngineObservationState.Ready));
        EngineControlPlaneStatus status = await provider.EnsureEngineControlPlaneAsync(
            Metadata("engine-1"),
            Spec(host),
            observed: null);
        AppleVirtualizationLedgerEntry<EngineControlPlane, EngineControlPlaneStatus> entry =
            ledger.TryGetEngineControlPlane(new ResourceRef<EngineControlPlane>(
                new ResourceId<EngineControlPlane>("engine-1"),
                AppleVirtualizationContractFixtures.RuntimeScope,
                new ResourceGeneration(1))).Entry!;
        ledger.AdvanceProviderGeneration();

        EngineControlPlaneStatus stopped = await provider.StopAsync(
            entry.TargetHandle,
            StopPolicy.Default);

        stopped.Phase.Should().Be(ResourcePhase.Failed);
        stopped.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == AppleVirtualizationHandleDiagnostics.StaleHandle);
        status.EnginePhase.Should().Be(EngineControlPlanePhase.Ready);
    }

    [Fact]
    public async Task Provider_registration_and_descriptor_are_honest_when_engine_gate_is_enabled()
    {
        var registry = new EnvironmentProviderRegistry();

        registry.RegisterModule(new AppleVirtualizationProviderModule(
            Options(engineEnabled: true, bootstrapEnabled: false)));

        ProviderDescriptor descriptor = (await registry.ListAsync()).Single();
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.EngineControlPlane);
        registry.EngineControlPlaneProviders.Should().ContainSingle();

        ProviderCapabilityReport report = await registry.ProviderCapabilityReporters.Single().GetCapabilitiesAsync(
            AppleVirtualizationProviderDescriptor.ProviderId,
            new ProviderCapabilityQuery(HostPlatform: new PlatformSpec("macos", "arm64")));
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.EngineControlPlaneCapability &&
            fact.AppliesTo == ProviderContractKind.EngineControlPlane &&
            fact.State == CapabilityState.RequiresConfiguration);
    }

    private static AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> SeedHost(
        AppleVirtualizationProviderStateLedger ledger,
        bool ready)
    {
        ResourceMetadata<RuntimeHost> metadata =
            AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("runtime-host-1", "runtime-host");
        return ledger.UpsertRuntimeHost(metadata, new RuntimeHostStatus
        {
            Phase = ready ? ResourcePhase.Ready : ResourcePhase.Pending,
            ObservedGeneration = metadata.Generation,
            HostPhase = ready ? RuntimeHostPhase.Ready : RuntimeHostPhase.Running,
            Readiness = new RuntimeHostReadinessStatus(ready),
            GuestControl = new GuestControlStatus(Expected: true, Installed: ready, Reachable: ready),
        });
    }

    private static ResourceMetadata<EngineControlPlane> Metadata(string id) =>
        AppleVirtualizationContractFixtures.Metadata<EngineControlPlane>(id, "engine-control-plane");

    private static EngineControlPlaneSpec Spec(
        ResourceRef<RuntimeHost> host,
        EngineControlPlaneKind kind = EngineControlPlaneKind.DockerCompatible,
        EngineApiKind api = EngineApiKind.DockerCompatible,
        EngineAuthorityMode authorityMode = EngineAuthorityMode.Rootless,
        EngineImageStoreMode imageStore = EngineImageStoreMode.ProviderManaged,
        EngineWorkloadAdoptionMode workloadAdoption = EngineWorkloadAdoptionMode.None) =>
        new()
        {
            Kind = kind,
            Api = api,
            AuthorityMode = authorityMode,
            ImageStore = imageStore,
            WorkloadAdoption = workloadAdoption,
            Host = host,
            EndpointPolicy = new SensitiveEndpointPolicy
            {
                Kind = SensitiveEndpointKind.EngineSocket,
                AuthorityClass = authorityMode == EngineAuthorityMode.Rootful
                    ? SensitiveAuthorityClass.RootfulEngineControl
                    : SensitiveAuthorityClass.RootlessEngineControl,
            },
        };

    private static AppleVirtualizationProviderOptions Options(
        bool engineEnabled,
        bool bootstrapEnabled,
        EngineAuthorityMode authorityMode = EngineAuthorityMode.Rootless,
        AppleVirtualizationEngineObservationState? state = null) =>
        new()
        {
            HelperTransportMode = AppleVirtualizationHelperTransportMode.InMemoryFake,
            EngineBootstrap = new AppleVirtualizationEngineBootstrapOptions
            {
                Enabled = bootstrapEnabled,
                AuthorityModeConfigured = bootstrapEnabled,
                AuthorityMode = authorityMode,
                ScriptedObservationState = state,
            },
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableEngineControlPlane = engineEnabled,
            },
        };
}
