using HPD.Base.Tests.InMemory.TestDoubles;
using HPD.Base.Testing;
using Microsoft.Extensions.Options;
using System.Collections.Immutable;

namespace HPD.Base.Tests.InMemory.Mutations;

public sealed class AtomicExecutionTests
{
    private static readonly RecordMutationExecutionRequest ExecutionRequest = new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitCompletionTimeout = TimeSpan.FromSeconds(5)
    };

    [Fact]
    public async Task Durable_yield_reclaims_the_same_attempt_with_a_new_slice()
    {
        var store = SemanticStore();
        BaseAtomicMutationExecutionLimits mutationLimits = ModuleLimits();
        BaseActivationExecutionLimits limits = ActivationLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], mutationLimits)).Value!;
        (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, mutationLimits, maximumYields: 2), ExecutionRequest))
            .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        BaseActivationYieldReservationState reserved = (await store.ReadYieldReservationStateAsync()).Value!;
        reserved.Generation.Should().Be(1);
        reserved.ReservedUnusedSlots.Should().Be(3);
        reserved.RetainedUsedSlots.Should().Be(0);
        BaseActivationDefinitionKey definition = new()
        {
            Id = "test.activation", Version = 1, Checksum = new byte[32].ToImmutableArray(),
        };
        BaseOwnedScopeSeekAuthority scope = new()
        {
            Kind = BaseSubjectScopeKind.Global,
            ProtectedIndexDigest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
        };
        BaseActivationWorkerAuthority worker = new()
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "yield-worker",
            Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
        };
        BaseActivationDueObservation firstObservation = (await store.ObserveDueAsync(new()
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
            AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = limits,
        })).Value!;
        var first = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new()
        {
            Observation = firstObservation.Token, Worker = worker, AcceptedTime = AcceptedTime(10), LeaseMilliseconds = 1_000,
            Identity = RequestIdentity("yield-claim-1"), Limits = limits,
        })).Value!;
        BaseActivationTransitionResult yielded = (await store.TransitionAsync(new BaseActivationYieldRequest
        {
            ActivationId = first.Claim.ActivationId, Claim = first.Claim, RequestedResumeAt = DateTimeOffset.FromUnixTimeMilliseconds(12),
            EffectiveDueAt = 12, ProgressFingerprint = System.Security.Cryptography.SHA256.HashData("progress-1"u8).ToImmutableArray(),
            ExpectedYieldCount = 0, MaximumYields = 2, AcceptedTime = AcceptedTime(11),
            Identity = RequestIdentity("yield-1"), Limits = limits,
        })).Value!;
        yielded.State.Should().Be(BaseActivationState.YieldPending);
        yielded.YieldCount.Should().Be(1);
        BaseActivationYieldReservationState converted = (await store.ReadYieldReservationStateAsync()).Value!;
        converted.Generation.Should().Be(2);
        converted.ReservedUnusedSlots.Should().Be(2);
        converted.RetainedUsedSlots.Should().Be(1);
        BaseActivationDueObservation secondObservation = (await store.ObserveDueAsync(new()
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
            AcceptedTime = AcceptedTime(12), MaximumCandidates = 8, Limits = limits,
        })).Value!;
        var second = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new()
        {
            Observation = secondObservation.Token, Worker = worker, AcceptedTime = AcceptedTime(12), LeaseMilliseconds = 1_000,
            Identity = RequestIdentity("yield-claim-2"), Limits = limits,
        })).Value!;
        second.Claim.AttemptNumber.Should().Be(first.Claim.AttemptNumber);
        second.Claim.ExecutionSliceOrdinal.Should().Be(first.Claim.ExecutionSliceOrdinal + 1);
        second.Claim.AttemptStartedAt.Should().Be(first.Claim.AttemptStartedAt);
        second.Claim.SliceStartedAt.Should().Be(12);
        second.Claim.YieldCount.Should().Be(1);
        second.Claim.MaximumYields.Should().Be(2);
        byte[] resultBytes = "done"u8.ToArray();
        (await store.TransitionAsync(new BaseActivationCompleteRequest
        {
            ActivationId = second.Claim.ActivationId, Claim = second.Claim,
            CanonicalResult = resultBytes.ToImmutableArray(),
            ResultChecksum = System.Security.Cryptography.SHA256.HashData(resultBytes).ToImmutableArray(),
            AcceptedTime = AcceptedTime(13), Identity = RequestIdentity("yield-complete"), Limits = limits,
        })).IsSuccess().Should().BeTrue();
        BaseActivationYieldReservationState released = (await store.ReadYieldReservationStateAsync()).Value!;
        released.Generation.Should().Be(3);
        released.ReservedUnusedSlots.Should().Be(0);
        released.RetainedUsedSlots.Should().Be(1);
        BaseActivationReceiptCompactionRequest request = new()
        {
            ApplicationId = "activation-test",
            Definition = definition,
            ReceiptRetention = new BaseActivationReceiptRetentionPolicy
            {
                FormatVersion = 1,
                DuplicateResolutionLifetime = TimeSpan.FromHours(24),
                ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
            },
            Scope = scope,
            AcceptedTime = AcceptedTime(86_400_020),
            Take = 8,
            BackupFloor = new BaseActivationReceiptBackupFloor
            {
                Kind = BaseActivationReceiptBackupFloorKind.NotApplicable,
            },
            ExpectedReservation = released,
            Limits = limits,
            Identity = RequestIdentity("yield-compaction"),
        };
        OperationResult<BaseActivationReceiptCompactionResult> compacted =
            await store.CompactActivationReceiptsAsync(request);
        compacted.IsSuccess().Should().BeTrue(compacted.Error?.Code);
        compacted.Value!.DeletedCount.Should().Be(1);
        compacted.Value.ResultingReservation.RetainedUsedSlots.Should().Be(0);
        compacted.Value.ResultingChain.CurrentSequence.Should().Be(compacted.Value.PriorChain.CurrentSequence);
        compacted.Value.ResultingChain.OrderedChecksum.Should().Equal(compacted.Value.PriorChain.OrderedChecksum);
        compacted.Value.ResultingChain.Generation.Should().Be(compacted.Value.PriorChain.Generation + 1);
        OperationResult<BaseActivationReceiptCompactionResult> duplicate =
            await store.CompactActivationReceiptsAsync(request);
        duplicate.IsSuccess().Should().BeTrue(duplicate.Error?.Code);
        duplicate.Value!.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
    }

    [Fact]
    public async Task Activation_prune_at_candidate_maximum_uses_a_boundary_probe_without_retaining_a_sentinel()
    {
        var store = SemanticStore();
        BaseAtomicMutationExecutionLimits mutationLimits = ModuleLimits();
        BaseActivationExecutionLimits limits = ActivationLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], mutationLimits)).Value!;
        BaseActivationDefinitionKey definition = new()
        {
            Id = "test.activation", Version = 1, Checksum = new byte[32].ToImmutableArray(),
        };
        BaseOwnedScopeSeekAuthority scope = new()
        {
            Kind = BaseSubjectScopeKind.Global,
            ProtectedIndexDigest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    $"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
        };
        BaseActivationWorkerAuthority worker = new()
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "prune-worker",
            Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
        };

        for (int index = 0; index < 2; index++)
        {
            string id = $"prune-{index}";
            (await store.ExecuteAtomicAsync(
                new ActivationCreationProbe(authority, mutationLimits, activationId: id), ExecutionRequest))
                .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            BaseActivationDueObservation observation = (await store.ObserveDueAsync(new()
            {
                ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition],
                Scope = scope, AcceptedTime = AcceptedTime(10 + index * 10), MaximumCandidates = 8, Limits = limits,
            })).Value!;
            var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new()
            {
                Observation = observation.Token, Worker = worker, AcceptedTime = AcceptedTime(11 + index * 10),
                LeaseMilliseconds = 1_000, Identity = RequestIdentity($"prune-claim-{index}"), Limits = limits,
            })).Value!;
            byte[] result = [(byte)index];
            BaseActivationTransitionResult completed = (await store.TransitionAsync(new BaseActivationCompleteRequest
            {
                ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim,
                CanonicalResult = result.ToImmutableArray(),
                ResultChecksum = System.Security.Cryptography.SHA256.HashData(result).ToImmutableArray(),
                AcceptedTime = AcceptedTime(12 + index * 10), Identity = RequestIdentity($"prune-complete-{index}"),
                Limits = limits,
            })).Value!;
            (await store.TransitionAsync(new BaseActivationDisposeRequest
            {
                ActivationId = claimed.Claim.ActivationId, ExpectedGeneration = completed.Generation,
                AcceptedTime = AcceptedTime(13 + index * 10), Identity = RequestIdentity($"prune-dispose-{index}"),
                Limits = limits,
            })).IsSuccess().Should().BeTrue();
        }

        for (int index = 0; index < 4; index++)
        {
            (await store.ExecuteAtomicAsync(
                new ActivationCreationProbe(
                    authority, mutationLimits, activationId: $"aaa-prune-noise-{index}"),
                ExecutionRequest)).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        }

        BaseActivationExecutionLimits pruneLimits = limits with { MaximumCandidates = 1 };
        OperationResult<BaseActivationPrunePage> boundedFailure = await store.PruneAsync(
            new BaseActivationPruneRequest
            {
                ApplicationId = "activation-test", Scope = scope, Definition = definition, Take = 1,
                AcceptedTime = AcceptedTime(86_400_099), Identity = RequestIdentity("prune-page-bounded"),
                Limits = pruneLimits with { MaximumIndexOperations = 3 },
            });
        boundedFailure.IsSuccess().Should().BeFalse();
        boundedFailure.Error!.Code.Should().Be("base.activation.budgetExceeded");

        BaseActivationPrunePage first = (await store.PruneAsync(new BaseActivationPruneRequest
        {
            ApplicationId = "activation-test", Scope = scope, Definition = definition, Take = 1,
            AcceptedTime = AcceptedTime(86_400_100), Identity = RequestIdentity("prune-page-1"), Limits = pruneLimits,
        })).Value!;
        first.Items.Should().ContainSingle();
        first.Completed.Should().BeFalse();
        first.Accounting.Candidates.Should().Be(1);
        first.Accounting.ReadIntervals.Should().Be(2);
        first.Accounting.IndexOperations.Should().Be(
            2 + first.Items.Length * 2 + first.DeletedReceiptCount * 2);
        first.Accounting.Comparisons.Should().BeGreaterThan(first.Accounting.Candidates);

        BaseActivationPrunePage second = (await store.PruneAsync(new BaseActivationPruneRequest
        {
            ApplicationId = "activation-test", Scope = scope, Definition = definition,
            AfterActivationId = first.NextActivationId, Take = 1,
            AcceptedTime = AcceptedTime(86_400_101), Identity = RequestIdentity("prune-page-2"), Limits = pruneLimits,
        })).Value!;
        second.Items.Should().ContainSingle();
        second.Completed.Should().BeTrue();
        second.Accounting.Candidates.Should().Be(1);
        second.Accounting.ReadIntervals.Should().Be(1);
        second.Accounting.IndexOperations.Should().Be(
            1 + second.Items.Length * 2 + second.DeletedReceiptCount * 2);
    }

    [Fact]
    public async Task Semantic_ensure_is_parent_independent_and_materializes_once()
    {
        var store = SemanticStore();
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], limits)).Value!;
        var first = new SemanticEnsureProbe(authority, limits, "parent-one");
        var second = new SemanticEnsureProbe(authority, limits, "different-parent");

        RecordMutationExecutionResult created = await store.ExecuteAtomicAsync(first, ExecutionRequest);
        RecordMutationExecutionResult existing = await store.ExecuteAtomicAsync(second, ExecutionRequest);

        created.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, first.RejectedCode);
        existing.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        first.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Missing);
        second.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Live);
        first.Provisional!.ActivationId.Should().Be(second.Provisional!.ActivationId);
        first.Provisional!.ResultingSlotGeneration.Should().Be(1);
        second.Provisional!.ResultingSlotGeneration.Should().Be(1);
        RejectsSubstitutedResultingSlotChecksum(first);
        RejectsSubstitutedResultingSlotChecksum(second);
    }

    [Fact]
    public async Task Semantic_maintenance_authority_and_zero_compaction_are_bounded_identified_and_owned()
    {
        BaseSemanticActivationKeyDefinition definition = BaseSemanticActivationDefinitionContract.Seal(
            SemanticDefinition() with
        {
            Compaction = new BaseSemanticActivationSubjectRetirementCompaction(
                new BaseSemanticActivationSubjectContractIdentity(
                    "test.subject", 1,
                    System.Security.Cryptography.SHA256.HashData("test.subject"u8).ToImmutableArray()),
                "subject", "test.subject.retire"),
            Checksum = [],
        });
        InMemoryRecordStore store = SemanticStore(semanticDefinitions: [definition]);
        BaseAtomicMutationAuthorityRequirement captured = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], ModuleLimits())).Value!;
        BaseSemanticActivationStoreAuthorityRequirement authority = captured.SemanticActivation!;
        BaseSemanticActivationMaintenanceAuthorityRequest inspection = new()
        {
            ApplicationId = authority.ApplicationId,
            LogicalStoreId = authority.LogicalStoreId,
            ProviderIncarnation = store.ProviderIncarnation,
            RestoreEpoch = authority.RestoreEpoch,
            Definition = new() { Id = definition.Id, Version = definition.Version, Checksum = definition.Checksum },
            SemanticAuthorityGeneration = authority.SemanticAuthorityGeneration,
            MaximumRows = 0,
            MaximumBytes = 0,
            RuntimeRequestChecksum = [],
        };
        inspection = inspection with
        {
            RuntimeRequestChecksum = BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(inspection),
        };

        BaseSemanticActivationMaintenanceAuthority empty =
            (await store.InspectMaintenanceAuthorityAsync(inspection, default)).RequireValue();
        empty.ExaminedRows.Should().Be(0);
        empty.SemanticAuthorityGeneration.Should().Be(1);
        empty.Checksum.Should().Equal(
            BaseSemanticActivationMaintenanceAuthorityContract.Checksum(inspection, empty));

        BaseMutationRequestIdentity identity = RequestIdentity("zero-compaction");
        var request = new BaseSemanticActivationCompactRequest
        {
            Identity = identity,
            ProviderIncarnation = store.ProviderIncarnation,
            Definition = inspection.Definition,
            ExpectedSemanticAuthorityGeneration = 1,
            ExpectedRetiredCount = 0,
            ExpectedRetiredChecksum = EmptyOrderedSemanticAuthorityChecksum(),
            Limits = SemanticMaintenanceLimits(),
        };
        BaseSemanticActivationMaintenanceResult cancelledBeforeProgress =
            (await store.ExecuteAsync(request, new CancellationToken(canceled: true))).RequireValue();
        cancelledBeforeProgress.Disposition.Should().Be(
            BaseSemanticActivationMaintenanceDisposition.ConfirmedRolledBack);
        cancelledBeforeProgress.ReceiptDisposition.Should().BeNull();
        BaseSemanticActivationMaintenanceContract.IsValid(request, cancelledBeforeProgress).Should().BeTrue();
        BaseSemanticActivationMaintenanceResult completed =
            (await store.ExecuteAsync(request, default)).RequireValue();
        completed.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
        completed.PreviousAuthorityGeneration.Should().Be(1);
        completed.ResultingAuthorityGeneration.Should().Be(1);
        completed.ExaminedRows.Should().Be(0);
        BaseSemanticActivationMaintenanceResult duplicate =
            (await store.ExecuteAsync(request, default)).RequireValue();
        duplicate.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Duplicate);
        duplicate.ReceiptDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        duplicate.ResultChecksum.Should().Equal(completed.ResultChecksum);

        byte[] leaked = duplicate.ResultChecksum.ToArray();
        leaked[0] ^= 0xFF;
        BaseSemanticActivationMaintenanceResult replay =
            (await store.ExecuteAsync(request, default)).RequireValue();
        replay.ResultChecksum.Should().Equal(completed.ResultChecksum);

        BaseSemanticActivationCompactRequest conflict = request with
        {
            ExpectedRetiredChecksum = System.Security.Cryptography.SHA256.HashData("different"u8).ToImmutableArray(),
        };
        BaseFailure<BaseSemanticActivationMaintenanceResult> rejected =
            (BaseFailure<BaseSemanticActivationMaintenanceResult>)await store.ExecuteAsync(conflict, default);
        rejected.Error.Code.Should().Be(BaseSemanticActivationErrorCodes.FingerprintConflict);
    }

    [Fact]
    public async Task Semantic_migration_pages_are_invisible_resumable_and_publish_owned_target_authority()
    {
        BaseSemanticActivationKeyDefinition source = SemanticDefinition();
        BaseSemanticActivationKeyDefinition target = SemanticDefinition(2, "semantic-definition-v2");
        BaseSemanticActivationKeyDefinition unrelated = SemanticDefinition(
            checksumSeed: "unrelated-semantic-definition",
            definitionId: "test.unrelated-semantic");
        BaseSemanticActivationMigrationDefinition migration = BaseSemanticActivationMigrationContract.Seal(new()
        {
            Id = "test.semantic.migration", Version = 1,
            From = new() { Id = source.Id, Version = source.Version, Checksum = source.Checksum },
            To = new() { Id = target.Id, Version = target.Version, Checksum = target.Checksum },
            Checksum = [],
        });
        InMemoryRecordStore store = SemanticStore(
            semanticDefinitions: [source, target, unrelated], semanticMigrations: [migration]);
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], limits)).Value!;
        (await store.ExecuteAtomicAsync(new SemanticEnsureProbe(authority, limits, "migration-one",
            semanticKey: "auth-user-41"), ExecutionRequest)).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        (await store.ExecuteAtomicAsync(new SemanticEnsureProbe(authority, limits, "migration-two",
            semanticKey: "auth-user-42"), ExecutionRequest)).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        (await store.ExecuteAtomicAsync(new SemanticEnsureProbe(authority, limits, "unrelated-before-migration",
            acceptedTime: 2, semanticKey: "unrelated-user-1",
            definitionChecksumSeed: "unrelated-semantic-definition",
            definitionId: "test.unrelated-semantic"), ExecutionRequest)).Outcome
            .Should().Be(RecordMutationExecutionOutcome.Committed);

        BaseActivationExecutionLimits activationLimits = ActivationLimits();
        var activationScope = new BaseOwnedScopeSeekAuthority
        {
            Kind = BaseSubjectScopeKind.Global,
            ProtectedIndexDigest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    $"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
        };
        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test",
            WorkerIdentity = "migration-fence-worker", Definitions = [source.Activation],
            Scope = activationScope, Checksum = new byte[32].ToImmutableArray(),
        };
        BaseActivationDueObservation initialDue = (await store.ObserveDueAsync(
            new BaseActivationDueObservationRequest
            {
                ApplicationId = "activation-test", WorkerModuleId = "test",
                Definitions = [source.Activation], Scope = activationScope,
                AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = activationLimits,
            })).Value!;
        var activeClaim = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(
            new BaseActivationClaimRequest
            {
                Observation = initialDue.Token, Worker = worker, AcceptedTime = AcceptedTime(10),
                LeaseMilliseconds = 1_000, Identity = RequestIdentity("migration-active-claim"),
                Limits = activationLimits,
            })).Value!;

        var request = new BaseSemanticActivationMigrateRequest
        {
            Identity = RequestIdentity("semantic-migration"), ProviderIncarnation = store.ProviderIncarnation,
            Definition = migration.From, ExpectedSemanticAuthorityGeneration = 1, Migration = migration,
            Limits = SemanticMaintenanceLimits() with { PageSize = 1 },
        };
        BaseSemanticActivationMaintenanceResult first = (await store.ExecuteAsync(request, default)).RequireValue();
        first.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.InProgress);
        first.Checkpoint!.CompletedRows.Should().Be(1);
        first.ResultingAuthorityGeneration.Should().Be(1);
        BaseSemanticActivationMaintenanceResult cancelledAfterProgress =
            (await store.ExecuteAsync(request, new CancellationToken(canceled: true))).RequireValue();
        cancelledAfterProgress.Disposition.Should().Be(
            BaseSemanticActivationMaintenanceDisposition.InProgress);
        cancelledAfterProgress.Checkpoint!.Checksum.Should().Equal(first.Checkpoint.Checksum);

        BaseActivationDueObservation due = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
        {
            ApplicationId = "activation-test", WorkerModuleId = "test",
            Definitions = [source.Activation], Scope = activationScope,
            AcceptedTime = AcceptedTime(20), MaximumCandidates = 8, Limits = activationLimits,
        })).Value!;
        OperationResult<BaseActivationClaimResult> fencedClaim =
            await store.TryClaimNextAsync(new BaseActivationClaimRequest
            {
                Observation = due.Token, Worker = worker, AcceptedTime = AcceptedTime(20),
                LeaseMilliseconds = 1_000, Identity = RequestIdentity("migration-fenced-claim"),
                Limits = activationLimits,
            });
        fencedClaim.Error!.Code.Should().Be(BaseSemanticActivationErrorCodes.GraphChanged);
        OperationResult<BaseActivationPrunePage> fencedPrune =
            await store.PruneAsync(new BaseActivationPruneRequest
            {
                ApplicationId = "activation-test", Scope = activationScope,
                Definition = source.Activation, Take = 1, AcceptedTime = AcceptedTime(21),
                Identity = RequestIdentity("migration-fenced-prune"), Limits = activationLimits,
            });
        fencedPrune.Error!.Code.Should().Be(BaseSemanticActivationErrorCodes.GraphChanged);
        OperationResult<BaseActivationRenewResult> fencedRenew = await store.RenewAsync(
            new BaseActivationRenewRequest
            {
                Claim = activeClaim.Claim, ExpectedLeaseRevision = activeClaim.Lease.LeaseRevision,
                ExtensionMilliseconds = 1_000, AcceptedTime = AcceptedTime(22),
                Identity = RequestIdentity("migration-fenced-renew"), Limits = activationLimits,
            });
        fencedRenew.Error!.Code.Should().Be(BaseSemanticActivationErrorCodes.GraphChanged);
        byte[] completedBytes = "migration-fenced-complete"u8.ToArray();
        OperationResult<BaseActivationTransitionResult> fencedTransition = await store.TransitionAsync(
            new BaseActivationCompleteRequest
            {
                ActivationId = activeClaim.Claim.ActivationId, Claim = activeClaim.Claim,
                CanonicalResult = completedBytes.ToImmutableArray(),
                ResultChecksum = System.Security.Cryptography.SHA256.HashData(completedBytes).ToImmutableArray(),
                AcceptedTime = AcceptedTime(23), Identity = RequestIdentity("migration-fenced-transition"),
                Limits = activationLimits,
            });
        fencedTransition.Error!.Code.Should().Be(BaseSemanticActivationErrorCodes.GraphChanged);

        ImmutableArray<byte> fingerprint = BaseSemanticActivationMaintenanceContract.RequestFingerprint(request);
        BaseSemanticActivationMaintenanceResult resolved = (await store.ResolveAsync(new()
        {
            Identity = request.Identity, ProviderIncarnation = store.ProviderIncarnation,
            Definition = request.Definition, MaintenanceId = Convert.ToHexStringLower(fingerprint.AsSpan()),
            RequestFingerprint = fingerprint, Deadline = TimeSpan.FromSeconds(5),
        }, default)).RequireValue();
        resolved.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.InProgress);
        resolved.Checkpoint!.Checksum.Should().Equal(first.Checkpoint.Checksum);

        var blocked = new SemanticEnsureProbe(authority, limits, "migration-blocked",
            semanticKey: "auth-user-41", acceptedTime: 24);
        (await store.ExecuteAtomicAsync(blocked, ExecutionRequest)).Outcome
            .Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        blocked.RejectedCode.Should().Be(BaseSemanticActivationErrorCodes.GraphChanged);

        BaseSemanticActivationMaintenanceResult completed = (await store.ExecuteAsync(request, default)).RequireValue();
        completed.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
        completed.ExaminedRows.Should().Be(2);
        completed.ChangedRows.Should().Be(2);
        completed.ResultingAuthorityGeneration.Should().Be(2);
        BaseSemanticActivationMaintenanceContract.IsValid(request, completed).Should().BeTrue();
        BaseSemanticActivationMaintenanceResult duplicate = (await store.ExecuteAsync(request, default)).RequireValue();
        duplicate.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Duplicate);
        duplicate.ReceiptDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        duplicate.ResultChecksum.Should().Equal(completed.ResultChecksum);

        BaseAtomicMutationAuthorityRequirement migratedAuthority =
            (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
        migratedAuthority.SemanticActivation!.SemanticAuthorityGeneration.Should().Be(2);
        var currentOwner = new SemanticEnsureProbe(migratedAuthority, limits, "migration-current-owner",
            semanticKey: "auth-user-41", definitionVersion: 2,
            definitionChecksumSeed: "semantic-definition-v2", acceptedTime: 25);
        (await store.ExecuteAtomicAsync(currentOwner, ExecutionRequest)).Outcome
            .Should().Be(RecordMutationExecutionOutcome.Committed, currentOwner.RejectedCode);
        currentOwner.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Live);
        var staleOwner = new SemanticEnsureProbe(migratedAuthority, limits, "migration-stale-owner",
            semanticKey: "auth-user-41", acceptedTime: 26);
        (await store.ExecuteAtomicAsync(staleOwner, ExecutionRequest)).Outcome
            .Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        staleOwner.RejectedCode.Should().Be(BaseSemanticActivationErrorCodes.NotInstalled);
        var unrelatedCurrentOwner = new SemanticEnsureProbe(
            migratedAuthority, limits, "unrelated-after-migration",
            acceptedTime: 27, semanticKey: "unrelated-user-1",
            definitionChecksumSeed: "unrelated-semantic-definition",
            definitionId: "test.unrelated-semantic");
        (await store.ExecuteAtomicAsync(unrelatedCurrentOwner, ExecutionRequest)).Outcome
            .Should().Be(RecordMutationExecutionOutcome.Committed,
                unrelatedCurrentOwner.RejectedCode);
        unrelatedCurrentOwner.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Live);
        BaseSemanticActivationMaintenanceAuthorityRequest targetInspection = new()
        {
            ApplicationId = migratedAuthority.SemanticActivation.ApplicationId,
            LogicalStoreId = migratedAuthority.SemanticActivation.LogicalStoreId,
            ProviderIncarnation = store.ProviderIncarnation, RestoreEpoch = 0,
            Definition = migration.To, SemanticAuthorityGeneration = 2,
            MaximumRows = 2, MaximumBytes = 1_000_000, RuntimeRequestChecksum = [],
        };
        targetInspection = targetInspection with
        {
            RuntimeRequestChecksum = BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(targetInspection),
        };
        BaseSemanticActivationMaintenanceAuthority targetAuthority =
            (await store.InspectMaintenanceAuthorityAsync(targetInspection, default)).RequireValue();
        targetAuthority.LiveCount.Should().Be(2);
        targetAuthority.ExaminedRows.Should().Be(2);
    }

    [Fact]
    public async Task Semantic_removal_publishes_a_permanent_tombstone_and_replays_after_generation_change()
    {
        BaseSemanticActivationKeyDefinition removed = SemanticDefinition();
        ImmutableArray<byte> resultingSet = System.Security.Cryptography.SHA256.HashData(
            "semantic-definition-set-without-v1"u8).ToImmutableArray();
        BaseSemanticActivationRemovalAuthority removal = BaseSemanticActivationRemovalAuthorityContract.Seal(new()
        {
            Id = "test.semantic.remove", Version = 1, From = removed,
            ResultingDefinitionSetChecksum = resultingSet, Checksum = [],
        });
        InMemoryRecordStore store = SemanticStore(
            semanticDefinitions: [], semanticRemovals: [removal]);
        bool cloneReached = false;
        store.BeforeSemanticMaintenanceStateClone = () => cloneReached = true;
        var request = new BaseSemanticActivationRemoveRequest
        {
            Identity = RequestIdentity("semantic-removal"), ProviderIncarnation = store.ProviderIncarnation,
            Definition = new()
            {
                Id = removal.From.Id,
                Version = removal.From.Version,
                Checksum = removal.From.Checksum,
            },
            ExpectedSemanticAuthorityGeneration = 1, RemovalAuthority = removal,
            ExpectedLiveCount = 0, ExpectedRetiredCount = 0, ExpectedAbsenceCount = 0,
            ExpectedDefinitionStateChecksum = EmptySemanticDefinitionStateChecksum(),
            ExpectedAbsenceAuthorityChecksum = EmptyOrderedSemanticAuthorityChecksum(),
            Limits = SemanticMaintenanceLimits(),
        };

        BaseSemanticActivationMaintenanceResult completed = (await store.ExecuteAsync(request, default)).RequireValue();
        cloneReached.Should().BeTrue();
        completed.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
        completed.ResultingAuthorityGeneration.Should().Be(2);
        BaseSemanticActivationMaintenanceContract.IsValid(request, completed).Should().BeTrue();
        BaseSemanticActivationMaintenanceResult duplicate = (await store.ExecuteAsync(request, default)).RequireValue();
        duplicate.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Duplicate);
        duplicate.ResultChecksum.Should().Equal(completed.ResultChecksum);

        BaseAtomicMutationAuthorityRequirement current = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], ModuleLimits())).Value!;
        BaseSemanticActivationMaintenanceAuthorityRequest inspection = new()
        {
            ApplicationId = current.SemanticActivation!.ApplicationId,
            LogicalStoreId = current.SemanticActivation.LogicalStoreId,
            ProviderIncarnation = store.ProviderIncarnation,
            RestoreEpoch = current.SemanticActivation.RestoreEpoch,
            Definition = request.Definition,
            SemanticAuthorityGeneration = current.SemanticActivation.SemanticAuthorityGeneration,
            MaximumRows = 0, MaximumBytes = 0, RuntimeRequestChecksum = [],
        };
        inspection = inspection with
        {
            RuntimeRequestChecksum = BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(inspection),
        };
        BaseFailure<BaseSemanticActivationMaintenanceAuthority> rejected =
            (BaseFailure<BaseSemanticActivationMaintenanceAuthority>)await store.InspectMaintenanceAuthorityAsync(
                inspection, default);
        rejected.Error.Code.Should().Be(BaseSemanticActivationErrorCodes.GraphChanged);
    }

    [Fact]
    public async Task Semantic_migrated_target_can_be_removed_after_dependencies_are_clear()
    {
        BaseSemanticActivationKeyDefinition source = SemanticDefinition();
        BaseSemanticActivationKeyDefinition target = SemanticDefinition(2, "semantic-definition-v2");
        BaseSemanticActivationMigrationDefinition migration = BaseSemanticActivationMigrationContract.Seal(new()
        {
            Id = "test.semantic.migration", Version = 1,
            From = new() { Id = source.Id, Version = source.Version, Checksum = source.Checksum },
            To = new() { Id = target.Id, Version = target.Version, Checksum = target.Checksum },
            Checksum = [],
        });
        ImmutableArray<byte> resultingSet = System.Security.Cryptography.SHA256.HashData(
            "semantic-definition-set-empty"u8).ToImmutableArray();
        BaseSemanticActivationRemovalAuthority removal = BaseSemanticActivationRemovalAuthorityContract.Seal(new()
        {
            Id = "test.semantic.remove-v2", Version = 1, From = target,
            ResultingDefinitionSetChecksum = resultingSet, Checksum = [],
        });
        InMemoryRecordStore store = SemanticStore(
            semanticDefinitions: [source, target], semanticMigrations: [migration],
            semanticRemovals: [removal]);
        var migrate = new BaseSemanticActivationMigrateRequest
        {
            Identity = RequestIdentity("semantic-empty-migration"),
            ProviderIncarnation = store.ProviderIncarnation,
            Definition = migration.From, ExpectedSemanticAuthorityGeneration = 1,
            Migration = migration, Limits = SemanticMaintenanceLimits(),
        };
        BaseSemanticActivationMaintenanceResult migrated =
            (await store.ExecuteAsync(migrate, default)).RequireValue();
        migrated.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
        migrated.ResultingAuthorityGeneration.Should().Be(2);

        var remove = new BaseSemanticActivationRemoveRequest
        {
            Identity = RequestIdentity("semantic-remove-migrated-target"),
            ProviderIncarnation = store.ProviderIncarnation,
            Definition = new BaseSemanticActivationDefinitionKey
            {
                Id = removal.From.Id,
                Version = removal.From.Version,
                Checksum = removal.From.Checksum,
            },
            ExpectedSemanticAuthorityGeneration = 2,
            RemovalAuthority = removal, ExpectedLiveCount = 0,
            ExpectedRetiredCount = 0, ExpectedAbsenceCount = 0,
            ExpectedDefinitionStateChecksum = EmptySemanticDefinitionStateChecksum(),
            ExpectedAbsenceAuthorityChecksum = EmptyOrderedSemanticAuthorityChecksum(),
            Limits = SemanticMaintenanceLimits(),
        };
        BaseSemanticActivationMaintenanceResult removed =
            (await store.ExecuteAsync(remove, default)).RequireValue();
        removed.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
        removed.ResultingAuthorityGeneration.Should().Be(3);
        BaseSemanticActivationMaintenanceContract.IsValid(remove, removed).Should().BeTrue();
        BaseSemanticActivationMaintenanceResult duplicate =
            (await store.ExecuteAsync(remove, default)).RequireValue();
        duplicate.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Duplicate);
        duplicate.ResultChecksum.Should().Equal(removed.ResultChecksum);
    }

    [Fact]
    public async Task Semantic_checkpoint_corruption_permanently_quarantines_the_store()
    {
        BaseSemanticActivationKeyDefinition source = SemanticDefinition();
        BaseSemanticActivationKeyDefinition target = SemanticDefinition(2, "semantic-definition-v2");
        BaseSemanticActivationMigrationDefinition migration = BaseSemanticActivationMigrationContract.Seal(new()
        {
            Id = "test.semantic.migration", Version = 1,
            From = new() { Id = source.Id, Version = source.Version, Checksum = source.Checksum },
            To = new() { Id = target.Id, Version = target.Version, Checksum = target.Checksum },
            Checksum = [],
        });
        InMemoryRecordStore store = SemanticAccountingStore(
            semanticDefinitions: [source, target], semanticMigrations: [migration]);
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority =
            (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation-test", [], limits)).Value!;
        (await store.ExecuteAtomicAsync(new SemanticEnsureProbe(
            authority, limits, "quarantine-one", semanticKey: "quarantine-user-1"), ExecutionRequest))
            .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        (await store.ExecuteAtomicAsync(new SemanticEnsureProbe(
            authority, limits, "quarantine-two", semanticKey: "quarantine-user-2"), ExecutionRequest))
            .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        var request = new BaseSemanticActivationMigrateRequest
        {
            Identity = RequestIdentity("semantic-corrupt-checkpoint"),
            ProviderIncarnation = store.ProviderIncarnation,
            Definition = migration.From, ExpectedSemanticAuthorityGeneration = 1,
            Migration = migration,
            Limits = SemanticMaintenanceLimits() with { PageSize = 1 },
        };
        BaseSemanticActivationMaintenanceResult first =
            (await store.ExecuteAsync(request, default)).RequireValue();
        first.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.InProgress);

        await store.CorruptSemanticMaintenanceCheckpointForCertificationAsync(request.Identity);
        BaseFailure<BaseSemanticActivationMaintenanceResult> corrupt =
            (BaseFailure<BaseSemanticActivationMaintenanceResult>)await store.ExecuteAsync(request, default);
        corrupt.Error.Code.Should().Be(BaseSemanticActivationErrorCodes.Corrupt);
        store.SemanticActivationOperationalStatus.Ready.Should().BeFalse();
        store.SemanticActivationOperationalStatus.Quarantined.Should().BeTrue();

        RecordMutationExecutionResult rejectedAtomic = await store.ExecuteAtomicAsync(
            new SemanticEnsureProbe(authority, limits, "quarantine-rejected",
                semanticKey: "quarantine-user-3"), ExecutionRequest);
        rejectedAtomic.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        rejectedAtomic.Error!.Code.Should().Be(BaseSemanticActivationErrorCodes.Quarantined);
        BaseFailure<BaseSemanticActivationMaintenanceResult> rejectedMaintenance =
            (BaseFailure<BaseSemanticActivationMaintenanceResult>)await store.ExecuteAsync(
                request with { Identity = RequestIdentity("semantic-after-quarantine") }, default);
        rejectedMaintenance.Error.Code.Should().Be(BaseSemanticActivationErrorCodes.Quarantined);
    }

    [Fact]
    public async Task Semantic_ensure_cannot_bypass_L51_pending_capacity()
    {
        var store = SemanticStore(maxPendingActivationRows: 1);
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], limits)).Value!;
        (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, limits), ExecutionRequest))
            .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        var semantic = new SemanticEnsureProbe(authority, limits, "semantic-parent");

        RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(semantic, ExecutionRequest);

        result.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        semantic.RejectedCode.Should().Be("base.activation.capacityUnavailable");
    }

    [Fact]
    public async Task Semantic_retirement_requires_terminal_activation_and_is_idempotent()
    {
        var store = SemanticStore();
        BaseAtomicMutationExecutionLimits mutationLimits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], mutationLimits)).Value!;
        (await store.ExecuteAtomicAsync(new SemanticEnsureProbe(authority, mutationLimits, "ensure"), ExecutionRequest))
            .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);

        var premature = new SemanticEnsureProbe(authority, mutationLimits, "retire-too-soon", retire: true);
        (await store.ExecuteAtomicAsync(premature, ExecutionRequest)).Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);

        BaseActivationExecutionLimits activationLimits = ActivationLimits();
        BaseActivationDefinitionKey activation = new()
        {
            Id = "test.activation", Version = 1,
            Checksum = System.Security.Cryptography.SHA256.HashData("activation-definition"u8).ToImmutableArray(),
        };
        var scope = new BaseOwnedScopeSeekAuthority
        {
            Kind = BaseSubjectScopeKind.Global,
            ProtectedIndexDigest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
        };
        BaseActivationDueObservation observed = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [activation], Scope = scope,
            AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = activationLimits,
        })).Value!;
        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "semantic-worker",
            Definitions = [activation], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
        };
        var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new BaseActivationClaimRequest
        {
            Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(10), LeaseMilliseconds = 1_000,
            Identity = RequestIdentity("semantic-claim"), Limits = activationLimits,
        })).Value!;
        byte[] resultBytes = "done"u8.ToArray();
        (await store.TransitionAsync(new BaseActivationCompleteRequest
        {
            ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim,
            CanonicalResult = resultBytes.ToImmutableArray(),
            ResultChecksum = System.Security.Cryptography.SHA256.HashData(resultBytes).ToImmutableArray(),
            AcceptedTime = AcceptedTime(11), Identity = RequestIdentity("semantic-complete"), Limits = activationLimits,
        })).IsSuccess().Should().BeTrue();

        var first = new SemanticEnsureProbe(authority, mutationLimits, "retire", retire: true, acceptedTime: 12);
        var duplicate = new SemanticEnsureProbe(authority, mutationLimits, "retire-again", retire: true, acceptedTime: 13);
        (await store.ExecuteAtomicAsync(first, ExecutionRequest)).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        (await store.ExecuteAtomicAsync(duplicate, ExecutionRequest)).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        first.Provisional!.ResultingState.Should().Be(BaseSemanticActivationSlotState.Retired);
        duplicate.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Retired);
        duplicate.Provisional!.ResultingSlotGeneration.Should().Be(first.Provisional.ResultingSlotGeneration);
        RejectsSubstitutedResultingSlotChecksum(first);
        RejectsSubstitutedResultingSlotChecksum(duplicate);
    }

    private static void RejectsSubstitutedResultingSlotChecksum(SemanticEnsureProbe probe)
    {
        BaseProvisionalSemanticActivation hostile = probe.Provisional! with
        {
            ResultingSlotChecksum = Enumerable.Repeat((byte)0xA5, 32).ToImmutableArray(),
        };
        BaseModuleMutationProcessor<object, object>.ResultingSlotChecksumMatches(
            probe.FinalizedExtension!, probe.CapturedEvidence!, hostile).Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_semantic_ensure_race_converges_on_one_slot()
    {
        var store = SemanticStore();
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], limits)).Value!;
        var left = new SemanticEnsureProbe(authority, limits, "parent-left");
        var right = new SemanticEnsureProbe(authority, limits, "parent-right");

        RecordMutationExecutionResult[] raced = await Task.WhenAll(
            store.ExecuteAtomicAsync(left, ExecutionRequest).AsTask(),
            store.ExecuteAtomicAsync(right, ExecutionRequest).AsTask());

        raced.Count(static result => result.Outcome == RecordMutationExecutionOutcome.Committed).Should().BeGreaterThanOrEqualTo(1);
        foreach (RecordMutationExecutionResult result in raced)
            (result.Outcome == RecordMutationExecutionOutcome.Committed
                || result.Outcome == RecordMutationExecutionOutcome.RollbackConfirmed).Should().BeTrue();
        left.Provisional!.ActivationId.Should().Be(right.Provisional!.ActivationId);
        var retry = new SemanticEnsureProbe(authority, limits, "parent-retry");
        (await store.ExecuteAtomicAsync(retry, ExecutionRequest)).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        retry.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Live);
        retry.Provisional!.ResultingSlotGeneration.Should().Be(1);
    }

    [Fact]
    public async Task Semantic_accounting_rejects_max_plus_one_before_writes()
    {
        var store = SemanticStore();
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], limits)).Value!;
        BaseSemanticActivationExecutionLimits tooSmall = SemanticEnsureProbe.CreateLimits() with { MaximumScopeDirectoryReads = 0 };
        var probe = new SemanticEnsureProbe(authority, limits, "bounded", semanticLimits: tooSmall);

        RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(probe, ExecutionRequest);

        result.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        probe.RejectedCode.Should().Be(BaseSemanticActivationErrorCodes.BudgetExceeded);
        var retry = new SemanticEnsureProbe(authority, limits, "exact");
        (await store.ExecuteAtomicAsync(retry, ExecutionRequest)).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        retry.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Missing);
    }

    [Fact]
    public async Task Semantic_maintenance_rejects_prospective_transient_work_before_state_clone()
    {
        (BaseResult<BaseSemanticActivationMaintenanceResult> result, bool cloneReached) =
            await ExecuteEmptySemanticRemovalAsync(1);
        BaseFailure<BaseSemanticActivationMaintenanceResult> failure =
            Assert.IsType<BaseFailure<BaseSemanticActivationMaintenanceResult>>(
                result);

        failure.Error.Code.Should().Be(BaseSemanticActivationErrorCodes.BudgetExceeded);
        cloneReached.Should().BeFalse();
    }

    [Fact]
    public async Task Semantic_maintenance_transient_accounting_accepts_the_exact_boundary_and_rejects_one_below()
    {
        long lower = 1;
        long upper = SemanticEnsureProbe.CreateLimits().MaximumTransientBytes;
        while (lower < upper)
        {
            long candidate = lower + ((upper - lower) / 2);
            (BaseResult<BaseSemanticActivationMaintenanceResult> result, _) =
                await ExecuteEmptySemanticRemovalAsync(candidate);
            if (result is BaseSuccess<BaseSemanticActivationMaintenanceResult>)
                upper = candidate;
            else
                lower = checked(candidate + 1);
        }

        lower.Should().BeGreaterThan(1);
        (BaseResult<BaseSemanticActivationMaintenanceResult> accepted, bool acceptedClone) =
            await ExecuteEmptySemanticRemovalAsync(lower);
        accepted.Should().BeOfType<BaseSuccess<BaseSemanticActivationMaintenanceResult>>();
        acceptedClone.Should().BeTrue();

        (BaseResult<BaseSemanticActivationMaintenanceResult> rejected, bool rejectedClone) =
            await ExecuteEmptySemanticRemovalAsync(checked(lower - 1));
        BaseFailure<BaseSemanticActivationMaintenanceResult> failure =
            Assert.IsType<BaseFailure<BaseSemanticActivationMaintenanceResult>>(rejected);
        failure.Error.Code.Should().Be(BaseSemanticActivationErrorCodes.BudgetExceeded);
        rejectedClone.Should().BeFalse();
    }

    [Fact]
    public async Task Semantic_migration_accounting_accepts_every_exact_limit_and_rejects_each_one_below_before_clone()
    {
        for (int dimension = 0; dimension < 5; dimension++)
            await VerifySingleRowMigrationAccountingBoundaryAsync(dimension);
    }

    [Fact]
    public async Task Semantic_removal_accounting_accepts_every_exact_limit_and_rejects_each_one_below_before_clone()
    {
        for (int dimension = 0; dimension < 5; dimension++)
            await VerifyEmptyRemovalAccountingBoundaryAsync(dimension);
    }

    [Fact]
    public async Task Semantic_activation_limits_are_intersected_by_measured_work_not_declared_maxima()
    {
        var store = SemanticStore();
        BaseAtomicMutationExecutionLimits enclosing = ModuleLimits() with
        {
            MaximumProducedMutations = 1,
            MaximumReadIntervals = 4,
            MaximumEvidenceBytes = 16_384,
            MaximumTransientBytes = 32_768,
            MaximumJournalBytes = 16_384,
            MaximumFactBytes = 16_384,
            MaximumReceiptBytes = 16_384,
        };
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], enclosing)).Value!;
        BaseActivationLimits broaderInstalled = SemanticCreationLimits() with
        {
            AtomicCreation = ModuleLimits(),
        };
        var probe = new SemanticEnsureProbe(authority, enclosing, "intersected", activationLimits: broaderInstalled);

        RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(probe, ExecutionRequest);

        result.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, probe.RejectedCode);
        probe.Provisional!.Accounting.ActivationCreation.Candidates.Should().Be(1);
    }

    [Fact]
    public async Task Semantic_activation_creation_honors_exact_provider_candidate_and_interval_caps()
    {
        InMemoryRecordStore store = SemanticStore(maximumDueCandidates: 1, maximumActivationReadIntervals: 1);
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], limits)).Value!;
        var probe = new SemanticEnsureProbe(authority, limits, "provider-intersection");

        RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(probe, ExecutionRequest);

        result.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, probe.RejectedCode);
        probe.Provisional!.Accounting.ActivationCreation.ReadIntervals.Should().Be(1);
    }

    [Fact]
    public async Task ActivationCreationCommitsAndExactReplayObservesExistingAuthority()
    {
        var store = new InMemoryRecordStore();
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], limits)).Value!;
        var first = new ActivationCreationProbe(authority, limits);
        var second = new ActivationCreationProbe(authority, limits);
        var conflict = new ActivationCreationProbe(authority, limits, "changed-input");

        RecordMutationExecutionResult committed = await store.ExecuteAtomicAsync(first, ExecutionRequest);
        RecordMutationExecutionResult duplicate = await store.ExecuteAtomicAsync(second, ExecutionRequest);
        RecordMutationExecutionResult rejected = await store.ExecuteAtomicAsync(conflict, ExecutionRequest);

        committed.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        duplicate.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        first.CapturedExisting.Should().BeFalse();
        second.CapturedExisting.Should().BeTrue();
        first.ProvisionalCount.Should().Be(1);
        second.ProvisionalCount.Should().Be(1);
        rejected.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        conflict.RejectedCode.Should().Be("base.activation.fingerprintConflict");
    }

    [Fact]
    public async Task Pending_activation_capacity_succeeds_at_exact_limit_and_rejects_max_plus_one()
    {
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            MaxPendingActivationRows = 1,
        });
        store.Descriptor.Capability.MaximumPendingRows.Should().Be(1);
        BaseActivationCertificationReceiptContract.Validate(store.Descriptor).Should().BeTrue();
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], limits)).Value!;

        RecordMutationExecutionResult exact = await store.ExecuteAtomicAsync(
            new ActivationCreationProbe(authority, limits, activationId: "activation-1"), ExecutionRequest);
        var excessProbe = new ActivationCreationProbe(authority, limits, activationId: "activation-2");
        RecordMutationExecutionResult excess = await store.ExecuteAtomicAsync(excessProbe, ExecutionRequest);

        exact.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        excess.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        excessProbe.RejectedCode.Should().Be("base.activation.capacityUnavailable");
    }

    [Fact]
    public async Task Active_and_terminal_capacities_are_transactional_at_exact_and_plus_one()
    {
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            MaxPendingActivationRows = 2, MaxClaimedActivationRows = 1, MaxTerminalActivationRows = 1,
        });
        BaseAtomicMutationExecutionLimits mutationLimits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], mutationLimits)).Value!;
        (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, mutationLimits, activationId: "active-1"), ExecutionRequest))
            .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, mutationLimits, activationId: "active-2"), ExecutionRequest))
            .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);

        BaseActivationExecutionLimits limits = ActivationLimits();
        BaseActivationDefinitionKey definition = new() { Id = "test.activation", Version = 1, Checksum = new byte[32].ToImmutableArray() };
        var scope = new BaseOwnedScopeSeekAuthority
        {
            Kind = BaseSubjectScopeKind.Global,
            ProtectedIndexDigest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
        };
        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "capacity-worker",
            Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
        };

        async ValueTask<OperationResult<BaseActivationClaimResult>> Claim(long now, string id)
        {
            BaseActivationDueObservation observation = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
            {
                ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
                AcceptedTime = AcceptedTime(now), MaximumCandidates = 8, Limits = limits,
            })).Value!;
            return await store.TryClaimNextAsync(new BaseActivationClaimRequest
            {
                Observation = observation.Token, Worker = worker, AcceptedTime = AcceptedTime(now), LeaseMilliseconds = 1_000,
                Identity = RequestIdentity(id), Limits = limits,
            });
        }

        var first = (BaseActivationClaimedResult)(await Claim(10, "capacity-claim-1")).Value!;
        OperationResult<BaseActivationClaimResult> activeExcess = await Claim(11, "capacity-claim-2-rejected");
        activeExcess.Status.Should().Be(OperationStatus.CapabilityUnavailable);
        activeExcess.Error!.Code.Should().Be("base.activation.capacityUnavailable");

        byte[] result = "done"u8.ToArray();
        (await store.TransitionAsync(new BaseActivationCompleteRequest
        {
            ActivationId = first.Claim.ActivationId, Claim = first.Claim, CanonicalResult = result.ToImmutableArray(),
            ResultChecksum = System.Security.Cryptography.SHA256.HashData(result).ToImmutableArray(), AcceptedTime = AcceptedTime(12),
            Identity = RequestIdentity("capacity-complete-1"), Limits = limits,
        })).IsSuccess().Should().BeTrue();
        var second = (BaseActivationClaimedResult)(await Claim(13, "capacity-claim-2")).Value!;
        OperationResult<BaseActivationTransitionResult> terminalExcess = await store.TransitionAsync(new BaseActivationCompleteRequest
        {
            ActivationId = second.Claim.ActivationId, Claim = second.Claim, CanonicalResult = result.ToImmutableArray(),
            ResultChecksum = System.Security.Cryptography.SHA256.HashData(result).ToImmutableArray(), AcceptedTime = AcceptedTime(14),
            Identity = RequestIdentity("capacity-complete-2"), Limits = limits,
        });
        terminalExcess.Status.Should().Be(OperationStatus.CapabilityUnavailable);
        terminalExcess.Error!.Code.Should().Be("base.activation.capacityUnavailable");
    }

    [Fact]
    public async Task Activation_due_claim_renew_complete_is_fenced_and_terminal()
    {
        var store = new InMemoryRecordStore();
        BaseAtomicMutationExecutionLimits mutationLimits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], mutationLimits)).Value!;
        var creation = new ActivationCreationProbe(authority, mutationLimits);
        (await store.ExecuteAtomicAsync(creation, ExecutionRequest)).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);

        BaseActivationExecutionLimits limits = ActivationLimits();
        BaseAcceptedTimeReceipt now = AcceptedTime(10);
        var scope = new BaseOwnedScopeSeekAuthority
        {
            Kind = BaseSubjectScopeKind.Global,
            ProtectedIndexDigest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n"))
                .ToImmutableArray(),
        };
        BaseActivationDefinitionKey definition = new()
        {
            Id = "test.activation", Version = 1, Checksum = new byte[32].ToImmutableArray(),
        };
        OperationResult<BaseActivationDueObservation> observationResult = await store.ObserveDueAsync(new BaseActivationDueObservationRequest
        {
            ApplicationId = "activation-test",
            WorkerModuleId = "test",
            Definitions = [definition],
            Scope = scope,
            AcceptedTime = now,
            MaximumCandidates = 8,
            Limits = limits,
        });
        observationResult.IsSuccess().Should().BeTrue(observationResult.Error?.Code);
        BaseActivationDueObservation observed = observationResult.Value!;
        observed.Earliest!.ActivationId.Should().NotBeNullOrWhiteSpace();

        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker-1",
            Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
        };
        var claimRequest = new BaseActivationClaimRequest
        {
            Observation = observed.Token,
            Worker = worker,
            AcceptedTime = now,
            LeaseMilliseconds = 1_000,
            Identity = RequestIdentity("claim"),
            Limits = limits,
        };
        var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(claimRequest)).Value!;
        BaseActivationRenewResult renewed = (await store.RenewAsync(new BaseActivationRenewRequest
        {
            Claim = claimed.Claim,
            ExpectedLeaseRevision = claimed.Lease.LeaseRevision,
            AcceptedTime = AcceptedTime(20),
            ExtensionMilliseconds = 2_000,
            Identity = RequestIdentity("renew"),
            Limits = limits,
        })).Value!;
        renewed.Claim.FencingToken.Should().Equal(claimed.Claim.FencingToken);
        renewed.Lease.LeaseRevision.Should().Be(2);

        byte[] result = "done"u8.ToArray();
        BaseActivationTransitionResult completed = (await store.TransitionAsync(new BaseActivationCompleteRequest
        {
            ActivationId = claimed.Claim.ActivationId,
            Claim = claimed.Claim,
            CanonicalResult = result.ToImmutableArray(),
            ResultChecksum = System.Security.Cryptography.SHA256.HashData(result).ToImmutableArray(),
            AcceptedTime = AcceptedTime(30),
            Identity = RequestIdentity("complete"),
            Limits = limits,
        })).Value!;
        completed.State.Should().Be(BaseActivationState.Succeeded);

        OperationResult<BaseActivationTransitionResult> late = await store.TransitionAsync(new BaseActivationCompleteRequest
        {
            ActivationId = claimed.Claim.ActivationId,
            Claim = claimed.Claim,
            CanonicalResult = result.ToImmutableArray(),
            ResultChecksum = System.Security.Cryptography.SHA256.HashData(result).ToImmutableArray(),
            AcceptedTime = AcceptedTime(40),
            Identity = RequestIdentity("late"),
            Limits = limits,
        });
        late.Status.Should().Be(OperationStatus.Conflict);
        late.Error!.Code.Should().Be("base.activation.claimLost");

        var disposalRequest = new BaseActivationDisposeRequest
        {
            ActivationId = claimed.Claim.ActivationId,
            ExpectedGeneration = completed.Generation,
            AcceptedTime = AcceptedTime(45),
            Identity = RequestIdentity("dispose"),
            Limits = limits,
        };
        BaseActivationTransitionResult disposed = (await store.TransitionAsync(disposalRequest)).Value!;
        BaseActivationTransitionResult disposedReplay = (await store.TransitionAsync(disposalRequest with
        {
            AcceptedTime = AcceptedTime(46),
        })).Value!;
        disposed.State.Should().Be(BaseActivationState.Disposed);
        disposedReplay.Generation.Should().Be(disposed.Generation);
        disposedReplay.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);

        BaseActivationDueObservation terminalObservation = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
            AcceptedTime = AcceptedTime(50), MaximumCandidates = 8, Limits = limits,
        })).Value!;
        terminalObservation.Earliest.Should().BeNull();
    }

    [Fact]
    public async Task At_most_once_effect_requires_live_executor_and_recovers_only_to_outcome_unknown()
    {
        var store = new InMemoryRecordStore();
        BaseAtomicMutationExecutionLimits mutationLimits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "activation-test", [], mutationLimits)).Value!;
        (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, mutationLimits), ExecutionRequest))
            .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        BaseActivationExecutionLimits limits = ActivationLimits();
        var scope = new BaseOwnedScopeSeekAuthority
        {
            Kind = BaseSubjectScopeKind.Global,
            ProtectedIndexDigest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
        };
        BaseActivationDefinitionKey definition = new() { Id = "test.activation", Version = 1, Checksum = new byte[32].ToImmutableArray() };
        OperationResult<BaseActivationDueObservation> effectObservationResult = await store.ObserveDueAsync(new BaseActivationDueObservationRequest
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
            AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = limits,
        });
        effectObservationResult.IsSuccess().Should().BeTrue(effectObservationResult.Error?.Code);
        BaseActivationDueObservation observed = effectObservationResult.Value!;
        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker-1",
            Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
        };
        OperationResult<BaseActivationClaimResult> claimResult = await store.TryClaimNextAsync(new BaseActivationClaimRequest
        {
            Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(10), LeaseMilliseconds = 1_000,
            Identity = RequestIdentity("effect-claim"), Limits = limits,
        });
        claimResult.IsSuccess().Should().BeTrue(claimResult.Error?.Code);
        var claimed = claimResult.Value.Should().BeOfType<BaseActivationClaimedResult>().Subject;
        BaseExecutorRegistrationResult executor = (await store.RegisterExecutorAsync(new BaseExecutorRegistrationRequest
        {
            ApplicationId = "activation-test", HostId = "host", ProcessIncarnationId = "process",
            WorkerDefinitionSetChecksum = new byte[32].ToImmutableArray(), RequestedHeartbeatMilliseconds = 100,
            AcceptedTime = AcceptedTime(20), Identity = RequestIdentity("executor"), Limits = limits,
        })).Value!;
        BaseActivationTransitionResult started = (await store.TransitionAsync(new BaseActivationBeginEffectRequest
        {
            ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim, Executor = executor.Executor,
            ExecutorHeartbeat = executor.Heartbeat, HeartbeatMilliseconds = 100, AcceptedTime = AcceptedTime(20),
            Identity = RequestIdentity("effect-start"), Limits = limits,
        })).Value!;
        started.State.Should().Be(BaseActivationState.EffectStarted);
        started.Effect.Should().NotBeNull();

        BaseActivationTransitionResult cancellation = (await store.TransitionAsync(new BaseActivationCancelRequest
        {
            ActivationId = claimed.Claim.ActivationId,
            ExpectedGeneration = started.Generation,
            Propagation = BaseCancellationPropagation.None,
            AcceptedTime = AcceptedTime(30),
            Identity = RequestIdentity("effect-cancel"),
            Limits = limits,
        })).Value!;
        cancellation.State.Should().Be(BaseActivationState.EffectStarted,
            "cancellation cannot manufacture certainty about an external effect that may have run");
        cancellation.Effect.Should().NotBeNull();

        OperationResult<BaseActivationTransitionResult> premature = await store.TransitionAsync(new BaseActivationRecoverEffectRequest
        {
            ActivationId = claimed.Claim.ActivationId, Effect = started.Effect!, AcceptedTime = AcceptedTime(50),
            Identity = RequestIdentity("premature"), Limits = limits,
        });
        premature.Status.Should().Be(OperationStatus.Conflict);

        BaseActivationTransitionResult unknown = (await store.TransitionAsync(new BaseActivationRecoverEffectRequest
        {
            ActivationId = claimed.Claim.ActivationId, Effect = started.Effect!, AcceptedTime = AcceptedTime(200),
            Identity = RequestIdentity("recover"), Limits = limits,
        })).Value!;
        unknown.State.Should().Be(BaseActivationState.OutcomeUnknown);

        byte[] verification = "verified-complete"u8.ToArray();
        var reconciliation = new BaseActivationReconcileEffectRequest
        {
            ActivationId = claimed.Claim.ActivationId,
            ExpectedEffectStartGeneration = started.Effect!.EffectStartGeneration,
            ExpectedEffectChecksum = started.Effect.Checksum,
            ExpectedGeneration = unknown.Generation,
            Disposition = BaseEffectReconciliationDisposition.Succeeded,
            VerificationEvidence = verification.ToImmutableArray(),
            VerificationChecksum = System.Security.Cryptography.SHA256.HashData(verification).ToImmutableArray(),
            AcceptedTime = AcceptedTime(210),
            Identity = RequestIdentity("reconcile"),
            Limits = limits,
        };
        OperationResult<BaseActivationTransitionResult> reconciliationResult = await store.TransitionAsync(reconciliation);
        reconciliationResult.IsSuccess().Should().BeTrue(reconciliationResult.Error?.Code);
        BaseActivationTransitionResult reconciled = reconciliationResult.Value!;
        OperationResult<BaseActivationTransitionResult> replayResult = await store.TransitionAsync(reconciliation with
        {
            AcceptedTime = AcceptedTime(220),
        });
        replayResult.IsSuccess().Should().BeTrue(replayResult.Error?.Code);
        BaseActivationTransitionResult replayed = replayResult.Value!;
        reconciled.State.Should().Be(BaseActivationState.Succeeded);
        replayed.State.Should().Be(BaseActivationState.Succeeded);
        replayed.Generation.Should().Be(reconciled.Generation);
        replayed.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleAndAtomicUseTheSameRestrictedSessionExecutor(bool atomic)
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var processor = new CallbackProcessor(async (session, token) =>
        {
            var create = await session.CreateAsync(
                collection,
                Create("one", "value"),
                Context(BaseRecordMutationKind.Create, "one"),
                token);
            return Ready(create);
        });

        var execution = atomic
            ? await store.ExecuteAtomicAsync(processor, ExecutionRequest)
            : await store.ExecuteSingleAsync(processor, ExecutionRequest);

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        processor.InvocationCount.Should().Be(1);
        var read = await store.GetAsync(
            collection,
            RecordId.Create("one"),
            InMemoryTestData.Operation(BaseOperationKind.Get));
        read.Status.Should().Be(OperationStatus.Ok);
    }

    [Fact]
    public async Task AtomicSessionProvidesOrderedReadYourWritesAcrossCollections()
    {
        var store = new InMemoryRecordStore();
        var firstCollection = InMemoryTestData.Collection("first");
        var secondCollection = InMemoryTestData.Collection("second");
        var processor = new CallbackProcessor(async (session, token) =>
        {
            var created = await session.CreateAsync(
                firstCollection,
                Create("shared", "before"),
                Context(BaseRecordMutationKind.Create, "create"),
                token);
            created.Status.Should().Be(OperationStatus.Created);

            var observed = await session.GetAsync(
                firstCollection,
                RecordId.Create("shared"),
                InMemoryTestData.Operation(BaseOperationKind.Get, "first"),
                token);
            observed.Value!.Payload.Fields!["title"].GetString().Should().Be("before");

            var patched = await session.PatchAsync(
                firstCollection,
                RecordId.Create("shared"),
                new RecordPatchRequest { Patch = InMemoryTestData.Patch("title", "after"), RemovedFieldIds = [] },
                Context(BaseRecordMutationKind.Patch, "patch"),
                token);
            var second = await session.CreateAsync(
                secondCollection,
                Create("other", "second"),
                Context(BaseRecordMutationKind.Create, "second"),
                token);
            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.ReadyToCommit,
                [created.Value!.Mutation, patched.Value!.Mutation, second.Value!.Mutation]);
        });

        var execution = await store.ExecuteAtomicAsync(processor, ExecutionRequest);

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        var first = await store.GetAsync(
            firstCollection,
            RecordId.Create("shared"),
            InMemoryTestData.Operation(BaseOperationKind.Get, "first"));
        var second = await store.GetAsync(
            secondCollection,
            RecordId.Create("other"),
            InMemoryTestData.Operation(BaseOperationKind.Get, "second"));
        first.Value!.Payload.Fields!["title"].GetString().Should().Be("after");
        second.Status.Should().Be(OperationStatus.Ok);
    }

    [Fact]
    public async Task FailedAtomicExecutionRollsBackRecordsAndCounters()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var failed = new CallbackProcessor(async (session, token) =>
        {
            var create = await session.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    Payload = InMemoryTestData.Payload(("title", "discard"))
                },
                Context(BaseRecordMutationKind.Create, "discard"),
                token);
            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.Failed,
                [create.Value!.Mutation],
                Error("test.rollback", "Force rollback."));
        });

        var failure = await store.ExecuteAtomicAsync(failed, ExecutionRequest);
        var committed = await InMemoryMutationTestDriver.CreateAsync(
            store,
            collection,
            new RecordCreateRequest
            {
                Payload = InMemoryTestData.Payload(("title", "kept"))
            },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        failure.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        committed.Value!.Id.Value.Should().Be("mem:0000000000000001");
        committed.Value.Metadata.Revision!.Value.Value.Should().Be("mem:0000000000000001");
    }

    [Fact]
    public async Task ConcurrentCommitCausesConfirmedConflictWithoutRetryOrLostWrite()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var staged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var losingProcessor = new CallbackProcessor(async (session, token) =>
        {
            var create = await session.CreateAsync(
                collection,
                Create("loser", "discard"),
                Context(BaseRecordMutationKind.Create, "loser"),
                token);
            staged.SetResult();
            await release.Task.WaitAsync(token);
            return Ready(create);
        });

        var losingExecution = store.ExecuteAtomicAsync(
            losingProcessor,
            ExecutionRequest).AsTask();
        await staged.Task;
        var winning = await InMemoryMutationTestDriver.CreateAsync(
            store,
            collection,
            Create("winner", "keep"),
            InMemoryTestData.Operation(BaseOperationKind.Create));
        release.SetResult();
        var conflict = await losingExecution;

        winning.Status.Should().Be(OperationStatus.Created);
        conflict.Outcome.Should().Be(RecordMutationExecutionOutcome.ConflictRollbackConfirmed);
        conflict.Error!.Code.Should().Be(BaseMutationErrorCodes.TransactionConflict);
        conflict.Error.Store!.Retryable.Should().BeFalse();
        losingProcessor.InvocationCount.Should().Be(1);
        (await store.GetAsync(
            collection,
            RecordId.Create("winner"),
            InMemoryTestData.Operation(BaseOperationKind.Get))).Status.Should().Be(OperationStatus.Ok);
        (await store.GetAsync(
            collection,
            RecordId.Create("loser"),
            InMemoryTestData.Operation(BaseOperationKind.Get))).Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task SessionFailsClosedAfterProviderInvocationCompletes()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        IAtomicRecordSession? retained = null;
        var processor = new CallbackProcessor(async (session, token) =>
        {
            retained = session;
            var create = await session.CreateAsync(
                collection,
                Create("one", "value"),
                Context(BaseRecordMutationKind.Create, "one"),
                token);
            return Ready(create);
        });

        var execution = await store.ExecuteSingleAsync(processor, ExecutionRequest);
        var escapedUse = await retained!.GetAsync(
            collection,
            RecordId.Create("one"),
            InMemoryTestData.Operation(BaseOperationKind.Get));

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        escapedUse.Status.Should().Be(OperationStatus.StoreError);
        escapedUse.Error!.Store!.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task PreparedModuleOperationIsSessionBoundAndSingleUse()
    {
        var store = new InMemoryRecordStore();
        BaseAtomicMutationExecutionLimits limits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "module.application", [], limits, default)).Value!;
        var prepareOnly = new PreparedModuleProbe(authority, limits, applyTwice: false);
        await store.ExecuteAtomicAsync(prepareOnly, ExecutionRequest);
        prepareOnly.Prepared.Should().NotBeNull();

        var foreign = new ForeignPreparedModuleProbe(prepareOnly.Prepared!);
        await store.ExecuteAtomicAsync(foreign, ExecutionRequest);
        foreign.RejectedCode.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);

        var twice = new PreparedModuleProbe(authority, limits, applyTwice: true);
        await store.ExecuteAtomicAsync(twice, ExecutionRequest);
        twice.RejectedCode.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);
    }

    [Fact]
    public async Task ModuleGenerationAccountingIsEnforcedAtTheMeasuredBoundary()
    {
        var store = new InMemoryRecordStore();
        BaseAtomicMutationExecutionLimits generous = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "module.application", [], generous, default)).Value!;
        var baseline = new PreparedModuleProbe(authority, generous, applyTwice: false);
        await store.ExecuteAtomicAsync(baseline, ExecutionRequest);
        BasePreparedAtomicMutationAccounting measured = baseline.Prepared!.Accounting;

        BaseAtomicMutationExecutionLimits exact = generous with
        {
            MaximumGenerationReads = measured.GenerationReads,
            MaximumGenerationComparisons = measured.GenerationComparisons,
            MaximumGenerationIncrements = measured.GenerationIncrements,
            MaximumGenerationBytes = measured.GenerationBytes,
            MaximumReadIntervals = measured.ReadIntervals,
            MaximumEvidenceBytes = measured.EvidenceBytes,
            MaximumTransientBytes = measured.TransientBytes,
        };
        var accepted = new PreparedModuleProbe(authority, exact, applyTwice: false);
        await store.ExecuteAtomicAsync(accepted, ExecutionRequest);
        accepted.Prepared.Should().NotBeNull();

        var rejected = new PreparedModuleProbe(authority, exact with
        {
            MaximumGenerationIncrements = checked(measured.GenerationIncrements - 1),
        }, applyTwice: false);
        await store.ExecuteAtomicAsync(rejected, ExecutionRequest);
        rejected.Prepared.Should().BeNull();
        rejected.RejectedCode.Should().Be(BaseSubjectErrorCodes.BudgetExceeded);

        rejected = new PreparedModuleProbe(authority, exact with
        {
            MaximumGenerationComparisons = checked(measured.GenerationComparisons - 1),
        }, applyTwice: false);
        await store.ExecuteAtomicAsync(rejected, ExecutionRequest);
        rejected.Prepared.Should().BeNull();
        rejected.RejectedCode.Should().Be(BaseSubjectErrorCodes.BudgetExceeded);
    }

    [Fact]
    public void CapabilitiesAdvertiseOnlyProvenL30GuaranteesAndBounds()
    {
        var capabilities = new InMemoryRecordStore().Capabilities;

        capabilities.Read.List.Should().BeTrue();
        capabilities.Read.Get.Should().BeTrue();
        capabilities.Mutation.Create.Should().BeTrue();
        capabilities.Revision!.Patch.Should().BeTrue();
        capabilities.Revision.Replace.Should().BeTrue();
        capabilities.Revision.Delete.Should().BeTrue();
        capabilities.Batch!.Modes.Should().Equal(BaseRecordBatchExecutionMode.Atomic);
        capabilities.Batch.MaxOperations.Should().Be(100);
        capabilities.Batch.MaxCanonicalPayloadBytes.Should().Be(1_048_576);
        capabilities.Batch.Ordered.Should().BeTrue();
        capabilities.Batch.CrossCollectionAtomic.Should().BeTrue();
        capabilities.Batch.ReadYourWrites.Should().BeTrue();
        capabilities.Batch.Durable.Should().BeFalse();
        capabilities.Batch.TransactionalJournal.Should().BeTrue();
        capabilities.Batch.Isolation.Should().Be(BaseTransactionIsolation.Serializable);
        capabilities.Batch.NestedTransactions.Should().BeFalse();
        capabilities.Batch.Savepoints.Should().BeFalse();
        capabilities.Upsert!.Atomic.Should().BeTrue();
        capabilities.Upsert.UpdateModes.Should().Equal(
            RecordUpsertUpdateMode.Patch,
            RecordUpsertUpdateMode.Replace);
        capabilities.Upsert.ExpectedRevision.Should().BeTrue();
        capabilities.Upsert.ExistenceConditions.Should().BeTrue();
    }

    [Fact]
    public void AtomicUpsertIsNotAdvertisedWhenRequestedIdsAreDisabled()
    {
        var capabilities = new InMemoryRecordStore(
            new HPDBaseInMemoryStoreOptions { AllowClientRequestedIds = false }).Capabilities;

        capabilities.Upsert.Should().BeNull();
    }

    private static RecordCreateRequest Create(string id, string title) => new()
    {
        RequestedId = RecordId.Create(id),
        Payload = InMemoryTestData.Payload(("title", title))
    };

    private static RecordMutationSessionContext Context(
        BaseRecordMutationKind mutation,
        string eventId) => new()
    {
        RequestedOperation = mutation,
        EventId = eventId,
        Operation = InMemoryTestData.Operation(
            mutation switch
            {
                BaseRecordMutationKind.Create => BaseOperationKind.Create,
                BaseRecordMutationKind.Patch => BaseOperationKind.Patch,
                BaseRecordMutationKind.Replace => BaseOperationKind.Replace,
                BaseRecordMutationKind.Delete => BaseOperationKind.Delete,
                _ => BaseOperationKind.Upsert
            })
    };

    private static AtomicMutationProcessingResult Ready(
        OperationResult<RecordMutationSessionResult> result)
    {
        result.Value.Should().NotBeNull();
        return new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.ReadyToCommit,
            [result.Value!.Mutation]);
    }

    private static BaseError Error(string code, string message) => new()
    {
        Code = code,
        Message = message,
        Category = ErrorCategory.Unexpected
    };

    private static BaseAtomicMutationExecutionLimits ModuleLimits() =>
        DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(BaseModuleMutationPlatform.MaximumLimits);

    private static InMemoryRecordStore SemanticStore(
        int maxPendingActivationRows = 1_000_000,
        int maximumDueCandidates = 256,
        int maximumActivationReadIntervals = 4096,
        BaseSemanticActivationKeyDefinition[]? semanticDefinitions = null,
        BaseSemanticActivationMigrationDefinition[]? semanticMigrations = null,
        BaseSemanticActivationRemovalAuthority[]? semanticRemovals = null) => new(
        new HPDBaseInMemoryStoreOptions
        {
            MaxPendingActivationRows = maxPendingActivationRows,
            ActivationMaximumDueCandidates = maximumDueCandidates,
            ActivationMaximumReadIntervals = maximumActivationReadIntervals,
            SemanticActivationApplicationId = "activation-test",
            SemanticActivationOwnerGeneration = 1,
            SemanticActivationDefinitionSetChecksum = System.Security.Cryptography.SHA256.HashData("semantic-definition"u8),
            SemanticActivations = semanticDefinitions ?? [SemanticDefinition()],
            SemanticActivationMigrations = semanticMigrations ?? [],
            SemanticActivationRemovals = semanticRemovals ?? [],
        });

    private static BaseSemanticActivationKeyDefinition SemanticDefinition(
        int version = 1,
        string checksumSeed = "semantic-definition",
        string definitionId = "test.semantic") => BaseSemanticActivationDefinitionContract.Seal(new()
    {
        Id = definitionId, Version = version, OwningApplicationId = "activation-test", OwningModuleId = "test",
        EnsureOperation = new() { OperationId = "semantic.ensure", OperationVersion = 1, OperationChecksum = new string('a', 64) },
        RetirementOperation = new()
        {
            OperationId = "semantic.retire", OperationVersion = 1,
            OperationChecksum = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("completion-operation"u8)),
        },
        Activation = new()
        {
            Id = "test.activation", Version = 1,
            Checksum = System.Security.Cryptography.SHA256.HashData("activation-definition"u8).ToImmutableArray(),
        },
        ScopeKind = BaseSubjectScopeKind.Global,
        EnsureGrantId = "semantic.ensure", RetirementGrantId = "semantic.retire", MaintenanceGrantId = "semantic.maintain",
        Compaction = new BaseSemanticActivationNoCompaction(), RequestTypeId = "request",
        RequestSerializerChecksum = System.Security.Cryptography.SHA256.HashData("request"u8).ToImmutableArray(),
        KeyExpressionChecksum = System.Security.Cryptography.SHA256.HashData("key"u8).ToImmutableArray(),
        Limits = new()
        {
            MaximumCanonicalKeyBytes = 256, MaximumLiveSlots = 100, MaximumRetiredSlots = 100,
            MaximumAbsenceMarkers = 100, Execution = SemanticEnsureProbe.CreateLimits(),
            Deadlines = new()
            {
                AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
                CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
                MaintenanceTimeout = TimeSpan.FromSeconds(5), QuarantineRetentionTimeout = TimeSpan.FromSeconds(5),
            },
        },
        Checksum = [],
    });

    private static BaseSemanticActivationMaintenanceLimits SemanticMaintenanceLimits() => new()
    {
        PageSize = 1, MaximumPages = 8, MaximumRows = 8,
        MaximumBytes = 262_144, Deadline = TimeSpan.FromSeconds(5),
    };

    private static long SemanticMaintenanceLimit(
        BaseSemanticActivationExecutionLimits limits,
        int dimension) => dimension switch
    {
        0 => limits.MaximumReadIntervals,
        1 => limits.MaximumIndexOperations,
        2 => limits.MaximumEvidenceBytes,
        3 => limits.MaximumReceiptBytes,
        4 => limits.MaximumTransientBytes,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

    private static BaseSemanticActivationExecutionLimits WithSemanticMaintenanceLimit(
        BaseSemanticActivationExecutionLimits limits,
        int dimension,
        long value) => dimension switch
    {
        0 => limits with { MaximumReadIntervals = checked((int)value) },
        1 => limits with { MaximumIndexOperations = checked((int)value) },
        2 => limits with { MaximumEvidenceBytes = value },
        3 => limits with { MaximumReceiptBytes = value },
        4 => limits with { MaximumTransientBytes = value },
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

    private static IEnumerable<long> SemanticMaintenanceCandidates(long baseline)
    {
        yield return baseline;
        for (long offset = 1; offset <= 512; offset++)
        {
            if (baseline > offset) yield return baseline - offset;
            yield return checked(baseline + offset);
        }
    }

    private static InMemoryRecordStore SemanticAccountingStore(
        BaseSemanticActivationKeyDefinition[]? semanticDefinitions = null,
        BaseSemanticActivationMigrationDefinition[]? semanticMigrations = null,
        BaseSemanticActivationRemovalAuthority[]? semanticRemovals = null,
        BaseSemanticActivationExecutionLimits? semanticCapabilityLimits = null)
    {
        var time = new BaseTestTimeProvider(
            new DateTimeOffset(2032, 1, 2, 3, 4, 5, TimeSpan.Zero));
        int nonceOrdinal = 0;
        var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 1,
                Key = Enumerable.Repeat((byte)0x4D, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            },
        }), time, length => System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                $"semantic-accounting-nonce:{nonceOrdinal++}"))[..length]);
        var options = new HPDBaseInMemoryStoreOptions
        {
            SemanticActivationApplicationId = "activation-test",
            SemanticActivationOwnerGeneration = 1,
            SemanticActivationDefinitionSetChecksum =
                System.Security.Cryptography.SHA256.HashData("semantic-definition"u8),
            SemanticActivations = semanticDefinitions ?? [SemanticDefinition()],
            SemanticActivationMigrations = semanticMigrations ?? [],
            SemanticActivationRemovals = semanticRemovals ?? [],
        };
        return new InMemoryRecordStore(
            options, protector, time,
            Enumerable.Repeat((byte)0x6B, 32).ToImmutableArray(),
            semanticCapabilityLimits is null ? null : SemanticMaintenanceCapability(semanticCapabilityLimits));
    }

    private static BaseSemanticActivationCapability SemanticMaintenanceCapability(
        BaseSemanticActivationExecutionLimits limits)
    {
        BaseSemanticActivationCapability value = BaseSemanticActivationCapabilityContract
            .BuiltIn(durable: false, maintenanceSupported: true) with
        {
            MaximumReadIntervals = limits.MaximumReadIntervals,
            MaximumIndexOperations = limits.MaximumIndexOperations,
            MaximumEvidenceBytes = limits.MaximumEvidenceBytes,
            MaximumReceiptBytes = limits.MaximumReceiptBytes,
            MaximumTransientBytes = limits.MaximumTransientBytes,
            Checksum = [],
        };
        return value with { Checksum = BaseSemanticActivationCapabilityContract.Checksum(value) };
    }

    private static async ValueTask<(BaseResult<BaseSemanticActivationMaintenanceResult> Result,
        bool CloneReached)> ExecuteEmptySemanticRemovalAsync(long maximumTransientBytes)
    {
        (BaseResult<BaseSemanticActivationMaintenanceResult> result, _, bool cloneReached) =
            await ExecuteEmptySemanticRemovalWithLimitsAsync(
                SemanticEnsureProbe.CreateLimits() with
                {
                    MaximumTransientBytes = maximumTransientBytes,
                }, "transient");
        return (result, cloneReached);
    }

    private static async ValueTask<(BaseResult<BaseSemanticActivationMaintenanceResult> Result,
        InMemorySemanticMaintenanceAccounting Accounting, bool CloneReached)>
        ExecuteEmptySemanticRemovalWithLimitsAsync(
            BaseSemanticActivationExecutionLimits executionLimits,
            string identitySuffix)
    {
        BaseSemanticActivationKeyDefinition definition =
            BaseSemanticActivationDefinitionContract.Seal(SemanticDefinition() with
            {
                Limits = SemanticDefinition().Limits with { Execution = SemanticEnsureProbe.CreateLimits() },
                Checksum = [],
            });
        BaseSemanticActivationRemovalAuthority removal =
            BaseSemanticActivationRemovalAuthorityContract.Seal(new()
            {
                Id = "test.semantic.preclone-removal",
                Version = 1,
                From = definition,
                ResultingDefinitionSetChecksum = System.Security.Cryptography.SHA256.HashData(
                    "semantic-preclone-result"u8).ToImmutableArray(),
                Checksum = [],
            });
        InMemoryRecordStore store = SemanticAccountingStore(
            semanticDefinitions: [], semanticRemovals: [removal],
            semanticCapabilityLimits: executionLimits);
        bool cloneReached = false;
        store.BeforeSemanticMaintenanceStateClone = () => cloneReached = true;
        var request = new BaseSemanticActivationRemoveRequest
        {
            Identity = RequestIdentity("semantic-preclone-" + identitySuffix),
            ProviderIncarnation = store.ProviderIncarnation,
            Definition = new BaseSemanticActivationDefinitionKey
            {
                Id = removal.From.Id,
                Version = removal.From.Version,
                Checksum = removal.From.Checksum,
            },
            ExpectedSemanticAuthorityGeneration = 1,
            RemovalAuthority = removal,
            ExpectedLiveCount = 0,
            ExpectedRetiredCount = 0,
            ExpectedAbsenceCount = 0,
            ExpectedDefinitionStateChecksum = EmptySemanticDefinitionStateChecksum(),
            ExpectedAbsenceAuthorityChecksum = EmptyOrderedSemanticAuthorityChecksum(),
            Limits = SemanticMaintenanceLimits() with
            {
                MaximumBytes = Math.Min(
                    SemanticMaintenanceLimits().MaximumBytes,
                    executionLimits.MaximumTransientBytes),
            },
        };
        BaseResult<BaseSemanticActivationMaintenanceResult> result =
            await store.ExecuteAsync(request, default);
        InMemorySemanticMaintenanceAccounting accounting = store.LastSemanticMaintenanceAccounting
            ?? throw new InvalidOperationException(
                $"Missing semantic maintenance accounting: {result.Status}:{(result as BaseFailure<BaseSemanticActivationMaintenanceResult>)?.Error.Code}");
        return (result, accounting, cloneReached);
    }

    private static async ValueTask VerifySingleRowMigrationAccountingBoundaryAsync(int dimension)
    {
        BaseSemanticActivationExecutionLimits generous = SemanticEnsureProbe.CreateLimits();
        (BaseResult<BaseSemanticActivationMaintenanceResult> baselineResult,
            InMemorySemanticMaintenanceAccounting baselineAccounting, _) =
            await ExecuteSingleRowMigrationWithLimitsAsync(generous, "accounting-boundary");
        baselineResult.RequireValue().Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
        long baseline = SemanticMaintenanceAccountingValue(baselineAccounting, dimension);
        long exact = 0;
        BaseResult<BaseSemanticActivationMaintenanceResult>? acceptedAtBoundary = null;
        InMemorySemanticMaintenanceAccounting? acceptedAccountingAtBoundary = null;
        bool acceptedCloneAtBoundary = false;
        foreach (long candidate in SemanticMaintenanceCandidates(baseline))
        {
            (BaseResult<BaseSemanticActivationMaintenanceResult> candidateResult,
                InMemorySemanticMaintenanceAccounting candidateAccounting, bool candidateClone) =
                await ExecuteSingleRowMigrationWithLimitsAsync(
                    WithSemanticMaintenanceLimit(generous, dimension, candidate),
                    "accounting-boundary");
            if (candidateResult is not BaseSuccess<BaseSemanticActivationMaintenanceResult>
                || !candidateClone)
                continue;
            if (candidate == 1)
            {
                exact = candidate; acceptedAtBoundary = candidateResult;
                acceptedAccountingAtBoundary = candidateAccounting;
                acceptedCloneAtBoundary = candidateClone; break;
            }
            (BaseResult<BaseSemanticActivationMaintenanceResult> adjacent,
                InMemorySemanticMaintenanceAccounting adjacentAccounting, bool adjacentClone) =
                await ExecuteSingleRowMigrationWithLimitsAsync(
                    WithSemanticMaintenanceLimit(generous, dimension, candidate - 1),
                    "accounting-boundary");
            if (adjacent is BaseFailure<BaseSemanticActivationMaintenanceResult> adjacentFailure
                && adjacentFailure.Error.Code == BaseSemanticActivationErrorCodes.BudgetExceeded
                && !adjacentClone
                && SemanticMaintenanceAccountingValue(adjacentAccounting, dimension) > candidate - 1)
            {
                exact = candidate; acceptedAtBoundary = candidateResult;
                acceptedAccountingAtBoundary = candidateAccounting;
                acceptedCloneAtBoundary = candidateClone; break;
            }
        }
        exact.Should().BeGreaterThan(0, "a fresh immutable capability boundary must exist");
        if (exact == 1)
        {
            BaseSemanticActivationCapabilityContract.IsValid(SemanticMaintenanceCapability(
                WithSemanticMaintenanceLimit(generous, dimension, 0))).Should().BeFalse();
            acceptedAtBoundary!.RequireValue().Disposition.Should().Be(
                BaseSemanticActivationMaintenanceDisposition.Completed);
            acceptedCloneAtBoundary.Should().BeTrue();
            return;
        }
        (BaseResult<BaseSemanticActivationMaintenanceResult> rejected,
            InMemorySemanticMaintenanceAccounting rejectedAccounting, bool rejectedClone) =
            await ExecuteSingleRowMigrationWithLimitsAsync(
                WithSemanticMaintenanceLimit(generous, dimension, exact - 1),
                "accounting-boundary");
        if (rejected is not BaseFailure<BaseSemanticActivationMaintenanceResult> rejectedFailure)
            throw new InvalidOperationException(
                $"Expected dimension {dimension} limit {exact - 1} to reject measured "
                + $"{SemanticMaintenanceAccountingValue(rejectedAccounting, dimension)}.");
        rejectedFailure.Error.Code.Should().Be(BaseSemanticActivationErrorCodes.BudgetExceeded);
        SemanticMaintenanceAccountingValue(rejectedAccounting, dimension).Should().BeGreaterThan(exact - 1);
        rejectedClone.Should().BeFalse();
        BaseSemanticActivationMaintenanceResult accepted = acceptedAtBoundary!.RequireValue();
        accepted.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
        SemanticMaintenanceAccountingValue(acceptedAccountingAtBoundary!, dimension).Should().BeLessThanOrEqualTo(exact);
        acceptedCloneAtBoundary.Should().BeTrue();
    }

    private static async ValueTask<(BaseResult<BaseSemanticActivationMaintenanceResult> Result,
        InMemorySemanticMaintenanceAccounting Accounting, bool CloneReached)>
        ExecuteSingleRowMigrationWithLimitsAsync(
            BaseSemanticActivationExecutionLimits executionLimits,
            string identitySuffix)
    {
        BaseSemanticActivationKeyDefinition source = BaseSemanticActivationDefinitionContract.Seal(
            SemanticDefinition() with
            {
                Limits = SemanticDefinition().Limits with { Execution = SemanticEnsureProbe.CreateLimits() },
                Checksum = [],
            });
        BaseSemanticActivationKeyDefinition target = BaseSemanticActivationDefinitionContract.Seal(
            SemanticDefinition(2, "semantic-definition-v2") with
            {
                Limits = SemanticDefinition(2, "semantic-definition-v2").Limits with
                {
                    Execution = SemanticEnsureProbe.CreateLimits(),
                },
                Checksum = [],
            });
        BaseSemanticActivationMigrationDefinition migration = BaseSemanticActivationMigrationContract.Seal(new()
        {
            Id = "test.semantic.accounting-migration", Version = 1,
            From = new() { Id = source.Id, Version = source.Version, Checksum = source.Checksum },
            To = new() { Id = target.Id, Version = target.Version, Checksum = target.Checksum },
            Checksum = [],
        });
        InMemoryRecordStore store = SemanticAccountingStore(
            semanticDefinitions: [source, target], semanticMigrations: [migration],
            semanticCapabilityLimits: executionLimits);
        BaseAtomicMutationExecutionLimits mutationLimits = ModuleLimits();
        BaseAtomicMutationAuthorityRequirement authority = (await store
            .CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], mutationLimits)).Value!;
        RecordMutationExecutionResult seeded = await store.ExecuteAtomicAsync(
            new SemanticEnsureProbe(authority, mutationLimits, $"accounting-seed-{identitySuffix}",
                semanticKey: "accounting-user", installedDefinition: source), ExecutionRequest);
        seeded.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        bool cloneReached = false;
        store.BeforeSemanticMaintenanceStateClone = () => cloneReached = true;
        var request = new BaseSemanticActivationMigrateRequest
        {
            Identity = RequestIdentity($"accounting-migrate-{identitySuffix}"),
            ProviderIncarnation = store.ProviderIncarnation, Definition = migration.From,
            ExpectedSemanticAuthorityGeneration = 1, Migration = migration,
            Limits = SemanticMaintenanceLimits() with
            {
                PageSize = 8,
                MaximumBytes = Math.Min(
                    SemanticMaintenanceLimits().MaximumBytes,
                    executionLimits.MaximumTransientBytes),
            },
        };
        BaseResult<BaseSemanticActivationMaintenanceResult> result =
            await store.ExecuteAsync(request, default);
        InMemorySemanticMaintenanceAccounting accounting = store.LastSemanticMaintenanceAccounting
            ?? throw new InvalidOperationException(
                $"Missing semantic maintenance accounting: {result.Status}:{(result as BaseFailure<BaseSemanticActivationMaintenanceResult>)?.Error.Code}");
        return (result, accounting, cloneReached);
    }

    private static async ValueTask VerifyEmptyRemovalAccountingBoundaryAsync(int dimension)
    {
        BaseSemanticActivationExecutionLimits generous = SemanticEnsureProbe.CreateLimits();
        (BaseResult<BaseSemanticActivationMaintenanceResult> baselineResult,
            InMemorySemanticMaintenanceAccounting baselineAccounting, _) =
            await ExecuteEmptySemanticRemovalWithLimitsAsync(generous, "accounting-boundary");
        baselineResult.RequireValue().Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
        long baseline = SemanticMaintenanceAccountingValue(baselineAccounting, dimension);
        long exact = 0;
        BaseResult<BaseSemanticActivationMaintenanceResult>? acceptedAtBoundary = null;
        bool acceptedCloneAtBoundary = false;
        foreach (long candidate in SemanticMaintenanceCandidates(baseline))
        {
            (BaseResult<BaseSemanticActivationMaintenanceResult> candidateResult,
                InMemorySemanticMaintenanceAccounting _, bool candidateClone) =
                await ExecuteEmptySemanticRemovalWithLimitsAsync(
                    WithSemanticMaintenanceLimit(generous, dimension, candidate),
                    "accounting-boundary");
            if (candidateResult is not BaseSuccess<BaseSemanticActivationMaintenanceResult>
                || !candidateClone)
                continue;
            if (candidate == 1)
            {
                exact = candidate; acceptedAtBoundary = candidateResult;
                acceptedCloneAtBoundary = candidateClone; break;
            }
            (BaseResult<BaseSemanticActivationMaintenanceResult> adjacent,
                InMemorySemanticMaintenanceAccounting adjacentAccounting, bool adjacentClone) =
                await ExecuteEmptySemanticRemovalWithLimitsAsync(
                    WithSemanticMaintenanceLimit(generous, dimension, candidate - 1),
                    "accounting-boundary");
            if (adjacent is BaseFailure<BaseSemanticActivationMaintenanceResult> adjacentFailure
                && adjacentFailure.Error.Code == BaseSemanticActivationErrorCodes.BudgetExceeded
                && !adjacentClone
                && SemanticMaintenanceAccountingValue(adjacentAccounting, dimension) > candidate - 1)
            {
                exact = candidate; acceptedAtBoundary = candidateResult;
                acceptedCloneAtBoundary = candidateClone; break;
            }
        }
        exact.Should().BeGreaterThan(0, "a fresh immutable capability boundary must exist");
        if (exact == 1)
        {
            BaseSemanticActivationCapabilityContract.IsValid(SemanticMaintenanceCapability(
                WithSemanticMaintenanceLimit(generous, dimension, 0))).Should().BeFalse();
            acceptedAtBoundary!.RequireValue().Disposition.Should().Be(
                BaseSemanticActivationMaintenanceDisposition.Completed);
            acceptedCloneAtBoundary.Should().BeTrue();
            return;
        }
        (BaseResult<BaseSemanticActivationMaintenanceResult> rejected,
            InMemorySemanticMaintenanceAccounting rejectedAccounting, bool rejectedClone) =
            await ExecuteEmptySemanticRemovalWithLimitsAsync(
                WithSemanticMaintenanceLimit(generous, dimension, exact - 1), "accounting-boundary");
        Assert.IsType<BaseFailure<BaseSemanticActivationMaintenanceResult>>(rejected).Error.Code
            .Should().Be(BaseSemanticActivationErrorCodes.BudgetExceeded);
        SemanticMaintenanceAccountingValue(rejectedAccounting, dimension).Should().BeGreaterThan(exact - 1);
        rejectedClone.Should().BeFalse();
        BaseSemanticActivationMaintenanceResult accepted = acceptedAtBoundary!.RequireValue();
        accepted.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
        acceptedCloneAtBoundary.Should().BeTrue();
    }

    private static long SemanticMaintenanceAccountingValue(
        InMemorySemanticMaintenanceAccounting accounting,
        int dimension) => dimension switch
    {
        0 => accounting.ReadIntervals,
        1 => accounting.IndexOperations,
        2 => accounting.EvidenceBytes,
        3 => accounting.ReceiptBytes,
        4 => accounting.TransientBytes,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

    private static ImmutableArray<byte> EmptyOrderedSemanticAuthorityChecksum()
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.orderedRows.v1\0"u8);
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static ImmutableArray<byte> EmptySemanticDefinitionStateChecksum()
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.definitionState.v1\0"u8);
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static BaseActivationExecutionLimits ActivationLimits() => new()
    {
        MaximumCandidates = 8,
        MaximumInputBytes = 4096,
        MaximumResultBytes = 4096,
        MaximumEvidenceBytes = 4096,
        MaximumTransientBytes = 16384,
        MaximumReadIntervals = 8,
        MaximumIndexOperations = 16,
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitObservationTimeout = TimeSpan.FromSeconds(5),
        ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
    };

    private static BaseActivationLimits SemanticCreationLimits() => new()
    {
        MaximumInputBytes = 4096,
        MaximumResultBytes = 4096,
        MaximumAttempts = 3, MaximumYields = 0,
        MaximumRenewalsPerSlice = 3,
        MaximumChildrenPerSlice = 8,
        MaximumLineageDepth = 8,
        LeaseDuration = TimeSpan.FromMinutes(1),
        HandlerTimeout = TimeSpan.FromMinutes(1),
        Provider = ActivationLimits(),
        AtomicCreation = ModuleLimits(),
    };

    private static BaseAcceptedTimeReceipt AcceptedTime(long milliseconds)
    {
        const string applicationId = "activation-test";
        const long generation = 1;
        long monotonic = milliseconds;
        long sequence = checked(milliseconds + 1);
        const long maximumForwardSkew = 30_000;
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        Append(hash, "base.activation.acceptedTime.v2\0");
        Append(hash, applicationId);
        Append(hash, generation);
        Append(hash, milliseconds);
        Append(hash, monotonic);
        Append(hash, sequence);
        Append(hash, maximumForwardSkew);
        return new BaseAcceptedTimeReceipt(
            applicationId, generation, milliseconds, monotonic, sequence, maximumForwardSkew, hash.GetHashAndReset());
    }

    private static void Append(System.Security.Cryptography.IncrementalHash hash, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length));
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void Append(System.Security.Cryptography.IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static BaseMutationRequestIdentity RequestIdentity(string id) =>
        BaseMutationRequestIdentity.Create(
            "activation-test", "activation", id, BaseMutationRequestFingerprint.Create(new byte[32]));

    private static BaseModuleGenerationCellDefinition ModuleCell() => new()
    {
        Id = "module.generation", Version = 1, OwningModuleId = "module",
        Scope = BaseModuleGenerationScope.Application, MaximumKeyUtf8Bytes = 32,
        MaximumCellsPerOperation = 1,
    };

    private static AtomicMutationProcessingResult ProbeFailure(BaseError? error) => new(
        AtomicMutationProcessingOutcome.Failed, [], error ?? new BaseError
        {
            Code = BaseSubjectErrorCodes.ProviderContractInvalid,
            Message = "The prepared-operation probe intentionally rolled back.",
            Category = ErrorCategory.Store,
        });

    private sealed class PreparedModuleProbe(
        BaseAtomicMutationAuthorityRequirement authority,
        BaseAtomicMutationExecutionLimits limits,
        bool applyTwice) : IAtomicMutationProcessor
    {
        public BasePreparedAtomicExecution? Prepared { get; private set; }
        public string? RejectedCode { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            var capture = new BaseAtomicExecutionRequest
            {
                Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
                Intent = new BaseAtomicMutationIntent
                {
                    IntentDigest = "in-memory-l50-probe-intent", Authority = authority, Items = [],
                },
                Module = new BaseModuleMutationCaptureExtension
                {
                    OperationId = "module.increment", OperationVersion = 1,
                    OperationChecksum = new string('a', 64), RequestDigest = "in-memory-l50-probe-request",
                    Records = [], RelationTargets = [],
                    Generations = [new BaseModuleGenerationCaptureRequest
                    {
                        Ordinal = 0, CaptureId = "generation", Cell = ModuleCell(),
                        Scope = new BaseModuleGenerationScopeAuthority { Kind = BaseModuleGenerationScope.Application },
                        KeyUtf8 = [], Absence = BaseModuleGenerationAbsenceBehavior.AllowEither,
                    }],
                },
                Limits = limits,
            };
            OperationResult<BaseCapturedAtomicExecution> captured =
                await session.CaptureAtomicExecutionAsync(capture, cancellationToken);
            if (!captured.IsSuccess() || captured.Value is null)
            {
                RejectedCode = captured.Error?.Code;
                return ProbeFailure(captured.Error);
            }
            var plan = new BaseFinalizedAtomicExecutionPlan
            {
                Kind = BaseAtomicMutationExecutionKind.ModuleMutation, PlanDigest = "in-memory-l50-probe-plan",
                IntentDigest = capture.Intent.IntentDigest, CaptureDigest = captured.Value.CaptureDigest,
                PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]), Authority = authority,
                Items = [], SubjectValidations = [], Limits = limits,
                Module = new BaseFinalizedModuleMutationExtension
                {
                    OperationId = "module.increment", OperationVersion = 1,
                    OperationChecksum = new string('a', 64), Decisions = [], ItemBindings = [],
                    RelationTargets = [], Comparisons = [new BaseModuleGenerationComparison
                    {
                        CaptureOrdinal = 0, Kind = BaseModuleGenerationComparisonKind.MustBeMissing,
                    }],
                    Increments = [new BaseModuleGenerationIncrement { CaptureOrdinal = 0, CreateIfAbsent = true }],
                    ResultProjectionDigest = "in-memory-l50-probe-result",
                },
            };
            OperationResult<BasePreparedAtomicExecution> prepared =
                await session.PrepareAtomicExecutionAsync(captured.Value, plan, cancellationToken);
            if (!prepared.IsSuccess() || prepared.Value is null)
            {
                RejectedCode = prepared.Error?.Code;
                return ProbeFailure(prepared.Error);
            }
            Prepared = prepared.Value;
            if (!applyTwice) return ProbeFailure(null);
            OperationResult<BaseProvisionalAtomicExecution> first =
                await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken);
            if (!first.IsSuccess()) return ProbeFailure(first.Error);
            OperationResult<BaseProvisionalAtomicExecution> second =
                await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken);
            RejectedCode = second.Error?.Code;
            return ProbeFailure(second.Error);
        }
    }

    private sealed class ActivationCreationProbe(
        BaseAtomicMutationAuthorityRequirement authority,
        BaseAtomicMutationExecutionLimits limits,
        string inputText = "activation-input",
        string activationId = "activation-1",
        long maximumYields = 0) : IAtomicMutationProcessor
    {
        public bool CapturedExisting { get; private set; }
        public int ProvisionalCount { get; private set; }
        public string? RejectedCode { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes(inputText);
            var extension = new BaseActivationCreationExtension
            {
                StructuralDigest = System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(activationId)).ToImmutableArray(),
                Items = [new BaseActivationCreateIntent
                {
                    Ordinal = 0,
                    Definition = new BaseActivationDefinitionKey
                    {
                        Id = "test.activation", Version = 1, Checksum = new byte[32].ToImmutableArray(),
                    },
                    ReceiptRetention = new BaseActivationReceiptRetentionPolicy
                    {
                        FormatVersion = 1,
                        DuplicateResolutionLifetime = TimeSpan.FromHours(24),
                        ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
                    },
                    CanonicalInput = input.ToImmutableArray(),
                    InputChecksum = System.Security.Cryptography.SHA256.HashData(input).ToImmutableArray(),
                    Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
                    RequestedDueAt = 1,
                    EffectiveDueAt = 1,
                    MaximumYields = maximumYields,
                    Identity = BaseMutationRequestIdentity.Create(
                        "activation-test", "enqueue", activationId,
                        BaseMutationRequestFingerprint.Create(new byte[32])),
                }],
            };
            var request = new BaseAtomicExecutionRequest
            {
                Kind = BaseAtomicMutationExecutionKind.ActivationCreation,
                Intent = new BaseAtomicMutationIntent
                {
                    IntentDigest = "activation-intent", Authority = authority, Items = [],
                },
                Activations = extension,
                Limits = limits,
            };
            OperationResult<BaseCapturedAtomicExecution> captured =
                await session.CaptureAtomicExecutionAsync(request, cancellationToken);
            if (!captured.IsSuccess() || captured.Value?.Activations is null)
            {
                RejectedCode = captured.Error?.Code;
                return ProbeFailure(captured.Error);
            }
            CapturedExisting = captured.Value.Activations.Items[0].Exists;
            var plan = new BaseFinalizedAtomicExecutionPlan
            {
                Kind = BaseAtomicMutationExecutionKind.ActivationCreation,
                PlanDigest = "activation-plan",
                IntentDigest = request.Intent.IntentDigest,
                CaptureDigest = captured.Value.CaptureDigest,
                PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]),
                Authority = authority,
                Items = [], SubjectValidations = [], Activations = extension, Limits = limits,
            };
            OperationResult<BasePreparedAtomicExecution> prepared =
                await session.PrepareAtomicExecutionAsync(captured.Value, plan, cancellationToken);
            if (!prepared.IsSuccess() || prepared.Value?.Activations is null)
                return ProbeFailure(prepared.Error);
            OperationResult<BaseProvisionalAtomicExecution> applied =
                await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken);
            if (!applied.IsSuccess() || applied.Value?.Activations is null)
            {
                RejectedCode = applied.Error?.Code;
                return ProbeFailure(applied.Error);
            }
            ProvisionalCount = applied.Value.Activations.Items.Length;
            return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, []);
        }
    }

    private sealed class SemanticEnsureProbe(
        BaseAtomicMutationAuthorityRequirement authority,
        BaseAtomicMutationExecutionLimits limits,
        string parentIdentity,
        bool retire = false,
        BaseSemanticActivationExecutionLimits? semanticLimits = null,
        BaseActivationLimits? activationLimits = null,
        long acceptedTime = 1,
        string semanticKey = "auth-user-42",
        int definitionVersion = 1,
        string definitionChecksumSeed = "semantic-definition",
        string definitionId = "test.semantic",
        BaseSemanticActivationKeyDefinition? installedDefinition = null) : IAtomicMutationProcessor
    {
        public BaseSemanticActivationCapturedState? CapturedState { get; private set; }
        public BaseCapturedSemanticActivationEvidence? CapturedEvidence { get; private set; }
        public BaseAtomicSemanticActivationExtension? FinalizedExtension { get; private set; }
        public BaseProvisionalSemanticActivation? Provisional { get; private set; }
        public string RejectedCode { get; private set; } = string.Empty;

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            BaseSemanticActivationKeyDefinition effectiveDefinition = installedDefinition
                ?? SemanticDefinition(definitionVersion, definitionChecksumSeed, definitionId);
            byte[] definitionChecksum = effectiveDefinition.Checksum.ToArray();
            definitionVersion = effectiveDefinition.Version;
            definitionId = effectiveDefinition.Id;
            byte[] activationChecksum = System.Security.Cryptography.SHA256.HashData("activation-definition"u8);
            byte[] canonicalKey = System.Text.Encoding.UTF8.GetBytes(semanticKey);
            byte[] binding = System.Security.Cryptography.SHA256.HashData("runtime-proposed-binding"u8);
            BaseSemanticActivationKeyDigest key = SemanticKey(definitionId, binding, canonicalKey);
            Span<byte> keyBytes = stackalloc byte[32];
            key.CopyTo(keyBytes);
            byte[] activationId = SemanticHash("base.semanticActivation.activation.v1\0",
                System.Text.Encoding.UTF8.GetBytes(authority.ApplicationId), System.Text.Encoding.UTF8.GetBytes(authority.StoreInstanceId),
                "test"u8.ToArray(), System.Text.Encoding.UTF8.GetBytes(definitionId), binding, canonicalKey);
            var definition = new BaseSemanticActivationDefinitionIdentity
            {
                Id = definitionId, Version = definitionVersion, Checksum = definitionChecksum.ToImmutableArray(), OwnerGeneration = 1,
                OwningModuleId = "test",
                RetirementOperation = new BaseSemanticActivationModuleOperationIdentity
                {
                    OperationId = "semantic.retire", OperationVersion = 1,
                    OperationChecksum = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("completion-operation"u8)),
                },
            };
            var scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global };
            var due = new BaseSemanticActivationDueAuthority
            {
                Mode = BaseSemanticActivationDueMode.ExplicitUtcInstant, CanonicalUnixMilliseconds = 1,
            };
            byte[] creationChecksum = SemanticHash("base.semanticActivation.creation.v1\0", definitionChecksum, keyBytes.ToArray(), binding, activationId);
            var ensure = new BaseSemanticActivationEnsureIntent
            {
                Definition = definition, Key = key, CanonicalKey = canonicalKey.ToImmutableArray(), Scope = scope, Due = due,
                Activation = new BaseSemanticActivationCreateIntent
                {
                    Definition = new BaseActivationDefinitionKey { Id = "test.activation", Version = 1, Checksum = activationChecksum.ToImmutableArray() },
                    ReceiptRetention = new BaseActivationReceiptRetentionPolicy
                    {
                        FormatVersion = 1,
                        DuplicateResolutionLifetime = TimeSpan.FromHours(24),
                        ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
                    },
                    CanonicalInput = "payload"u8.ToArray().ToImmutableArray(),
                    InputChecksum = System.Security.Cryptography.SHA256.HashData("payload"u8).ToImmutableArray(),
                    Scope = scope, Due = due, Priority = 0, InitiallyEligible = true,
                    Limits = activationLimits ?? SemanticCreationLimits(),
                    Identity = new BaseSemanticActivationCreationIdentity
                    {
                        SemanticDefinition = definition, Key = key, ScopeBindingId = binding.ToImmutableArray(),
                        DerivedActivationIdBytes = activationId.ToImmutableArray(), Checksum = creationChecksum.ToImmutableArray(),
                    },
                },
            };
            BaseSemanticActivationOperation operation = retire
                ? new BaseSemanticActivationRetireIntent
                {
                    Definition = definition, Key = key, CanonicalKey = canonicalKey.ToImmutableArray(), Scope = scope,
                    CompletionOperation = new BaseSemanticActivationModuleOperationIdentity
                    {
                        OperationId = "semantic.retire", OperationVersion = 1,
                        OperationChecksum = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("completion-operation"u8)),
                    },
                }
                : ensure;
            BaseSemanticActivationExecutionLimits effectiveSemanticLimits = semanticLimits ?? CreateLimits();
            var extension = new BaseAtomicSemanticActivationExtension
            {
                Capture = new BaseSemanticActivationCaptureRequest
                {
                    Definition = definition, CanonicalKey = canonicalKey.ToImmutableArray(),
                    KeyPreimageChecksum = System.Security.Cryptography.SHA256.HashData(canonicalKey).ToImmutableArray(),
                    Scope = scope, ProposedScopeBindingId = binding.ToImmutableArray(), Operation = retire
                        ? BaseSemanticActivationOperationKind.Retire : BaseSemanticActivationOperationKind.Ensure,
                    StoreAuthority = new BaseSemanticActivationStoreAuthorityRequirement
                    {
                        ApplicationId = authority.SemanticActivation!.ApplicationId,
                        LogicalStoreId = authority.SemanticActivation.LogicalStoreId,
                        StoreInstanceId = authority.SemanticActivation.StoreInstanceId,
                        RestoreEpoch = authority.SemanticActivation.RestoreEpoch,
                        SchemaGeneration = authority.SemanticActivation.SchemaGeneration,
                        SemanticAuthorityGeneration = authority.SemanticActivation.SemanticAuthorityGeneration,
                        DefinitionSetChecksum = authority.SemanticActivation.DefinitionSetChecksum,
                    },
                    Limits = effectiveSemanticLimits,
                    AcceptedTime = AcceptedTime(acceptedTime),
                },
                Operation = operation,
                StructuralDigest = SemanticHash("base.semanticActivation.extension.v1\0", definitionChecksum, canonicalKey, binding, [retire ? (byte)2 : (byte)1]).ToImmutableArray(),
            };
            var request = new BaseAtomicExecutionRequest
            {
                Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
                Intent = new BaseAtomicMutationIntent { IntentDigest = parentIdentity, Authority = authority, Items = [] },
                Module = new BaseModuleMutationCaptureExtension
                {
                    OperationId = retire ? "semantic.retire" : "semantic.ensure", OperationVersion = 1, OperationChecksum = new string('a', 64),
                    RequestDigest = parentIdentity, Records = [], RelationTargets = [], Generations = [],
                },
                SemanticActivation = extension, Limits = limits,
            };
            OperationResult<BaseCapturedAtomicExecution> captured = await session.CaptureAtomicExecutionAsync(request, cancellationToken);
            if (!captured.IsSuccess() || captured.Value?.SemanticActivation is null) { RejectedCode = captured.Error?.Code ?? "capture"; return ProbeFailure(captured.Error); }
            CapturedState = captured.Value.SemanticActivation.State;
            CapturedEvidence = captured.Value.SemanticActivation;
            var plan = new BaseFinalizedAtomicExecutionPlan
            {
                Kind = request.Kind, PlanDigest = $"semantic-plan-{parentIdentity}", IntentDigest = request.Intent.IntentDigest,
                CaptureDigest = captured.Value.CaptureDigest, PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]),
                Authority = authority, Items = [], SubjectValidations = [], Limits = limits, SemanticActivation = extension,
                Module = new BaseFinalizedModuleMutationExtension
                {
                    OperationId = retire ? "semantic.retire" : "semantic.ensure", OperationVersion = 1, OperationChecksum = new string('a', 64),
                    Decisions = [], ItemBindings = [], RelationTargets = [], Comparisons = [], Increments = [],
                    ResultProjectionDigest = parentIdentity,
                },
            };
            FinalizedExtension = extension;
            OperationResult<BasePreparedAtomicExecution> prepared = await session.PrepareAtomicExecutionAsync(captured.Value, plan, cancellationToken);
            if (!prepared.IsSuccess() || prepared.Value?.SemanticActivation is null) { RejectedCode = prepared.Error?.Code ?? "prepare"; return ProbeFailure(prepared.Error); }
            OperationResult<BaseProvisionalAtomicExecution> applied = await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken);
            if (!applied.IsSuccess() || applied.Value?.SemanticActivation is null) { RejectedCode = applied.Error?.Code ?? "apply"; return ProbeFailure(applied.Error); }
            Provisional = applied.Value.SemanticActivation;
            return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, []);
        }

        internal static BaseSemanticActivationExecutionLimits CreateLimits() => new()
        {
            MaximumOperations = 1, MaximumScopeDirectoryReads = 1, MaximumSlotReads = 1, MaximumActivationReads = 1,
            MaximumReadIntervals = 8, MaximumIndexOperations = 4096, MaximumActivationBytes = 4096,
            MaximumScopeDirectoryBytes = 4096, MaximumEvidenceBytes = 16384, MaximumReceiptBytes = 4096,
            MaximumTransientBytes = 262144,
        };

        private static BaseSemanticActivationKeyDigest SemanticKey(string id, byte[] binding, byte[] key) =>
            BaseSemanticActivationKeyDigest.Create(SemanticHash("base.semanticActivation.key.v1\0", System.Text.Encoding.UTF8.GetBytes(id), binding, key));

        private static byte[] SemanticHash(string marker, params byte[][] parts)
        {
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(marker));
            byte[] length = new byte[4];
            foreach (byte[] part in parts)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, part.Length);
                hash.AppendData(length); hash.AppendData(part);
            }
            return hash.GetHashAndReset();
        }
    }

    private sealed class ForeignPreparedModuleProbe(BasePreparedAtomicExecution prepared) : IAtomicMutationProcessor
    {
        public string? RejectedCode { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            OperationResult<BaseProvisionalAtomicExecution> result =
                await session.ApplyPreparedAtomicExecutionAsync(prepared, cancellationToken);
            RejectedCode = result.Error?.Code;
            return ProbeFailure(result.Error);
        }
    }

    private sealed class CallbackProcessor(
        Func<IAtomicRecordSession, CancellationToken, ValueTask<AtomicMutationProcessingResult>> callback)
        : IAtomicMutationProcessor
    {
        public int InvocationCount { get; private set; }

        public ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return callback(session, cancellationToken);
        }
    }
}
