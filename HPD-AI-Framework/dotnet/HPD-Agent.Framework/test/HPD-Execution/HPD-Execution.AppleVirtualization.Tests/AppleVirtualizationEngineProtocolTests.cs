namespace HPD.Execution.AppleVirtualization.Tests;

using System.Text.Json;
using FluentAssertions;
using HPD.Execution.AppleVirtualization.GuestAgent;
using HPD.Execution.AppleVirtualization.Protocol;
using HPD.Execution.Contracts;
using Xunit;

public sealed class AppleVirtualizationEngineProtocolTests
{
    [Fact]
    public void Engine_helper_dtos_round_trip_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EngineStatus,
            "engine-status-1",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.EngineStatusRequestSchema).ToResponse(sequenceNumber: 2) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.EngineStatusResponseSchema,
            EngineStatusRequest = new AppleVirtualizationEngineStatusRequest
            {
                HostId = "host-1",
                EngineId = "engine-1",
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                AuthorityMode = EngineAuthorityMode.Rootless,
                ImageStore = EngineImageStoreMode.ProviderManaged,
                WorkloadAdoption = EngineWorkloadAdoptionMode.None,
                ScriptedObservationState = AppleVirtualizationEngineObservationState.Ready,
            },
            EngineStatusResponse = AppleVirtualizationEngineStatusResponse.FromGuestStatus(
                new AppleVirtualizationEngineStatusRequest
                {
                    HostId = "host-1",
                    EngineId = "engine-1",
                    Kind = EngineControlPlaneKind.DockerCompatible,
                    Api = EngineApiKind.DockerCompatible,
                    AuthorityMode = EngineAuthorityMode.Rootless,
                },
                AppleVirtualizationGuestAgentEngineStatus.FromObservation(
                    "host-1",
                    "engine-1",
                    AppleVirtualizationEngineObservationState.Ready,
                    EngineControlPlaneKind.DockerCompatible,
                    EngineApiKind.DockerCompatible,
                    EngineAuthorityMode.Rootless,
                    EngineImageStoreMode.ProviderManaged,
                    EngineWorkloadAdoptionMode.None,
                    maxEndpoints: 8,
                    maxContainers: 32)),
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(json, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.EngineStatus);
        roundTrip.EngineStatusRequest.Should().NotBeNull();
        roundTrip.EngineStatusRequest!.ScriptedObservationState.Should().Be(AppleVirtualizationEngineObservationState.Ready);
        roundTrip.EngineStatusResponse.Should().NotBeNull();
        roundTrip.EngineStatusResponse!.Ready.Should().BeTrue();
        roundTrip.EngineStatusResponse.Endpoints.Should().ContainSingle();
        roundTrip.EngineStatusResponse.Endpoints[0].RequiresAuthorityBinding.Should().BeTrue();
        roundTrip.EngineStatusResponse.Endpoints[0].SensitivePolicy.Kind.Should().Be(SensitiveEndpointKind.EngineSocket);
        roundTrip.EngineStatusResponse.Status.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Engine_guest_agent_dtos_round_trip_through_source_generated_json()
    {
        AppleVirtualizationGuestAgentEnvelope envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.EngineStatus,
            "guest-engine-status-1",
            sequenceNumber: 10,
            AppleVirtualizationGuestAgentProtocol.EngineSchema).ToResponse(sequenceNumber: 11) with
        {
            HostId = "host-1",
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.EngineSchema,
            EngineStatusRequest = new AppleVirtualizationGuestAgentEngineStatusRequest
            {
                HostId = "host-1",
                EngineId = "engine-containerd",
                Kind = EngineControlPlaneKind.Containerd,
                Api = EngineApiKind.ContainerdApi,
                AuthorityMode = EngineAuthorityMode.Rootful,
                ImageStore = EngineImageStoreMode.EngineLocal,
                WorkloadAdoption = EngineWorkloadAdoptionMode.ObserveOnly,
                ScriptedObservationState = AppleVirtualizationEngineObservationState.Degraded,
            },
            EngineStatus = AppleVirtualizationGuestAgentEngineStatus.FromObservation(
                "host-1",
                "engine-containerd",
                AppleVirtualizationEngineObservationState.Degraded,
                EngineControlPlaneKind.Containerd,
                EngineApiKind.ContainerdApi,
                EngineAuthorityMode.Rootful,
                EngineImageStoreMode.EngineLocal,
                EngineWorkloadAdoptionMode.ObserveOnly,
                maxEndpoints: 8,
                maxContainers: 32),
        };

        byte[] json = AppleVirtualizationGuestAgentJsonCodec.Encode(envelope);
        AppleVirtualizationGuestAgentEnvelope roundTrip = AppleVirtualizationGuestAgentJsonCodec.Decode(json);

        roundTrip.Operation.Should().Be(AppleVirtualizationGuestAgentOperation.EngineStatus);
        roundTrip.EngineStatusRequest.Should().NotBeNull();
        roundTrip.EngineStatusRequest!.AuthorityMode.Should().Be(EngineAuthorityMode.Rootful);
        roundTrip.EngineStatus.Should().NotBeNull();
        roundTrip.EngineStatus!.ObservationState.Should().Be(AppleVirtualizationEngineObservationState.Degraded);
        roundTrip.EngineStatus.Api.Should().Be(EngineApiKind.ContainerdApi);
        roundTrip.EngineStatus.ImageStore.Should().Be(EngineImageStoreMode.EngineLocal);
        roundTrip.EngineStatus.WorkloadAdoption.Should().Be(EngineWorkloadAdoptionMode.ObserveOnly);
        roundTrip.EngineStatus.Status.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Real_shaped_engine_observation_maps_containerd_socket_and_bounds_payloads()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EngineStatus,
            "engine-status-real-shape",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.EngineStatusRequestSchema) with
        {
            EngineStatusRequest = new AppleVirtualizationEngineStatusRequest
            {
                HostId = "host-1",
                EngineId = "engine-containerd",
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                AuthorityMode = EngineAuthorityMode.Rootless,
                ObservationLocus = BoundaryLocus.RuntimeHost,
                ObservedSocketPath = "/run/containerd/containerd.sock",
                ObservedVersion = new string('v', 64),
                ObservedStatus = new string('s', 64),
                ObservedContainers =
                [
                    new AppleVirtualizationGuestAgentContainerObservation { ContainerId = "c1", Phase = ResourcePhase.Ready },
                    new AppleVirtualizationGuestAgentContainerObservation { ContainerId = "c2", Phase = ResourcePhase.Pending },
                ],
                ObservedDiagnostics =
                [
                    Diagnostic("AppleVirtualization.EngineStatus.RealDiagnostic1"),
                    Diagnostic("AppleVirtualization.EngineStatus.RealDiagnostic2"),
                ],
                MaxContainers = 1,
                MaxDiagnostics = 1,
                MaxVersionLength = 8,
                MaxStatusLength = 9,
                ScriptedObservationState = AppleVirtualizationEngineObservationState.Ready,
            },
        };

        AppleVirtualizationHelperEnvelope response = helper.SendAsync(request).GetAwaiter().GetResult();

        response.EngineStatusResponse.Should().NotBeNull();
        AppleVirtualizationEngineStatusResponse status = response.EngineStatusResponse!;
        status.Ready.Should().BeTrue();
        status.Kind.Should().Be(EngineControlPlaneKind.Containerd);
        status.Api.Should().Be(EngineApiKind.ContainerdApi);
        status.AuthorityMode.Should().Be(EngineAuthorityMode.Rootful);
        status.Version.Should().HaveLength(8);
        status.Status.Should().HaveLength(9);
        status.Endpoints.Should().ContainSingle(endpoint =>
            endpoint.SocketPath.HasValue &&
            endpoint.SocketPath.Value.Value == "/run/containerd/containerd.sock" &&
            endpoint.SensitivePolicy.AuthorityClass == SensitiveAuthorityClass.RootfulEngineControl);
        status.Containers.Should().ContainSingle(container => container.ContainerId == "c1");
        status.ContainersTruncated.Should().BeTrue();
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineStatus.RealDiagnostic1");
        status.DiagnosticsTruncated.Should().BeTrue();
    }

    [Theory]
    [InlineData(AppleVirtualizationEngineObservationState.Ready, true, ResourcePhase.Ready, EngineControlPlanePhase.Ready)]
    [InlineData(AppleVirtualizationEngineObservationState.Degraded, false, ResourcePhase.Degraded, EngineControlPlanePhase.Degraded)]
    [InlineData(AppleVirtualizationEngineObservationState.NotInstalled, false, ResourcePhase.Pending, EngineControlPlanePhase.Pending)]
    [InlineData(AppleVirtualizationEngineObservationState.Unknown, false, ResourcePhase.Pending, EngineControlPlanePhase.Pending)]
    public void Real_shaped_engine_observation_reports_ready_degraded_not_installed_and_unavailable(
        AppleVirtualizationEngineObservationState state,
        bool ready,
        ResourcePhase expectedPhase,
        EngineControlPlanePhase expectedEnginePhase)
    {
        AppleVirtualizationGuestAgentEngineStatus status =
            AppleVirtualizationGuestAgentEngineStatus.FromRequest(new AppleVirtualizationGuestAgentEngineStatusRequest
            {
                HostId = "host-1",
                EngineId = "engine-1",
                ObservationLocus = BoundaryLocus.RuntimeHost,
                ObservedSocketPath = "/run/user/1000/docker.sock",
                ObservedStatus = state == AppleVirtualizationEngineObservationState.Unknown
                    ? "guest-agent transport unavailable"
                    : null,
                ScriptedObservationState = state,
            });

        status.Ready.Should().Be(ready);
        status.Phase.Should().Be(expectedPhase);
        status.EnginePhase.Should().Be(expectedEnginePhase);
        status.Status.Should().NotBeNullOrWhiteSpace();
        status.Endpoints.All(endpoint => endpoint.RequiresAuthorityBinding).Should().BeTrue();
    }

    [Fact]
    public void Host_engine_sockets_are_rejected_for_engine_status_observation()
    {
        AppleVirtualizationGuestAgentEngineStatus status =
            AppleVirtualizationGuestAgentEngineStatus.FromRequest(new AppleVirtualizationGuestAgentEngineStatusRequest
            {
                HostId = "host-1",
                EngineId = "engine-1",
                ObservationLocus = BoundaryLocus.Host,
                ObservedSocketPath = "/var/run/docker.sock",
                ScriptedObservationState = AppleVirtualizationEngineObservationState.Ready,
            });

        status.Ready.Should().BeFalse();
        status.Phase.Should().Be(ResourcePhase.Failed);
        status.EnginePhase.Should().Be(EngineControlPlanePhase.Failed);
        status.ObservationState.Should().Be(AppleVirtualizationEngineObservationState.Unsupported);
        status.Endpoints.Should().BeEmpty();
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineStatusHostSocketRejected" &&
            diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Engine_provisioning_helper_dtos_round_trip_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EngineProvision,
            "engine-provision-1",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.EngineProvisionRequestSchema).ToResponse(sequenceNumber: 2) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.EngineProvisionResponseSchema,
            EngineProvisioningRequest = new AppleVirtualizationEngineProvisioningRequest
            {
                HostId = "host-1",
                EngineId = "engine-1",
                AuthorityMode = EngineAuthorityMode.Rootless,
                AllowPackageInstall = true,
                AllowServiceEnablement = true,
                ProvisioningTimeoutMilliseconds = 250,
                MaxCapturedOutputBytes = 16,
                ScriptedExecutionState = AppleVirtualizationEngineProvisioningExecutionState.Succeeded,
                ScriptedOutput = "existing-engine with very long output",
                ScriptedStdout = "stdout-existing-engine with very long output",
                ScriptedStderr = "stderr provisioning output",
            },
            EngineProvisioningResponse = new AppleVirtualizationEngineProvisioningResponse
            {
                HostId = "host-1",
                EngineId = "engine-1",
                Phase = AppleVirtualizationEngineProvisioningPhase.Planned,
                AuthorityMode = EngineAuthorityMode.Rootless,
                GuestSocketPath = "/run/user/1000/docker.sock",
                Plan =
                [
                    new AppleVirtualizationEngineProvisioningPlanStep
                    {
                        Name = "validate-prerequisites",
                        Action = AppleVirtualizationEngineProvisioningAction.ValidatePrerequisites,
                    },
                ],
                Output = new AppleVirtualizationEngineProvisioningOutputCapture
                {
                    MaxCapturedBytes = 16,
                    CapturedBytes = 16,
                    Truncated = true,
                    Text = "existing-engine ",
                    StdoutCapturedBytes = 16,
                    StderrCapturedBytes = 16,
                    StdoutTruncated = true,
                    StderrTruncated = true,
                    StdoutText = "stdout-existing-",
                    StderrText = "stderr provision",
                },
                Evidence = new AppleVirtualizationEngineProvisioningEvidence
                {
                    HelperMediated = true,
                    GuestAgentMediated = true,
                    PackageManager = AppleVirtualizationEngineProvisioningPackageManager.Apt,
                    PackageManagerAvailable = true,
                    NetworkAvailable = true,
                    WritableGuestStorageAvailable = true,
                    SystemdAvailable = true,
                    UserSystemdAvailable = true,
                    ExistingEngineObserved = true,
                    PackageInstallAllowed = true,
                    ServiceEnablementAllowed = true,
                    TimeoutMilliseconds = 250,
                    MaxCapturedOutputBytes = 16,
                    StdoutCapturedBytes = 16,
                    StderrCapturedBytes = 16,
                    StdoutTruncated = true,
                    StderrTruncated = true,
                },
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(json, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.EngineProvision);
        roundTrip.EngineProvisioningRequest.Should().NotBeNull();
        roundTrip.EngineProvisioningRequest!.AllowPackageInstall.Should().BeTrue();
        roundTrip.EngineProvisioningRequest.AllowServiceEnablement.Should().BeTrue();
        roundTrip.EngineProvisioningRequest.ProvisioningTimeoutMilliseconds.Should().Be(250);
        roundTrip.EngineProvisioningRequest.ScriptedExecutionState.Should().Be(AppleVirtualizationEngineProvisioningExecutionState.Succeeded);
        roundTrip.EngineProvisioningRequest.ScriptedStdout.Should().StartWith("stdout-existing-engine");
        roundTrip.EngineProvisioningRequest.ScriptedStderr.Should().StartWith("stderr provisioning");
        roundTrip.EngineProvisioningResponse.Should().NotBeNull();
        roundTrip.EngineProvisioningResponse!.Plan.Should().ContainSingle();
        roundTrip.EngineProvisioningResponse.Output.Truncated.Should().BeTrue();
        roundTrip.EngineProvisioningResponse.Output.StdoutTruncated.Should().BeTrue();
        roundTrip.EngineProvisioningResponse.Output.StderrText.Should().Be("stderr provision");
        roundTrip.EngineProvisioningResponse.Evidence.HelperMediated.Should().BeTrue();
        roundTrip.EngineProvisioningResponse.Evidence.GuestAgentMediated.Should().BeTrue();
        roundTrip.EngineProvisioningResponse.Evidence.HostShellInvoked.Should().BeFalse();
        roundTrip.EngineProvisioningResponse.Evidence.HostDockerInvoked.Should().BeFalse();
        roundTrip.EngineProvisioningResponse.Evidence.PackageManager.Should().Be(AppleVirtualizationEngineProvisioningPackageManager.Apt);
        roundTrip.EngineProvisioningResponse.Evidence.ExistingEngineObserved.Should().BeTrue();
        roundTrip.EngineProvisioningResponse.Evidence.StdoutTruncated.Should().BeTrue();
    }

    [Fact]
    public void Engine_provisioning_guest_agent_dtos_round_trip_through_source_generated_json()
    {
        AppleVirtualizationGuestAgentEnvelope envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.EngineProvision,
            "guest-engine-provision-1",
            sequenceNumber: 10,
            AppleVirtualizationGuestAgentProtocol.EngineProvisioningSchema).ToResponse(sequenceNumber: 11) with
        {
            HostId = "host-1",
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.EngineProvisioningSchema,
            EngineProvisioningRequest = new AppleVirtualizationGuestAgentEngineProvisioningRequest
            {
                HostId = "host-1",
                EngineId = "engine-containerd",
                Kind = EngineControlPlaneKind.Containerd,
                Api = EngineApiKind.ContainerdApi,
                AuthorityMode = EngineAuthorityMode.Rootful,
                AllowPackageInstall = true,
                AllowServiceEnablement = true,
                ProvisioningTimeoutMilliseconds = 500,
                ScriptedExecutionState = AppleVirtualizationEngineProvisioningExecutionState.Failed,
            },
            EngineProvisioningResult = new AppleVirtualizationGuestAgentEngineProvisioningResult
            {
                HostId = "host-1",
                EngineId = "engine-containerd",
                Kind = EngineControlPlaneKind.Containerd,
                Api = EngineApiKind.ContainerdApi,
                AuthorityMode = EngineAuthorityMode.Rootful,
                Phase = AppleVirtualizationEngineProvisioningPhase.Planned,
                GuestSocketPath = "/run/containerd/containerd.sock",
                Evidence = new AppleVirtualizationEngineProvisioningEvidence
                {
                    HelperMediated = true,
                    GuestAgentMediated = true,
                    PackageInstallAllowed = true,
                    ServiceEnablementAllowed = true,
                    TimeoutMilliseconds = 500,
                },
            },
        };

        byte[] json = AppleVirtualizationGuestAgentJsonCodec.Encode(envelope);
        AppleVirtualizationGuestAgentEnvelope roundTrip = AppleVirtualizationGuestAgentJsonCodec.Decode(json);

        roundTrip.Operation.Should().Be(AppleVirtualizationGuestAgentOperation.EngineProvision);
        roundTrip.EngineProvisioningRequest.Should().NotBeNull();
        roundTrip.EngineProvisioningRequest!.Api.Should().Be(EngineApiKind.ContainerdApi);
        roundTrip.EngineProvisioningRequest.AllowServiceEnablement.Should().BeTrue();
        roundTrip.EngineProvisioningRequest.ScriptedExecutionState.Should().Be(AppleVirtualizationEngineProvisioningExecutionState.Failed);
        roundTrip.EngineProvisioningResult.Should().NotBeNull();
        roundTrip.EngineProvisioningResult!.InstallAttempted.Should().BeFalse();
        roundTrip.EngineProvisioningResult.GuestSocketPath.Should().Be("/run/containerd/containerd.sock");
        roundTrip.EngineProvisioningResult.Evidence.GuestAgentMediated.Should().BeTrue();
        roundTrip.EngineProvisioningResult.Evidence.TimeoutMilliseconds.Should().Be(500);
    }

    [Theory]
    [InlineData(AppleVirtualizationEngineObservationState.Ready, true, EngineControlPlanePhase.Ready)]
    [InlineData(AppleVirtualizationEngineObservationState.Degraded, false, EngineControlPlanePhase.Degraded)]
    [InlineData(AppleVirtualizationEngineObservationState.NotInstalled, false, EngineControlPlanePhase.Pending)]
    public void Fake_guest_agent_reports_engine_states(
        AppleVirtualizationEngineObservationState state,
        bool ready,
        EngineControlPlanePhase expectedPhase)
    {
        var guest = new FakeAppleVirtualizationGuestAgentHarness();
        AppleVirtualizationGuestAgentEnvelope request = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.EngineStatus,
            "engine-status",
            sequenceNumber: 1,
            AppleVirtualizationGuestAgentProtocol.EngineSchema) with
        {
            EngineStatusRequest = new AppleVirtualizationGuestAgentEngineStatusRequest
            {
                HostId = "host-1",
                EngineId = "engine-1",
                ScriptedObservationState = state,
            },
        };

        AppleVirtualizationGuestAgentEnvelope response = guest.SendAsync(request).GetAwaiter().GetResult();

        response.EngineStatus.Should().NotBeNull();
        response.EngineStatus!.Ready.Should().Be(ready);
        response.EngineStatus.EnginePhase.Should().Be(expectedPhase);
        response.EngineStatus.Endpoints.All(endpoint => endpoint.RequiresAuthorityBinding).Should().BeTrue();
    }

    [Fact]
    public void Fake_helper_routes_engine_status_operation()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EngineStatus,
            "engine-status",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.EngineStatusRequestSchema) with
        {
            EngineStatusRequest = new AppleVirtualizationEngineStatusRequest
            {
                HostId = "host-1",
                EngineId = "engine-1",
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                AuthorityMode = EngineAuthorityMode.Rootless,
                ScriptedObservationState = AppleVirtualizationEngineObservationState.Ready,
            },
        };

        AppleVirtualizationHelperEnvelope response = helper.SendAsync(request).GetAwaiter().GetResult();

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.EngineStatusResponseSchema);
        response.EngineStatusResponse.Should().NotBeNull();
        response.EngineStatusResponse!.Ready.Should().BeTrue();
        response.EngineStatusResponse.Endpoints.Should().ContainSingle();
        response.EngineStatusResponse.Endpoints[0].HpdPublished.Should().BeFalse();
        response.EngineStatusResponse.Endpoints[0].SensitivePolicy.AuthorityClass.Should().Be(SensitiveAuthorityClass.RootlessEngineControl);
    }

    [Fact]
    public async Task Fake_helper_routes_engine_provisioning_with_bounded_output_and_idempotent_existing_observation()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EngineProvision,
            "engine-provision",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.EngineProvisionRequestSchema) with
        {
            EngineProvisioningRequest = new AppleVirtualizationEngineProvisioningRequest
            {
                HostId = "host-1",
                EngineId = "engine-1",
                AuthorityMode = EngineAuthorityMode.Rootless,
                AllowPackageInstall = true,
                AllowServiceEnablement = true,
                MaxCapturedOutputBytes = 8,
                ScriptedOutput = "existing-engine already installed",
            },
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.EngineProvisionResponseSchema);
        response.EngineProvisioningResponse.Should().NotBeNull();
        response.EngineProvisioningResponse!.ExistingEngineObserved.Should().BeTrue();
        response.EngineProvisioningResponse.InstallAttempted.Should().BeFalse();
        response.EngineProvisioningResponse.Output.Truncated.Should().BeTrue();
        response.EngineProvisioningResponse.GuestSocketPath.Should().Be("/run/user/1000/docker.sock");
        response.EngineProvisioningResponse.Evidence.ExistingEngineObserved.Should().BeTrue();
        response.EngineProvisioningResponse.Evidence.InstallAttempted.Should().BeFalse();
        response.EngineProvisioningResponse.Evidence.HostShellInvoked.Should().BeFalse();
        response.EngineProvisioningResponse.Evidence.HostDockerInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task Fake_helper_routes_engine_provisioning_through_guest_agent_execution_shape()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EngineProvision,
            "engine-provision-real-execute",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.EngineProvisionRequestSchema) with
        {
            EngineProvisioningRequest = new AppleVirtualizationEngineProvisioningRequest
            {
                HostId = "host-1",
                EngineId = "engine-1",
                AuthorityMode = EngineAuthorityMode.Rootless,
                AllowPackageInstall = true,
                AllowServiceEnablement = true,
                MaxCapturedOutputBytes = 8,
                ScriptedExecutionState = AppleVirtualizationEngineProvisioningExecutionState.Succeeded,
                ScriptedStdout = "stdout output is long",
                ScriptedStderr = "stderr output is long",
            },
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.EngineProvisioningResponse.Should().NotBeNull();
        AppleVirtualizationEngineProvisioningResponse provisioning = response.EngineProvisioningResponse!;
        provisioning.Phase.Should().Be(AppleVirtualizationEngineProvisioningPhase.Ready);
        provisioning.InstallAttempted.Should().BeTrue();
        provisioning.Output.StdoutText.Should().Be("stdout o");
        provisioning.Output.StderrText.Should().Be("stderr o");
        provisioning.Output.StdoutTruncated.Should().BeTrue();
        provisioning.Output.StderrTruncated.Should().BeTrue();
        provisioning.Evidence.HelperMediated.Should().BeTrue();
        provisioning.Evidence.GuestAgentMediated.Should().BeTrue();
        provisioning.Evidence.InstallAttempted.Should().BeTrue();
        provisioning.Evidence.PackageInstallAllowed.Should().BeTrue();
        provisioning.Evidence.ServiceEnablementAllowed.Should().BeTrue();
        provisioning.Evidence.StdoutCapturedBytes.Should().Be(8);
        provisioning.Evidence.StderrCapturedBytes.Should().Be(8);
        provisioning.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task Fake_helper_blocks_engine_provisioning_when_install_or_service_gates_are_absent()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EngineProvision,
            "engine-provision-gates",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.EngineProvisionRequestSchema) with
        {
            EngineProvisioningRequest = new AppleVirtualizationEngineProvisioningRequest
            {
                HostId = "host-1",
                EngineId = "engine-1",
                AuthorityMode = EngineAuthorityMode.Rootless,
            },
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.EngineProvisioningResponse.Should().NotBeNull();
        AppleVirtualizationEngineProvisioningResponse provisioning = response.EngineProvisioningResponse!;
        provisioning.Phase.Should().Be(AppleVirtualizationEngineProvisioningPhase.Degraded);
        provisioning.InstallAttempted.Should().BeFalse();
        provisioning.Diagnostics.Select(diagnostic => diagnostic.Code.Value).Should().Contain([
            "AppleVirtualization.EngineProvisioning.PackageInstallDisabled",
            "AppleVirtualization.EngineProvisioning.ServiceEnablementDisabled",
        ]);
    }

    [Fact]
    public void Engine_helper_l15_json_surface_contains_swift_parity_fields()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EngineProvision,
            "engine-provision-schema-parity",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.EngineProvisionRequestSchema).ToResponse(sequenceNumber: 2) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.EngineProvisionResponseSchema,
            EngineStatusRequest = new AppleVirtualizationEngineStatusRequest
            {
                HostId = "host-1",
                EngineId = "engine-containerd",
                ObservationLocus = BoundaryLocus.RuntimeHost,
                ObservedSocketPath = "/run/containerd/containerd.sock",
                ObservedVersion = "containerd 2.0",
                ObservedStatus = "ready",
                ObservedContainers =
                [
                    new AppleVirtualizationGuestAgentContainerObservation { ContainerId = "c1", Phase = ResourcePhase.Ready },
                ],
                ObservedDiagnostics =
                [
                    Diagnostic("AppleVirtualization.EngineStatus.RealDiagnostic1"),
                ],
            },
            EngineStatusResponse = AppleVirtualizationEngineStatusResponse.FromGuestStatus(
                new AppleVirtualizationEngineStatusRequest
                {
                    HostId = "host-1",
                    EngineId = "engine-containerd",
                    ObservationLocus = BoundaryLocus.RuntimeHost,
                    ObservedSocketPath = "/run/containerd/containerd.sock",
                    ObservedVersion = "containerd 2.0",
                    ObservedStatus = "ready",
                    ObservedContainers =
                    [
                        new AppleVirtualizationGuestAgentContainerObservation { ContainerId = "c1", Phase = ResourcePhase.Ready },
                    ],
                    ObservedDiagnostics =
                    [
                        Diagnostic("AppleVirtualization.EngineStatus.RealDiagnostic1"),
                    ],
                    ScriptedObservationState = AppleVirtualizationEngineObservationState.Ready,
                },
                AppleVirtualizationGuestAgentEngineStatus.FromRequest(new AppleVirtualizationGuestAgentEngineStatusRequest
                {
                    HostId = "host-1",
                    EngineId = "engine-containerd",
                    ObservationLocus = BoundaryLocus.RuntimeHost,
                    ObservedSocketPath = "/run/containerd/containerd.sock",
                    ObservedVersion = "containerd 2.0",
                    ObservedStatus = "ready",
                    ObservedContainers =
                    [
                        new AppleVirtualizationGuestAgentContainerObservation { ContainerId = "c1", Phase = ResourcePhase.Ready },
                    ],
                    IncludeContainers = true,
                    ObservedDiagnostics =
                    [
                        Diagnostic("AppleVirtualization.EngineStatus.RealDiagnostic1"),
                    ],
                    ScriptedObservationState = AppleVirtualizationEngineObservationState.Ready,
                })),
            EngineProvisioningRequest = new AppleVirtualizationEngineProvisioningRequest
            {
                HostId = "host-1",
                EngineId = "engine-containerd",
                Api = EngineApiKind.ContainerdApi,
                AuthorityMode = EngineAuthorityMode.Rootful,
                AllowPackageInstall = true,
                MaxCapturedOutputBytes = 8,
                ScriptedOutput = "existing-engine already installed",
            },
            EngineProvisioningResponse = AppleVirtualizationEngineProvisioningPlanner.Plan(
                new AppleVirtualizationEngineProvisioningRequest
                {
                    HostId = "host-1",
                    EngineId = "engine-containerd",
                    Api = EngineApiKind.ContainerdApi,
                    AuthorityMode = EngineAuthorityMode.Rootful,
                    AllowPackageInstall = true,
                    MaxCapturedOutputBytes = 8,
                    ScriptedOutput = "existing-engine already installed",
                }),
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        root.GetProperty("Operation").GetInt32().Should().Be((int)AppleVirtualizationHelperOperation.EngineProvision);
        root.GetProperty("PayloadSchema").GetProperty("Value").GetString()
            .Should().Be(AppleVirtualizationHelperProtocol.EngineProvisionResponseSchema.Value);

        JsonElement statusRequest = root.GetProperty("EngineStatusRequest");
        statusRequest.GetProperty("ObservationLocus").GetInt32().Should().Be((int)BoundaryLocus.RuntimeHost);
        statusRequest.GetProperty("ObservedSocketPath").GetString().Should().Be("/run/containerd/containerd.sock");
        statusRequest.GetProperty("ObservedVersion").GetString().Should().Be("containerd 2.0");
        statusRequest.GetProperty("ObservedStatus").GetString().Should().Be("ready");
        statusRequest.GetProperty("ObservedContainers").GetArrayLength().Should().Be(1);
        statusRequest.GetProperty("ObservedDiagnostics").GetArrayLength().Should().Be(1);

        JsonElement statusResponse = root.GetProperty("EngineStatusResponse");
        statusResponse.GetProperty("Version").GetString().Should().Be("containerd 2.0");
        statusResponse.GetProperty("Status").GetString().Should().Be("ready");
        statusResponse.GetProperty("Containers").GetArrayLength().Should().Be(1);
        statusResponse.GetProperty("ContainersTruncated").GetBoolean().Should().BeFalse();
        statusResponse.GetProperty("DiagnosticsTruncated").GetBoolean().Should().BeFalse();

        JsonElement provisioningResponse = root.GetProperty("EngineProvisioningResponse");
        provisioningResponse.GetProperty("ExistingEngineObserved").GetBoolean().Should().BeTrue();
        provisioningResponse.GetProperty("InstallAttempted").GetBoolean().Should().BeFalse();
        provisioningResponse.GetProperty("GuestSocketPath").GetString().Should().Be("/run/containerd/containerd.sock");
        provisioningResponse.GetProperty("Output").GetProperty("Truncated").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("/run/user/1000/docker.sock", EngineControlPlaneKind.DockerCompatible, EngineApiKind.DockerCompatible, EngineAuthorityMode.Rootless)]
    [InlineData("/var/run/docker.sock", EngineControlPlaneKind.DockerCompatible, EngineApiKind.DockerCompatible, EngineAuthorityMode.Rootful)]
    [InlineData("/run/containerd/containerd.sock", EngineControlPlaneKind.Containerd, EngineApiKind.ContainerdApi, EngineAuthorityMode.Rootful)]
    [InlineData("/run/user/1000/podman/podman.sock", EngineControlPlaneKind.Podman, EngineApiKind.PodmanApi, EngineAuthorityMode.Rootless)]
    [InlineData("/run/podman/podman.sock", EngineControlPlaneKind.Podman, EngineApiKind.PodmanApi, EngineAuthorityMode.Rootful)]
    [InlineData("/run/user/1000/buildkit-default/buildkitd.sock", EngineControlPlaneKind.BuildKit, EngineApiKind.BuildKitApi, EngineAuthorityMode.Rootless)]
    [InlineData("/run/buildkit/buildkitd.sock", EngineControlPlaneKind.BuildKit, EngineApiKind.BuildKitApi, EngineAuthorityMode.Rootful)]
    public async Task Fake_guest_engine_probe_detects_guest_visible_engine_sockets(
        string socketPath,
        EngineControlPlaneKind expectedKind,
        EngineApiKind expectedApi,
        EngineAuthorityMode expectedAuthorityMode)
    {
        var guest = new FakeAppleVirtualizationGuestAgentHarness().WithEngineProbe(
            new FakeAppleVirtualizationGuestAgentEngineProbe(new AppleVirtualizationGuestAgentEngineProbeObservation
            {
                Readiness = AppleVirtualizationGuestAgentEngineProbeReadiness.Ready,
                SocketPath = new UnixSocketPath(socketPath),
                SocketExists = true,
                SocketAccessible = true,
                VersionOutput = "engine 27.0.0\nignored trailing output",
                StatusOutput = "active (running)\nignored trailing output",
                Containers =
                [
                    new AppleVirtualizationGuestAgentContainerObservation
                    {
                        ContainerId = "container-1",
                        Phase = ResourcePhase.Ready,
                    },
                ],
            }));

        AppleVirtualizationGuestAgentEnvelope response = await guest.SendAsync(GuestEngineStatusRequest(
            socketPath.Contains("containerd", StringComparison.Ordinal)
                ? EngineControlPlaneKind.Containerd
                : socketPath.Contains("podman", StringComparison.Ordinal)
                    ? EngineControlPlaneKind.Podman
                : socketPath.Contains("buildkit", StringComparison.Ordinal)
                    ? EngineControlPlaneKind.BuildKit
                : EngineControlPlaneKind.DockerCompatible,
            socketPath.Contains("containerd", StringComparison.Ordinal)
                ? EngineApiKind.ContainerdApi
                : socketPath.Contains("podman", StringComparison.Ordinal)
                    ? EngineApiKind.PodmanApi
                : socketPath.Contains("buildkit", StringComparison.Ordinal)
                    ? EngineApiKind.BuildKitApi
                : EngineApiKind.DockerCompatible,
            expectedAuthorityMode,
            includeContainers: true));

        response.EngineStatus.Should().NotBeNull();
        AppleVirtualizationGuestAgentEngineStatus status = response.EngineStatus!;
        status.Ready.Should().BeTrue();
        status.Kind.Should().Be(expectedKind);
        status.Api.Should().Be(expectedApi);
        status.AuthorityMode.Should().Be(expectedAuthorityMode);
        status.Version.Should().Be("engine 27.0.0");
        status.Status.Should().Be("active (running)");
        status.Endpoints.Should().ContainSingle(endpoint =>
            endpoint.SocketPath.HasValue &&
            endpoint.SocketPath.Value.Value == socketPath &&
            endpoint.GuestVisibleOnly &&
            !endpoint.HpdPublished &&
            endpoint.RequiresAuthorityBinding);
        status.Containers.Should().ContainSingle(container => container.ContainerId == "container-1");
    }

    [Fact]
    public async Task Fake_guest_engine_probe_maps_not_installed_without_host_fallback()
    {
        var guest = new FakeAppleVirtualizationGuestAgentHarness().WithEngineProbe(
            new FakeAppleVirtualizationGuestAgentEngineProbe(new AppleVirtualizationGuestAgentEngineProbeObservation
            {
                Readiness = AppleVirtualizationGuestAgentEngineProbeReadiness.NotInstalled,
                Issue = AppleVirtualizationGuestAgentEngineProbeIssue.SocketMissing,
                SocketPath = new UnixSocketPath("/run/user/1000/docker.sock"),
                SocketExists = false,
                SocketAccessible = false,
            }));

        AppleVirtualizationGuestAgentEnvelope response = await guest.SendAsync(GuestEngineStatusRequest());

        response.EngineStatus.Should().NotBeNull();
        response.EngineStatus!.Ready.Should().BeFalse();
        response.EngineStatus.ObservationState.Should().Be(AppleVirtualizationEngineObservationState.NotInstalled);
        response.EngineStatus.Phase.Should().Be(ResourcePhase.Pending);
        response.EngineStatus.Endpoints.Should().BeEmpty();
        response.EngineStatus.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineProbe.NotInstalled");
    }

    [Fact]
    public async Task Fake_guest_engine_probe_maps_permission_denied_to_degraded()
    {
        var guest = new FakeAppleVirtualizationGuestAgentHarness().WithEngineProbe(
            new FakeAppleVirtualizationGuestAgentEngineProbe(new AppleVirtualizationGuestAgentEngineProbeObservation
            {
                Readiness = AppleVirtualizationGuestAgentEngineProbeReadiness.Degraded,
                Issue = AppleVirtualizationGuestAgentEngineProbeIssue.PermissionDenied,
                SocketPath = new UnixSocketPath("/run/user/1000/docker.sock"),
                SocketExists = true,
                SocketAccessible = false,
                StatusOutput = "permission denied while connecting to guest socket",
            }));

        AppleVirtualizationGuestAgentEnvelope response = await guest.SendAsync(GuestEngineStatusRequest());

        response.EngineStatus.Should().NotBeNull();
        response.EngineStatus!.ObservationState.Should().Be(AppleVirtualizationEngineObservationState.Degraded);
        response.EngineStatus.Phase.Should().Be(ResourcePhase.Degraded);
        response.EngineStatus.Ready.Should().BeFalse();
        response.EngineStatus.Endpoints.Should().ContainSingle(endpoint =>
            endpoint.SocketPath.HasValue &&
            endpoint.SocketPath.Value.Value == "/run/user/1000/docker.sock");
        response.EngineStatus.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineProbe.PermissionDenied");
    }

    [Fact]
    public async Task Fake_guest_engine_probe_maps_missing_systemd_to_degraded_with_bounded_status()
    {
        var guest = new FakeAppleVirtualizationGuestAgentHarness().WithEngineProbe(
            new FakeAppleVirtualizationGuestAgentEngineProbe(new AppleVirtualizationGuestAgentEngineProbeObservation
            {
                Readiness = AppleVirtualizationGuestAgentEngineProbeReadiness.Degraded,
                Issue = AppleVirtualizationGuestAgentEngineProbeIssue.SystemdMissing,
                SocketPath = new UnixSocketPath("/var/run/docker.sock"),
                SocketExists = true,
                SocketAccessible = true,
                SystemdAvailable = false,
                StatusOutput = new string('s', 1024),
            }));

        AppleVirtualizationGuestAgentEnvelope response = await guest.SendAsync(GuestEngineStatusRequest(
            authorityMode: EngineAuthorityMode.Rootful,
            maxStatusLength: 32));

        response.EngineStatus.Should().NotBeNull();
        response.EngineStatus!.ObservationState.Should().Be(AppleVirtualizationEngineObservationState.Degraded);
        response.EngineStatus.Status.Should().HaveLength(32);
        response.EngineStatus.AuthorityMode.Should().Be(EngineAuthorityMode.Rootful);
        response.EngineStatus.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineProbe.SystemdMissing");
    }

    [Fact]
    public async Task Fake_guest_engine_probe_maps_transport_error_to_unavailable_failure()
    {
        var guest = new FakeAppleVirtualizationGuestAgentHarness().WithEngineProbe(
            new FakeAppleVirtualizationGuestAgentEngineProbe(new AppleVirtualizationGuestAgentEngineProbeObservation
            {
                Readiness = AppleVirtualizationGuestAgentEngineProbeReadiness.Unavailable,
                Issue = AppleVirtualizationGuestAgentEngineProbeIssue.TransportError,
                StatusOutput = "vsock transport unavailable\nignored trailing output",
            }));

        AppleVirtualizationGuestAgentEnvelope response = await guest.SendAsync(GuestEngineStatusRequest());

        response.EngineStatus.Should().NotBeNull();
        response.EngineStatus!.Ready.Should().BeFalse();
        response.EngineStatus.ObservationState.Should().Be(AppleVirtualizationEngineObservationState.Failed);
        response.EngineStatus.EnginePhase.Should().Be(EngineControlPlanePhase.Failed);
        response.EngineStatus.Endpoints.Should().BeEmpty();
        response.EngineStatus.Status.Should().Be("vsock transport unavailable");
        response.EngineStatus.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineProbe.TransportError" &&
            diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Guest_engine_probe_candidates_are_guest_visible_paths_only()
    {
        AppleVirtualizationGuestAgentEngineProbeMapper.CandidateSocketPaths(GuestEngineStatusPayload())
            .Should().ContainSingle(candidate =>
                candidate.SocketPath.Value == "/run/user/1000/docker.sock" &&
                candidate.AuthorityMode == EngineAuthorityMode.Rootless &&
                candidate.GuestVisibleOnly);

        AppleVirtualizationGuestAgentEngineProbeMapper.CandidateSocketPaths(GuestEngineStatusPayload(authorityMode: EngineAuthorityMode.Rootful))
            .Should().Contain(candidate =>
                candidate.SocketPath.Value == "/var/run/docker.sock" &&
                candidate.AuthorityMode == EngineAuthorityMode.Rootful &&
                candidate.GuestVisibleOnly);

        AppleVirtualizationGuestAgentEngineProbeMapper.CandidateSocketPaths(GuestEngineStatusPayload(
                kind: EngineControlPlaneKind.Containerd,
                api: EngineApiKind.ContainerdApi,
                authorityMode: EngineAuthorityMode.Rootful))
            .Should().Contain(candidate =>
                candidate.SocketPath.Value == "/run/containerd/containerd.sock" &&
                candidate.Api == EngineApiKind.ContainerdApi &&
                candidate.GuestVisibleOnly);

        AppleVirtualizationGuestAgentEngineProbeMapper.CandidateSocketPaths(GuestEngineStatusPayload(
                kind: EngineControlPlaneKind.Podman,
                api: EngineApiKind.PodmanApi,
                authorityMode: EngineAuthorityMode.Rootless))
            .Should().ContainSingle(candidate =>
                candidate.SocketPath.Value == "/run/user/1000/podman/podman.sock" &&
                candidate.Api == EngineApiKind.PodmanApi &&
                candidate.AuthorityMode == EngineAuthorityMode.Rootless &&
                candidate.GuestVisibleOnly);

        AppleVirtualizationGuestAgentEngineProbeMapper.CandidateSocketPaths(GuestEngineStatusPayload(
                kind: EngineControlPlaneKind.Podman,
                api: EngineApiKind.PodmanApi,
                authorityMode: EngineAuthorityMode.Rootful))
            .Should().ContainSingle(candidate =>
                candidate.SocketPath.Value == "/run/podman/podman.sock" &&
                candidate.Api == EngineApiKind.PodmanApi &&
                candidate.AuthorityMode == EngineAuthorityMode.Rootful &&
                candidate.GuestVisibleOnly);

        AppleVirtualizationGuestAgentEngineProbeMapper.CandidateSocketPaths(GuestEngineStatusPayload(
                kind: EngineControlPlaneKind.BuildKit,
                api: EngineApiKind.BuildKitApi,
                authorityMode: EngineAuthorityMode.Rootless))
            .Should().ContainSingle(candidate =>
                candidate.SocketPath.Value == "/run/user/1000/buildkit-default/buildkitd.sock" &&
                candidate.Api == EngineApiKind.BuildKitApi &&
                candidate.AuthorityMode == EngineAuthorityMode.Rootless &&
                candidate.GuestVisibleOnly);

        AppleVirtualizationGuestAgentEngineProbeMapper.CandidateSocketPaths(GuestEngineStatusPayload(
                kind: EngineControlPlaneKind.BuildKit,
                api: EngineApiKind.BuildKitApi,
                authorityMode: EngineAuthorityMode.Rootful))
            .Should().ContainSingle(candidate =>
                candidate.SocketPath.Value == "/run/buildkit/buildkitd.sock" &&
                candidate.Api == EngineApiKind.BuildKitApi &&
                candidate.AuthorityMode == EngineAuthorityMode.Rootful &&
                candidate.GuestVisibleOnly);
    }

    [Fact]
    public void Engine_endpoint_is_sensitive_authority_bearing_not_published_endpoint()
    {
        AppleVirtualizationGuestAgentEngineStatus status =
            AppleVirtualizationGuestAgentEngineStatus.FromObservation(
                "host-1",
                "engine-1",
                AppleVirtualizationEngineObservationState.Ready,
                EngineControlPlaneKind.DockerCompatible,
                EngineApiKind.DockerCompatible,
                EngineAuthorityMode.Mixed,
                EngineImageStoreMode.ProviderManaged,
                EngineWorkloadAdoptionMode.None,
                maxEndpoints: 8,
                maxContainers: 32);

        status.Endpoints.Should().ContainSingle();
        AppleVirtualizationGuestAgentEngineApiEndpoint endpoint = status.Endpoints[0];
        endpoint.HpdPublished.Should().BeFalse();
        endpoint.GuestVisibleOnly.Should().BeTrue();
        endpoint.RequiresAuthorityBinding.Should().BeTrue();
        endpoint.SensitivePolicy.Kind.Should().Be(SensitiveEndpointKind.EngineSocket);
        endpoint.Transport.Should().Be(NetworkTransport.UnixStream);
    }

    [Theory]
    [InlineData(EngineAuthorityMode.Rootless, SensitiveAuthorityClass.RootlessEngineControl)]
    [InlineData(EngineAuthorityMode.Rootful, SensitiveAuthorityClass.RootfulEngineControl)]
    [InlineData(EngineAuthorityMode.Mixed, SensitiveAuthorityClass.RootlessEngineControl)]
    public void Engine_authority_mode_observations_are_represented_honestly(
        EngineAuthorityMode mode,
        SensitiveAuthorityClass expectedEndpointClass)
    {
        AppleVirtualizationGuestAgentEngineStatus status =
            AppleVirtualizationGuestAgentEngineStatus.FromObservation(
                "host-1",
                "engine-1",
                AppleVirtualizationEngineObservationState.Ready,
                EngineControlPlaneKind.DockerCompatible,
                EngineApiKind.DockerCompatible,
                mode,
                EngineImageStoreMode.ProviderManaged,
                EngineWorkloadAdoptionMode.None,
                maxEndpoints: 8,
                maxContainers: 32);

        status.AuthorityMode.Should().Be(mode);
        status.Endpoints[0].SensitivePolicy.AuthorityClass.Should().Be(expectedEndpointClass);
    }

    [Fact]
    public void Image_store_mode_does_not_claim_artifact_or_rootfs_ownership()
    {
        AppleVirtualizationGuestAgentEngineStatus status =
            AppleVirtualizationGuestAgentEngineStatus.FromObservation(
                "host-1",
                "engine-1",
                AppleVirtualizationEngineObservationState.Ready,
                EngineControlPlaneKind.Containerd,
                EngineApiKind.ContainerdApi,
                EngineAuthorityMode.Rootless,
                EngineImageStoreMode.EngineLocal,
                EngineWorkloadAdoptionMode.None,
                maxEndpoints: 8,
                maxContainers: 32);

        status.ImageStore.Should().Be(EngineImageStoreMode.EngineLocal);
        status.Endpoints[0].Api.Should().Be(EngineApiKind.ContainerdApi);
        status.Containers.Should().BeEmpty();
    }

    [Fact]
    public void Engine_operation_wire_name_is_explicit()
    {
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.EngineStatus)
            .Should().Be("engine.status");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.EngineProvision)
            .Should().Be("engine.provision");
    }

    private static Diagnostic Diagnostic(string code) =>
        new()
        {
            Severity = DiagnosticSeverity.Warning,
            Code = new DiagnosticCode(code),
            Message = code,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "engine.status",
        };

    private static AppleVirtualizationGuestAgentEnvelope GuestEngineStatusRequest(
        EngineControlPlaneKind kind = EngineControlPlaneKind.DockerCompatible,
        EngineApiKind api = EngineApiKind.DockerCompatible,
        EngineAuthorityMode authorityMode = EngineAuthorityMode.Rootless,
        bool includeContainers = false,
        int maxStatusLength = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxStatusLength) =>
        AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.EngineStatus,
            "guest-engine-probe",
            sequenceNumber: 1,
            AppleVirtualizationGuestAgentProtocol.EngineSchema) with
        {
            EngineStatusRequest = GuestEngineStatusPayload(kind, api, authorityMode, includeContainers, maxStatusLength),
        };

    private static AppleVirtualizationGuestAgentEngineStatusRequest GuestEngineStatusPayload(
        EngineControlPlaneKind kind = EngineControlPlaneKind.DockerCompatible,
        EngineApiKind api = EngineApiKind.DockerCompatible,
        EngineAuthorityMode authorityMode = EngineAuthorityMode.Rootless,
        bool includeContainers = false,
        int maxStatusLength = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxStatusLength) =>
        new()
        {
            HostId = "host-1",
            EngineId = "engine-1",
            Kind = kind,
            Api = api,
            AuthorityMode = authorityMode,
            IncludeContainers = includeContainers,
            MaxStatusLength = maxStatusLength,
        };
}
