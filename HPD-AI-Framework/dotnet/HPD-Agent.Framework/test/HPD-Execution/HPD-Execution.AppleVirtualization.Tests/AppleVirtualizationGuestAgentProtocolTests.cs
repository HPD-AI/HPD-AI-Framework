namespace HPD.Execution.AppleVirtualization.Tests;

using System.Text.Json;
using FluentAssertions;
using HPD.Execution.AppleVirtualization.GuestAgent;
using HPD.Execution.AppleVirtualization.Processes;
using HPD.Execution.AppleVirtualization.Protocol;
using HPD.Execution.AppleVirtualization.State;
using HPD.Execution.AppleVirtualization.Tests.Fixtures;
using HPD.Execution.Contracts;
using HPD.Execution.Runtime;
using Xunit;

public sealed class AppleVirtualizationGuestAgentProtocolTests
{
    [Fact]
    public void Guest_hello_and_ready_round_trip_through_source_generated_json()
    {
        var envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.Hello,
            "guest-hello-1",
            1,
            AppleVirtualizationGuestAgentProtocol.HelloSchema) with
        {
            ResponseStatus = AppleVirtualizationGuestAgentResponseStatus.Ok,
            GuestBootId = "boot-abc",
            GuestBootGeneration = 7,
            GuestAgentGeneration = 11,
            Hello = new AppleVirtualizationGuestAgentHello
            {
                AgentVersion = "0.1.0-test",
                ProtocolVersion = AppleVirtualizationGuestAgentProtocol.CurrentVersion,
                GuestBootId = "boot-abc",
                GuestBootGeneration = 7,
                GuestAgentGeneration = 11,
                Hostname = "hpd-guest",
                RuntimeUser = "hpd",
                Capabilities = new AppleVirtualizationGuestAgentCapabilities
                {
                    Pty = true,
                    ProcessResize = true,
                    ProjectionObserve = true,
                    Limitations = ["test-limit"],
                },
            },
            Ready = new AppleVirtualizationGuestAgentReady
            {
                IsReady = true,
                GuestBootId = "boot-abc",
                GuestBootGeneration = 7,
                GuestAgentGeneration = 11,
                Conditions = [Condition("AppleVirtualization.GuestAgentReady", ConditionStatus.True, "Ready")],
            },
        };

        string json = JsonSerializer.Serialize(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentEnvelope);
        AppleVirtualizationGuestAgentEnvelope roundTrip = JsonSerializer.Deserialize(
            json,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentEnvelope)!;

        roundTrip.Hello!.AgentVersion.Should().Be("0.1.0-test");
        roundTrip.Hello.GuestBootId.Should().Be("boot-abc");
        roundTrip.Hello.GuestBootGeneration.Should().Be(7);
        roundTrip.Hello.GuestAgentGeneration.Should().Be(11);
        roundTrip.Hello.Capabilities.ProcessResize.Should().BeTrue();
        roundTrip.Ready!.IsReady.Should().BeTrue();
        roundTrip.Ready.Conditions.Should().Contain(condition => condition.Type == "AppleVirtualization.GuestAgentReady");
    }

    [Fact]
    public void Guest_network_status_dtos_round_trip_through_source_generated_json()
    {
        var address = new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000002);
        var envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.NetworkStatus,
            "guest-network-1",
            41,
            AppleVirtualizationGuestAgentProtocol.NetworkSchema) with
        {
            NetworkStatusRequest = new AppleVirtualizationGuestAgentNetworkStatusRequest
            {
                HostId = "host-network",
                UnitId = "unit-network",
                IncludeRoutes = true,
                IncludeListeners = true,
                MaxInterfaces = 2,
                MaxRoutes = 4,
                MaxListeners = 8,
            },
            NetworkStatus = new AppleVirtualizationGuestAgentNetworkStatus
            {
                HostId = "host-network",
                UnitId = "unit-network",
                GuestAgentReady = true,
                Interfaces =
                [
                    new AppleVirtualizationGuestAgentNetworkInterfaceStatus
                    {
                        Name = "en0",
                        Mtu = 1500,
                        MacAddress = new MacAddressValue(0x020000000001),
                        IsUp = true,
                        Addresses = [new NetworkAddressAssignment(address, 24, AddressAssignmentKind.ProviderAssigned, IsPrimary: true)],
                    },
                ],
                Routes =
                [
                    new AppleVirtualizationGuestAgentNetworkRouteObservation
                    {
                        Gateway = new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000001),
                        InterfaceName = "en0",
                        IsDefault = true,
                    },
                ],
                Listeners =
                [
                    new AppleVirtualizationGuestAgentNetworkListenerObservation
                    {
                        Name = "guest-observed-tcp-listener",
                        Transport = NetworkTransport.Tcp,
                        Address = address,
                        Port = new NetworkPort(8080),
                        GuestVisibleOnly = true,
                        HpdPublished = false,
                    },
                ],
                Generation = new AppleVirtualizationGuestAgentNetworkGenerationStamp(
                    ProviderGeneration: 1,
                    HostStartGeneration: 2,
                    GuestBootId: "guest-boot-1",
                    GuestBootGeneration: 3,
                    GuestAgentGeneration: 4),
                Limitations =
                [
                    new NetworkLimitation(NetworkDegradedFeature.StaticMacAddress, CapabilityDegradationMode.DisabledByPolicy, "AppleVirtualization.StaticMacDeferred"),
                ],
            },
        };

        AppleVirtualizationGuestAgentEnvelope roundTrip = RoundTrip(envelope);

        roundTrip.Operation.Should().Be(AppleVirtualizationGuestAgentOperation.NetworkStatus);
        roundTrip.PayloadSchema.Should().Be(AppleVirtualizationGuestAgentProtocol.NetworkSchema);
        roundTrip.NetworkStatusRequest!.HostId.Should().Be("host-network");
        roundTrip.NetworkStatus!.Interfaces.Should().ContainSingle(networkInterface => networkInterface.Name == "en0" && networkInterface.IsUp);
        roundTrip.NetworkStatus.Routes.Should().ContainSingle(route => route.IsDefault);
        roundTrip.NetworkStatus.Listeners.Should().ContainSingle(listener =>
            listener.Transport == NetworkTransport.Tcp &&
            listener.GuestVisibleOnly &&
            !listener.HpdPublished);
        roundTrip.NetworkStatus.Generation.GuestBootId.Should().Be("guest-boot-1");
        roundTrip.NetworkStatus.Limitations.Should().Contain(limitation =>
            limitation.Feature == NetworkDegradedFeature.StaticMacAddress &&
            limitation.Mode == CapabilityDegradationMode.DisabledByPolicy);
    }

    [Fact]
    public async Task Fake_guest_agent_network_status_bounds_results_without_claiming_publication()
    {
        var toolharness = new FakeAppleVirtualizationGuestAgentToolHarness();
        var request = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.NetworkStatus,
            "guest-network-fake",
            42,
            AppleVirtualizationGuestAgentProtocol.NetworkSchema) with
        {
            NetworkStatusRequest = new AppleVirtualizationGuestAgentNetworkStatusRequest
            {
                HostId = "host-network",
                IncludeRoutes = false,
                IncludeListeners = true,
                MaxInterfaces = 1,
                MaxRoutes = 0,
                MaxListeners = 1,
            },
        };

        AppleVirtualizationGuestAgentEnvelope response = await toolharness.SendAsync(request);

        response.NetworkStatus.Should().NotBeNull();
        response.NetworkStatus!.Interfaces.Should().ContainSingle();
        response.NetworkStatus.Routes.Should().BeEmpty();
        response.NetworkStatus.Listeners.Should().ContainSingle(listener =>
            listener.GuestVisibleOnly &&
            !listener.HpdPublished);
        response.NetworkStatus.RoutesTruncated.Should().BeFalse();
        response.NetworkStatus.ListenersTruncated.Should().BeFalse();
    }

    [Fact]
    public void Guest_projection_mount_and_status_round_trip()
    {
        var envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.ProjectionMount,
            "projection-mount-1",
            2,
            AppleVirtualizationGuestAgentProtocol.ProjectionSchema) with
        {
            ProjectionId = "projection-1",
            ProjectionMountRequest = new AppleVirtualizationGuestAgentProjectionMountRequest
            {
                ProjectionId = "projection-1",
                Tag = "hpdprojection1",
                GuestPath = "/hpd/projections/projection-1",
                AccessMode = AccessMode.ReadOnly,
            },
            ProjectionStatus = new AppleVirtualizationGuestAgentProjectionStatus
            {
                ProjectionId = "projection-1",
                Tag = "hpdprojection1",
                GuestPath = "/hpd/projections/projection-1",
                Mounted = true,
                GuestMountVerified = true,
                ProjectionPhase = ContentProjectionPhase.Projected,
                EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
                EffectiveCoherence = CoherenceClass.CloseToOpen,
                Conditions = [Condition("AppleVirtualization.GuestMountVerified", ConditionStatus.True, "Mounted")],
            },
        };

        AppleVirtualizationGuestAgentEnvelope roundTrip = RoundTrip(envelope);

        roundTrip.ProjectionMountRequest!.Tag.Should().Be("hpdprojection1");
        roundTrip.ProjectionMountRequest.GuestPath.Should().Be("/hpd/projections/projection-1");
        roundTrip.ProjectionStatus!.GuestMountVerified.Should().BeTrue();
        roundTrip.ProjectionStatus.ProjectionPhase.Should().Be(ContentProjectionPhase.Projected);
        roundTrip.ProjectionStatus.Conditions.Should().Contain(condition => condition.Type == "AppleVirtualization.GuestMountVerified");
    }

    [Fact]
    public void Guest_projection_contract_distinguishes_configured_framework_and_guest_verified_states()
    {
        var envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.ProjectionMount,
            "projection-mount-2",
            22,
            AppleVirtualizationGuestAgentProtocol.ProjectionSchema) with
        {
            ProjectionId = "projection-2",
            ProjectionMountRequest = new AppleVirtualizationGuestAgentProjectionMountRequest
            {
                ProjectionId = "projection-2",
                Tag = "hpdprojection2",
                GuestPath = "/workspace",
                AccessMode = AccessMode.ReadWrite,
                Identity = new AppleVirtualizationGuestAgentProjectionIdentity("projection-2", HostId: "host-1", UnitId: "unit-1"),
                HostSource = new AppleVirtualizationGuestAgentProjectionHostSourceIdentity("/Users/test/project", "hpdprojection2", HostShareConfigured: true, FrameworkShareAccepted: true),
                ExpectedGuestPath = new AppleVirtualizationGuestAgentProjectionGuestPathExpectation("/workspace"),
                RequestedRealization = ProjectionRealizationKind.LiveProjection,
                RequestedWriteEffect = ProjectionWriteEffect.DirectSourceMutation,
                RequestedCoherence = CoherenceClass.CloseToOpen,
                Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(
                    ProviderGeneration: 9,
                    HostStartGeneration: 10,
                    GuestBootId: "boot-2",
                    GuestBootGeneration: 11,
                    GuestAgentGeneration: 12,
                    ProjectionGeneration: 13),
            },
            ProjectionMountResult = new AppleVirtualizationGuestAgentProjectionMountResult
            {
                Status = new AppleVirtualizationGuestAgentProjectionStatus
                {
                    ProjectionId = "projection-2",
                    Tag = "hpdprojection2",
                    GuestPath = "/workspace",
                    Mounted = true,
                    GuestMountVerified = true,
                    HostShareState = AppleVirtualizationGuestAgentProjectionHostShareState.HostShareConfigured,
                    FrameworkShareState = AppleVirtualizationGuestAgentProjectionFrameworkShareState.Accepted,
                    VerificationState = AppleVirtualizationGuestAgentProjectionVerificationState.ReadyForHpdUse,
                    ExpectedGuestPath = "/workspace",
                    ActualGuestPath = "/workspace",
                    RequestedAccessMode = AccessMode.ReadWrite,
                    EffectiveAccessMode = AccessMode.ReadWrite,
                    ProjectionPhase = ContentProjectionPhase.Projected,
                    EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                    EffectiveWriteEffect = ProjectionWriteEffect.DirectSourceMutation,
                    EffectiveCoherence = CoherenceClass.CloseToOpen,
                    EffectiveCache = CacheBehavior.None,
                    Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(GuestBootId: "boot-2", GuestBootGeneration: 11, GuestAgentGeneration: 12, ProjectionGeneration: 13),
                },
            },
        };

        AppleVirtualizationGuestAgentEnvelope roundTrip = RoundTrip(envelope);

        roundTrip.ProjectionMountRequest!.HostSource!.HostShareConfigured.Should().BeTrue();
        roundTrip.ProjectionMountRequest.HostSource.FrameworkShareAccepted.Should().BeTrue();
        roundTrip.ProjectionMountRequest.ExpectedGuestPath!.ExpectedGuestPath.Should().Be("/workspace");
        roundTrip.ProjectionMountRequest.Generation.GuestBootId.Should().Be("boot-2");
        roundTrip.ProjectionMountResult!.Status.HostShareState.Should().Be(AppleVirtualizationGuestAgentProjectionHostShareState.HostShareConfigured);
        roundTrip.ProjectionMountResult.Status.FrameworkShareState.Should().Be(AppleVirtualizationGuestAgentProjectionFrameworkShareState.Accepted);
        roundTrip.ProjectionMountResult.Status.VerificationState.Should().Be(AppleVirtualizationGuestAgentProjectionVerificationState.ReadyForHpdUse);
        roundTrip.ProjectionMountResult.Status.ReadyForHpdUse.Should().BeTrue();
        roundTrip.ProjectionMountResult.Status.EffectiveAccessMode.Should().Be(AccessMode.ReadWrite);
    }

    [Fact]
    public void Guest_projection_status_request_unmount_and_observe_round_trip_without_process_or_terminal_payloads()
    {
        var envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.ProjectionObserve,
            "projection-observe-1",
            23,
            AppleVirtualizationGuestAgentProtocol.ProjectionSchema) with
        {
            ProjectionId = "projection-3",
            ProjectionStatusRequest = new AppleVirtualizationGuestAgentProjectionStatusRequest("projection-3", "/workspace"),
            ProjectionUnmountRequest = new AppleVirtualizationGuestAgentProjectionUnmountRequest("projection-3", "/workspace", Force: true),
            ProjectionUnmountResult = new AppleVirtualizationGuestAgentProjectionUnmountResult("projection-3", Unmounted: true, WasMounted: true),
            ProjectionObserveRequest = new AppleVirtualizationGuestAgentProjectionObserveRequest("projection-3", "/workspace", Recursive: false, AfterSequence: 7, Limit: 4),
            ProjectionObserveResult = new AppleVirtualizationGuestAgentProjectionObserveResult(
                "projection-3",
                new AppleVirtualizationGuestAgentProjectionStatus
                {
                    ProjectionId = "projection-3",
                    Tag = "hpdprojection3",
                    GuestPath = "/workspace",
                    Mounted = false,
                    GuestMountVerified = false,
                    VerificationState = AppleVirtualizationGuestAgentProjectionVerificationState.MountPathMissing,
                    ProjectionPhase = ContentProjectionPhase.Degraded,
                    EffectiveWriteEffect = ProjectionWriteEffect.Unknown,
                },
                Events: [],
                HasMore: false),
        };

        AppleVirtualizationGuestAgentEnvelope roundTrip = RoundTrip(envelope);

        roundTrip.ProjectionStatusRequest!.ExpectedGuestPath.Should().Be("/workspace");
        roundTrip.ProjectionUnmountResult!.Unmounted.Should().BeTrue();
        roundTrip.ProjectionObserveRequest!.AfterSequence.Should().Be(7);
        roundTrip.ProjectionObserveResult!.Status.VerificationState.Should().Be(AppleVirtualizationGuestAgentProjectionVerificationState.MountPathMissing);
        roundTrip.ProcessStartRequest.Should().BeNull();
        roundTrip.ProcessResizeRequest.Should().BeNull();
    }

    [Fact]
    public void Guest_projection_sync_dtos_round_trip_through_source_generated_json()
    {
        var envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.ProjectionSync,
            "projection-sync-1",
            24,
            AppleVirtualizationGuestAgentProtocol.ProjectionSyncSchema) with
        {
            ProjectionId = "projection-sync",
            ProjectionSyncRequest = new AppleVirtualizationGuestAgentProjectionSyncRequest
            {
                ProjectionId = "projection-sync",
                GuestPath = "/workspace",
                Mode = SyncMode.Manual,
                Direction = SyncDirection.TargetToSource,
                ConflictPolicy = ConflictPolicy.RecordConflict,
                DryRun = true,
                MaxChanges = 2,
                MaxConflicts = 1,
                Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(GuestBootId: "boot-sync", GuestBootGeneration: 3, GuestAgentGeneration: 4, ProjectionGeneration: 5),
            },
            ProjectionSyncResult = new AppleVirtualizationGuestAgentProjectionSyncResult
            {
                ProjectionId = "projection-sync",
                State = AppleVirtualizationGuestAgentProjectionSyncState.DryRun,
                Succeeded = true,
                DryRun = true,
                CheckpointVersion = 0,
                ChangeSummary = new ContentProjectionChangeSummary(Created: 1, Modified: 2, Deleted: 1, Conflicted: 1, ManifestDigest: new Digest("sha256", "manifest-sync")),
                Changes =
                [
                    new AppleVirtualizationGuestAgentProjectionChange(1, FileEventKind.Created, "/workspace/new.txt", new ByteSize(7), new Digest("sha256", "new"), DateTimeOffset.UtcNow),
                ],
                Conflicts = [new WorkspaceConflict("/workspace/conflict.txt", ConflictKind.ConcurrentWrite, "changed in both places")],
                Conditions = [Condition("AppleVirtualization.ProjectionSyncDryRun", ConditionStatus.True, "DryRun")],
            },
        };

        AppleVirtualizationGuestAgentEnvelope roundTrip = RoundTrip(envelope);

        roundTrip.ProjectionSyncRequest!.ProjectionId.Should().Be("projection-sync");
        roundTrip.ProjectionSyncRequest.DryRun.Should().BeTrue();
        roundTrip.ProjectionSyncRequest.Generation.GuestBootId.Should().Be("boot-sync");
        roundTrip.ProjectionSyncResult!.State.Should().Be(AppleVirtualizationGuestAgentProjectionSyncState.DryRun);
        roundTrip.ProjectionSyncResult.ChangeSummary.Created.Should().Be(1);
        roundTrip.ProjectionSyncResult.ChangeSummary.ManifestDigest!.Value.Value.Should().Be("manifest-sync");
        roundTrip.ProjectionSyncResult.Changes.Should().ContainSingle(change => change.Path == "/workspace/new.txt");
        roundTrip.ProjectionSyncResult.Conflicts.Should().ContainSingle(conflict => conflict.Kind == ConflictKind.ConcurrentWrite);
    }

    [Fact]
    public void Guest_projection_finalization_change_enumeration_and_promotion_dtos_round_trip()
    {
        var envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.ProjectionFinalize,
            "projection-finalize-1",
            25,
            AppleVirtualizationGuestAgentProtocol.ProjectionFinalizationSchema) with
        {
            ProjectionId = "projection-finalize",
            ProjectionFinalizationRequest = new AppleVirtualizationGuestAgentProjectionFinalizationRequest
            {
                ProjectionId = "projection-finalize",
                GuestPath = "/workspace",
                Kind = FinalizationKind.ManifestAndChangedContent,
                IncludeDeletedEntries = true,
                ProducerId = "agent-61-test",
                MaxContentRefs = 4,
                MaxConflicts = 2,
                Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(ProjectionGeneration: 6),
            },
            ProjectionFinalizationResult = new AppleVirtualizationGuestAgentProjectionFinalizationResult
            {
                ProjectionId = "projection-finalize",
                State = AppleVirtualizationGuestAgentProjectionFinalizationState.Succeeded,
                Succeeded = true,
                ManifestDigest = new Digest("sha256", "manifest-final"),
                Content =
                [
                    new FinalizedContentRef("/workspace/out.txt", "content-1", new Digest("sha256", "out"), new ByteSize(9), ContentProjectionRole.Workspace),
                ],
                Conflicts = [new WorkspaceConflict("/workspace/conflict.txt", ConflictKind.ProviderConflict, "provider conflict")],
            },
            ProjectionChangeEnumerationRequest = new AppleVirtualizationGuestAgentProjectionChangeEnumerationRequest
            {
                ProjectionId = "projection-finalize",
                GuestPath = "/workspace",
                AfterSequence = 7,
                Limit = 8,
            },
            ProjectionChangeEnumerationResult = new AppleVirtualizationGuestAgentProjectionChangeEnumerationResult
            {
                ProjectionId = "projection-finalize",
                Changes = [new AppleVirtualizationGuestAgentProjectionChange(8, FileEventKind.Deleted, "/workspace/deleted.txt", Deleted: true)],
                NextSequence = 9,
                HasMore = false,
            },
            ProjectionPromotionRequest = new AppleVirtualizationGuestAgentProjectionPromotionRequest
            {
                ProjectionId = "projection-finalize",
                GuestPath = "/workspace",
                Direction = SyncDirection.TargetToSource,
                ConflictPolicy = ConflictPolicy.RequireExplicitPromotion,
                DryRun = false,
            },
            ProjectionPromotionResult = new AppleVirtualizationGuestAgentProjectionPromotionResult
            {
                ProjectionId = "projection-finalize",
                State = AppleVirtualizationGuestAgentProjectionPromotionState.Succeeded,
                Succeeded = true,
                ChangeSummary = new ContentProjectionChangeSummary(Modified: 1),
            },
        };

        AppleVirtualizationGuestAgentEnvelope roundTrip = RoundTrip(envelope);

        roundTrip.ProjectionFinalizationRequest!.ProducerId.Should().Be("agent-61-test");
        roundTrip.ProjectionFinalizationResult!.ManifestDigest!.Value.Value.Should().Be("manifest-final");
        roundTrip.ProjectionFinalizationResult.Content.Should().ContainSingle(content => content.ContentId == "content-1");
        roundTrip.ProjectionChangeEnumerationRequest!.AfterSequence.Should().Be(7);
        roundTrip.ProjectionChangeEnumerationResult!.Changes.Should().ContainSingle(change => change.Deleted);
        roundTrip.ProjectionPromotionRequest!.ConflictPolicy.Should().Be(ConflictPolicy.RequireExplicitPromotion);
        roundTrip.ProjectionPromotionResult!.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Fake_guest_agent_sync_and_finalization_require_verified_projection_and_current_generation()
    {
        var missing = new FakeAppleVirtualizationGuestAgentToolHarness();
        AppleVirtualizationGuestAgentEnvelope missingProjection = await missing.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionSync) with
        {
            ProjectionSyncRequest = SyncRequest("projection-missing", generation: 0),
        });
        missingProjection.ResponseStatus.Should().Be(AppleVirtualizationGuestAgentResponseStatus.Error);
        missingProjection.Error!.Code.Should().Be("AppleVirtualization.ProjectionNotVerified");

        var stale = new FakeAppleVirtualizationGuestAgentToolHarness()
            .WithProjectionMount("projection-stale", "hpdstale", "/workspace", verified: true);
        _ = await stale.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionMount));
        AppleVirtualizationGuestAgentEnvelope staleProjection = await stale.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionSync) with
        {
            ProjectionSyncRequest = SyncRequest("projection-stale", generation: 99),
        });
        staleProjection.ResponseStatus.Should().Be(AppleVirtualizationGuestAgentResponseStatus.Error);
        staleProjection.Error!.Code.Should().Be("AppleVirtualization.ProjectionStaleGeneration");
    }

    [Fact]
    public async Task Fake_guest_agent_returns_structured_unsupported_sync_mode_and_finalization_kind()
    {
        var toolharness = new FakeAppleVirtualizationGuestAgentToolHarness()
            .WithProjectionMount("projection-unsupported", "hpdu", "/workspace", verified: true);
        _ = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionMount));

        AppleVirtualizationGuestAgentEnvelope unsupportedSync = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionSync) with
        {
            ProjectionSyncRequest = SyncRequest("projection-unsupported", generation: 1) with
            {
                Mode = SyncMode.Continuous,
            },
        });
        unsupportedSync.ResponseStatus.Should().NotBe(AppleVirtualizationGuestAgentResponseStatus.Error);
        unsupportedSync.ProjectionSyncResult!.Succeeded.Should().BeFalse();
        unsupportedSync.ProjectionSyncResult.State.Should().Be(AppleVirtualizationGuestAgentProjectionSyncState.UnsupportedMode);

        AppleVirtualizationGuestAgentEnvelope unsupportedFinalization = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionFinalize) with
        {
            ProjectionFinalizationRequest = new AppleVirtualizationGuestAgentProjectionFinalizationRequest
            {
                ProjectionId = "projection-unsupported",
                GuestPath = "/workspace",
                Kind = FinalizationKind.PublishArtifacts,
                Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(ProjectionGeneration: 1),
            },
        });
        unsupportedFinalization.ResponseStatus.Should().NotBe(AppleVirtualizationGuestAgentResponseStatus.Error);
        unsupportedFinalization.ProjectionFinalizationResult!.Succeeded.Should().BeFalse();
        unsupportedFinalization.ProjectionFinalizationResult.State.Should().Be(AppleVirtualizationGuestAgentProjectionFinalizationState.UnsupportedKind);
    }

    [Fact]
    public async Task Fake_guest_agent_sync_finalization_toolharness_round_trips_bounded_results()
    {
        AppleVirtualizationGuestAgentProjectionChange[] changes =
        [
            new(1, FileEventKind.Created, "/workspace/a.txt"),
            new(2, FileEventKind.Modified, "/workspace/b.txt"),
            new(3, FileEventKind.Deleted, "/workspace/c.txt", Deleted: true),
        ];
        WorkspaceConflict[] conflicts =
        [
            new("/workspace/conflict-1.txt", ConflictKind.ConcurrentWrite),
            new("/workspace/conflict-2.txt", ConflictKind.ProviderConflict),
        ];
        FinalizedContentRef[] content =
        [
            new("/workspace/a.txt", "content-a", null, new ByteSize(1), ContentProjectionRole.Workspace),
            new("/workspace/b.txt", "content-b", null, new ByteSize(2), ContentProjectionRole.Workspace),
        ];

        var toolharness = new FakeAppleVirtualizationGuestAgentToolHarness()
            .WithProjectionMount("projection-bounded", "hpdbounded", "/workspace", verified: true)
            .WithProjectionSync(
                "projection-bounded",
                new ContentProjectionChangeSummary(Created: 1, Modified: 1, Deleted: 1, Conflicted: 2),
                changes,
                conflicts,
                maxChanges: 2,
                maxConflicts: 1)
            .WithProjectionFinalization(
                "projection-bounded",
                new Digest("sha256", "manifest-bounded"),
                content,
                conflicts,
                maxContentRefs: 1,
                maxConflicts: 1);
        _ = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionMount));

        AppleVirtualizationGuestAgentEnvelope sync = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionSync) with
        {
            ProjectionSyncRequest = SyncRequest("projection-bounded", generation: 1),
        });
        AppleVirtualizationGuestAgentEnvelope finalization = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionFinalize) with
        {
            ProjectionFinalizationRequest = new AppleVirtualizationGuestAgentProjectionFinalizationRequest
            {
                ProjectionId = "projection-bounded",
                GuestPath = "/workspace",
                Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(ProjectionGeneration: 1),
            },
        });

        sync.ProjectionSyncResult!.Succeeded.Should().BeTrue();
        sync.ProjectionSyncResult.Changes.Should().HaveCount(2);
        sync.ProjectionSyncResult.ChangesTruncated.Should().BeTrue();
        sync.ProjectionSyncResult.Conflicts.Should().ContainSingle();
        sync.ProjectionSyncResult.ConflictsTruncated.Should().BeTrue();
        finalization.ProjectionFinalizationResult!.Succeeded.Should().BeTrue();
        finalization.ProjectionFinalizationResult.Content.Should().ContainSingle(contentRef => contentRef.ContentId == "content-a");
        finalization.ProjectionFinalizationResult.ContentTruncated.Should().BeTrue();
        finalization.ProjectionFinalizationResult.ConflictsTruncated.Should().BeTrue();
    }

    [Fact]
    public void Guest_process_start_wait_and_result_round_trip()
    {
        var envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.ProcessStart,
            "process-start-1",
            3,
            AppleVirtualizationGuestAgentProtocol.ProcessSchema) with
        {
            ProcessId = "process-1",
            UnitId = "unit-1",
            ProcessStartRequest = new AppleVirtualizationGuestAgentProcessStartRequest
            {
                ProcessId = "process-1",
                UnitId = "unit-1",
                Command = new ProcessCommandSpec
                {
                    FileName = "/bin/echo",
                    Arguments = ["hello"],
                    WorkingDirectory = "/workspace",
                    Environment = new Dictionary<string, string?>
                    {
                        ["HPD_TEST"] = "1",
                    },
                },
                Identity = new ProcessIdentitySpec(User: "hpd", Group: "hpd", SupplementalGroups: ["staff"]),
                Limits = new ProcessLimitSpec(ProcessCount: 4, MemoryBytes: 1024 * 1024, CpuTime: TimeSpan.FromSeconds(3)),
                RequireVerifiedProjection = true,
                RequiredProjectionId = "projection-1",
                RequiredProjectionGuestPath = "/workspace",
                Io = ProcessIoSpec.Default with
                {
                    Terminal = new TerminalSpec(100, 30),
                },
                ObservationRetention = ObservationRetentionPolicy.EventsAndResult,
                Terminal = new AppleVirtualizationGuestAgentTerminalState
                {
                    PtyState = AppleVirtualizationGuestAgentPtyState.Requested,
                    Size = new TerminalSpec(100, 30),
                    ResizeSupported = false,
                    ResizeUnsupportedReason = "L8 does not support terminal resize.",
                },
                Generation = new AppleVirtualizationGuestAgentProcessGenerationStamp(
                    ProviderGeneration: 1,
                    HostStartGeneration: 2,
                    GuestBootId: "boot-1",
                    GuestBootGeneration: 3,
                    GuestAgentGeneration: 4,
                    ProcessGeneration: 5),
            },
            ProcessStatusRequest = new AppleVirtualizationGuestAgentProcessStatusRequest("process-1", IncludeResult: true),
            ProcessStatus = new AppleVirtualizationGuestAgentProcessStatus
            {
                ProcessId = "process-1",
                ProcessPhase = ProcessInvocationPhase.Running,
                IoState = ProcessIoState.Open,
                ProviderProcessId = "guest-process-1",
                SystemProcessId = 42,
            },
            ProcessWaitRequest = new AppleVirtualizationGuestAgentProcessWaitRequest("process-1", TimeSpan.FromSeconds(5)),
            ProcessResult = new AppleVirtualizationGuestAgentProcessResult
            {
                ProcessId = "process-1",
                ProviderProcessId = "guest-process-1",
                SystemProcessId = 42,
                ExitCode = 0,
                CompletionKind = ProcessCompletionKind.Exited,
                StdoutCapture = new AppleVirtualizationGuestAgentCaptureAccounting
                {
                    BytesObserved = 5,
                    BytesCaptured = 5,
                    MaxCapturedBytes = 1024,
                },
                OutputDrainTimeout = TimeSpan.FromSeconds(2),
                Generation = new AppleVirtualizationGuestAgentProcessGenerationStamp(GuestBootId: "boot-1", GuestBootGeneration: 3, GuestAgentGeneration: 4, ProcessGeneration: 5),
            },
        };

        AppleVirtualizationGuestAgentEnvelope roundTrip = RoundTrip(envelope);

        roundTrip.ProcessStartRequest!.Command.FileName.Should().Be("/bin/echo");
        roundTrip.ProcessStartRequest.Command.Arguments.Should().Equal("hello");
        roundTrip.ProcessStartRequest.Command.Environment["HPD_TEST"].Should().Be("1");
        roundTrip.ProcessStartRequest.Identity!.User.Should().Be("hpd");
        roundTrip.ProcessStartRequest.Limits!.ProcessCount.Should().Be(4);
        roundTrip.ProcessStartRequest.RequireVerifiedProjection.Should().BeTrue();
        roundTrip.ProcessStartRequest.RequiredProjectionGuestPath.Should().Be("/workspace");
        roundTrip.ProcessStartRequest.Terminal.PtyState.Should().Be(AppleVirtualizationGuestAgentPtyState.Requested);
        roundTrip.ProcessStartRequest.Terminal.ResizeSupported.Should().BeFalse();
        roundTrip.ProcessStartRequest.Generation.GuestBootId.Should().Be("boot-1");
        roundTrip.ProcessStatusRequest!.IncludeResult.Should().BeTrue();
        roundTrip.ProcessStatus!.ProcessPhase.Should().Be(ProcessInvocationPhase.Running);
        roundTrip.ProcessWaitRequest!.Timeout.Should().Be(TimeSpan.FromSeconds(5));
        roundTrip.ProcessResult!.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        roundTrip.ProcessResult.StdoutCapture.BytesObserved.Should().Be(5);
        roundTrip.ProcessResult.Generation.ProcessGeneration.Should().Be(5);
    }

    [Fact]
    public void Guest_process_stdin_close_signal_and_stop_round_trip()
    {
        var bytes = new byte[] { 0x48, 0x50, 0x44 };
        var envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.ProcessStdin,
            "process-stdin-1",
            30,
            AppleVirtualizationGuestAgentProtocol.ProcessSchema) with
        {
            ProcessId = "process-stdin",
            ProcessStdinRequest = new AppleVirtualizationGuestAgentProcessStdinRequest
            {
                ProcessId = "process-stdin",
                Bytes = bytes,
                CloseAfterWrite = true,
                Sequence = 7,
            },
            ProcessCloseStdinRequest = new AppleVirtualizationGuestAgentProcessCloseStdinRequest("process-stdin", "inline-complete"),
            ProcessSignalRequest = new AppleVirtualizationGuestAgentProcessSignalRequest("process-stdin", new ProcessSignal("SIGTERM")),
            ProcessStopRequest = new AppleVirtualizationGuestAgentProcessStopRequest("process-stdin", StopKind.GracefulThenKill, TimeSpan.FromSeconds(1), "test-stop"),
            ProcessControlResult = new AppleVirtualizationGuestAgentProcessControlResult("process-stdin", Accepted: true, ProcessInvocationPhase.Stopping),
        };

        AppleVirtualizationGuestAgentEnvelope roundTrip = RoundTrip(envelope);

        roundTrip.ProcessStdinRequest!.Bytes.ToArray().Should().Equal(bytes);
        roundTrip.ProcessStdinRequest.CloseAfterWrite.Should().BeTrue();
        roundTrip.ProcessStdinRequest.Sequence.Should().Be(7);
        roundTrip.ProcessCloseStdinRequest!.Reason.Should().Be("inline-complete");
        roundTrip.ProcessSignalRequest!.Signal.Name.Should().Be("SIGTERM");
        roundTrip.ProcessStopRequest!.Kind.Should().Be(StopKind.GracefulThenKill);
        roundTrip.ProcessControlResult!.Accepted.Should().BeTrue();
        roundTrip.ProcessControlResult.ProcessPhase.Should().Be(ProcessInvocationPhase.Stopping);
    }

    [Fact]
    public void Guest_process_output_chunk_preserves_bytes_stream_sequence_flags_and_capture_counts()
    {
        var bytes = new byte[] { 0x00, 0x48, 0x50, 0x44, 0xff };
        var chunk = new AppleVirtualizationGuestAgentProcessOutputChunk
        {
            ProcessId = "process-1",
            Stream = ProcessOutputStream.Stderr,
            Sequence = 99,
            ObservedAt = new DateTimeOffset(2026, 5, 21, 12, 0, 0, TimeSpan.Zero),
            Bytes = bytes,
            Flags = ProcessOutputChunkFlags.Final | ProcessOutputChunkFlags.Truncated,
            Capture = new AppleVirtualizationGuestAgentCaptureAccounting
            {
                BytesObserved = 5,
                BytesCaptured = 3,
                BytesDiscarded = 2,
                Truncated = true,
                MaxCapturedBytes = 3,
            },
        };
        var envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.ProcessReadOutput,
            "process-output-1",
            4,
            AppleVirtualizationGuestAgentProtocol.ProcessOutputSchema) with
        {
            MessageType = AppleVirtualizationGuestAgentMessageType.Event,
            EventKind = AppleVirtualizationGuestAgentEventKind.ProcessOutput,
            ProcessReadOutputRequest = new AppleVirtualizationGuestAgentProcessReadOutputRequest("process-1", AfterSequence: 98, Limit: 1, Follow: true),
            ProcessOutputReadResult = new AppleVirtualizationGuestAgentProcessOutputReadResult
            {
                ProcessId = "process-1",
                Chunks = [chunk],
                NextSequence = 100,
                HasMore = false,
                FollowActive = true,
            },
            ProcessOutputChunk = chunk,
        };

        AppleVirtualizationGuestAgentEnvelope roundTrip = RoundTrip(envelope);

        roundTrip.ProcessReadOutputRequest!.AfterSequence.Should().Be(98);
        roundTrip.ProcessOutputReadResult!.Chunks.Should().ContainSingle();
        roundTrip.ProcessOutputReadResult.NextSequence.Should().Be(100);
        roundTrip.ProcessOutputReadResult.FollowActive.Should().BeTrue();
        roundTrip.ProcessOutputChunk!.Bytes.ToArray().Should().Equal(bytes);
        roundTrip.ProcessOutputChunk.Stream.Should().Be(ProcessOutputStream.Stderr);
        roundTrip.ProcessOutputChunk.Sequence.Should().Be(99);
        roundTrip.ProcessOutputChunk.Flags.Should().HaveFlag(ProcessOutputChunkFlags.Final);
        roundTrip.ProcessOutputChunk.Flags.Should().HaveFlag(ProcessOutputChunkFlags.Truncated);
        roundTrip.ProcessOutputChunk.Capture.BytesObserved.Should().Be(5);
        roundTrip.ProcessOutputChunk.Capture.BytesCaptured.Should().Be(3);
        roundTrip.ProcessOutputChunk.Capture.BytesDiscarded.Should().Be(2);
    }

    [Fact]
    public async Task Guest_process_resize_dto_exists_but_provider_support_remains_disabled()
    {
        var resize = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.ProcessResize,
            "process-resize-1",
            5,
            AppleVirtualizationGuestAgentProtocol.ProcessSchema) with
        {
            ProcessResizeRequest = new AppleVirtualizationGuestAgentProcessResizeRequest
            {
                ProcessId = "process-1",
                Size = new TerminalSpec(120, 40),
            },
        };

        RoundTrip(resize).ProcessResizeRequest!.Size.Should().Be(new TerminalSpec(120, 40));

        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = ledger.UpsertExecutionUnit(
            AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>("unit-1", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                UnitPhase = ExecutionUnitPhase.Ready,
                LastTransitionAt = DateTimeOffset.UtcNow,
            });
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.ProcessStart,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProcessStatusResponse = new AppleVirtualizationProcessStatusResponse
            {
                ProcessId = "process-1",
                ProcessPhase = ProcessInvocationPhase.Running,
                IoState = ProcessIoState.Open,
            },
        });
        var provider = new AppleVirtualizationProcessProvider(ledger, helper);
        IProcessInvocationHandle handle = await provider.StartAsync(AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.TargetHandle));

        Func<Task> act = async () => await provider.ResizeTerminalAsync(handle.Handle, new TerminalSpec(120, 40));

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("AppleVirtualization.ProcessResizeUnsupported:*");
        helper.Requests.Should().NotContain(request => request.Operation == AppleVirtualizationHelperOperation.ProcessResize);
    }

    [Fact]
    public async Task Fake_guest_agent_toolharness_scripts_readiness_projection_and_process_output_deterministically()
    {
        var toolharness = new FakeAppleVirtualizationGuestAgentToolHarness()
            .WithHandshake()
            .WithReady()
            .WithProjectionMount("projection-1", "hpdprojection1", "/workspace", verified: true)
            .WithProcessStarted("process-1", "unit-1")
            .WithProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { 1, 2, 3 }, final: false, maxCapturedBytes: 4)
            .WithProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { 4, 5 }, final: true, maxCapturedBytes: 4)
            .WithProcessExited("process-1", exitCode: 0);

        AppleVirtualizationGuestAgentEnvelope hello = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.Hello));
        AppleVirtualizationGuestAgentEnvelope ready = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.Ready));
        AppleVirtualizationGuestAgentEnvelope projection = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionMount));
        AppleVirtualizationGuestAgentEnvelope started = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProcessStart));
        AppleVirtualizationGuestAgentEnvelope result = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProcessWait));
        List<AppleVirtualizationGuestAgentEnvelope> events = [];
        await foreach (AppleVirtualizationGuestAgentEnvelope guestEvent in toolharness.ReadEventsAsync())
        {
            events.Add(guestEvent);
        }

        hello.Hello!.GuestBootId.Should().Be("guest-boot-1");
        ready.Ready!.IsReady.Should().BeTrue();
        projection.ProjectionStatus!.GuestMountVerified.Should().BeTrue();
        started.ProcessStarted!.ProviderProcessId.Should().Be("guest-process-1");
        events.Select(e => e.ProcessOutputChunk!.Sequence).Should().BeInAscendingOrder();
        events[0].ProcessOutputChunk!.Capture.BytesCaptured.Should().Be(3);
        events[1].ProcessOutputChunk!.Capture.BytesObserved.Should().Be(5);
        events[1].ProcessOutputChunk.Capture.BytesDiscarded.Should().Be(1);
        result.ProcessResult!.ExitCode.Should().Be(0);
        result.ProcessResult.StdoutCapture.BytesObserved.Should().Be(5);
    }

    [Fact]
    public async Task Fake_guest_agent_toolharness_scripts_projection_visibility_access_and_coherence_states()
    {
        var toolharness = new FakeAppleVirtualizationGuestAgentToolHarness()
            .WithProjectionConfiguredOnly("projection-configured", "hpdconfigured", "/workspace")
            .WithProjectionAccessMismatch("projection-access", "hpdaccess", "/workspace", AccessMode.ReadWrite, AccessMode.ReadOnly)
            .WithProjectionCoherence("projection-coherence", "hpdcoherence", "/workspace", CoherenceClass.Unknown);

        AppleVirtualizationGuestAgentEnvelope configured = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionStatus));
        AppleVirtualizationGuestAgentEnvelope access = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionStatus));
        AppleVirtualizationGuestAgentEnvelope coherence = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProjectionStatus));

        configured.ProjectionStatus!.HostShareState.Should().Be(AppleVirtualizationGuestAgentProjectionHostShareState.HostShareConfigured);
        configured.ProjectionStatus.FrameworkShareState.Should().Be(AppleVirtualizationGuestAgentProjectionFrameworkShareState.Unknown);
        configured.ProjectionStatus.VerificationState.Should().Be(AppleVirtualizationGuestAgentProjectionVerificationState.HostShareConfigured);
        configured.ProjectionStatus.ReadyForHpdUse.Should().BeFalse();

        access.ProjectionStatus!.VerificationState.Should().Be(AppleVirtualizationGuestAgentProjectionVerificationState.AccessMismatch);
        access.ProjectionStatus.RequestedAccessMode.Should().Be(AccessMode.ReadWrite);
        access.ProjectionStatus.EffectiveAccessMode.Should().Be(AccessMode.ReadOnly);
        access.ProjectionStatus.Limitations.Should().Contain(limitation => limitation.Feature == ContentProjectionDegradedFeature.ReadOnlyEnforcement);
        access.ProjectionStatus.ReadyForHpdUse.Should().BeFalse();

        coherence.ProjectionStatus!.VerificationState.Should().Be(AppleVirtualizationGuestAgentProjectionVerificationState.CoherenceUnknown);
        coherence.ProjectionStatus.EffectiveCoherence.Should().Be(CoherenceClass.Unknown);
        coherence.ProjectionStatus.Limitations.Should().Contain(limitation => limitation.Feature == ContentProjectionDegradedFeature.Coherence);
        coherence.ProjectionStatus.ReadyForHpdUse.Should().BeFalse();
    }

    [Fact]
    public async Task Fake_guest_agent_toolharness_scripts_process_status_failed_and_control_results()
    {
        var toolharness = new FakeAppleVirtualizationGuestAgentToolHarness()
            .WithProcessStatus("process-status", ProcessInvocationPhase.Running)
            .WithProcessControlResult(AppleVirtualizationGuestAgentOperation.ProcessSignal, "process-status")
            .WithProcessControlResult(AppleVirtualizationGuestAgentOperation.ProcessCloseStdin, "process-status")
            .WithProcessFailed("process-status", ProcessCompletionKind.Faulted);

        AppleVirtualizationGuestAgentEnvelope status = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProcessStatus));
        AppleVirtualizationGuestAgentEnvelope signal = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProcessSignal));
        AppleVirtualizationGuestAgentEnvelope closeStdin = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProcessCloseStdin));
        AppleVirtualizationGuestAgentEnvelope failed = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProcessWait));

        status.ProcessStatus!.ProcessPhase.Should().Be(ProcessInvocationPhase.Running);
        signal.ProcessControlResult!.Accepted.Should().BeTrue();
        closeStdin.ProcessControlResult!.Accepted.Should().BeTrue();
        failed.ProcessStatus!.ProcessPhase.Should().Be(ProcessInvocationPhase.Failed);
        failed.ProcessResult!.CompletionKind.Should().Be(ProcessCompletionKind.Faulted);
        failed.ProcessResult.Diagnostics.Should().Contain(condition => condition.Type == "AppleVirtualization.GuestProcessFailed");
    }

    [Fact]
    public async Task Malformed_guest_frame_is_structured_and_does_not_imply_helper_crash()
    {
        AppleVirtualizationGuestAgentFrameResult malformed = AppleVirtualizationGuestAgentJsonCodec.DecodeFrame("not-json"u8);
        malformed.IsMalformed.Should().BeTrue();
        malformed.Error!.Code.Should().Be("AppleVirtualization.GuestAgentMalformedFrame");

        var toolharness = new FakeAppleVirtualizationGuestAgentToolHarness()
            .WithMalformedFrame("not-json"u8.ToArray())
            .WithReady();

        AppleVirtualizationGuestAgentEnvelope error = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.Health));
        AppleVirtualizationGuestAgentEnvelope ready = await toolharness.SendAsync(Request(AppleVirtualizationGuestAgentOperation.Ready));

        error.ResponseStatus.Should().Be(AppleVirtualizationGuestAgentResponseStatus.Error);
        error.Error!.Code.Should().Be("AppleVirtualization.GuestAgentMalformedFrame");
        ready.Ready!.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task Fake_guest_agent_resize_is_structured_unsupported_unless_pty_support_is_scripted()
    {
        var unsupported = new FakeAppleVirtualizationGuestAgentToolHarness().WithHandshake(ptyResizeSupported: false);
        AppleVirtualizationGuestAgentEnvelope response = await unsupported.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProcessResize));

        response.ResponseStatus.Should().Be(AppleVirtualizationGuestAgentResponseStatus.Error);
        response.Error!.Code.Should().Be("AppleVirtualization.GuestAgentUnsupported");

        var supported = new FakeAppleVirtualizationGuestAgentToolHarness().WithHandshake(ptyResizeSupported: true);
        AppleVirtualizationGuestAgentEnvelope ok = await supported.SendAsync(Request(AppleVirtualizationGuestAgentOperation.ProcessResize));

        ok.ResponseStatus.Should().NotBe(AppleVirtualizationGuestAgentResponseStatus.Error);
    }

    [Fact]
    public void Provider_json_registry_includes_guest_agent_dtos()
    {
        var registry = new ExecutionProviderRegistry();

        registry.RegisterAppleVirtualizationProvider();

        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentEnvelope));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentHello));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentProjectionStatus));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentProcessOutputChunk));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentProcessResult));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentProcessStatus));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentProcessOutputReadResult));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentProcessControlResult));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentProjectionSyncRequest));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentProjectionSyncResult));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentProjectionFinalizationRequest));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentProjectionFinalizationResult));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentProjectionChange));
        registry.JsonTypes.Select(registration => registration.TypeDiscriminator).Should().OnlyHaveUniqueItems();
    }

    private static AppleVirtualizationGuestAgentEnvelope RoundTrip(AppleVirtualizationGuestAgentEnvelope envelope)
    {
        byte[] json = AppleVirtualizationGuestAgentJsonCodec.Encode(envelope);
        return AppleVirtualizationGuestAgentJsonCodec.Decode(json);
    }

    private static AppleVirtualizationGuestAgentEnvelope Request(AppleVirtualizationGuestAgentOperation operation) =>
        AppleVirtualizationGuestAgentEnvelope.Request(operation, "request-" + operation, 1);

    private static AppleVirtualizationGuestAgentProjectionSyncRequest SyncRequest(string projectionId, ulong generation) =>
        new()
        {
            ProjectionId = projectionId,
            GuestPath = "/workspace",
            Mode = SyncMode.Manual,
            Direction = SyncDirection.TargetToSource,
            ConflictPolicy = ConflictPolicy.RecordConflict,
            Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(ProjectionGeneration: generation),
        };

    private static Condition Condition(string type, ConditionStatus status, string reason) =>
        new(type, status, reason, reason, DateTimeOffset.UtcNow, default, DiagnosticSeverity.Info);
}
