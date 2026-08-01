namespace HPD.Environment.AppleVirtualization.Tests;

using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Sandbox.ProcessIsolation;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationHelperProtocolTests
{
    [Fact]
    public void Hello_envelope_round_trips_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.Hello,
            "request-1",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.HelloRequestSchema) with
        {
            HelloRequest = new AppleVirtualizationHelperHelloRequest
            {
                ClientName = "test-client",
                MinimumProtocolVersion = "1.0",
                RequestedProtocolVersion = AppleVirtualizationHelperProtocol.CurrentVersion,
            },
        };

        string json = JsonSerializer.Serialize(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope? roundTrip = JsonSerializer.Deserialize(json, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);

        roundTrip.Should().NotBeNull();
        roundTrip!.ProtocolVersion.Should().Be(AppleVirtualizationHelperProtocol.CurrentVersion);
        roundTrip.MessageType.Should().Be(AppleVirtualizationHelperMessageType.Request);
        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.Hello);
        roundTrip.RequestId.Should().Be("request-1");
        roundTrip.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.HelloRequestSchema);
        roundTrip.HelloRequest!.ClientName.Should().Be("test-client");
    }

    [Fact]
    public void Error_response_round_trips_with_retryability_and_detail_payload()
    {
        byte[] detail = Encoding.UTF8.GetBytes("invalid virtual machine configuration");
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.HostStart,
            "request-2",
            sequenceNumber: 2,
            AppleVirtualizationHelperProtocol.ErrorSchema).ToResponse(sequenceNumber: 3) with
        {
            Error = new AppleVirtualizationHelperError
            {
                Code = "AppleVirtualization.ConfigurationInvalid",
                Message = "The VM configuration failed validation.",
                FailedPhase = "Preparing",
                Retryable = false,
                Severity = DiagnosticSeverity.Error,
                DetailSchema = new SchemaId("text/plain"),
                Detail = detail,
            },
        };

        string json = JsonSerializer.Serialize(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope? roundTrip = JsonSerializer.Deserialize(json, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);

        roundTrip.Should().NotBeNull();
        roundTrip!.MessageType.Should().Be(AppleVirtualizationHelperMessageType.Response);
        roundTrip.CausationId.Should().Be("request-2");
        roundTrip.Error!.Code.Should().Be("AppleVirtualization.ConfigurationInvalid");
        roundTrip.Error.Retryable.Should().BeFalse();
        roundTrip.Error.FailedPhase.Should().Be("Preparing");
        Encoding.UTF8.GetString(roundTrip.Error.Detail.ToArray()).Should().Be("invalid virtual machine configuration");
    }

    [Fact]
    public void Response_envelope_round_trips_with_typed_status_payload()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.HealthProbe,
            "request-health",
            sequenceNumber: 5,
            AppleVirtualizationHelperProtocol.HealthResponseSchema).ToResponse(sequenceNumber: 6) with
        {
            HealthProbeResponse = new AppleVirtualizationHealthProbeResponse(
                Ready: true,
                Detail: "helper protocol loop is ready"),
        };

        byte[] json = AppleVirtualizationHelperJsonCodec.Encode(envelope);
        AppleVirtualizationHelperEnvelope roundTrip = AppleVirtualizationHelperJsonCodec.Decode(json);

        roundTrip.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.HealthProbe);
        roundTrip.CausationId.Should().Be("request-health");
        roundTrip.HealthProbeResponse.Should().NotBeNull();
        roundTrip.HealthProbeResponse!.Ready.Should().BeTrue();
    }

    [Fact]
    public void Process_output_event_maps_to_process_output_chunk_shape()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("hello\n");
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        AppleVirtualizationHelperEnvelope envelope = new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Event,
            Operation = AppleVirtualizationHelperOperation.ProcessReadOutput,
            EventKind = AppleVirtualizationHelperEventKind.ProcessOutput,
            EventId = "event-1",
            SequenceNumber = 10,
            Timestamp = observedAt,
            ResourceKind = new ResourceKind("process-invocation"),
            ResourceId = "process-1",
            ResourceScope = new ResourceScope("runtime-1"),
            PayloadSchema = AppleVirtualizationHelperProtocol.ProcessOutputEventSchema,
            ProcessOutputEvent = new AppleVirtualizationProcessOutputEvent
            {
                ProcessId = "process-1",
                Stream = ProcessOutputStream.Stdout,
                Sequence = 7,
                ObservedAt = observedAt,
                Bytes = bytes,
                Flags = ProcessOutputChunkFlags.Final,
            },
        };

        string json = JsonSerializer.Serialize(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(json, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.ProcessOutputEvent.Should().NotBeNull();
        roundTrip.ProcessOutputEvent!.Stream.Should().Be(ProcessOutputStream.Stdout);
        roundTrip.ProcessOutputEvent.Sequence.Should().Be(7);
        roundTrip.ProcessOutputEvent.Flags.Should().HaveFlag(ProcessOutputChunkFlags.Final);
        Encoding.UTF8.GetString(roundTrip.ProcessOutputEvent.Bytes.ToArray()).Should().Be("hello\n");
    }

    [Fact]
    public void First_slice_operation_names_are_explicit_wire_names()
    {
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.HostEnsure)
            .Should().Be("host.ensure");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.GuestControlWaitReady)
            .Should().Be("guestControl.waitReady");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.ProjectionConfigure)
            .Should().Be("projection.configure");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.ProcessCloseStdin)
            .Should().Be("process.closeStdin");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.ProcessResize)
            .Should().Be("process.resize");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.VmConfigurationValidate)
            .Should().Be("vmConfiguration.validate");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.GuestAgentTransportProbe)
            .Should().Be("guestAgent.transportProbe");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.ProjectionUnmount)
            .Should().Be("projection.unmount");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.ProjectionObserve)
            .Should().Be("projection.observe");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.ProjectionSync)
            .Should().Be("projection.sync");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.ProjectionFinalize)
            .Should().Be("projection.finalize");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.ProjectionEnumerateChanges)
            .Should().Be("projection.enumerateChanges");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.ProjectionPromote)
            .Should().Be("projection.promote");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.NetworkStatus)
            .Should().Be("network.status");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.EndpointPublish)
            .Should().Be("endpoint.publish");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.EndpointRelease)
            .Should().Be("endpoint.release");
        AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.EndpointUnsupported)
            .Should().Be("endpoint.unsupported");
        AppleVirtualizationHelperOperationNames.ToWireName(
                AppleVirtualizationHelperOperation.Storage)
            .Should().Be("storage");
    }

    [Fact]
    public void Storage_protocol_round_trips_identity_capacity_and_action()
    {
        AppleVirtualizationHelperEnvelope envelope =
            AppleVirtualizationHelperEnvelope.Request(
                AppleVirtualizationHelperOperation.Storage,
                "storage-request",
                50,
                AppleVirtualizationHelperProtocol.StorageRequestSchema) with
            {
                StorageRequest =
                    new AppleVirtualizationStorageRequest
                    {
                        HostId = "host-a",
                        ProviderGeneration = 7,
                        HostStartGeneration = 3,
                        Action =
                            AppleVirtualizationStorageAction.WriteRestoreChunk,
                        LogicalVolumeId = "penpot-data",
                        MaximumBytes = new ByteSize(1048576),
                        OperationId = "restore-a",
                        Offset = 4096,
                        ChunkBase64 = "ZHVyYWJsZQ==",
                        ExpectedContentSha256 = new string('a', 64),
                        ExpectedEncodedPayloadBytes = 8192,
                        ExpectedLogicalBytes = 4096,
                        ExpectedEntryCount = 3,
                    },
            };

        byte[] encoded =
            AppleVirtualizationHelperJsonCodec.Encode(envelope);
        AppleVirtualizationHelperEnvelope decoded =
            AppleVirtualizationHelperJsonCodec.Decode(encoded);

        decoded.PayloadSchema.Should().Be(
            AppleVirtualizationHelperProtocol.StorageRequestSchema);
        decoded.StorageRequest.Should().NotBeNull();
        decoded.StorageRequest!.HostId.Should().Be("host-a");
        decoded.StorageRequest.ProviderGeneration.Should().Be(7);
        decoded.StorageRequest.HostStartGeneration.Should().Be(3);
        decoded.StorageRequest.Action.Should().Be(
            AppleVirtualizationStorageAction.WriteRestoreChunk);
        decoded.StorageRequest.LogicalVolumeId.Should().Be(
            "penpot-data");
        decoded.StorageRequest.MaximumBytes.Should().Be(
            new ByteSize(1048576));
        decoded.StorageRequest.OperationId.Should().Be("restore-a");
        decoded.StorageRequest.Offset.Should().Be(4096);
        decoded.StorageRequest.ChunkBase64.Should().Be("ZHVyYWJsZQ==");
        decoded.StorageRequest.ExpectedContentSha256.Should()
            .Be(new string('a', 64));
        decoded.StorageRequest.ExpectedEncodedPayloadBytes.Should().Be(8192);
        decoded.StorageRequest.ExpectedLogicalBytes.Should().Be(4096);
        decoded.StorageRequest.ExpectedEntryCount.Should().Be(3);
    }

    [Fact]
    public void Endpoint_publication_dtos_round_trip_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EndpointPublish,
            "request-endpoint-publish",
            sequenceNumber: 70,
            AppleVirtualizationHelperProtocol.EndpointPublicationRequestSchema).ToResponse(sequenceNumber: 71) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.EndpointPublicationResponseSchema,
            EndpointPublicationRequest = new AppleVirtualizationEndpointPublicationRequest
            {
                EndpointId = "endpoint-1",
                ListenerKind = EndpointListenerKind.HostAddress,
                Transport = NetworkTransport.Tcp,
                ExposureScope = EndpointExposureScope.HostLocal,
                ListenerAddress = "127.0.0.1",
                RequestedPort = 8080,
                TargetKind = EndpointTargetKind.NetworkMembership,
                TargetResourceId = "membership-1",
                TargetAddress = "10.0.0.2",
                TargetPort = 8080,
                RequireRouteHealth = true,
                ScriptedRouteHealthy = true,
            },
            EndpointPublicationResponse = new AppleVirtualizationEndpointPublicationResponse
            {
                EndpointId = "endpoint-1",
                EndpointPhase = PublishedEndpointPhase.Bound,
                ListenerKind = EndpointListenerKind.HostAddress,
                Transport = NetworkTransport.Tcp,
                ExposureScope = EndpointExposureScope.HostLocal,
                BoundAddress = "127.0.0.1",
                BoundPort = 8080,
                HpdOwned = true,
                RouteHealthy = true,
                ResolvedAddress = "10.0.0.2",
                ResolvedPort = 8080,
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(json, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.EndpointPublish);
        roundTrip.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.EndpointPublicationResponseSchema);
        roundTrip.EndpointPublicationRequest!.EndpointId.Should().Be("endpoint-1");
        roundTrip.EndpointPublicationRequest.Transport.Should().Be(NetworkTransport.Tcp);
        roundTrip.EndpointPublicationResponse!.EndpointPhase.Should().Be(PublishedEndpointPhase.Bound);
        roundTrip.EndpointPublicationResponse.HpdOwned.Should().BeTrue();
        roundTrip.EndpointPublicationResponse.RouteHealthy.Should().BeTrue();
        roundTrip.EndpointPublicationResponse.BoundPort.Should().Be(8080);
    }

    [Fact]
    public void Network_status_dtos_round_trip_through_source_generated_json()
    {
        var address = new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000002);
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.NetworkStatus,
            "request-network-status",
            sequenceNumber: 41,
            AppleVirtualizationHelperProtocol.NetworkStatusRequestSchema).ToResponse(sequenceNumber: 42) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.NetworkStatusResponseSchema,
            NetworkStatusRequest = new AppleVirtualizationNetworkStatusRequest
            {
                HostId = "host-network",
                RequestedAttachment = AppleVirtualizationNetworkAttachmentKind.Nat,
                IncludeGuestObservation = true,
                IncludeSocketObservation = true,
                MaxInterfaces = 4,
                MaxRoutes = 8,
                MaxListeners = 16,
            },
            NetworkStatusResponse = new AppleVirtualizationNetworkStatusResponse
            {
                HostId = "host-network",
                State = AppleVirtualizationNetworkObservationState.Ready,
                DefaultAttachment = AppleVirtualizationNetworkAttachmentKind.Nat,
                RequestedAttachment = AppleVirtualizationNetworkAttachmentKind.Nat,
                RealizedCapabilities = NetworkCapabilitySet.IPv4 | NetworkCapabilitySet.NatEgress,
                DiscoveryCapabilities = DiscoveryCapabilitySet.None,
                VmRunning = true,
                GuestAgentReady = true,
                VirtioSocketConfigured = true,
                AttachmentCapabilities =
                [
                    new AppleVirtualizationNetworkAttachmentCapabilityFact
                    {
                        AttachmentKind = AppleVirtualizationNetworkAttachmentKind.Nat,
                        State = CapabilityState.Supported,
                        Capabilities = NetworkCapabilitySet.IPv4 | NetworkCapabilitySet.NatEgress,
                        Detail = "NAT egress only.",
                    },
                    new AppleVirtualizationNetworkAttachmentCapabilityFact
                    {
                        AttachmentKind = AppleVirtualizationNetworkAttachmentKind.VirtioSocket,
                        State = CapabilityState.Supported,
                        Capabilities = NetworkCapabilitySet.None,
                        Detail = "Not TCP/UDP endpoint publication.",
                    },
                ],
                GuestNetworkStatus = new AppleVirtualizationGuestAgentNetworkStatus
                {
                    HostId = "host-network",
                    GuestAgentReady = true,
                    Interfaces =
                    [
                        new AppleVirtualizationGuestAgentNetworkInterfaceStatus
                        {
                            Name = "en0",
                            Mtu = 1500,
                            IsUp = true,
                            Addresses = [new NetworkAddressAssignment(address, 24, AddressAssignmentKind.ProviderAssigned, IsPrimary: true)],
                        },
                    ],
                    Listeners =
                    [
                        new AppleVirtualizationGuestAgentNetworkListenerObservation
                        {
                            Name = "guest-listener",
                            Transport = NetworkTransport.Tcp,
                            Address = address,
                            Port = new NetworkPort(8080),
                            GuestVisibleOnly = true,
                            HpdPublished = false,
                        },
                    ],
                },
                Limitations =
                [
                    new NetworkLimitation(NetworkDegradedFeature.StaticAddress, CapabilityDegradationMode.Unsupported, "AppleVirtualization.StaticAddressNotAssignedByVz"),
                ],
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(json, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.NetworkStatus);
        roundTrip.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.NetworkStatusResponseSchema);
        roundTrip.NetworkStatusRequest!.HostId.Should().Be("host-network");
        roundTrip.NetworkStatusResponse!.State.Should().Be(AppleVirtualizationNetworkObservationState.Ready);
        roundTrip.NetworkStatusResponse.RealizedCapabilities.Should().HaveFlag(NetworkCapabilitySet.NatEgress);
        roundTrip.NetworkStatusResponse.AttachmentCapabilities.Should().Contain(fact =>
            fact.AttachmentKind == AppleVirtualizationNetworkAttachmentKind.VirtioSocket &&
            fact.Capabilities == NetworkCapabilitySet.None);
        roundTrip.NetworkStatusResponse.GuestNetworkStatus!.Listeners.Should().ContainSingle(listener =>
            listener.GuestVisibleOnly && !listener.HpdPublished);
        roundTrip.NetworkStatusResponse.Limitations.Should().Contain(limitation =>
            limitation.Feature == NetworkDegradedFeature.StaticAddress &&
            limitation.Mode == CapabilityDegradationMode.Unsupported);
    }

    [Fact]
    public void Process_resize_request_round_trips_through_source_generated_json()
    {
        var processHandle = new ProviderOpaqueHandle(
            AppleVirtualizationProviderDescriptor.ProviderId,
            "process-invocation:scope:process-1:g1:h1",
            new SchemaId("hpd.execution.apple-virtualization.handle.process-invocation.v1"),
            Generation: 1);
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProcessResize,
            "request-resize",
            sequenceNumber: 31,
            AppleVirtualizationHelperProtocol.ProcessRequestSchema) with
        {
            ResourceKind = new ResourceKind("process-invocation"),
            ResourceId = "process-1",
            ResourceScope = new ResourceScope("scope"),
            ProviderHandle = processHandle,
            ProcessResizeRequest = new AppleVirtualizationProcessResizeRequest
            {
                ProcessId = "process-1",
                ProcessHandle = processHandle,
                Size = new TerminalSpec(132, 43),
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(
            json,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessResize);
        roundTrip.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.ProcessRequestSchema);
        roundTrip.ProcessResizeRequest.Should().NotBeNull();
        roundTrip.ProcessResizeRequest!.ProcessId.Should().Be("process-1");
        roundTrip.ProcessResizeRequest.ProcessHandle.Should().Be(processHandle);
        roundTrip.ProcessResizeRequest.Size.Columns.Should().Be(132);
        roundTrip.ProcessResizeRequest.Size.Rows.Should().Be(43);
    }

    [Fact]
    public void Helper_process_lifecycle_requests_round_trip_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope start = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProcessStart,
            "request-process-start",
            sequenceNumber: 45,
            AppleVirtualizationHelperProtocol.ProcessRequestSchema).ToResponse(sequenceNumber: 46) with
        {
            ProcessStartRequest = new AppleVirtualizationProcessStartRequest
            {
                ProcessId = "process-bridge",
                UnitId = "unit-bridge",
                Command = new ProcessCommandSpec
                {
                    FileName = "uname",
                    Arguments = ["-a"],
                    WorkingDirectory = "/workspace",
                    Environment = new Dictionary<string, string?> { ["HPD_TEST"] = "1" },
                },
                Identity = new ProcessIdentitySpec(User: "hpd", Group: "hpd"),
                Limits = new ProcessLimitSpec(ProcessCount: 8, MemoryBytes: 128 * 1024 * 1024),
                Isolation = ProcessIsolationPolicy.Default with
                {
                    Mode = ProcessIsolationMode.Isolated,
                    Network = new NetworkEgressPolicy
                    {
                        Mode = NetworkEgressMode.Unrestricted,
                    },
                },
                SandboxPlan = new SandboxPlanEnvelope
                {
                    SchemaId = SandboxPlanEnvelope.DefaultSchemaId,
                    ExecutionPlatform = new PlatformSpec("linux", "arm64"),
                    EnforcementLocation = SandboxEnforcementLocation.Guest,
                    Plan = SandboxIsolationCompiler.Compile(ProcessIsolationPolicy.Default with
                    {
                        Mode = ProcessIsolationMode.Isolated,
                        Network = new NetworkEgressPolicy
                        {
                            Mode = NetworkEgressMode.Unrestricted,
                        },
                    }),
                },
                RequiredProjectionId = "projection-bridge",
                RequiredProjectionGuestPath = "/workspace",
                RequireVerifiedProjection = true,
                ScriptedReadinessState = AppleVirtualizationGuestAgentReadinessState.Ready,
                ScriptedGuestProjectionState = AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            },
            ProcessStatusResponse = new AppleVirtualizationProcessStatusResponse
            {
                ProcessId = "process-bridge",
                ProcessPhase = ProcessInvocationPhase.Running,
                IoState = ProcessIoState.Open,
                SystemProcessId = 4242,
                ProviderProcessId = "guest-process-bridge",
            },
        };

        AppleVirtualizationHelperEnvelope stdin = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProcessStdin,
            "request-process-stdin",
            sequenceNumber: 47,
            AppleVirtualizationHelperProtocol.ProcessRequestSchema) with
        {
            ProcessStdinRequest = new AppleVirtualizationProcessStdinRequest
            {
                ProcessId = "process-bridge",
                Bytes = "input\n"u8.ToArray(),
                Sequence = 3,
                CloseAfterWrite = true,
                ScriptedReadinessState = AppleVirtualizationGuestAgentReadinessState.Ready,
            },
        };

        AppleVirtualizationHelperEnvelope lifecycle = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProcessReadOutput,
            "request-process-output",
            sequenceNumber: 48,
            AppleVirtualizationHelperProtocol.ProcessRequestSchema) with
        {
            ProcessLifecycleRequest = new AppleVirtualizationProcessLifecycleRequest
            {
                ProcessId = "process-bridge",
                AfterOutputSequence = 7,
                OutputLimit = 4,
                ScriptedReadinessState = AppleVirtualizationGuestAgentReadinessState.Ready,
                ScriptedGuestProjectionState = AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            },
        };

        AppleVirtualizationHelperEnvelope stop = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProcessStop,
            "request-process-stop",
            sequenceNumber: 49,
            AppleVirtualizationHelperProtocol.ProcessRequestSchema) with
        {
            ProcessStopRequest = new AppleVirtualizationProcessStopRequest(
                "process-bridge",
                StopKind.GracefulThenKill,
                TimeSpan.FromSeconds(1),
                "test",
                AppleVirtualizationGuestAgentReadinessState.Ready),
        };

        AppleVirtualizationHelperEnvelope[] envelopes = [start, stdin, lifecycle, stop];
        foreach (AppleVirtualizationHelperEnvelope envelope in envelopes)
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                envelope,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
            AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(
                json,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

            roundTrip.Operation.Should().Be(envelope.Operation);
            roundTrip.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.ProcessRequestSchema);
        }

        AppleVirtualizationHelperEnvelope startRoundTrip = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(start, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope),
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;
        startRoundTrip.ProcessStartRequest!.RequireVerifiedProjection.Should().BeTrue();
        startRoundTrip.ProcessStartRequest.RequiredProjectionGuestPath.Should().Be("/workspace");
        startRoundTrip.ProcessStartRequest.Identity!.User.Should().Be("hpd");
        startRoundTrip.ProcessStartRequest.SandboxPlan.Should().NotBeNull();
        startRoundTrip.ProcessStartRequest.SandboxPlan!.EnforcementLocation.Should().Be(SandboxEnforcementLocation.Guest);
        startRoundTrip.ProcessStartRequest.SandboxPlan.ExecutionPlatform.OperatingSystem.Should().Be("linux");
        startRoundTrip.ProcessStatusResponse!.ProcessPhase.Should().Be(ProcessInvocationPhase.Running);

        AppleVirtualizationHelperEnvelope stdinRoundTrip = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(stdin, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope),
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;
        stdinRoundTrip.ProcessStdinRequest!.Bytes.ToArray().Should().Equal("input\n"u8.ToArray());
        stdinRoundTrip.ProcessStdinRequest.Sequence.Should().Be(3);

        AppleVirtualizationHelperEnvelope lifecycleRoundTrip = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(lifecycle, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope),
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;
        lifecycleRoundTrip.ProcessLifecycleRequest!.AfterOutputSequence.Should().Be(7);
        lifecycleRoundTrip.ProcessLifecycleRequest.OutputLimit.Should().Be(4);

        AppleVirtualizationHelperEnvelope stopRoundTrip = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(stop, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope),
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;
        stopRoundTrip.ProcessStopRequest!.Kind.Should().Be(StopKind.GracefulThenKill);
        stopRoundTrip.ProcessStopRequest.ScriptedReadinessState.Should().Be(AppleVirtualizationGuestAgentReadinessState.Ready);
    }

    [Fact]
    public void Host_lifecycle_request_and_status_round_trip_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.HostStart,
            "request-host-start",
            sequenceNumber: 33,
            AppleVirtualizationHelperProtocol.HostRequestSchema).ToResponse(sequenceNumber: 34) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.HostResponseSchema,
            HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
            {
                HostId = "host-round-trip",
                ExplicitRealMode = true,
                GracePeriodMilliseconds = 250,
                ObservedWakeGeneration = 17,
                VmConfigurationValidationRequest = new AppleVirtualizationVmConfigurationValidationRequest
                {
                    HostId = "host-round-trip",
                    CpuCount = 2,
                    MemorySizeBytes = 512L * 1024 * 1024,
                    GuestImage = new AppleVirtualizationGuestImageOptions
                    {
                        BootLoader = AppleVirtualizationGuestBootLoaderKind.LinuxBootLoader,
                        KernelPath = "/tmp/hpd-vz/vmlinuz",
                        InitrdPath = "/tmp/hpd-vz/initrd.img",
                        DiskAttachments = AppleVirtualizationTestDiskSet.Create("/tmp/hpd-vz/root.raw"),
                        SerialLogPath = "/tmp/hpd-vz/serial.log",
                    },
                },
            },
            HostStatusResponse = new AppleVirtualizationHostStatusResponse
            {
                HostId = "host-round-trip",
                HostPhase = RuntimeHostPhase.Starting,
                Phase = ResourcePhase.Reconciling,
                GuestControlReachable = false,
                HostPowerState = AppleVirtualizationHostPowerState.WakeReconciliationRequired,
                SleepGeneration = 4,
                WakeGeneration = 17,
                RequiresWakeReconciliation = true,
                PowerObservedAt = DateTimeOffset.Parse("2026-07-30T22:00:00Z"),
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(
            json,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.HostStart);
        roundTrip.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.HostResponseSchema);
        roundTrip.HostLifecycleRequest.Should().NotBeNull();
        roundTrip.HostLifecycleRequest!.ExplicitRealMode.Should().BeTrue();
        roundTrip.HostLifecycleRequest.GracePeriodMilliseconds.Should().Be(250);
        roundTrip.HostLifecycleRequest.ObservedWakeGeneration.Should().Be(17);
        roundTrip.HostLifecycleRequest.VmConfigurationValidationRequest.Should().NotBeNull();
        roundTrip.HostLifecycleRequest.VmConfigurationValidationRequest!.GuestImage.KernelPath.Should().Be("/tmp/hpd-vz/vmlinuz");
        roundTrip.HostStatusResponse.Should().NotBeNull();
        roundTrip.HostStatusResponse!.HostPhase.Should().Be(RuntimeHostPhase.Starting);
        roundTrip.HostStatusResponse.GuestControlReachable.Should().BeFalse();
        roundTrip.HostStatusResponse.HostPowerState.Should().Be(
            AppleVirtualizationHostPowerState.WakeReconciliationRequired);
        roundTrip.HostStatusResponse.SleepGeneration.Should().Be(4);
        roundTrip.HostStatusResponse.WakeGeneration.Should().Be(17);
        roundTrip.HostStatusResponse.RequiresWakeReconciliation.Should().BeTrue();
    }

    [Fact]
    public void Guest_agent_transport_probe_round_trips_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.GuestAgentTransportProbe,
            "request-transport",
            sequenceNumber: 40,
            AppleVirtualizationHelperProtocol.GuestAgentTransportRequestSchema).ToResponse(sequenceNumber: 41) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.GuestAgentTransportResponseSchema,
            GuestAgentTransportProbeRequest = new AppleVirtualizationGuestAgentTransportProbeRequest
            {
                HostId = "host-transport",
                Endpoint = new AppleVirtualizationGuestAgentTransportEndpoint
                {
                    Kind = AppleVirtualizationGuestAgentTransportKind.VirtioSocket,
                    Port = 7_777,
                    Name = "hpd-guest-agent",
                },
                TimeoutMilliseconds = 250,
                ExplicitRealMode = true,
                RequireVmRunning = true,
                ScriptedStatus = AppleVirtualizationGuestAgentTransportState.Connected,
            },
            GuestAgentTransportProbeResponse = new AppleVirtualizationGuestAgentTransportProbeResponse
            {
                HostId = "host-transport",
                State = AppleVirtualizationGuestAgentTransportState.Connected,
                Endpoint = new AppleVirtualizationGuestAgentTransportEndpoint
                {
                    Kind = AppleVirtualizationGuestAgentTransportKind.VirtioSocket,
                    Port = 7_777,
                    Name = "hpd-guest-agent",
                },
                VmRunning = true,
                GuestReady = false,
                Reason = "FakeConnected",
                Message = "Transport connected; this is not guest readiness.",
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(
            json,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.GuestAgentTransportProbe);
        roundTrip.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.GuestAgentTransportResponseSchema);
        roundTrip.GuestAgentTransportProbeRequest.Should().NotBeNull();
        roundTrip.GuestAgentTransportProbeRequest!.HostId.Should().Be("host-transport");
        roundTrip.GuestAgentTransportProbeRequest.Endpoint.Port.Should().Be(7_777);
        roundTrip.GuestAgentTransportProbeRequest.ExplicitRealMode.Should().BeTrue();
        roundTrip.GuestAgentTransportProbeRequest.ScriptedStatus.Should().Be(AppleVirtualizationGuestAgentTransportState.Connected);
        roundTrip.GuestAgentTransportProbeResponse.Should().NotBeNull();
        roundTrip.GuestAgentTransportProbeResponse!.Connected.Should().BeTrue();
        roundTrip.GuestAgentTransportProbeResponse.GuestReady.Should().BeFalse();
    }

    [Fact]
    public void Guest_agent_readiness_probe_round_trips_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.GuestAgentReadinessProbe,
            "request-readiness",
            sequenceNumber: 42,
            AppleVirtualizationHelperProtocol.GuestAgentReadinessRequestSchema).ToResponse(sequenceNumber: 43) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.GuestAgentReadinessResponseSchema,
            GuestAgentReadinessProbeRequest = new AppleVirtualizationGuestAgentReadinessProbeRequest
            {
                HostId = "host-readiness",
                Endpoint = new AppleVirtualizationGuestAgentTransportEndpoint
                {
                    Kind = AppleVirtualizationGuestAgentTransportKind.VirtioSocket,
                    Port = 7_777,
                    Name = "hpd-guest-agent",
                },
                TimeoutMilliseconds = 250,
                ExplicitRealMode = true,
                ExpectedProtocolVersion = "1.0",
                ExpectedAgentVersion = "0.1.0-test",
                RequiredCapabilities = ["process.start", "projection.mount"],
                ScriptedState = AppleVirtualizationGuestAgentReadinessState.Ready,
            },
            GuestAgentReadinessProbeResponse = new AppleVirtualizationGuestAgentReadinessProbeResponse
            {
                HostId = "host-readiness",
                State = AppleVirtualizationGuestAgentReadinessState.Ready,
                TransportState = AppleVirtualizationGuestAgentTransportState.Connected,
                Endpoint = new AppleVirtualizationGuestAgentTransportEndpoint
                {
                    Kind = AppleVirtualizationGuestAgentTransportKind.VirtioSocket,
                    Port = 7_777,
                    Name = "hpd-guest-agent",
                },
                VmRunning = true,
                TransportConnected = true,
                VerifiedReady = true,
                ProtocolVersion = "1.0",
                AgentVersion = "0.1.0-test",
                GuestBootId = "guest-boot-1",
                GuestBootGeneration = 1,
                GuestAgentGeneration = 1,
                Capabilities = new HPD.Environment.AppleVirtualization.GuestAgent.AppleVirtualizationGuestAgentCapabilities(),
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(
            json,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.GuestAgentReadinessProbe);
        roundTrip.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.GuestAgentReadinessResponseSchema);
        roundTrip.GuestAgentReadinessProbeRequest.Should().NotBeNull();
        roundTrip.GuestAgentReadinessProbeRequest!.RequiredCapabilities.Should().Equal("process.start", "projection.mount");
        roundTrip.GuestAgentReadinessProbeRequest.ScriptedState.Should().Be(AppleVirtualizationGuestAgentReadinessState.Ready);
        roundTrip.GuestAgentReadinessProbeResponse.Should().NotBeNull();
        roundTrip.GuestAgentReadinessProbeResponse!.VerifiedReady.Should().BeTrue();
        roundTrip.GuestAgentReadinessProbeResponse.TransportConnected.Should().BeTrue();
        roundTrip.GuestAgentReadinessProbeResponse.GuestBootId.Should().Be("guest-boot-1");
        roundTrip.GuestAgentReadinessProbeResponse.Capabilities!.ProcessStart.Should().BeTrue();
    }

    [Fact]
    public void Helper_projection_lifecycle_bridge_dtos_round_trip_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProjectionObserve,
            "request-projection-observe",
            sequenceNumber: 44,
            AppleVirtualizationHelperProtocol.ProjectionRequestSchema).ToResponse(sequenceNumber: 45) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionResponseSchema,
            ProjectionMountRequest = new AppleVirtualizationProjectionMountRequest
            {
                ProjectionId = "projection-bridge",
                HostId = "host-bridge",
                HostPath = "/Users/test/workspace",
                Tag = "hpdprojectionbridge",
                GuestPath = "/workspace",
                AccessMode = AccessMode.ReadWrite,
                Realization = ProjectionRealizationKind.LiveProjection,
                RequestedWriteEffect = ProjectionWriteEffect.DirectSourceMutation,
                RequestedCoherence = CoherenceClass.CloseToOpen,
                ScriptedReadinessState = AppleVirtualizationGuestAgentReadinessState.Ready,
                ScriptedGuestProjectionState = AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
                Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(GuestBootId: "guest-boot-1", GuestBootGeneration: 1, GuestAgentGeneration: 1, ProjectionGeneration: 7),
            },
            ProjectionStatusRequest = new AppleVirtualizationProjectionStatusRequest
            {
                ProjectionId = "projection-bridge",
                HostId = "host-bridge",
                ExpectedGuestPath = "/workspace",
                ScriptedReadinessState = AppleVirtualizationGuestAgentReadinessState.Ready,
                ScriptedGuestProjectionState = AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            },
            ProjectionUnmountRequest = new AppleVirtualizationProjectionUnmountRequest
            {
                ProjectionId = "projection-bridge",
                HostId = "host-bridge",
                GuestPath = "/workspace",
                Force = true,
                Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(GuestBootId: "guest-boot-1", GuestBootGeneration: 1, GuestAgentGeneration: 1, ProjectionGeneration: 7),
            },
            ProjectionObserveRequest = new AppleVirtualizationProjectionObserveRequest
            {
                ProjectionId = "projection-bridge",
                HostId = "host-bridge",
                GuestPath = "/workspace",
                Recursive = false,
                AfterSequence = 9,
                Limit = 2,
                ScriptedReadinessState = AppleVirtualizationGuestAgentReadinessState.Ready,
                ScriptedGuestProjectionState = AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            },
            ProjectionStatusResponse = new AppleVirtualizationProjectionStatusResponse
            {
                ProjectionId = "projection-bridge",
                ProjectionPhase = ContentProjectionPhase.Projected,
                EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                EffectiveWriteEffect = ProjectionWriteEffect.DirectSourceMutation,
                EffectiveCoherence = CoherenceClass.CloseToOpen,
                GuestAgentReady = true,
                HostShareConfigured = true,
                FrameworkShareAccepted = true,
                VerifiedByGuestAgent = true,
                GuestProjectionStatus = new AppleVirtualizationGuestAgentProjectionStatus
                {
                    ProjectionId = "projection-bridge",
                    GuestPath = "/workspace",
                    Tag = "hpdprojectionbridge",
                    Mounted = true,
                    GuestMountVerified = true,
                    VerificationState = AppleVirtualizationGuestAgentProjectionVerificationState.ReadyForHpdUse,
                    ProjectionPhase = ContentProjectionPhase.Projected,
                    EffectiveWriteEffect = ProjectionWriteEffect.DirectSourceMutation,
                    EffectiveCoherence = CoherenceClass.CloseToOpen,
                },
                GuestProjectionUnmountResult = new AppleVirtualizationGuestAgentProjectionUnmountResult("projection-bridge", Unmounted: true, WasMounted: true),
                GuestProjectionObserveResult = new AppleVirtualizationGuestAgentProjectionObserveResult(
                    "projection-bridge",
                    new AppleVirtualizationGuestAgentProjectionStatus
                    {
                        ProjectionId = "projection-bridge",
                        GuestPath = "/workspace",
                        Tag = "hpdprojectionbridge",
                        Mounted = true,
                        GuestMountVerified = true,
                        VerificationState = AppleVirtualizationGuestAgentProjectionVerificationState.ReadyForHpdUse,
                        ProjectionPhase = ContentProjectionPhase.Projected,
                        EffectiveWriteEffect = ProjectionWriteEffect.DirectSourceMutation,
                        EffectiveCoherence = CoherenceClass.CloseToOpen,
                    },
                    Events: [],
                    HasMore: false),
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(
            json,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.ProjectionObserve);
        roundTrip.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.ProjectionResponseSchema);
        roundTrip.ProjectionMountRequest!.Tag.Should().Be("hpdprojectionbridge");
        roundTrip.ProjectionStatusRequest!.ExpectedGuestPath.Should().Be("/workspace");
        roundTrip.ProjectionUnmountRequest!.Force.Should().BeTrue();
        roundTrip.ProjectionObserveRequest!.AfterSequence.Should().Be(9);
        roundTrip.ProjectionStatusResponse.Should().NotBeNull();
        roundTrip.ProjectionStatusResponse!.ReadyForHpdUse.Should().BeTrue();
        roundTrip.ProjectionStatusResponse.GuestProjectionStatus!.ReadyForHpdUse.Should().BeTrue();
        roundTrip.ProcessStartRequest.Should().BeNull();
        roundTrip.ProcessResizeRequest.Should().BeNull();
    }

    [Fact]
    public void Helper_projection_sync_and_finalization_dtos_round_trip_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope sync = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProjectionSync,
            "request-projection-sync",
            sequenceNumber: 50,
            AppleVirtualizationHelperProtocol.ProjectionSyncRequestSchema).ToResponse(sequenceNumber: 51) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionSyncResponseSchema,
            ProjectionSyncRequest = new AppleVirtualizationProjectionSyncRequest
            {
                ProjectionId = "projection-sync",
                HostId = "host-sync",
                GuestPath = "/workspace",
                Mode = SyncMode.Manual,
                Direction = SyncDirection.TargetToSource,
                ConflictPolicy = ConflictPolicy.RecordConflict,
                DryRun = true,
                MaxChanges = 2,
                MaxConflicts = 1,
                Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(GuestBootId: "guest-boot-1", GuestBootGeneration: 1, GuestAgentGeneration: 1, ProjectionGeneration: 1),
                ScriptedReadinessState = AppleVirtualizationGuestAgentReadinessState.Ready,
                ScriptedGuestProjectionState = AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            },
            ProjectionSyncResult = new AppleVirtualizationGuestAgentProjectionSyncResult
            {
                ProjectionId = "projection-sync",
                State = AppleVirtualizationGuestAgentProjectionSyncState.DryRun,
                Succeeded = true,
                DryRun = true,
                CheckpointVersion = 0,
                ChangeSummary = new ContentProjectionChangeSummary(Created: 1, Modified: 1, Deleted: 0, Conflicted: 1, ManifestDigest: new Digest("sha256", "helper-sync-manifest")),
                Changes =
                [
                    new AppleVirtualizationGuestAgentProjectionChange(1, FileEventKind.Created, "/workspace/new.txt", new ByteSize(12), new Digest("sha256", "new"), Role: ContentProjectionRole.Workspace),
                ],
                Conflicts =
                [
                    new WorkspaceConflict("/workspace/conflict.txt", ConflictKind.ConcurrentWrite, "helper conflict"),
                ],
            },
        };

        AppleVirtualizationHelperEnvelope finalization = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProjectionFinalize,
            "request-projection-finalize",
            sequenceNumber: 52,
            AppleVirtualizationHelperProtocol.ProjectionFinalizationRequestSchema).ToResponse(sequenceNumber: 53) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionFinalizationResponseSchema,
            ProjectionFinalizationRequest = new AppleVirtualizationProjectionFinalizationRequest
            {
                ProjectionId = "projection-finalize",
                HostId = "host-finalize",
                GuestPath = "/workspace",
                Kind = FinalizationKind.ManifestAndChangedContent,
                ProducerId = "agent-62-test",
                MaxContentRefs = 2,
                MaxConflicts = 1,
                Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(GuestBootId: "guest-boot-1", GuestBootGeneration: 1, GuestAgentGeneration: 1, ProjectionGeneration: 1),
                ScriptedReadinessState = AppleVirtualizationGuestAgentReadinessState.Ready,
                ScriptedGuestProjectionState = AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            },
            ProjectionFinalizationResult = new AppleVirtualizationGuestAgentProjectionFinalizationResult
            {
                ProjectionId = "projection-finalize",
                State = AppleVirtualizationGuestAgentProjectionFinalizationState.Succeeded,
                Succeeded = true,
                ManifestDigest = new Digest("sha256", "helper-final-manifest"),
                Content =
                [
                    new FinalizedContentRef("/workspace/new.txt", "content-new", new Digest("sha256", "new"), new ByteSize(12), ContentProjectionRole.Workspace),
                ],
            },
        };

        foreach (AppleVirtualizationHelperEnvelope envelope in new[] { sync, finalization })
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                envelope,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
            AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(
                json,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

            roundTrip.Operation.Should().Be(envelope.Operation);
            roundTrip.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        }

        AppleVirtualizationHelperEnvelope syncRoundTrip = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(sync, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope),
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;
        syncRoundTrip.ProjectionSyncRequest!.DryRun.Should().BeTrue();
        syncRoundTrip.ProjectionSyncRequest.Generation.ProjectionGeneration.Should().Be(1);
        syncRoundTrip.ProjectionSyncResult!.ChangeSummary.ManifestDigest!.Value.Value.Should().Be("helper-sync-manifest");
        syncRoundTrip.ProjectionSyncResult.Changes.Should().ContainSingle(change => change.Path == "/workspace/new.txt");
        syncRoundTrip.ProjectionSyncResult.Conflicts.Should().ContainSingle(conflict => conflict.Kind == ConflictKind.ConcurrentWrite);

        AppleVirtualizationHelperEnvelope finalizationRoundTrip = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(finalization, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope),
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;
        finalizationRoundTrip.ProjectionFinalizationRequest!.ProducerId.Should().Be("agent-62-test");
        finalizationRoundTrip.ProjectionFinalizationResult!.ManifestDigest!.Value.Value.Should().Be("helper-final-manifest");
        finalizationRoundTrip.ProjectionFinalizationResult.Content.Should().ContainSingle(content => content.ContentId == "content-new");
    }

    [Fact]
    public void Helper_projection_change_enumeration_and_promotion_dtos_round_trip_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope enumeration = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProjectionEnumerateChanges,
            "request-projection-enumerate",
            sequenceNumber: 54,
            AppleVirtualizationHelperProtocol.ProjectionSyncRequestSchema).ToResponse(sequenceNumber: 55) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionSyncResponseSchema,
            ProjectionChangeEnumerationRequest = new AppleVirtualizationProjectionChangeEnumerationRequest
            {
                ProjectionId = "projection-enumerate",
                HostId = "host-enumerate",
                GuestPath = "/workspace",
                AfterSequence = 4,
                Limit = 2,
                Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(ProjectionGeneration: 1),
                ScriptedReadinessState = AppleVirtualizationGuestAgentReadinessState.Ready,
                ScriptedGuestProjectionState = AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            },
            ProjectionChangeEnumerationResult = new AppleVirtualizationGuestAgentProjectionChangeEnumerationResult
            {
                ProjectionId = "projection-enumerate",
                Changes =
                [
                    new AppleVirtualizationGuestAgentProjectionChange(5, FileEventKind.Modified, "/workspace/file.txt", new ByteSize(5)),
                ],
                NextSequence = 5,
                HasMore = true,
                Truncated = true,
            },
        };

        AppleVirtualizationHelperEnvelope promotion = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProjectionPromote,
            "request-projection-promote",
            sequenceNumber: 56,
            AppleVirtualizationHelperProtocol.ProjectionSyncRequestSchema).ToResponse(sequenceNumber: 57) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionSyncResponseSchema,
            ProjectionPromotionRequest = new AppleVirtualizationProjectionPromotionRequest
            {
                ProjectionId = "projection-promote",
                HostId = "host-promote",
                GuestPath = "/workspace",
                Direction = SyncDirection.TargetToSource,
                ConflictPolicy = ConflictPolicy.RequireExplicitPromotion,
                DryRun = true,
                Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(ProjectionGeneration: 1),
                ScriptedReadinessState = AppleVirtualizationGuestAgentReadinessState.Ready,
                ScriptedGuestProjectionState = AppleVirtualizationHelperProjectionScriptedGuestState.Verified,
            },
            ProjectionPromotionResult = new AppleVirtualizationGuestAgentProjectionPromotionResult
            {
                ProjectionId = "projection-promote",
                State = AppleVirtualizationGuestAgentProjectionPromotionState.DryRun,
                Succeeded = true,
                DryRun = true,
                ChangeSummary = new ContentProjectionChangeSummary(Created: 1),
            },
        };

        foreach (AppleVirtualizationHelperEnvelope envelope in new[] { enumeration, promotion })
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                envelope,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
            AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(
                json,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

            roundTrip.Operation.Should().Be(envelope.Operation);
        }

        AppleVirtualizationHelperEnvelope enumerationRoundTrip = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(enumeration, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope),
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;
        enumerationRoundTrip.ProjectionChangeEnumerationRequest!.AfterSequence.Should().Be(4);
        enumerationRoundTrip.ProjectionChangeEnumerationResult!.Changes.Should().ContainSingle(change => change.Sequence == 5);

        AppleVirtualizationHelperEnvelope promotionRoundTrip = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(promotion, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope),
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;
        promotionRoundTrip.ProjectionPromotionRequest!.ConflictPolicy.Should().Be(ConflictPolicy.RequireExplicitPromotion);
        promotionRoundTrip.ProjectionPromotionResult!.State.Should().Be(AppleVirtualizationGuestAgentProjectionPromotionState.DryRun);
    }

    [Fact]
    public void Preflight_facts_round_trip_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.PreflightRun,
            "request-preflight",
            sequenceNumber: 2,
            AppleVirtualizationHelperProtocol.PreflightResponseSchema).ToResponse(sequenceNumber: 3) with
        {
            PreflightRunResponse = new AppleVirtualizationPreflightRunResponse
            {
                Facts =
                [
                    new AppleVirtualizationPreflightFact
                    {
                        Name = "vm-boot-inputs",
                        State = AppleVirtualizationPreflightFactState.RequiresConfiguration,
                        Reason = "BootInputsMissing",
                        Message = "No boot inputs were provided.",
                        ObservedValue = "missing",
                        Severity = DiagnosticSeverity.Warning,
                    },
                    new AppleVirtualizationPreflightFact
                    {
                        Name = "helper-health-not-guest-readiness",
                        State = AppleVirtualizationPreflightFactState.Supported,
                        Reason = "ReadinessBoundaryPreserved",
                        Message = "Helper health is not HPD guest readiness.",
                        Severity = DiagnosticSeverity.Info,
                    },
                ],
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(
            json,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.PreflightRunResponse.Should().NotBeNull();
        roundTrip.PreflightRunResponse!.Facts.Should().Contain(fact =>
            fact.Name == "vm-boot-inputs" &&
            fact.State == AppleVirtualizationPreflightFactState.RequiresConfiguration &&
            fact.Reason == "BootInputsMissing" &&
            fact.Severity == DiagnosticSeverity.Warning);
        roundTrip.PreflightRunResponse.Facts.Should().Contain(fact =>
            fact.Name == "helper-health-not-guest-readiness" &&
            fact.State == AppleVirtualizationPreflightFactState.Supported);
    }

    [Fact]
    public void Protocol_version_mismatch_error_has_stable_code_and_operation()
    {
        AppleVirtualizationHelperError error = AppleVirtualizationHelperJsonCodec.ProtocolMismatch(
            "hello",
            requestedVersion: "1.0",
            helperVersion: "2.0");

        error.Code.Should().Be("AppleVirtualization.HelperProtocolMismatch");
        error.Operation.Should().Be("hello");
        error.Retryable.Should().BeFalse();
        error.FailedPhase.Should().Be("Activation");
    }

    [Fact]
    public async Task Fake_helper_returns_queued_response_and_records_request()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.Hello,
            RequestId = "request-3",
            SequenceNumber = 2,
            PayloadSchema = AppleVirtualizationHelperProtocol.HelloResponseSchema,
            HelloResponse = new AppleVirtualizationHelperHelloResponse
            {
                HelperVersion = "0.1.0",
                ProtocolVersion = AppleVirtualizationHelperProtocol.CurrentVersion,
                ProtocolCompatible = true,
                ProviderGeneration = 42,
                VirtualizationFrameworkAvailable = true,
                VirtualizationEntitlementVerified = true,
            },
        });

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(
            AppleVirtualizationHelperEnvelope.Request(AppleVirtualizationHelperOperation.Hello, "request-3", 1));

        helper.Requests.Should().ContainSingle(request => request.Operation == AppleVirtualizationHelperOperation.Hello);
        response.HelloResponse.Should().NotBeNull();
        response.HelloResponse!.ProtocolCompatible.Should().BeTrue();
        response.HelloResponse.ProviderGeneration.Should().Be(42);
    }

    [Fact]
    public async Task In_memory_transport_records_sent_frames_and_replays_incoming_frames()
    {
        await using var transport = new InMemoryAppleVirtualizationHelperTransport();
        AppleVirtualizationHelperEnvelope incoming = new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Event,
            Operation = AppleVirtualizationHelperOperation.HostStatus,
            EventKind = AppleVirtualizationHelperEventKind.VmRunning,
            EventId = "event-vm-running",
            SequenceNumber = 11,
        };
        transport.EnqueueIncoming(incoming);

        await transport.SendAsync(AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.HostStatus,
            "request-host-status",
            sequenceNumber: 10));

        var read = new List<AppleVirtualizationHelperEnvelope>();
        await foreach (AppleVirtualizationHelperEnvelope frame in transport.ReadAsync())
        {
            read.Add(frame);
        }

        transport.Sent.Should().ContainSingle(frame => frame.RequestId == "request-host-status");
        read.Should().ContainSingle(frame =>
            frame.EventKind == AppleVirtualizationHelperEventKind.VmRunning &&
            frame.EventId == "event-vm-running");
    }

    [Fact]
    public async Task Fake_helper_replays_queued_events_in_order()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueEvent(new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Event,
            Operation = AppleVirtualizationHelperOperation.ProcessStart,
            EventId = "event-1",
            SequenceNumber = 1,
        });
        helper.EnqueueEvent(new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Event,
            Operation = AppleVirtualizationHelperOperation.ProcessReadOutput,
            EventId = "event-2",
            SequenceNumber = 2,
        });

        var events = new List<AppleVirtualizationHelperEnvelope>();
        await foreach (AppleVirtualizationHelperEnvelope helperEvent in helper.ReadEventsAsync())
        {
            events.Add(helperEvent);
        }

        events.Should().HaveCount(2);
        events[0].EventId.Should().Be("event-1");
        events[1].EventId.Should().Be("event-2");
    }
}
