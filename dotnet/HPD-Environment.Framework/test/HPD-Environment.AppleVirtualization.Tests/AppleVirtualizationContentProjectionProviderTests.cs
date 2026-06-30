namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Projections;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationContentProjectionProviderTests : IDisposable
{
    private readonly string _hostPath;

    public AppleVirtualizationContentProjectionProviderTests()
    {
        _hostPath = Path.Combine(Path.GetTempPath(), "hpd-applevz-projection-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_hostPath);
    }

    [Fact]
    public async Task Read_only_host_path_projection_requires_guest_mount_verification()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-read-only", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionMount, metadata.Id.Value, ContentProjectionPhase.Projected));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projecting);
        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.Views.Should().ContainSingle();
        status.Views[0].EffectiveRealization.Should().Be(ProjectionRealizationKind.LiveProjection);
        status.Views[0].EffectiveWriteEffect.Should().Be(ProjectionWriteEffect.NoWrites);
        status.Views[0].ReadOnlyEnforcement!.Enforced.Should().BeTrue();
        status.Conditions.Should().Contain(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.GuestMountVerifiedCondition &&
            condition.Status == ConditionStatus.False);
        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.ProjectionConfigure,
            AppleVirtualizationHelperOperation.ProjectionMount);
        ledger.TryGetContentProjection(new ResourceRef<ContentProjection>(metadata.Id, metadata.Scope, metadata.Generation)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Read_only_host_path_projection_reports_projected_after_guest_mount_verification()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-verified", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projected,
            GuestMountVerified(metadata.Generation)));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projected);
        status.Phase.Should().Be(ResourcePhase.Ready);
        status.ProviderHandle.Should().NotBeNull();
        status.Views[0].ProviderHandle.Should().Be(status.ProviderHandle);
        status.Views[0].EffectiveCache.Should().Be(CacheBehavior.ReadCache);
        status.Views[0].EffectiveCoherence.Should().Be(CoherenceClass.CloseToOpen);
    }

    [Fact]
    public async Task Unit_target_projection_is_recorded_on_owning_unit_after_guest_verification()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedUnit(ledger);
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-unit-owned", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projected,
            GuestMountVerified(metadata.Generation)));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit.TargetHandle, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projected);
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.RealizedContentProjections
            .Should().ContainSingle().Which.Id.Value.Should().Be("projection-unit-owned");
    }

    [Fact]
    public async Task Host_level_projection_is_not_recorded_as_unit_owned()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedUnit(ledger);
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-host-shared", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projected,
            GuestMountVerified(metadata.Generation)));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projected);
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.RealizedContentProjections.Should().BeEmpty();
    }

    [Fact]
    public async Task Stale_unit_projection_handle_fails_without_helper_dispatch()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedUnit(ledger);
        ledger.AdvanceProviderGeneration();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-stale-unit", "content-projection");

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, ProjectionSpec(AccessMode.ReadOnly), host, unit.TargetHandle, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Failed);
        status.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(AppleVirtualizationHandleDiagnostics.StaleHandle);
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Read_write_projection_requires_explicit_direct_source_mutation_policy()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-rw", "content-projection");
        ContentProjectionSpec denied = ProjectionSpec(AccessMode.ReadWrite);

        ContentProjectionStatus deniedStatus = await provider.ProjectAsync(metadata, denied, host, unit: null, CancellationToken.None);

        deniedStatus.ProjectionPhase.Should().Be(ContentProjectionPhase.Failed);
        deniedStatus.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == AppleVirtualizationContentProjectionProvider.DirectHostMutationDenied);
        helper.Requests.Should().BeEmpty();

        ContentProjectionSpec allowed = denied with
        {
            SecurityPolicy = denied.SecurityPolicy with
            {
                AllowDirectSourceMutation = true,
            },
        };
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projected,
            GuestProjectionStatus(
                metadata.Id.Value,
                allowed,
                ContentProjectionPhase.Projected,
                readyForHpdUse: true,
                effectiveAccess: AccessMode.ReadWrite,
                writeEffect: ProjectionWriteEffect.DirectSourceMutation),
            guestAgentReady: true,
            verifiedByGuestAgent: true,
            writeEffect: ProjectionWriteEffect.DirectSourceMutation));

        ContentProjectionStatus allowedStatus = await provider.ProjectAsync(metadata, allowed, host, unit: null, CancellationToken.None);

        allowedStatus.ProjectionPhase.Should().Be(ContentProjectionPhase.Projected);
        allowedStatus.Views[0].EffectiveWriteEffect.Should().Be(ProjectionWriteEffect.DirectSourceMutation);
        allowedStatus.Views[0].ReadOnlyEnforcement!.Enforced.Should().BeFalse();
    }

    [Fact]
    public async Task Unsupported_access_mode_reports_copy_fallback_when_allowed()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-fallback", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.CopyOnWrite) with
        {
            Realization = new ProjectionRealizationSpec
            {
                Kind = ProjectionRealizationKind.LiveProjection,
                Fallback = new ProjectionFallbackPolicy
                {
                    AllowFallback = true,
                    PreferredFallback = ProjectionRealizationKind.CopyIn,
                },
            },
        };

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Degraded);
        status.Views[0].EffectiveRealization.Should().Be(ProjectionRealizationKind.CopyIn);
        status.Views[0].Fallback!.Used.Should().BeTrue();
        status.Views[0].Limitations.Should().ContainSingle(limitation =>
            limitation.Feature == ContentProjectionDegradedFeature.LiveProjection);
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Helper_projection_failure_maps_to_degraded_status_and_limitation()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-degraded", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly) with
        {
            Realization = new ProjectionRealizationSpec
            {
                Kind = ProjectionRealizationKind.LiveProjection,
                RequestedCoherence = CoherenceClass.CloseToOpen,
                Cache = CacheBehavior.Unknown,
            },
        };
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projecting,
            GuestMountPending(metadata.Generation),
            CoherenceClass.ProviderDefined,
            CacheBehavior.ProviderDefined));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.Views[0].Limitations.Should().Contain(limitation =>
            limitation.Feature == ContentProjectionDegradedFeature.Coherence);
        status.Views[0].Limitations.Should().Contain(limitation =>
            limitation.Feature == ContentProjectionDegradedFeature.Cache);
    }

    [Fact]
    public async Task Guest_not_ready_prevents_projected_status()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-guest-not-ready", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projected,
            GuestProjectionStatus(metadata.Id.Value, spec, ContentProjectionPhase.Projected, readyForHpdUse: true),
            guestAgentReady: false,
            verifiedByGuestAgent: false));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projecting);
        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.Conditions.Should().Contain(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.GuestMountVerifiedCondition &&
            condition.Status == ConditionStatus.False);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AppleVirtualizationContentProjectionProvider.ProjectionGuestNotReady);
    }

    [Fact]
    public async Task Configured_but_not_visible_produces_stable_diagnostic_and_not_projected()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-configured-not-visible", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projecting,
            GuestProjectionStatus(
                metadata.Id.Value,
                spec,
                ContentProjectionPhase.Projecting,
                readyForHpdUse: false,
                verificationState: AppleVirtualizationGuestAgentProjectionVerificationState.NotVisible),
            guestAgentReady: true,
            verifiedByGuestAgent: false));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projecting);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AppleVirtualizationContentProjectionProvider.ProjectionGuestNotVisible &&
            diagnostic.Message.Length <= AppleVirtualizationContentProjectionProvider.MaxProjectionDiagnosticMessageLength);
    }

    [Fact]
    public async Task Access_mismatch_produces_stable_limitation_and_diagnostic()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-access-mismatch", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadWrite) with
        {
            SecurityPolicy = ProjectionSpec(AccessMode.ReadWrite).SecurityPolicy with
            {
                AllowDirectSourceMutation = true,
            },
        };
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projecting,
            GuestProjectionStatus(
                metadata.Id.Value,
                spec,
                ContentProjectionPhase.Projecting,
                readyForHpdUse: false,
                effectiveAccess: AccessMode.ReadOnly,
                writeEffect: ProjectionWriteEffect.NoWrites,
                verificationState: AppleVirtualizationGuestAgentProjectionVerificationState.AccessMismatch),
            guestAgentReady: true,
            verifiedByGuestAgent: false,
            writeEffect: ProjectionWriteEffect.NoWrites));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projecting);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AppleVirtualizationContentProjectionProvider.ProjectionAccessMismatch);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AppleVirtualizationContentProjectionProvider.ProjectionWriteModeDegraded);
        status.Views[0].Limitations.Should().Contain(limitation =>
            limitation.ReasonCode == "AppleVirtualization.ProjectionAccessMismatch");
    }

    [Fact]
    public async Task Verified_guest_projection_maps_effective_view_from_guest_agent_status()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-view-map", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadWrite) with
        {
            SecurityPolicy = ProjectionSpec(AccessMode.ReadWrite).SecurityPolicy with
            {
                AllowDirectSourceMutation = true,
            },
        };
        AppleVirtualizationGuestAgentProjectionStatus guestStatus = GuestProjectionStatus(
            metadata.Id.Value,
            spec,
            ContentProjectionPhase.Projected,
            readyForHpdUse: true,
            effectiveAccess: AccessMode.ReadWrite,
            writeEffect: ProjectionWriteEffect.DirectSourceMutation,
            coherence: CoherenceClass.Strong,
            cache: CacheBehavior.WriteThrough);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projected,
            guestStatus,
            guestAgentReady: true,
            verifiedByGuestAgent: true));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projected);
        status.Views[0].EffectiveAccess.Should().Be(AccessMode.ReadWrite);
        status.Views[0].EffectiveWriteEffect.Should().Be(ProjectionWriteEffect.DirectSourceMutation);
        status.Views[0].EffectiveCoherence.Should().Be(CoherenceClass.Strong);
        status.Views[0].EffectiveCache.Should().Be(CacheBehavior.WriteThrough);
    }

    [Fact]
    public async Task Projection_limitations_and_conditions_from_guest_agent_are_preserved()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-guest-limits", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        ContentProjectionLimitation limitation = new(
            ContentProjectionDegradedFeature.Coherence,
            CapabilityDegradationMode.PartiallyAvailable,
            "AppleVirtualization.GuestCoherenceDegraded",
            "Guest agent reported degraded projection coherence.");
        Condition condition = new(
            "AppleVirtualization.GuestProjectionCoherence",
            ConditionStatus.False,
            "CoherenceDegraded",
            "Guest agent reported degraded projection coherence.",
            DateTimeOffset.UtcNow,
            metadata.Generation,
            DiagnosticSeverity.Warning);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projecting,
            GuestProjectionStatus(
                metadata.Id.Value,
                spec,
                ContentProjectionPhase.Projecting,
                readyForHpdUse: false,
                coherence: CoherenceClass.ProviderDefined,
                cache: CacheBehavior.ProviderDefined,
                limitations: [limitation],
                conditions: [condition])));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projecting);
        status.Views[0].Limitations.Should().Contain(limitation);
        status.Conditions.Should().Contain(condition);
        status.Views[0].Conditions.Should().Contain(condition);
    }

    [Fact]
    public async Task Coherence_unknown_or_degraded_is_represented_without_overclaiming()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-coherence-unknown", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projecting,
            GuestProjectionStatus(
                metadata.Id.Value,
                spec,
                ContentProjectionPhase.Projecting,
                readyForHpdUse: false,
                coherence: CoherenceClass.Unknown,
                verificationState: AppleVirtualizationGuestAgentProjectionVerificationState.CoherenceUnknown),
            guestAgentReady: true,
            verifiedByGuestAgent: false,
            coherence: CoherenceClass.Unknown));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projecting);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AppleVirtualizationContentProjectionProvider.ProjectionCoherenceUnverified);
        status.Views[0].Limitations.Should().Contain(limitation =>
            limitation.Feature == ContentProjectionDegradedFeature.Coherence);
    }

    [Fact]
    public async Task Malformed_projection_response_is_bounded_and_structured()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-malformed", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.ProjectionMount,
            SequenceNumber = 2,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Error,
            PayloadSchema = AppleVirtualizationHelperProtocol.ErrorSchema,
            Error = new AppleVirtualizationHelperError
            {
                Code = "AppleVirtualization.GuestProjectionMalformedResponse",
                Message = new string('x', AppleVirtualizationContentProjectionProvider.MaxProjectionDiagnosticMessageLength + 200),
                Operation = "projection.mount",
                FailedPhase = "ProjectionMount",
                Retryable = true,
            },
        });

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.GuestProjectionMalformedResponse" &&
            diagnostic.Message.Length <= AppleVirtualizationContentProjectionProvider.MaxProjectionDiagnosticMessageLength);
    }

    [Fact]
    public async Task Guest_reboot_generation_mismatch_requires_projection_reverification()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, _, TargetHandle<RuntimeHost> host) = CreateProvider("guest-boot-a:1");
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-reboot-mismatch", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projected,
            GuestProjectionStatus(
                metadata.Id.Value,
                spec,
                ContentProjectionPhase.Projected,
                readyForHpdUse: true,
                generation: new AppleVirtualizationGuestAgentProjectionGenerationStamp(GuestBootId: "guest-boot-b", GuestBootGeneration: 2)),
            guestAgentReady: true,
            verifiedByGuestAgent: true));

        ContentProjectionStatus status = await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);

        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projecting);
        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AppleVirtualizationContentProjectionProvider.ProjectionGuestBootMismatch);
    }

    [Fact]
    public async Task Sync_and_finalization_results_update_projection_ledger_status()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-sync-finalize", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        await ProjectVerifiedAsync(provider, helper, metadata, spec, host);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(new ResourceRef<ContentProjection>(metadata.Id, metadata.Scope, metadata.Generation)).Entry!.TargetHandle;
        helper.EnqueueResponse(ProjectionSyncResponse(metadata.Id.Value, checkpointVersion: 7, new ContentProjectionChangeSummary(Created: 1, Modified: 2, Deleted: 0, Conflicted: 1, new Digest("sha256", "sync-manifest"))));
        helper.EnqueueResponse(ProjectionFinalizationResponse(metadata.Id.Value, new Digest("sha256", "final-manifest")));

        SyncResult sync = await provider.SyncAsync(handle, new SyncRequest(), CancellationToken.None);
        FinalizationResult finalization = await provider.FinalizeAsync(handle, new FinalizationRequest(), events: null, CancellationToken.None);

        sync.Checkpoint.Version.Should().Be(7);
        sync.Checkpoint.Changes!.Modified.Should().Be(2);
        sync.Checkpoint.TargetManifestDigest!.Value.Value.Should().Be("sync-manifest");
        finalization.ManifestDigest!.Value.Value.Should().Be("final-manifest");
        finalization.Content.Should().ContainSingle(content => content.Path == "/workspace/changed.txt");
        ContentProjectionStatus status = ledger.TryGetContentProjection(new ResourceRef<ContentProjection>(metadata.Id, metadata.Scope, metadata.Generation)).Entry!.Status;
        status.LastSync!.Version.Should().Be(7);
        status.ChangeSummary.Modified.Should().Be(2);
        status.LastFinalization!.ManifestDigest!.Value.Value.Should().Be("final-manifest");
        helper.Requests.Select(request => request.Operation).Should().EndWith([
            AppleVirtualizationHelperOperation.ProjectionSync,
            AppleVirtualizationHelperOperation.ProjectionFinalize,
        ]);
    }

    [Fact]
    public async Task Sync_and_finalization_do_not_promote_unverified_projection_to_projected()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-sync-unverified", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionMount, metadata.Id.Value, ContentProjectionPhase.Projected));
        await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;

        _ = await provider.SyncAsync(handle, new SyncRequest(), CancellationToken.None);
        _ = await provider.FinalizeAsync(handle, new FinalizationRequest(), events: null, CancellationToken.None);

        ContentProjectionStatus status = ledger.TryGetContentProjection(resource).Entry!.Status;
        status.ProjectionPhase.Should().Be(ContentProjectionPhase.Projecting);
        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.LastSync.Should().BeNull();
        status.LastFinalization.Should().BeNull();
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.ProjectionSync);
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.ProjectionFinalize);
    }

    [Fact]
    public async Task SyncAsync_maps_helper_conflicts_to_result_and_updates_last_sync()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-sync-conflict", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        WorkspaceConflict conflict = new("/workspace/conflicted.txt", ConflictKind.ConcurrentWrite, "guest and host changed the same path");
        helper.EnqueueResponse(ProjectionSyncResponse(metadata.Id.Value, checkpointVersion: 3, new ContentProjectionChangeSummary(Conflicted: 1), conflicts: [conflict]));

        SyncResult sync = await provider.SyncAsync(handle, new SyncRequest(), CancellationToken.None);

        sync.Conflicts.Should().ContainSingle().Which.Should().Be(conflict);
        ledger.TryGetContentProjection(resource).Entry!.Status.LastSync!.Version.Should().Be(3);
    }

    [Fact]
    public async Task SyncAsync_conflict_policy_fail_rejects_conflicted_helper_success_without_checkpoint_update()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-sync-fail-conflict", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        WorkspaceConflict conflict = new("/workspace/conflicted.txt", ConflictKind.ConcurrentWrite, "guest and host changed the same path");
        helper.EnqueueResponse(ProjectionSyncResponse(metadata.Id.Value, checkpointVersion: 4, new ContentProjectionChangeSummary(Modified: 1, Conflicted: 1), conflicts: [conflict]));

        SyncResult sync = await provider.SyncAsync(handle, new SyncRequest { OverrideConflictPolicy = ConflictPolicy.Fail }, CancellationToken.None);

        sync.Conflicts.Should().ContainSingle().Which.Should().Be(conflict);
        sync.Conditions.Should().Contain(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.SyncConflictPolicyCondition &&
            condition.Status == ConditionStatus.False &&
            condition.Reason == "ConflictsRejected");
        ledger.TryGetContentProjection(resource).Entry!.Status.LastSync.Should().BeNull();
    }

    [Fact]
    public async Task SyncAsync_record_conflict_records_conflicts_without_selecting_a_winner()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-sync-record-conflict", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        WorkspaceConflict conflict = new("/workspace/conflicted.txt", ConflictKind.ConcurrentWrite, "recorded for explicit resolution");
        helper.EnqueueResponse(ProjectionSyncResponse(metadata.Id.Value, checkpointVersion: 5, new ContentProjectionChangeSummary(Conflicted: 1), conflicts: [conflict]));

        SyncResult sync = await provider.SyncAsync(handle, new SyncRequest { OverrideConflictPolicy = ConflictPolicy.RecordConflict }, CancellationToken.None);

        sync.Conditions.Should().Contain(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.SyncConflictPolicyCondition &&
            condition.Status == ConditionStatus.True &&
            condition.Reason == "ConflictsRecorded");
        sync.Conflicts.Should().ContainSingle().Which.Should().Be(conflict);
        ledger.TryGetContentProjection(resource).Entry!.Status.LastSync!.Version.Should().Be(5);
    }

    [Theory]
    [InlineData(ConflictPolicy.PreferSource, "ConflictPolicyUnsupported")]
    [InlineData(ConflictPolicy.PreferTarget, "ConflictPolicyUnsupported")]
    [InlineData(ConflictPolicy.RequireExplicitPromotion, "ExplicitPromotionRequired")]
    public async Task SyncAsync_rejects_unsafe_conflict_policies_before_helper_dispatch(ConflictPolicy conflictPolicy, string reason)
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>($"projection-sync-{conflictPolicy}", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        int requestCount = helper.Requests.Count;

        SyncResult sync = await provider.SyncAsync(handle, new SyncRequest { OverrideConflictPolicy = conflictPolicy }, CancellationToken.None);

        sync.Conditions.Should().ContainSingle(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.SyncConflictPolicyCondition &&
            condition.Status == ConditionStatus.False &&
            condition.Reason == reason);
        helper.Requests.Should().HaveCount(requestCount);
        ledger.TryGetContentProjection(resource).Entry!.Status.LastSync.Should().BeNull();
    }

    [Fact]
    public async Task SyncAsync_dry_run_does_not_mutate_projection_checkpoint()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-sync-dryrun", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        helper.EnqueueResponse(ProjectionSyncResponse(metadata.Id.Value, checkpointVersion: 0, new ContentProjectionChangeSummary(Created: 1), dryRun: true));

        SyncResult sync = await provider.SyncAsync(handle, new SyncRequest { DryRun = true }, CancellationToken.None);

        sync.Checkpoint.Changes!.Created.Should().Be(1);
        ledger.TryGetContentProjection(resource).Entry!.Status.LastSync.Should().BeNull();
    }

    [Fact]
    public async Task SyncAsync_truncated_helper_results_are_reported_as_bounded_conditions()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-sync-truncated", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        helper.EnqueueResponse(ProjectionSyncResponse(
            metadata.Id.Value,
            checkpointVersion: 8,
            new ContentProjectionChangeSummary(Created: 10, Conflicted: 2),
            changesTruncated: true,
            conflictsTruncated: true));

        SyncResult sync = await provider.SyncAsync(handle, new SyncRequest(), CancellationToken.None);

        sync.Conditions.Should().Contain(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.SyncResultBoundedCondition &&
            condition.Reason == "ChangesTruncated");
        sync.Conditions.Should().Contain(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.SyncResultBoundedCondition &&
            condition.Reason == "ConflictsTruncated");
        ledger.TryGetContentProjection(resource).Entry!.Status.LastSync!.Version.Should().Be(8);
    }

    [Fact]
    public async Task SyncAsync_helper_stale_generation_error_does_not_update_checkpoint()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-sync-helper-stale", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        helper.EnqueueResponse(ProjectionHelperError(AppleVirtualizationHelperOperation.ProjectionSync, "AppleVirtualization.ProjectionStaleGeneration"));

        SyncResult sync = await provider.SyncAsync(handle, new SyncRequest(), CancellationToken.None);

        sync.Conditions.Should().ContainSingle(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.SyncFailedCondition &&
            condition.Reason == "ProjectionStaleGeneration");
        ledger.TryGetContentProjection(resource).Entry!.Status.LastSync.Should().BeNull();
    }

    [Fact]
    public async Task SyncAsync_unsupported_mode_returns_structured_condition_without_status_success()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-sync-unsupported", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        helper.EnqueueResponse(ProjectionSyncResponse(
            metadata.Id.Value,
            checkpointVersion: 0,
            new ContentProjectionChangeSummary(),
            succeeded: false,
            state: AppleVirtualizationGuestAgentProjectionSyncState.UnsupportedMode,
            conditions:
            [
                new Condition("AppleVirtualization.ProjectionSyncUnsupported", ConditionStatus.True, "UnsupportedMode", "Continuous sync is unsupported.", DateTimeOffset.UtcNow, metadata.Generation, DiagnosticSeverity.Warning),
            ]));

        SyncResult sync = await provider.SyncAsync(handle, new SyncRequest { OverrideMode = SyncMode.Continuous }, CancellationToken.None);

        sync.Conditions.Should().Contain(condition => condition.Reason == "UnsupportedMode");
        ledger.TryGetContentProjection(resource).Entry!.Status.LastSync.Should().BeNull();
    }

    [Fact]
    public async Task SyncAsync_missing_projection_handle_returns_structured_condition_without_helper_call()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-sync-missing", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        ledger.RemoveContentProjection(resource);
        int requestCount = helper.Requests.Count;

        SyncResult sync = await provider.SyncAsync(handle, new SyncRequest(), CancellationToken.None);

        sync.Checkpoint.Version.Should().Be(0);
        sync.Conditions.Should().ContainSingle(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.InvalidHandleCondition &&
            condition.Status == ConditionStatus.False &&
            condition.Reason == "HandleMissing" &&
            condition.Message.StartsWith(AppleVirtualizationHandleDiagnostics.MissingHandle.Value, StringComparison.Ordinal));
        helper.Requests.Should().HaveCount(requestCount);
        ledger.TryGetContentProjection(resource).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task SyncAsync_stale_projection_handle_returns_structured_condition_without_ledger_update()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-sync-stale", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        ledger.AdvanceProviderGeneration();

        SyncResult sync = await provider.SyncAsync(handle, new SyncRequest(), CancellationToken.None);

        sync.Checkpoint.Version.Should().Be(0);
        sync.Conditions.Should().ContainSingle(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.InvalidHandleCondition &&
            condition.Status == ConditionStatus.False &&
            condition.Reason == "StaleHandle" &&
            condition.Message.StartsWith(AppleVirtualizationHandleDiagnostics.StaleHandle.Value, StringComparison.Ordinal));
        ledger.TryGetContentProjection(resource).Entry!.Status.LastSync.Should().BeNull();
    }

    [Fact]
    public async Task SyncAsync_wrong_kind_handle_returns_structured_condition_without_helper_call()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, _) = CreateProvider();
        TargetHandle<ContentProjection> wrongKind = WrongKindProjectionHandle(ledger);
        int requestCount = helper.Requests.Count;

        SyncResult sync = await provider.SyncAsync(wrongKind, new SyncRequest(), CancellationToken.None);

        sync.Checkpoint.Version.Should().Be(0);
        sync.Conditions.Should().ContainSingle(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.InvalidHandleCondition &&
            condition.Status == ConditionStatus.False &&
            condition.Reason == "WrongHandleKind" &&
            condition.Message.StartsWith(AppleVirtualizationHandleDiagnostics.WrongHandleKind.Value, StringComparison.Ordinal));
        helper.Requests.Should().HaveCount(requestCount);
    }

    [Fact]
    public async Task FinalizeAsync_missing_projection_handle_returns_not_completed_condition_without_helper_call()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-finalize-missing", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        ledger.RemoveContentProjection(resource);
        int requestCount = helper.Requests.Count;

        FinalizationResult finalization = await provider.FinalizeAsync(handle, new FinalizationRequest(), events: null, CancellationToken.None);

        finalization.CompletedAt.Should().Be(DateTimeOffset.UnixEpoch);
        finalization.ManifestDigest.Should().BeNull();
        finalization.Content.Should().BeEmpty();
        finalization.Conditions.Should().ContainSingle(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.InvalidHandleCondition &&
            condition.Status == ConditionStatus.False &&
            condition.Reason == "HandleMissing" &&
            condition.Message.StartsWith(AppleVirtualizationHandleDiagnostics.MissingHandle.Value, StringComparison.Ordinal));
        helper.Requests.Should().HaveCount(requestCount);
        ledger.TryGetContentProjection(resource).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task FinalizeAsync_stale_projection_handle_returns_not_completed_condition_without_ledger_update()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-finalize-stale", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        ledger.AdvanceProviderGeneration();

        FinalizationResult finalization = await provider.FinalizeAsync(handle, new FinalizationRequest(), events: null, CancellationToken.None);

        finalization.CompletedAt.Should().Be(DateTimeOffset.UnixEpoch);
        finalization.Conditions.Should().ContainSingle(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.InvalidHandleCondition &&
            condition.Status == ConditionStatus.False &&
            condition.Reason == "StaleHandle" &&
            condition.Message.StartsWith(AppleVirtualizationHandleDiagnostics.StaleHandle.Value, StringComparison.Ordinal));
        ledger.TryGetContentProjection(resource).Entry!.Status.LastFinalization.Should().BeNull();
    }

    [Fact]
    public async Task FinalizeAsync_wrong_kind_handle_returns_not_completed_condition_without_helper_call()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, _) = CreateProvider();
        TargetHandle<ContentProjection> wrongKind = WrongKindProjectionHandle(ledger);
        int requestCount = helper.Requests.Count;

        FinalizationResult finalization = await provider.FinalizeAsync(wrongKind, new FinalizationRequest(), events: null, CancellationToken.None);

        finalization.CompletedAt.Should().Be(DateTimeOffset.UnixEpoch);
        finalization.ManifestDigest.Should().BeNull();
        finalization.Content.Should().BeEmpty();
        finalization.Conditions.Should().ContainSingle(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.InvalidHandleCondition &&
            condition.Status == ConditionStatus.False &&
            condition.Reason == "WrongHandleKind" &&
            condition.Message.StartsWith(AppleVirtualizationHandleDiagnostics.WrongHandleKind.Value, StringComparison.Ordinal));
        helper.Requests.Should().HaveCount(requestCount);
    }

    [Fact]
    public async Task FinalizeAsync_maps_finalized_content_refs_and_conflicts()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-finalize-content", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        WorkspaceConflict conflict = new("/workspace/conflicted.txt", ConflictKind.ProviderConflict, "finalization conflict");
        helper.EnqueueResponse(ProjectionFinalizationResponse(metadata.Id.Value, new Digest("sha256", "manifest-with-conflict"), conflicts: [conflict]));

        FinalizationResult finalization = await provider.FinalizeAsync(handle, new FinalizationRequest(), events: null, CancellationToken.None);

        finalization.Content.Should().ContainSingle(content => content.Path == "/workspace/changed.txt" && content.ContentId == "content-1");
        finalization.Conflicts.Should().ContainSingle().Which.Should().Be(conflict);
        ledger.TryGetContentProjection(resource).Entry!.Status.LastFinalization!.ManifestDigest!.Value.Value.Should().Be("manifest-with-conflict");
    }

    [Fact]
    public async Task FinalizeAsync_forwards_include_deleted_entries_boundary_to_helper()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-finalize-deleted-boundary", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        helper.EnqueueResponse(ProjectionFinalizationResponse(metadata.Id.Value, new Digest("sha256", "manifest-no-deletes")));

        _ = await provider.FinalizeAsync(handle, new FinalizationRequest { IncludeDeletedEntries = false }, events: null, CancellationToken.None);

        helper.Requests.Last().ProjectionFinalizationRequest!.IncludeDeletedEntries.Should().BeFalse();
    }

    [Fact]
    public async Task FinalizeAsync_truncated_helper_results_are_reported_as_bounded_conditions()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-finalize-truncated", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        helper.EnqueueResponse(ProjectionFinalizationResponse(
            metadata.Id.Value,
            new Digest("sha256", "manifest-truncated"),
            contentTruncated: true,
            conflictsTruncated: true));

        FinalizationResult finalization = await provider.FinalizeAsync(handle, new FinalizationRequest(), events: null, CancellationToken.None);

        finalization.Conditions.Should().Contain(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.FinalizationResultBoundedCondition &&
            condition.Reason == "ContentRefsTruncated");
        finalization.Conditions.Should().Contain(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.FinalizationResultBoundedCondition &&
            condition.Reason == "ConflictsTruncated");
        ledger.TryGetContentProjection(resource).Entry!.Status.LastFinalization!.ManifestDigest!.Value.Value.Should().Be("manifest-truncated");
    }

    [Fact]
    public async Task FinalizeAsync_unsupported_kind_returns_structured_condition_without_status_success()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-finalize-unsupported", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        helper.EnqueueResponse(ProjectionFinalizationResponse(
            metadata.Id.Value,
            manifestDigest: null,
            succeeded: false,
            state: AppleVirtualizationGuestAgentProjectionFinalizationState.UnsupportedKind,
            conditions:
            [
                new Condition("AppleVirtualization.ProjectionFinalizationUnsupported", ConditionStatus.True, "UnsupportedKind", "PublishArtifacts is unsupported.", DateTimeOffset.UtcNow, metadata.Generation, DiagnosticSeverity.Warning),
            ]));

        FinalizationResult finalization = await provider.FinalizeAsync(handle, new FinalizationRequest { Kind = FinalizationKind.PublishArtifacts }, events: null, CancellationToken.None);

        finalization.Conditions.Should().Contain(condition => condition.Reason == "UnsupportedKind");
        ledger.TryGetContentProjection(resource).Entry!.Status.LastFinalization.Should().BeNull();
    }

    [Fact]
    public async Task FinalizeAsync_helper_guest_unavailable_error_does_not_update_status()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-finalize-guest-unavailable", "content-projection");
        await ProjectVerifiedAsync(provider, helper, metadata, ProjectionSpec(AccessMode.ReadOnly), host);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;
        helper.EnqueueResponse(ProjectionHelperError(AppleVirtualizationHelperOperation.ProjectionFinalize, "AppleVirtualization.GuestAgentNotReady"));

        FinalizationResult finalization = await provider.FinalizeAsync(handle, new FinalizationRequest(), events: null, CancellationToken.None);

        finalization.Conditions.Should().ContainSingle(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.FinalizationFailedCondition &&
            condition.Reason == "GuestAgentNotReady");
        ledger.TryGetContentProjection(resource).Entry!.Status.LastFinalization.Should().BeNull();
    }

    [Fact]
    public async Task Release_sends_projection_release_and_removes_ledger_entry()
    {
        (AppleVirtualizationContentProjectionProvider provider, FakeAppleVirtualizationHelperClient helper, AppleVirtualizationProviderStateLedger ledger, TargetHandle<RuntimeHost> host) = CreateProvider();
        ResourceMetadata<ContentProjection> metadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-release", "content-projection");
        ContentProjectionSpec spec = ProjectionSpec(AccessMode.ReadOnly);
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projected,
            GuestMountVerified(metadata.Generation)));
        await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);
        ResourceRef<ContentProjection> resource = new(metadata.Id, metadata.Scope, metadata.Generation);
        TargetHandle<ContentProjection> handle = ledger.TryGetContentProjection(resource).Entry!.TargetHandle;

        await provider.ReleaseAsync(handle, CancellationToken.None);

        helper.Requests.Last().Operation.Should().Be(AppleVirtualizationHelperOperation.ProjectionRelease);
        ledger.TryGetContentProjection(resource).Succeeded.Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_hostPath))
        {
            Directory.Delete(_hostPath, recursive: true);
        }
    }

    private (AppleVirtualizationContentProjectionProvider Provider, FakeAppleVirtualizationHelperClient Helper, AppleVirtualizationProviderStateLedger Ledger, TargetHandle<RuntimeHost> Host) CreateProvider(
        string? guestBootGeneration = null)
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<RuntimeHost> hostMetadata = AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("runtime-host-1", "runtime-host");
        var host = ledger.UpsertRuntimeHost(hostMetadata, new RuntimeHostStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = hostMetadata.Generation,
            HostPhase = RuntimeHostPhase.Ready,
            Generations = new RuntimeHostGenerationStatus
            {
                GuestBootGeneration = guestBootGeneration is null ? null : new GuestBootGeneration(guestBootGeneration),
            },
        });

        return (new AppleVirtualizationContentProjectionProvider(helper, ledger), helper, ledger, host.TargetHandle);
    }

    private async Task ProjectVerifiedAsync(
        AppleVirtualizationContentProjectionProvider provider,
        FakeAppleVirtualizationHelperClient helper,
        ResourceMetadata<ContentProjection> metadata,
        ContentProjectionSpec spec,
        TargetHandle<RuntimeHost> host)
    {
        helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, metadata.Id.Value, ContentProjectionPhase.Projecting));
        helper.EnqueueResponse(ProjectionResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata.Id.Value,
            ContentProjectionPhase.Projected,
            GuestMountVerified(metadata.Generation)));
        await provider.ProjectAsync(metadata, spec, host, unit: null, CancellationToken.None);
    }

    private static TargetHandle<ContentProjection> WrongKindProjectionHandle(AppleVirtualizationProviderStateLedger ledger)
    {
        ResourceMetadata<ProcessInvocation> metadata = AppleVirtualizationContractFixtures.Metadata<ProcessInvocation>("process-wrong-kind", "process-invocation");
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> process = ledger.UpsertProcessInvocation(metadata, new ProcessInvocationStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            ProcessPhase = ProcessInvocationPhase.Running,
        });

        return new TargetHandle<ContentProjection>(
            process.TargetHandle.Route,
            process.TargetHandle.Lifetime,
            process.TargetHandle.Authority,
            process.TargetHandle.ProviderGeneration);
    }

    private static AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> SeedUnit(
        AppleVirtualizationProviderStateLedger ledger) =>
        ledger.UpsertExecutionUnit(
            AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>("unit-1", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                UnitPhase = ExecutionUnitPhase.Ready,
                AssignedHost = AppleVirtualizationContractFixtures.RuntimeHostRef(),
                RealizedContentProjections = Array.Empty<ResourceRef<ContentProjection>>(),
                ActiveProcesses = Array.Empty<ResourceRef<ProcessInvocation>>(),
            });

    private ContentProjectionSpec ProjectionSpec(AccessMode accessMode) =>
        AppleVirtualizationContractFixtures.ReadOnlyWorkspaceProjection() with
        {
            Source = new ContentSelector
            {
                Kind = ContentSelectorKind.HostPath,
                HostPath = new HostPathSelection(new HostPath(_hostPath), HostPathKind.Directory),
            },
            AccessMode = accessMode,
            Realization = new ProjectionRealizationSpec
            {
                Kind = ProjectionRealizationKind.LiveProjection,
                WriteEffect = accessMode == AccessMode.ReadOnly ? ProjectionWriteEffect.NoWrites : ProjectionWriteEffect.DirectSourceMutation,
                RequestedCoherence = CoherenceClass.CloseToOpen,
                Cache = CacheBehavior.ReadCache,
            },
        };

    private static AppleVirtualizationHelperEnvelope ProjectionSyncResponse(
        string projectionId,
        long checkpointVersion,
        ContentProjectionChangeSummary changeSummary,
        bool succeeded = true,
        bool dryRun = false,
        AppleVirtualizationGuestAgentProjectionSyncState state = AppleVirtualizationGuestAgentProjectionSyncState.Succeeded,
        IReadOnlyList<WorkspaceConflict>? conflicts = null,
        IReadOnlyList<Condition>? conditions = null,
        bool changesTruncated = false,
        bool conflictsTruncated = false)
    {
        if (dryRun && state == AppleVirtualizationGuestAgentProjectionSyncState.Succeeded)
        {
            state = AppleVirtualizationGuestAgentProjectionSyncState.DryRun;
        }

        return new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.ProjectionSync,
            SequenceNumber = 10,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionSyncResponseSchema,
            ProjectionSyncResult = new AppleVirtualizationGuestAgentProjectionSyncResult
            {
                ProjectionId = projectionId,
                State = state,
                Succeeded = succeeded,
                DryRun = dryRun,
                CheckpointVersion = checkpointVersion,
                CompletedAt = DateTimeOffset.UtcNow,
                ChangeSummary = changeSummary,
                Conflicts = conflicts ?? Array.Empty<WorkspaceConflict>(),
                ChangesTruncated = changesTruncated,
                ConflictsTruncated = conflictsTruncated,
                Conditions = conditions ??
                [
                    new Condition("AppleVirtualization.ProjectionSyncCompleted", ConditionStatus.True, "Completed", "Projection sync completed.", DateTimeOffset.UtcNow, default),
                ],
            },
        };
    }

    private static AppleVirtualizationHelperEnvelope ProjectionFinalizationResponse(
        string projectionId,
        Digest? manifestDigest,
        bool succeeded = true,
        AppleVirtualizationGuestAgentProjectionFinalizationState state = AppleVirtualizationGuestAgentProjectionFinalizationState.Succeeded,
        IReadOnlyList<WorkspaceConflict>? conflicts = null,
        IReadOnlyList<Condition>? conditions = null,
        bool contentTruncated = false,
        bool conflictsTruncated = false)
    {
        IReadOnlyList<FinalizedContentRef> content = succeeded
            ? [new FinalizedContentRef("/workspace/changed.txt", "content-1", new Digest("sha256", "content-digest"), new ByteSize(12), ContentProjectionRole.Workspace)]
            : Array.Empty<FinalizedContentRef>();

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
                CompletedAt = DateTimeOffset.UtcNow,
                ManifestDigest = manifestDigest,
                Content = content,
                Conflicts = conflicts ?? Array.Empty<WorkspaceConflict>(),
                ContentTruncated = contentTruncated,
                ConflictsTruncated = conflictsTruncated,
                Conditions = conditions ??
                [
                    new Condition("AppleVirtualization.ProjectionFinalized", ConditionStatus.True, "Completed", "Projection finalization completed.", DateTimeOffset.UtcNow, default),
                ],
            },
        };
    }

    private static AppleVirtualizationHelperEnvelope ProjectionHelperError(
        AppleVirtualizationHelperOperation operation,
        string code) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            SequenceNumber = 12,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Error,
            PayloadSchema = AppleVirtualizationHelperProtocol.ErrorSchema,
            Error = new AppleVirtualizationHelperError
            {
                Code = code,
                Message = "Projection content lifecycle operation failed.",
                Operation = AppleVirtualizationHelperOperationNames.ToWireName(operation),
                FailedPhase = operation.ToString(),
                Retryable = false,
            },
        };

    private static AppleVirtualizationHelperEnvelope ProjectionResponse(
        AppleVirtualizationHelperOperation operation,
        string projectionId,
        ContentProjectionPhase phase,
        Condition? condition = null,
        CoherenceClass coherence = CoherenceClass.CloseToOpen,
        CacheBehavior cache = CacheBehavior.ReadCache,
        ProjectionWriteEffect writeEffect = ProjectionWriteEffect.NoWrites)
    {
        List<Condition> conditions = [];
        if (condition is not null)
        {
            conditions.Add(condition.Value);
        }

        bool verified = HasGuestMountVerifiedCondition(conditions);
        AppleVirtualizationGuestAgentProjectionStatus? guestStatus = verified
            ? GuestProjectionStatus(projectionId, null, phase, readyForHpdUse: true, writeEffect: writeEffect, coherence: coherence, cache: cache, conditions: conditions)
            : null;
        return ProjectionResponse(operation, projectionId, phase, guestStatus, guestAgentReady: verified, verifiedByGuestAgent: verified, coherence: coherence, writeEffect: writeEffect, conditions: conditions);
    }

    private static AppleVirtualizationHelperEnvelope ProjectionResponse(
        AppleVirtualizationHelperOperation operation,
        string projectionId,
        ContentProjectionPhase phase,
        AppleVirtualizationGuestAgentProjectionStatus? guestStatus,
        bool guestAgentReady = true,
        bool verifiedByGuestAgent = false,
        CoherenceClass coherence = CoherenceClass.CloseToOpen,
        ProjectionWriteEffect writeEffect = ProjectionWriteEffect.NoWrites,
        IReadOnlyList<Condition>? conditions = null)
    {
        return new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            SequenceNumber = 1,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionResponseSchema,
            ProjectionStatusResponse = new AppleVirtualizationProjectionStatusResponse
            {
                ProjectionId = projectionId,
                ProjectionPhase = phase,
                EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                EffectiveWriteEffect = writeEffect,
                EffectiveCoherence = coherence,
                GuestAgentReady = guestAgentReady,
                HostShareConfigured = true,
                FrameworkShareAccepted = true,
                VerifiedByGuestAgent = verifiedByGuestAgent,
                GuestProjectionStatus = guestStatus,
                Conditions = conditions ?? Array.Empty<Condition>(),
            },
        };
    }

    private static AppleVirtualizationGuestAgentProjectionStatus GuestProjectionStatus(
        string projectionId,
        ContentProjectionSpec? spec,
        ContentProjectionPhase phase,
        bool readyForHpdUse,
        AccessMode? effectiveAccess = null,
        ProjectionWriteEffect writeEffect = ProjectionWriteEffect.NoWrites,
        CoherenceClass coherence = CoherenceClass.CloseToOpen,
        CacheBehavior cache = CacheBehavior.ReadCache,
        IReadOnlyList<ContentProjectionLimitation>? limitations = null,
        IReadOnlyList<Condition>? conditions = null,
        AppleVirtualizationGuestAgentProjectionVerificationState? verificationState = null,
        AppleVirtualizationGuestAgentProjectionGenerationStamp generation = default) =>
        new()
        {
            ProjectionId = projectionId,
            GuestPath = spec?.View.GuestPath?.Value ?? "/workspace",
            Tag = "hpdprojection",
            Mounted = readyForHpdUse,
            GuestMountVerified = readyForHpdUse,
            HostShareState = AppleVirtualizationGuestAgentProjectionHostShareState.HostShareConfigured,
            FrameworkShareState = AppleVirtualizationGuestAgentProjectionFrameworkShareState.Accepted,
            VerificationState = verificationState ?? (readyForHpdUse
                ? AppleVirtualizationGuestAgentProjectionVerificationState.ReadyForHpdUse
                : AppleVirtualizationGuestAgentProjectionVerificationState.FrameworkShareAccepted),
            ExpectedGuestPath = spec?.View.GuestPath?.Value ?? "/workspace",
            ActualGuestPath = spec?.View.GuestPath?.Value ?? "/workspace",
            RequestedAccessMode = spec?.AccessMode ?? AccessMode.ReadOnly,
            EffectiveAccessMode = effectiveAccess ?? spec?.AccessMode ?? AccessMode.ReadOnly,
            ProjectionPhase = phase,
            EffectiveRealization = ProjectionRealizationKind.LiveProjection,
            EffectiveWriteEffect = writeEffect,
            EffectiveCoherence = coherence,
            EffectiveCache = cache,
            Generation = generation,
            Conditions = conditions ?? Array.Empty<Condition>(),
            Limitations = limitations ?? Array.Empty<ContentProjectionLimitation>(),
        };

    private static bool HasGuestMountVerifiedCondition(IReadOnlyList<Condition> conditions)
    {
        for (int i = 0; i < conditions.Count; i++)
        {
            if (conditions[i].Type == AppleVirtualizationContentProjectionProvider.GuestMountVerifiedCondition &&
                conditions[i].Status == ConditionStatus.True)
            {
                return true;
            }
        }

        return false;
    }

    private static Condition GuestMountVerified(ResourceGeneration generation) =>
        new(
            AppleVirtualizationContentProjectionProvider.GuestMountVerifiedCondition,
            ConditionStatus.True,
            "Mounted",
            "Guest mount was verified by fake helper.",
            DateTimeOffset.UtcNow,
            generation);

    private static Condition GuestMountPending(ResourceGeneration generation) =>
        new(
            AppleVirtualizationContentProjectionProvider.GuestMountVerifiedCondition,
            ConditionStatus.False,
            "GuestMountPending",
            "Guest mount verification is pending.",
            DateTimeOffset.UtcNow,
            generation,
            DiagnosticSeverity.Warning);
}
