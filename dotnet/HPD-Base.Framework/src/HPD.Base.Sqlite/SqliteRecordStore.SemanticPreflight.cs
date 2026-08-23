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
        string storeInstance = _options.StoreId;
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
        BaseActivationPayload activationPayload; ImmutableArray<byte> creationFingerprint; int priority; ImmutableArray<byte>? overlapKey;
        BaseScheduleOverlapPolicy overlapPolicy; bool eligible; int attemptNumber; long claimEpoch; ImmutableArray<byte>? canonicalResult;
        await using (SqliteCommand activation = connection.CreateCommand())
        {
            activation.Transaction = transaction;
            activation.CommandText = $"SELECT definition_id,definition_version,definition_checksum,canonical_input,input_checksum,scope_kind,scope_value,payload_checksum,fingerprint,state,generation,requested_due_at,effective_due_at,occurrence_id,priority,overlap_key,overlap_policy,eligible,control_checksum,attempt_number,claim_epoch,claim_fence,claim_worker,lease_revision,lease_expires_at,canonical_result,terminal_receipt_checksum FROM {_names.Activations} WHERE activation_id=$id;";
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
                CanonicalInput = ((byte[])reader.GetValue(3)).ToImmutableArray(), InputChecksum = ((byte[])reader.GetValue(4)).ToImmutableArray(),
                Scope = live.Scope, OccurrenceId = null, RequestedDueAt = reader.GetInt64(11), EffectiveDueAt = reader.GetInt64(12),
                Checksum = ((byte[])reader.GetValue(7)).ToImmutableArray(),
            };
            creationFingerprint = ((byte[])reader.GetValue(8)).ToImmutableArray(); priority = reader.GetInt32(14);
            overlapKey = reader.IsDBNull(15) ? null : ((byte[])reader.GetValue(15)).ToImmutableArray();
            overlapPolicy = (BaseScheduleOverlapPolicy)reader.GetInt32(16); eligible = reader.GetInt32(17) != 0;
            attemptNumber = reader.GetInt32(19); claimEpoch = reader.GetInt64(20);
            canonicalResult = reader.IsDBNull(25) ? null : ((byte[])reader.GetValue(25)).ToImmutableArray();
        }
        if (activationState is not (BaseActivationState.Succeeded or BaseActivationState.Exhausted or BaseActivationState.Cancelled
                or BaseActivationState.Migrated or BaseActivationState.Disposed) || terminalReceipt.Length != 32)
            return PreflightFailure(BaseSemanticActivationErrorCodes.ActivationNotTerminal, OperationStatus.Conflict, ErrorCategory.Conflict);
        byte[] expectedControl = SHA256.HashData(Encoding.UTF8.GetBytes($"base.activation.control.v2\0{live.ActivationId}\n{activationGeneration}\n{(int)activationState}"));
        if (!CryptographicOperations.FixedTimeEquals(expectedControl, activationChecksum.AsSpan())) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
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
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT receipt_key,operation_kind,fingerprint,result_json,result_checksum,authority_checksum FROM {_names.ActivationReceipts} WHERE activation_id=$id AND authority_checksum=$authority LIMIT 2;";
        command.Parameters.AddWithValue("$id", activationId); command.Parameters.Add("$authority", SqliteType.Blob).Value = authorityChecksum.ToArray();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        string receiptKey = reader.GetString(0); string kind = reader.GetString(1); byte[] fingerprint = (byte[])reader.GetValue(2);
        byte[] json = (byte[])reader.GetValue(3); byte[] resultChecksum = (byte[])reader.GetValue(4); byte[] storedAuthority = (byte[])reader.GetValue(5);
        BaseActivationTransitionResult? result = JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult);
        byte[] expectedAuthority = SHA256.HashData(Encoding.UTF8.GetBytes(kind).Concat(fingerprint).Concat(json).ToArray());
        bool valid = PreflightTerminalKind(kind, state) && fingerprint.Length == 32 && result is not null
            && result.State == state && result.Generation == generation
            && CryptographicOperations.FixedTimeEquals(result.ControlChecksum.AsSpan(), controlChecksum.AsSpan())
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(json), resultChecksum)
            && CryptographicOperations.FixedTimeEquals(authorityChecksum.AsSpan(), storedAuthority)
            && CryptographicOperations.FixedTimeEquals(expectedAuthority, storedAuthority);
        bool additional = await reader.ReadAsync(token).ConfigureAwait(false);
        return valid && !additional ? new BaseSemanticRecoveryTerminalReceiptEvidence
        {
            ReceiptKey = receiptKey, OperationKind = kind, Fingerprint = fingerprint.ToImmutableArray(),
            ResultBytes = json.ToImmutableArray(), ResultChecksum = resultChecksum.ToImmutableArray(),
            AuthorityChecksum = storedAuthority.ToImmutableArray(),
        } : null;
    }

    private static bool PreflightTerminalKind(string kind, BaseActivationState state) => state switch
    {
        BaseActivationState.Succeeded => kind is "activation-completed" or "effect-completed" or "effect-reconciled",
        BaseActivationState.Exhausted => kind is "activation-failed-terminal" or "effect-reconciled",
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
