namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.ExecutionUnits;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Projections;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using Xunit;

public sealed class AppleVirtualizationCleanupFinalizationPolicyTests
{
    [Fact]
    public async Task Unit_stop_finalizes_OnExecutionUnitStop_projection_before_release()
    {
        CleanupFixture fixture = CreateFixture(FinalizationPolicy.OnExecutionUnitStop, CleanupPolicy.Default);
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(ProjectionFinalizationResponse("projection-1", new Digest("sha256", "unit-stop")));
        fixture.Helper.EnqueueResponse(OkResponse(AppleVirtualizationHelperOperation.ProjectionRelease));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Stopped));
        ExecutionUnitStatus unit = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), fixture.UnitSpec, null);

        ExecutionUnitStatus stopped = await fixture.UnitProvider.StopAsync(unit.Handle!.Value, StopPolicy.Default);

        stopped.UnitPhase.Should().Be(ExecutionUnitPhase.Stopped);
        fixture.Helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.ProjectionFinalize,
            AppleVirtualizationHelperOperation.ProjectionRelease,
            AppleVirtualizationHelperOperation.UnitStop);
        fixture.Ledger.TryGetContentProjection(ProjectionRef()).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Required_finalization_blocks_release_when_cleanup_policy_fails_operation()
    {
        CleanupFixture fixture = CreateFixture(FinalizationPolicy.Required, CleanupPolicy.Default with
        {
            FailureMode = CleanupFailureMode.FailOperation,
        });
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(ProjectionFinalizationResponse(
            "projection-1",
            manifestDigest: null,
            succeeded: false,
            state: AppleVirtualizationGuestAgentProjectionFinalizationState.UnsupportedKind));
        ExecutionUnitStatus unit = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), fixture.UnitSpec, null);

        ExecutionUnitStatus stopped = await fixture.UnitProvider.StopAsync(unit.Handle!.Value, StopPolicy.Default);

        stopped.Phase.Should().Be(ResourcePhase.Degraded);
        stopped.UnitPhase.Should().Be(ExecutionUnitPhase.Stopping);
        stopped.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.ExecutionUnitCleanupFailed");
        fixture.Helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.ProjectionFinalize);
        fixture.Ledger.TryGetContentProjection(ProjectionRef()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task MarkDegradedAndRetain_keeps_projection_when_required_finalization_fails()
    {
        CleanupFixture fixture = CreateFixture(FinalizationPolicy.Required, CleanupPolicy.Default with
        {
            FailureMode = CleanupFailureMode.MarkDegradedAndRetain,
        });
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(ProjectionFinalizationResponse(
            "projection-1",
            manifestDigest: null,
            succeeded: false,
            state: AppleVirtualizationGuestAgentProjectionFinalizationState.UnsupportedKind));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Stopped));
        ExecutionUnitStatus unit = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), fixture.UnitSpec, null);

        ExecutionUnitStatus stopped = await fixture.UnitProvider.StopAsync(unit.Handle!.Value, StopPolicy.Default);

        stopped.UnitPhase.Should().Be(ExecutionUnitPhase.Stopped);
        ContentProjectionStatus retained = fixture.Ledger.TryGetContentProjection(ProjectionRef()).Entry!.Status;
        retained.Phase.Should().Be(ResourcePhase.Degraded);
        retained.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.ProjectionFinalizationRequiredFailed");
        fixture.Helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.ProjectionFinalize,
            AppleVirtualizationHelperOperation.UnitStop);
    }

    [Fact]
    public async Task BestEffortRelease_records_failure_and_releases_projection()
    {
        CleanupFixture fixture = CreateFixture(FinalizationPolicy.Required, CleanupPolicy.Default with
        {
            FailureMode = CleanupFailureMode.BestEffortRelease,
        });
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(ProjectionFinalizationResponse(
            "projection-1",
            manifestDigest: null,
            succeeded: false,
            state: AppleVirtualizationGuestAgentProjectionFinalizationState.UnsupportedKind));
        fixture.Helper.EnqueueResponse(OkResponse(AppleVirtualizationHelperOperation.ProjectionRelease));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Stopped));
        ExecutionUnitStatus unit = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), fixture.UnitSpec, null);

        ExecutionUnitStatus stopped = await fixture.UnitProvider.StopAsync(unit.Handle!.Value, StopPolicy.Default);

        stopped.UnitPhase.Should().Be(ExecutionUnitPhase.Stopped);
        fixture.Ledger.TryGetContentProjection(ProjectionRef()).Succeeded.Should().BeFalse();
        fixture.Helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.ProjectionFinalize,
            AppleVirtualizationHelperOperation.ProjectionRelease,
            AppleVirtualizationHelperOperation.UnitStop);
    }

    [Fact]
    public async Task PromoteExplicitly_is_not_auto_promoted_by_cleanup()
    {
        CleanupFixture fixture = CreateFixture(FinalizationPolicy.PromoteExplicitly, CleanupPolicy.Default with
        {
            FailureMode = CleanupFailureMode.MarkDegradedAndRetain,
        });
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Stopped));
        ExecutionUnitStatus unit = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), fixture.UnitSpec, null);

        ExecutionUnitStatus stopped = await fixture.UnitProvider.StopAsync(unit.Handle!.Value, StopPolicy.Default);

        stopped.UnitPhase.Should().Be(ExecutionUnitPhase.Stopped);
        fixture.Ledger.TryGetContentProjection(ProjectionRef()).Entry!.Status.Diagnostics
            .Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.ProjectionRetainedDuringCleanup");
        fixture.Helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.UnitStop);
    }

    [Fact]
    public async Task Runtime_finalization_aggregates_OnRuntimeEnd_projection_results()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new AppleVirtualizationProviderModule(
            new AppleVirtualizationProviderOptions { HelperTransportMode = AppleVirtualizationHelperTransportMode.InMemoryFake },
            helper,
            ledger));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        SeedProjectedProjection(ledger, ProjectionSpec(FinalizationPolicy.OnRuntimeEnd));
        helper.EnqueueResponse(ProjectionFinalizationResponse("projection-1", new Digest("sha256", "runtime-final")));

        RuntimeFinalizationResult result = await runtime.FinalizeRuntimeAsync(
            new RuntimeFinalizationRequest(AppleVirtualizationContractFixtures.RuntimeScope, PromoteMemory: false, CleanupPolicy.Default));

        result.ContentProjections.Should().ContainSingle()
            .Which.ManifestDigest.Should().Be(new Digest("sha256", "runtime-final"));
        result.Conflicts.Should().BeEmpty();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "hpd.execution.runtime.finalized");
        helper.Requests.Should().ContainSingle(request => request.Operation == AppleVirtualizationHelperOperation.ProjectionFinalize);
    }

    private static CleanupFixture CreateFixture(FinalizationPolicy finalizationPolicy, CleanupPolicy cleanupPolicy)
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        var projectionProvider = new AppleVirtualizationContentProjectionProvider(helper, ledger);
        SeedHost(ledger);
        ContentProjectionSpec projectionSpec = ProjectionSpec(finalizationPolicy);
        SeedProjectedProjection(ledger, projectionSpec);

        ExecutionUnitSpec unitSpec = AppleVirtualizationContractFixtures.ExecutionUnitSpec() with
        {
            LifecyclePolicy = LifecyclePolicy.Default with { Cleanup = cleanupPolicy },
        };

        return new CleanupFixture(
            ledger,
            helper,
            new AppleVirtualizationExecutionUnitProvider(ledger, helper, projectionProvider),
            unitSpec);
    }

    private static ResourceMetadata<ExecutionUnit> Metadata(string id) =>
        AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>(id, "execution-unit");

    private static ResourceRef<ContentProjection> ProjectionRef() =>
        AppleVirtualizationContractFixtures.ContentProjectionRef();

    private static ContentProjectionSpec ProjectionSpec(FinalizationPolicy finalizationPolicy) =>
        AppleVirtualizationContractFixtures.ReadOnlyWorkspaceProjection() with
        {
            FinalizationPolicy = finalizationPolicy,
        };

    private static void SeedHost(AppleVirtualizationProviderStateLedger ledger)
    {
        ResourceMetadata<RuntimeHost> metadata =
            AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("runtime-host-1", "runtime-host");
        ledger.UpsertRuntimeHost(metadata, new RuntimeHostStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            HostPhase = RuntimeHostPhase.Ready,
            GuestControl = new GuestControlStatus(
                Expected: true,
                Installed: true,
                Reachable: true,
                Transport: ProviderTransportKind.Vsock),
            Readiness = new RuntimeHostReadinessStatus(Ready: true),
        });
    }

    private static void SeedProjectedProjection(
        AppleVirtualizationProviderStateLedger ledger,
        ContentProjectionSpec spec)
    {
        ResourceMetadata<ContentProjection> metadata =
            AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-1", "content-projection");
        ledger.UpsertContentProjection(metadata, new ContentProjectionStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            ProjectionPhase = ContentProjectionPhase.Projected,
            Conditions =
            [
                new Condition(
                    AppleVirtualizationContentProjectionProvider.GuestMountVerifiedCondition,
                    ConditionStatus.True,
                    "GuestVerified",
                    "Test projection is guest verified.",
                    DateTimeOffset.UtcNow,
                    metadata.Generation),
            ],
            Views =
            [
                new RealizedProjectionView
                {
                    Kind = ProjectionViewKind.FilesystemTree,
                    GuestPath = new GuestPath("/workspace"),
                    EffectiveAccess = AccessMode.ReadOnly,
                    EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                    EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
                    EffectiveCoherence = CoherenceClass.CloseToOpen,
                },
            ],
        }, spec);
    }

    private static AppleVirtualizationHelperEnvelope UnitResponse(ExecutionUnitPhase phase) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.UnitEnsure,
            RequestId = "unit-response",
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            UnitStatusResponse = new AppleVirtualizationUnitStatusResponse
            {
                UnitId = "unit-response",
                UnitPhase = phase,
                WorkingDirectory = "/workspace",
            },
        };

    private static AppleVirtualizationHelperEnvelope OkResponse(AppleVirtualizationHelperOperation operation) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            RequestId = "ok-response",
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
        };

    private static AppleVirtualizationHelperEnvelope ProjectionFinalizationResponse(
        string projectionId,
        Digest? manifestDigest,
        bool succeeded = true,
        AppleVirtualizationGuestAgentProjectionFinalizationState state = AppleVirtualizationGuestAgentProjectionFinalizationState.Succeeded)
    {
        return new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.ProjectionFinalize,
            SequenceNumber = 11,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionFinalizationResponseSchema,
            ProjectionFinalizationResult = new AppleVirtualizationGuestAgentProjectionFinalizationResult
            {
                ProjectionId = projectionId,
                State = state,
                Succeeded = succeeded,
                CompletedAt = succeeded ? DateTimeOffset.UtcNow : DateTimeOffset.UnixEpoch,
                ManifestDigest = manifestDigest,
                Content = succeeded
                    ? [new FinalizedContentRef("/workspace/changed.txt", "content-1", new Digest("sha256", "content"), new ByteSize(7), ContentProjectionRole.Workspace)]
                    : Array.Empty<FinalizedContentRef>(),
                Conditions =
                [
                    new Condition(
                        AppleVirtualizationContentProjectionProvider.FinalizationFailedCondition,
                        succeeded ? ConditionStatus.True : ConditionStatus.False,
                        succeeded ? "Completed" : state.ToString(),
                        succeeded ? "Projection finalization completed." : "Projection finalization failed.",
                        DateTimeOffset.UtcNow,
                        default,
                        succeeded ? DiagnosticSeverity.Info : DiagnosticSeverity.Error),
                ],
            },
        };
    }

    private sealed record CleanupFixture(
        AppleVirtualizationProviderStateLedger Ledger,
        FakeAppleVirtualizationHelperClient Helper,
        AppleVirtualizationExecutionUnitProvider UnitProvider,
        ExecutionUnitSpec UnitSpec);
}
