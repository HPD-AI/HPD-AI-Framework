using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore
{
    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSemanticRecoveryPreflightEvidence>> PreflightSemanticRecoveryAsync(
        BaseSemanticRecoveryPreflightRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Deadline <= TimeSpan.Zero || request.Deadline > _options.CommandTimeout
            || request.CanonicalKey.IsDefaultOrEmpty || request.KeyPreimageChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(request.CanonicalKey.AsSpan()), request.KeyPreimageChecksum.AsSpan())
            || request.StoreAuthority.DefinitionSetChecksum.Length != 32 || _subjectScopes is null
            || _subjectScopeProtectionKey is null || _subjectScopeProtectionKeyId is null)
            return PreflightFailure(BaseSemanticActivationErrorCodes.Invalid, OperationStatus.ValidationFailed, ErrorCategory.Validation);
        BaseSemanticActivationKeyDefinition? installed = _options.SemanticActivations.SingleOrDefault(value =>
            value.Id == request.Definition.Id && value.Version == request.Definition.Version);
        if (installed is null || request.Definition.OwnerGeneration != _options.SemanticActivationOwnerGeneration
            || request.Definition.OwningModuleId != installed.OwningModuleId
            || !CryptographicOperations.FixedTimeEquals(request.Definition.Checksum.AsSpan(), installed.Checksum.AsSpan()))
            return PreflightFailure(BaseSemanticActivationErrorCodes.GraphChanged, OperationStatus.Conflict, ErrorCategory.Conflict);
        BaseSemanticActivationCapability providerCapability = BaseSemanticActivationCapabilityContract.BuiltIn(durable: true);
        int maximumKeyBytes = Math.Min(installed.Limits.MaximumCanonicalKeyBytes, providerCapability.MaximumKeyBytes);
        if (request.MaximumCanonicalKeyBytes != maximumKeyBytes || request.CanonicalKey.Length > maximumKeyBytes)
            return PreflightFailure(BaseSemanticActivationErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Deadline); CancellationToken token = deadline.Token;
        await using SqliteConnection connection = await _connections.OpenAsync(token).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token).ConfigureAwait(false);
        long restoreEpoch = await ReadSemanticPreflightRestoreEpochAsync(connection, transaction, token).ConfigureAwait(false);
        string storeInstance;
        await using (SqliteCommand identity = connection.CreateCommand())
        {
            identity.Transaction = transaction;
            identity.CommandText = $"SELECT store_instance_id FROM {_names.SchemaIdentity} WHERE singleton=1;";
            storeInstance = (string?)await identity.ExecuteScalarAsync(token).ConfigureAwait(false)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        Volatile.Write(ref _currentStoreInstanceId, storeInstance);
        long schemaGeneration = Volatile.Read(ref _schemaGeneration);
        (long semanticGeneration, byte[] definitionSet) = await ReadSemanticPreflightAuthorityAsync(connection, transaction, token).ConfigureAwait(false);
        if (request.StoreAuthority.ApplicationId != _options.SemanticActivationApplicationId
            || request.StoreAuthority.LogicalStoreId != _options.StoreId || request.StoreAuthority.StoreInstanceId != storeInstance
            || request.StoreAuthority.RestoreEpoch != restoreEpoch || request.StoreAuthority.SchemaGeneration != schemaGeneration
            || request.StoreAuthority.SemanticAuthorityGeneration != semanticGeneration
            || !CryptographicOperations.FixedTimeEquals(request.StoreAuthority.DefinitionSetChecksum.AsSpan(), definitionSet))
            return PreflightFailure(BaseSemanticActivationErrorCodes.RestoreConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
        BaseProtectedSubjectScope protectedScope = _subjectScopes.Protect(request.Scope, _subjectScopeProtectionKey.Value);
        BaseSemanticActivationScopeBinding binding;
        await using (SqliteCommand scope = connection.CreateCommand())
        {
            scope.Transaction = transaction;
            scope.CommandText = $"SELECT binding_id,binding_json FROM {_names.SemanticActivationScopes} WHERE scope_kind=$kind AND seek_digest=$digest;";
            scope.Parameters.AddWithValue("$kind", (int)request.Scope.Kind); scope.Parameters.Add("$digest", SqliteType.Blob).Value = protectedScope.IndexDigest;
            await using SqliteDataReader reader = await scope.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return PreflightFailure(BaseSemanticActivationErrorCodes.NotInstalled, OperationStatus.NotFound, ErrorCategory.NotFound);
            binding = JsonSerializer.Deserialize((byte[])reader.GetValue(1), HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            var storedProtectedScope = new BaseProtectedSubjectScope
            {
                Kind = binding.Kind, IndexDigest = binding.SeekDigest.ToArray(),
                ProtectedCanonicalValue = binding.ProtectedCanonicalScope.ToArray(),
            };
            if (binding.Kind != request.Scope.Kind || !binding.BindingId.AsSpan().SequenceEqual((byte[])reader.GetValue(0))
                || !_subjectScopes.Matches(storedProtectedScope, request.Scope)
                || !binding.Checksum.AsSpan().SequenceEqual(BaseSemanticActivationEvidenceContract.ScopeBindingChecksum(binding).AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        BaseSemanticActivationKeyDigest key = BaseSemanticActivationKeyDigest.Create(PreflightHash(
            "base.semanticActivation.key.v1\0", Encoding.UTF8.GetBytes(request.Definition.Id), binding.BindingId.ToArray(), request.CanonicalKey.ToArray()));
        byte[] keyBytes = new byte[32]; key.CopyTo(keyBytes);
        BaseSemanticActivationLiveAuthority live;
        await using (SqliteCommand slot = connection.CreateCommand())
        {
            slot.Transaction = transaction;
            slot.CommandText = $"SELECT state,slot_generation,authority_json FROM {_names.SemanticActivationSlots} WHERE definition_id=$definition AND binding_id=$binding AND key_digest=$key;";
            slot.Parameters.AddWithValue("$definition", request.Definition.Id); slot.Parameters.Add("$binding", SqliteType.Blob).Value = binding.BindingId.ToArray();
            slot.Parameters.Add("$key", SqliteType.Blob).Value = keyBytes;
            await using SqliteDataReader reader = await slot.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetInt32(0) != 1)
                return PreflightFailure(BaseSemanticActivationErrorCodes.NotInstalled, OperationStatus.NotFound, ErrorCategory.NotFound);
            live = JsonSerializer.Deserialize((byte[])reader.GetValue(2), HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            if (live.SlotGeneration != reader.GetInt64(1) || !BaseSemanticActivationEvidenceContract.LiveChecksum(live).AsSpan().SequenceEqual(live.Checksum.AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        long activationGeneration; BaseActivationState activationState; ImmutableArray<byte> activationChecksum; ImmutableArray<byte> terminalReceipt;
        long yieldCount; long maximumYields; long executionSliceOrdinal; long? attemptStartedAt; long? sliceStartedAt;
        BaseActivationYieldDisposition? terminalYieldDisposition; string? terminalYieldFailureCode;
        BaseActivationPayload activationPayload; ImmutableArray<byte> creationFingerprint; int priority; ImmutableArray<byte>? overlapKey;
        BaseScheduleOverlapPolicy overlapPolicy; bool eligible; int attemptNumber; long claimEpoch; ImmutableArray<byte>? canonicalResult;
        await using (SqliteCommand activation = connection.CreateCommand())
        {
            activation.Transaction = transaction;
            activation.CommandText = $"SELECT definition_id,definition_version,definition_checksum,canonical_input,input_checksum,scope_kind,scope_value,payload_checksum,fingerprint,state,generation,requested_due_at,effective_due_at,occurrence_id,priority,overlap_key,overlap_policy,eligible,control_checksum,attempt_number,claim_epoch,claim_fence,claim_worker,lease_revision,lease_expires_at,canonical_result,terminal_receipt_checksum,yield_count,maximum_yields,execution_slice_ordinal,attempt_started_at,slice_started_at,yield_terminal_disposition,yield_terminal_failure_code,receipt_format_version,receipt_duplicate_lifetime_ms,receipt_backup_coverage FROM {_names.Activations} WHERE activation_id=$id;";
            activation.Parameters.AddWithValue("$id", live.ActivationId);
            await using SqliteDataReader reader = await activation.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            activationState = (BaseActivationState)reader.GetInt32(9); activationGeneration = reader.GetInt64(10);
            activationChecksum = ((byte[])reader.GetValue(18)).ToImmutableArray();
            terminalReceipt = reader.IsDBNull(26) ? [] : ((byte[])reader.GetValue(26)).ToImmutableArray();
            if (!reader.IsDBNull(13) || !reader.IsDBNull(21) || !reader.IsDBNull(22) || !reader.IsDBNull(23) || !reader.IsDBNull(24))
                return PreflightFailure(BaseSemanticActivationErrorCodes.ActivationNotTerminal, OperationStatus.Conflict, ErrorCategory.Conflict);
            activationPayload = new BaseActivationPayload
            {
                ActivationId = live.ActivationId,
                Definition = new() { Id = reader.GetString(0), Version = reader.GetInt32(1), Checksum = ((byte[])reader.GetValue(2)).ToImmutableArray() },
                ReceiptRetention = new BaseActivationReceiptRetentionPolicy
                {
                    FormatVersion = reader.GetInt32(34),
                    DuplicateResolutionLifetime = TimeSpan.FromMilliseconds(reader.GetInt64(35)),
                    ProtectedBackupCoverage = (BaseActivationProtectedBackupCoverage)reader.GetInt32(36),
                },
                CanonicalInput = ((byte[])reader.GetValue(3)).ToImmutableArray(), InputChecksum = ((byte[])reader.GetValue(4)).ToImmutableArray(),
                Scope = live.Scope, OccurrenceId = null, RequestedDueAt = reader.GetInt64(11), EffectiveDueAt = reader.GetInt64(12),
                Checksum = ((byte[])reader.GetValue(7)).ToImmutableArray(),
            };
            creationFingerprint = ((byte[])reader.GetValue(8)).ToImmutableArray(); priority = reader.GetInt32(14);
            overlapKey = reader.IsDBNull(15) ? null : ((byte[])reader.GetValue(15)).ToImmutableArray();
            overlapPolicy = (BaseScheduleOverlapPolicy)reader.GetInt32(16); eligible = reader.GetInt32(17) != 0;
            attemptNumber = reader.GetInt32(19); claimEpoch = reader.GetInt64(20);
            canonicalResult = reader.IsDBNull(25) ? null : ((byte[])reader.GetValue(25)).ToImmutableArray();
            yieldCount = reader.GetInt64(27); maximumYields = reader.GetInt64(28); executionSliceOrdinal = reader.GetInt64(29);
            attemptStartedAt = reader.IsDBNull(30) ? null : reader.GetInt64(30);
            sliceStartedAt = reader.IsDBNull(31) ? null : reader.GetInt64(31);
            terminalYieldDisposition = reader.IsDBNull(32) ? null : (BaseActivationYieldDisposition)reader.GetInt32(32);
            terminalYieldFailureCode = reader.IsDBNull(33) ? null : reader.GetString(33);
        }
        if (activationState is not (BaseActivationState.Succeeded or BaseActivationState.Exhausted or BaseActivationState.Cancelled
                or BaseActivationState.Migrated or BaseActivationState.Disposed) || terminalReceipt.Length != 32)
            return PreflightFailure(BaseSemanticActivationErrorCodes.ActivationNotTerminal, OperationStatus.Conflict, ErrorCategory.Conflict);
        if (!BaseActivationControlChecksumContract.Matches(activationChecksum.AsSpan(), live.ActivationId,
            activationGeneration, activationState, activationPayload.EffectiveDueAt, yieldCount, maximumYields,
            executionSliceOrdinal, attemptStartedAt, sliceStartedAt, terminalYieldDisposition,
            terminalYieldFailureCode)) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        BaseSemanticRecoveryTerminalReceiptEvidence? receipt = await ReadPreflightTerminalReceiptAsync(connection, transaction, live.ActivationId, activationGeneration,
            activationState, activationChecksum, terminalReceipt, token).ConfigureAwait(false);
        if (receipt is null) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        await using (SqliteCommand effect = connection.CreateCommand())
        {
            effect.Transaction = transaction; effect.CommandText = $"SELECT COUNT(*) FROM {_names.ActivationEffects} WHERE activation_id=$id;";
            effect.Parameters.AddWithValue("$id", live.ActivationId);
            if (Convert.ToInt64(await effect.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 0)
                return PreflightFailure(BaseSemanticActivationErrorCodes.ActivationNotTerminal, OperationStatus.Conflict, ErrorCategory.Conflict);
        }
        var terminalActivation = new BaseSemanticRecoveryTerminalActivationAuthority
        {
            Payload = activationPayload, CreationFingerprint = creationFingerprint, Priority = priority,
            OverlapKey = overlapKey, OverlapPolicy = overlapPolicy, Eligible = eligible,
            State = activationState, Generation = activationGeneration, ControlChecksum = activationChecksum,
            EffectiveDueAt = activationPayload.EffectiveDueAt, YieldCount = yieldCount, MaximumYields = maximumYields,
            ExecutionSliceOrdinal = executionSliceOrdinal, AttemptStartedAt = attemptStartedAt,
            SliceStartedAt = sliceStartedAt, TerminalYieldDisposition = terminalYieldDisposition,
            TerminalYieldFailureCode = terminalYieldFailureCode,
            AttemptNumber = attemptNumber, ClaimEpoch = claimEpoch, CanonicalResult = canonicalResult,
            CanonicalResultChecksum = canonicalResult is null ? null : SHA256.HashData(canonicalResult.Value.AsSpan()).ToImmutableArray(),
            TerminalReceipt = receipt, Checksum = [],
        };
        terminalActivation = terminalActivation with { Checksum = BaseSemanticRecoveryAuthorityContract.TerminalActivationChecksum(terminalActivation) };
        BaseAtomicReadIntervalEvidence[] intervals =
        [
            PreflightInterval("base.semanticActivation.scope", Encoding.UTF8.GetBytes($"{(int)request.Scope.Kind}\n{Convert.ToHexString(binding.SeekDigest.AsSpan())}")),
            PreflightInterval("base.semanticActivation.slot", Encoding.UTF8.GetBytes($"{request.Definition.Id}\n{Convert.ToHexString(binding.BindingId.AsSpan())}\n{Convert.ToHexString(keyBytes)}")),
            PreflightInterval("base.activation.byId", Encoding.UTF8.GetBytes(live.ActivationId)),
        ];
        var result = new BaseSemanticRecoveryPreflightEvidence
        {
            ScopeBinding = binding, Key = key, Live = live, ActivationGeneration = activationGeneration,
            ActivationState = activationState, ActivationChecksum = activationChecksum,
            ActivationTerminalReceiptChecksum = terminalReceipt, TerminalReceipt = receipt,
            TerminalActivation = terminalActivation,
            ReadIntervals = intervals.ToImmutableArray(), Accounting = null!, Checksum = [],
        };
        BaseSemanticActivationAccounting accounting = BaseSemanticActivationEvidenceContract.RecoveryPreflightAccounting(request, result);
        result = result with { Accounting = accounting };
        result = result with { Checksum = BaseSemanticActivationEvidenceContract.RecoveryPreflightChecksum(request, result) };
        if (!PreflightAccountingWithinExecution(accounting, request.Limits))
            return PreflightFailure(BaseSemanticActivationErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (!BaseSemanticActivationEvidenceContract.RecoveryPreflightIsValid(request, result)
            || !PreflightAccountingWithinProvider(accounting, providerCapability))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        await transaction.RollbackAsync(token).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    private async ValueTask<BaseSemanticRecoveryTerminalReceiptEvidence?> ReadPreflightTerminalReceiptAsync(SqliteConnection connection, SqliteTransaction transaction, string activationId,
        long generation, BaseActivationState state, ImmutableArray<byte> controlChecksum,
        ImmutableArray<byte> authorityChecksum, CancellationToken token)
    {
        if (state == BaseActivationState.Migrated)
            return await ReadPreflightMigrationReceiptAsync(
                connection, transaction, activationId, generation, controlChecksum, authorityChecksum, token).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT receipt_key,operation_kind,activation_id,definition_id,definition_version,definition_checksum,receipt_format_version,receipt_duplicate_lifetime_ms,receipt_backup_coverage,fingerprint,result_json,result_checksum,authority_checksum,committed_at,duplicate_resolve_until,receipt_sequence,prior_ordered_checksum,ordered_checksum FROM {_names.ActivationInstanceReceipts} WHERE activation_id=$id AND authority_checksum=$authority LIMIT 2;";
        command.Parameters.AddWithValue("$id", activationId); command.Parameters.Add("$authority", SqliteType.Blob).Value = authorityChecksum.ToArray();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        string receiptKey = reader.GetString(0); string kind = reader.GetString(1); string storedActivationId = reader.GetString(2);
        var definition = new BaseActivationDefinitionKey
        {
            Id = reader.GetString(3), Version = reader.GetInt32(4), Checksum = ((byte[])reader.GetValue(5)).ToImmutableArray(),
        };
        var retention = new BaseActivationReceiptRetentionPolicy
        {
            FormatVersion = reader.GetInt32(6),
            DuplicateResolutionLifetime = TimeSpan.FromMilliseconds(reader.GetInt64(7)),
            ProtectedBackupCoverage = (BaseActivationProtectedBackupCoverage)reader.GetInt32(8),
        };
        byte[] fingerprint = (byte[])reader.GetValue(9); byte[] json = (byte[])reader.GetValue(10);
        byte[] resultChecksum = (byte[])reader.GetValue(11); byte[] storedAuthority = (byte[])reader.GetValue(12);
        long committedAt = reader.GetInt64(13); long duplicateResolveUntil = reader.GetInt64(14);
        long sequence = reader.GetInt64(15); byte[] priorOrdered = (byte[])reader.GetValue(16); byte[] ordered = (byte[])reader.GetValue(17);
        BaseActivationTransitionResult? transition = kind == "activation-yielded-v1" ? null
            : JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult);
        BaseActivationYieldReceipt? yielded = kind == "activation-yielded-v1"
            ? JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseActivationYieldReceipt) : null;
        ImmutableArray<byte> expectedAuthority = BaseActivationInstanceReceiptChainContract.ReceiptAuthorityChecksum(receiptKey, kind, storedActivationId,
            definition, retention, fingerprint, resultChecksum, committedAt, duplicateResolveUntil,
            sequence, priorOrdered);
        ImmutableArray<byte> expectedOrdered = BaseActivationInstanceReceiptChainContract.Append(
            sequence, priorOrdered, storedAuthority, receiptKey);
        bool resultMatches = transition is not null
            ? transition.State == state && transition.Generation == generation
                && CryptographicOperations.FixedTimeEquals(transition.ControlChecksum.AsSpan(), controlChecksum.AsSpan())
            : yielded is not null && yielded.ResultingState == state && yielded.ResultingGeneration == generation
                && CryptographicOperations.FixedTimeEquals(yielded.ControlChecksum.AsSpan(), controlChecksum.AsSpan());
        bool valid = PreflightTerminalKind(kind, state) && storedActivationId == activationId
            && fingerprint.Length == 32 && resultMatches
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(json), resultChecksum)
            && CryptographicOperations.FixedTimeEquals(authorityChecksum.AsSpan(), storedAuthority)
            && CryptographicOperations.FixedTimeEquals(expectedAuthority.AsSpan(), storedAuthority)
            && CryptographicOperations.FixedTimeEquals(expectedOrdered.AsSpan(), ordered);
        bool additional = await reader.ReadAsync(token).ConfigureAwait(false);
        return valid && !additional ? new BaseSemanticRecoveryTerminalReceiptEvidence
        {
            Kind = BaseSemanticRecoveryTerminalReceiptKind.Instance,
            ReceiptKey = receiptKey, OperationKind = kind,
            Fingerprint = fingerprint.ToImmutableArray(),
            ResultBytes = json.ToImmutableArray(), ResultChecksum = resultChecksum.ToImmutableArray(),
            AuthorityChecksum = storedAuthority.ToImmutableArray(),
            Instance = new BaseSemanticRecoveryInstanceReceiptAuthority
            {
                ActivationId = storedActivationId, Definition = definition, ReceiptRetention = retention,
                CommittedAt = committedAt, DuplicateResolveUntil = duplicateResolveUntil,
                ReceiptSequence = sequence, PriorOrderedChecksum = priorOrdered.ToImmutableArray(),
                OrderedChecksum = ordered.ToImmutableArray(),
            },
        } : null;
    }

    private async ValueTask<BaseSemanticRecoveryTerminalReceiptEvidence?> ReadPreflightMigrationReceiptAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string activationId,
        long generation,
        ImmutableArray<byte> controlChecksum,
        ImmutableArray<byte> authorityChecksum,
        CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT receipt_key,operation_kind,fingerprint,result_json,result_checksum,authority_checksum FROM {_names.ActivationControlReceipts} WHERE authority_checksum=$authority LIMIT 2;";
        command.Parameters.Add("$authority", SqliteType.Blob).Value = authorityChecksum.ToArray();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        string receiptKey = reader.GetString(0); string operationKind = reader.GetString(1);
        byte[] fingerprint = (byte[])reader[2]; byte[] resultBytes = (byte[])reader[3];
        byte[] resultChecksum = (byte[])reader[4]; byte[] storedAuthority = (byte[])reader[5];
        BaseActivationMigrationResult? result = JsonSerializer.Deserialize(
            resultBytes, HPDBaseJsonSerializerContext.Default.BaseActivationMigrationResult);
        ImmutableArray<byte> expectedAuthority = BaseActivationControlReceiptContract.AuthorityChecksum(
            receiptKey, operationKind, fingerprint, resultChecksum);
        bool valid = operationKind == "activation-migrated" && result is not null
            && result.SourceActivationId == activationId && result.SourceGeneration == generation
            && result.SourceControlChecksum.AsSpan().SequenceEqual(controlChecksum.AsSpan())
            && result.SourceDefinition.Version > 0 && result.SourceDefinition.Checksum.Length == 32
            && result.ReplacementDefinition.Version > 0 && result.ReplacementDefinition.Checksum.Length == 32
            && result.MigrationVersion > 0 && result.MigrationChecksum.Length == 32
            && fingerprint.Length == 32
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(resultBytes), resultChecksum)
            && CryptographicOperations.FixedTimeEquals(authorityChecksum.AsSpan(), storedAuthority)
            && CryptographicOperations.FixedTimeEquals(expectedAuthority.AsSpan(), storedAuthority);
        bool additional = await reader.ReadAsync(token).ConfigureAwait(false);
        return valid && !additional ? new BaseSemanticRecoveryTerminalReceiptEvidence
        {
            Kind = BaseSemanticRecoveryTerminalReceiptKind.Migration,
            ReceiptKey = receiptKey,
            OperationKind = operationKind,
            Fingerprint = fingerprint.ToImmutableArray(),
            ResultBytes = resultBytes.ToImmutableArray(),
            ResultChecksum = resultChecksum.ToImmutableArray(),
            AuthorityChecksum = storedAuthority.ToImmutableArray(),
            Migration = new BaseSemanticRecoveryMigrationReceiptAuthority { Result = result! },
        } : null;
    }

    private static bool PreflightTerminalKind(string kind, BaseActivationState state) => state switch
    {
        BaseActivationState.Succeeded => kind is "activation-completed" or "effect-completed" or "effect-reconciled",
        BaseActivationState.Exhausted => kind is "activation-failed-terminal" or "effect-reconciled" or "activation-yielded-v1",
        BaseActivationState.Cancelled => kind == "activation-cancelled",
        BaseActivationState.Migrated => kind == "activation-migrated",
        BaseActivationState.Disposed => kind == "activation-disposed",
        _ => false,
    };

    private async ValueTask<long> ReadSemanticPreflightRestoreEpochAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch';";
        object? value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        return value is null or DBNull ? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt)
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private async ValueTask<(long Generation, byte[] Definitions)> ReadSemanticPreflightAuthorityAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT CAST(g.value AS INTEGER),d.value FROM {_names.ProviderState} g JOIN {_names.ProviderState} d ON d.key='semantic_activation_definition_set_checksum' WHERE g.key='semantic_activation_authority_generation';";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        return (reader.GetInt64(0), Convert.FromHexString(reader.GetString(1)));
    }

    private static BaseAtomicReadIntervalEvidence PreflightInterval(string path, byte[] key)
    {
        return new BaseAtomicReadIntervalEvidence
        {
            LogicalAccessPathId = path, CanonicalLowerBound = key.ToImmutableArray(), LowerInclusive = true,
            CanonicalUpperBound = key.ToImmutableArray(), UpperInclusive = true,
        };
    }

    private static byte[] PreflightHash(string marker, params byte[][] fields)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData(Encoding.UTF8.GetBytes(marker));
        Span<byte> length = stackalloc byte[4]; foreach (byte[] field in fields) { System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, field.Length); hash.AppendData(length); hash.AppendData(field); }
        return hash.GetHashAndReset();
    }

    private static bool PreflightAccountingWithinProvider(BaseSemanticActivationAccounting value, BaseSemanticActivationCapability limits) =>
        value.Operations <= limits.MaximumOperationsPerTransaction && value.ScopeDirectoryReads <= limits.MaximumScopeDirectoryReads
        && value.SlotReads <= limits.MaximumSlotReads && value.ActivationReads <= limits.MaximumActivationReads
        && value.ReadIntervals <= limits.MaximumReadIntervals && value.IndexOperations <= limits.MaximumIndexOperations
        && value.ActivationBytes <= limits.MaximumActivationBytes && value.ScopeDirectoryBytes <= limits.MaximumScopeDirectoryBytes
        && value.EvidenceBytes <= limits.MaximumEvidenceBytes && value.ReceiptBytes <= limits.MaximumReceiptBytes
        && value.TransientBytes <= limits.MaximumTransientBytes;

    private static bool PreflightAccountingWithinExecution(BaseSemanticActivationAccounting value, BaseSemanticActivationExecutionLimits limits) =>
        value.Operations <= limits.MaximumOperations && value.ScopeDirectoryReads <= limits.MaximumScopeDirectoryReads
        && value.SlotReads <= limits.MaximumSlotReads && value.ActivationReads <= limits.MaximumActivationReads
        && value.ReadIntervals <= limits.MaximumReadIntervals && value.IndexOperations <= limits.MaximumIndexOperations
        && value.ActivationBytes <= limits.MaximumActivationBytes && value.ScopeDirectoryBytes <= limits.MaximumScopeDirectoryBytes
        && value.EvidenceBytes <= limits.MaximumEvidenceBytes && value.ReceiptBytes <= limits.MaximumReceiptBytes
        && value.TransientBytes <= limits.MaximumTransientBytes;

    private static OperationResult<BaseSemanticRecoveryPreflightEvidence> PreflightFailure(string code, OperationStatus status, ErrorCategory category) =>
        new() { Status = status, Error = new BaseError { Code = code, Message = "The semantic recovery preflight is unavailable.", Category = category } };
}
