using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed partial class SqliteModuleMutationTests
{
    [Fact]
    public async Task Semantic_runtime_finalizes_null_due_across_different_parents_and_restart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-runtime-{Guid.NewGuid():N}.db");
        try
        {
            BaseRegisteredModuleMutationDefinition module = Definition();
            var serializer = new Json(BaseSerializerGeneratedContract.CreateOptions(null));
            var moduleIdentity = new BaseGeneratedModuleMutationIdentity<Request, Result>(
                module.Id, module.Version, module.Checksum.ToArray(), serializer.Request, serializer.Result, [],
                [BaseModuleDtoPropertyBinding.Create<Result, string>("result.generation", nameof(Result.Generation))]);
            BaseTransactionalActivationRegistration<Request, Result> activation = RuntimeActivation(module);
            BaseSemanticActivationKeyExpression expression = new BaseSemanticActivationKeyConstantExpression
            {
                ScalarKind = BaseSemanticActivationKeyScalarKind.String,
                CanonicalBaseJson = JsonSerializer.SerializeToUtf8Bytes("auth-user-42").ToImmutableArray(),
                MaximumValueBytes = 64,
            };
            byte[] serializerChecksum = Convert.FromHexString(BaseSerializerContract.GraphFingerprint(
                moduleIdentity.RequestTypeInfo, moduleIdentity.SerializerDeclarations));
            byte[] expressionChecksum = BaseSemanticActivationKeyCompiler.ExpressionChecksum(expression);
            BaseSemanticActivationKeyDefinition semantic = BaseSemanticActivationDefinitionContract.Seal(new()
            {
                Id = "module.semantic.runtime", Version = 1, OwningApplicationId = "module.application", OwningModuleId = "module",
                EnsureOperation = new() { OperationId = module.Id, OperationVersion = module.Version, OperationChecksum = Convert.ToHexStringLower(module.Checksum.ToArray()) },
                RetirementOperation = new() { OperationId = "module.semantic.retire", OperationVersion = 1, OperationChecksum = Convert.ToHexStringLower(SHA256.HashData("runtime-retire"u8)) },
                Activation = new() { Id = activation.Definition.Id, Version = activation.Definition.Version, Checksum = activation.Definition.Checksum },
                ScopeKind = BaseSubjectScopeKind.Global, EnsureGrantId = "semantic.ensure", RetirementGrantId = "semantic.retire",
                MaintenanceGrantId = "semantic.maintain", Compaction = new BaseSemanticActivationNoCompaction(),
                RequestTypeId = module.RequestTypeId, RequestSerializerChecksum = serializerChecksum.ToImmutableArray(),
                KeyExpressionChecksum = expressionChecksum.ToImmutableArray(), Limits = SemanticDefinition().Limits, Checksum = [],
            });
            BaseSemanticActivationKeyIdentity<Request, RuntimeSemanticMarker> keyIdentity =
                BaseSemanticActivations.CreateKeyIdentity<Request, Result, RuntimeSemanticMarker>(semantic.Id, semantic.Version,
                    semantic.OwningApplicationId, semantic.OwningModuleId, semantic.Checksum.AsSpan(), semantic.Limits.MaximumCanonicalKeyBytes,
                    moduleIdentity, expression);
            var installed = new BaseInstalledSemanticActivationRegistration<Request, RuntimeSemanticMarker>(new()
            {
                Definition = semantic, RequestTypeId = semantic.RequestTypeId, RequestSerializerChecksum = semantic.RequestSerializerChecksum,
                KeyIdentity = keyIdentity,
            });
            var semanticRegistry = new BaseSemanticActivationRegistry([installed]);
            BaseSemanticActivationKey<RuntimeSemanticMarker> key = semanticRegistry.CreateKey(keyIdentity, new Request());

            async Task ExecuteParentAsync(SqliteRecordStore store, string parentIdentity)
            {
                BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
                BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                    "module.application", [], limits, default)).Value!;
                (await store.ExecuteAtomicAsync(new ActivationCreationProbe(authority, limits, parentIdentity, parentIdentity), ExecutionRequest()))
                    .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                long acceptedTime = TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds();
                BaseActivationDefinitionKey parentDefinition = ActivationDefinition();
                BaseOwnedScopeSeekAuthority parentScope = ActivationScope();
                OperationResult<BaseActivationDueObservation> observationResult = await store.ObserveDueAsync(new BaseActivationDueObservationRequest
                {
                    ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [parentDefinition], Scope = parentScope,
                    AcceptedTime = AcceptedTime(acceptedTime), MaximumCandidates = 8, Limits = ActivationLimits(),
                });
                observationResult.IsSuccess().Should().BeTrue($"{parentIdentity}:{observationResult.Error?.Code}");
                BaseActivationDueObservation observed = observationResult.Value!;
                observed.Token.Should().NotBeNull(parentIdentity);
                var worker = new BaseActivationWorkerAuthority
                {
                    ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "runtime-parent-worker",
                    Definitions = [parentDefinition], Scope = parentScope, Checksum = new byte[32].ToImmutableArray(),
                };
                OperationResult<BaseActivationClaimResult> claimResult = await store.TryClaimNextAsync(new BaseActivationClaimRequest
                {
                    Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(acceptedTime), LeaseMilliseconds = 10_000,
                    Identity = BaseMutationRequestIdentity.Create("activation-test", "claim", parentIdentity,
                        BaseMutationRequestFingerprint.Create(SHA256.HashData(Encoding.UTF8.GetBytes(parentIdentity)))), Limits = ActivationLimits(),
                });
                claimResult.IsSuccess().Should().BeTrue(claimResult.Error?.Code);
                BaseActivationClaimedResult claimed = claimResult.Value.Should().BeOfType<BaseActivationClaimedResult>().Subject;
                BaseMutationRequestFingerprint childFingerprint = BaseMutationRequestFingerprint.Create(SHA256.HashData(Encoding.UTF8.GetBytes("child:" + parentIdentity)));
                var options = new BaseModuleMutationExecutionOptions
                {
                    ActivationGuard = new BaseActivationGuard
                    {
                        Claim = claimed.Claim, StepId = "semantic-child", ChildOrdinal = 1,
                        ChildRequestFingerprint = childFingerprint.ToArray().ToImmutableArray(),
                    },
                    SemanticActivation = new BaseSemanticActivationGuardedEnsureRequest
                    {
                        Key = key, Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
                        Activation = new BaseActivationDefinitionKey { Id = activation.Definition.Id, Version = activation.Definition.Version, Checksum = activation.Definition.Checksum },
                        CanonicalInput = JsonSerializer.SerializeToUtf8Bytes(new Request(), Json.Default.Request).ToImmutableArray(),
                        InputChecksum = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new Request(), Json.Default.Request)).ToImmutableArray(),
                        DueAt = null,
                    },
                };
                BaseMutationRequestIdentity requestIdentity = BaseMutationRequestIdentity.Create(
                    "module", "semantic", parentIdentity, childFingerprint);
                BaseResult<BaseModuleMutationExecutionResult<Result>> result = await RuntimeSemantic(store, module, semanticRegistry,
                    activation).ExecuteAsync(Session(), module, moduleIdentity, new Request(),
                    requestIdentity, options, default);
                BaseModuleMutationExecutionResult<Result> committed = result.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<Result>>>(
                    result is BaseFailure<BaseModuleMutationExecutionResult<Result>> failure
                        ? $"{parentIdentity}:{failure.Error.Code}:{failure.Error.Message}:{failure.Error.Detail}" : string.Empty).Subject.Value;
                committed.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);

                BaseResult<BaseModuleMutationExecutionResult<Result>> replay = await RuntimeSemantic(store, module, semanticRegistry,
                    activation).ExecuteAsync(Session(), module, moduleIdentity, new Request(), requestIdentity, options, default);
                BaseModuleMutationExecutionResult<Result> historical = replay.Should()
                    .BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<Result>>>().Subject.Value;
                historical.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
                historical.Outcome.Should().Be(BaseModuleMutationOutcome.Duplicate);
                historical.Result.Should().BeEquivalentTo(committed.Result);
            }

            static async Task AssertOneSemanticActivationAsync(string databasePath)
            {
                await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}");
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*), COUNT(DISTINCT activation_id) FROM hpd_base_semantic_activation_slots WHERE state=1 AND activation_id IS NOT NULL;";
                await using Microsoft.Data.Sqlite.SqliteDataReader reader = await command.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                reader.GetInt64(0).Should().Be(1);
                reader.GetInt64(1).Should().Be(1);
            }

            await using (SqliteRecordStore store = SemanticStore(path, installedDefinition: semantic,
                ownerGeneration: semanticRegistry.OwnerGeneration, definitionSetChecksum: semanticRegistry.DefinitionSetChecksum))
            {
                await ExecuteParentAsync(store, "parent-one");
                await ExecuteParentAsync(store, "parent-two");
                await AssertOneSemanticActivationAsync(path);
            }
            await using (SqliteRecordStore reopened = SemanticStore(path, installedDefinition: semantic,
                ownerGeneration: semanticRegistry.OwnerGeneration, definitionSetChecksum: semanticRegistry.DefinitionSetChecksum))
            {
                await ExecuteParentAsync(reopened, "parent-three");
                await AssertOneSemanticActivationAsync(path);
            }
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_ensure_is_parent_independent_and_persistent()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-semantic-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using (SqliteRecordStore store = SemanticStore(path))
            {
                BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                    "activation-test", [], limits, default)).Value!;
                var first = new SqliteSemanticEnsureProbe(authority, limits, "parent-one");
                var second = new SqliteSemanticEnsureProbe(authority, limits, "parent-two");

                RecordMutationExecutionResult firstResult = await store.ExecuteAtomicAsync(first, ExecutionRequest());
                firstResult.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, first.RejectedCode + ":" + firstResult.Error?.Code);
                RecordMutationExecutionResult secondResult = await store.ExecuteAtomicAsync(second, ExecutionRequest());
                secondResult.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, second.RejectedCode + ":" + secondResult.Error?.Code + ":" + secondResult.Error?.Message);

                first.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Missing);
                first.Provisional!.ResultingState.Should().Be(BaseSemanticActivationSlotState.Live);
                second.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Live);
                second.Provisional!.ActivationId.Should().Be(first.Provisional.ActivationId);
                RejectsSubstitutedResultingSlotChecksum(first);
                RejectsSubstitutedResultingSlotChecksum(second);
            }
            await using (SqliteRecordStore reopened = SemanticStore(path))
            {
                BaseAtomicMutationAuthorityRequirement authority = (await reopened.CaptureAtomicMutationAuthorityRequirementAsync(
                    "activation-test", [], limits, default)).Value!;
                var third = new SqliteSemanticEnsureProbe(authority, limits, "parent-three");
                (await reopened.ExecuteAtomicAsync(third, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                third.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Live);
            }
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Semantic_ensure_serializes_different_parent_races()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-race-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using SqliteRecordStore firstStore = SemanticStore(path);
            await using SqliteRecordStore secondStore = SemanticStore(path);
            BaseAtomicMutationAuthorityRequirement firstAuthority = (await firstStore.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits, default)).Value!;
            BaseAtomicMutationAuthorityRequirement secondAuthority = (await secondStore.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits, default)).Value!;
            var first = new SqliteSemanticEnsureProbe(firstAuthority, limits, "race-one");
            var second = new SqliteSemanticEnsureProbe(secondAuthority, limits, "race-two");
            RecordMutationExecutionResult[] results = await Task.WhenAll(
                firstStore.ExecuteAtomicAsync(first, ExecutionRequest()).AsTask(),
                secondStore.ExecuteAtomicAsync(second, ExecutionRequest()).AsTask());
            results.Should().OnlyContain(value => value.Outcome == RecordMutationExecutionOutcome.Committed);
            new[] { first.CapturedState, second.CapturedState }.Should().BeEquivalentTo(
                [BaseSemanticActivationCapturedState.Missing, BaseSemanticActivationCapturedState.Live]);
            first.Provisional!.ActivationId.Should().Be(second.Provisional!.ActivationId);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_ensure_finalizes_provider_time_and_an_existing_scope_binding()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-finalization-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using SqliteRecordStore store = SemanticStore(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation-test", [], limits, default)).Value!;
            var first = new SqliteSemanticEnsureProbe(authority, limits, "scope-parent-one", acceptedCurrentTime: true);
            var second = new SqliteSemanticEnsureProbe(authority, limits, "scope-parent-two", acceptedCurrentTime: true);
            (await store.ExecuteAtomicAsync(first, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, first.RejectedCode);
            (await store.ExecuteAtomicAsync(second, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, second.RejectedCode);
            first.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Missing);
            second.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Live);
            second.Provisional!.ActivationId.Should().Be(first.Provisional!.ActivationId);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_ensure_enforces_live_slot_and_pending_row_capacity()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-capacity-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using SqliteRecordStore store = SemanticStore(path, maximumLiveSlots: 1, maximumPendingRows: 1);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits, default)).Value!;
            var first = new SqliteSemanticEnsureProbe(authority, limits, "capacity-one", "subject-one");
            var second = new SqliteSemanticEnsureProbe(authority, limits, "capacity-two", "subject-two");
            (await store.ExecuteAtomicAsync(first, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            RecordMutationExecutionResult rejected = await store.ExecuteAtomicAsync(second, ExecutionRequest());
            rejected.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
            second.RejectedCode.Should().Contain(BaseSubjectErrorCodes.BudgetExceeded);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_schema_rejects_pre_L53_activation_shape()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-schema-{Guid.NewGuid():N}.db");
        try
        {
            await using (SqliteRecordStore initialized = SemanticStore(path)) { }
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE hpd_base_activations DROP COLUMN terminal_receipt_checksum;";
                await command.ExecuteNonQueryAsync();
            }
            Action reopen = () => _ = SemanticStore(path);
            reopen.Should().Throw<InvalidOperationException>().WithMessage("*terminal_receipt_checksum*");
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_retirement_binds_the_exact_terminal_activation_receipt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-retire-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits mutationLimits = ExecutionLimits();
            await using SqliteRecordStore store = SemanticStore(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], mutationLimits, default)).Value!;
            var ensure = new SqliteSemanticEnsureProbe(authority, mutationLimits, "retire-parent");
            (await store.ExecuteAtomicAsync(ensure, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            await CompleteSemanticActivationAsync(store, "retirement");
            var retire = new SqliteSemanticEnsureProbe(authority, mutationLimits, "retirement", retire: true);
            (await store.ExecuteAtomicAsync(retire, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, retire.RejectedCode);
            retire.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Live);
            retire.Provisional!.ResultingState.Should().Be(BaseSemanticActivationSlotState.Retired);
            var duplicate = new SqliteSemanticEnsureProbe(authority, mutationLimits, "retirement-duplicate", retire: true);
            (await store.ExecuteAtomicAsync(duplicate, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, duplicate.RejectedCode);
            duplicate.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Retired);
            RejectsSubstitutedResultingSlotChecksum(retire);
            RejectsSubstitutedResultingSlotChecksum(duplicate);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_recovery_preflight_returns_exact_terminal_authority_without_open_transaction()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-preflight-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using SqliteRecordStore store = SemanticStore(path, maximumCanonicalKeyBytes: 12);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation-test", [], limits, default)).Value!;
            var ensure = new SqliteSemanticEnsureProbe(authority, limits, "preflight-parent");
            (await store.ExecuteAtomicAsync(ensure, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            await CompleteSemanticActivationAsync(store, "preflight");
            BaseSemanticActivationKeyDefinition installed = SemanticDefinition(maximumCanonicalKeyBytes: 12);
            byte[] canonicalKey = Encoding.UTF8.GetBytes("auth-user-42");
            var request = new BaseSemanticRecoveryPreflightRequest
            {
                Definition = new BaseSemanticActivationDefinitionIdentity
                {
                    Id = installed.Id, Version = installed.Version, Checksum = installed.Checksum,
                    OwnerGeneration = 1, OwningModuleId = installed.OwningModuleId,
                    RetirementOperation = installed.RetirementOperation,
                },
                CanonicalKey = canonicalKey.ToImmutableArray(),
                KeyPreimageChecksum = SHA256.HashData(canonicalKey).ToImmutableArray(),
                Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
                MaximumCanonicalKeyBytes = installed.Limits.MaximumCanonicalKeyBytes,
                StoreAuthority = authority.SemanticActivation!, Limits = installed.Limits.Execution,
                Deadline = TimeSpan.FromSeconds(5),
            };
            OperationResult<BaseSemanticRecoveryPreflightEvidence> result = await store.PreflightSemanticRecoveryAsync(request);
            result.IsSuccess().Should().BeTrue(result.Error?.Code);
            BaseSemanticActivationEvidenceContract.RecoveryPreflightIsValid(request, result.Value!).Should().BeTrue();
            result.Value!.ActivationTerminalReceiptChecksum.Should().HaveCount(32);

            BaseSemanticRecoveryPreflightRequest substituted = request with
            {
                StoreAuthority = request.StoreAuthority with { RestoreEpoch = checked(request.StoreAuthority.RestoreEpoch + 1) },
            };
            (await store.PreflightSemanticRecoveryAsync(substituted)).IsSuccess().Should().BeFalse();
            byte[] oversizedKey = Enumerable.Repeat((byte)'x', 13).ToArray();
            OperationResult<BaseSemanticRecoveryPreflightEvidence> oversized = await store.PreflightSemanticRecoveryAsync(request with
            {
                CanonicalKey = oversizedKey.ToImmutableArray(),
                KeyPreimageChecksum = SHA256.HashData(oversizedKey).ToImmutableArray(),
            });
            oversized.IsSuccess().Should().BeFalse();
            oversized.Error!.Code.Should().Be(BaseSemanticActivationErrorCodes.BudgetExceeded);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_retirement_charges_terminal_receipt_bytes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-receipt-budget-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using SqliteRecordStore store = SemanticStore(path, maximumReceiptBytes: 1);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits, default)).Value!;
            var ensure = new SqliteSemanticEnsureProbe(authority, limits, "receipt-parent");
            (await store.ExecuteAtomicAsync(ensure, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            await CompleteSemanticActivationAsync(store, "receipt-budget");
            var retire = new SqliteSemanticEnsureProbe(authority, limits, "receipt-retire", retire: true);
            RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(retire, ExecutionRequest());
            result.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
            retire.RejectedCode.Should().Contain(BaseSubjectErrorCodes.BudgetExceeded);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    private static async Task CompleteSemanticActivationAsync(SqliteRecordStore store, string identity)
    {
        BaseActivationExecutionLimits limits = ActivationLimits();
        BaseActivationDueObservation observed = (await store.ObserveDueAsync(new()
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [ActivationDefinition()], Scope = ActivationScope(),
            AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = limits,
        })).Value!;
        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker",
            Definitions = [ActivationDefinition()], Scope = ActivationScope(), Checksum = new byte[32].ToImmutableArray(),
        };
        var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new()
        {
            Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(10), LeaseMilliseconds = 1_000,
            Identity = ActivationIdentity(identity + "-claim"), Limits = limits,
        })).Value!;
        byte[] result = "done"u8.ToArray();
        (await store.TransitionAsync(new BaseActivationCompleteRequest
        {
            ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim,
            CanonicalResult = result.ToImmutableArray(), ResultChecksum = SHA256.HashData(result).ToImmutableArray(),
            AcceptedTime = AcceptedTime(20), Identity = ActivationIdentity(identity + "-complete"), Limits = limits,
        })).Value!.State.Should().Be(BaseActivationState.Succeeded);
    }

    private static void RejectsSubstitutedResultingSlotChecksum(SqliteSemanticEnsureProbe probe)
    {
        BaseProvisionalSemanticActivation hostile = probe.Provisional! with
        {
            ResultingSlotChecksum = Enumerable.Repeat((byte)0xA5, 32).ToImmutableArray(),
        };
        BaseModuleMutationProcessor<object, object>.ResultingSlotChecksumMatches(
            probe.FinalizedExtension!, probe.CapturedEvidence!, hostile).Should().BeFalse();
    }

    private static async Task DisposeSemanticActivationAsync(SqliteRecordStore store, string identity)
    {
        BaseActivationExecutionLimits limits = ActivationLimits();
        BaseActivationDueObservation observed = (await store.ObserveDueAsync(new()
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [ActivationDefinition()], Scope = ActivationScope(),
            AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = limits,
        })).Value!;
        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker",
            Definitions = [ActivationDefinition()], Scope = ActivationScope(), Checksum = new byte[32].ToImmutableArray(),
        };
        var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new()
        {
            Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(10), LeaseMilliseconds = 1_000,
            Identity = ActivationIdentity(identity + "-claim"), Limits = limits,
        })).Value!;
        byte[] result = "done"u8.ToArray();
        BaseActivationTransitionResult completed = (await store.TransitionAsync(new BaseActivationCompleteRequest
        {
            ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim,
            CanonicalResult = result.ToImmutableArray(), ResultChecksum = SHA256.HashData(result).ToImmutableArray(),
            AcceptedTime = AcceptedTime(20), Identity = ActivationIdentity(identity + "-complete"), Limits = limits,
        })).Value!;
        (await store.TransitionAsync(new BaseActivationDisposeRequest
        {
            ActivationId = claimed.Claim.ActivationId, ExpectedGeneration = completed.Generation,
            AcceptedTime = AcceptedTime(30), Identity = ActivationIdentity(identity + "-dispose"), Limits = limits,
        })).Value!.State.Should().Be(BaseActivationState.Disposed);
    }

    [Fact]
    public async Task Semantic_prepare_rejects_substituted_finalized_scope()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-hostile-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using SqliteRecordStore store = SemanticStore(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits, default)).Value!;
            var hostile = new SqliteSemanticEnsureProbe(authority, limits, "hostile", substituteFinalizedScope: true);
            RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(hostile, ExecutionRequest());
            result.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
            hostile.RejectedCode.Should().Contain(BaseSubjectErrorCodes.ProviderContractInvalid);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("input")]
    [InlineData("due")]
    [InlineData("limits")]
    public async Task Semantic_prepare_rejects_other_finalized_authority_substitutions(string substitution)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-hostile-{substitution}-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using SqliteRecordStore store = SemanticStore(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits, default)).Value!;
            var hostile = new SqliteSemanticEnsureProbe(authority, limits, "hostile-" + substitution, finalizedSubstitution: substitution);
            RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(hostile, ExecutionRequest());
            result.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
            hostile.RejectedCode.Should().Contain(BaseSubjectErrorCodes.ProviderContractInvalid);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_schema_rejects_reserved_wrong_live_index_shape()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-index-{Guid.NewGuid():N}.db");
        try
        {
            await using (SqliteRecordStore initialized = SemanticStore(path)) { }
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync(); await using var command = connection.CreateCommand();
                command.CommandText = "DROP INDEX hpd_base_semantic_activation_live_idx; CREATE INDEX hpd_base_semantic_activation_live_idx ON hpd_base_semantic_activation_slots(slot_generation);";
                await command.ExecuteNonQueryAsync();
            }
            Action reopen = () => _ = SemanticStore(path);
            reopen.Should().Throw<InvalidOperationException>().WithMessage("*index-shape:hpd_base_semantic_activation_live_idx*");
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_retirement_rejects_substituted_terminal_receipt_authority()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-receipt-hostile-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using SqliteRecordStore store = SemanticStore(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits, default)).Value!;
            var ensure = new SqliteSemanticEnsureProbe(authority, limits, "receipt-hostile-parent");
            (await store.ExecuteAtomicAsync(ensure, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            await CompleteSemanticActivationAsync(store, "receipt-hostile");
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync(); await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE hpd_base_activations SET terminal_receipt_checksum=$checksum; UPDATE hpd_base_activation_receipts SET authority_checksum=$checksum WHERE activation_id IS NOT NULL;";
                command.Parameters.Add("$checksum", Microsoft.Data.Sqlite.SqliteType.Blob).Value = Enumerable.Repeat((byte)0xA5, 32).ToArray();
                await command.ExecuteNonQueryAsync();
            }
            var retire = new SqliteSemanticEnsureProbe(authority, limits, "receipt-hostile-retire", retire: true);
            RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(retire, ExecutionRequest());
            result.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
            retire.RejectedCode.Should().Contain(BaseSubjectErrorCodes.ProviderContractInvalid);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_migration_stages_fixed_pages_and_resumes_after_restart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-migrate-{Guid.NewGuid():N}.db");
        try
        {
            BaseSemanticActivationKeyDefinition sourceDefinition = SemanticDefinition();
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using (SqliteRecordStore initial = SemanticStore(path, installedDefinition: sourceDefinition))
            {
                BaseAtomicMutationAuthorityRequirement authority = (await initial.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
                (await initial.ExecuteAtomicAsync(new SqliteSemanticEnsureProbe(authority, limits, "migration-parent"), ExecutionRequest()))
                    .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                (await initial.ExecuteAtomicAsync(new SqliteSemanticEnsureProbe(authority, limits, "migration-parent-2", "auth-user-43"), ExecutionRequest()))
                    .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            }
            BaseSemanticActivationKeyDefinition to = sourceDefinition with
            {
                Version = 2, EnsureGrantId = "semantic.ensure.v2",
                Checksum = SHA256.HashData("semantic-definition-v2"u8).ToImmutableArray(),
            };
            BaseSemanticActivationMigrationDefinition migration = BaseSemanticActivationMigrationContract.Seal(new()
            {
                Id = "test.semantic.migration", Version = 1,
                From = new() { Id = sourceDefinition.Id, Version = sourceDefinition.Version, Checksum = sourceDefinition.Checksum },
                To = new() { Id = to.Id, Version = to.Version, Checksum = to.Checksum }, Checksum = [],
            });
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create("semantic-maintenance", "migrate", "migration-1",
                BaseMutationRequestFingerprint.Create(SHA256.HashData("migration-request"u8)));
            BaseSemanticActivationMigrateRequest request = new()
            {
                Identity = identity, Definition = migration.From, ExpectedSemanticAuthorityGeneration = 1,
                Migration = migration, Limits = new() { PageSize = 1, MaximumPages = 2, MaximumRows = 2,
                    MaximumBytes = 1_000_000, Deadline = TimeSpan.FromSeconds(5) },
            };
            await using (SqliteRecordStore first = SemanticStore(path, installedDefinition: to, ownerGeneration: 2,
                definitionSetChecksum: to.Checksum, migrations: [migration]))
            {
                BaseResult<BaseSemanticActivationMaintenanceResult> progress = await first.ExecuteAsync(request, default);
                progress.RequireValue().Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.InProgress);
                progress.RequireValue().Checkpoint!.CompletedRows.Should().Be(1);
            }
            await using (SqliteRecordStore resumed = SemanticStore(path, installedDefinition: to, ownerGeneration: 2,
                definitionSetChecksum: to.Checksum, migrations: [migration]))
            {
                BaseResult<BaseSemanticActivationMaintenanceResult> completed = await resumed.ExecuteAsync(request, default);
                completed.RequireValue().Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
                completed.RequireValue().ResultingAuthorityGeneration.Should().Be(2);
                completed.RequireValue().ChangedRows.Should().Be(2);
                (await resumed.ExecuteAsync(request, default)).RequireValue().Disposition
                    .Should().Be(BaseSemanticActivationMaintenanceDisposition.Duplicate);
                BaseAtomicMutationAuthorityRequirement authority = (await resumed.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
                authority.SemanticActivation!.SemanticAuthorityGeneration.Should().Be(2);
                authority.SemanticActivation.DefinitionSetChecksum.Should().Equal(to.Checksum);
            }
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task In_place_restore_unions_post_artifact_retirement_without_rematerialization(
        bool artifactContainsLive, bool pruneAfterRetirement)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-restore-floor-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using SqliteRecordStore store = SemanticStore(path, administrationEnabled: true);
            var artifact = new MemoryStream();
            OperationResult<BaseBackupManifest>? backup = null;
            if (!artifactContainsLive)
            {
                backup = await store.CreateBackupAsync(artifact, new BaseBackupRequest
                { StoreId = "module-store", Principal = AdministrationPrincipal() });
                backup.IsSuccess().Should().BeTrue(backup.Error?.Code);
            }

            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
            var ensure = new SqliteSemanticEnsureProbe(authority, limits, "restore-live");
            (await store.ExecuteAtomicAsync(ensure, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            if (artifactContainsLive)
            {
                backup = await store.CreateBackupAsync(artifact, new BaseBackupRequest
                { StoreId = "module-store", Principal = AdministrationPrincipal() });
                backup.IsSuccess().Should().BeTrue(backup.Error?.Code);
            }

            if (pruneAfterRetirement)
                await DisposeSemanticActivationAsync(store, "restore-terminal");
            else
                await CompleteSemanticActivationAsync(store, "restore-terminal");
            authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
            var retire = new SqliteSemanticEnsureProbe(authority, limits, "restore-retire", retire: true);
            RecordMutationExecutionResult retirement = await store.ExecuteAtomicAsync(retire, ExecutionRequest());
            retirement.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, $"{retire.RejectedCode}:{retirement.Error?.Code}:{retirement.Error?.Message}");
            await using (var before = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await before.OpenAsync(); await using var floor = before.CreateCommand();
                floor.CommandText = "INSERT INTO hpd_base_semantic_activation_recovery_floors(definition_id,binding_id,key_digest,state,slot_generation,authority_json,receipt_scope,receipt_operation,receipt_key,receipt_fingerprint,receipt_structural_digest,receipt_result_json,receipt_authority_checksum) SELECT definition_id,binding_id,key_digest,state,slot_generation,authority_json,'semantic','retire','restore-retire',$fingerprint,$structural,$result,$authority FROM hpd_base_semantic_activation_slots WHERE state=2;";
                byte[] fingerprint = SHA256.HashData("restore-fingerprint"u8); byte[] structural = SHA256.HashData("restore-structural"u8);
                floor.Parameters.Add("$fingerprint", Microsoft.Data.Sqlite.SqliteType.Blob).Value = fingerprint;
                floor.Parameters.Add("$structural", Microsoft.Data.Sqlite.SqliteType.Blob).Value = structural;
                floor.Parameters.Add("$result", Microsoft.Data.Sqlite.SqliteType.Blob).Value = retire.RecoveryReceiptJson!;
                floor.Parameters.Add("$authority", Microsoft.Data.Sqlite.SqliteType.Blob).Value = BaseSemanticActivationEvidenceContract.RecoveryReceiptChecksum(
                    "semantic", "retire", "restore-retire", fingerprint, structural, retire.RecoveryReceiptJson!).ToArray();
                (await floor.ExecuteNonQueryAsync()).Should().Be(1);
            }

            if (pruneAfterRetirement)
            {
                OperationResult<BaseActivationPrunePage> pruned = await store.PruneAsync(new BaseActivationPruneRequest
                {
                    ApplicationId = "activation-test", Scope = ActivationScope(), Definition = ActivationDefinition(),
                    Take = 1, AcceptedTime = AcceptedTime(40), Identity = ActivationIdentity("restore-prune"),
                    Limits = ActivationLimits(),
                });
                pruned.IsSuccess().Should().BeTrue(pruned.Error?.Code);
                pruned.Value!.Items.Should().ContainSingle();
                pruned.Value.Items[0].ActivationId.Should().Be(retire.Provisional!.ActivationId);
            }

            artifact.Position = 0;
            BaseBackupManifest manifest = backup!.Value!;
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
            restored.IsSuccess().Should().BeTrue($"{restored.Error?.Code}:{restored.Error?.Message}:{restored.Error?.Detail}");

            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync(); await using var command = connection.CreateCommand();
            command.CommandText = "SELECT s.state,COUNT(*),f.state,(SELECT COUNT(*) FROM hpd_base_activations a WHERE a.activation_id=(SELECT json_extract(s2.authority_json,'$.activationId') FROM hpd_base_semantic_activation_slots s2 LIMIT 1)) FROM hpd_base_semantic_activation_slots s JOIN hpd_base_semantic_activation_recovery_floors f USING(definition_id,binding_id,key_digest) GROUP BY s.state,f.state;";
            await using Microsoft.Data.Sqlite.SqliteDataReader reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetInt32(0).Should().Be((int)BaseSemanticActivationSlotState.Retired);
            reader.GetInt64(1).Should().Be(1);
            reader.GetInt32(2).Should().Be((int)BaseSemanticActivationSlotState.Retired);
            reader.GetInt64(3).Should().Be(pruneAfterRetirement ? 0 : 1);
            (await reader.ReadAsync()).Should().BeFalse();
            if (pruneAfterRetirement)
            {
                await reader.DisposeAsync();
                command.CommandText = "SELECT COUNT(*) FROM hpd_base_activation_prune_floors WHERE activation_id=$id;";
                command.Parameters.AddWithValue("$id", retire.Provisional!.ActivationId);
                Convert.ToInt64(await command.ExecuteScalarAsync()).Should().Be(1);
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    private static SqliteRecordStore SemanticStore(string path, long maximumLiveSlots = 100,
        int maximumPendingRows = 1_000_000, long maximumReceiptBytes = 4096,
        BaseSemanticActivationKeyDefinition? installedDefinition = null,
        long ownerGeneration = 1,
        ImmutableArray<byte> definitionSetChecksum = default,
        ImmutableArray<BaseSemanticActivationMigrationDefinition> migrations = default,
        bool administrationEnabled = false,
        int maximumCanonicalKeyBytes = 256)
    {
        BaseSemanticActivationKeyDefinition definition = installedDefinition ?? SemanticDefinition(maximumLiveSlots, maximumReceiptBytes, maximumCanonicalKeyBytes);
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "module-store", DataSource = path, Collections = [],
            SemanticActivations = [definition], SemanticActivationApplicationId = definition.OwningApplicationId,
            SemanticActivationOwnerGeneration = ownerGeneration,
            SemanticActivationDefinitionSetChecksum = definitionSetChecksum.IsDefaultOrEmpty
                ? definition.Checksum.ToArray() : definitionSetChecksum.ToArray(),
            SemanticActivationMigrations = migrations.IsDefault ? [] : migrations.ToArray(),
            ModuleMutations = [Definition()], ModuleGenerationCells = [Cell()],
            MaxPendingActivationRows = maximumPendingRows, MaxClaimedActivationRows = 100,
            AdministrationEnabled = administrationEnabled, MaxBackupArtifactBytes = 16 * 1024 * 1024,
        };
        var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey { Id = 31, Key = Enumerable.Repeat((byte)0x31, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch },
        }));
        var store = new SqliteRecordStore(options, NullLoggerFactory.Instance, TimeProvider.System, tokenProtector: protector);
        store.InitializeUnacceptedSchemaForTestsAsync().AsTask().GetAwaiter().GetResult();
        if (administrationEnabled)
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO hpd_base_schema_identity(singleton,store_instance_id) VALUES (1,'module-store');
                INSERT OR IGNORE INTO hpd_base_schema_baseline(application_id,store_instance_id,baseline_id,checksum,generation,last_plan_id,applied_at)
                VALUES ('activation-test','module-store','baseline-1','checksum-1',1,'plan-1','2026-08-22T00:00:00Z');
                """;
            command.ExecuteNonQuery();
            _ = store.InspectSchemaAsync(new BaseSchemaInspectionRequest
            {
                ApplicationId = "activation-test", ExpectedLogicalChecksum = "checksum-1",
            }).AsTask().GetAwaiter().GetResult();
        }
        return store;
    }

    private static BaseSemanticActivationKeyDefinition SemanticDefinition(long maximumLiveSlots = 100,
        long maximumReceiptBytes = 4096, int maximumCanonicalKeyBytes = 256) => new()
    {
        Id = "test.semantic", Version = 1, OwningApplicationId = "activation-test", OwningModuleId = "test",
        EnsureOperation = new() { OperationId = "semantic.ensure", OperationVersion = 1, OperationChecksum = new string('a', 64) },
        RetirementOperation = new() { OperationId = "semantic.retire", OperationVersion = 1, OperationChecksum = Convert.ToHexStringLower(SHA256.HashData("completion-operation"u8)) },
        Activation = ActivationDefinition(), ScopeKind = BaseSubjectScopeKind.Global,
        EnsureGrantId = "semantic.ensure", RetirementGrantId = "semantic.retire", MaintenanceGrantId = "semantic.maintain",
        Compaction = new BaseSemanticActivationNoCompaction(), RequestTypeId = "request",
        RequestSerializerChecksum = SHA256.HashData("request"u8).ToImmutableArray(),
        KeyExpressionChecksum = SHA256.HashData("key"u8).ToImmutableArray(),
        Limits = new()
        {
            MaximumCanonicalKeyBytes = maximumCanonicalKeyBytes, MaximumLiveSlots = maximumLiveSlots, MaximumRetiredSlots = 100, MaximumAbsenceMarkers = 100,
            Execution = SqliteSemanticEnsureProbe.CreateLimits() with { MaximumReceiptBytes = maximumReceiptBytes },
            Deadlines = new()
            {
                AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
                CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
                MaintenanceTimeout = TimeSpan.FromSeconds(5), QuarantineRetentionTimeout = TimeSpan.FromSeconds(5),
            },
        },
        Checksum = SHA256.HashData("semantic-definition"u8).ToImmutableArray(),
    };

    private static BaseTransactionalActivationRegistration<Request, Result> RuntimeActivation(
        BaseRegisteredModuleMutationDefinition module) => BaseActivationDefinitionBuilder.CreateTransactional(new BaseActivationDefinition
        {
            Id = "module.semantic.child", Version = 1, OwningModuleId = "module",
            ExecutionClass = BaseActivationExecutionClass.TransactionalOperation, InputTypeId = "request", ResultTypeId = "result",
            Grants = new BaseActivationGrantSet
            {
                Enqueue = "activation.enqueue", Observe = "activation.observe", Claim = "activation.claim", Execute = "activation.execute",
                Renew = "activation.renew", Complete = "activation.complete", Fail = "activation.fail", Cancel = "activation.cancel",
                Inspect = "activation.inspect", Replay = "activation.replay", Migrate = "activation.migrate", Reconcile = "activation.reconcile",
                Retry = "activation.retry", Dispose = "activation.dispose", Remove = "activation.remove", Repair = "activation.repair",
            },
            SourceGrantIds = [], Retry = new BaseActivationRetryProfile
            {
                MaximumAttempts = 1, InitialDelayMilliseconds = 1, MaximumDelayMilliseconds = 1,
                MultiplierNumerator = 1, MultiplierDenominator = 1, JitterBasisPoints = 0, RetryableFailureCodes = [],
            },
            Limits = new BaseActivationLimits
            {
                MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 1, MaximumRenewalsPerAttempt = 1,
                MaximumChildrenPerAttempt = 4, MaximumLineageDepth = 4, LeaseDuration = TimeSpan.FromMinutes(1),
                HandlerTimeout = TimeSpan.FromMinutes(1), Provider = ActivationLimits(), AtomicCreation = ExecutionLimits(),
            },
            TransactionalTarget = new BaseModuleMutationActivationTarget
            {
                OperationId = module.Id, OperationVersion = module.Version,
                OperationChecksum = Convert.ToHexStringLower(module.Checksum.ToArray()),
            },
            Checksum = [],
        }, Json.Default.Request, Json.Default.Result, [],
        [BaseModuleDtoPropertyBinding.Create<Result, string>("result.generation", nameof(Result.Generation))]);

    private static DefaultBaseModuleMutationRuntime RuntimeSemantic(
        SqliteRecordStore store,
        BaseRegisteredModuleMutationDefinition module,
        BaseSemanticActivationRegistry semanticRegistry,
        BaseTransactionalActivationRegistration<Request, Result> activation)
    {
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store });
        return new DefaultBaseModuleMutationRuntime(stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>()),
            new BaseModuleMutationRegistry([module], [Cell()]), null!, SemanticPolicy(), null!, new BaseSubjectContractRegistry([]),
            TimeProvider.System, semanticRegistry: semanticRegistry,
            activationRegistry: new BaseActivationRegistry([new BaseInstalledTransactionalActivationRegistration<Request, Result>(activation)]),
            acceptedTimeAuthority: new BaseActivationAcceptedTimeAuthority(TimeProvider.System));
    }

    private static DefaultBasePolicyOrchestrator SemanticPolicy()
    {
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(new BasePolicyAuthorityDefinition
        {
            Id = "module.semantic.policy", Version = 1, OwningModuleId = "module",
            EvaluatorContractId = "module.semantic.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, new AllowPolicyEvaluator());
        foreach (string grantId in new[] { "module.increment", "semantic.ensure", "semantic.retire" })
        {
            builder.AddStaticGrant(new BaseGrantAuthorityDefinition
            {
                Id = grantId, Version = 1, OwningModuleId = "module", SourceContractId = "module.semantic.grants", SourceContractVersion = 1,
            }, new AccessGrant
            {
                Id = grantId, ApplicationId = "module.application", ModuleId = "module", Audience = HPDBaseEndpointAudience.ControlPlane,
                Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
                Action = grantId == "module.increment" ? grantId : "module.semantic.runtime",
                Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
            });
        }
        return new DefaultBasePolicyOrchestrator(builder.Freeze("module.application"));
    }

    private sealed class RuntimeSemanticMarker;

    private sealed class SqliteSemanticEnsureProbe(
        BaseAtomicMutationAuthorityRequirement authority,
        BaseAtomicMutationExecutionLimits limits,
        string parentIdentity,
        string semanticKey = "auth-user-42",
        bool retire = false,
        bool substituteFinalizedScope = false,
        string? finalizedSubstitution = null,
        bool acceptedCurrentTime = false) : IAtomicMutationProcessor
    {
        public BaseSemanticActivationCapturedState? CapturedState { get; private set; }
        public BaseCapturedSemanticActivationEvidence? CapturedEvidence { get; private set; }
        public BaseAtomicSemanticActivationExtension? FinalizedExtension { get; private set; }
        public BaseProvisionalSemanticActivation? Provisional { get; private set; }
        public byte[]? RecoveryReceiptJson { get; private set; }
        public string RejectedCode { get; private set; } = string.Empty;

        public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
            BaseAtomicReceiptResult committedResult,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.ReadyToCommit, committedResult));

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(IAtomicRecordSession session, CancellationToken cancellationToken = default)
        {
            byte[] definitionChecksum = SHA256.HashData("semantic-definition"u8);
            byte[] canonicalKey = Encoding.UTF8.GetBytes(semanticKey);
            byte[] binding = SHA256.HashData(Encoding.UTF8.GetBytes("runtime-proposed-binding:" + parentIdentity));
            BaseSemanticActivationKeyDigest key = BaseSemanticActivationKeyDigest.Create(BoundHash("base.semanticActivation.key.v1\0", "test.semantic"u8.ToArray(), binding, canonicalKey));
            Span<byte> keyBytes = stackalloc byte[32]; key.CopyTo(keyBytes);
            byte[] activationId = BoundHash("base.semanticActivation.activation.v1\0", Encoding.UTF8.GetBytes(authority.ApplicationId),
                Encoding.UTF8.GetBytes(authority.StoreInstanceId), "test"u8.ToArray(), "test.semantic"u8.ToArray(), binding, canonicalKey);
            var retirement = new BaseSemanticActivationModuleOperationIdentity
            {
                OperationId = "semantic.retire", OperationVersion = 1,
                OperationChecksum = Convert.ToHexStringLower(SHA256.HashData("completion-operation"u8)),
            };
            var definition = new BaseSemanticActivationDefinitionIdentity
            {
                Id = "test.semantic", Version = 1, Checksum = definitionChecksum.ToImmutableArray(), OwnerGeneration = 1,
                OwningModuleId = "test", RetirementOperation = retirement,
            };
            var scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global };
            var due = new BaseSemanticActivationDueAuthority
            {
                Mode = acceptedCurrentTime ? BaseSemanticActivationDueMode.AcceptedCurrentTime : BaseSemanticActivationDueMode.ExplicitUtcInstant,
                CanonicalUnixMilliseconds = acceptedCurrentTime ? 0 : 1,
            };
            var ensure = new BaseSemanticActivationEnsureIntent
            {
                Definition = definition, Key = key, CanonicalKey = canonicalKey.ToImmutableArray(), Scope = scope, Due = due,
                Activation = new()
                {
                    Definition = ActivationDefinition(), CanonicalInput = "payload"u8.ToArray().ToImmutableArray(),
                    InputChecksum = SHA256.HashData("payload"u8).ToImmutableArray(), Scope = scope, Due = due, Priority = 0, InitiallyEligible = true,
                    Limits = CreationLimits(), Identity = new()
                    {
                        SemanticDefinition = definition, Key = key, ScopeBindingId = binding.ToImmutableArray(),
                        DerivedActivationIdBytes = activationId.ToImmutableArray(),
                        Checksum = BoundHash("base.semanticActivation.creation.v1\0", definitionChecksum, keyBytes.ToArray(), binding, activationId).ToImmutableArray(),
                    },
                },
            };
            BaseSemanticActivationOperation operation = retire
                ? new BaseSemanticActivationRetireIntent
                {
                    Definition = definition, Key = key, CanonicalKey = canonicalKey.ToImmutableArray(), Scope = scope,
                    CompletionOperation = retirement,
                }
                : ensure;
            var extension = new BaseAtomicSemanticActivationExtension
            {
                Capture = new()
                {
                    Definition = definition, CanonicalKey = canonicalKey.ToImmutableArray(), KeyPreimageChecksum = SHA256.HashData(canonicalKey).ToImmutableArray(),
                    Scope = scope, ProposedScopeBindingId = binding.ToImmutableArray(), Operation = retire
                        ? BaseSemanticActivationOperationKind.Retire : BaseSemanticActivationOperationKind.Ensure,
                    StoreAuthority = new()
                    {
                        ApplicationId = authority.ApplicationId, LogicalStoreId = authority.StoreInstanceId, StoreInstanceId = authority.StoreInstanceId,
                        RestoreEpoch = authority.RestoreEpoch, SchemaGeneration = authority.SchemaGeneration, SemanticAuthorityGeneration = 1,
                        DefinitionSetChecksum = definitionChecksum.ToImmutableArray(),
                    },
                    Limits = CreateLimits(), AcceptedTime = AcceptedTime(retire ? 30 : 1),
                },
                Operation = operation,
                StructuralDigest = BoundHash("base.semanticActivation.extension.v1\0", definitionChecksum, canonicalKey, binding, [retire ? (byte)2 : (byte)1]).ToImmutableArray(),
            };
            var request = new BaseAtomicExecutionRequest
            {
                Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
                Intent = new() { IntentDigest = parentIdentity, Authority = authority, Items = [] },
                Module = new() { OperationId = retire ? "semantic.retire" : "semantic.ensure", OperationVersion = 1,
                    OperationChecksum = retire ? retirement.OperationChecksum : new string('a', 64), RequestDigest = parentIdentity, Records = [], RelationTargets = [], Generations = [] },
                SemanticActivation = extension, Limits = limits,
            };
            OperationResult<BaseCapturedAtomicExecution> captured = await session.CaptureAtomicExecutionAsync(request, cancellationToken);
            if (!captured.IsSuccess() || captured.Value?.SemanticActivation is null) { RejectedCode = "capture:" + captured.Error?.Code; return Failure(captured.Error); }
            CapturedState = captured.Value.SemanticActivation.State;
            CapturedEvidence = captured.Value.SemanticActivation;
            BaseAtomicSemanticActivationExtension finalizedExtension = FinalizeExtension(extension, captured.Value.SemanticActivation, authority);
            finalizedExtension = substituteFinalizedScope && finalizedExtension.Operation is BaseSemanticActivationEnsureIntent finalizedEnsure
                ? finalizedExtension with
                {
                    Operation = finalizedEnsure with { Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "substituted" } },
                }
                : finalizedExtension;
            if (finalizedExtension.Operation is BaseSemanticActivationEnsureIntent hostileEnsure)
            {
                finalizedExtension = finalizedSubstitution switch
                {
                    "definition" => finalizedExtension with
                    {
                        Operation = hostileEnsure with { Definition = hostileEnsure.Definition with { Version = 2 } },
                    },
                    "input" => finalizedExtension with
                    {
                        Operation = hostileEnsure with { Activation = hostileEnsure.Activation with { CanonicalInput = "substituted"u8.ToArray().ToImmutableArray() } },
                    },
                    "due" => finalizedExtension with
                    {
                        Operation = hostileEnsure with { Due = hostileEnsure.Due with { CanonicalUnixMilliseconds = 2 } },
                    },
                    "limits" => finalizedExtension with
                    {
                        Operation = hostileEnsure with { Activation = hostileEnsure.Activation with { Limits = hostileEnsure.Activation.Limits with { MaximumInputBytes = 1 } } },
                    },
                    _ => finalizedExtension,
                };
            }
            FinalizedExtension = finalizedExtension;
            var plan = new BaseFinalizedAtomicExecutionPlan
            {
                Kind = request.Kind, PlanDigest = "plan-" + parentIdentity, IntentDigest = request.Intent.IntentDigest,
                CaptureDigest = captured.Value.CaptureDigest, PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]),
                Authority = authority, Items = [], SubjectValidations = [], Limits = limits, SemanticActivation = finalizedExtension,
                Module = new() { OperationId = retire ? "semantic.retire" : "semantic.ensure", OperationVersion = 1,
                    OperationChecksum = retire ? retirement.OperationChecksum : new string('a', 64), Decisions = [], ItemBindings = [], RelationTargets = [], Comparisons = [], Increments = [], ResultProjectionDigest = parentIdentity },
            };
            OperationResult<BasePreparedAtomicExecution> prepared = await session.PrepareAtomicExecutionAsync(captured.Value, plan, cancellationToken);
            if (!prepared.IsSuccess() || prepared.Value?.SemanticActivation is null) { RejectedCode = "prepare:" + prepared.Error?.Code; return Failure(prepared.Error); }
            OperationResult<BaseProvisionalAtomicExecution> applied = await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken);
            if (!applied.IsSuccess() || applied.Value?.SemanticActivation is null) { RejectedCode = "apply:" + applied.Error?.Code; return Failure(applied.Error); }
            Provisional = applied.Value.SemanticActivation;
            if (!retire) return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, []);
            BaseProvisionalSemanticActivation provisional = Provisional!;
            BaseSemanticActivationKeyDigest committedKey = ((BaseSemanticActivationRetireIntent)finalizedExtension.Operation).Key;
            byte[] slotChecksum = provisional.ResultingSlotChecksum.ToArray();
            byte[] commitChecksum = BoundHash("base.semanticActivation.commit.v1\0", slotChecksum,
                BitConverter.GetBytes(provisional.CommitJournalPosition).Reverse().ToArray());
            Span<byte> committedKeyBytes = stackalloc byte[32]; committedKey.CopyTo(committedKeyBytes);
            var semanticReceipt = new BaseSemanticActivationReceiptEvidence
            {
                Operation = BaseSemanticActivationOperationKind.Retire,
                DefinitionId = definition.Id, DefinitionVersion = definition.Version, DefinitionChecksum = definition.Checksum,
                Key = committedKey, State = provisional.ResultingState, SlotGeneration = provisional.ResultingSlotGeneration,
                EnsureDisposition = null, RetirementDisposition = BaseSemanticActivationRetirementDisposition.RetiredNow,
                ActivationId = null, SlotChecksum = slotChecksum.ToImmutableArray(),
                JournalPosition = provisional.CommitJournalPosition, CommitEvidenceChecksum = commitChecksum.ToImmutableArray(),
                Checksum = BoundHash("base.semanticActivation.receipt.v1\0", definition.Checksum.ToArray(),
                    committedKeyBytes.ToArray(), slotChecksum, commitChecksum).ToImmutableArray(),
            };
            var recoveryReceipt = new BaseAtomicReceiptResult
            {
                Kind = BaseAtomicReceiptResultKind.ModuleMutation, Mutations = [],
                ModuleMutation = new BaseModuleMutationReceiptResult
                {
                    OperationId = "semantic.retire", OperationVersion = 1,
                    Disposition = BaseMutationRequestDisposition.Committed, Outcome = BaseModuleMutationOutcome.Committed,
                    Generations = [], CanonicalResultBytes = [], CreatedActivationIds = [], SemanticActivation = semanticReceipt,
                },
            };
            RecoveryReceiptJson = JsonSerializer.SerializeToUtf8Bytes(BaseAtomicReceiptWire.From(recoveryReceipt),
                HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
            return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, []);
        }

        private static BaseAtomicSemanticActivationExtension FinalizeExtension(
            BaseAtomicSemanticActivationExtension requested,
            BaseCapturedSemanticActivationEvidence captured,
            BaseAtomicMutationAuthorityRequirement authority)
        {
            BaseSemanticActivationScopeBinding binding = captured.ScopeDirectory.ResultingBinding;
            BaseSemanticActivationDefinitionIdentity definition = requested.Capture.Definition;
            byte[] canonicalKey = requested.Capture.CanonicalKey.ToArray();
            BaseSemanticActivationKeyDigest key = BaseSemanticActivationKeyDigest.Create(BoundHash(
                "base.semanticActivation.key.v1\0", Encoding.UTF8.GetBytes(definition.Id), binding.BindingId.ToArray(), canonicalKey));
            BaseSemanticActivationOperation operation = requested.Operation switch
            {
                BaseSemanticActivationEnsureIntent ensure => FinalizeEnsure(ensure, definition, key, binding.BindingId, captured.AcceptedTime,
                    requested.Capture.StoreAuthority),
                BaseSemanticActivationRetireIntent retire => retire with
                {
                    Key = key,
                    SubjectLifetime = FinalizeLifetime(retire.SubjectLifetime, binding.BindingId),
                },
                _ => throw new InvalidOperationException(),
            };
            return requested with
            {
                Operation = operation,
                StructuralDigest = BoundHash("base.semanticActivation.extension.v1\0", definition.Checksum.ToArray(), canonicalKey,
                    binding.BindingId.ToArray(), [requested.Operation is BaseSemanticActivationEnsureIntent ? (byte)1 : (byte)2]).ToImmutableArray(),
            };
        }

        private static BaseSemanticActivationEnsureIntent FinalizeEnsure(
            BaseSemanticActivationEnsureIntent ensure,
            BaseSemanticActivationDefinitionIdentity definition,
            BaseSemanticActivationKeyDigest key,
            ImmutableArray<byte> binding,
            BaseAcceptedTimeReceipt acceptedTime,
            BaseSemanticActivationStoreAuthorityRequirement store)
        {
            Span<byte> keyBytes = stackalloc byte[32]; key.CopyTo(keyBytes);
            byte[] activationId = BoundHash("base.semanticActivation.activation.v1\0", Encoding.UTF8.GetBytes(store.ApplicationId),
                Encoding.UTF8.GetBytes(store.LogicalStoreId), Encoding.UTF8.GetBytes(definition.OwningModuleId),
                Encoding.UTF8.GetBytes(definition.Id), binding.ToArray(), ensure.CanonicalKey.ToArray());
            BaseSemanticActivationDueAuthority due = ensure.Due.Mode == BaseSemanticActivationDueMode.AcceptedCurrentTime
                ? ensure.Due with { CanonicalUnixMilliseconds = acceptedTime.CapturedUtc }
                : ensure.Due;
            byte[] identityChecksum = BoundHash("base.semanticActivation.creation.v1\0", definition.Checksum.ToArray(),
                keyBytes.ToArray(), binding.ToArray(), activationId);
            return ensure with
            {
                Key = key,
                SubjectLifetime = FinalizeLifetime(ensure.SubjectLifetime, binding),
                Due = due,
                Activation = ensure.Activation with
                {
                    Due = due,
                    Identity = ensure.Activation.Identity with
                    {
                        SemanticDefinition = definition, Key = key, ScopeBindingId = binding.ToArray().ToImmutableArray(),
                        DerivedActivationIdBytes = activationId.ToImmutableArray(), Checksum = identityChecksum.ToImmutableArray(),
                    },
                },
            };
        }

        private static BaseSemanticActivationSubjectLifetimeBinding? FinalizeLifetime(
            BaseSemanticActivationSubjectLifetimeBinding? value,
            ImmutableArray<byte> binding)
        {
            if (value is null) return null;
            var bound = value with { ScopeBindingId = binding.ToArray().ToImmutableArray(), Checksum = [] };
            byte[] checksum = BoundHash("base.semanticActivation.subjectLifetime.v1\0", Encoding.UTF8.GetBytes(bound.ContractId),
                BitConverter.GetBytes(bound.ContractVersion).Reverse().ToArray(), bound.ContractChecksum.ToArray(), bound.SubjectId.ToUtf8Bytes(),
                Encoding.UTF8.GetBytes(bound.AuthorityEpoch.ToBase64Url()), Encoding.UTF8.GetBytes(bound.Incarnation.ToBase64Url()), binding.ToArray());
            return bound with { Checksum = checksum.ToImmutableArray() };
        }

        internal static BaseSemanticActivationExecutionLimits CreateLimits() => new()
        {
            MaximumOperations = 1, MaximumScopeDirectoryReads = 1, MaximumSlotReads = 1, MaximumActivationReads = 1,
            MaximumReadIntervals = 4, MaximumIndexOperations = 4, MaximumActivationBytes = 4096,
            MaximumScopeDirectoryBytes = 4096, MaximumEvidenceBytes = 16384, MaximumReceiptBytes = 4096, MaximumTransientBytes = 32768,
        };

        private static BaseActivationLimits CreationLimits() => new()
        {
            MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 3, MaximumRenewalsPerAttempt = 3,
            MaximumChildrenPerAttempt = 8, MaximumLineageDepth = 8, LeaseDuration = TimeSpan.FromMinutes(1), HandlerTimeout = TimeSpan.FromMinutes(1),
            Provider = ActivationLimits(), AtomicCreation = ExecutionLimits(),
        };

        private static byte[] BoundHash(string marker, params byte[][] parts)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData(Encoding.UTF8.GetBytes(marker));
            Span<byte> length = stackalloc byte[4];
            foreach (byte[] part in parts) { BinaryPrimitives.WriteInt32BigEndian(length, part.Length); hash.AppendData(length); hash.AppendData(part); }
            return hash.GetHashAndReset();
        }

        private static AtomicMutationProcessingResult Failure(BaseError? error) =>
            new(AtomicMutationProcessingOutcome.Failed, [], error ?? new BaseError { Code = "probe", Message = "Probe failed.", Category = ErrorCategory.Unexpected });
    }
}
