using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HPD.Base.Testing;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed partial class SqliteModuleMutationTests
{
    [Fact]
    public async Task Semantic_maintenance_authority_is_exact_bounded_and_hostile_requests_fail_closed()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-semantic-authority-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            BaseSemanticActivationKeyDefinition definition = SemanticDefinition();
            await using SqliteRecordStore store = SemanticStore(path, installedDefinition: definition, administrationEnabled: true);
            BaseAtomicMutationAuthorityRequirement captured = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation-test", [], limits)).Value!;
            var ensure = new SqliteSemanticEnsureProbe(captured, limits, "authority-live", semanticDefinition: definition);
            (await store.ExecuteAtomicAsync(ensure, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);

            BaseSemanticActivationStoreAuthorityRequirement authority = captured.SemanticActivation!;
            BaseSemanticActivationMaintenanceAuthorityRequest request = new()
            {
                ApplicationId = authority.ApplicationId, LogicalStoreId = authority.LogicalStoreId,
                RestoreEpoch = authority.RestoreEpoch,
                Definition = new() { Id = definition.Id, Version = definition.Version, Checksum = definition.Checksum },
                SemanticAuthorityGeneration = authority.SemanticAuthorityGeneration,
                MaximumRows = 1, MaximumBytes = 1_000_000, RuntimeRequestChecksum = [],
            };
            request = request with { RuntimeRequestChecksum = BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(request) };
            BaseSemanticActivationMaintenanceAuthority exact = (await store.InspectMaintenanceAuthorityAsync(request, default)).RequireValue();
            exact.LiveCount.Should().Be(1); exact.RetiredCount.Should().Be(0); exact.AbsenceCount.Should().Be(0);
            exact.ExaminedRows.Should().Be(1); exact.CanonicalBytes.Should().BePositive(); exact.Checksum.Length.Should().Be(32);
            exact.Checksum.Should().Equal(BaseSemanticActivationMaintenanceAuthorityContract.Checksum(request, exact));

            BaseSemanticActivationMaintenanceAuthorityRequest exactLimit = request with
            { MaximumBytes = exact.CanonicalBytes, RuntimeRequestChecksum = [] };
            exactLimit = exactLimit with { RuntimeRequestChecksum = BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(exactLimit) };
            (await store.InspectMaintenanceAuthorityAsync(exactLimit, default)).Should().BeOfType<BaseSuccess<BaseSemanticActivationMaintenanceAuthority>>();
            BaseSemanticActivationMaintenanceAuthorityRequest overBudget = exactLimit with
            { MaximumBytes = exact.CanonicalBytes - 1, RuntimeRequestChecksum = [] };
            overBudget = overBudget with { RuntimeRequestChecksum = BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(overBudget) };
            ((BaseFailure<BaseSemanticActivationMaintenanceAuthority>)await store.InspectMaintenanceAuthorityAsync(overBudget, default))
                .Error.Code.Should().Be(BaseSemanticActivationErrorCodes.BudgetExceeded);
            ((BaseFailure<BaseSemanticActivationMaintenanceAuthority>)await store.InspectMaintenanceAuthorityAsync(
                request with { RestoreEpoch = checked(request.RestoreEpoch + 1) }, default))
                .Error.Code.Should().Be(BaseSemanticActivationErrorCodes.Invalid);
            ((BaseFailure<BaseSemanticActivationMaintenanceAuthority>)await store.InspectMaintenanceAuthorityAsync(request with
                { RuntimeRequestChecksum = SHA256.HashData("substituted"u8).ToImmutableArray() }, default))
                .Error.Code.Should().Be(BaseSemanticActivationErrorCodes.Invalid);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync(); await using SqliteCommand corrupt = connection.CreateCommand();
                corrupt.CommandText = "UPDATE hpd_base_semantic_activation_slots SET authority_json=randomblob(length(authority_json));";
                (await corrupt.ExecuteNonQueryAsync()).Should().Be(1);
            }
            ((BaseFailure<BaseSemanticActivationMaintenanceAuthority>)await store.InspectMaintenanceAuthorityAsync(request, default))
                .Error.Code.Should().Be(BaseSemanticActivationErrorCodes.Corrupt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Certification_processors_commit_distinct_outer_receipts_for_one_sqlite_semantic_activation()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-semantic-certification-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            BaseSemanticActivationKeyDefinition definition =
                BaseSemanticActivationCertificationProcessor.InstalledDefinition(limits);
            await using SqliteRecordStore store = SemanticStore(path, installedDefinition: definition,
                definitionSetChecksum: BaseSemanticActivationCertificationProcessor.InstalledDefinitionSetChecksum);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "certification-application", [], limits)).Value!;
            var left = new BaseSemanticActivationCertificationProcessor(authority, limits, "module-store", "sqlite-parent-left");
            var right = new BaseSemanticActivationCertificationProcessor(authority, limits, "module-store", "sqlite-parent-right");

            RecordMutationExecutionResult first = await store.ExecuteAtomicAsync(left, ExecutionRequest("certification-left"));
            RecordMutationExecutionResult second = await store.ExecuteAtomicAsync(right, ExecutionRequest("certification-right"));

            first.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, left.FailureStage + ":" + first.Error?.Code);
            second.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, right.FailureStage + ":" + second.Error?.Code);
            first.Processing!.Receipt.ModuleMutation!.SemanticActivation!.EnsureDisposition
                .Should().Be(BaseSemanticActivationEnsureDisposition.Created);
            second.Processing!.Receipt.ModuleMutation!.SemanticActivation!.EnsureDisposition
                .Should().Be(BaseSemanticActivationEnsureDisposition.Existing);
            left.Provisional!.ActivationId.Should().Be(right.Provisional!.ActivationId);
            first.ReceiptAuthority!.ReceiptChecksum.Should().NotEqual(second.ReceiptAuthority!.ReceiptChecksum);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static RecordMutationExecutionRequest ExecutionRequest(string id)
    {
        byte[] fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes("semantic-certification:" + id));
        return ExecutionRequest() with
        {
            AtomicRequest = new BaseAtomicMutationExecutionRequest
            {
                Identity = BaseMutationRequestIdentity.Create("certification", "semantic.ensure", id,
                    BaseMutationRequestFingerprint.Create(fingerprint)),
                StructuralDigest = fingerprint, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                MaxReceiptBytes = 1_048_576,
            },
        };
    }

    [Fact]
    public async Task Scope_rotation_reprotects_semantic_directory_and_live_authority_without_changing_identity()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-semantic-scope-rotation-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteRecordStore store = SemanticStore(path, enableScopeRotationKey: true);
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
            var first = new SqliteSemanticEnsureProbe(authority, limits, "rotation-parent-a");
            (await store.ExecuteAtomicAsync(first, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            (BaseSemanticActivationScopeBinding priorBinding, BaseSemanticActivationLiveAuthority priorLive) = await ReadSemanticRotationAuthorityAsync(path);

            var request = new BaseSubjectAuthorityMaintenanceExecutionRequest
            {
                Lifecycle = new() { Kind = BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection, ExpectedDeliveryEpoch = 1, PlanChecksum = SHA256.HashData("semantic-rotation-plan"u8) },
                Identity = BaseMutationRequestIdentity.Create("control-plane", "rotate-subject-scope-protection", "semantic-rotation-1",
                    BaseMutationRequestFingerprint.Create(SHA256.HashData("semantic-rotation"u8))),
                CombinedPlanChecksum = new byte[32], ExpectedStoreGeneration = store.VectorSchemaGeneration,
                ExpectedSchemaGeneration = store.VectorSchemaGeneration, ExpectedRestoreEpoch = 0,
                ExpectedScopeProtectionGeneration = 1, ExpectedScopeProtectionKeyId = "31", ReplacementScopeProtectionKeyId = "32",
                ExpectedSemanticActivationAuthorityGeneration = 1,
                ExpectedSemanticActivationDefinitionSetChecksum = SemanticDefinition().Checksum,
                PageSize = 256, OperationTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5),
            };
            request = request with { CombinedPlanChecksum = BaseSubjectAuthorityMaintenanceProcessor.PlanChecksum(request) };
            var processor = new BaseSubjectAuthorityMaintenanceProcessor();
            RecordMutationExecutionResult rotated = await store.ExecuteMaintenanceAsync(processor, request);
            rotated.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, rotated.Error?.Code ?? rotated.Processing?.Error?.Code);

            (BaseSemanticActivationScopeBinding nextBinding, BaseSemanticActivationLiveAuthority nextLive) = await ReadSemanticRotationAuthorityAsync(path);
            nextBinding.BindingId.Should().Equal(priorBinding.BindingId);
            nextBinding.ProtectionKeyId.Should().Be("32");
            nextBinding.SeekDigest.Should().NotEqual(priorBinding.SeekDigest);
            nextBinding.Checksum.Should().NotEqual(priorBinding.Checksum);
            nextLive.ActivationId.Should().Be(priorLive.ActivationId);
            nextLive.KeyDigest.Should().Be(priorLive.KeyDigest);
            nextLive.ScopeBinding.BindingId.Should().Equal(priorLive.ScopeBinding.BindingId);
            nextLive.ScopeBinding.Checksum.Should().Equal(nextBinding.Checksum);
            nextLive.Checksum.Should().NotEqual(priorLive.Checksum);
            BaseSemanticActivationEvidenceContract.LiveChecksum(nextLive).Should().Equal(nextLive.Checksum);

            authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
            var duplicate = new SqliteSemanticEnsureProbe(authority, limits, "rotation-parent-b");
            (await store.ExecuteAtomicAsync(duplicate, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            duplicate.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Live);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static async Task<(BaseSemanticActivationScopeBinding Binding, BaseSemanticActivationLiveAuthority Live)> ReadSemanticRotationAuthorityAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT s.binding_json,l.authority_json FROM hpd_base_semantic_activation_scopes s JOIN hpd_base_semantic_activation_slots l ON l.binding_id=s.binding_id WHERE l.state=1;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (
            JsonSerializer.Deserialize((byte[])reader.GetValue(0), HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding)!,
            JsonSerializer.Deserialize((byte[])reader.GetValue(1), HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)!);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Scope_rotation_rebinds_retired_slot_and_recovery_floor_without_rewriting_historical_receipt(
        bool corruptHistoricalAuthority, bool corruptSlotRow)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-semantic-retired-rotation-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteRecordStore store = SemanticStore(path, enableScopeRotationKey: true);
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
            (await store.ExecuteAtomicAsync(new SqliteSemanticEnsureProbe(authority, limits, "retired-rotation-ensure"), ExecutionRequest()))
                .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            await CompleteSemanticActivationAsync(store, "retired-rotation-complete");
            var retire = new SqliteSemanticEnsureProbe(authority, limits, "retired-rotation-retire", retire: true);
            (await store.ExecuteAtomicAsync(retire, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, retire.RejectedCode);
            await InsertRetiredRecoveryFloorAsync(path, retire.RecoveryReceiptJson!);
            (BaseSemanticActivationRetirementAuthority priorSlot, BaseSemanticActivationRetirementAuthority priorFloor, byte[] priorReceipt) =
                await ReadRetiredRotationAuthorityAsync(path);
            if (corruptHistoricalAuthority)
            {
                await using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); await connection.OpenAsync();
                await using SqliteCommand corrupt = connection.CreateCommand();
                corrupt.CommandText = "UPDATE hpd_base_semantic_activation_recovery_floors SET receipt_slot_authority_json=randomblob(length(receipt_slot_authority_json));";
                (await corrupt.ExecuteNonQueryAsync()).Should().Be(1);
            }
            if (corruptSlotRow)
            {
                await using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); await connection.OpenAsync();
                await using SqliteCommand corrupt = connection.CreateCommand();
                corrupt.CommandText = "UPDATE hpd_base_semantic_activation_slots SET slot_generation=slot_generation+1 WHERE state=2;";
                (await corrupt.ExecuteNonQueryAsync()).Should().Be(1);
            }

            BaseSubjectAuthorityMaintenanceExecutionRequest request = SemanticRotationRequest(store, "semantic-retired-rotation");
            RecordMutationExecutionResult rotated = await store.ExecuteMaintenanceAsync(new BaseSubjectAuthorityMaintenanceProcessor(), request);
            if (corruptHistoricalAuthority || corruptSlotRow)
            {
                rotated.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
                return;
            }
            rotated.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, rotated.Error?.Code ?? rotated.Processing?.Error?.Code);

            (BaseSemanticActivationRetirementAuthority nextSlot, BaseSemanticActivationRetirementAuthority nextFloor, byte[] nextReceipt) =
                await ReadRetiredRotationAuthorityAsync(path);
            nextSlot.StoreAuthority.Requirement.SemanticAuthorityGeneration.Should().Be(2);
            nextFloor.StoreAuthority.Requirement.SemanticAuthorityGeneration.Should().Be(2);
            nextSlot.Checksum.Should().NotEqual(priorSlot.Checksum);
            nextFloor.Checksum.Should().NotEqual(priorFloor.Checksum);
            nextReceipt.Should().Equal(priorReceipt);
            var duplicate = new SqliteSemanticEnsureProbe(
                (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!,
                limits, "retired-rotation-duplicate", retire: true);
            (await store.ExecuteAtomicAsync(duplicate, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, duplicate.RejectedCode);
            duplicate.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Retired);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static async Task<(BaseSemanticActivationRetirementAuthority Slot, BaseSemanticActivationRetirementAuthority Floor, byte[] Receipt)>
        ReadRetiredRotationAuthorityAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT s.authority_json,f.authority_json,f.receipt_result_json FROM hpd_base_semantic_activation_slots s JOIN hpd_base_semantic_activation_recovery_floors f ON f.definition_id=s.definition_id AND f.binding_id=s.binding_id AND f.key_digest=s.key_digest WHERE s.state=2 AND f.state=2;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (
            JsonSerializer.Deserialize((byte[])reader.GetValue(0), HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)!,
            JsonSerializer.Deserialize((byte[])reader.GetValue(1), HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)!,
            (byte[])reader.GetValue(2));
    }

    private static async Task InsertRetiredRecoveryFloorAsync(string path, byte[] receipt)
    {
        const string scope = "semantic-rotation";
        const string operation = "retire";
        const string key = "retired-floor";
        byte[] fingerprint = SHA256.HashData("retired-floor-fingerprint"u8);
        byte[] structural = SHA256.HashData("retired-floor-structural"u8);
        byte[] receiptAuthority = BaseSemanticActivationEvidenceContract.RecoveryReceiptChecksum(
            scope, operation, key, fingerprint, structural, receipt).ToArray();
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO hpd_base_semantic_activation_recovery_floors(
 definition_id,binding_id,key_digest,state,slot_generation,authority_json,
 receipt_scope,receipt_operation,receipt_key,receipt_fingerprint,receipt_structural_digest,receipt_result_json,receipt_authority_checksum,receipt_slot_authority_json)
SELECT definition_id,binding_id,key_digest,state,slot_generation,authority_json,
 $scope,$operation,$key,$fingerprint,$structural,$receipt,$authority,authority_json
FROM hpd_base_semantic_activation_slots WHERE state=2;
""";
        command.Parameters.AddWithValue("$scope", scope); command.Parameters.AddWithValue("$operation", operation);
        command.Parameters.AddWithValue("$key", key); command.Parameters.Add("$fingerprint", SqliteType.Blob).Value = fingerprint;
        command.Parameters.Add("$structural", SqliteType.Blob).Value = structural; command.Parameters.Add("$receipt", SqliteType.Blob).Value = receipt;
        command.Parameters.Add("$authority", SqliteType.Blob).Value = receiptAuthority;
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scope_rotation_rebinds_compacted_absence_slot_and_floor(bool corruptSlotRow)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-semantic-absence-rotation-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteRecordStore store = SemanticStore(path, enableScopeRotationKey: true);
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
            (await store.ExecuteAtomicAsync(new SqliteSemanticEnsureProbe(authority, limits, "absence-rotation-ensure"), ExecutionRequest()))
                .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            await CompleteSemanticActivationAsync(store, "absence-rotation-complete");
            var retire = new SqliteSemanticEnsureProbe(authority, limits, "absence-rotation-retire", retire: true);
            (await store.ExecuteAtomicAsync(retire, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, retire.RejectedCode);
            await ConvertRetiredToAbsenceForRotationAsync(path);
            ImmutableArray<byte> prior = await ReadAbsenceChecksumAsync(path);
            if (corruptSlotRow)
            {
                await using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); await connection.OpenAsync();
                await using SqliteCommand corrupt = connection.CreateCommand();
                corrupt.CommandText = "UPDATE hpd_base_semantic_activation_slots SET slot_generation=slot_generation+1 WHERE state=3;";
                (await corrupt.ExecuteNonQueryAsync()).Should().Be(1);
            }

            RecordMutationExecutionResult rotated = await store.ExecuteMaintenanceAsync(
                new BaseSubjectAuthorityMaintenanceProcessor(), SemanticRotationRequest(store, "semantic-absence-rotation"));
            if (corruptSlotRow)
            {
                rotated.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
                return;
            }
            rotated.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, rotated.Error?.Code ?? rotated.Processing?.Error?.Code);
            ImmutableArray<byte> next = await ReadAbsenceChecksumAsync(path);
            next.Should().NotEqual(prior);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static async Task ConvertRetiredToAbsenceForRotationAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); await connection.OpenAsync();
        await using SqliteCommand read = connection.CreateCommand();
        read.CommandText = "SELECT authority_json FROM hpd_base_semantic_activation_slots WHERE state=2;";
        BaseSemanticActivationRetirementAuthority retired = JsonSerializer.Deserialize(
            (byte[])(await read.ExecuteScalarAsync())!, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)!;
        var absent = new BaseSemanticActivationAbsenceAuthority
        {
            Key = retired.KeyDigest, Definition = new BaseSemanticActivationDefinitionIdentity
            {
                Id = retired.Definition.Id, Version = retired.Definition.Version, Checksum = retired.Definition.Checksum,
                OwnerGeneration = 1, OwningModuleId = "test", RetirementOperation = new()
                {
                    OperationId = "semantic.retire", OperationVersion = 1,
                    OperationChecksum = Convert.ToHexStringLower(retired.CompletionOperationChecksum.ToArray()),
                },
            },
            ScopeBindingId = retired.SubjectLifetime?.ScopeBindingId ?? SHA256.HashData("runtime-proposed-binding:absence-rotation-ensure"u8).ToImmutableArray(),
            SubjectLifetime = retired.SubjectLifetime, FinalSlotGeneration = retired.SlotGeneration,
            AbsenceFloorGeneration = 1, RetirementPosition = retired.RetirementPosition,
            StoreAuthority = retired.StoreAuthority, Checksum = [],
        };
        absent = absent with { Checksum = BaseSemanticActivationEvidenceContract.AbsenceChecksum(absent) };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(absent, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority);
        await using SqliteCommand update = connection.CreateCommand();
        update.CommandText = """
UPDATE hpd_base_semantic_activation_slots SET state=3,authority_json=$authority WHERE state=2;
INSERT INTO hpd_base_semantic_activation_recovery_floors(definition_id,binding_id,key_digest,state,slot_generation,authority_json)
SELECT definition_id,binding_id,key_digest,3,slot_generation,$authority FROM hpd_base_semantic_activation_slots WHERE state=3;
""";
        update.Parameters.Add("$authority", SqliteType.Blob).Value = json;
        (await update.ExecuteNonQueryAsync()).Should().Be(2);
    }

    private static async Task<ImmutableArray<byte>> ReadAbsenceChecksumAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT authority_json FROM hpd_base_semantic_activation_slots WHERE state=3;";
        return JsonSerializer.Deserialize((byte[])(await command.ExecuteScalarAsync())!,
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)!.Checksum;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Semantic_scope_rotation_resumes_verified_staging_and_rejects_corruption(bool corruptStage)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-semantic-scope-resume-{Guid.NewGuid():N}.db");
        try
        {
            var interruption = new SemanticRotationInterruption();
            await using SqliteRecordStore store = SemanticStore(path, enableScopeRotationKey: true, administrationOperations: interruption);
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
            (await store.ExecuteAtomicAsync(new SqliteSemanticEnsureProbe(authority, limits, "resume-parent"), ExecutionRequest()))
                .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            BaseSemanticActivationScopeBinding prior = (await ReadSemanticRotationAuthorityAsync(path)).Binding;
            BaseSubjectAuthorityMaintenanceExecutionRequest request = SemanticRotationRequest(store, "semantic-rotation-resume");
            await FluentActions.Awaiting(async () => await store.ExecuteMaintenanceAsync(new BaseSubjectAuthorityMaintenanceProcessor(), request))
                .Should().ThrowAsync<IOException>();
            (await ReadSemanticRotationAuthorityAsync(path)).Binding.Checksum.Should().Equal(prior.Checksum);
            if (corruptStage)
            {
                await using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); await connection.OpenAsync();
                await using SqliteCommand corrupt = connection.CreateCommand();
                corrupt.CommandText = "UPDATE hpd_base_subject_lifecycle_scope_stage SET replacement_value=randomblob(length(replacement_value)) WHERE domain_ordinal=9;";
                (await corrupt.ExecuteNonQueryAsync()).Should().Be(1);
            }
            RecordMutationExecutionResult resumed = await store.ExecuteMaintenanceAsync(new BaseSubjectAuthorityMaintenanceProcessor(), request);
            if (corruptStage)
            {
                resumed.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
                resumed.Error?.Code.Should().Be(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                (await ReadSemanticRotationAuthorityAsync(path)).Binding.Checksum.Should().Equal(prior.Checksum);
            }
            else
            {
                resumed.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                (await ReadSemanticRotationAuthorityAsync(path)).Binding.ProtectionKeyId.Should().Be("32");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static BaseSubjectAuthorityMaintenanceExecutionRequest SemanticRotationRequest(SqliteRecordStore store, string idempotencyKey)
    {
        var request = new BaseSubjectAuthorityMaintenanceExecutionRequest
        {
            Lifecycle = new() { Kind = BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection, ExpectedDeliveryEpoch = 1, PlanChecksum = SHA256.HashData("semantic-rotation-plan"u8) },
            Identity = BaseMutationRequestIdentity.Create("control-plane", "rotate-subject-scope-protection", idempotencyKey,
                BaseMutationRequestFingerprint.Create(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))),
            CombinedPlanChecksum = new byte[32], ExpectedStoreGeneration = store.VectorSchemaGeneration,
            ExpectedSchemaGeneration = store.VectorSchemaGeneration, ExpectedRestoreEpoch = 0,
            ExpectedScopeProtectionGeneration = 1, ExpectedScopeProtectionKeyId = "31", ReplacementScopeProtectionKeyId = "32",
            ExpectedSemanticActivationAuthorityGeneration = 1,
            ExpectedSemanticActivationDefinitionSetChecksum = SemanticDefinition().Checksum,
            PageSize = 256, OperationTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5),
        };
        return request with { CombinedPlanChecksum = BaseSubjectAuthorityMaintenanceProcessor.PlanChecksum(request) };
    }

    private sealed class SemanticRotationInterruption : ISqliteAdministrationOperationController
    {
        private int _remaining = 1;
        public ValueTask BeforePhaseAsync(string phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (phase == "subjectLifecycleRotationAfterPage" && Interlocked.Exchange(ref _remaining, 0) == 1)
                throw new IOException("Injected semantic rotation interruption.");
            return ValueTask.CompletedTask;
        }
        public void DeleteFile(string path) => File.Delete(path);
    }

    [Theory]
    [InlineData("binding-checksum")]
    [InlineData("live-checksum")]
    [InlineData("store-generation")]
    [InlineData("store-application")]
    [InlineData("store-logical")]
    [InlineData("store-instance")]
    [InlineData("store-restore")]
    [InlineData("store-schema")]
    [InlineData("definition-version")]
    [InlineData("definition-checksum")]
    [InlineData("activation-input")]
    [InlineData("scope-row")]
    [InlineData("live-row")]
    public async Task Scope_rotation_rejects_corrupt_semantic_source_authority(string corruption)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-semantic-source-{corruption}-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteRecordStore store = SemanticStore(path, enableScopeRotationKey: true);
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
            (await store.ExecuteAtomicAsync(new SqliteSemanticEnsureProbe(authority, limits, "source-corruption"), ExecutionRequest()))
                .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            await CorruptSemanticRotationSourceAsync(path, corruption);
            RecordMutationExecutionResult result = await store.ExecuteMaintenanceAsync(
                new BaseSubjectAuthorityMaintenanceProcessor(), SemanticRotationRequest(store, "source-corruption-" + corruption));
            result.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
            result.Error?.Code.Should().Be(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static async Task CorruptSemanticRotationSourceAsync(string path, string corruption)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); await connection.OpenAsync();
        if (corruption == "activation-input")
        {
            await using SqliteCommand activation = connection.CreateCommand();
            activation.CommandText = "UPDATE hpd_base_activations SET input_checksum=randomblob(32);";
            (await activation.ExecuteNonQueryAsync()).Should().Be(1); return;
        }
        if (corruption == "scope-row" || corruption == "live-row")
        {
            await using SqliteCommand relational = connection.CreateCommand();
            relational.CommandText = corruption == "scope-row"
                ? "UPDATE hpd_base_semantic_activation_scopes SET scope_kind=1;"
                : "UPDATE hpd_base_semantic_activation_slots SET slot_generation=slot_generation+1 WHERE state=1;";
            (await relational.ExecuteNonQueryAsync()).Should().Be(1); return;
        }
        await using SqliteCommand read = connection.CreateCommand();
        read.CommandText = "SELECT s.binding_json,l.authority_json FROM hpd_base_semantic_activation_scopes s JOIN hpd_base_semantic_activation_slots l ON l.binding_id=s.binding_id WHERE l.state=1;";
        await using SqliteDataReader reader = await read.ExecuteReaderAsync(); (await reader.ReadAsync()).Should().BeTrue();
        BaseSemanticActivationScopeBinding binding = JsonSerializer.Deserialize((byte[])reader[0], HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding)!;
        BaseSemanticActivationLiveAuthority live = JsonSerializer.Deserialize((byte[])reader[1], HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)!;
        await reader.DisposeAsync();
        if (corruption == "binding-checksum")
            binding = binding with { Checksum = Enumerable.Repeat((byte)0x91, 32).ToImmutableArray() };
        else if (corruption == "live-checksum")
            live = live with { Checksum = Enumerable.Repeat((byte)0x92, 32).ToImmutableArray() };
        else if (corruption.StartsWith("store-", StringComparison.Ordinal))
        {
            BaseSemanticActivationStoreAuthorityRequirement requirement = live.StoreAuthority.Requirement;
            requirement = corruption switch
            {
                "store-generation" => requirement with { SemanticAuthorityGeneration = 2 },
                "store-application" => requirement with { ApplicationId = "substituted.application" },
                "store-logical" => requirement with { LogicalStoreId = "substituted.logical" },
                "store-instance" => requirement with { StoreInstanceId = "substituted.instance" },
                "store-restore" => requirement with { RestoreEpoch = checked(requirement.RestoreEpoch + 1) },
                "store-schema" => requirement with { SchemaGeneration = checked(requirement.SchemaGeneration + 1) },
                _ => throw new InvalidOperationException(),
            };
            BaseSemanticActivationStoreAuthority storeAuthority = BaseSemanticActivationEvidenceContract.CreateStoreAuthority(
                requirement);
            live = live with { StoreAuthority = storeAuthority, Checksum = [] };
            live = live with { Checksum = BaseSemanticActivationEvidenceContract.LiveChecksum(live) };
        }
        else
        {
            BaseSemanticActivationDefinitionIdentity definition = corruption == "definition-version"
                ? live.Definition with { Version = checked(live.Definition.Version + 1) }
                : live.Definition with { Checksum = SHA256.HashData("substituted-semantic-definition"u8).ToImmutableArray() };
            live = live with { Definition = definition, Checksum = [] };
            live = live with { Checksum = BaseSemanticActivationEvidenceContract.LiveChecksum(live) };
        }
        await using SqliteCommand update = connection.CreateCommand();
        update.CommandText = "UPDATE hpd_base_semantic_activation_scopes SET binding_json=$binding; UPDATE hpd_base_semantic_activation_slots SET authority_json=$live WHERE state=1;";
        update.Parameters.Add("$binding", SqliteType.Blob).Value = JsonSerializer.SerializeToUtf8Bytes(binding, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding);
        update.Parameters.Add("$live", SqliteType.Blob).Value = JsonSerializer.SerializeToUtf8Bytes(live, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority);
        (await update.ExecuteNonQueryAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Semantic_runtime_finalizes_null_due_across_different_parents_and_restart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-runtime-{Guid.NewGuid():N}.db");
        try
        {
            BaseRegisteredModuleMutationDefinition module = Definition();
            var moduleIdentity = new BaseGeneratedModuleMutationIdentity<Request, Result>(
                module.Id, module.Version, module.Checksum.ToArray(),
                SqliteActivationDtos.HPDBaseActivationDtoAuthority.InputTypeInfo,
                SqliteActivationDtos.HPDBaseActivationDtoAuthority.ResultTypeInfo,
                SqliteActivationDtos.HPDBaseActivationDtoAuthority.InputBindings.Values.ToArray(),
                SqliteActivationDtos.HPDBaseActivationDtoAuthority.ResultBindings.Values.ToArray());
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
            second.RejectedCode.Should().Contain(BaseSemanticActivationErrorCodes.BudgetExceeded);
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
            retire.RejectedCode.Should().Contain(BaseSemanticActivationErrorCodes.BudgetExceeded);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    private static async Task CompleteSemanticActivationAsync(
        SqliteRecordStore store, string identity, long observeAt = 10)
    {
        BaseActivationExecutionLimits limits = ActivationLimits();
        BaseActivationDueObservation observed = (await store.ObserveDueAsync(new()
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [ActivationDefinition()], Scope = ActivationScope(),
            AcceptedTime = AcceptedTime(observeAt), MaximumCandidates = 8, Limits = limits,
        })).Value!;
        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker",
            Definitions = [ActivationDefinition()], Scope = ActivationScope(), Checksum = new byte[32].ToImmutableArray(),
        };
        var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new()
        {
            Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(observeAt), LeaseMilliseconds = 1_000,
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

    private static async Task YieldSemanticActivationAsync(SqliteRecordStore store, string identity)
    {
        BaseActivationExecutionLimits limits = ActivationLimits();
        BaseActivationDefinitionKey definition = ActivationDefinition();
        BaseActivationDueObservation observed = (await store.ObserveDueAsync(new()
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [definition],
            Scope = ActivationScope(), AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = limits,
        })).Value!;
        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "yield-worker",
            Definitions = [definition], Scope = ActivationScope(), Checksum = new byte[32].ToImmutableArray(),
        };
        var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new()
        {
            Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(10),
            LeaseMilliseconds = 1_000, Identity = ActivationIdentity(identity + "-claim"), Limits = limits,
        })).Value!;
        OperationResult<BaseActivationTransitionResult> yielded = await store.TransitionAsync(new BaseActivationYieldRequest
        {
            ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim,
            RequestedResumeAt = DateTimeOffset.FromUnixTimeMilliseconds(12), EffectiveDueAt = 12,
            ProgressFingerprint = SHA256.HashData("restore-progress"u8).ToImmutableArray(),
            ExpectedYieldCount = 0, MaximumYields = 2, AcceptedTime = AcceptedTime(11),
            Identity = ActivationIdentity(identity), Limits = limits,
        });
        yielded.IsSuccess().Should().BeTrue(yielded.Error?.Code);
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

    private static async Task DisposeSemanticActivationAsync(
        SqliteRecordStore store, string identity, long observeAt = 10)
    {
        BaseActivationExecutionLimits limits = ActivationLimits();
        BaseActivationDueObservation observed = (await store.ObserveDueAsync(new()
        {
            ApplicationId = "activation-test", WorkerModuleId = "test", Definitions = [ActivationDefinition()], Scope = ActivationScope(),
            AcceptedTime = AcceptedTime(observeAt), MaximumCandidates = 8, Limits = limits,
        })).Value!;
        var worker = new BaseActivationWorkerAuthority
        {
            ApplicationId = "activation-test", ModuleId = "test", WorkerIdentity = "worker",
            Definitions = [ActivationDefinition()], Scope = ActivationScope(), Checksum = new byte[32].ToImmutableArray(),
        };
        var claimed = (BaseActivationClaimedResult)(await store.TryClaimNextAsync(new()
        {
            Observation = observed.Token, Worker = worker, AcceptedTime = AcceptedTime(observeAt), LeaseMilliseconds = 1_000,
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

    [Theory]
    [InlineData("hpd_base_semantic_activation_scopes", "AUTOINCREMENT", "")]
    [InlineData("hpd_base_semantic_activation_scopes", ",\n  UNIQUE(scope_kind,seek_digest)", "")]
    [InlineData("hpd_base_semantic_activation_slots", ",\n  UNIQUE(definition_id,binding_id,key_digest)", "")]
    [InlineData("hpd_base_semantic_activation_recovery_floors", "AUTOINCREMENT", "")]
    public async Task Semantic_schema_rejects_missing_rotation_and_logical_uniqueness_authority(
        string table, string oldText, string newText)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-schema-authority-{Guid.NewGuid():N}.db");
        try
        {
            await using (SqliteRecordStore initialized = SemanticStore(path)) { }
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand read = connection.CreateCommand();
                read.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$table;";
                read.Parameters.AddWithValue("$table", table);
                string sql = (string)(await read.ExecuteScalarAsync())!;
                string replacement = sql.Replace(oldText, newText, StringComparison.OrdinalIgnoreCase);
                replacement.Should().NotBe(sql);
                await using SqliteCommand rewrite = connection.CreateCommand();
                rewrite.CommandText = $"DROP TABLE {table}; {replacement};";
                await rewrite.ExecuteNonQueryAsync();
            }
            Action reopen = () => _ = SemanticStore(path);
            reopen.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Theory]
    [InlineData("hpd_base_semantic_activation_scopes", ", CHECK(scope_kind>=0)")]
    [InlineData("hpd_base_semantic_activation_slots", ", UNIQUE(definition_id)")]
    [InlineData("hpd_base_semantic_activation_recovery_floors", ", CHECK(slot_generation<9223372036854775807)")]
    public async Task Semantic_schema_rejects_additional_table_authority(string table, string additionalConstraint)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-extra-schema-authority-{Guid.NewGuid():N}.db");
        try
        {
            await using (SqliteRecordStore initialized = SemanticStore(path)) { }
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand read = connection.CreateCommand();
                read.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$table;";
                read.Parameters.AddWithValue("$table", table);
                string sql = (string)(await read.ExecuteScalarAsync())!;
                int closing = sql.LastIndexOf(')');
                closing.Should().BePositive();
                string replacement = sql.Insert(closing, additionalConstraint);
                await using SqliteCommand rewrite = connection.CreateCommand();
                rewrite.CommandText = $"DROP TABLE {table}; {replacement};";
                await rewrite.ExecuteNonQueryAsync();
            }
            Action reopen = () => _ = SemanticStore(path);
            reopen.Should().Throw<InvalidOperationException>().WithMessage($"*table-sql:{table}*");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
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
                command.CommandText = "UPDATE hpd_base_activations SET terminal_receipt_checksum=$checksum;";
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
            BaseSemanticActivationKeyDefinition unrelatedDefinition = sourceDefinition with
            {
                Id = "test.semantic.unrelated",
                Checksum = SHA256.HashData("semantic-definition-unrelated"u8).ToImmutableArray(),
            };
            ImmutableArray<byte> initialSet = SHA256.HashData("semantic-set-initial"u8).ToImmutableArray();
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using (SqliteRecordStore initial = SemanticStore(path, installedDefinition: sourceDefinition,
                additionalDefinitions: [unrelatedDefinition], definitionSetChecksum: initialSet))
            {
                BaseAtomicMutationAuthorityRequirement authority = (await initial.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
                (await initial.ExecuteAtomicAsync(new SqliteSemanticEnsureProbe(authority, limits, "migration-parent"), ExecutionRequest()))
                    .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                (await initial.ExecuteAtomicAsync(new SqliteSemanticEnsureProbe(authority, limits, "migration-parent-2", "auth-user-43"), ExecutionRequest()))
                    .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                (await initial.ExecuteAtomicAsync(new SqliteSemanticEnsureProbe(authority, limits, "migration-parent-3", "auth-user-44"), ExecutionRequest()))
                    .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                (await initial.ExecuteAtomicAsync(new SqliteSemanticEnsureProbe(authority, limits, "migration-unrelated",
                    "auth-user-unrelated", semanticDefinition: unrelatedDefinition), ExecutionRequest()))
                    .Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                await DisposeSemanticActivationAsync(initial, "migration-terminal");
                authority = (await initial.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
                var migrationRetire = new SqliteSemanticEnsureProbe(authority, limits, "migration-retire", retire: true);
                (await initial.ExecuteAtomicAsync(migrationRetire, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                await InsertRetiredRecoveryFloorAsync(path, migrationRetire.RecoveryReceiptJson!);
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
            ImmutableArray<byte> resultingSet = SHA256.HashData("semantic-set-resulting"u8).ToImmutableArray();
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create("semantic-maintenance", "migrate", "migration-1",
                BaseMutationRequestFingerprint.Create(SHA256.HashData("migration-request"u8)));
            BaseSemanticActivationMigrateRequest request = new()
            {
                Identity = identity, Definition = migration.From, ExpectedSemanticAuthorityGeneration = 1,
                Migration = migration, Limits = new() { PageSize = 1, MaximumPages = 20, MaximumRows = 100,
                    MaximumBytes = 8_000_000, Deadline = TimeSpan.FromSeconds(5) },
            };
            await using (SqliteRecordStore first = SemanticStore(path, installedDefinition: to, ownerGeneration: 2,
                definitionSetChecksum: resultingSet, migrations: [migration], additionalDefinitions: [unrelatedDefinition]))
            {
                BaseResult<BaseSemanticActivationMaintenanceResult> progress = await first.ExecuteAsync(request, default);
                progress.RequireValue().Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.InProgress);
                progress.RequireValue().Checkpoint!.CompletedRows.Should().Be(1);
            }
            await using (SqliteRecordStore resumed = SemanticStore(path, installedDefinition: to, ownerGeneration: 2,
                definitionSetChecksum: resultingSet, migrations: [migration], additionalDefinitions: [unrelatedDefinition]))
            {
                BaseResult<BaseSemanticActivationMaintenanceResult> completed = await resumed.ExecuteAsync(request, default);
                completed.RequireValue().Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
                completed.RequireValue().ResultingAuthorityGeneration.Should().Be(2);
                completed.RequireValue().ChangedRows.Should().Be(6);
                (await resumed.ExecuteAsync(request, default)).RequireValue().Disposition
                    .Should().Be(BaseSemanticActivationMaintenanceDisposition.Duplicate);
                BaseAtomicMutationAuthorityRequirement authority = (await resumed.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
                authority.SemanticActivation!.SemanticAuthorityGeneration.Should().Be(2);
                authority.SemanticActivation.DefinitionSetChecksum.Should().Equal(resultingSet);
                var postMigration = new SqliteSemanticEnsureProbe(authority, limits, "migration-parent-after",
                    "auth-user-43", semanticDefinition: to, semanticOwnerGeneration: 2, acceptedTimeSeconds: 40);
                (await resumed.ExecuteAtomicAsync(postMigration, ExecutionRequest())).Outcome
                    .Should().Be(RecordMutationExecutionOutcome.Committed, postMigration.RejectedCode);
                postMigration.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Live);
                authority = (await resumed.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
                var retiredAfter = new SqliteSemanticEnsureProbe(authority, limits, "migration-retired-after",
                    semanticDefinition: to, semanticOwnerGeneration: 2, acceptedTimeSeconds: 40);
                (await resumed.ExecuteAtomicAsync(retiredAfter, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, retiredAfter.RejectedCode);
                retiredAfter.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Retired);
                retiredAfter.CapturedEvidence!.DefinitionMigrationChain.Should().ContainSingle();
                postMigration.Provisional!.PriorState.Should().Be(BaseSemanticActivationCapturedState.Live);
                var unrelatedAfter = new SqliteSemanticEnsureProbe(authority, limits, "migration-unrelated-after",
                    "auth-user-unrelated", semanticDefinition: unrelatedDefinition, semanticOwnerGeneration: 2, acceptedTimeSeconds: 40);
                (await resumed.ExecuteAtomicAsync(unrelatedAfter, ExecutionRequest())).Outcome
                    .Should().Be(RecordMutationExecutionOutcome.Committed);
                unrelatedAfter.CapturedState.Should().Be(BaseSemanticActivationCapturedState.Live);
            }
            await using (var tamper = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await tamper.OpenAsync(); await using var command = tamper.CreateCommand();
                command.CommandText = "UPDATE hpd_base_semantic_activation_migration_history SET binding_id=randomblob(32) WHERE migration_id=(SELECT migration_id FROM hpd_base_semantic_activation_migration_history LIMIT 1) AND migration_version=(SELECT migration_version FROM hpd_base_semantic_activation_migration_history LIMIT 1) AND binding_id=(SELECT binding_id FROM hpd_base_semantic_activation_migration_history LIMIT 1) AND key_digest=(SELECT key_digest FROM hpd_base_semantic_activation_migration_history LIMIT 1);";
                (await command.ExecuteNonQueryAsync()).Should().Be(1);
            }
            Func<Task> reopenCorrupt = async () =>
            {
                await using SqliteRecordStore ignored = SemanticStore(path, installedDefinition: to, ownerGeneration: 2,
                    definitionSetChecksum: resultingSet, migrations: [migration], additionalDefinitions: [unrelatedDefinition]);
            };
            await reopenCorrupt.Should().ThrowAsync<InvalidOperationException>();
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_removal_is_graph_admitted_and_retains_nonprunable_definition_authority()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-remove-{Guid.NewGuid():N}.db");
        try
        {
            BaseSemanticActivationKeyDefinition removed = SemanticDefinition();
            BaseSemanticActivationKeyDefinition retained = removed with
            {
                Id = "test.semantic.retained", Checksum = SHA256.HashData("semantic-definition-retained"u8).ToImmutableArray(),
            };
            ImmutableArray<byte> initialSet = SHA256.HashData("semantic-remove-initial"u8).ToImmutableArray();
            ImmutableArray<byte> resultingSet = SHA256.HashData("semantic-remove-result"u8).ToImmutableArray();
            BaseSemanticActivationRemovalAuthority removal = BaseSemanticActivationRemovalAuthorityContract.Seal(new()
            {
                Id = "test.semantic.remove", Version = 1, From = removed,
                ResultingDefinitionSetChecksum = resultingSet, Checksum = [],
            });
            removed = removal.From;
            var artifact = new MemoryStream(); BaseBackupManifest manifest;
            byte[] expectedState; byte[] expectedAbsence; string artifactActivationId;
            await using (SqliteRecordStore initial = SemanticStore(path, installedDefinition: removed,
                additionalDefinitions: [retained], definitionSetChecksum: initialSet, administrationEnabled: true))
            {
                BaseAtomicMutationExecutionLimits execution = ExecutionLimits();
                BaseAtomicMutationAuthorityRequirement authority = (await initial.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], execution)).Value!;
                var artifactEnsure = new SqliteSemanticEnsureProbe(authority, execution, "removal-live", semanticDefinition: removed);
                (await initial.ExecuteAtomicAsync(artifactEnsure, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                artifactActivationId = artifactEnsure.Provisional!.ActivationId!;
                OperationResult<BaseBackupManifest> created = await initial.CreateBackupAsync(artifact, new BaseBackupRequest
                    { StoreId = "module-store", Principal = AdministrationPrincipal() });
                created.IsSuccess().Should().BeTrue(created.Error?.Code); manifest = created.Value!;
                await using var prepare = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
                await prepare.OpenAsync(); byte[] binding; byte[] key; long generation; BaseSemanticActivationLiveAuthority live;
                await using (var read = prepare.CreateCommand())
                {
                    read.CommandText = "SELECT binding_id,key_digest,slot_generation,authority_json FROM hpd_base_semantic_activation_slots WHERE definition_id=$id;";
                    read.Parameters.AddWithValue("$id", removed.Id); await using var row = await read.ExecuteReaderAsync();
                    (await row.ReadAsync()).Should().BeTrue(); binding = (byte[])row[0]; key = (byte[])row[1]; generation = row.GetInt64(2);
                    live = JsonSerializer.Deserialize((byte[])row[3], HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)!;
                }
                var absent = new BaseSemanticActivationAbsenceAuthority
                {
                    Key = live.KeyDigest, Definition = live.Definition, ScopeBindingId = binding.ToImmutableArray(), SubjectLifetime = live.SubjectLifetime,
                    FinalSlotGeneration = checked(generation + 1), AbsenceFloorGeneration = 1, RetirementPosition = 1,
                    StoreAuthority = live.StoreAuthority, Checksum = [],
                };
                absent = absent with { Checksum = BaseSemanticActivationEvidenceContract.AbsenceChecksum(absent) };
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(absent, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority);
                await using (var write = prepare.CreateCommand())
                {
                    write.CommandText = "UPDATE hpd_base_semantic_activation_slots SET state=3,slot_generation=$generation,activation_id=NULL,authority_json=$authority WHERE definition_id=$id; INSERT INTO hpd_base_semantic_activation_recovery_floors(definition_id,binding_id,key_digest,state,slot_generation,authority_json) VALUES($id,$binding,$key,3,$generation,$authority);";
                    write.Parameters.AddWithValue("$generation", absent.FinalSlotGeneration); write.Parameters.AddWithValue("$id", removed.Id);
                    write.Parameters.Add("$binding", Microsoft.Data.Sqlite.SqliteType.Blob).Value = binding; write.Parameters.Add("$key", Microsoft.Data.Sqlite.SqliteType.Blob).Value = key;
                    write.Parameters.Add("$authority", Microsoft.Data.Sqlite.SqliteType.Blob).Value = json; (await write.ExecuteNonQueryAsync()).Should().Be(2);
                }
                expectedState = SemanticDefinitionStateChecksum(binding, key, 3, absent.FinalSlotGeneration, json);
                expectedAbsence = OrderedSemanticAuthoritiesChecksum([SemanticHistoricalNegativeRow(binding, key, 3, json)]);
            }
            await using SqliteRecordStore replacement = SemanticStore(path, installedDefinition: retained,
                ownerGeneration: 2, definitionSetChecksum: resultingSet, removals: [removal], administrationEnabled: true);
            BaseSemanticActivationRemoveRequest request = new()
            {
                Identity = BaseMutationRequestIdentity.Create("semantic-maintenance", "remove", "remove-1",
                    BaseMutationRequestFingerprint.Create(SHA256.HashData("semantic-remove-request"u8))),
                Definition = new() { Id = removed.Id, Version = removed.Version, Checksum = removed.Checksum },
                RemovalAuthority = removal, ExpectedSemanticAuthorityGeneration = 1,
                ExpectedLiveCount = 0, ExpectedRetiredCount = 0, ExpectedAbsenceCount = 1,
                ExpectedDefinitionStateChecksum = expectedState.ToImmutableArray(),
                ExpectedAbsenceAuthorityChecksum = expectedAbsence.ToImmutableArray(),
                Limits = new() { PageSize = 1, MaximumPages = 1, MaximumRows = 1,
                    MaximumBytes = 1_000_000, Deadline = TimeSpan.FromSeconds(5) },
            };
            BaseSemanticActivationMaintenanceResult completed = (await replacement.ExecuteAsync(request, default)).RequireValue();
            completed.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
            (await replacement.ExecuteAsync(request, default)).RequireValue().Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Duplicate);
            artifact.Position = 0;
            OperationResult<BaseRestoreResult> removalRestore = await replacement.RestoreAsync(artifact, new BaseRestoreRequest
            {
                StoreId = "module-store", Principal = AdministrationPrincipal(),
                ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true, ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
            });
            removalRestore.IsSuccess().Should().BeTrue($"{removalRestore.Error?.Code}:{removalRestore.Error?.Message}:{removalRestore.Error?.Detail}");
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync(); await using var command = connection.CreateCommand();
            command.CommandText = "SELECT d.execution_enabled,r.removal_id,length(r.authority_checksum) FROM hpd_base_semantic_activation_definitions d JOIN hpd_base_semantic_activation_removed_definitions r USING(definition_id,definition_version) WHERE d.definition_id=$id;";
            command.Parameters.AddWithValue("$id", removed.Id); await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue(); reader.GetInt32(0).Should().Be(0);
            reader.GetString(1).Should().Be(removal.Id); reader.GetInt32(2).Should().Be(32);
            await reader.DisposeAsync();
            command.CommandText = "SELECT COUNT(*) FROM hpd_base_activations WHERE activation_id=$activation;";
            command.Parameters.AddWithValue("$activation", artifactActivationId);
            Convert.ToInt64(await command.ExecuteScalarAsync()).Should().Be(0);
            await using SqliteRecordStore restarted = SemanticStore(path, installedDefinition: retained,
                ownerGeneration: 2, definitionSetChecksum: resultingSet, removals: [removal]);
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Semantic_compaction_consumes_lifecycle_and_activation_floors_and_survives_restart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-compact-{Guid.NewGuid():N}.db");
        byte[] contractChecksum = SHA256.HashData("subject-contract"u8);
        BaseSemanticActivationKeyDefinition definition = SemanticDefinition() with
        {
            Compaction = new BaseSemanticActivationSubjectRetirementCompaction(
                new BaseSemanticActivationSubjectContractIdentity(
                    "test.subject", 1, contractChecksum.ToImmutableArray()),
                "request.subject", "subject.retire"),
            Checksum = SHA256.HashData("semantic-definition-compaction"u8).ToImmutableArray(),
        };
        var lifetime = new BaseSemanticActivationSubjectLifetimeBinding
        {
            ContractId = "test.subject", ContractVersion = 1, ContractChecksum = contractChecksum.ToImmutableArray(),
            SubjectId = BaseSubjectId.Create("subject-42", BaseSubjectIdKind.OrdinalString),
            AuthorityEpoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)0x21, 16).ToArray()),
            Incarnation = new BaseSubjectIncarnation(SemanticIncarnation(1)), ScopeBindingId = [], Checksum = [],
        };
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using (SqliteRecordStore store = SemanticStore(path, installedDefinition: definition))
            {
                BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                    "activation-test", [], limits)).Value!;
                var ensure = new SqliteSemanticEnsureProbe(authority, limits, "compact-ensure",
                    semanticDefinition: definition, subjectLifetime: lifetime);
                (await store.ExecuteAtomicAsync(ensure, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                await DisposeSemanticActivationAsync(store, "compact-terminal");
                authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
                var retire = new SqliteSemanticEnsureProbe(authority, limits, "compact-retire", retire: true,
                    semanticDefinition: definition, subjectLifetime: lifetime);
                RecordMutationExecutionResult retirement = await store.ExecuteAtomicAsync(retire, ExecutionRequest());
                retirement.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed,
                    $"{retire.RejectedCode}; captured={retire.CapturedState}; lifetime={retire.CapturedEvidence?.Live?.SubjectLifetime is not null}; " +
                    $"lifetimeValid={retire.CapturedEvidence?.Live?.SubjectLifetime is { } capturedLifetime && BaseSemanticActivationEvidenceContract.SubjectLifetimeChecksum(capturedLifetime).AsSpan().SequenceEqual(capturedLifetime.Checksum.AsSpan())}");
                await InsertRetiredRecoveryFloorAsync(path, retire.RecoveryReceiptJson!);
                OperationResult<BaseActivationPrunePage> pruned = await store.PruneAsync(new BaseActivationPruneRequest
                {
                    ApplicationId = "activation-test", Scope = ActivationScope(), Definition = ActivationDefinition(), Take = 1,
                    AcceptedTime = AcceptedTime(86_400_030), Identity = ActivationIdentity("compact-prune"), Limits = ActivationLimits(),
                });
                pruned.IsSuccess().Should().BeTrue(pruned.Error?.Code);

                BaseSemanticActivationRetirementAuthority retired = (await ReadRetiredRotationAuthorityAsync(path)).Slot;
                await using (var dependency = new SqliteConnection($"Data Source={path};Pooling=False"))
                {
                    await dependency.OpenAsync(); await using SqliteCommand verify = dependency.CreateCommand();
                    verify.CommandText = "SELECT terminal_generation,terminal_control_checksum,terminal_receipt_checksum FROM hpd_base_activation_prune_floors WHERE activation_id=$id;";
                    verify.Parameters.AddWithValue("$id", retired.ActivationId); await using SqliteDataReader evidence = await verify.ExecuteReaderAsync();
                    (await evidence.ReadAsync()).Should().BeTrue(); evidence.GetInt64(0).Should().Be(retired.TerminalActivationGeneration);
                    ((byte[])evidence[1]).Should().Equal(retired.TerminalActivationChecksum);
                    ((byte[])evidence[2]).Should().Equal(retired.CompletionReceiptChecksum);
                }
                await InsertTerminalSubjectLifetimeAsync(path, retired);
                byte[] ordered = OrderedSemanticAuthoritiesChecksum([JsonSerializer.SerializeToUtf8Bytes(retired,
                    HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)]);
                BaseSemanticActivationCompactRequest request = new()
                {
                    Identity = BaseMutationRequestIdentity.Create("semantic-maintenance", "compact", "compact-1",
                        BaseMutationRequestFingerprint.Create(SHA256.HashData("compact-request"u8))),
                    Definition = new() { Id = definition.Id, Version = definition.Version, Checksum = definition.Checksum },
                    ExpectedSemanticAuthorityGeneration = 1, ExpectedRetiredCount = 1,
                    ExpectedRetiredChecksum = ordered.ToImmutableArray(),
                    Limits = new() { PageSize = 1, MaximumPages = 2, MaximumRows = 2,
                        MaximumBytes = 1_000_000, Deadline = TimeSpan.FromSeconds(5) },
                };
                BaseResult<BaseSemanticActivationMaintenanceResult> compactResult = await store.ExecuteAsync(request, default);
                compactResult.Should().BeOfType<BaseSuccess<BaseSemanticActivationMaintenanceResult>>(
                    compactResult is BaseFailure<BaseSemanticActivationMaintenanceResult> failed ? $"{failed.Error.Code}:{failed.Error.Message}" : string.Empty);
                BaseSemanticActivationMaintenanceResult compacted = ((BaseSuccess<BaseSemanticActivationMaintenanceResult>)compactResult).Value;
                compacted.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
                compacted.ResultingAuthorityGeneration.Should().Be(2);
            }
            await using (SqliteRecordStore reopened = SemanticStore(path, installedDefinition: definition))
            {
                BaseAtomicMutationAuthorityRequirement authority = (await reopened.CaptureAtomicMutationAuthorityRequirementAsync(
                    "activation-test", [], ExecutionLimits())).Value!;
                var duplicate = new SqliteSemanticEnsureProbe(authority, ExecutionLimits(), "compact-duplicate", retire: true,
                    semanticDefinition: definition, subjectLifetime: lifetime, acceptedTimeSeconds: 86_400_031);
                RecordMutationExecutionResult duplicateResult = await reopened.ExecuteAtomicAsync(duplicate, ExecutionRequest());
                duplicateResult.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed,
                    $"{duplicate.RejectedCode}:{duplicateResult.Error?.Code}:{duplicateResult.Error?.Message}");
                duplicate.CapturedState.Should().Be(BaseSemanticActivationCapturedState.CompactedAbsent);
            }
            await using var check = new SqliteConnection($"Data Source={path};Pooling=False"); await check.OpenAsync();
            await using SqliteCommand command = check.CreateCommand();
            command.CommandText = "SELECT s.state,f.state,f.receipt_slot_authority_json FROM hpd_base_semantic_activation_slots s JOIN hpd_base_semantic_activation_recovery_floors f USING(definition_id,binding_id,key_digest);";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(); (await reader.ReadAsync()).Should().BeTrue();
            reader.GetInt32(0).Should().Be(3); reader.GetInt32(1).Should().Be(3); reader.IsDBNull(2).Should().BeTrue();
        }
        finally { SqliteConnection.ClearAllPools(); foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }

    private static async Task InsertTerminalSubjectLifetimeAsync(string path, BaseSemanticActivationRetirementAuthority retired)
    {
        BaseSemanticActivationSubjectLifetimeBinding lifetime = retired.SubjectLifetime!;
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); await connection.OpenAsync();
        await using SqliteCommand bindingCommand = connection.CreateCommand();
        bindingCommand.CommandText = "SELECT binding_json FROM hpd_base_semantic_activation_scopes WHERE binding_id=$binding;";
        bindingCommand.Parameters.Add("$binding", SqliteType.Blob).Value = lifetime.ScopeBindingId.ToArray();
        await using SqliteDataReader binding = await bindingCommand.ExecuteReaderAsync(); (await binding.ReadAsync()).Should().BeTrue();
        BaseSemanticActivationScopeBinding scope = JsonSerializer.Deserialize((byte[])binding[0],
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding)!;
        int kind = (int)scope.Kind; byte[] seek = scope.SeekDigest.ToArray();
        byte[] protectedScope = scope.ProtectedCanonicalScope.ToArray(); await binding.DisposeAsync();
        await using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO hpd_base_subject_terminal_lifetimes(contract_id,contract_version,subject_id,scope_kind,scope_index_digest,protected_scope_value,retired_authority_epoch,retired_incarnation,retired_lifetime_generation,retired_subject_sequence,retired_position,contract_state_generation,restore_epoch,receipt_checksum) VALUES($contract,$version,$subject,$kind,$scope,$protected,$epoch,$incarnation,$lifetime,1,$position,1,0,$receipt);";
        insert.Parameters.AddWithValue("$contract", lifetime.ContractId); insert.Parameters.AddWithValue("$version", lifetime.ContractVersion);
        insert.Parameters.AddWithValue("$subject", lifetime.SubjectId.Value); insert.Parameters.AddWithValue("$kind", kind);
        insert.Parameters.Add("$scope", SqliteType.Blob).Value = seek; insert.Parameters.Add("$protected", SqliteType.Blob).Value = protectedScope;
        insert.Parameters.Add("$epoch", SqliteType.Blob).Value = lifetime.AuthorityEpoch.ToArray();
        insert.Parameters.Add("$incarnation", SqliteType.Blob).Value = lifetime.Incarnation.ToArray();
        insert.Parameters.AddWithValue("$lifetime", lifetime.Incarnation.LifetimeGeneration);
        // Final subject retirement is published after semantic retirement in the
        // supported public lifecycle ordering.
        insert.Parameters.AddWithValue("$position", checked(retired.RetirementPosition + 1));
        insert.Parameters.AddWithValue("$receipt", Convert.ToHexStringLower(SHA256.HashData("subject-terminal"u8)));
        (await insert.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static byte[] OrderedSemanticAuthoritiesChecksum(IEnumerable<byte[]> rows)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.orderedRows.v1\0"u8); byte[] length = new byte[4];
        foreach (byte[] row in rows) { BinaryPrimitives.WriteInt32BigEndian(length, row.Length); hash.AppendData(length); hash.AppendData(row); }
        return hash.GetHashAndReset();
    }

    private static byte[] SemanticHistoricalNegativeRow(byte[] binding, byte[] key, int state, byte[] authority) =>
        SemanticFramedHash("base.semanticActivation.historicalNegativeRow.v1\0", binding, key, Int64Bytes(state), authority);

    private static byte[] SemanticDefinitionStateChecksum(byte[] binding, byte[] key, int state, long generation, byte[] authority)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.definitionState.v1\0"u8); hash.AppendData(binding); hash.AppendData(key);
        hash.AppendData([(byte)state]); hash.AppendData(Int64Bytes(generation)); hash.AppendData(authority); return hash.GetHashAndReset();
    }

    private static byte[] SemanticFramedHash(string purpose, params byte[][] values)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData(Encoding.UTF8.GetBytes(purpose));
        byte[] length = new byte[4]; foreach (byte[] value in values)
        { BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length); hash.AppendData(value); }
        return hash.GetHashAndReset();
    }

    private static byte[] Int64Bytes(long value)
    { byte[] bytes = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); return bytes; }

    private static byte[] SemanticIncarnation(long generation)
    {
        byte[] value = new byte[24]; BinaryPrimitives.WriteInt64BigEndian(value.AsSpan(0, 8), generation);
        Enumerable.Repeat((byte)0x42, 16).ToArray().CopyTo(value, 8); return value;
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task In_place_restore_unions_post_artifact_retirement_without_rematerialization(
        bool artifactContainsLive, bool pruneAfterRetirement, bool corruptArtifactControl)
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
            var ensure = new SqliteSemanticEnsureProbe(
                authority, limits, "restore-live", maximumYields: artifactContainsLive ? 2 : 0);
            (await store.ExecuteAtomicAsync(ensure, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            if (artifactContainsLive)
            {
                byte[]? originalControl = null;
                if (corruptArtifactControl)
                {
                    await using var corrupt = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
                    await corrupt.OpenAsync(); await using var mutate = corrupt.CreateCommand();
                    mutate.CommandText = "SELECT control_checksum FROM hpd_base_activations LIMIT 1;";
                    originalControl = (byte[])(await mutate.ExecuteScalarAsync())!;
                    mutate.CommandText = "UPDATE hpd_base_activations SET control_checksum=$checksum;";
                    mutate.Parameters.Add("$checksum", Microsoft.Data.Sqlite.SqliteType.Blob).Value = new byte[32];
                    (await mutate.ExecuteNonQueryAsync()).Should().Be(1);
                }
                backup = await store.CreateBackupAsync(artifact, new BaseBackupRequest
                { StoreId = "module-store", Principal = AdministrationPrincipal() });
                backup.IsSuccess().Should().BeTrue(backup.Error?.Code);
                if (originalControl is not null)
                {
                    await using var repair = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
                    await repair.OpenAsync(); await using var mutate = repair.CreateCommand();
                    mutate.CommandText = "UPDATE hpd_base_activations SET control_checksum=$checksum;";
                    mutate.Parameters.Add("$checksum", Microsoft.Data.Sqlite.SqliteType.Blob).Value = originalControl;
                    (await mutate.ExecuteNonQueryAsync()).Should().Be(1);
                }
                await YieldSemanticActivationAsync(store, "restore-yield");
            }

            if (pruneAfterRetirement)
                await DisposeSemanticActivationAsync(store, "restore-terminal", artifactContainsLive ? 12 : 10);
            else
                await CompleteSemanticActivationAsync(store, "restore-terminal", artifactContainsLive ? 12 : 10);
            authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
            var retire = new SqliteSemanticEnsureProbe(authority, limits, "restore-retire", retire: true);
            RecordMutationExecutionResult retirement = await store.ExecuteAtomicAsync(retire, ExecutionRequest());
            retirement.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, $"{retire.RejectedCode}:{retirement.Error?.Code}:{retirement.Error?.Message}");
            await using (var before = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await before.OpenAsync(); await using var floor = before.CreateCommand();
                floor.CommandText = "INSERT INTO hpd_base_semantic_activation_recovery_floors(definition_id,binding_id,key_digest,state,slot_generation,authority_json,receipt_scope,receipt_operation,receipt_key,receipt_fingerprint,receipt_structural_digest,receipt_result_json,receipt_authority_checksum,receipt_slot_authority_json) SELECT definition_id,binding_id,key_digest,state,slot_generation,authority_json,'semantic','retire','restore-retire',$fingerprint,$structural,$result,$authority,authority_json FROM hpd_base_semantic_activation_slots WHERE state=2;";
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
                    Take = 1, AcceptedTime = AcceptedTime(86_400_040), Identity = ActivationIdentity("restore-prune"),
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
            if (corruptArtifactControl)
            {
                restored.IsSuccess().Should().BeFalse();
                restored.Error!.Code.Should().Be(BaseSemanticActivationErrorCodes.RecoveryProofInvalid);
                return;
            }
            restored.IsSuccess().Should().BeTrue($"{restored.Error?.Code}:{restored.Error?.Message}:{restored.Error?.Detail}");
            if (artifactContainsLive)
            {
                BaseActivationYieldReservationState reservation =
                    (await store.ReadYieldReservationStateAsync()).Value!;
                reservation.ReservedUnusedSlots.Should().Be(0);
                reservation.RetainedUsedSlots.Should().Be(pruneAfterRetirement ? 0 : 1);
            }

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
        ImmutableArray<BaseSemanticActivationRemovalAuthority> removals = default,
        ImmutableArray<BaseSemanticActivationKeyDefinition> additionalDefinitions = default,
        bool administrationEnabled = false,
        int maximumCanonicalKeyBytes = 256,
        bool enableScopeRotationKey = false,
        ISqliteAdministrationOperationController? administrationOperations = null,
        TimeSpan? restoreStagingTimeout = null)
    {
        BaseSemanticActivationKeyDefinition definition = installedDefinition ?? SemanticDefinition(maximumLiveSlots, maximumReceiptBytes, maximumCanonicalKeyBytes);
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "module-store", DataSource = path, Collections = [],
            SemanticActivations = additionalDefinitions.IsDefault ? [definition] : [definition, .. additionalDefinitions], SemanticActivationApplicationId = definition.OwningApplicationId,
            SemanticActivationOwnerGeneration = ownerGeneration,
            SemanticActivationDefinitionSetChecksum = definitionSetChecksum.IsDefaultOrEmpty
                ? definition.Checksum.ToArray() : definitionSetChecksum.ToArray(),
            SemanticActivationMigrations = migrations.IsDefault ? [] : migrations.ToArray(),
            SemanticActivationRemovals = removals.IsDefault ? [] : removals.ToArray(),
            ModuleMutations = [Definition()], ModuleGenerationCells = [Cell()],
            MaxPendingActivationRows = maximumPendingRows, MaxClaimedActivationRows = 100,
            AdministrationEnabled = administrationEnabled, MaxBackupArtifactBytes = 16 * 1024 * 1024,
            RestoreStagingTimeout = restoreStagingTimeout ?? TimeSpan.FromSeconds(30),
        };
        var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey { Id = 31, Key = Enumerable.Repeat((byte)0x31, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch },
            DecryptionKeys = enableScopeRotationKey
                ? [new BaseOpaqueTokenKey { Id = 32, Key = Enumerable.Repeat((byte)0x32, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch }]
                : [],
        }));
        var store = new SqliteRecordStore(options, NullLoggerFactory.Instance, TimeProvider.System,
            tokenProtector: protector, administrationOperations: administrationOperations);
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
        BaseRegisteredModuleMutationDefinition module) => BaseActivationDefinitionBuilder.CreateGeneratedTransactional(new BaseActivationDefinitionDraft
        {
            Id = "module.semantic.child", Version = 1, OwningModuleId = "module",
            ExecutionClass = BaseActivationExecutionClass.TransactionalOperation,
            Grants = new BaseActivationGrantSet
            {
                Enqueue = "activation.enqueue", Observe = "activation.observe", Claim = "activation.claim", Execute = "activation.execute",
                Renew = "activation.renew", Complete = "activation.complete", Fail = "activation.fail", Yield = "activation.yield", Cancel = "activation.cancel",
                Inspect = "activation.inspect", Replay = "activation.replay", Migrate = "activation.migrate", Reconcile = "activation.reconcile",
                Retry = "activation.retry", Dispose = "activation.dispose", Remove = "activation.remove", Repair = "activation.repair",
            },
            SourceGrantIds = [], Retry = new BaseActivationRetryProfile
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
                MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 1, MaximumYields = 0, MaximumRenewalsPerSlice = 1,
                MaximumChildrenPerSlice = 4, MaximumLineageDepth = 4, LeaseDuration = TimeSpan.FromMinutes(1),
                HandlerTimeout = TimeSpan.FromMinutes(1), Provider = ActivationLimits(), AtomicCreation = ExecutionLimits(),
            },
            TransactionalTarget = new BaseModuleMutationActivationTarget
            {
                OperationId = module.Id, OperationVersion = module.Version,
                OperationChecksum = Convert.ToHexStringLower(module.Checksum.ToArray()),
            },
        }, SqliteActivationDtos.HPDBaseActivationDtoAuthority);

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
        bool acceptedCurrentTime = false,
        BaseSemanticActivationKeyDefinition? semanticDefinition = null,
        long semanticOwnerGeneration = 1,
        BaseSemanticActivationSubjectLifetimeBinding? subjectLifetime = null,
        long? acceptedTimeSeconds = null,
        int maximumYields = 0) : IAtomicMutationProcessor
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
            BaseSemanticActivationKeyDefinition installedSemantic = semanticDefinition ?? SemanticDefinition();
            byte[] definitionChecksum = installedSemantic.Checksum.ToArray();
            byte[] canonicalKey = Encoding.UTF8.GetBytes(semanticKey);
            byte[] binding = SHA256.HashData(Encoding.UTF8.GetBytes("runtime-proposed-binding:" + parentIdentity));
            BaseSemanticActivationKeyDigest key = BaseSemanticActivationKeyDigest.Create(BoundHash("base.semanticActivation.key.v1\0", Encoding.UTF8.GetBytes(installedSemantic.Id), binding, canonicalKey));
            Span<byte> keyBytes = stackalloc byte[32]; key.CopyTo(keyBytes);
            byte[] activationId = BoundHash("base.semanticActivation.activation.v1\0", Encoding.UTF8.GetBytes(authority.ApplicationId),
                Encoding.UTF8.GetBytes(authority.SemanticActivation!.LogicalStoreId), Encoding.UTF8.GetBytes(installedSemantic.OwningModuleId), Encoding.UTF8.GetBytes(installedSemantic.Id), binding, canonicalKey);
            var retirement = new BaseSemanticActivationModuleOperationIdentity
            {
                OperationId = installedSemantic.RetirementOperation.OperationId,
                OperationVersion = installedSemantic.RetirementOperation.OperationVersion,
                OperationChecksum = installedSemantic.RetirementOperation.OperationChecksum,
            };
            var definition = new BaseSemanticActivationDefinitionIdentity
            {
                Id = installedSemantic.Id, Version = installedSemantic.Version, Checksum = definitionChecksum.ToImmutableArray(),
                OwnerGeneration = semanticOwnerGeneration, OwningModuleId = installedSemantic.OwningModuleId,
                RetirementOperation = retirement,
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
                SubjectLifetime = subjectLifetime,
                Activation = new()
                {
                    Definition = installedSemantic.Activation,
                    ReceiptRetention = new BaseActivationReceiptRetentionPolicy
                    {
                        FormatVersion = 1,
                        DuplicateResolutionLifetime = TimeSpan.FromHours(24),
                        ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
                    },
                    CanonicalInput = "payload"u8.ToArray().ToImmutableArray(),
                    InputChecksum = SHA256.HashData("payload"u8).ToImmutableArray(), Scope = scope, Due = due, Priority = 0, InitiallyEligible = true,
                    Limits = CreationLimits(maximumYields), Identity = new()
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
                    CompletionOperation = retirement, SubjectLifetime = subjectLifetime,
                }
                : ensure;
            var extension = new BaseAtomicSemanticActivationExtension
            {
                Capture = new()
                {
                    Definition = definition, CanonicalKey = canonicalKey.ToImmutableArray(), KeyPreimageChecksum = SHA256.HashData(canonicalKey).ToImmutableArray(),
                    Scope = scope, ProposedScopeBindingId = binding.ToImmutableArray(), Operation = retire
                        ? BaseSemanticActivationOperationKind.Retire : BaseSemanticActivationOperationKind.Ensure,
                    StoreAuthority = authority.SemanticActivation!,
                    Limits = CreateLimits(), AcceptedTime = AcceptedTime(acceptedTimeSeconds ?? (retire ? 30 : 1)),
                },
                Operation = operation,
                StructuralDigest = BoundHash("base.semanticActivation.extension.v1\0", definitionChecksum, canonicalKey, binding, [retire ? (byte)2 : (byte)1]).ToImmutableArray(),
            };
            var request = new BaseAtomicExecutionRequest
            {
                Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
                Intent = new() { IntentDigest = parentIdentity, Authority = authority, Items = [] },
                Module = new() { OperationId = retire ? retirement.OperationId : installedSemantic.EnsureOperation.OperationId,
                    OperationVersion = retire ? retirement.OperationVersion : installedSemantic.EnsureOperation.OperationVersion,
                    OperationChecksum = retire ? retirement.OperationChecksum : installedSemantic.EnsureOperation.OperationChecksum,
                    RequestDigest = parentIdentity, Records = [], RelationTargets = [], Generations = [] },
                SemanticActivation = extension, Limits = limits,
            };
            OperationResult<BaseCapturedAtomicExecution> captured = await session.CaptureAtomicExecutionAsync(request, cancellationToken);
            if (!captured.IsSuccess() || captured.Value?.SemanticActivation is null) { RejectedCode = captured.Error?.Code ?? BaseSemanticActivationErrorCodes.ProviderContractInvalid; return Failure(captured.Error); }
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
                Module = new() { OperationId = retire ? retirement.OperationId : installedSemantic.EnsureOperation.OperationId,
                    OperationVersion = retire ? retirement.OperationVersion : installedSemantic.EnsureOperation.OperationVersion,
                    OperationChecksum = retire ? retirement.OperationChecksum : installedSemantic.EnsureOperation.OperationChecksum,
                    Decisions = [], ItemBindings = [], RelationTargets = [], Comparisons = [], Increments = [], ResultProjectionDigest = parentIdentity },
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
                EnsureDisposition = null, RetirementDisposition = provisional.PriorState switch
                {
                    BaseSemanticActivationCapturedState.Live => BaseSemanticActivationRetirementDisposition.RetiredNow,
                    BaseSemanticActivationCapturedState.Retired => BaseSemanticActivationRetirementDisposition.AlreadyRetired,
                    BaseSemanticActivationCapturedState.CompactedAbsent => BaseSemanticActivationRetirementDisposition.AlreadyCompacted,
                    _ => throw new InvalidOperationException("base.semanticActivation.referenceInvalid"),
                },
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
            return bound with { Checksum = BaseSemanticActivationEvidenceContract.SubjectLifetimeChecksum(bound) };
        }

        internal static BaseSemanticActivationExecutionLimits CreateLimits() => new()
        {
            MaximumOperations = 1, MaximumScopeDirectoryReads = 1, MaximumSlotReads = 1, MaximumActivationReads = 1,
            MaximumReadIntervals = 4, MaximumIndexOperations = 4, MaximumActivationBytes = 4096,
            MaximumScopeDirectoryBytes = 4096, MaximumEvidenceBytes = 16384, MaximumReceiptBytes = 4096, MaximumTransientBytes = 32768,
        };

        private static BaseActivationLimits CreationLimits(int maximumYields) => new()
        {
            MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 3,
            MaximumYields = maximumYields, MaximumRenewalsPerSlice = 3,
            MaximumChildrenPerSlice = 8, MaximumLineageDepth = 8, LeaseDuration = TimeSpan.FromMinutes(1), HandlerTimeout = TimeSpan.FromMinutes(1),
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
