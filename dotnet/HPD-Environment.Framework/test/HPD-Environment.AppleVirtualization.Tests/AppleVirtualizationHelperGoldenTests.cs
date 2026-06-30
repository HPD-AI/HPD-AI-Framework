namespace HPD.Environment.AppleVirtualization.Tests;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationHelperGoldenTests
{
    [Fact]
    public async Task DotNet_generated_hello_request_is_consumed_by_swift_and_response_is_consumed_by_dotnet()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.Hello,
            "golden-hello",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.HelloRequestSchema) with
        {
            HelloRequest = new AppleVirtualizationHelperHelloRequest
            {
                ClientName = "HPD golden test",
                MinimumProtocolVersion = AppleVirtualizationHelperProtocol.CurrentVersion,
                RequestedProtocolVersion = AppleVirtualizationHelperProtocol.CurrentVersion,
            },
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.MessageType.Should().Be(AppleVirtualizationHelperMessageType.Response);
        response.Operation.Should().Be(AppleVirtualizationHelperOperation.Hello);
        response.RequestId.Should().Be("golden-hello");
        response.CausationId.Should().Be("golden-hello");
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.HelloResponseSchema);
        response.HelloResponse.Should().NotBeNull();
        response.HelloResponse!.HelperName.Should().Be("hpd-vz");
        response.HelloResponse.ProtocolVersion.Should().Be(AppleVirtualizationHelperProtocol.CurrentVersion);
        response.HelloResponse.ProtocolCompatible.Should().BeTrue();
        response.HelloResponse.ProviderGeneration.Should().Be(1);
        response.HelloResponse.VirtualizationFrameworkAvailable.Should().BeTrue();
        response.HelloResponse.PreflightFacts.Should().Contain(fact => fact.Name == "helper-protocol-compatibility");
        response.HelloResponse.PreflightFacts.Should().Contain(fact =>
            fact.Name == "vm-boot-inputs" &&
            fact.State == AppleVirtualizationPreflightFactState.RequiresConfiguration &&
            fact.Reason == "BootInputsMissing");
    }

    [Fact]
    public async Task DotNet_generated_health_request_is_consumed_by_swift_and_response_is_consumed_by_dotnet()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.HealthProbe,
            "golden-health",
            sequenceNumber: 2,
            AppleVirtualizationHelperProtocol.HealthResponseSchema) with
        {
            HealthProbeRequest = new AppleVirtualizationHealthProbeRequest(IncludeGuestControl: true),
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.MessageType.Should().Be(AppleVirtualizationHelperMessageType.Response);
        response.Operation.Should().Be(AppleVirtualizationHelperOperation.HealthProbe);
        response.RequestId.Should().Be("golden-health");
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.HealthResponseSchema);
        response.HealthProbeResponse.Should().NotBeNull();
        response.HealthProbeResponse!.Ready.Should().BeTrue();
        response.HealthProbeResponse.Detail.Should().Contain("protocol loop is ready");
        response.HealthProbeResponse.Detail.Should().Contain("not HPD guest readiness");
    }

    [Fact]
    public async Task DotNet_generated_preflight_request_receives_swift_stable_facts()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.PreflightRun,
            "golden-preflight",
            sequenceNumber: 21,
            AppleVirtualizationHelperProtocol.PreflightResponseSchema) with
        {
            PreflightRunRequest = new AppleVirtualizationPreflightRunRequest(),
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.MessageType.Should().Be(AppleVirtualizationHelperMessageType.Response);
        response.Operation.Should().Be(AppleVirtualizationHelperOperation.PreflightRun);
        response.RequestId.Should().Be("golden-preflight");
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.PreflightResponseSchema);
        response.PreflightRunResponse.Should().NotBeNull();
        response.PreflightRunResponse!.Facts.Select(fact => fact.Name).Should().Contain(
            "helper-protocol-compatibility",
            "host-os",
            "host-architecture",
            "virtualization-framework",
            "vzvirtualmachine-supported",
            "virtualization-entitlement",
            "vm-boot-inputs",
            "guest-agent-provisioning",
            "helper-health-not-guest-readiness");
        response.PreflightRunResponse.Facts.Should().Contain(fact =>
            fact.Name == "vm-boot-inputs" &&
            fact.State == AppleVirtualizationPreflightFactState.RequiresConfiguration &&
            fact.Reason == "BootInputsMissing");
        response.PreflightRunResponse.Facts.Should().Contain(fact =>
            fact.Name == "helper-health-not-guest-readiness" &&
            fact.State == AppleVirtualizationPreflightFactState.Supported);
    }

    [Fact]
    public async Task DotNet_generated_authority_revoke_receives_versioned_swift_revocation_evidence()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(AuthorityRevokeEnvelope());

        response.MessageType.Should().Be(AppleVirtualizationHelperMessageType.Response);
        response.Operation.Should().Be(AppleVirtualizationHelperOperation.AuthorityRevoke);
        response.RequestId.Should().Be("golden-authority-revoke");
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.AuthorityBindingResponseSchema);
        response.AuthorityBindingResponse.Should().NotBeNull();
        response.AuthorityBindingResponse!.BindingId.Should().Be("authority-golden");
        response.AuthorityBindingResponse.BindingPhase.Should().Be(AuthorityBindingPhase.Revoking);
        response.AuthorityBindingResponse.RevocationStatus.Should().Be(RevocationVerificationStatus.NotSupported);
        response.AuthorityBindingResponse.RevocationEvidence.Should().ContainSingle();
        AppleVirtualizationAuthorityRevocationEvidence evidence = response.AuthorityBindingResponse.RevocationEvidence.Single();
        evidence.EvidenceProtocolVersion.Should().Be("v1");
        evidence.Kind.Should().Be(AppleVirtualizationAuthorityRevocationEvidenceKind.Unsupported);
        evidence.Observed.Should().BeTrue();
        evidence.GuestSocketPath.Should().Be(new UnixSocketPath("/run/hpd/engine/docker.sock"));
        evidence.Detail.Should().Contain("did not have observable");
        response.AuthorityBindingResponse.AuditEventsTruncated.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, AppleVirtualizationAuthorityRevocationEvidenceKind.ConnectionFileDescriptorClosed, RevocationVerificationStatus.Verified)]
    [InlineData(42, AppleVirtualizationAuthorityRevocationEvidenceKind.ConnectionFileDescriptorOpen, RevocationVerificationStatus.Failed)]
    public async Task DotNet_generated_authority_revoke_receives_swift_file_descriptor_evidence(
        int fileDescriptor,
        AppleVirtualizationAuthorityRevocationEvidenceKind expectedKind,
        RevocationVerificationStatus expectedStatus)
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(AuthorityRevokeEnvelope(
            observedFileDescriptor: fileDescriptor));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.AuthorityBindingResponse.Should().NotBeNull();
        response.AuthorityBindingResponse!.RevocationStatus.Should().Be(expectedStatus);
        AppleVirtualizationAuthorityRevocationEvidence evidence = response.AuthorityBindingResponse.RevocationEvidence.Should().ContainSingle().Subject;
        evidence.Kind.Should().Be(expectedKind);
        evidence.Observed.Should().BeTrue();
        evidence.FileDescriptor.Should().Be(fileDescriptor);
        evidence.GuestSocketPath.Should().Be(new UnixSocketPath("/run/hpd/engine/docker.sock"));
    }

    [Theory]
    [InlineData(false, AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketAbsent, RevocationVerificationStatus.Verified)]
    [InlineData(true, AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketPresent, RevocationVerificationStatus.Failed)]
    public async Task DotNet_generated_authority_revoke_receives_swift_guest_socket_evidence(
        bool guestSocketPresent,
        AppleVirtualizationAuthorityRevocationEvidenceKind expectedKind,
        RevocationVerificationStatus expectedStatus)
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(AuthorityRevokeEnvelope(
            guestSocketPresent: guestSocketPresent));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.AuthorityBindingResponse.Should().NotBeNull();
        response.AuthorityBindingResponse!.RevocationStatus.Should().Be(expectedStatus);
        AppleVirtualizationAuthorityRevocationEvidence evidence = response.AuthorityBindingResponse.RevocationEvidence.Should().ContainSingle().Subject;
        evidence.Kind.Should().Be(expectedKind);
        evidence.Observed.Should().BeTrue();
        evidence.GuestSocketPath.Should().Be(new UnixSocketPath("/run/hpd/engine/docker.sock"));
    }

    [Fact]
    public async Task Real_authority_status_for_missing_vm_returns_structured_error_without_fallback_success()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync(fake: false);

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(AuthorityStatusEnvelope());

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.Operation.Should().Be(AppleVirtualizationHelperOperation.AuthorityStatus);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("AppleVirtualization.AuthorityBindingGuestAgentFailed");
        response.Error.FailedPhase.Should().Be("GuestAuthority");
        response.AuthorityBindingResponse.Should().BeNull();
    }

    [Theory]
    [InlineData(AppleVirtualizationGuestAgentTransportState.Connected)]
    [InlineData(AppleVirtualizationGuestAgentTransportState.Refused)]
    [InlineData(AppleVirtualizationGuestAgentTransportState.Timeout)]
    [InlineData(AppleVirtualizationGuestAgentTransportState.Unsupported)]
    [InlineData(AppleVirtualizationGuestAgentTransportState.Failed)]
    public async Task DotNet_generated_guest_agent_transport_probe_receives_scripted_fake_swift_status(
        AppleVirtualizationGuestAgentTransportState scriptedStatus)
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(GuestTransportEnvelope(
            "golden-transport-" + scriptedStatus,
            "host-transport",
            scriptedStatus));

        response.Operation.Should().Be(AppleVirtualizationHelperOperation.GuestAgentTransportProbe);
        response.RequestId.Should().Be("golden-transport-" + scriptedStatus);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.GuestAgentTransportResponseSchema);
        response.GuestAgentTransportProbeResponse.Should().NotBeNull();
        response.GuestAgentTransportProbeResponse!.State.Should().Be(scriptedStatus);
        response.GuestAgentTransportProbeResponse.GuestReady.Should().BeFalse();
        response.GuestAgentTransportProbeResponse.Connected.Should().Be(scriptedStatus == AppleVirtualizationGuestAgentTransportState.Connected);
    }

    [Fact]
    public async Task Real_guest_agent_transport_probe_for_missing_host_waits_for_vm_running_without_claiming_ready()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync(fake: false);

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(GuestTransportEnvelope(
            "golden-transport-real-missing",
            "host-real-missing",
            scriptedStatus: null));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.GuestAgentTransportResponseSchema);
        response.GuestAgentTransportProbeResponse.Should().NotBeNull();
        response.GuestAgentTransportProbeResponse!.State.Should().Be(AppleVirtualizationGuestAgentTransportState.WaitingForVmRunning);
        response.GuestAgentTransportProbeResponse.VmRunning.Should().BeFalse();
        response.GuestAgentTransportProbeResponse.Connected.Should().BeFalse();
        response.GuestAgentTransportProbeResponse.GuestReady.Should().BeFalse();
    }

    [Fact]
    public async Task Real_guest_agent_transport_probe_without_explicit_real_mode_is_not_attempted()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync(fake: false);

        AppleVirtualizationHelperEnvelope request = GuestTransportEnvelope(
            "golden-transport-real-not-explicit",
            "host-real-not-explicit",
            scriptedStatus: null) with
        {
            GuestAgentTransportProbeRequest = GuestTransportEnvelope(
                "golden-transport-real-not-explicit",
                "host-real-not-explicit",
                scriptedStatus: null).GuestAgentTransportProbeRequest! with
            {
                ExplicitRealMode = false,
            },
        };
        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.GuestAgentTransportProbeResponse.Should().NotBeNull();
        response.GuestAgentTransportProbeResponse!.State.Should().Be(AppleVirtualizationGuestAgentTransportState.NotAttempted);
        response.GuestAgentTransportProbeResponse.Connected.Should().BeFalse();
        response.GuestAgentTransportProbeResponse.GuestReady.Should().BeFalse();
        response.GuestAgentTransportProbeResponse.Error.Should().NotBeNull();
        response.GuestAgentTransportProbeResponse.Error!.Code.Should().Be("AppleVirtualization.RealModeExplicitEnablementRequired");
    }

    [Theory]
    [InlineData(AppleVirtualizationGuestAgentReadinessState.Ready, AppleVirtualizationHelperResponseStatus.Ok)]
    [InlineData(AppleVirtualizationGuestAgentReadinessState.NotReady, AppleVirtualizationHelperResponseStatus.Ok)]
    [InlineData(AppleVirtualizationGuestAgentReadinessState.IncompatibleProtocol, AppleVirtualizationHelperResponseStatus.Ok)]
    [InlineData(AppleVirtualizationGuestAgentReadinessState.IncompatibleAgentVersion, AppleVirtualizationHelperResponseStatus.Ok)]
    [InlineData(AppleVirtualizationGuestAgentReadinessState.MissingCapability, AppleVirtualizationHelperResponseStatus.Ok)]
    [InlineData(AppleVirtualizationGuestAgentReadinessState.MalformedFrame, AppleVirtualizationHelperResponseStatus.Error)]
    [InlineData(AppleVirtualizationGuestAgentReadinessState.GuestAgentError, AppleVirtualizationHelperResponseStatus.Error)]
    [InlineData(AppleVirtualizationGuestAgentReadinessState.Disconnected, AppleVirtualizationHelperResponseStatus.Error)]
    public async Task DotNet_generated_guest_agent_readiness_probe_receives_scripted_fake_swift_status(
        AppleVirtualizationGuestAgentReadinessState scriptedState,
        AppleVirtualizationHelperResponseStatus expectedStatus)
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(GuestReadinessEnvelope(
            "golden-readiness-" + scriptedState,
            "host-readiness",
            scriptedState));

        response.Operation.Should().Be(AppleVirtualizationHelperOperation.GuestAgentReadinessProbe);
        response.RequestId.Should().Be("golden-readiness-" + scriptedState);
        response.ResponseStatus.Should().Be(expectedStatus);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.GuestAgentReadinessResponseSchema);
        response.GuestAgentReadinessProbeResponse.Should().NotBeNull();
        response.GuestAgentReadinessProbeResponse!.State.Should().Be(
            scriptedState == AppleVirtualizationGuestAgentReadinessState.MissingCapability
                ? AppleVirtualizationGuestAgentReadinessState.MissingCapability
                : scriptedState);
        response.GuestAgentReadinessProbeResponse.TransportConnected.Should().Be(scriptedState != AppleVirtualizationGuestAgentReadinessState.TransportNotConnected);
        response.GuestAgentReadinessProbeResponse.VerifiedReady.Should().Be(scriptedState == AppleVirtualizationGuestAgentReadinessState.Ready);

        if (scriptedState == AppleVirtualizationGuestAgentReadinessState.Ready)
        {
            response.GuestAgentReadinessProbeResponse.GuestBootId.Should().Be("guest-boot-1");
            response.GuestAgentReadinessProbeResponse.GuestBootGeneration.Should().Be(1);
            response.GuestAgentReadinessProbeResponse.GuestAgentGeneration.Should().Be(1);
            response.GuestAgentReadinessProbeResponse.AgentVersion.Should().Be("0.1.0-test");
            response.GuestAgentReadinessProbeResponse.ProtocolVersion.Should().Be("1.0");
            response.GuestAgentReadinessProbeResponse.Capabilities.Should().NotBeNull();
        }
        else
        {
            response.GuestAgentReadinessProbeResponse.VerifiedReady.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Guest_agent_readiness_transport_connected_alone_is_not_ready()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(GuestReadinessEnvelope(
            "golden-readiness-handshaking",
            "host-readiness",
            AppleVirtualizationGuestAgentReadinessState.Handshaking));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.GuestAgentReadinessProbeResponse.Should().NotBeNull();
        response.GuestAgentReadinessProbeResponse!.TransportState.Should().Be(AppleVirtualizationGuestAgentTransportState.Connected);
        response.GuestAgentReadinessProbeResponse.TransportConnected.Should().BeTrue();
        response.GuestAgentReadinessProbeResponse.VerifiedReady.Should().BeFalse();
        response.GuestAgentReadinessProbeResponse.State.Should().Be(AppleVirtualizationGuestAgentReadinessState.Handshaking);
    }

    [Fact]
    public async Task DotNet_generated_network_status_request_receives_swift_fake_nat_shape()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(NetworkStatusEnvelope(
            "golden-network-nat",
            AppleVirtualizationNetworkAttachmentKind.Nat));

        response.Operation.Should().Be(AppleVirtualizationHelperOperation.NetworkStatus);
        response.RequestId.Should().Be("golden-network-nat");
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.NetworkStatusResponseSchema);
        response.NetworkStatusResponse.Should().NotBeNull();
        response.NetworkStatusResponse!.State.Should().Be(AppleVirtualizationNetworkObservationState.Ready);
        response.NetworkStatusResponse.DefaultAttachment.Should().Be(AppleVirtualizationNetworkAttachmentKind.Nat);
        response.NetworkStatusResponse.RealizedCapabilities.Should().HaveFlag(NetworkCapabilitySet.IPv4);
        response.NetworkStatusResponse.RealizedCapabilities.Should().HaveFlag(NetworkCapabilitySet.NatEgress);
        response.NetworkStatusResponse.GuestNetworkStatus.Should().NotBeNull();
        response.NetworkStatusResponse.GuestNetworkStatus!.Interfaces.Should().ContainSingle(networkInterface => networkInterface.Name == "en0");
    }

    [Theory]
    [InlineData(AppleVirtualizationNetworkAttachmentKind.Bridged, AppleVirtualizationNetworkObservationState.RequiresPermission)]
    [InlineData(AppleVirtualizationNetworkAttachmentKind.Vmnet, AppleVirtualizationNetworkObservationState.RequiresConfiguration)]
    [InlineData(AppleVirtualizationNetworkAttachmentKind.FileHandle, AppleVirtualizationNetworkObservationState.RequiresConfiguration)]
    public async Task Swift_fake_network_status_reports_non_default_attachments_as_config_or_permission_required(
        AppleVirtualizationNetworkAttachmentKind requested,
        AppleVirtualizationNetworkObservationState expectedState)
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(NetworkStatusEnvelope(
            "golden-network-" + requested,
            requested));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.NetworkStatusResponse.Should().NotBeNull();
        response.NetworkStatusResponse!.RequestedAttachment.Should().Be(requested);
        response.NetworkStatusResponse.State.Should().Be(expectedState);
        response.NetworkStatusResponse.AttachmentCapabilities.Should().Contain(fact =>
            fact.AttachmentKind == requested &&
            (fact.State == CapabilityState.RequiresPermission ||
             fact.State == CapabilityState.RequiresConfiguration ||
             fact.State == CapabilityState.Unsupported));
    }

    [Fact]
    public async Task Swift_fake_virtio_socket_observation_does_not_claim_tcp_or_udp_publication()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(NetworkStatusEnvelope(
            "golden-network-vsock",
            AppleVirtualizationNetworkAttachmentKind.VirtioSocket));

        response.NetworkStatusResponse.Should().NotBeNull();
        AppleVirtualizationNetworkAttachmentCapabilityFact virtioSocket = response.NetworkStatusResponse!.AttachmentCapabilities
            .Single(fact => fact.AttachmentKind == AppleVirtualizationNetworkAttachmentKind.VirtioSocket);
        virtioSocket.Capabilities.Should().NotHaveFlag(NetworkCapabilitySet.TcpPublish);
        virtioSocket.Capabilities.Should().NotHaveFlag(NetworkCapabilitySet.UdpPublish);
        response.NetworkStatusResponse.GuestNetworkStatus!.Listeners.Should().Contain(listener =>
            listener.GuestVisibleOnly &&
            !listener.HpdPublished);
    }

    [Fact]
    public async Task DotNet_generated_host_start_request_receives_swift_structured_unsupported_error_without_booting()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.HostStart,
            "golden-host-start",
            sequenceNumber: 3,
            AppleVirtualizationHelperProtocol.HostRequestSchema) with
        {
            HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
            {
                HostId = "host-golden",
                Reason = "golden smoke",
            },
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.Operation.Should().Be(AppleVirtualizationHelperOperation.HostStart);
        response.RequestId.Should().Be("golden-host-start");
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.HostResponseSchema);
        response.HostStatusResponse.Should().NotBeNull();
        response.HostStatusResponse!.HostId.Should().Be("host-golden");
        response.HostStatusResponse.HostPhase.Should().Be(RuntimeHostPhase.Failed);
        response.HostStatusResponse.GuestControlReachable.Should().BeFalse();
        response.HostStatusResponse.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.HelperOperationNotImplemented");
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("AppleVirtualization.HelperOperationNotImplemented");
        response.Error.Operation.Should().Be("host.start");
        response.Error.Retryable.Should().BeFalse();
        response.Error.FailedPhase.Should().Be("HostLifecycle");
        response.Error.Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Real_host_start_without_explicit_real_mode_is_rejected_before_vm_creation()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync(fake: false);

        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.HostStart,
            "golden-host-start-not-explicit",
            sequenceNumber: 33,
            AppleVirtualizationHelperProtocol.HostRequestSchema) with
        {
            HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
            {
                HostId = "host-not-explicit",
                ExplicitRealMode = false,
                Reason = "verify real-mode gate",
            },
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.HostResponseSchema);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("AppleVirtualization.RealModeExplicitEnablementRequired");
        response.Error.Operation.Should().Be("host.start");
        response.HostStatusResponse.Should().NotBeNull();
        response.HostStatusResponse!.HostPhase.Should().Be(RuntimeHostPhase.Failed);
        response.HostStatusResponse.GuestControlReachable.Should().BeFalse();
    }

    [Fact]
    public async Task Real_host_start_with_failed_preconditions_returns_structured_failure_without_vm_creation()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync(fake: false);

        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.HostStart,
            "golden-host-start-bad-preconditions",
            sequenceNumber: 34,
            AppleVirtualizationHelperProtocol.HostRequestSchema) with
        {
            HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
            {
                HostId = "host-bad-preconditions",
                ExplicitRealMode = true,
                VmConfigurationValidationRequest = new AppleVirtualizationVmConfigurationValidationRequest
                {
                    HostId = "host-bad-preconditions",
                    CpuCount = 0,
                    MemorySizeBytes = 0,
                    GuestImage = new AppleVirtualizationGuestImageOptions(),
                    IncludeSerialConsole = true,
                },
                Reason = "verify structural preconditions",
            },
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.Error.Should().NotBeNull();
        response.Error!.FailedPhase.Should().Be("HostLifecycle");
        response.HostStatusResponse.Should().NotBeNull();
        response.HostStatusResponse!.HostId.Should().Be("host-bad-preconditions");
        response.HostStatusResponse.HostPhase.Should().Be(RuntimeHostPhase.Failed);
        response.HostStatusResponse.GuestControlReachable.Should().BeFalse();
        response.HostStatusResponse.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.VmConfigurationCpuCountInvalid");
        response.HostStatusResponse.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.VmConfigurationBootInputMissing");
    }

    [Fact]
    public async Task Real_host_status_and_stop_for_missing_host_are_deterministic_without_vm_creation()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync(fake: false);

        AppleVirtualizationHelperEnvelope status = await helper.SendAsync(HostLifecycleEnvelope(
            AppleVirtualizationHelperOperation.HostStatus,
            "golden-host-status-missing",
            "host-missing"));
        AppleVirtualizationHelperEnvelope stop = await helper.SendAsync(HostLifecycleEnvelope(
            AppleVirtualizationHelperOperation.HostStop,
            "golden-host-stop-missing",
            "host-missing"));

        status.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        status.HostStatusResponse.Should().NotBeNull();
        status.HostStatusResponse!.HostPhase.Should().Be(RuntimeHostPhase.Stopped);
        status.HostStatusResponse.Phase.Should().Be(ResourcePhase.Pending);
        status.HostStatusResponse.GuestControlReachable.Should().BeFalse();

        stop.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        stop.HostStatusResponse.Should().NotBeNull();
        stop.HostStatusResponse!.HostPhase.Should().Be(RuntimeHostPhase.Stopped);
        stop.HostStatusResponse.GuestControlReachable.Should().BeFalse();
    }

    [Fact]
    public async Task DotNet_generated_process_start_receives_swift_fake_running_status_after_guest_and_projection_verified()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ProcessStartEnvelope(
            "golden-process-start",
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified));

        response.Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessStart);
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.ProcessResponseSchema);
        response.ProcessStatusResponse.Should().NotBeNull();
        response.ProcessStatusResponse!.ProcessId.Should().Be("process-golden");
        response.ProcessStatusResponse.ProcessPhase.Should().Be(ProcessInvocationPhase.Running);
        response.ProcessStatusResponse.IoState.Should().Be(ProcessIoState.Open);
        response.ProcessStatusResponse.ProviderProcessId.Should().Be("guest-process-golden");
    }

    [Fact]
    public async Task Process_start_before_guest_ready_returns_structured_not_ready()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ProcessStartEnvelope(
            "golden-process-not-ready",
            AppleVirtualizationGuestAgentReadinessState.TransportNotConnected,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified));

        response.Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessStart);
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.ProcessStatusResponse.Should().NotBeNull();
        response.ProcessStatusResponse!.ProcessPhase.Should().Be(ProcessInvocationPhase.Failed);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("AppleVirtualization.GuestAgentNotReady");
        response.Error.Operation.Should().Be("process.start");
        response.Error.FailedPhase.Should().Be("GuestProcess");
    }

    [Fact]
    public async Task Process_start_with_unverified_projected_workdir_returns_structured_projection_not_ready()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ProcessStartEnvelope(
            "golden-process-projection-not-ready",
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.FrameworkAcceptedOnly));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.ProcessStatusResponse.Should().NotBeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("AppleVirtualization.ProcessProjectionNotReady");
        response.Error.Operation.Should().Be("process.start");
    }

    [Fact]
    public async Task Process_output_bridge_preserves_stdout_bytes_and_sequence()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ProcessLifecycleEnvelope(
            AppleVirtualizationHelperOperation.ProcessReadOutput,
            "golden-process-output",
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified));

        response.Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessReadOutput);
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.ProcessOutputEventSchema);
        response.ProcessOutputEvent.Should().NotBeNull();
        response.ProcessOutputEvent!.ProcessId.Should().Be("process-golden");
        response.ProcessOutputEvent.Stream.Should().Be(ProcessOutputStream.Stdout);
        response.ProcessOutputEvent.Sequence.Should().Be(8);
        response.ProcessOutputEvent.Bytes.ToArray().Should().Equal(0x48, 0x50, 0x44, 0x2d, 0x0a);
        response.ProcessOutputEvent.Flags.Should().HaveFlag(ProcessOutputChunkFlags.Final);
    }

    [Theory]
    [InlineData(AppleVirtualizationHelperOperation.ProcessSignal)]
    [InlineData(AppleVirtualizationHelperOperation.ProcessStop)]
    [InlineData(AppleVirtualizationHelperOperation.ProcessStdin)]
    [InlineData(AppleVirtualizationHelperOperation.ProcessCloseStdin)]
    [InlineData(AppleVirtualizationHelperOperation.ProcessWait)]
    public async Task DotNet_generated_process_control_operations_receive_swift_structured_results(
        AppleVirtualizationHelperOperation operation)
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ProcessLifecycleEnvelope(
            operation,
            "golden-" + AppleVirtualizationHelperOperationNames.ToWireName(operation),
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified));

        response.Operation.Should().Be(operation);
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.ProcessStatusResponse.Should().NotBeNull();
        response.ProcessStatusResponse!.ProcessId.Should().Be("process-golden");

        if (operation == AppleVirtualizationHelperOperation.ProcessWait)
        {
            response.ProcessStatusResponse.ProcessPhase.Should().Be(ProcessInvocationPhase.Exited);
            response.ProcessStatusResponse.Result.Should().NotBeNull();
            response.ProcessStatusResponse.Result!.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
            response.ProcessStatusResponse.Result.ExitCode.Should().Be(0);
        }
    }

    [Fact]
    public async Task DotNet_generated_process_resize_request_receives_swift_structured_unsupported_error()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();
        var processHandle = new ProviderOpaqueHandle(
            AppleVirtualizationProviderDescriptor.ProviderId,
            "process-invocation:golden:process-golden:g1:h1",
            new SchemaId("hpd.execution.apple-virtualization.handle.process-invocation.v1"),
            Generation: 1);

        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProcessResize,
            "golden-process-resize",
            sequenceNumber: 31,
            AppleVirtualizationHelperProtocol.ProcessRequestSchema) with
        {
            ResourceKind = new ResourceKind("process-invocation"),
            ResourceId = "process-golden",
            ResourceScope = new ResourceScope("golden"),
            ProviderHandle = processHandle,
            ProcessResizeRequest = new AppleVirtualizationProcessResizeRequest
            {
                ProcessId = "process-golden",
                ProcessHandle = processHandle,
                Size = new TerminalSpec(120, 40),
            },
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessResize);
        response.RequestId.Should().Be("golden-process-resize");
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.ErrorSchema);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("AppleVirtualization.HelperOperationNotImplemented");
        response.Error.Operation.Should().Be("process.resize");
        response.Error.Retryable.Should().BeFalse();
        response.Error.FailedPhase.Should().Be("HelperSkeleton");
    }

    [Fact]
    public async Task Protocol_mismatch_frame_receives_swift_structured_mismatch_error()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.Hello,
            "golden-mismatch",
            sequenceNumber: 4,
            AppleVirtualizationHelperProtocol.HelloRequestSchema) with
        {
            ProtocolVersion = "9.9",
            HelloRequest = new AppleVirtualizationHelperHelloRequest
            {
                ClientName = "HPD golden test",
                MinimumProtocolVersion = "9.9",
                RequestedProtocolVersion = "9.9",
            },
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.Operation.Should().Be(AppleVirtualizationHelperOperation.Hello);
        response.RequestId.Should().Be("golden-mismatch");
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("AppleVirtualization.HelperProtocolMismatch");
        response.Error.Operation.Should().Be("hello");
        response.Error.Retryable.Should().BeFalse();
        response.Error.FailedPhase.Should().Be("Activation");
    }

    [Theory]
    [InlineData(AppleVirtualizationHelperOperation.ProjectionMount)]
    [InlineData(AppleVirtualizationHelperOperation.ProjectionStatus)]
    [InlineData(AppleVirtualizationHelperOperation.ProjectionUnmount)]
    [InlineData(AppleVirtualizationHelperOperation.ProjectionObserve)]
    public async Task DotNet_generated_projection_lifecycle_request_receives_guest_verified_fake_swift_result(
        AppleVirtualizationHelperOperation operation)
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ProjectionEnvelope(
            operation,
            "golden-" + AppleVirtualizationHelperOperationNames.ToWireName(operation),
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified));

        response.Operation.Should().Be(operation);
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.ProjectionResponseSchema);
        response.ProjectionStatusResponse.Should().NotBeNull();
        response.ProjectionStatusResponse!.GuestAgentReady.Should().BeTrue();
        response.ProjectionStatusResponse.HostShareConfigured.Should().BeTrue();
        response.ProjectionStatusResponse.FrameworkShareAccepted.Should().BeTrue();
        response.ProjectionStatusResponse.VerifiedByGuestAgent.Should().BeTrue();
        response.ProjectionStatusResponse.ReadyForHpdUse.Should().BeTrue();
        response.ProjectionStatusResponse.GuestProjectionStatus.Should().NotBeNull();
        response.ProjectionStatusResponse.GuestProjectionStatus!.ReadyForHpdUse.Should().BeTrue();

        if (operation == AppleVirtualizationHelperOperation.ProjectionUnmount)
        {
            response.ProjectionStatusResponse.GuestProjectionUnmountResult.Should().NotBeNull();
            response.ProjectionStatusResponse.GuestProjectionUnmountResult!.Unmounted.Should().BeTrue();
        }

        if (operation == AppleVirtualizationHelperOperation.ProjectionObserve)
        {
            response.ProjectionStatusResponse.GuestProjectionObserveResult.Should().NotBeNull();
            response.ProjectionStatusResponse.GuestProjectionObserveResult!.HasMore.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Projection_lifecycle_request_before_guest_ready_returns_structured_not_ready()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ProjectionEnvelope(
            AppleVirtualizationHelperOperation.ProjectionMount,
            "golden-projection-not-ready",
            AppleVirtualizationGuestAgentReadinessState.TransportNotConnected,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.ProjectionStatusResponse.Should().NotBeNull();
        response.ProjectionStatusResponse!.GuestAgentReady.Should().BeFalse();
        response.ProjectionStatusResponse.VerifiedByGuestAgent.Should().BeFalse();
        response.ProjectionStatusResponse.ProjectionPhase.Should().NotBe(ContentProjectionPhase.Projected);
        response.ProjectionStatusResponse.ReadyForHpdUse.Should().BeFalse();
        response.ProjectionStatusResponse.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.GuestAgentNotReady");
    }

    [Fact]
    public async Task Host_framework_share_configured_alone_is_not_projected_without_guest_verification()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ProjectionEnvelope(
            AppleVirtualizationHelperOperation.ProjectionStatus,
            "golden-projection-configured-only",
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.HostConfiguredOnly));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.ProjectionStatusResponse.Should().NotBeNull();
        response.ProjectionStatusResponse!.GuestAgentReady.Should().BeTrue();
        response.ProjectionStatusResponse.HostShareConfigured.Should().BeTrue();
        response.ProjectionStatusResponse.FrameworkShareAccepted.Should().BeFalse();
        response.ProjectionStatusResponse.VerifiedByGuestAgent.Should().BeFalse();
        response.ProjectionStatusResponse.ProjectionPhase.Should().NotBe(ContentProjectionPhase.Projected);
        response.ProjectionStatusResponse.GuestProjectionStatus!.VerificationState
            .Should().Be(AppleVirtualizationGuestAgentProjectionVerificationState.HostShareConfigured);
    }

    [Fact]
    public async Task Malformed_guest_projection_response_is_structured()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ProjectionEnvelope(
            AppleVirtualizationHelperOperation.ProjectionMount,
            "golden-projection-malformed",
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.MalformedResponse));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("AppleVirtualization.GuestProjectionMalformedResponse");
        response.Error.Operation.Should().Be("projection.mount");
        response.ProjectionStatusResponse.Should().NotBeNull();
        response.ProjectionStatusResponse!.VerifiedByGuestAgent.Should().BeFalse();
    }

    [Theory]
    [InlineData(AppleVirtualizationHelperOperation.ProjectionSync)]
    [InlineData(AppleVirtualizationHelperOperation.ProjectionFinalize)]
    public async Task DotNet_generated_projection_sync_and_finalization_requests_receive_swift_fake_results(
        AppleVirtualizationHelperOperation operation)
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ProjectionContentEnvelope(
            operation,
            "golden-" + AppleVirtualizationHelperOperationNames.ToWireName(operation),
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            projectionGeneration: 1));

        response.Operation.Should().Be(operation);
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.Error.Should().BeNull();

        if (operation == AppleVirtualizationHelperOperation.ProjectionSync)
        {
            response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.ProjectionSyncResponseSchema);
            response.ProjectionSyncResult.Should().NotBeNull();
            response.ProjectionSyncResult!.Succeeded.Should().BeTrue();
            response.ProjectionSyncResult.State.Should().Be(AppleVirtualizationGuestAgentProjectionSyncState.Succeeded);
            response.ProjectionSyncResult.ChangeSummary.Created.Should().Be(1);
            response.ProjectionSyncResult.Changes.Should().HaveCount(2);
            response.ProjectionSyncResult.ChangesTruncated.Should().BeTrue();
            response.ProjectionSyncResult.Conflicts.Should().ContainSingle();
            response.ProjectionSyncResult.ConflictsTruncated.Should().BeTrue();
        }
        else
        {
            response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.ProjectionFinalizationResponseSchema);
            response.ProjectionFinalizationResult.Should().NotBeNull();
            response.ProjectionFinalizationResult!.Succeeded.Should().BeTrue();
            response.ProjectionFinalizationResult.State.Should().Be(AppleVirtualizationGuestAgentProjectionFinalizationState.Succeeded);
            response.ProjectionFinalizationResult.ManifestDigest!.Value.Value.Should().Be("fake-helper-manifest");
            response.ProjectionFinalizationResult.Content.Should().HaveCount(2);
            response.ProjectionFinalizationResult.ContentTruncated.Should().BeTrue();
            response.ProjectionFinalizationResult.Conflicts.Should().ContainSingle();
            response.ProjectionFinalizationResult.ConflictsTruncated.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Projection_sync_and_finalization_require_guest_readiness_projection_verification_and_current_generation()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope notReady = await helper.SendAsync(ProjectionContentEnvelope(
            AppleVirtualizationHelperOperation.ProjectionSync,
            "golden-sync-not-ready",
            AppleVirtualizationGuestAgentReadinessState.TransportNotConnected,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            projectionGeneration: 1));
        notReady.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        notReady.Error!.Code.Should().Be("AppleVirtualization.GuestAgentNotReady");
        notReady.ProjectionSyncResult!.Succeeded.Should().BeFalse();

        AppleVirtualizationHelperEnvelope notVerified = await helper.SendAsync(ProjectionContentEnvelope(
            AppleVirtualizationHelperOperation.ProjectionFinalize,
            "golden-finalize-not-verified",
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.FrameworkAcceptedOnly,
            projectionGeneration: 1));
        notVerified.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        notVerified.Error!.Code.Should().Be("AppleVirtualization.ProjectionNotVerified");
        notVerified.ProjectionFinalizationResult!.Succeeded.Should().BeFalse();

        AppleVirtualizationHelperEnvelope stale = await helper.SendAsync(ProjectionContentEnvelope(
            AppleVirtualizationHelperOperation.ProjectionSync,
            "golden-sync-stale-generation",
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            projectionGeneration: 99));
        stale.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        stale.Error!.Code.Should().Be("AppleVirtualization.ProjectionStaleGeneration");
        stale.ProjectionSyncResult!.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Projection_sync_and_finalization_return_structured_unsupported_results()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope unsupportedSync = await helper.SendAsync(ProjectionContentEnvelope(
            AppleVirtualizationHelperOperation.ProjectionSync,
            "golden-sync-unsupported",
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            projectionGeneration: 1,
            syncMode: SyncMode.Continuous));

        unsupportedSync.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        unsupportedSync.ProjectionSyncResult!.Succeeded.Should().BeFalse();
        unsupportedSync.ProjectionSyncResult.State.Should().Be(AppleVirtualizationGuestAgentProjectionSyncState.UnsupportedMode);
        unsupportedSync.ProjectionSyncResult.UnsupportedReason.Should().Be("UnsupportedMode");

        AppleVirtualizationHelperEnvelope unsupportedFinalization = await helper.SendAsync(ProjectionContentEnvelope(
            AppleVirtualizationHelperOperation.ProjectionFinalize,
            "golden-finalization-unsupported",
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            projectionGeneration: 1,
            finalizationKind: FinalizationKind.PublishArtifacts));

        unsupportedFinalization.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        unsupportedFinalization.ProjectionFinalizationResult!.Succeeded.Should().BeFalse();
        unsupportedFinalization.ProjectionFinalizationResult.State.Should().Be(AppleVirtualizationGuestAgentProjectionFinalizationState.UnsupportedKind);
        unsupportedFinalization.ProjectionFinalizationResult.UnsupportedReason.Should().Be("UnsupportedKind");
    }

    [Fact]
    public async Task Projection_change_enumeration_and_promotion_are_routed_by_swift_fake_helper()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope enumeration = await helper.SendAsync(ProjectionContentEnvelope(
            AppleVirtualizationHelperOperation.ProjectionEnumerateChanges,
            "golden-enumerate-changes",
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            projectionGeneration: 1));
        enumeration.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        enumeration.ProjectionChangeEnumerationResult.Should().NotBeNull();
        enumeration.ProjectionChangeEnumerationResult!.Changes.Should().Contain(change => change.Path == "/workspace/created.txt");

        AppleVirtualizationHelperEnvelope promotion = await helper.SendAsync(ProjectionContentEnvelope(
            AppleVirtualizationHelperOperation.ProjectionPromote,
            "golden-promote",
            AppleVirtualizationGuestAgentReadinessState.Ready,
            AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            projectionGeneration: 1));
        promotion.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        promotion.ProjectionPromotionResult.Should().NotBeNull();
        promotion.ProjectionPromotionResult!.Succeeded.Should().BeTrue();
        promotion.ProjectionPromotionResult.State.Should().Be(AppleVirtualizationGuestAgentProjectionPromotionState.Succeeded);
    }

    [Fact]
    public async Task Malformed_json_frame_is_reported_and_helper_loop_continues_to_valid_health_frame()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        await helper.WriteRawLineAsync("{\"ProtocolVersion\":\"1.0\",\"MessageType\":0,\"Operation\":4");
        AppleVirtualizationHelperEnvelope malformedResponse = await helper.ReadResponseAsync();

        malformedResponse.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        malformedResponse.Error.Should().NotBeNull();
        malformedResponse.Error!.Code.Should().Be("AppleVirtualization.MalformedFrame");
        malformedResponse.Error.Operation.Should().Be("endpoint.unsupported");
        malformedResponse.Error.FailedPhase.Should().Be("Protocol");

        AppleVirtualizationHelperEnvelope health = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.HealthProbe,
            "golden-health-after-malformed",
            sequenceNumber: 5,
            AppleVirtualizationHelperProtocol.HealthResponseSchema) with
        {
            HealthProbeRequest = new AppleVirtualizationHealthProbeRequest(),
        };

        AppleVirtualizationHelperEnvelope healthResponse = await helper.SendAsync(health);

        healthResponse.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        healthResponse.RequestId.Should().Be("golden-health-after-malformed");
        healthResponse.HealthProbeResponse.Should().NotBeNull();
        healthResponse.HealthProbeResponse!.Ready.Should().BeTrue();
    }

    [Fact]
    public async Task DotNet_generated_endpoint_publish_request_receives_swift_fake_host_local_tcp_route()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EndpointPublish,
            "golden-endpoint-publish",
            sequenceNumber: 70,
            AppleVirtualizationHelperProtocol.EndpointPublicationRequestSchema) with
        {
            EndpointPublicationRequest = new AppleVirtualizationEndpointPublicationRequest
            {
                EndpointId = "endpoint-golden",
                ListenerKind = EndpointListenerKind.HostAddress,
                Transport = NetworkTransport.Tcp,
                ExposureScope = EndpointExposureScope.HostLocal,
                ListenerAddress = "127.0.0.1",
                RequestedPort = 8080,
                TargetKind = EndpointTargetKind.NetworkMembership,
                TargetResourceId = "membership-golden",
                TargetAddress = "10.0.0.2",
                TargetPort = 8080,
                RequireRouteHealth = true,
                ScriptedRouteHealthy = true,
            },
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.Operation.Should().Be(AppleVirtualizationHelperOperation.EndpointPublish);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.EndpointPublicationResponseSchema);
        response.EndpointPublicationResponse.Should().NotBeNull();
        response.EndpointPublicationResponse!.EndpointPhase.Should().Be(PublishedEndpointPhase.Bound);
        response.EndpointPublicationResponse.HpdOwned.Should().BeTrue();
        response.EndpointPublicationResponse.RouteHealthy.Should().BeTrue();
        response.EndpointPublicationResponse.BoundAddress.Should().Be("127.0.0.1");
        response.EndpointPublicationResponse.BoundPort.Should().Be(8080);
        response.EndpointPublicationResponse.ResolvedAddress.Should().Be("10.0.0.2");
        response.EndpointPublicationResponse.ResolvedPort.Should().Be(8080);
    }

    [Fact]
    public void DotNet_generated_process_output_event_has_stable_golden_shape()
    {
        DateTimeOffset observedAt = DateTimeOffset.Parse("2026-05-21T12:00:00.0000000+00:00");
        AppleVirtualizationHelperEnvelope envelope = new()
        {
            ProtocolVersion = AppleVirtualizationHelperProtocol.CurrentVersion,
            MessageType = AppleVirtualizationHelperMessageType.Event,
            Operation = AppleVirtualizationHelperOperation.ProcessReadOutput,
            EventKind = AppleVirtualizationHelperEventKind.ProcessOutput,
            EventId = "golden-process-output-event",
            SequenceNumber = 42,
            Timestamp = observedAt,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.ProcessOutputEventSchema,
            ProcessOutputEvent = new AppleVirtualizationProcessOutputEvent
            {
                ProcessId = "process-golden",
                Stream = ProcessOutputStream.Stdout,
                Sequence = 7,
                ObservedAt = observedAt,
                Bytes = new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f, 0x0a },
                Flags = ProcessOutputChunkFlags.Final | ProcessOutputChunkFlags.Truncated,
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        root.GetProperty("MessageType").GetInt32().Should().Be(2);
        root.GetProperty("Operation").GetInt32().Should().Be(28);
        root.GetProperty("EventKind").GetInt32().Should().Be(27);
        root.GetProperty("PayloadSchema").GetProperty("Value").GetString()
            .Should().Be(AppleVirtualizationHelperProtocol.ProcessOutputEventSchema.Value);
        JsonElement output = root.GetProperty("ProcessOutputEvent");
        output.GetProperty("ProcessId").GetString().Should().Be("process-golden");
        output.GetProperty("Stream").GetInt32().Should().Be(0);
        output.GetProperty("Sequence").GetInt64().Should().Be(7);
        output.GetProperty("Bytes").GetString().Should().Be("aGVsbG8K");
        output.GetProperty("Flags").GetInt32().Should().Be(3);

        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(
            json,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.ProcessOutputEvent.Should().NotBeNull();
        roundTrip.ProcessOutputEvent!.Bytes.ToArray().Should().Equal(0x68, 0x65, 0x6c, 0x6c, 0x6f, 0x0a);
        roundTrip.ProcessOutputEvent.Flags.Should().Be(ProcessOutputChunkFlags.Final | ProcessOutputChunkFlags.Truncated);
    }

    private sealed class SwiftHelperProcess : IAsyncDisposable
    {
        private readonly Process _process;

        private SwiftHelperProcess(Process process)
        {
            _process = process;
        }

        public static async Task<SwiftHelperProcess> StartAsync(bool fake = true)
        {
            string helperPath = ResolveHelperPath();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(helperPath, fake ? "--fake" : "")
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };

            process.Start().Should().BeTrue();
            await Task.Yield();
            return new SwiftHelperProcess(process);
        }

        public async Task<AppleVirtualizationHelperEnvelope> SendAsync(AppleVirtualizationHelperEnvelope envelope)
        {
            string json = JsonSerializer.Serialize(
                envelope,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
            await WriteRawLineAsync(json).ConfigureAwait(false);
            return await ReadResponseAsync().ConfigureAwait(false);
        }

        public async Task WriteRawLineAsync(string line)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.StandardInput.WriteLineAsync(line).WaitAsync(cancellation.Token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellation.Token).ConfigureAwait(false);
        }

        public async Task<AppleVirtualizationHelperEnvelope> ReadResponseAsync()
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string? line = await _process.StandardOutput.ReadLineAsync().WaitAsync(cancellation.Token).ConfigureAwait(false);
            if (line is null)
            {
                string stderr = await _process.StandardError.ReadToEndAsync(cancellation.Token).ConfigureAwait(false);
                throw new InvalidOperationException($"hpd-vz exited before writing a response. stderr: {stderr}");
            }

            return JsonSerializer.Deserialize(
                line,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)
                ?? throw new JsonException("Swift helper response was not a helper envelope.");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _process.StandardInput.Close();
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            finally
            {
                _process.Dispose();
            }
        }

        private static string ResolveHelperPath()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                string helperRoot = Path.Combine(
                    directory.FullName,
                    "HPD-Environment.Framework",
                    "src",
                    "HPD-Environment.AppleVirtualization",
                    "hpd-vz");
                if (Directory.Exists(helperRoot))
                {
                    return FindBuiltHelper(helperRoot);
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate hpd-vz source root from the test base directory.");
        }

        private static string FindBuiltHelper(string helperRoot)
        {
            string[] candidates =
            [
                Path.Combine(helperRoot, ".build", "debug", "hpd-vz"),
                Path.Combine(helperRoot, ".build", "arm64-apple-macosx", "debug", "hpd-vz"),
                Path.Combine(helperRoot, ".build", "x86_64-apple-macosx", "debug", "hpd-vz"),
            ];

            foreach (string candidate in candidates)
            {
                if (IsExecutableHelper(candidate))
                {
                    return candidate;
                }
            }

            string? discovered = Directory.Exists(Path.Combine(helperRoot, ".build"))
                ? Directory.EnumerateFiles(Path.Combine(helperRoot, ".build"), "hpd-vz", SearchOption.AllDirectories)
                    .FirstOrDefault(path =>
                        path.Contains($"{Path.DirectorySeparatorChar}debug{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !path.Contains(".dSYM", StringComparison.Ordinal) &&
                        IsExecutableHelper(path))
                : null;

            return discovered ?? throw new InvalidOperationException(
                $"Built hpd-vz helper was not found under '{helperRoot}'. Run `swift build` in that directory before golden tests.");
        }

        private static bool IsExecutableHelper(string path)
        {
            if (!File.Exists(path) || path.Contains(".dSYM", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                UnixFileMode mode = File.GetUnixFileMode(path);
                return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
            }
            catch (PlatformNotSupportedException)
            {
                return true;
            }
        }
    }

    private static AppleVirtualizationHelperEnvelope HostLifecycleEnvelope(
        AppleVirtualizationHelperOperation operation,
        string requestId,
        string hostId) =>
        AppleVirtualizationHelperEnvelope.Request(
            operation,
            requestId,
            sequenceNumber: 35,
            AppleVirtualizationHelperProtocol.HostRequestSchema) with
        {
            HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
            {
                HostId = hostId,
                Reason = requestId,
            },
        };

    private static AppleVirtualizationHelperEnvelope GuestTransportEnvelope(
        string requestId,
        string hostId,
        AppleVirtualizationGuestAgentTransportState? scriptedStatus) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.GuestAgentTransportProbe,
            requestId,
            sequenceNumber: 41,
            AppleVirtualizationHelperProtocol.GuestAgentTransportRequestSchema) with
        {
            GuestAgentTransportProbeRequest = new AppleVirtualizationGuestAgentTransportProbeRequest
            {
                HostId = hostId,
                TimeoutMilliseconds = 50,
                ExplicitRealMode = true,
                RequireVmRunning = true,
                ScriptedStatus = scriptedStatus,
                Endpoint = new AppleVirtualizationGuestAgentTransportEndpoint
                {
                    Kind = AppleVirtualizationGuestAgentTransportKind.VirtioSocket,
                    Port = 7_777,
                    Name = "hpd-guest-agent",
                },
            },
        };

    private static AppleVirtualizationHelperEnvelope GuestReadinessEnvelope(
        string requestId,
        string hostId,
        AppleVirtualizationGuestAgentReadinessState scriptedState) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.GuestAgentReadinessProbe,
            requestId,
            sequenceNumber: 42,
            AppleVirtualizationHelperProtocol.GuestAgentReadinessRequestSchema) with
        {
            GuestAgentReadinessProbeRequest = new AppleVirtualizationGuestAgentReadinessProbeRequest
            {
                HostId = hostId,
                TimeoutMilliseconds = 50,
                ExplicitRealMode = true,
                ExpectedProtocolVersion = "1.0",
                ExpectedAgentVersion = "0.1.0-test",
                RequiredCapabilities = ["process.start", "projection.mount"],
                ScriptedState = scriptedState,
                Endpoint = new AppleVirtualizationGuestAgentTransportEndpoint
                {
                    Kind = AppleVirtualizationGuestAgentTransportKind.VirtioSocket,
                    Port = 7_777,
                    Name = "hpd-guest-agent",
                },
            },
        };

    private static AppleVirtualizationHelperEnvelope ProjectionEnvelope(
        AppleVirtualizationHelperOperation operation,
        string requestId,
        AppleVirtualizationGuestAgentReadinessState scriptedReadiness,
        AppleVirtualizationHelperProjectionScriptedGuestState scriptedProjection)
    {
        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            operation,
            requestId,
            sequenceNumber: 44,
            AppleVirtualizationHelperProtocol.ProjectionRequestSchema);

        return operation switch
        {
            AppleVirtualizationHelperOperation.ProjectionMount => request with
            {
                ProjectionMountRequest = new AppleVirtualizationProjectionMountRequest
                {
                    ProjectionId = "projection-golden",
                    HostId = "host-projection",
                    HostPath = "/Users/test/workspace",
                    Tag = "hpdprojectiongolden",
                    GuestPath = "/workspace",
                    AccessMode = AccessMode.ReadOnly,
                    Realization = ProjectionRealizationKind.LiveProjection,
                    RequestedWriteEffect = ProjectionWriteEffect.NoWrites,
                    RequestedCoherence = CoherenceClass.CloseToOpen,
                    ScriptedReadinessState = scriptedReadiness,
                    ScriptedGuestProjectionState = scriptedProjection,
                },
            },
            AppleVirtualizationHelperOperation.ProjectionStatus => request with
            {
                ProjectionStatusRequest = new AppleVirtualizationProjectionStatusRequest
                {
                    ProjectionId = "projection-golden",
                    HostId = "host-projection",
                    ExpectedGuestPath = "/workspace",
                    ScriptedReadinessState = scriptedReadiness,
                    ScriptedGuestProjectionState = scriptedProjection,
                },
            },
            AppleVirtualizationHelperOperation.ProjectionUnmount => request with
            {
                ProjectionUnmountRequest = new AppleVirtualizationProjectionUnmountRequest
                {
                    ProjectionId = "projection-golden",
                    HostId = "host-projection",
                    GuestPath = "/workspace",
                    Force = true,
                    ScriptedReadinessState = scriptedReadiness,
                    ScriptedGuestProjectionState = scriptedProjection,
                },
            },
            AppleVirtualizationHelperOperation.ProjectionObserve => request with
            {
                ProjectionObserveRequest = new AppleVirtualizationProjectionObserveRequest
                {
                    ProjectionId = "projection-golden",
                    HostId = "host-projection",
                    GuestPath = "/workspace",
                    Recursive = false,
                    AfterSequence = 0,
                    Limit = 1,
                    ScriptedReadinessState = scriptedReadiness,
                    ScriptedGuestProjectionState = scriptedProjection,
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Operation is not a projection lifecycle operation."),
        };
    }

    private static AppleVirtualizationHelperEnvelope ProjectionContentEnvelope(
        AppleVirtualizationHelperOperation operation,
        string requestId,
        AppleVirtualizationGuestAgentReadinessState scriptedReadiness,
        AppleVirtualizationHelperProjectionScriptedGuestState scriptedProjection,
        ulong projectionGeneration,
        SyncMode syncMode = SyncMode.Manual,
        FinalizationKind finalizationKind = FinalizationKind.ManifestAndChangedContent)
    {
        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            operation,
            requestId,
            sequenceNumber: 50,
            operation == AppleVirtualizationHelperOperation.ProjectionFinalize
                ? AppleVirtualizationHelperProtocol.ProjectionFinalizationRequestSchema
                : AppleVirtualizationHelperProtocol.ProjectionSyncRequestSchema);

        var generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(
            GuestBootId: "guest-boot-1",
            GuestBootGeneration: 1,
            GuestAgentGeneration: 1,
            ProjectionGeneration: projectionGeneration);

        return operation switch
        {
            AppleVirtualizationHelperOperation.ProjectionSync => request with
            {
                ProjectionSyncRequest = new AppleVirtualizationProjectionSyncRequest
                {
                    ProjectionId = "projection-golden",
                    HostId = "host-projection",
                    GuestPath = "/workspace",
                    Mode = syncMode,
                    Direction = SyncDirection.TargetToSource,
                    ConflictPolicy = ConflictPolicy.RecordConflict,
                    DryRun = false,
                    MaxChanges = 2,
                    MaxConflicts = 1,
                    Generation = generation,
                    ScriptedReadinessState = scriptedReadiness,
                    ScriptedGuestProjectionState = scriptedProjection,
                },
            },
            AppleVirtualizationHelperOperation.ProjectionFinalize => request with
            {
                ProjectionFinalizationRequest = new AppleVirtualizationProjectionFinalizationRequest
                {
                    ProjectionId = "projection-golden",
                    HostId = "host-projection",
                    GuestPath = "/workspace",
                    Kind = finalizationKind,
                    IncludeProvenance = true,
                    IncludeDeletedEntries = true,
                    ProducerId = "golden",
                    MaxContentRefs = 2,
                    MaxConflicts = 1,
                    Generation = generation,
                    ScriptedReadinessState = scriptedReadiness,
                    ScriptedGuestProjectionState = scriptedProjection,
                },
            },
            AppleVirtualizationHelperOperation.ProjectionEnumerateChanges => request with
            {
                ProjectionChangeEnumerationRequest = new AppleVirtualizationProjectionChangeEnumerationRequest
                {
                    ProjectionId = "projection-golden",
                    HostId = "host-projection",
                    GuestPath = "/workspace",
                    AfterSequence = 0,
                    Limit = 2,
                    IncludeDeletedEntries = true,
                    Generation = generation,
                    ScriptedReadinessState = scriptedReadiness,
                    ScriptedGuestProjectionState = scriptedProjection,
                },
            },
            AppleVirtualizationHelperOperation.ProjectionPromote => request with
            {
                ProjectionPromotionRequest = new AppleVirtualizationProjectionPromotionRequest
                {
                    ProjectionId = "projection-golden",
                    HostId = "host-projection",
                    GuestPath = "/workspace",
                    Direction = SyncDirection.TargetToSource,
                    ConflictPolicy = ConflictPolicy.RequireExplicitPromotion,
                    DryRun = false,
                    MaxChanges = 2,
                    MaxConflicts = 1,
                    Generation = generation,
                    ScriptedReadinessState = scriptedReadiness,
                    ScriptedGuestProjectionState = scriptedProjection,
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Operation is not a projection content lifecycle operation."),
        };
    }

    private static AppleVirtualizationHelperEnvelope AuthorityRevokeEnvelope(
        int? observedFileDescriptor = null,
        bool? guestSocketPresent = null) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.AuthorityRevoke,
            "golden-authority-revoke",
            sequenceNumber: 23,
            AppleVirtualizationHelperProtocol.AuthorityBindingRequestSchema) with
        {
            AuthorityBindingRequest = new AppleVirtualizationAuthorityBindingRequest
            {
                BindingId = "authority-golden",
                Action = AppleVirtualizationAuthorityBindingAction.Revoke,
                Source = new AppleVirtualizationAuthoritySourceDescriptor
                {
                    Kind = AuthoritySourceKind.UnixSocket,
                    Locus = BoundaryLocus.RuntimeHost,
                    SocketPath = new UnixSocketPath("/run/user/1000/docker.sock"),
                    SensitiveEndpointKind = SensitiveEndpointKind.EngineSocket,
                    AuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                    RedactedDisplayName = "engine:***",
                },
                Target = new AppleVirtualizationAuthorityTargetDescriptor
                {
                    Kind = AuthorityTargetKind.ExecutionUnit,
                    UnitId = "unit-golden",
                    Locus = BoundaryLocus.ExecutionUnit,
                },
                Projection = new AppleVirtualizationAuthorityProjectionDescriptor
                {
                    Kind = AuthorityProjectionKind.SocketPath,
                    TargetSocketPath = new UnixSocketPath("/run/hpd/engine/docker.sock"),
                },
                Direction = AuthorityBindingDirection.ProviderToGuest,
                RequestedAuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                EffectiveAuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                AuditCorrelationId = "authority-golden-correlation",
                ObservedFileDescriptor = observedFileDescriptor,
                GuestSocketPresent = guestSocketPresent,
            },
        };

    private static AppleVirtualizationHelperEnvelope AuthorityStatusEnvelope() =>
        AuthorityRevokeEnvelope() with
        {
            Operation = AppleVirtualizationHelperOperation.AuthorityStatus,
            RequestId = "golden-authority-status",
            AuthorityBindingRequest = AuthorityRevokeEnvelope().AuthorityBindingRequest! with
            {
                Action = AppleVirtualizationAuthorityBindingAction.Status,
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessStartEnvelope(
        string requestId,
        AppleVirtualizationGuestAgentReadinessState scriptedReadiness,
        AppleVirtualizationHelperProjectionScriptedGuestState scriptedProjection) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProcessStart,
            requestId,
            sequenceNumber: 45,
            AppleVirtualizationHelperProtocol.ProcessRequestSchema) with
        {
            ProcessStartRequest = new AppleVirtualizationProcessStartRequest
            {
                ProcessId = "process-golden",
                UnitId = "unit-golden",
                Command = new ProcessCommandSpec
                {
                    FileName = "uname",
                    Arguments = ["-a"],
                    WorkingDirectory = "/workspace",
                    Environment = new Dictionary<string, string?> { ["HPD_TEST"] = "1" },
                },
                Io = ProcessIoSpec.Default,
                Policy = ProcessInvocationPolicy.Default,
                RequiredProjectionId = "projection-golden",
                RequiredProjectionGuestPath = "/workspace",
                RequireVerifiedProjection = true,
                ScriptedReadinessState = scriptedReadiness,
                ScriptedGuestProjectionState = scriptedProjection,
            },
        };

    private static AppleVirtualizationHelperEnvelope NetworkStatusEnvelope(
        string requestId,
        AppleVirtualizationNetworkAttachmentKind requestedAttachment) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.NetworkStatus,
            requestId,
            sequenceNumber: 67,
            AppleVirtualizationHelperProtocol.NetworkStatusRequestSchema) with
        {
            NetworkStatusRequest = new AppleVirtualizationNetworkStatusRequest
            {
                HostId = "host-network",
                RequestedAttachment = requestedAttachment,
                IncludeGuestObservation = true,
                IncludeSocketObservation = true,
                MaxInterfaces = 4,
                MaxRoutes = 4,
                MaxListeners = 4,
                ScriptedReadinessState = AppleVirtualizationGuestAgentReadinessState.Ready,
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessLifecycleEnvelope(
        AppleVirtualizationHelperOperation operation,
        string requestId,
        AppleVirtualizationGuestAgentReadinessState scriptedReadiness,
        AppleVirtualizationHelperProjectionScriptedGuestState scriptedProjection)
    {
        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            operation,
            requestId,
            sequenceNumber: 46,
            AppleVirtualizationHelperProtocol.ProcessRequestSchema);

        return operation switch
        {
            AppleVirtualizationHelperOperation.ProcessStdin => request with
            {
                ProcessStdinRequest = new AppleVirtualizationProcessStdinRequest
                {
                    ProcessId = "process-golden",
                    Bytes = "stdin\n"u8.ToArray(),
                    Sequence = 1,
                    CloseAfterWrite = false,
                    ScriptedReadinessState = scriptedReadiness,
                },
            },
            AppleVirtualizationHelperOperation.ProcessCloseStdin => request with
            {
                ProcessStdinRequest = new AppleVirtualizationProcessStdinRequest
                {
                    ProcessId = "process-golden",
                    Bytes = ReadOnlyMemory<byte>.Empty,
                    Sequence = 2,
                    CloseAfterWrite = true,
                    ScriptedReadinessState = scriptedReadiness,
                },
            },
            AppleVirtualizationHelperOperation.ProcessSignal => request with
            {
                ProcessSignalRequest = new AppleVirtualizationProcessSignalRequest(
                    "process-golden",
                    new ProcessSignal("SIGTERM"),
                    scriptedReadiness),
            },
            AppleVirtualizationHelperOperation.ProcessStop => request with
            {
                ProcessStopRequest = new AppleVirtualizationProcessStopRequest(
                    "process-golden",
                    StopKind.GracefulThenKill,
                    TimeSpan.FromSeconds(1),
                    "golden-stop",
                    scriptedReadiness),
            },
            AppleVirtualizationHelperOperation.ProcessWait or AppleVirtualizationHelperOperation.ProcessReadOutput => request with
            {
                ProcessLifecycleRequest = new AppleVirtualizationProcessLifecycleRequest
                {
                    ProcessId = "process-golden",
                    AfterOutputSequence = 7,
                    OutputLimit = 1,
                    ScriptedReadinessState = scriptedReadiness,
                    ScriptedGuestProjectionState = scriptedProjection,
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Operation is not a process lifecycle operation."),
        };
    }
}
