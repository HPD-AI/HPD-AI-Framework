using System.Text.Json.Serialization;
using System.Collections.Immutable;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed partial class SqliteModuleMutationTests
{
    [Fact]
    public async Task Activation_creation_commits_replays_conflicts_and_survives_restart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l51-activation-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using (SqliteRecordStore store = Store(path))
            {
                BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                    "activation.application", [], limits, default)).Value!;
                var first = new ActivationCreationProbe(authority, limits);
                var duplicate = new ActivationCreationProbe(authority, limits);
                var conflict = new ActivationCreationProbe(authority, limits, "changed-input");

                RecordMutationExecutionResult committed = await store.ExecuteAtomicAsync(first, ExecutionRequest());
                RecordMutationExecutionResult replayed = await store.ExecuteAtomicAsync(duplicate, ExecutionRequest());
                RecordMutationExecutionResult rejected = await store.ExecuteAtomicAsync(conflict, ExecutionRequest());

                committed.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                replayed.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                first.CapturedExisting.Should().BeFalse();
                duplicate.CapturedExisting.Should().BeTrue();
                first.ProvisionalCount.Should().Be(1);
                duplicate.ProvisionalCount.Should().Be(1);
                rejected.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
                conflict.RejectedCode.Should().Be("base.activation.fingerprintConflict");
            }

            await using (SqliteRecordStore reopened = Store(path))
            {
                BaseAtomicMutationAuthorityRequirement authority = (await reopened.CaptureAtomicMutationAuthorityRequirementAsync(
                    "activation.application", [], limits, default)).Value!;
                var duplicate = new ActivationCreationProbe(authority, limits);

                RecordMutationExecutionResult replayed = await reopened.ExecuteAtomicAsync(duplicate, ExecutionRequest());
                BaseActivationDependencyResult dependencies = (await reopened.ReadDependenciesAsync(
                    new BaseActivationDependencyRequest
                    {
                        ApplicationId = "activation.application", MaximumDefinitions = 8,
                        DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
                    })).Value!;

                replayed.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                duplicate.CapturedExisting.Should().BeTrue();
                duplicate.ProvisionalCount.Should().Be(1);
                dependencies.Dependencies.Should().ContainSingle(item =>
                    item.ReferencedByActivation && !item.ReferencedBySchedule
                    && item.Definition.Id == ActivationDefinition().Id
                    && item.Definition.Version == ActivationDefinition().Version);
            }
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Activation_due_claim_renew_and_completion_are_transactional_and_persistent()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l51-claim-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits mutationLimits = ExecutionLimits();
            BaseActivationExecutionLimits limits = ActivationLimits();
            BaseOwnedScopeSeekAuthority scope = ActivationScope();
            BaseActivationDefinitionKey definition = ActivationDefinition();
            await using (SqliteRecordStore store = Store(path))
            {
                BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                    "activation.application", [], mutationLimits, default)).Value!;
                var creation = new ActivationCreationProbe(authority, mutationLimits);
                (await store.ExecuteAtomicAsync(creation, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);

                BaseActivationDueObservation observed = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
                {
                    ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
                    AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = limits,
                })).Value!;
                var worker = new BaseActivationWorkerAuthority
                {
                    ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker-1",
                    Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
                };
                var claimRequest = new BaseActivationClaimRequest
                {
                    Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(10), LeaseMilliseconds = 1_000,
                    Identity = ActivationIdentity("claim"), Limits = limits,
                };
                var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(claimRequest)).Value!;
                claimed.Payload.RequestedDueAt.Should().Be(1);
                claimed.Payload.EffectiveDueAt.Should().Be(1);
                claimed.Payload.OccurrenceId.Should().BeNull();
                (await store.TryClaimNextAsync(claimRequest with { AcceptedTime = AcceptedTime(11) })).Value
                    .Should().BeOfType<BaseActivationClaimedResult>();
                var renewRequest = new BaseActivationRenewRequest
                {
                    Claim = claimed.Claim, ExpectedLeaseRevision = 1, AcceptedTime = AcceptedTime(20), ExtensionMilliseconds = 2_000,
                    Identity = ActivationIdentity("renew"), Limits = limits,
                };
                BaseActivationRenewResult renewed = (await store.RenewAsync(renewRequest)).Value!;
                renewed.Claim.FencingToken.Should().Equal(claimed.Claim.FencingToken);
                renewed.Lease.LeaseRevision.Should().Be(2);
                (await store.RenewAsync(renewRequest with { AcceptedTime = AcceptedTime(21) })).Value!.Disposition
                    .Should().Be(BaseMutationRequestDisposition.Duplicate);
                byte[] result = "done"u8.ToArray();
                var completeRequest = new BaseActivationCompleteRequest
                {
                    ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim,
                    CanonicalResult = result.ToImmutableArray(), ResultChecksum = System.Security.Cryptography.SHA256.HashData(result).ToImmutableArray(),
                    AcceptedTime = AcceptedTime(30), Identity = ActivationIdentity("complete"), Limits = limits,
                };
                BaseActivationTransitionResult completed = (await store.TransitionAsync(completeRequest)).Value!;
                completed.State.Should().Be(BaseActivationState.Succeeded);
                BaseActivationAdministrationPage administration = (await store.ReadAdministrationAsync(
                    new BaseActivationAdministrationQueryRequest
                    {
                        ApplicationId = "activation-test", Scope = scope, Definition = definition,
                        States = BaseActivationStateSelector.Terminal, Take = 8,
                        AcceptedTime = AcceptedTime(30), Limits = limits,
                    })).Value!;
                administration.Items.Should().ContainSingle(item =>
                    item.ActivationId == claimed.Claim.ActivationId && item.State == BaseActivationState.Succeeded);
                administration.Intervals.Should().ContainSingle(interval =>
                    interval.LogicalAccessPathId == "base.activation.administration.byScopeDefinitionStateDue.v1");
                BaseOwnedScopeSeekAuthority foreignScope = scope with
                {
                    ProtectedIndexDigest = System.Security.Cryptography.SHA256.HashData(
                        "base.activation.scope.v2\0\u0002\nforeign"u8).ToImmutableArray(),
                };
                (await store.ReadAdministrationAsync(new BaseActivationAdministrationQueryRequest
                {
                    ApplicationId = "activation-test", Scope = foreignScope, Definition = definition,
                    States = BaseActivationStateSelector.All, Take = 8,
                    AcceptedTime = AcceptedTime(30), Limits = limits,
                })).Value!.Items.Should().BeEmpty();
                BaseActivationReceiptResolution resolvedClaim = (await store.ResolveReceiptAsync(
                    new BaseActivationReceiptResolutionRequest
                    {
                        Identity = claimRequest.Identity,
                        AcceptedTime = AcceptedTime(30),
                        Limits = limits,
                    })).Value!;
                resolvedClaim.OperationKind.Should().Be("activation-claimed");
                System.Text.Json.JsonSerializer.Deserialize(
                        resolvedClaim.CanonicalResult.AsSpan(),
                        HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult)
                    .Should().BeOfType<BaseActivationClaimTerminalResult>();
                (await store.TransitionAsync(completeRequest with { AcceptedTime = AcceptedTime(31) })).Value!.Disposition
                    .Should().Be(BaseMutationRequestDisposition.Duplicate);
                (await store.TryClaimNextAsync(claimRequest with { AcceptedTime = AcceptedTime(32) })).Value
                    .Should().BeOfType<BaseActivationClaimTerminalResult>();
            }

            await using (SqliteRecordStore reopened = Store(path))
            {
                BaseActivationDueObservation terminal = (await reopened.ObserveDueAsync(new BaseActivationDueObservationRequest
                {
                    ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
                    AcceptedTime = AcceptedTime(40), MaximumCandidates = 8, Limits = limits,
                })).Value!;
                terminal.Earliest.Should().BeNull();
            }
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Durable_yield_reclaims_the_same_attempt_with_a_new_slice()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l76-yield-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits mutationLimits = ExecutionLimits();
            BaseActivationExecutionLimits limits = ActivationLimits();
            BaseOwnedScopeSeekAuthority scope = ActivationScope();
            BaseActivationDefinitionKey definition = ActivationDefinition();
            await using SqliteRecordStore store = Store(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation.application", [], mutationLimits, default)).Value!;
            (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, mutationLimits, maximumYields: 2), ExecutionRequest()))
                .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            BaseActivationYieldReservationState reserved = (await store.ReadYieldReservationStateAsync()).Value!;
            reserved.Generation.Should().Be(1);
            reserved.ReservedUnusedSlots.Should().Be(3);
            reserved.RetainedUsedSlots.Should().Be(0);
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
                Identity = ActivationIdentity("yield-claim-1"), Limits = limits,
            })).Value!;
            BaseActivationTransitionResult yielded = (await store.TransitionAsync(new BaseActivationYieldRequest
            {
                ActivationId = first.Claim.ActivationId, Claim = first.Claim, RequestedResumeAt = DateTimeOffset.FromUnixTimeMilliseconds(12),
                EffectiveDueAt = 12, ProgressFingerprint = SHA256.HashData("progress-1"u8).ToImmutableArray(),
                ExpectedYieldCount = 0, MaximumYields = 2, AcceptedTime = AcceptedTime(11),
                Identity = ActivationIdentity("yield-1"), Limits = limits,
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
                Identity = ActivationIdentity("yield-claim-2"), Limits = limits,
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
                CanonicalResult = resultBytes.ToImmutableArray(), ResultChecksum = SHA256.HashData(resultBytes).ToImmutableArray(),
                AcceptedTime = AcceptedTime(13), Identity = ActivationIdentity("yield-complete"), Limits = limits,
            })).IsSuccess().Should().BeTrue();
            BaseActivationYieldReservationState released = (await store.ReadYieldReservationStateAsync()).Value!;
            released.Generation.Should().Be(3);
            released.ReservedUnusedSlots.Should().Be(0);
            released.RetainedUsedSlots.Should().Be(1);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Expired_nonterminal_yield_receipt_compacts_only_after_a_later_slice()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l76-compact-yield-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits mutationLimits = ExecutionLimits();
            BaseActivationExecutionLimits limits = ActivationLimits();
            BaseOwnedScopeSeekAuthority scope = ActivationScope();
            BaseActivationDefinitionKey definition = ActivationDefinition();
            await using SqliteRecordStore store = Store(path, semanticApplicationId: "activation-test");
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation.application", [], mutationLimits, default)).Value!;
            (await store.ExecuteAtomicAsync(
                new ActivationCreationProbe(authority, mutationLimits, maximumYields: 2), ExecutionRequest()))
                .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            BaseActivationWorkerAuthority worker = new()
            {
                ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "compaction-worker",
                Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
            };
            BaseActivationDueObservation firstObservation = (await store.ObserveDueAsync(new()
            {
                ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
                AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = limits,
            })).Value!;
            var first = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new()
            {
                Observation = firstObservation.Token, Worker = worker, AcceptedTime = AcceptedTime(10),
                LeaseMilliseconds = 1_000, Identity = ActivationIdentity("compact-claim-1"), Limits = limits,
            })).Value!;
            (await store.TransitionAsync(new BaseActivationYieldRequest
            {
                ActivationId = first.Claim.ActivationId, Claim = first.Claim,
                RequestedResumeAt = DateTimeOffset.FromUnixTimeMilliseconds(12), EffectiveDueAt = 12,
                ProgressFingerprint = SHA256.HashData("compact-progress"u8).ToImmutableArray(),
                ExpectedYieldCount = 0, MaximumYields = 2, AcceptedTime = AcceptedTime(11),
                Identity = ActivationIdentity("compact-yield"), Limits = limits,
            })).IsSuccess().Should().BeTrue();

            BaseActivationYieldReservationState pending = (await store.ReadYieldReservationStateAsync()).Value!;
            OperationResult<BaseActivationReceiptCompactionResult> protectedCurrent =
                await store.CompactActivationReceiptsAsync(CompactionRequest(
                    definition, scope, pending, 86_400_020, "compact-current"));
            protectedCurrent.IsSuccess().Should().BeTrue(protectedCurrent.Error?.Code);
            protectedCurrent.Value!.ExaminedCount.Should().Be(1);
            protectedCurrent.Value.DeletedCount.Should().Be(0);
            protectedCurrent.Value.ResultingReservation.Should().BeEquivalentTo(pending);

            BaseActivationDueObservation secondObservation = (await store.ObserveDueAsync(new()
            {
                ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
                AcceptedTime = AcceptedTime(86_400_021), MaximumCandidates = 8, Limits = limits,
            })).Value!;
            var second = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new()
            {
                Observation = secondObservation.Token, Worker = worker, AcceptedTime = AcceptedTime(86_400_021),
                LeaseMilliseconds = 1_000, Identity = ActivationIdentity("compact-claim-2"), Limits = limits,
            })).Value!;
            byte[] terminal = "compacted"u8.ToArray();
            BaseActivationTransitionResult completed = (await store.TransitionAsync(new BaseActivationCompleteRequest
            {
                ActivationId = second.Claim.ActivationId, Claim = second.Claim,
                CanonicalResult = terminal.ToImmutableArray(), ResultChecksum = SHA256.HashData(terminal).ToImmutableArray(),
                AcceptedTime = AcceptedTime(86_400_022), Identity = ActivationIdentity("compact-complete"), Limits = limits,
            })).Value!;
            BaseActivationYieldReservationState retained = (await store.ReadYieldReservationStateAsync()).Value!;
            retained.RetainedUsedSlots.Should().Be(1);

            BaseActivationReceiptCompactionRequest request = CompactionRequest(
                definition, scope, retained, 86_400_023, "compact-expired");
            OperationResult<BaseActivationReceiptCompactionResult> compacted =
                await store.CompactActivationReceiptsAsync(request);
            compacted.IsSuccess().Should().BeTrue(compacted.Error?.Code);
            compacted.Value!.ExaminedCount.Should().Be(1);
            compacted.Value.DeletedCount.Should().Be(1);
            compacted.Value.Completed.Should().BeTrue();
            compacted.Value.ResultingReservation.RetainedUsedSlots.Should().Be(0);
            compacted.Value.ResultingReservation.Generation.Should().Be(retained.Generation + 1);
            compacted.Value.ResultingChain.CurrentSequence.Should().Be(compacted.Value.PriorChain.CurrentSequence);
            compacted.Value.ResultingChain.OrderedChecksum.Should().Equal(compacted.Value.PriorChain.OrderedChecksum);
            compacted.Value.ResultingChain.Generation.Should().Be(compacted.Value.PriorChain.Generation + 1);

            OperationResult<BaseActivationReceiptCompactionResult> duplicate =
                await store.CompactActivationReceiptsAsync(request);
            duplicate.IsSuccess().Should().BeTrue(duplicate.Error?.Code);
            duplicate.Value!.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            duplicate.Value.DeletedCount.Should().Be(1);

            (await store.TransitionAsync(new BaseActivationDisposeRequest
            {
                ActivationId = second.Claim.ActivationId, ExpectedGeneration = completed.Generation,
                AcceptedTime = AcceptedTime(86_400_024), Identity = ActivationIdentity("compact-dispose"), Limits = limits,
            })).IsSuccess().Should().BeTrue();
            BaseActivationPruneRequest prune = new()
            {
                ApplicationId = "activation-test", Scope = scope, Definition = definition, Take = 1,
                AcceptedTime = AcceptedTime(86_400_025), Identity = ActivationIdentity("compact-prune-early"), Limits = limits,
            };
            OperationResult<BaseActivationPrunePage> earlyPrune = await store.PruneAsync(prune);
            earlyPrune.Status.Should().Be(OperationStatus.Conflict);
            earlyPrune.Error!.Code.Should().Be("base.activation.removalBlocked");
            OperationResult<BaseActivationPrunePage> pruned = await store.PruneAsync(prune with
            {
                AcceptedTime = AcceptedTime(172_800_030), Identity = ActivationIdentity("compact-prune-expired"),
            });
            pruned.IsSuccess().Should().BeTrue(pruned.Error?.Code);
            pruned.Value!.Items.Should().ContainSingle();
            await using SqliteRecordStore reopened = Store(path, semanticApplicationId: "activation-test");
            BaseActivationYieldReservationState reopenedReservation =
                (await reopened.ReadYieldReservationStateAsync()).Value!;
            reopenedReservation.Should().BeEquivalentTo(compacted.Value.ResultingReservation);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Expired_claim_maintenance_is_bounded_and_fenced()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l51-maintenance-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits mutationLimits = ExecutionLimits(); BaseActivationExecutionLimits limits = ActivationLimits();
            BaseOwnedScopeSeekAuthority scope = ActivationScope(); BaseActivationDefinitionKey definition = ActivationDefinition();
            await using SqliteRecordStore store = Store(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation.application", [], mutationLimits, default)).Value!;
            (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, mutationLimits), ExecutionRequest()))
                .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            BaseActivationDueObservation observed = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
            {
                ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
                AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = limits,
            })).Value!;
            _ = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new BaseActivationClaimRequest
            {
                Observation = observed.Token, Worker = new BaseActivationWorkerAuthority
                {
                    ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker",
                    Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
                },
                AcceptedTime = AcceptedTime(10), LeaseMilliseconds = 1,
                Identity = ActivationIdentity("maintenance-claim"), Limits = limits,
            })).Value!;

            var maintenance = new BaseActivationMaintenanceRequest
            {
                ApplicationId = "activation-test", Scope = scope, Definition = definition,
                Kind = BaseActivationMaintenanceKind.RecoverExpiredClaims, Take = 1,
                AcceptedTime = AcceptedTime(12), Identity = ActivationIdentity("maintenance-page"), Limits = limits,
            };
            BaseActivationMaintenancePage recovered = (await store.AdvanceMaintenanceAsync(maintenance)).Value!;

            recovered.Completed.Should().BeTrue();
            BaseActivationMaintenanceItem item = recovered.Items.Should().ContainSingle().Subject;
            item.PreviousState.Should().Be(BaseActivationState.Claimed);
            item.ResultingState.Should().Be(BaseActivationState.RetryPending);
            item.ResultingGeneration.Should().Be(item.PreviousGeneration + 1);
            BaseActivationMaintenancePage replay = (await store.AdvanceMaintenanceAsync(
                maintenance with { AcceptedTime = AcceptedTime(13) })).Value!;
            replay.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            replay.Items.Should().ContainSingle();
            (await store.AdvanceMaintenanceAsync(new BaseActivationMaintenanceRequest
            {
                ApplicationId = "activation-test", Scope = scope, Definition = definition,
                Kind = BaseActivationMaintenanceKind.RecoverExpiredClaims, Take = 1,
                AcceptedTime = AcceptedTime(13), Identity = ActivationIdentity("maintenance-empty"), Limits = limits,
            })).Value!.Items.Should().BeEmpty();
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Expired_claim_recovery_is_receipted_and_exactly_replayed()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-claim-recovery-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits mutationLimits = ExecutionLimits();
            BaseActivationExecutionLimits limits = ActivationLimits();
            BaseOwnedScopeSeekAuthority scope = ActivationScope();
            BaseActivationDefinitionKey definition = ActivationDefinition();
            await using SqliteRecordStore store = Store(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation.application", [], mutationLimits, default)).Value!;
            (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, mutationLimits), ExecutionRequest()))
                .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);

            var worker = new BaseActivationWorkerAuthority
            {
                ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker-1",
                Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
            };
            BaseActivationDueObservation firstObservation = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
            {
                ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
                AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = limits,
            })).Value!;
            _ = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new BaseActivationClaimRequest
            {
                Observation = firstObservation.Token, Worker = worker, AcceptedTime = AcceptedTime(10), LeaseMilliseconds = 1,
                Identity = ActivationIdentity("initial-claim"), Limits = limits,
            })).Value!;

            BaseActivationDueObservation expiredObservation = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
            {
                ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
                AcceptedTime = AcceptedTime(20), MaximumCandidates = 8, Limits = limits,
            })).Value!;
            var recovery = new BaseActivationClaimRequest
            {
                Observation = expiredObservation.Token, Worker = worker, AcceptedTime = AcceptedTime(20), LeaseMilliseconds = 1_000,
                Identity = ActivationIdentity("recover-claim"), Limits = limits,
            };
            BaseActivationClaimResult first = (await store.TryClaimNextAsync(recovery)).Value!;
            BaseActivationClaimResult replay = (await store.TryClaimNextAsync(recovery with
            {
                AcceptedTime = AcceptedTime(21),
            })).Value!;

            var recovered = first.Should().BeOfType<BaseActivationRecoveredClaimResult>().Subject;
            var replayed = replay.Should().BeOfType<BaseActivationRecoveredClaimResult>().Subject;
            replayed.ActivationId.Should().Be(recovered.ActivationId);
            replayed.ResultingGeneration.Should().Be(recovered.ResultingGeneration);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Outcome_unknown_effect_reconciliation_is_authority_bound_and_receipted()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-effect-reconcile-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits mutationLimits = ExecutionLimits();
            BaseActivationExecutionLimits limits = ActivationLimits();
            BaseOwnedScopeSeekAuthority scope = ActivationScope();
            BaseActivationDefinitionKey definition = ActivationDefinition();
            await using SqliteRecordStore store = Store(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation.application", [], mutationLimits, default)).Value!;
            (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, mutationLimits), ExecutionRequest()))
                .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            BaseActivationDueObservation observed = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
            {
                ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
                AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = limits,
            })).Value!;
            var worker = new BaseActivationWorkerAuthority
            {
                ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker-1",
                Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
            };
            var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new BaseActivationClaimRequest
            {
                Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(10), LeaseMilliseconds = 1_000,
                Identity = ActivationIdentity("effect-claim"), Limits = limits,
            })).Value!;
            BaseExecutorRegistrationResult executor = (await store.RegisterExecutorAsync(new BaseExecutorRegistrationRequest
            {
                ApplicationId = "activation-test", HostId = "host", ProcessIncarnationId = "process",
                WorkerDefinitionSetChecksum = new byte[32].ToImmutableArray(), RequestedHeartbeatMilliseconds = 100,
                AcceptedTime = AcceptedTime(20), Identity = ActivationIdentity("effect-executor"), Limits = limits,
            })).Value!;
            BaseActivationTransitionResult started = (await store.TransitionAsync(new BaseActivationBeginEffectRequest
            {
                ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim, Executor = executor.Executor,
                ExecutorHeartbeat = executor.Heartbeat, HeartbeatMilliseconds = 100, AcceptedTime = AcceptedTime(20),
                Identity = ActivationIdentity("effect-start"), Limits = limits,
            })).Value!;
            BaseActivationTransitionResult cancellation = (await store.TransitionAsync(new BaseActivationCancelRequest
            {
                ActivationId = claimed.Claim.ActivationId, ExpectedGeneration = started.Generation,
                Propagation = BaseCancellationPropagation.None, AcceptedTime = AcceptedTime(30),
                Identity = ActivationIdentity("effect-cancel"), Limits = limits,
            })).Value!;
            cancellation.State.Should().Be(BaseActivationState.EffectStarted);
            cancellation.Effect.Should().NotBeNull();
            BaseActivationTransitionResult unknown = (await store.TransitionAsync(new BaseActivationRecoverEffectRequest
            {
                ActivationId = claimed.Claim.ActivationId, Effect = started.Effect!, AcceptedTime = AcceptedTime(200),
                Identity = ActivationIdentity("effect-recover"), Limits = limits,
            })).Value!;
            byte[] evidence = "externally-verified"u8.ToArray();
            var request = new BaseActivationReconcileEffectRequest
            {
                ActivationId = claimed.Claim.ActivationId,
                ExpectedEffectStartGeneration = started.Effect!.EffectStartGeneration,
                ExpectedEffectChecksum = started.Effect.Checksum,
                ExpectedGeneration = unknown.Generation,
                Disposition = BaseEffectReconciliationDisposition.Exhausted,
                VerificationEvidence = evidence.ToImmutableArray(),
                VerificationChecksum = System.Security.Cryptography.SHA256.HashData(evidence).ToImmutableArray(),
                AcceptedTime = AcceptedTime(210), Identity = ActivationIdentity("effect-reconcile"), Limits = limits,
            };
            BaseActivationTransitionResult reconciled = (await store.TransitionAsync(request)).Value!;
            BaseActivationTransitionResult replayed = (await store.TransitionAsync(request with
            {
                AcceptedTime = AcceptedTime(220),
            })).Value!;

            reconciled.State.Should().Be(BaseActivationState.Exhausted);
            replayed.State.Should().Be(BaseActivationState.Exhausted);
            replayed.Generation.Should().Be(reconciled.Generation);
            replayed.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);

            var retry = new BaseActivationOperatorRetryRequest
            {
                ActivationId = claimed.Claim.ActivationId,
                ExpectedGeneration = reconciled.Generation,
                RetryDueAt = 230,
                AcceptedTime = AcceptedTime(225),
                Identity = ActivationIdentity("operator-retry"),
                Limits = limits,
            };
            BaseActivationTransitionResult retried = (await store.TransitionAsync(retry)).Value!;
            BaseActivationTransitionResult retriedReplay = (await store.TransitionAsync(retry with
            {
                AcceptedTime = AcceptedTime(226),
            })).Value!;
            retried.State.Should().Be(BaseActivationState.RetryPending);
            retriedReplay.Generation.Should().Be(retried.Generation);
            retriedReplay.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Activation_migration_atomically_terminalizes_source_creates_replacement_and_replays()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-activation-migration-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits mutationLimits = ExecutionLimits();
            BaseActivationExecutionLimits limits = ActivationLimits();
            BaseOwnedScopeSeekAuthority scope = ActivationScope();
            BaseActivationDefinitionKey sourceDefinition = ActivationDefinition();
            await using SqliteRecordStore store = Store(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation.application", [], mutationLimits, default)).Value!;
            (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, mutationLimits), ExecutionRequest()))
                .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            BaseActivationAdministrationPage sourcePage = (await store.ReadAdministrationAsync(new BaseActivationAdministrationQueryRequest
            {
                ApplicationId = "activation-test", Scope = scope, Definition = sourceDefinition,
                States = BaseActivationStateSelector.All, Take = 8, AcceptedTime = AcceptedTime(10), Limits = limits,
            })).Value!;
            BaseActivationAdministrationItem source = sourcePage.Items.Should().ContainSingle().Subject;
            BaseActivationMigrationCandidate candidate = (await store.ReadMigrationCandidateAsync(new BaseActivationMigrationCandidateRequest
            {
                ApplicationId = "activation-test", Scope = scope, SourceDefinition = sourceDefinition,
                ActivationId = source.ActivationId, ExpectedGeneration = source.Generation,
                AcceptedTime = AcceptedTime(11), Limits = limits,
            })).Value!;
            byte[] replacementInput = "{\"value\":\"migrated\"}"u8.ToArray();
            BaseActivationDefinitionKey target = sourceDefinition with
            {
                Id = "activation.target", Version = 2,
                Checksum = SHA256.HashData("activation.target.v2"u8).ToImmutableArray(),
            };
            var request = new BaseActivationMigrationRequest
            {
                ApplicationId = "activation-test", Scope = scope, SourceDefinition = sourceDefinition,
                SourceActivationId = source.ActivationId, ExpectedSourceGeneration = source.Generation,
                ExpectedSourceInputChecksum = candidate.InputChecksum, ReplacementActivationId = "replacement-activation",
                Replacement = new BaseActivationCreateIntent
                {
                    Ordinal = 0, Definition = target, CanonicalInput = replacementInput.ToImmutableArray(),
                    ReceiptRetention = DefaultReceiptRetention(),
                    InputChecksum = SHA256.HashData(replacementInput).ToImmutableArray(),
                    Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
                    RequestedDueAt = 12, EffectiveDueAt = 12, Priority = 0, OverlapKey = [],
                    OverlapPolicy = BaseScheduleOverlapPolicy.Allow, InitiallyEligible = true,
                    MaximumYields = 0,
                    Identity = ActivationIdentity("migration-replacement"),
                },
                MigrationId = "activation.migration", MigrationVersion = 1,
                MigrationChecksum = SHA256.HashData("activation.migration.v1"u8).ToImmutableArray(),
                AcceptedTime = AcceptedTime(12), Identity = ActivationIdentity("migration"), Limits = limits,
            };
            BaseActivationMigrationResult committed = (await store.MigrateAsync(request)).Value!;
            BaseActivationMigrationResult replayed = (await store.MigrateAsync(request with { AcceptedTime = AcceptedTime(13) })).Value!;

            committed.SourceGeneration.Should().Be(source.Generation + 1);
            committed.ReplacementActivationId.Should().Be("replacement-activation");
            replayed.SourceGeneration.Should().Be(committed.SourceGeneration);
            replayed.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            BaseActivationAdministrationPage terminal = (await store.ReadAdministrationAsync(new BaseActivationAdministrationQueryRequest
            {
                ApplicationId = "activation-test", Scope = scope, Definition = sourceDefinition,
                States = BaseActivationStateSelector.Terminal, Take = 8, AcceptedTime = AcceptedTime(14), Limits = limits,
            })).Value!;
            terminal.Items.Should().ContainSingle(item => item.ActivationId == source.ActivationId && item.State == BaseActivationState.Migrated);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Activation_accepted_time_cannot_regress_after_restart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-activation-time-{Guid.NewGuid():N}.db");
        try
        {
            var request = new BaseActivationDueObservationRequest
            {
                ApplicationId = "activation-test", WorkerModuleId = "test",
                Definitions = [ActivationDefinition()], Scope = ActivationScope(), AcceptedTime = AcceptedTime(100),
                MaximumCandidates = 8, Limits = ActivationLimits(),
            };
            await using (SqliteRecordStore store = Store(path))
                (await store.ObserveDueAsync(request)).IsSuccess().Should().BeTrue();
            await using (SqliteRecordStore reopened = Store(path))
            {
                OperationResult<BaseActivationDueObservation> stale = await reopened.ObserveDueAsync(
                    request with { AcceptedTime = AcceptedTime(99) });
                stale.IsSuccess().Should().BeFalse();
                stale.Error!.Code.Should().Be("base.activation.clockInvalid");
            }
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Schedule_overlap_is_decided_inside_the_sqlite_transaction()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-schedule-overlap-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteRecordStore store = Store(path);
            byte[] overlap = System.Security.Cryptography.SHA256.HashData("overlap"u8);
            BaseScheduleDefinition first = Schedule(1, BaseScheduleOverlapPolicy.Allow, overlap);
            BaseScheduleDefinition second = Schedule(2, BaseScheduleOverlapPolicy.SkipWhileActive, overlap);
            BaseScheduleMutationRequest createFirst = ScheduleMutation(first, 100, "create-1");
            (await store.MutateScheduleAsync(createFirst)).IsSuccess().Should().BeTrue();
            BaseScheduleMutationResult replayedCreate = (await store.MutateScheduleAsync(createFirst with
            { AcceptedTime = AcceptedTime(101) })).Value!;
            replayedCreate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            OperationResult<BaseScheduleMutationResult> collision = await store.MutateScheduleAsync(createFirst with
            {
                AcceptedTime = AcceptedTime(102),
                Identity = createFirst.Identity with { Fingerprint = BaseMutationRequestFingerprint.Create(Enumerable.Repeat((byte)7, 32).ToArray()) },
            });
            collision.IsSuccess().Should().BeFalse();
            collision.Error!.Code.Should().Be("base.activation.fingerprintConflict");
            BaseScheduleMaintenancePage materialized = (await store.AdvanceSchedulesAsync(
                SchedulePage((await store.ReadScheduleAsync(first.Id, first.Version)).Value!, first, 103, "advance-1"))).Value!;
            materialized.Occurrences[0].Disposition.Should().BeOfType<BaseOccurrenceMaterialized>();

            (await store.MutateScheduleAsync(ScheduleMutation(second, 104, "create-2"))).IsSuccess().Should().BeTrue();
            BaseScheduleMaintenancePage skipped = (await store.AdvanceSchedulesAsync(
                SchedulePage((await store.ReadScheduleAsync(second.Id, second.Version)).Value!, second, 105, "advance-2"))).Value!;
            skipped.Occurrences[0].Disposition.Should().BeOfType<BaseOccurrenceSkippedOverlap>();

            BaseScheduleDefinition third = Schedule(3, BaseScheduleOverlapPolicy.CancelPrevious, overlap);
            (await store.MutateScheduleAsync(ScheduleMutation(third, 106, "create-3"))).IsSuccess().Should().BeTrue();
            BaseScheduleMaintenancePage replacement = (await store.AdvanceSchedulesAsync(
                SchedulePage((await store.ReadScheduleAsync(third.Id, third.Version)).Value!, third, 107, "advance-3"))).Value!;
            BaseScheduleCancellationAuthority cancellation = replacement.Cancellations.Should().ContainSingle().Subject;
            OperationResult<BaseScheduleCancellationMaintenancePage> cancelled = await store.AdvanceScheduleCancellationAsync(
                new BaseScheduleCancellationMaintenanceRequest
                {
                    MaintenanceId = cancellation.MaintenanceId, ReplacementActivationId = cancellation.ReplacementActivationId,
                    OverlapKey = cancellation.OverlapKey, HighWater = cancellation.HighWater, AcceptedTime = AcceptedTime(108),
                    Identity = ActivationIdentity("cancel-page"), Limits = ActivationLimits(),
                });
            cancelled.IsSuccess().Should().BeTrue();
            cancelled.Value!.Completed.Should().BeTrue();
            cancelled.Value.CancelledCount.Should().Be(1);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Pending_activation_capacity_succeeds_at_exact_limit_and_rejects_max_plus_one()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-activation-capacity-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteRecordStore store = Store(path, maxPendingActivationRows: 1);
            BaseActivationProviderDescriptor descriptor = ((IBaseActivationProvider)store).Descriptor;
            descriptor.Capability.MaximumPendingRows.Should().Be(1);
            BaseActivationCertificationReceiptContract.Validate(descriptor).Should().BeTrue();
            byte[] overlap = System.Security.Cryptography.SHA256.HashData("capacity"u8);
            BaseScheduleDefinition first = Schedule(11, BaseScheduleOverlapPolicy.Allow, overlap);
            BaseScheduleDefinition second = Schedule(12, BaseScheduleOverlapPolicy.Allow, overlap);
            (await store.MutateScheduleAsync(ScheduleMutation(first, 100, "capacity-create-1"))).IsSuccess().Should().BeTrue();
            BaseScheduleAuthority firstAuthority = (await store.ReadScheduleAsync(first.Id, first.Version)).Value!;
            (await store.AdvanceSchedulesAsync(SchedulePage(firstAuthority, first, 101, "capacity-page-1")))
                .IsSuccess().Should().BeTrue();

            (await store.MutateScheduleAsync(ScheduleMutation(second, 102, "capacity-create-2"))).IsSuccess().Should().BeTrue();
            BaseScheduleAuthority secondAuthority = (await store.ReadScheduleAsync(second.Id, second.Version)).Value!;
            OperationResult<BaseScheduleMaintenancePage> excess = await store.AdvanceSchedulesAsync(
                SchedulePage(secondAuthority, second, 103, "capacity-page-2"));

            excess.IsSuccess().Should().BeFalse();
            excess.Status.Should().Be(OperationStatus.CapabilityUnavailable);
            excess.Error!.Code.Should().Be("base.activation.capacityUnavailable");
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Active_and_terminal_capacities_are_transactional_at_exact_and_plus_one()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-activation-state-capacity-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteRecordStore store = Store(path, maxPendingActivationRows: 2,
                maxClaimedActivationRows: 1, maxTerminalActivationRows: 1);
            byte[] overlap = System.Security.Cryptography.SHA256.HashData("state-capacity"u8);
            foreach (int version in new[] { 21, 22 })
            {
                BaseScheduleDefinition schedule = Schedule(version, BaseScheduleOverlapPolicy.Allow, overlap);
                (await store.MutateScheduleAsync(ScheduleMutation(schedule, version, $"state-create-{version}"))).IsSuccess().Should().BeTrue();
                BaseScheduleAuthority authority = (await store.ReadScheduleAsync(schedule.Id, schedule.Version)).Value!;
                (await store.AdvanceSchedulesAsync(SchedulePage(authority, schedule, version + 1, $"state-page-{version}")))
                    .IsSuccess().Should().BeTrue();
            }
            BaseActivationExecutionLimits limits = ActivationLimits();
            BaseActivationDefinitionKey definition = ActivationDefinition();
            BaseOwnedScopeSeekAuthority scope = ActivationScope();
            var worker = new BaseActivationWorkerAuthority
            {
                ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "capacity-worker",
                Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
            };
            async ValueTask<OperationResult<BaseActivationClaimResult>> Claim(long now, string id)
            {
                BaseActivationDueObservation observed = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
                {
                    ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
                    AcceptedTime = AcceptedTime(now), MaximumCandidates = 8, Limits = limits,
                })).Value!;
                return await store.TryClaimNextAsync(new BaseActivationClaimRequest
                {
                    Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(now), LeaseMilliseconds = 1_000,
                    Identity = ActivationIdentity(id), Limits = limits,
                });
            }
            var first = (BaseActivationClaimedResult)(await Claim(30, "state-claim-1")).Value!;
            OperationResult<BaseActivationClaimResult> activeExcess = await Claim(31, "state-claim-excess");
            activeExcess.Status.Should().Be(OperationStatus.CapabilityUnavailable);
            activeExcess.Error!.Code.Should().Be("base.activation.capacityUnavailable");
            byte[] result = "done"u8.ToArray();
            (await store.TransitionAsync(new BaseActivationCompleteRequest
            {
                ActivationId = first.Claim.ActivationId, Claim = first.Claim, CanonicalResult = result.ToImmutableArray(),
                ResultChecksum = System.Security.Cryptography.SHA256.HashData(result).ToImmutableArray(), AcceptedTime = AcceptedTime(32),
                Identity = ActivationIdentity("state-complete-1"), Limits = limits,
            })).IsSuccess().Should().BeTrue();
            var second = (BaseActivationClaimedResult)(await Claim(33, "state-claim-2")).Value!;
            OperationResult<BaseActivationTransitionResult> terminalExcess = await store.TransitionAsync(new BaseActivationCompleteRequest
            {
                ActivationId = second.Claim.ActivationId, Claim = second.Claim, CanonicalResult = result.ToImmutableArray(),
                ResultChecksum = System.Security.Cryptography.SHA256.HashData(result).ToImmutableArray(), AcceptedTime = AcceptedTime(34),
                Identity = ActivationIdentity("state-complete-excess"), Limits = limits,
            });
            terminalExcess.Status.Should().Be(OperationStatus.CapabilityUnavailable);
            terminalExcess.Error!.Code.Should().Be("base.activation.capacityUnavailable");
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static BaseScheduleDefinition Schedule(int version, BaseScheduleOverlapPolicy policy, byte[] overlap)
    {
        return BaseScheduleDefinitionBuilder.CreateGenerated(new BaseScheduleDefinitionDraft
        {
            Id = "test.schedule", Version = version, OwningModuleId = "test", ManageGrantId = "schedule.manage",
            MaterializeGrantId = "schedule.materialize", Expression = new BaseOnceSchedule(version),
            GapPolicy = BaseTimeGapPolicy.Skip, TimeOverlapPolicy = BaseTimeOverlapPolicy.EarlierOffset,
            MisfirePolicy = BaseScheduleMisfirePolicy.RunAll, ActivationOverlapPolicy = policy,
            OverlapKeyKind = BaseScheduleOverlapKeyKind.CanonicalConcurrencyKey, ConcurrencyKey = overlap.ToImmutableArray(),
            Priority = 0, MaximumSplayMilliseconds = 0,
        }, ScheduleTarget, SqliteActivationDtos.HPDBaseActivationDtoAuthority, new Request()).Definition;
    }

    private static BaseActivationHandlerRegistration<Request, Result> ScheduleTarget { get; } =
        BaseActivationDefinitionBuilder.CreateGenerated(new BaseActivationDefinitionDraft
        {
            Id = "test.activation", Version = 1, OwningModuleId = "module",
            ExecutionClass = BaseActivationExecutionClass.AtLeastOnceWorker,
            Grants = new BaseActivationGrantSet
            {
                Enqueue = "activation.enqueue", Observe = "activation.observe", Claim = "activation.claim",
                Execute = "activation.execute", Renew = "activation.renew", Complete = "activation.complete",
                Fail = "activation.fail", Yield = "activation.yield", Cancel = "activation.cancel", Inspect = "activation.inspect",
                Replay = "activation.replay", Migrate = "activation.migrate", Reconcile = "activation.reconcile",
                Retry = "activation.retry", Dispose = "activation.dispose", Remove = "activation.remove", Repair = "activation.repair",
            },
            SourceGrantIds = [],
            Retry = new BaseActivationRetryProfile
            {
                MaximumAttempts = 1, InitialDelayMilliseconds = 1, MaximumDelayMilliseconds = 1,
                MultiplierNumerator = 1, MultiplierDenominator = 1, JitterBasisPoints = 0, RetryableFailureCodes = [],
            },
            ReceiptRetention = new BaseActivationReceiptRetentionPolicy
            {
                FormatVersion = 1, DuplicateResolutionLifetime = TimeSpan.FromHours(24),
                ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
            },
            Limits = new BaseActivationLimits
            {
                MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 1, MaximumYields = 0,
                MaximumRenewalsPerSlice = 1, MaximumChildrenPerSlice = 1, MaximumLineageDepth = 1,
                LeaseDuration = TimeSpan.FromMinutes(1), HandlerTimeout = TimeSpan.FromMinutes(1),
                Provider = ActivationLimits(), AtomicCreation = ExecutionLimits(),
            },
            Handler = new BaseActivationHandlerDraft
            {
                Id = "test.activation.handler", Version = 1, FactoryId = "test.activation.handler.factory",
                WorkerSubjectKind = AccessSubjectKind.System,
                SemanticAuthority = BaseActivationHandlerSemanticAuthority.Create("test.activation.handler.semantics", 1),
            },
        }, SqliteActivationDtos.HPDBaseActivationDtoAuthority, static _ => new ScheduleHandler());

    [Fact]
    public async Task Activation_prune_pages_emit_exact_durable_floors_without_self_blocking()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l51-prune-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector();
        try
        {
            await using SqliteRecordStore store = ActivationAdministrationStore(path, protector);
            BaseActivationExecutionLimits limits = ActivationLimits(); BaseActivationDefinitionKey definition = ActivationDefinition();
            BaseOwnedScopeSeekAuthority scope = ActivationScope(); byte[] overlap = System.Security.Cryptography.SHA256.HashData("prune"u8);
            long now = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
            var worker = new BaseActivationWorkerAuthority
            {
                ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "prune-worker",
                Definitions = [definition], Scope = scope, Checksum = new byte[32].ToImmutableArray(),
            };
            for (int version = 21; version <= 22; version++)
            {
                BaseScheduleDefinition schedule = Schedule(version, BaseScheduleOverlapPolicy.Allow, overlap);
                OperationResult<BaseScheduleMutationResult> scheduled = await store.MutateScheduleAsync(ScheduleMutation(schedule, version, $"prune-create-{version}") with { AcceptedTime = AcceptedTime(now++) });
                scheduled.IsSuccess().Should().BeTrue(scheduled.Error?.Code);
                BaseScheduleAuthority scheduleAuthority = (await store.ReadScheduleAsync(schedule.Id, schedule.Version)).Value!;
                (await store.AdvanceSchedulesAsync(SchedulePage(scheduleAuthority, schedule, version + 1, $"prune-page-{version}") with { AcceptedTime = AcceptedTime(now++) })).IsSuccess().Should().BeTrue();
                BaseActivationDueObservation observed = (await store.ObserveDueAsync(new BaseActivationDueObservationRequest
                {
                    ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition], Scope = scope,
                    AcceptedTime = AcceptedTime(now++), MaximumCandidates = 8, Limits = limits,
                })).Value!;
                var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new BaseActivationClaimRequest
                {
                    Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(now++), LeaseMilliseconds = 1_000,
                    Identity = ActivationIdentity($"prune-claim-{version}"), Limits = limits,
                })).Value!;
                byte[] result = [(byte)version];
                BaseActivationTransitionResult terminal = (await store.TransitionAsync(new BaseActivationCompleteRequest
                {
                    ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim, CanonicalResult = result.ToImmutableArray(),
                    ResultChecksum = System.Security.Cryptography.SHA256.HashData(result).ToImmutableArray(), AcceptedTime = AcceptedTime(now++),
                    Identity = ActivationIdentity($"prune-complete-{version}"), Limits = limits,
                })).Value!;
                (await store.TransitionAsync(new BaseActivationDisposeRequest
                {
                    ActivationId = claimed.Claim.ActivationId, ExpectedGeneration = terminal.Generation, AcceptedTime = AcceptedTime(now++),
                    Identity = ActivationIdentity($"prune-dispose-{version}"), Limits = limits,
                })).IsSuccess().Should().BeTrue();
            }
            now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            BaseActivationExecutionLimits pruneLimits = limits with { MaximumCandidates = 1 };
            var firstRequest = new BaseActivationPruneRequest
            {
                ApplicationId = "activation-test", Scope = scope, Definition = definition, Take = 1, AcceptedTime = AcceptedTime(now++),
                Identity = ActivationIdentity("prune-first"), Limits = pruneLimits,
            };
            OperationResult<BaseActivationPrunePage> tooSmall = await store.PruneAsync(firstRequest with
            {
                Identity = ActivationIdentity("prune-too-small"), Limits = pruneLimits with { MaximumEvidenceBytes = 1 },
            });
            tooSmall.Status.Should().Be(OperationStatus.ValidationFailed);
            tooSmall.Error!.Code.Should().Be("base.activation.budgetExceeded");
            BaseActivationPrunePage first = (await store.PruneAsync(firstRequest)).Value!;
            first.Items.Should().ContainSingle(); first.Completed.Should().BeFalse();
            first.Accounting.Candidates.Should().Be(1);
            first.Accounting.ReadIntervals.Should().Be(2);
            BaseActivationPruneEvidenceContract.IsValid(first.Items[0]).Should().BeTrue();
            BaseActivationPrunePage second = (await store.PruneAsync(new BaseActivationPruneRequest
            {
                ApplicationId = "activation-test", Scope = scope, Definition = definition, AfterActivationId = first.NextActivationId,
                Take = 1, AcceptedTime = AcceptedTime(now++), Identity = ActivationIdentity("prune-second"), Limits = pruneLimits,
            })).Value!;
            second.Items.Should().ContainSingle(); second.Completed.Should().BeTrue();
            second.Accounting.Candidates.Should().Be(1);
            second.Accounting.ReadIntervals.Should().Be(1);
            BaseActivationPruneEvidenceContract.IsValid(second.Items[0]).Should().BeTrue();
            second.Items[0].ActivationId.Should().NotBe(first.Items[0].ActivationId);

            var artifact = new MemoryStream();
            OperationResult<BaseBackupManifest> backupResult = await store.CreateBackupAsync(artifact, new BaseBackupRequest
            {
                StoreId = "module-store", Principal = AdministrationPrincipal(),
            });
            backupResult.IsSuccess().Should().BeTrue(backupResult.Error?.Code);
            BaseBackupManifest manifest = backupResult.Value!;
            artifact.Position = 0;
            (await store.RestoreAsync(artifact, new BaseRestoreRequest
            {
                StoreId = "module-store", Principal = AdministrationPrincipal(),
                ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true,
                ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
            })).IsSuccess().Should().BeTrue();
            await AssertStoredPruneFloorsValidAsync(path, expectedCount: 2);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    private static BaseScheduleMutationRequest ScheduleMutation(BaseScheduleDefinition definition, long now, string key) => new()
    {
        Kind = BaseScheduleMutationKind.Create, Definition = definition, InitialNextNominal = definition.Version,
        AcceptedTime = AcceptedTime(now), Identity = ActivationIdentity(key), Limits = ActivationLimits(),
    };

    private static BaseScheduleMaintenanceRequest SchedulePage(BaseScheduleAuthority authority, BaseScheduleDefinition definition, long now, string key)
    {
        string activationId = $"activation-{definition.Version}"; string occurrenceId = $"occurrence-{definition.Version}";
        var activation = new BaseActivationCreateIntent
        {
            Ordinal = 0, Definition = definition.Activation, CanonicalInput = definition.CanonicalInput,
            ReceiptRetention = DefaultReceiptRetention(),
            InputChecksum = definition.InputChecksum, Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
            RequestedDueAt = definition.Version, EffectiveDueAt = definition.Version, OccurrenceId = occurrenceId,
            OverlapKey = System.Security.Cryptography.SHA256.HashData(definition.ConcurrencyKey.AsSpan()).ToImmutableArray(),
            OverlapPolicy = definition.ActivationOverlapPolicy, InitiallyEligible = definition.ActivationOverlapPolicy != BaseScheduleOverlapPolicy.CancelPrevious,
            MaximumYields = 0,
            Identity = ActivationIdentity(key + "-activation"),
        };
        var fact = new BaseScheduleOccurrenceFact
        {
            OccurrenceId = occurrenceId, ScheduleId = definition.Id, ScheduleEpoch = authority.ScheduleEpoch,
            NominalAt = definition.Version, EffectiveAt = definition.Version, OverlapOrdinal = 0,
            Disposition = new BaseOccurrenceMaterialized(activationId), Checksum = [],
        };
        fact = fact with { Checksum = InMemoryRecordStore.OccurrenceChecksum(fact).ToImmutableArray() };
        return new BaseScheduleMaintenanceRequest
        {
            ScheduleId = definition.Id, ScheduleVersion = definition.Version, ExpectedAuthorityChecksum = authority.Checksum,
            Occurrences = [new BaseScheduleOccurrenceProposal { Fact = fact, Activation = activation }],
            ResultingLastConsideredNominal = definition.Version, ResultingNextNominal = null,
            AcceptedTime = AcceptedTime(now), Identity = ActivationIdentity(key), Limits = ActivationLimits(),
        };
    }

    [Fact]
    public async Task Generation_operation_commits_replays_and_survives_restart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-{Guid.NewGuid():N}.db");
        try
        {
            BaseMutationRequestIdentity requestIdentity = BaseMutationRequestIdentity.Create(
                "module", "increment", "one", BaseMutationRequestFingerprint.Create(new byte[32]));
            await using (SqliteRecordStore store = Store(path))
            {
                DefaultBaseModuleMutationRuntime runtime = Runtime(store);
                BaseResult<BaseModuleMutationExecutionResult<Result>> first = await runtime.ExecuteAsync(
                    Session(), Definition(), Identity(), new Request(), requestIdentity, null, default);
                BaseResult<BaseModuleMutationExecutionResult<Result>> duplicate = await runtime.ExecuteAsync(
                    Session(), Definition(), Identity(), new Request(), requestIdentity, null, default);

                first.RequireValue().Result.Generation.Should().Be("1");
                first.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
                duplicate.RequireValue().Result.Generation.Should().Be("1");
                duplicate.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            }

            await using (SqliteRecordStore reopened = Store(path))
            {
                BaseResult<BaseModuleMutationExecutionResult<Result>> resolved = await Runtime(reopened).ResolveAsync(
                    Session(), Definition(), Identity(), requestIdentity, default);
                resolved.RequireValue().Result.Generation.Should().Be("1");
                resolved.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            }
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Prepared_module_operation_is_session_bound_and_single_use()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-prepared-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteRecordStore store = Store(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "module.application", [], ExecutionLimits(), default)).Value!;
            var prepareOnly = new PreparedPlanProbe(authority, applyTwice: false);
            await store.ExecuteAtomicAsync(prepareOnly, ExecutionRequest());
            prepareOnly.Prepared.Should().NotBeNull();

            var foreign = new ForeignPreparedProbe(prepareOnly.Prepared!);
            await store.ExecuteAtomicAsync(foreign, ExecutionRequest());
            foreign.RejectedCode.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);

            var twice = new PreparedPlanProbe(authority, applyTwice: true);
            await store.ExecuteAtomicAsync(twice, ExecutionRequest());
            twice.RejectedCode.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Generation_provider_accounting_is_enforced_at_exact_boundaries()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-accounting-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteRecordStore store = Store(path);
            BaseAtomicMutationExecutionLimits generous = ExecutionLimits();
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "module.application", [], generous, default)).Value!;
            var baseline = new PreparedPlanProbe(authority, applyTwice: false, generous);
            await store.ExecuteAtomicAsync(baseline, ExecutionRequest());
            BasePreparedAtomicMutationAccounting measured = baseline.Prepared!.Accounting;

            BaseAtomicMutationExecutionLimits[] exactLimits =
            [
                generous with { MaximumGenerationReads = measured.GenerationReads },
                generous with { MaximumGenerationComparisons = measured.GenerationComparisons },
                generous with { MaximumGenerationIncrements = measured.GenerationIncrements },
                generous with { MaximumReadIntervals = measured.ReadIntervals },
                generous with { MaximumGenerationBytes = measured.GenerationBytes },
                generous with { MaximumEvidenceBytes = measured.EvidenceBytes },
                generous with { MaximumTransientBytes = measured.TransientBytes },
            ];
            foreach (BaseAtomicMutationExecutionLimits exact in exactLimits)
            {
                var accepted = new PreparedPlanProbe(authority, applyTwice: false, exact);
                await store.ExecuteAtomicAsync(accepted, ExecutionRequest());
                accepted.Prepared.Should().NotBeNull();
            }

            BaseAtomicMutationExecutionLimits[] belowLimits =
            [
                generous with { MaximumGenerationReads = checked(measured.GenerationReads - 1) },
                generous with { MaximumGenerationComparisons = checked(measured.GenerationComparisons - 1) },
                generous with { MaximumGenerationIncrements = checked(measured.GenerationIncrements - 1) },
                generous with { MaximumReadIntervals = checked(measured.ReadIntervals - 1) },
                generous with { MaximumGenerationBytes = checked(measured.GenerationBytes - 1) },
                generous with { MaximumEvidenceBytes = checked(measured.EvidenceBytes - 1) },
                generous with { MaximumTransientBytes = checked(measured.TransientBytes - 1) },
            ];
            for (int index = 0; index < belowLimits.Length; index++)
            {
                BaseAtomicMutationExecutionLimits below = belowLimits[index];
                var rejected = new PreparedPlanProbe(authority, applyTwice: false, below);
                await store.ExecuteAtomicAsync(rejected, ExecutionRequest());
                rejected.Prepared.Should().BeNull("boundary {0} must reject measured work plus one", index);
                rejected.RejectedCode.Should().NotBeNull("boundary {0} must report a stable provider failure", index);
            }
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Operation_and_cell_removal_are_rejected_while_receipt_and_generation_authority_remain()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-removal-{Guid.NewGuid():N}.db");
        string cellPath = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-cell-removal-{Guid.NewGuid():N}.db");
        try
        {
            await using (SqliteRecordStore store = Store(path))
            {
                BaseResult<BaseModuleMutationExecutionResult<Result>> committed = await Runtime(store).ExecuteAsync(
                    Session(), Definition(), Identity(), new Request(),
                    BaseMutationRequestIdentity.Create("module", "increment", "retained", BaseMutationRequestFingerprint.Create(new byte[32])),
                    null, default);
                committed.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<Result>>>();
            }

            Func<Task> remove = async () => await Store(path, installModuleAssets: false).DisposeAsync();
            await remove.Should().ThrowAsync<InvalidOperationException>().WithMessage("base.moduleMutation.removalRequired");

            await using (SqliteRecordStore store = Store(cellPath))
            {
                await Runtime(store).ExecuteAsync(
                    Session(), Definition(), Identity(), new Request(),
                    BaseMutationRequestIdentity.Create("module", "increment", "cell-retained", BaseMutationRequestFingerprint.Create(new byte[32])),
                    null, default);
            }
            Func<Task> removeCell = async () => await Store(cellPath, installOperation: true, installCell: false).DisposeAsync();
            await removeCell.Should().ThrowAsync<InvalidOperationException>().WithMessage("base.moduleMutation.removalRequired");
        }
        finally
        {
            foreach (string target in new[] { path, cellPath })
                foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(target + suffix)) File.Delete(target + suffix);
        }
    }

    [Fact]
    public async Task Operation_checksum_drift_is_rejected_during_schema_installation()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-drift-{Guid.NewGuid():N}.db");
        try
        {
            await Store(path).DisposeAsync();
            BaseRegisteredModuleMutationDefinition drifted = Definition() with
            {
                Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData("drift"u8)),
            };
            Func<Task> reopen = async () => await Store(path, operation: drifted).DisposeAsync();
            await reopen.Should().ThrowAsync<InvalidOperationException>().WithMessage("base.moduleMutation.schemaDrift");
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Module_receipts_and_generations_round_trip_through_backup_restore()
    {
        string temporary = Path.GetFullPath(Path.GetTempPath());
        if (OperatingSystem.IsMacOS() && temporary.StartsWith("/var/", StringComparison.Ordinal))
            temporary = "/private" + temporary;
        string path = Path.Combine(temporary, $"hpd-base-l50-administration-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector();
        try
        {
            await using SqliteRecordStore store = AdministrationStore(path, protector);
            store.AdministrationCapability.Backup.Should().BeTrue();
            DefaultBaseModuleMutationRuntime runtime = Runtime(store);
            BaseMutationRequestIdentity original = BaseMutationRequestIdentity.Create(
                "module", "increment", "before-backup", BaseMutationRequestFingerprint.Create(new byte[32]));
            (await runtime.ExecuteAsync(Session(), Definition(), Identity(), new Request(), original, null, default))
                .RequireValue().Result.Generation.Should().Be("1");

            var artifact = new MemoryStream();
            OperationResult<BaseBackupManifest> backup = await store.CreateBackupAsync(artifact, new BaseBackupRequest
            {
                StoreId = "module-store", Principal = AdministrationPrincipal(),
            });
            backup.IsSuccess().Should().BeTrue(backup.Error?.Code);

            byte[] corrupted = artifact.ToArray();
            corrupted[corrupted.Length / 2] ^= 0xff;
            OperationResult<BaseBackupManifest> validation = await store.ValidateBackupAsync(
                new MemoryStream(corrupted),
                new BaseBackupValidationRequest { StoreId = "module-store", Principal = AdministrationPrincipal() });
            validation.Error!.Code.Should().Be(BaseAdministrationErrorCodes.ArtifactInvalid);

            (await runtime.ExecuteAsync(Session(), Definition(), Identity(), new Request(),
                BaseMutationRequestIdentity.Create("module", "increment", "after-backup", BaseMutationRequestFingerprint.Create(new byte[32])),
                null, default)).RequireValue().Result.Generation.Should().Be("2");

            artifact.Position = 0;
            BaseBackupManifest manifest = backup.Value!;
            OperationResult<BaseRestoreResult> restore = await store.RestoreAsync(artifact, new BaseRestoreRequest
            {
                StoreId = "module-store", Principal = AdministrationPrincipal(),
                ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true,
                ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
            });
            restore.IsSuccess().Should().BeTrue(restore.Error?.Code);

            (await Runtime(store).ResolveAsync(Session(), Definition(), Identity(), original, default))
                .RequireValue().Result.Generation.Should().Be("1");
            (await Runtime(store).ExecuteAsync(Session(), Definition(), Identity(), new Request(),
                BaseMutationRequestIdentity.Create("module", "increment", "after-restore", BaseMutationRequestFingerprint.Create(new byte[32])),
                null, default)).RequireValue().Result.Generation.Should().Be("2");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in Directory.GetFiles(Path.GetDirectoryName(path)!)
                .Where(file => Path.GetFileName(file).Contains(Path.GetFileName(path), StringComparison.Ordinal)))
                File.Delete(candidate);
        }
    }

    [Fact]
    public async Task In_place_restore_preserves_nonprunable_schedule_floor()
    {
        string temporary = Path.GetFullPath(Path.GetTempPath());
        if (OperatingSystem.IsMacOS() && temporary.StartsWith("/var/", StringComparison.Ordinal))
            temporary = "/private" + temporary;
        string path = Path.Combine(temporary, $"hpd-base-activation-floor-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector();
        try
        {
            await using SqliteRecordStore store = AdministrationStore(path, protector);
            BaseScheduleDefinition schedule = Schedule(11, BaseScheduleOverlapPolicy.Allow, new byte[32]);
            (await store.MutateScheduleAsync(ScheduleMutation(schedule, 100, "floor-create"))).IsSuccess().Should().BeTrue();
            var artifact = new MemoryStream();
            OperationResult<BaseBackupManifest> backup = await store.CreateBackupAsync(artifact, new BaseBackupRequest
            { StoreId = "module-store", Principal = AdministrationPrincipal() });
            backup.IsSuccess().Should().BeTrue(backup.Error?.Code);
            BaseBackupManifest manifest = backup.Value!;

            BaseScheduleAuthority beforeAdvance = (await store.ReadScheduleAsync(schedule.Id, schedule.Version)).Value!;
            (await store.AdvanceSchedulesAsync(SchedulePage(beforeAdvance, schedule, 101, "floor-advance")))
                .IsSuccess().Should().BeTrue();

            artifact.Position = 0;
            OperationResult<BaseRestoreResult> restored = await store.RestoreAsync(artifact, new BaseRestoreRequest
            {
                StoreId = "module-store", Principal = AdministrationPrincipal(),
                ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true,
                ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
            });

            restored.IsSuccess().Should().BeTrue(restored.Error?.Code);
            BaseScheduleAuthority authority = (await store.ReadScheduleAsync(schedule.Id, schedule.Version)).Value!;
            authority.LastConsideredNominal.Should().Be(schedule.Version);
            authority.ScheduleEpoch.Should().Be(beforeAdvance.ScheduleEpoch);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in Directory.GetFiles(Path.GetDirectoryName(path)!)
                .Where(file => Path.GetFileName(file).Contains(Path.GetFileName(path), StringComparison.Ordinal)))
                File.Delete(candidate);
        }
    }

    [Fact]
    public async Task Disaster_restore_requires_graph_key_and_consumes_manifest_nonce()
    {
        string temporary = Path.GetFullPath(Path.GetTempPath());
        if (OperatingSystem.IsMacOS() && temporary.StartsWith("/var/", StringComparison.Ordinal)) temporary = "/private" + temporary;
        string path = Path.Combine(temporary, $"hpd-base-activation-disaster-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector();
        try
        {
            await using SqliteRecordStore store = AdministrationStore(path, protector);
            BaseScheduleDefinition schedule = Schedule(12, BaseScheduleOverlapPolicy.Allow, new byte[32]);
            (await store.MutateScheduleAsync(ScheduleMutation(schedule, 100, "disaster-create"))).IsSuccess().Should().BeTrue();
            var artifact = new MemoryStream();
            BaseBackupManifest backup = (await store.CreateBackupAsync(artifact, new BaseBackupRequest
            { StoreId = "module-store", Principal = AdministrationPrincipal() })).Value!;
            byte[] seed = Enumerable.Range(1, 32).Select(static value => checked((byte)value)).ToArray();
            BaseScheduleRecoveryVerificationKey key = BaseScheduleRecoveryManifestContract.CreateVerificationKeyFromPrivateSeed(
                "recovery", 1, seed, 0);
            byte[] protectedKey = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                $"base.activation.scheduleRecoveryKey.v1\0{schedule.Id}\n{schedule.Version}"));
            var unsignedRecovery = new BaseScheduleRecoveryManifest
            {
                ApplicationId = "tests.application", LogicalStoreId = "module-store",
                BackupArtifactId = backup.ProviderPayloadSha256,
                BackupArtifactChecksum = Convert.FromHexString(backup.ProviderPayloadSha256).ToImmutableArray(),
                SourceStoreInstanceId = backup.StoreIdentityDigest, SourceRestoreEpoch = backup.RestoreEpoch,
                Floors = [new BaseScheduleRecoveryFloor
                {
                    ProtectedScheduleKeyDigest = protectedKey.ToImmutableArray(), ScheduleEpoch = 1,
                    LastConsideredNominal = schedule.Version, OccurrenceCount = 0, OccurrenceChecksum = System.Security.Cryptography.SHA256.HashData([]).ToImmutableArray(),
                    LatestActivationLineageChecksum = System.Security.Cryptography.SHA256.HashData("base.activation.emptyLineage.v1"u8).ToImmutableArray(),
                }],
                IssuedAt = 1_000, ExpiresAt = 2_000, Nonce = Enumerable.Repeat((byte)9, 32).ToImmutableArray(),
                SigningKeyId = key.Id, SigningKeyVersion = key.Version, ManifestChecksum = [], Signature = [],
            };
            BaseScheduleRecoveryManifest recovery = BaseScheduleRecoveryManifestContract.Sign(unsignedRecovery, key, seed);
            var request = new BaseRestoreRequest
            {
                StoreId = "module-store", Principal = AdministrationPrincipal(),
                ExpectedCurrentStoreIdentityDigest = backup.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = backup.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true, ScheduleRestoreDomain = BaseScheduleRestoreDomain.NewDisasterDomain,
                ScheduleRecoveryManifest = recovery, RecoveryApplicationId = "tests.application",
                RecoveryVerificationKeys = [key], RecoveryAcceptedNow = 1_500,
            };
            artifact.Position = 0;
            (await store.RestoreAsync(artifact, request)).IsSuccess().Should().BeTrue();
            artifact.Position = 0;
            OperationResult<BaseRestoreResult> replay = await store.RestoreAsync(artifact, request);
            replay.IsSuccess().Should().BeFalse();
            replay.Error!.Code.Should().Be(BaseAdministrationErrorCodes.ArtifactInvalid);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in Directory.GetFiles(Path.GetDirectoryName(path)!)
                .Where(file => Path.GetFileName(file).Contains(Path.GetFileName(path), StringComparison.Ordinal))) File.Delete(candidate);
        }
    }

    private static SqliteRecordStore AdministrationStore(string path, BaseOpaqueTokenProtector protector)
    {
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "module-store", DataSource = path, AdministrationEnabled = true,
            Collections = [SqliteTestFactory.Collection()], ModuleMutations = [Definition()],
            ModuleGenerationCells = [Cell()], MaxBackupArtifactBytes = 16 * 1024 * 1024,
        };
        SqliteRecordStore store = SqliteTestFactory.Create(options, tokenProtector: protector);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO hpd_base_schema_identity(singleton,store_instance_id) VALUES (1,'module-store-instance');
            INSERT OR IGNORE INTO hpd_base_schema_baseline(application_id,store_instance_id,baseline_id,checksum,generation,last_plan_id,applied_at)
            VALUES ('module.application','module-store-instance','baseline-1','checksum-1',1,'plan-1','2026-08-19T00:00:00Z');
            """;
        command.ExecuteNonQuery();
        return store;
    }

    private static SqliteRecordStore ActivationAdministrationStore(string path, BaseOpaqueTokenProtector protector)
    {
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "module-store", DataSource = path, AdministrationEnabled = true,
            Collections = [], ModuleMutations = [Definition()], ModuleGenerationCells = [Cell()],
            MaxBackupArtifactBytes = 16 * 1024 * 1024,
        };
        SqliteRecordStore store = SqliteTestFactory.Create(options, tokenProtector: protector);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO hpd_base_schema_identity(singleton,store_instance_id) VALUES (1,'module-store-instance');
            INSERT OR IGNORE INTO hpd_base_schema_baseline(application_id,store_instance_id,baseline_id,checksum,generation,last_plan_id,applied_at)
            VALUES ('module.application','module-store-instance','baseline-1','checksum-1',1,'plan-1','2026-08-19T00:00:00Z');
            """;
        command.ExecuteNonQuery();
        return store;
    }

    private static async Task AssertStoredPruneFloorsValidAsync(string path, int expectedCount)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT activation_id,definition_id,definition_version,definition_checksum,terminal_generation,terminal_control_checksum,terminal_receipt_checksum,occurrence_checksum,result_checksum,prune_authority_generation,application_id,logical_store_id,store_instance_id,restore_epoch,publication_authority_checksum,authority_checksum FROM hpd_base_activation_prune_floors ORDER BY activation_id;";
        await using Microsoft.Data.Sqlite.SqliteDataReader reader = await command.ExecuteReaderAsync();
        var evidence = new List<BaseActivationPruneEvidence>();
        while (await reader.ReadAsync())
            evidence.Add(new BaseActivationPruneEvidence
            {
                ActivationId = reader.GetString(0), Definition = new BaseActivationDefinitionKey { Id = reader.GetString(1), Version = reader.GetInt32(2), Checksum = ((byte[])reader[3]).ToImmutableArray() },
                TerminalGeneration = reader.GetInt64(4), TerminalControlChecksum = ((byte[])reader[5]).ToImmutableArray(), TerminalReceiptChecksum = ((byte[])reader[6]).ToImmutableArray(),
                OccurrenceChecksum = reader.IsDBNull(7) ? null : ((byte[])reader[7]).ToImmutableArray(), ResultChecksum = reader.IsDBNull(8) ? null : ((byte[])reader[8]).ToImmutableArray(),
                PruneAuthorityGeneration = reader.GetInt64(9), ApplicationId = reader.GetString(10), LogicalStoreId = reader.GetString(11), StoreInstanceId = reader.GetString(12), RestoreEpoch = reader.GetInt64(13),
                PublicationAuthorityChecksum = ((byte[])reader[14]).ToImmutableArray(), Checksum = ((byte[])reader[15]).ToImmutableArray(),
            });
        evidence.Should().HaveCount(expectedCount);
        evidence.Should().OnlyContain(static item => BaseActivationPruneEvidenceContract.IsValid(item));
        evidence.Should().OnlyContain(static item => item.RestoreEpoch > 0);
    }

    private static BaseOpaqueTokenProtector Protector() => new(Microsoft.Extensions.Options.Options.Create(
        new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 50, Key = Enumerable.Repeat((byte)0x50, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            },
        }));

    private static PrincipalContext AdministrationPrincipal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectKind = AccessSubjectKind.System,
        SubjectId = "system",
    };

    private static SqliteRecordStore Store(
        string path,
        bool installModuleAssets = true,
        BaseRegisteredModuleMutationDefinition? operation = null,
        bool? installOperation = null,
        bool? installCell = null,
        int maxPendingActivationRows = 1_000_000,
        int maxClaimedActivationRows = 1_000_000,
        int maxTerminalActivationRows = 1_000_000,
        string semanticApplicationId = "")
    {
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "module-store", DataSource = path, Collections = [],
            MaxPendingActivationRows = maxPendingActivationRows,
            MaxClaimedActivationRows = maxClaimedActivationRows,
            MaxTerminalActivationRows = maxTerminalActivationRows,
            SemanticActivationApplicationId = semanticApplicationId,
        };
        if (installOperation ?? installModuleAssets)
            options.ModuleMutations = [operation ?? Definition()];
        if (installCell ?? installModuleAssets)
            options.ModuleGenerationCells = [Cell()];
        var store = new SqliteRecordStore(options, NullLoggerFactory.Instance);
        store.InitializeUnacceptedSchemaForTestsAsync().AsTask().GetAwaiter().GetResult();
        return store;
    }

    private static DefaultBaseModuleMutationRuntime Runtime(SqliteRecordStore store)
    {
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store });
        BaseRegisteredModuleMutationDefinition definition = Definition();
        BaseModuleGenerationCellDefinition cell = Cell();
        return new DefaultBaseModuleMutationRuntime(stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>()),
            new BaseModuleMutationRegistry([definition], [cell]), null!, Policy(), null!, new BaseSubjectContractRegistry([]), TimeProvider.System);
    }

    private static BaseModuleGenerationCellDefinition Cell() => new()
    {
        Id = "module.generation", Version = 1, OwningModuleId = "module",
        Scope = BaseModuleGenerationScope.Application, MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
    };

    private static BaseSession Session() => new(null!, TimeProvider.System,
        new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "system" },
        new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane }, applicationId: "module.application");

    private static DefaultBasePolicyOrchestrator Policy()
    {
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(new BasePolicyAuthorityDefinition
        {
            Id = "module.policy", Version = 1, OwningModuleId = "module",
            EvaluatorContractId = "module.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, new AllowPolicyEvaluator());
        builder.AddStaticGrant(new BaseGrantAuthorityDefinition
        {
            Id = "module.increment", Version = 1, OwningModuleId = "module",
            SourceContractId = "module.grants", SourceContractVersion = 1,
        }, new AccessGrant
        {
            Id = "module.increment", ApplicationId = "module.application", ModuleId = "module",
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
            Action = "module.increment", Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
        });
        return new DefaultBasePolicyOrchestrator(builder.Freeze("module.application"));
    }

    private static BaseGeneratedModuleMutationIdentity<Request, Result> Identity() => new(
        "module.increment", 1, new byte[32],
        SqliteActivationDtos.HPDBaseActivationDtoAuthority.InputTypeInfo,
        SqliteActivationDtos.HPDBaseActivationDtoAuthority.ResultTypeInfo,
        SqliteActivationDtos.HPDBaseActivationDtoAuthority.InputBindings.Values.ToArray(),
        SqliteActivationDtos.HPDBaseActivationDtoAuthority.ResultBindings.Values.ToArray());

    private static BaseRegisteredModuleMutationDefinition Definition() => BaseModuleMutationContract.Seal(new()
    {
        Id = "module.increment", Version = 1, OwningModuleId = "module", GrantId = "module.increment",
        Audience = BaseModuleMutationAudience.System, RequestTypeId = "request", ResultTypeId = "result",
        SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = ["module.generation"], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [new BaseModuleGenerationCapture { Id = "generation", CellId = "module.generation", Absence = BaseModuleGenerationAbsenceBehavior.AllowEither }],
            Guards = [],
            Preconditions = [],
            Body = new BaseModuleMutationBlock { Statements = [new BaseModuleIncrementGenerationStatement { Id = "increment", CaptureId = "generation", CreateIfAbsent = true }] },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result",
                    Properties = [new BaseModuleObjectPropertyExpression
                    {
                        StablePropertyId = "result.generation",
                        Value = new BaseModuleResultingGenerationExpression
                        {
                            Id = "result-generation",
                            ResultType = BaseGeneratedModuleScalarManifest.Primitive<string>().Seal(["result.generation"]).ValueType,
                            CaptureId = "generation",
                        },
                    }],
                },
            },
        },
        Limits = Limits(), ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
    });

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 8, MaximumRecordCaptures = 8, MaximumRelationTargetCaptures = 8, MaximumGenerationCaptures = 8,
        MaximumRecordMutations = 8, MaximumGenerationReads = 8, MaximumGenerationComparisons = 8, MaximumGenerationIncrements = 8,
        MaximumGuardNodes = 8, MaximumGuardDepth = 8, MaximumStatements = 8, MaximumBranches = 8, MaximumExpressionNodes = 32,
        MaximumPreconditions = 8, MaximumRequestGuardEvaluations = 16, MaximumStaticSetMembers = 16, MaximumStaticSetComparisons = 120, MaximumDisabledCaptures = 8, MaximumRemovedFields = 8,
        MaximumReadIntervals = 16, MaximumSubjectValidations = 8, MaximumAuthorityReads = 16, MaximumRelationChecks = 8,
        MaximumUniqueConstraintChecks = 8, MaximumRequestBytes = 4096, MaximumSelectedBytes = 4096, MaximumGenerationBytes = 4096,
        MaximumEvidenceBytes = 4096, MaximumWrittenBytes = 4096, MaximumFactBytes = 4096, MaximumJournalBytes = 4096,
        MaximumReceiptBytes = 4096, MaximumResultBytes = 4096, MaximumTransientBytes = 65536,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };

    private static BaseAtomicMutationExecutionLimits ExecutionLimits() =>
        DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(Limits());

    private static RecordMutationExecutionRequest ExecutionRequest() => new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitCompletionTimeout = TimeSpan.FromSeconds(5),
    };

    private static BaseActivationExecutionLimits ActivationLimits() => new()
    {
        MaximumCandidates = 8, MaximumInputBytes = 4096, MaximumResultBytes = 4096,
        MaximumEvidenceBytes = 4096, MaximumTransientBytes = 16384,
        MaximumReadIntervals = 8, MaximumIndexOperations = 16,
        AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
    };

    private static BaseAcceptedTimeReceipt AcceptedTime(long milliseconds)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        Append(hash, "base.activation.acceptedTime.v2\0"); Append(hash, "activation-test"); Append(hash, 1);
        Append(hash, milliseconds); Append(hash, milliseconds); Append(hash, milliseconds + 1); Append(hash, 30_000);
        return new BaseAcceptedTimeReceipt("activation-test", 1, milliseconds, milliseconds, milliseconds + 1, 30_000, hash.GetHashAndReset());
    }

    private static void Append(System.Security.Cryptography.IncrementalHash hash, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length)); hash.AppendData(length); hash.AppendData(bytes);
    }

    private static void Append(System.Security.Cryptography.IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes);
    }

    private static BaseActivationDefinitionKey ActivationDefinition() => new()
    {
        Id = ScheduleTarget.Definition.Id,
        Version = ScheduleTarget.Definition.Version,
        Checksum = ScheduleTarget.Definition.Checksum,
    };

    private static BaseOwnedScopeSeekAuthority ActivationScope() => new()
    {
        Kind = BaseSubjectScopeKind.Global,
        ProtectedIndexDigest = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
    };

    private static BaseMutationRequestIdentity ActivationIdentity(string id) =>
        BaseMutationRequestIdentity.Create(
            "activation-test", "activation", id, BaseMutationRequestFingerprint.Create(new byte[32]));

    private static BaseActivationReceiptCompactionRequest CompactionRequest(
        BaseActivationDefinitionKey definition,
        BaseOwnedScopeSeekAuthority scope,
        BaseActivationYieldReservationState reservation,
        long acceptedTime,
        string identity) => new()
    {
        ApplicationId = "activation-test",
        Definition = definition,
        ReceiptRetention = DefaultReceiptRetention(),
        Scope = scope,
        AcceptedTime = AcceptedTime(acceptedTime),
        Take = 8,
        BackupFloor = new BaseActivationReceiptBackupFloor
        {
            Kind = BaseActivationReceiptBackupFloorKind.NotApplicable,
        },
        ExpectedReservation = reservation,
        Limits = ActivationLimits(),
        Identity = ActivationIdentity(identity),
    };

    private sealed class PreparedPlanProbe(
        BaseAtomicMutationAuthorityRequirement authority,
        bool applyTwice,
        BaseAtomicMutationExecutionLimits? suppliedLimits = null) : IAtomicMutationProcessor
    {
        public BasePreparedAtomicExecution? Prepared { get; private set; }
        public string? RejectedCode { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            BaseAtomicMutationExecutionLimits limits = suppliedLimits ?? ExecutionLimits();
            var capture = new BaseAtomicExecutionRequest
            {
                Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
                Intent = new BaseAtomicMutationIntent { IntentDigest = "l50-probe-intent", Authority = authority, Items = [] },
                Module = new BaseModuleMutationCaptureExtension
                {
                    OperationId = Definition().Id, OperationVersion = Definition().Version,
                    OperationChecksum = Convert.ToHexString(Definition().Checksum.ToArray()).ToLowerInvariant(),
                    RequestDigest = "l50-probe-request", Records = [], RelationTargets = [],
                    Generations = [new BaseModuleGenerationCaptureRequest
                    {
                        Ordinal = 0, CaptureId = "generation", Cell = Cell(),
                        Scope = new BaseModuleGenerationScopeAuthority { Kind = BaseModuleGenerationScope.Application },
                        KeyUtf8 = ImmutableArray<byte>.Empty, Absence = BaseModuleGenerationAbsenceBehavior.AllowEither,
                    }],
                },
                Limits = limits,
            };
            OperationResult<BaseCapturedAtomicExecution> captured =
                await session.CaptureAtomicExecutionAsync(capture, cancellationToken);
            if (!captured.IsSuccess() || captured.Value is null)
            {
                RejectedCode = captured.Error?.Code;
                return Failure(captured.Error);
            }
            var plan = new BaseFinalizedAtomicExecutionPlan
            {
                Kind = BaseAtomicMutationExecutionKind.ModuleMutation, PlanDigest = "l50-probe-plan",
                IntentDigest = capture.Intent.IntentDigest, CaptureDigest = captured.Value.CaptureDigest,
                PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]), Authority = authority,
                Items = [], SubjectValidations = [], Limits = limits,
                Module = new BaseFinalizedModuleMutationExtension
                {
                    OperationId = Definition().Id, OperationVersion = Definition().Version,
                    OperationChecksum = Convert.ToHexString(Definition().Checksum.ToArray()).ToLowerInvariant(),
                    Decisions = [], ItemBindings = [], RelationTargets = [],
                    Comparisons = [new BaseModuleGenerationComparison
                    {
                        CaptureOrdinal = 0, Kind = BaseModuleGenerationComparisonKind.MustBeMissing,
                    }],
                    Increments = [new BaseModuleGenerationIncrement { CaptureOrdinal = 0, CreateIfAbsent = true }],
                    ResultProjectionDigest = "l50-probe-result",
                },
            };
            OperationResult<BasePreparedAtomicExecution> prepared =
                await session.PrepareAtomicExecutionAsync(captured.Value, plan, cancellationToken);
            if (!prepared.IsSuccess() || prepared.Value is null)
            {
                RejectedCode = prepared.Error?.Code;
                return Failure(prepared.Error);
            }
            Prepared = prepared.Value;
            if (!applyTwice) return Failure(null);
            OperationResult<BaseProvisionalAtomicExecution> first =
                await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken);
            if (!first.IsSuccess()) return Failure(first.Error);
            OperationResult<BaseProvisionalAtomicExecution> second =
                await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken);
            RejectedCode = second.Error?.Code;
            return Failure(second.Error);
        }
    }

    private sealed class ForeignPreparedProbe(BasePreparedAtomicExecution prepared) : IAtomicMutationProcessor
    {
        public string? RejectedCode { get; private set; }
        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            OperationResult<BaseProvisionalAtomicExecution> result =
                await session.ApplyPreparedAtomicExecutionAsync(prepared, cancellationToken);
            RejectedCode = result.Error?.Code;
            return Failure(result.Error);
        }
    }

    private sealed class ActivationCreationProbe(
        BaseAtomicMutationAuthorityRequirement authority,
        BaseAtomicMutationExecutionLimits limits,
        string inputText = "activation-input",
        string activationIdentity = "activation-1",
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
                    System.Text.Encoding.UTF8.GetBytes(activationIdentity)).ToImmutableArray(),
                Items = [new BaseActivationCreateIntent
                {
                    Ordinal = 0,
                    Definition = ActivationDefinition(),
                    ReceiptRetention = DefaultReceiptRetention(),
                    CanonicalInput = input.ToImmutableArray(),
                    InputChecksum = System.Security.Cryptography.SHA256.HashData(input).ToImmutableArray(),
                    Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
                    RequestedDueAt = 1,
                    EffectiveDueAt = 1,
                    MaximumYields = maximumYields,
                    Identity = BaseMutationRequestIdentity.Create(
                        "activation-test", "enqueue", activationIdentity,
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
                return Failure(captured.Error);
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
            {
                RejectedCode = prepared.Error?.Code;
                return Failure(prepared.Error);
            }
            OperationResult<BaseProvisionalAtomicExecution> applied =
                await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken);
            if (!applied.IsSuccess() || applied.Value?.Activations is null)
            {
                RejectedCode = applied.Error?.Code;
                return Failure(applied.Error);
            }
            ProvisionalCount = applied.Value.Activations.Items.Length;
            return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, []);
        }
    }

    private static BaseActivationReceiptRetentionPolicy DefaultReceiptRetention() => new()
    {
        FormatVersion = 1,
        DuplicateResolutionLifetime = TimeSpan.FromHours(24),
        ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
    };

    private static AtomicMutationProcessingResult Failure(BaseError? error) => new(
        AtomicMutationProcessingOutcome.Failed, [], error ?? new BaseError
        {
            Code = BaseSubjectErrorCodes.ProviderContractInvalid,
            Message = "The prepared-operation probe intentionally rolled back.",
            Category = ErrorCategory.Store,
        });

    public sealed record Request
    {
        [BaseField("sqlite.activation.request.scope", MaximumUtf8Bytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
        public string Scope { get; init; } = "application";
    }
    public sealed record Result
    {
        [BaseField("result.generation", MaximumUtf8Bytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
        public required string Generation { get; init; }
    }
    [JsonSerializable(typeof(Request))]
    [JsonSerializable(typeof(Result))]
    internal sealed partial class Json : JsonSerializerContext;

    private sealed class ScheduleHandler : IBaseActivationHandler<Request, Result>
    {
        public ValueTask<BaseActivationHandlerResult<Result>> ExecuteAsync(
            BaseActivationContext context, Request input, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BaseActivationHandlerResult<Result>>(new BaseActivationSucceeded<Result>
            {
                Result = new Result { Generation = input.Scope },
            });
    }

    private sealed class AllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed });
    }
}

[BaseActivationDtoAuthority("sqlite.activation.dto", 1, "module", "request", "result",
    typeof(SqliteModuleMutationTests.Json), typeof(SqliteModuleMutationTests.Request), typeof(SqliteModuleMutationTests.Result))]
internal static partial class SqliteActivationDtos;
