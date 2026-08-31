using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore
{
    internal async ValueTask<SemanticRecoveryCertificationEvidence?> CaptureSemanticActivationRecoveryFloorCertificationAsync(
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await CaptureSemanticRecoveryCertificationEvidenceAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask<bool> VerifySemanticActivationRecoveryFloorCertificationAsync(
        SemanticRecoveryCertificationEvidence expected, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        SemanticRecoveryCertificationEvidence? actual = await CaptureSemanticRecoveryCertificationEvidenceAsync(connection, cancellationToken).ConfigureAwait(false);
        return actual is not null && expected.RowCount > 0 && actual.RowCount == expected.RowCount
            && actual.RestoreEpoch == checked(expected.RestoreEpoch + 1)
            && actual.AuthorityGeneration == checked(expected.AuthorityGeneration + 1)
            && actual.SchemaGeneration == expected.SchemaGeneration
            && CryptographicOperations.FixedTimeEquals(actual.DefinitionSetChecksum.AsSpan(), expected.DefinitionSetChecksum.AsSpan())
            && CryptographicOperations.FixedTimeEquals(actual.InvariantChecksum.AsSpan(), expected.InvariantChecksum.AsSpan());
    }

    internal async ValueTask<bool> ProveSemanticActivationHistoricalReceiptSubstitutionRejectedAsync(
        SemanticRecoveryCertificationEvidence expected, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        string definition; byte[] binding; byte[] key; byte[] original;
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.CommandText = $"SELECT definition_id,binding_id,key_digest,receipt_slot_authority_json FROM {_names.SemanticActivationRecoveryFloors} WHERE receipt_slot_authority_json IS NOT NULL ORDER BY definition_id,binding_id,key_digest LIMIT 1;";
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return false;
            definition = reader.GetString(0); binding = (byte[])reader[1]; key = (byte[])reader[2]; original = (byte[])reader[3];
        }
        byte[] substituted = original.ToArray();
        substituted[^1] ^= 1;
        async ValueTask WriteAsync(byte[] value)
        {
            await using SqliteCommand write = connection.CreateCommand();
            write.CommandText = $"UPDATE {_names.SemanticActivationRecoveryFloors} SET receipt_slot_authority_json=$value WHERE definition_id=$definition AND binding_id=$binding AND key_digest=$key;";
            write.Parameters.Add("$value", SqliteType.Blob).Value = value;
            write.Parameters.AddWithValue("$definition", definition);
            write.Parameters.Add("$binding", SqliteType.Blob).Value = binding;
            write.Parameters.Add("$key", SqliteType.Blob).Value = key;
            if (await write.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        }
        await WriteAsync(substituted).ConfigureAwait(false);
        bool rejected;
        try
        {
            SemanticRecoveryCertificationEvidence? actual = await CaptureSemanticRecoveryCertificationEvidenceAsync(connection, cancellationToken).ConfigureAwait(false);
            rejected = actual is null || !CryptographicOperations.FixedTimeEquals(
                actual.InvariantChecksum.AsSpan(), expected.InvariantChecksum.AsSpan());
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            rejected = true;
        }
        finally
        {
            await WriteAsync(original).ConfigureAwait(false);
        }
        return rejected && await VerifySemanticActivationRecoveryFloorCertificationAsync(expected, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask CorruptSemanticActivationRecoveryFloorCertificationAsync(
        bool retentionOvertake, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = retentionOvertake
            ? $"DELETE FROM {_names.SemanticActivationRecoveryFloors};"
            : $"UPDATE {_names.SemanticActivationRecoveryFloors} SET authority_json=randomblob(length(authority_json));";
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        bool rejected = false;
        try { await RequireArtifactNegativeCorrespondenceAsync(connection, null, cancellationToken).ConfigureAwait(false); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
    }
    private readonly BaseSemanticActivationCapability _semanticActivationCapability;
    /// <inheritdoc />
    public ImmutableArray<byte> ProviderIncarnation => SHA256.HashData(
        Encoding.UTF8.GetBytes($"hpd.base.sqlite.semantic.incarnation.v1\0{_options.StoreId}"))
        .ToImmutableArray();
    /// <inheritdoc />
    public BaseSemanticActivationCapability SemanticActivationCapability =>
        BaseSemanticActivationCapabilityContract.Clone(_semanticActivationCapability);

    /// <inheritdoc />
    public BaseSemanticActivationOperationalStatus SemanticActivationOperationalStatus => new()
    {
        Ready = Volatile.Read(ref _semanticMutationQuarantined) == 0,
        Quarantined = Volatile.Read(ref _semanticMutationQuarantined) != 0,
        ActiveOperations = Volatile.Read(ref _semanticMutationActive),
        RetainedOperations = Volatile.Read(ref _semanticMutationRetained),
        MaximumRetainedOperations = _semanticActivationCapability.MaximumQuarantinedOperations,
    };

    internal (int Active, int Quarantined, int Released, int RejectedLateCompletions)
        ObserveSemanticLateWorkCertificationState() =>
        (Volatile.Read(ref _semanticMutationActive), Volatile.Read(ref _semanticMutationQuarantined),
            Volatile.Read(ref _semanticMutationReleased), Volatile.Read(ref _semanticRejectedLateCompletions));

    internal async ValueTask<(long Live, long Retired, long Absent, long Activations, long Receipts)>
        ObserveSemanticActivationCertificationStateAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COALESCE(SUM(state=1),0),COALESCE(SUM(state=2),0),COALESCE(SUM(state=3),0),(SELECT COUNT(*) FROM {_names.Activations}),(SELECT COUNT(*) FROM {_names.OperationReceipts}) FROM {_names.SemanticActivationSlots};";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
    }

    internal async ValueTask<ImmutableArray<byte>> ReadSemanticActivationCertificationAuthorityAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT state,authority_json FROM {_names.SemanticActivationSlots} LIMIT 2;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        int state = reader.GetInt32(0); byte[] json = (byte[])reader.GetValue(1);
        ImmutableArray<byte> checksum = state switch
        {
            1 => JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)!.Checksum,
            2 => JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)!.Checksum,
            3 => JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)!.Checksum,
            _ => throw new InvalidOperationException("base.semanticActivation.certificationInvalid"),
        };
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        return checksum.ToArray().ToImmutableArray();
    }

    internal async ValueTask CorruptSemanticActivationCertificationStateAsync(bool compactedAbsence,
        BaseSemanticActivationDefinitionIdentity definition, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand read = connection.CreateCommand(); read.Transaction = transaction;
        read.CommandText = $"SELECT rotation_id,authority_json FROM {_names.SemanticActivationSlots} WHERE state=2 LIMIT 2;";
        await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        long row = reader.GetInt64(0); byte[] json = (byte[])reader.GetValue(1);
        BaseSemanticActivationRetirementAuthority retired = JsonSerializer.Deserialize(json,
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)!;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        await reader.DisposeAsync().ConfigureAwait(false);
        object authority; int state;
        if (compactedAbsence)
        {
            authority = new BaseSemanticActivationAbsenceAuthority
            {
                Key = BaseSemanticActivationKeyDigest.Create(retired.KeyDigest.ToArray()), Definition = definition,
                ScopeBindingId = retired.ScopeBindingId, SubjectLifetime = retired.SubjectLifetime,
                FinalSlotGeneration = retired.SlotGeneration, AbsenceFloorGeneration = retired.SlotGeneration,
                RetirementPosition = retired.RetirementPosition, StoreAuthority = retired.StoreAuthority,
                Checksum = new byte[32].ToImmutableArray(),
            };
            state = 3;
        }
        else { authority = retired with { Checksum = new byte[32].ToImmutableArray() }; state = 2; }
        byte[] encoded = state == 3
            ? JsonSerializer.SerializeToUtf8Bytes((BaseSemanticActivationAbsenceAuthority)authority, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)
            : JsonSerializer.SerializeToUtf8Bytes((BaseSemanticActivationRetirementAuthority)authority, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority);
        await using SqliteCommand update = connection.CreateCommand(); update.Transaction = transaction;
        update.CommandText = $"UPDATE {_names.SemanticActivationSlots} SET state=$state,activation_id=NULL,authority_json=$authority WHERE rotation_id=$row;";
        update.Parameters.AddWithValue("$state", state); update.Parameters.AddWithValue("$authority", encoded); update.Parameters.AddWithValue("$row", row);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ImmutableArray<BaseSemanticActivationDefinitionMigrationAuthority>> ReadSemanticMigrationChainAsync(
        SqliteConnection connection, SqliteTransaction transaction, BaseSemanticActivationDefinitionKey source,
        BaseSemanticActivationDefinitionKey target, CancellationToken token)
    {
        if (DefinitionEqual(source, target)) return [];
        var result = ImmutableArray.CreateBuilder<BaseSemanticActivationDefinitionMigrationAuthority>();
        BaseSemanticActivationDefinitionKey cursor = source;
        var visited = new HashSet<(int Version, string Checksum)>();
        while (!DefinitionEqual(cursor, target))
        {
            if (!visited.Add((cursor.Version, Convert.ToHexString(cursor.Checksum.AsSpan()))))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = $"SELECT migration_id,migration_version,from_definition_id,from_version,from_checksum,to_definition_id,to_version,to_checksum,live_count,retired_count,absence_count,negative_checksum,publication_generation,receipt_checksum,authority_checksum FROM {_names.SemanticActivationMigrations} WHERE from_definition_id=$id AND from_version=$version AND from_checksum=$checksum;";
            command.Parameters.AddWithValue("$id", cursor.Id); command.Parameters.AddWithValue("$version", cursor.Version);
            command.Parameters.Add("$checksum", SqliteType.Blob).Value = cursor.Checksum.ToArray();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            var authority = new BaseSemanticActivationDefinitionMigrationAuthority
            {
                MigrationId = reader.GetString(0), MigrationVersion = reader.GetInt32(1),
                From = new() { Id = reader.GetString(2), Version = reader.GetInt32(3), Checksum = ((byte[])reader[4]).ToImmutableArray() },
                To = new() { Id = reader.GetString(5), Version = reader.GetInt32(6), Checksum = ((byte[])reader[7]).ToImmutableArray() },
                ExpectedLiveCount = reader.GetInt64(8), ExpectedRetiredCount = reader.GetInt64(9), ExpectedAbsenceCount = reader.GetInt64(10),
                OrderedNegativeAuthorityChecksum = ((byte[])reader[11]).ToImmutableArray(), PublicationGeneration = reader.GetInt64(12),
                ReceiptChecksum = ((byte[])reader[13]).ToImmutableArray(), Checksum = ((byte[])reader[14]).ToImmutableArray(),
            };
            if (!CryptographicOperations.FixedTimeEquals(authority.Checksum.AsSpan(),
                    BaseSemanticActivationMigrationAuthorityContract.Checksum(authority).AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            if (await reader.ReadAsync(token).ConfigureAwait(false)
                || !InstalledMigrationMatches(authority))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            result.Add(authority); cursor = authority.To;
            if (result.Count > _options.SemanticActivationMigrations.Length) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        return result.ToImmutable();
    }
    /// <inheritdoc />
    public async ValueTask<BaseResult<BaseSemanticActivationMaintenanceAuthority>> InspectMaintenanceAuthorityAsync(
        BaseSemanticActivationMaintenanceAuthorityRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ApplicationId) || string.IsNullOrWhiteSpace(request.LogicalStoreId)
            || string.IsNullOrWhiteSpace(request.Definition.Id) || request.Definition.Version <= 0 || request.Definition.Checksum.Length != 32
            || request.ApplicationId != _options.SemanticActivationApplicationId || request.LogicalStoreId != _options.StoreId
            || request.ProviderIncarnation.Length != 32
            || !CryptographicOperations.FixedTimeEquals(request.ProviderIncarnation.AsSpan(), ProviderIncarnation.AsSpan())
            || request.RestoreEpoch < 0 || request.SemanticAuthorityGeneration <= 0 || request.MaximumRows < 0
            || request.MaximumBytes < 0 || request.RuntimeRequestChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(request.RuntimeRequestChecksum.AsSpan(),
                BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(request).AsSpan()))
            return SemanticFailure<BaseSemanticActivationMaintenanceAuthority>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation, "The semantic activation request is invalid.");
        try
        {
            await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (!await DefinitionMatchesAsync(connection, request.Definition, cancellationToken, transaction).ConfigureAwait(false)
                || await ReadRestoreEpochAsync(connection, transaction, cancellationToken).ConfigureAwait(false) != request.RestoreEpoch
                || await ReadSemanticAuthorityGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false) != request.SemanticAuthorityGeneration)
                return SemanticFailure<BaseSemanticActivationMaintenanceAuthority>(OperationStatus.Conflict,
                    BaseSemanticActivationErrorCodes.GraphChanged, ErrorCategory.Conflict, "Semantic activation authority changed.");
            long[] counts = new long[3]; long rows = 0;
            using var definitionHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            definitionHash.AppendData("base.semanticActivation.definitionState.v1\0"u8);
            using var retiredHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            retiredHash.AppendData("base.semanticActivation.orderedRows.v1\0"u8);
            using var negativeHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            negativeHash.AppendData("base.semanticActivation.orderedRows.v1\0"u8);
            byte[] framedLength = new byte[4];
            long bytes = 0;
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"SELECT binding_id,key_digest,state,slot_generation,authority_json FROM {_names.SemanticActivationSlots} WHERE definition_id=$id ORDER BY binding_id,key_digest;";
                command.Parameters.AddWithValue("$id", request.Definition.Id);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    byte[] binding = (byte[])reader[0]; byte[] key = (byte[])reader[1];
                    int state = reader.GetInt32(2); long slotGeneration = reader.GetInt64(3);
                    byte[] authority = (byte[])reader[4];
                    if (state is < (int)BaseSemanticActivationSlotState.Live or > (int)BaseSemanticActivationSlotState.CompactedAbsent)
                        throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                    ValidateSemanticAuthorityBlob(request.Definition, request.SemanticAuthorityGeneration,
                        request.RestoreEpoch, binding, key, (BaseSemanticActivationSlotState)state,
                        slotGeneration, authority);
                    rows = checked(rows + 1); counts[state - 1] = checked(counts[state - 1] + 1);
                    bytes = checked(bytes + binding.Length + key.Length + 1L + sizeof(long) + authority.Length);
                    definitionHash.AppendData(binding); definitionHash.AppendData(key); definitionHash.AppendData([(byte)state]);
                    definitionHash.AppendData(ToInt64(slotGeneration)); definitionHash.AppendData(authority);
                    if (state == (int)BaseSemanticActivationSlotState.Retired)
                    {
                        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(framedLength, authority.Length);
                        retiredHash.AppendData(framedLength); retiredHash.AppendData(authority);
                    }
                    if (state is (int)BaseSemanticActivationSlotState.Retired or (int)BaseSemanticActivationSlotState.CompactedAbsent)
                    {
                        byte[] negative = HistoricalNegativeRow(binding, key, state, authority);
                        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(framedLength, negative.Length);
                        negativeHash.AppendData(framedLength); negativeHash.AppendData(negative);
                    }
                    if (rows > request.MaximumRows || bytes > request.MaximumBytes)
                        return SemanticFailure<BaseSemanticActivationMaintenanceAuthority>(OperationStatus.ValidationFailed,
                            BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation, "The semantic activation operation exceeded its installed limits.");
                }
            }
            var value = new BaseSemanticActivationMaintenanceAuthority
            {
                SemanticAuthorityGeneration = request.SemanticAuthorityGeneration,
                LiveCount = counts[0], RetiredCount = counts[1], AbsenceCount = counts[2],
                RetiredAuthorityChecksum = retiredHash.GetHashAndReset().ToImmutableArray(),
                DefinitionStateChecksum = definitionHash.GetHashAndReset().ToImmutableArray(),
                AbsenceAuthorityChecksum = negativeHash.GetHashAndReset().ToImmutableArray(),
                ExaminedRows = rows, CanonicalBytes = bytes, Checksum = [],
            };
            value = value with { Checksum = BaseSemanticActivationMaintenanceAuthorityContract.Checksum(request, value) };
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BaseSuccess<BaseSemanticActivationMaintenanceAuthority>(value, OperationStatus.Ok, null, null, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OverflowException)
        {
            return SemanticFailure<BaseSemanticActivationMaintenanceAuthority>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation, "The semantic activation operation exceeded its installed limits.");
        }
        catch (Exception exception) when (exception is SqliteException or InvalidDataException or JsonException)
        {
            return SemanticFailure<BaseSemanticActivationMaintenanceAuthority>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.Corrupt, ErrorCategory.Store, "Semantic activation authority requires operator attention.");
        }
    }

    /// <inheritdoc />
    public async ValueTask<BaseResult<BaseSemanticActivationProviderInspectionPage>> InspectAsync(
        BaseSemanticActivationProviderInspectionRequest request, CancellationToken cancellationToken)
    {
        if (!ValidInspection(request)) return SemanticFailure<BaseSemanticActivationProviderInspectionPage>(
            OperationStatus.ValidationFailed, BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation,
            "The semantic activation request is invalid.");
        try
        {
            await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await DefinitionMatchesAsync(connection, request.Definition, cancellationToken).ConfigureAwait(false))
                return SemanticFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.ValidationFailed,
                    BaseSemanticActivationErrorCodes.NotInstalled, ErrorCategory.Validation,
                    "The semantic activation contract is unavailable.");
            long generation = await ReadSemanticAuthorityGenerationAsync(connection, null, cancellationToken).ConfigureAwait(false);
            long currentRestoreEpoch;
            await using (SqliteCommand restore = connection.CreateCommand())
            {
                restore.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch';";
                currentRestoreEpoch = Convert.ToInt64(await restore.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (request.ApplicationId != _options.SemanticActivationApplicationId || request.LogicalStoreId != _options.StoreId
                    || request.ProviderIncarnation.Length != 32
                    || !CryptographicOperations.FixedTimeEquals(request.ProviderIncarnation.AsSpan(), ProviderIncarnation.AsSpan())
                    || request.RestoreEpoch != currentRestoreEpoch
                    || !CryptographicOperations.FixedTimeEquals(request.RuntimeRequestAuthorityChecksum.AsSpan(),
                        BaseSemanticActivationInspectionContract.RequestChecksum(request).AsSpan()))
                    return SemanticFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.ValidationFailed,
                        BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation,
                        "The semantic activation request is invalid.");
            }
            if (request.After is { } after && (after.DefinitionId != request.Definition.Id
                || after.CapturedAuthorityGeneration != generation || after.ScopeBindingId.Length != 32
                || after.RuntimeBoundaryChecksum.Length != 32
                || !CryptographicOperations.FixedTimeEquals(after.RuntimeBoundaryChecksum.AsSpan(),
                    BaseSemanticActivationInspectionContract.BoundaryChecksum(request, after.ScopeBindingId.AsSpan(), after.Key, generation).AsSpan())))
                return SemanticFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.ValidationFailed,
                    BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation,
                    "The semantic activation request is invalid.");

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandTimeout = TimeoutSeconds();
            command.CommandText = $"""
SELECT binding_id,key_digest,state,slot_generation,authority_json
FROM {_names.SemanticActivationSlots}
WHERE definition_id=$definition
  AND ($state IS NULL OR state=$state)
  AND ($afterBinding IS NULL OR binding_id>$afterBinding OR (binding_id=$afterBinding AND key_digest>$afterKey))
ORDER BY binding_id,key_digest
LIMIT $take;
""";
            command.Parameters.AddWithValue("$definition", request.Definition.Id);
            command.Parameters.AddWithValue("$state", request.State is null ? DBNull.Value : (int)request.State.Value);
            command.Parameters.Add("$afterBinding", SqliteType.Blob).Value = request.After is null ? DBNull.Value : request.After.ScopeBindingId.ToArray();
            command.Parameters.Add("$afterKey", SqliteType.Blob).Value = request.After is null ? DBNull.Value : KeyBytes(request.After.Key);
            command.Parameters.AddWithValue("$take", request.Take);
            var items = ImmutableArray.CreateBuilder<BaseSemanticActivationProviderInspectionItem>();
            long bytes = 0;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                byte[] binding = (byte[])reader[0]; byte[] keyBytes = (byte[])reader[1];
                var state = (BaseSemanticActivationSlotState)reader.GetInt32(2); long slotGeneration = reader.GetInt64(3);
                byte[] authority = (byte[])reader[4]; bytes = checked(bytes + binding.Length + keyBytes.Length + authority.Length + 52);
                ValidateSemanticAuthorityBlob(request.Definition, generation, currentRestoreEpoch, binding, keyBytes,
                    state, slotGeneration, authority);
                BaseSemanticActivationKeyDigest key = BaseSemanticActivationKeyDigest.Create(keyBytes);
                long? retirement = null; ImmutableArray<byte> stateChecksum;
                if (state == BaseSemanticActivationSlotState.Live)
                {
                    BaseSemanticActivationLiveAuthority value = JsonSerializer.Deserialize(authority,
                        HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)
                        ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                    stateChecksum = value.Checksum;
                }
                else if (state == BaseSemanticActivationSlotState.Retired)
                {
                    BaseSemanticActivationRetirementAuthority value = JsonSerializer.Deserialize(authority,
                        HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                        ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                    retirement = value.RetirementPosition; stateChecksum = value.Checksum;
                }
                else
                {
                    BaseSemanticActivationAbsenceAuthority value = JsonSerializer.Deserialize(authority,
                        HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)
                        ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                    retirement = value.RetirementPosition; stateChecksum = value.Checksum;
                }
                var boundary = new BaseSemanticActivationProviderInspectionBoundary
                {
                    DefinitionId = request.Definition.Id, ProviderIncarnation = request.ProviderIncarnation,
                    ScopeBindingId = binding.ToImmutableArray(), Key = key,
                    CapturedAuthorityGeneration = generation,
                    RuntimeBoundaryChecksum = BaseSemanticActivationInspectionContract.BoundaryChecksum(
                        request, binding, BaseSemanticActivationKeyDigest.Create(keyBytes), generation),
                };
                items.Add(new BaseSemanticActivationProviderInspectionItem
                {
                    State = state, SlotGeneration = slotGeneration, Boundary = boundary,
                    RetirementPosition = retirement, StateChecksum = stateChecksum.ToArray().ToImmutableArray(),
                    CanonicalStateAuthority = authority.ToImmutableArray(),
                });
            }
            ImmutableArray<BaseSemanticActivationProviderInspectionItem> pageItems = items.ToImmutable();
            var accounting = new BaseSemanticActivationAccounting
            {
                Operations = 0, ScopeDirectoryReads = 0, SlotReads = pageItems.Length,
                ActivationReads = 0, ReadIntervals = 1, IndexOperations = 1,
                KeyBytes = checked(pageItems.Length * 32L), ScopeDirectoryBytes = 0, ActivationBytes = 0,
                EvidenceBytes = bytes, ReceiptBytes = 0, TransientBytes = bytes,
                ActivationCreation = EmptyActivationAccounting(),
            };
            if (accounting.SlotReads > request.Limits.MaximumSlotReads || accounting.EvidenceBytes > request.Limits.MaximumEvidenceBytes
                || accounting.TransientBytes > request.Limits.MaximumTransientBytes)
                return SemanticFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.ValidationFailed,
                    BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation,
                    "The semantic activation operation exceeded its installed limits.");
            ImmutableArray<BaseAtomicReadIntervalEvidence> intervals = [InspectionInterval(request, pageItems)];
            BaseSemanticActivationProviderInspectionBoundary? next = pageItems.Length == request.Take ? pageItems[^1].Boundary : null;
            var page = new BaseSemanticActivationProviderInspectionPage
            {
                Items = pageItems, Next = next, CapturedAuthorityGeneration = generation,
                ReadIntervals = intervals, Accounting = accounting, Checksum = [],
            };
            page = page with { Checksum = BaseSemanticActivationInspectionContract.PageChecksum(request, page) };
            return BaseProviderResultContract.Ok(page);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is SqliteException or InvalidDataException or OverflowException)
        {
            return SemanticFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.Corrupt, ErrorCategory.Store,
                "Semantic activation authority requires operator attention.");
        }
    }

    private void ValidateSemanticAuthorityBlob(BaseSemanticActivationDefinitionKey expectedDefinition,
        long generation, long restoreEpoch, byte[] binding, byte[] keyBytes,
        BaseSemanticActivationSlotState state, long slotGeneration, byte[] authority)
    {
        BaseSemanticActivationStoreAuthority store;
        BaseSemanticActivationDefinitionKey definition;
        BaseSemanticActivationKeyDigest key;
        long authorityGeneration;
        if (state == BaseSemanticActivationSlotState.Live)
        {
            BaseSemanticActivationLiveAuthority value = JsonSerializer.Deserialize(authority,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            if (!CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(),
                    BaseSemanticActivationEvidenceContract.LiveChecksum(value).AsSpan())
                || !value.ScopeBinding.BindingId.AsSpan().SequenceEqual(binding))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            store = value.StoreAuthority; definition = new() { Id = value.Definition.Id, Version = value.Definition.Version, Checksum = value.Definition.Checksum };
            key = value.KeyDigest; authorityGeneration = value.SlotGeneration;
        }
        else if (state == BaseSemanticActivationSlotState.Retired)
        {
            BaseSemanticActivationRetirementAuthority value = JsonSerializer.Deserialize(authority,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            if (!CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(),
                    BaseSemanticActivationEvidenceContract.RetirementChecksum(value).AsSpan())
                || !value.ScopeBindingId.AsSpan().SequenceEqual(binding)
                || value.SubjectLifetime is { } lifetime
                    && !lifetime.ScopeBindingId.AsSpan().SequenceEqual(binding))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            store = value.StoreAuthority; definition = value.Definition; key = value.KeyDigest; authorityGeneration = value.SlotGeneration;
        }
        else if (state == BaseSemanticActivationSlotState.CompactedAbsent)
        {
            BaseSemanticActivationAbsenceAuthority value = JsonSerializer.Deserialize(authority,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            if (!CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(),
                    BaseSemanticActivationEvidenceContract.AbsenceChecksum(value).AsSpan())
                || !value.ScopeBindingId.AsSpan().SequenceEqual(binding))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            store = value.StoreAuthority; definition = new() { Id = value.Definition.Id, Version = value.Definition.Version, Checksum = value.Definition.Checksum };
            key = value.Key; authorityGeneration = value.FinalSlotGeneration;
        }
        else throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        Span<byte> actualKey = stackalloc byte[32]; key.CopyTo(actualKey);
        bool definitionMatches = DefinitionEqual(definition, expectedDefinition)
            || state is BaseSemanticActivationSlotState.Retired or BaseSemanticActivationSlotState.CompactedAbsent
                && HasInstalledSemanticMigrationPath(definition, expectedDefinition);
        if (authorityGeneration != slotGeneration || !CryptographicOperations.FixedTimeEquals(actualKey, keyBytes)
            || !definitionMatches)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        ValidateSemanticStore(store, generation, _options.SemanticActivationDefinitionSetChecksum,
            restoreEpoch, _schemaGeneration);
    }

    private bool HasInstalledSemanticMigrationPath(
        BaseSemanticActivationDefinitionKey from,
        BaseSemanticActivationDefinitionKey to)
    {
        BaseSemanticActivationDefinitionKey current = from;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (!DefinitionEqual(current, to))
        {
            string identity = $"{current.Id}\n{current.Version}\n{Convert.ToHexString(current.Checksum.AsSpan())}";
            if (!visited.Add(identity)) return false;
            BaseSemanticActivationMigrationDefinition? migration = _options.SemanticActivationMigrations
                .SingleOrDefault(value => DefinitionEqual(value.From, current));
            if (migration is null) return false;
            current = migration.To;
        }
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<BaseResult<BaseSemanticActivationMaintenanceResult>> ExecuteAsync(
        BaseSemanticActivationMaintenanceRequest request, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _semanticMutationQuarantined) != 0)
            return SemanticFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.Quarantined, ErrorCategory.Store,
                "Semantic activation authority is quarantined pending recovery.");
        if (!ValidMaintenance(request) || request.ProviderIncarnation.Length != 32
            || !CryptographicOperations.FixedTimeEquals(request.ProviderIncarnation.AsSpan(), ProviderIncarnation.AsSpan())) return SemanticFailure<BaseSemanticActivationMaintenanceResult>(
            OperationStatus.ValidationFailed, BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation,
            "The semantic activation request is invalid.");
        if (!MaintenanceWithinInstalledAuthority(request)) return SemanticFailure<BaseSemanticActivationMaintenanceResult>(
            OperationStatus.ValidationFailed, BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation,
            "The semantic activation operation exceeded its installed limits.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Limits.Deadline);
        CancellationToken operationToken = deadline.Token;
        try
        {
            await using SqliteConnection connection = await _connections.OpenAsync(operationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(operationToken).ConfigureAwait(false);
            byte[] fingerprint = MaintenanceFingerprint(request);
            BaseSemanticActivationMaintenanceResult? durableReceipt = await ReadSemanticMaintenanceReceiptAsync(
                connection, transaction, request.Identity, fingerprint, operationToken).ConfigureAwait(false);
            if (durableReceipt is not null)
            {
                await transaction.RollbackAsync(operationToken).ConfigureAwait(false);
                return BaseProviderResultContract.Ok(durableReceipt with
                {
                    Disposition = BaseSemanticActivationMaintenanceDisposition.Duplicate,
                    ReceiptDisposition = BaseMutationRequestDisposition.Duplicate,
                });
            }
            BaseSemanticActivationMaintenanceResult? duplicate = await ReadMaintenanceResultAsync(connection, transaction,
                request.Identity, fingerprint, operationToken).ConfigureAwait(false);
            if (duplicate is not null && duplicate.Disposition != BaseSemanticActivationMaintenanceDisposition.InProgress)
            {
                await transaction.RollbackAsync(operationToken).ConfigureAwait(false);
                return BaseProviderResultContract.Ok(duplicate with
                {
                    Disposition = BaseSemanticActivationMaintenanceDisposition.Duplicate,
                    ReceiptDisposition = BaseMutationRequestDisposition.Duplicate,
                });
            }
            if (!await DefinitionMatchesAsync(connection, request.Definition, operationToken, transaction).ConfigureAwait(false)
                || request.ExpectedSemanticAuthorityGeneration != await ReadSemanticAuthorityGenerationAsync(connection, transaction, operationToken).ConfigureAwait(false))
                return SemanticFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                    BaseSemanticActivationErrorCodes.GraphChanged, ErrorCategory.Conflict,
                    "The semantic activation contract changed.");
            BaseSemanticActivationMaintenanceResult result = request switch
            {
                BaseSemanticActivationCompactRequest or BaseSemanticActivationMigrateRequest =>
                    await ExecuteStagedSemanticMaintenanceAsync(connection, transaction, request, fingerprint, duplicate?.Checkpoint, operationToken).ConfigureAwait(false),
                BaseSemanticActivationRemoveRequest removeRequest => await RemoveSemanticAsync(connection, transaction, removeRequest, operationToken).ConfigureAwait(false),
                _ => throw new InvalidDataException(BaseSemanticActivationErrorCodes.ProviderContractInvalid),
            };
            if (result.Disposition == BaseSemanticActivationMaintenanceDisposition.Completed
                && result.ResultingAuthorityGeneration != result.PreviousAuthorityGeneration)
                await PublishSemanticAuthorityGenerationAsync(connection, transaction,
                    result.PreviousAuthorityGeneration, result.ResultingAuthorityGeneration, operationToken).ConfigureAwait(false);
            if (result.Disposition == BaseSemanticActivationMaintenanceDisposition.Completed
                && request is BaseSemanticActivationMigrateRequest or BaseSemanticActivationRemoveRequest)
                await PublishSemanticDefinitionSetAsync(connection, transaction, operationToken).ConfigureAwait(false);
            await StoreMaintenanceResultAsync(connection, transaction, request, fingerprint, result, operationToken).ConfigureAwait(false);
            if (result.Disposition == BaseSemanticActivationMaintenanceDisposition.Completed)
                await InsertSemanticMaintenanceReceiptAsync(connection, transaction, request.Identity,
                    fingerprint, result, operationToken).ConfigureAwait(false);
            if (result.Disposition == BaseSemanticActivationMaintenanceDisposition.Completed
                && request is BaseSemanticActivationRemoveRequest completedRemoval)
                await PersistRemovedDefinitionAuthorityAsync(connection, transaction, completedRemoval, fingerprint, result, operationToken).ConfigureAwait(false);
            await AwaitSemanticAdministrationPhaseAsync("semanticMaintenanceBeforePublication", operationToken).ConfigureAwait(false);
            await transaction.CommitAsync(operationToken).ConfigureAwait(false);
            return BaseProviderResultContract.Ok(result, OperationStatus.Updated);
        }
        catch (SemanticMaintenanceBlockedException exception)
        {
            return SemanticFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                exception.Code, ErrorCategory.Conflict, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return SemanticFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.MaintenanceTimeout, ErrorCategory.Store,
                "Semantic activation maintenance did not complete in time.");
        }
        catch (Exception exception) when (exception is SqliteException or InvalidDataException or OverflowException)
        {
            return SemanticFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.MaintenanceIndeterminate, ErrorCategory.Store,
                "Semantic activation maintenance requires reconciliation.");
        }
    }

    /// <inheritdoc />
    public async ValueTask<BaseResult<BaseSemanticActivationMaintenanceResult>> ResolveAsync(
        BaseSemanticActivationMaintenanceResolutionRequest request, CancellationToken cancellationToken)
    {
        if (request.Identity is null || !ValidDefinition(request.Definition) || string.IsNullOrWhiteSpace(request.MaintenanceId)
            || request.ProviderIncarnation.Length != 32
            || !CryptographicOperations.FixedTimeEquals(request.ProviderIncarnation.AsSpan(), ProviderIncarnation.AsSpan())
            || request.RequestFingerprint.Length != 32 || request.Deadline <= TimeSpan.Zero)
            return SemanticFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation,
                "The semantic activation request is invalid.");
        if (!string.Equals(request.MaintenanceId, Convert.ToHexStringLower(request.RequestFingerprint.AsSpan()), StringComparison.Ordinal))
            return SemanticFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation,
                "The semantic activation request is invalid.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Deadline);
        CancellationToken operationToken = deadline.Token;
        await using SqliteConnection connection = await _connections.OpenAsync(operationToken).ConfigureAwait(false);
        await using (SqliteCommand authority = connection.CreateCommand())
        {
            authority.CommandText = $"SELECT COUNT(*) FROM {_names.SemanticActivationMaintenance} WHERE request_scope=$scope AND request_operation=$operation AND request_key=$key AND maintenance_id=$maintenance AND definition_id=$definition AND definition_version=$version AND definition_checksum=$checksum;";
            authority.Parameters.AddWithValue("$scope", request.Identity.Scope); authority.Parameters.AddWithValue("$operation", request.Identity.Operation);
            authority.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey); authority.Parameters.AddWithValue("$maintenance", request.MaintenanceId);
            authority.Parameters.AddWithValue("$definition", request.Definition.Id); authority.Parameters.AddWithValue("$version", request.Definition.Version);
            authority.Parameters.Add("$checksum", SqliteType.Blob).Value = request.Definition.Checksum.ToArray();
            if (Convert.ToInt64(await authority.ExecuteScalarAsync(operationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
                return SemanticFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                    BaseSemanticActivationErrorCodes.MaintenanceIndeterminate, ErrorCategory.Store,
                    "Semantic activation maintenance requires reconciliation.");
        }
        BaseSemanticActivationMaintenanceResult? receipt = await ReadSemanticMaintenanceReceiptAsync(connection, null,
            request.Identity, request.RequestFingerprint.ToArray(), operationToken).ConfigureAwait(false);
        if (receipt is not null) return BaseProviderResultContract.Ok(receipt with
        {
            Disposition = BaseSemanticActivationMaintenanceDisposition.Duplicate,
            ReceiptDisposition = BaseMutationRequestDisposition.Duplicate,
        });
        BaseSemanticActivationMaintenanceResult? result = await ReadMaintenanceResultAsync(connection, null,
            request.Identity, request.RequestFingerprint.ToArray(), operationToken).ConfigureAwait(false);
        return result is null
            ? SemanticFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.MaintenanceIndeterminate, ErrorCategory.Store,
                "Semantic activation maintenance requires reconciliation.")
            : result.Disposition == BaseSemanticActivationMaintenanceDisposition.InProgress
                ? BaseProviderResultContract.Ok(result)
                : BaseProviderResultContract.Ok(result with
            {
                Disposition = BaseSemanticActivationMaintenanceDisposition.Duplicate,
                ReceiptDisposition = BaseMutationRequestDisposition.Duplicate,
            });
    }

    private async ValueTask<BaseSemanticActivationMaintenanceResult?> ReadSemanticMaintenanceReceiptAsync(
        SqliteConnection connection, SqliteTransaction? transaction, BaseMutationRequestIdentity identity,
        byte[] structuralDigest, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT fingerprint,structural_digest,result_json,expires_at FROM {_names.OperationReceipts} WHERE scope=$scope AND operation=$operation AND idempotency_key=$key;";
        command.Parameters.AddWithValue("$scope", identity.Scope); command.Parameters.AddWithValue("$operation", identity.Operation);
        command.Parameters.AddWithValue("$key", identity.IdempotencyKey);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        if (!CryptographicOperations.FixedTimeEquals((byte[])reader[0], identity.Fingerprint.ToArray())
            || !CryptographicOperations.FixedTimeEquals((byte[])reader[1], structuralDigest))
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.FingerprintConflict,
                "The semantic identity was used with different activation semantics.");
        DateTimeOffset expires = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        if (expires <= _timeProvider.GetUtcNow()) return null;
        BaseAtomicReceiptWire? wire = JsonSerializer.Deserialize((byte[])reader[2], HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        BaseAtomicReceiptResult? receipt = wire?.Materialize();
        if (receipt?.Kind != BaseAtomicReceiptResultKind.SemanticActivationMaintenance
            || receipt.SemanticActivationMaintenance is null)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        return receipt.SemanticActivationMaintenance;
    }

    private async ValueTask InsertSemanticMaintenanceReceiptAsync(SqliteConnection connection, SqliteTransaction transaction,
        BaseMutationRequestIdentity identity, byte[] structuralDigest, BaseSemanticActivationMaintenanceResult result,
        CancellationToken token)
    {
        var receipt = new BaseAtomicReceiptResult
        {
            Kind = BaseAtomicReceiptResultKind.SemanticActivationMaintenance,
            Mutations = [], SemanticActivationMaintenance = result,
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(BaseAtomicReceiptWire.From(receipt),
            HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        if (bytes.Length > 16_384) throw new InvalidDataException(BaseSemanticActivationErrorCodes.BudgetExceeded);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {_names.OperationReceipts}(scope,operation,idempotency_key,fingerprint,structural_digest,result_json,result_format_version,schema_generation,store_instance_id,committed_at,expires_at) VALUES($scope,$operation,$key,$fingerprint,$structural,$result,2,$generation,$store,$committed,$expires);";
        command.Parameters.AddWithValue("$scope", identity.Scope); command.Parameters.AddWithValue("$operation", identity.Operation);
        command.Parameters.AddWithValue("$key", identity.IdempotencyKey); command.Parameters.Add("$fingerprint", SqliteType.Blob).Value = identity.Fingerprint.ToArray();
        command.Parameters.Add("$structural", SqliteType.Blob).Value = structuralDigest; command.Parameters.Add("$result", SqliteType.Blob).Value = bytes;
        command.Parameters.AddWithValue("$generation", _schemaGeneration); command.Parameters.AddWithValue("$store", CurrentStoreInstanceId);
        command.Parameters.AddWithValue("$committed", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$expires", now.AddDays(30).ToString("O", CultureInfo.InvariantCulture));
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private static bool ValidInspection(BaseSemanticActivationProviderInspectionRequest request) =>
        !string.IsNullOrWhiteSpace(request.ApplicationId) && !string.IsNullOrWhiteSpace(request.LogicalStoreId)
        && request.ProviderIncarnation.Length == 32 && request.RestoreEpoch >= 0 && ValidDefinition(request.Definition) && request.Take is >= 1 and <= 256
        && request.RuntimeRequestAuthorityChecksum.Length == 32
        && (request.State is null || Enum.IsDefined(request.State.Value));

    private static bool ValidMaintenance(BaseSemanticActivationMaintenanceRequest request) =>
        request.Identity is not null && request.ProviderIncarnation.Length == 32 && ValidDefinition(request.Definition)
        && request.ExpectedSemanticAuthorityGeneration > 0 && request.Limits.PageSize is >= 1 and <= 256
        && request.Limits.MaximumPages > 0 && request.Limits.MaximumRows > 0
        && request.Limits.MaximumBytes > 0 && request.Limits.Deadline > TimeSpan.Zero;

    private bool MaintenanceWithinInstalledAuthority(BaseSemanticActivationMaintenanceRequest request)
    {
        BaseSemanticActivationCapability capability = _semanticActivationCapability;
        BaseSemanticActivationKeyDefinition? definition = request switch
        {
            BaseSemanticActivationMigrateRequest migrate => _options.SemanticActivations.SingleOrDefault(value =>
                DefinitionEqual(new BaseSemanticActivationDefinitionKey { Id = value.Id, Version = value.Version, Checksum = value.Checksum }, migrate.Migration.To)),
            BaseSemanticActivationRemoveRequest remove => _options.SemanticActivationRemovals.SingleOrDefault(value =>
                DefinitionEqual(new BaseSemanticActivationDefinitionKey { Id = value.From.Id, Version = value.From.Version, Checksum = value.From.Checksum }, remove.Definition))?.From,
            _ => _options.SemanticActivations.SingleOrDefault(value => value.Id == request.Definition.Id
                && value.Version == request.Definition.Version && value.Checksum.AsSpan().SequenceEqual(request.Definition.Checksum.AsSpan())),
        };
        long installedRows = _options.SemanticActivations.Concat(_options.SemanticActivationRemovals.Select(static value => value.From))
            .GroupBy(static value => (value.Id, value.Version)).Select(static group => group.First())
            .Aggregate(0L, static (sum, value) => checked(sum + value.Limits.MaximumLiveSlots
                + value.Limits.MaximumRetiredSlots + value.Limits.MaximumAbsenceMarkers));
        if (definition is null || !capability.Supported || request.Limits.PageSize > capability.MaximumMaintenancePageSize
            || request.Limits.Deadline > capability.Deadlines.MaintenanceTimeout
            || request.Limits.Deadline > definition.Limits.Deadlines.MaintenanceTimeout
            || request.Limits.MaximumRows > installedRows
            || request.Limits.MaximumBytes > capability.MaximumTransientBytes)
            return false;
        // Maintenance can consist of multiple independently paged phases (for
        // example, compaction staging followed by authority rebinding). Every
        // non-empty page consumes at least one row from the aggregate row budget.
        return request.Limits.MaximumPages <= request.Limits.MaximumRows;
    }

    private static bool ValidDefinition(BaseSemanticActivationDefinitionKey value) =>
        !string.IsNullOrWhiteSpace(value.Id) && value.Version > 0 && value.Checksum.Length == 32;

    private async ValueTask<bool> DefinitionMatchesAsync(SqliteConnection connection,
        BaseSemanticActivationDefinitionKey definition, CancellationToken token, SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT definition_checksum FROM {_names.SemanticActivationDefinitions} WHERE definition_id=$id AND definition_version=$version;";
        command.Parameters.AddWithValue("$id", definition.Id); command.Parameters.AddWithValue("$version", definition.Version);
        object? value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        return value is byte[] checksum && CryptographicOperations.FixedTimeEquals(checksum, definition.Checksum.AsSpan());
    }

    private async ValueTask<long> ReadSemanticAuthorityGenerationAsync(SqliteConnection connection,
        SqliteTransaction? transaction, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='semantic_activation_authority_generation';";
        object? value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        long generation = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        if (generation <= 0) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        return generation;
    }

    private async ValueTask PublishSemanticAuthorityGenerationAsync(SqliteConnection connection, SqliteTransaction transaction,
        long expected, long resulting, CancellationToken token)
    {
        if (expected <= 0 || resulting <= expected) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.ProviderState} SET value=$resulting WHERE key='semantic_activation_authority_generation' AND CAST(value AS INTEGER)=$expected;";
        command.Parameters.AddWithValue("$resulting", resulting); command.Parameters.AddWithValue("$expected", expected);
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.GraphChanged,
                "The semantic activation contract changed.");
    }

    private async ValueTask PublishSemanticDefinitionSetAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.ProviderState} SET value=$checksum WHERE key='semantic_activation_definition_set_checksum';";
        command.Parameters.AddWithValue("$checksum", Convert.ToHexStringLower(_options.SemanticActivationDefinitionSetChecksum));
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private static byte[] KeyBytes(BaseSemanticActivationKeyDigest key) { byte[] value = new byte[32]; key.CopyTo(value); return value; }

    private async ValueTask<BaseSemanticActivationMaintenanceResult> RemoveSemanticAsync(SqliteConnection connection,
        SqliteTransaction transaction, BaseSemanticActivationRemoveRequest request, CancellationToken token)
    {
        BaseSemanticActivationRemovalAuthority installedRemoval = _options.SemanticActivationRemovals.SingleOrDefault(value =>
            value.From.Id == request.Definition.Id && value.From.Version == request.Definition.Version
            && CryptographicOperations.FixedTimeEquals(value.From.Checksum.AsSpan(), request.Definition.Checksum.AsSpan()))
            ?? throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.RemovalBlocked,
                "The semantic activation definition cannot be removed.");
        if (!CryptographicOperations.FixedTimeEquals(installedRemoval.Checksum.AsSpan(), request.RemovalAuthority.Checksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(installedRemoval.ResultingDefinitionSetChecksum.AsSpan(),
                _options.SemanticActivationDefinitionSetChecksum))
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.RemovalBlocked,
                "The semantic activation definition cannot be removed.");
        long[] counts = await StateCountsAsync(connection, transaction, request.Definition.Id, token).ConfigureAwait(false);
        byte[] checksum = await DefinitionStateChecksumAsync(connection, transaction, request.Definition.Id, token).ConfigureAwait(false);
        byte[] absenceChecksum = await NegativeAuthorityChecksumAsync(connection, transaction, request.Definition.Id, token).ConfigureAwait(false);
        if (counts[0] != request.ExpectedLiveCount || counts[1] != request.ExpectedRetiredCount || counts[2] != request.ExpectedAbsenceCount
            || counts[0] != 0 || counts[1] != 0
            || !CryptographicOperations.FixedTimeEquals(checksum, request.ExpectedDefinitionStateChecksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(absenceChecksum, request.ExpectedAbsenceAuthorityChecksum.AsSpan())
            || !await RemovalDependenciesSatisfiedAsync(connection, transaction, request.Definition.Id, token).ConfigureAwait(false))
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.RemovalBlocked,
                "The semantic activation definition cannot be removed.");
        await using SqliteCommand remove = connection.CreateCommand(); remove.Transaction = transaction;
        remove.CommandText = $"UPDATE {_names.SemanticActivationDefinitions} SET execution_enabled=0 WHERE definition_id=$id AND definition_version=$version AND definition_checksum=$checksum AND execution_enabled=0;";
        remove.Parameters.AddWithValue("$id", request.Definition.Id); remove.Parameters.AddWithValue("$version", request.Definition.Version);
        remove.Parameters.Add("$checksum", SqliteType.Blob).Value = request.Definition.Checksum.ToArray();
        if (await remove.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.RemovalBlocked,
                "The semantic activation definition cannot be removed.");
        await using (SqliteCommand history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = $"INSERT INTO {_names.SemanticActivationRemovedDefinitionHistory}(definition_id,definition_version,binding_id,key_digest,authority_json) SELECT definition_id,$version,binding_id,key_digest,authority_json FROM {_names.SemanticActivationSlots} WHERE definition_id=$id AND state=3 ORDER BY binding_id,key_digest;";
            history.Parameters.AddWithValue("$version", request.Definition.Version); history.Parameters.AddWithValue("$id", request.Definition.Id);
            if (await history.ExecuteNonQueryAsync(token).ConfigureAwait(false) != counts[2])
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        (long reboundRows, long reboundBytes) = await RebindAllSemanticAuthoritiesAsync(
            connection, transaction, request, null, 0, 0, token).ConfigureAwait(false);
        return MaintenanceResult(request.ProviderIncarnation, request.ExpectedSemanticAuthorityGeneration,
            checked(request.ExpectedSemanticAuthorityGeneration + 1), reboundRows, reboundRows, reboundBytes, checksum);
    }

    private async ValueTask<bool> RemovalDependenciesSatisfiedAsync(SqliteConnection connection, SqliteTransaction transaction,
        string definitionId, CancellationToken token)
    {
        // Negative recovery floors must cover every retained absence marker. They remain non-prunable after
        // executable graph authority is removed and are the only state this operation is permitted to retain.
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"""
SELECT
 (SELECT COUNT(*) FROM {_names.SemanticActivationSlots} WHERE definition_id=$id AND state=3),
 (SELECT COUNT(*) FROM {_names.SemanticActivationRecoveryFloors} WHERE definition_id=$id AND state=3),
 (SELECT COUNT(*) FROM {_names.SemanticActivationRewriteStage} WHERE definition_id=$id),
 (SELECT COUNT(*) FROM {_names.SemanticActivationMaintenance} WHERE definition_id=$id AND disposition IN (3,5)),
 (SELECT COUNT(*) FROM {_names.SemanticActivationMigrations} WHERE from_definition_id=$id OR to_definition_id=$id),
 (SELECT COUNT(*) FROM {_names.SemanticActivationSlots} s JOIN {_names.Activations} a ON a.activation_id=s.activation_id WHERE s.definition_id=$id),
 (SELECT COUNT(*) FROM {_names.SemanticActivationRecoveryFloors} f LEFT JOIN {_names.SemanticActivationSlots} s ON s.definition_id=f.definition_id AND s.binding_id=f.binding_id AND s.key_digest=f.key_digest WHERE f.definition_id=$id AND (s.state<>3 OR s.state IS NULL));
""";
        command.Parameters.AddWithValue("$id", definitionId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false)
            && reader.GetInt64(0) == reader.GetInt64(1) && reader.GetInt64(2) == 0 && reader.GetInt64(3) == 0
            && reader.GetInt64(4) == 0 && reader.GetInt64(5) == 0 && reader.GetInt64(6) == 0;
    }

    private async ValueTask<bool> TerminalLifetimeExistsAsync(SqliteConnection connection, SqliteTransaction transaction,
        byte[] bindingId, BaseSemanticActivationRetirementAuthority retired, CancellationToken token)
    {
        if (retired.SubjectLifetime is null) return false;
        BaseSemanticActivationSubjectLifetimeBinding lifetime = retired.SubjectLifetime;
        BaseSemanticActivationScopeBinding binding;
        await using (SqliteCommand scope = connection.CreateCommand())
        {
            scope.Transaction = transaction; scope.CommandText = $"SELECT binding_json FROM {_names.SemanticActivationScopes} WHERE binding_id=$binding;";
            scope.Parameters.Add("$binding", SqliteType.Blob).Value = bindingId;
            if (await scope.ExecuteScalarAsync(token).ConfigureAwait(false) is not byte[] json
                || JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding) is not { } value) return false;
            binding = value;
        }
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT 1 FROM {_names.SubjectTerminalLifetimes} WHERE scope_kind=$kind AND scope_index_digest=$scope AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND retired_authority_epoch=$epoch AND retired_incarnation=$incarnation AND retired_lifetime_generation=$generation AND retired_position>=$position LIMIT 1;";
        command.Parameters.AddWithValue("$kind", (int)binding.Kind); command.Parameters.Add("$scope", SqliteType.Blob).Value = binding.SeekDigest.ToArray();
        command.Parameters.AddWithValue("$contract", lifetime.ContractId); command.Parameters.AddWithValue("$version", lifetime.ContractVersion);
        command.Parameters.AddWithValue("$subject", lifetime.SubjectId.Value); command.Parameters.Add("$epoch", SqliteType.Blob).Value = lifetime.AuthorityEpoch.ToArray();
        command.Parameters.Add("$incarnation", SqliteType.Blob).Value = lifetime.Incarnation.ToArray();
        command.Parameters.AddWithValue("$generation", lifetime.Incarnation.LifetimeGeneration); command.Parameters.AddWithValue("$position", retired.RetirementPosition);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not null;
    }

    private async ValueTask<bool> TerminalActivationAndFloorMatchAsync(SqliteConnection connection, SqliteTransaction transaction,
        string definitionId, byte[] binding, byte[] key, BaseSemanticActivationRetirementAuthority retired, CancellationToken token)
    {
        if (retired.SubjectLifetime is null) return false;
        BaseSemanticActivationKeyDefinition semanticDefinition = _options.SemanticActivations.Single(value =>
            value.Id == definitionId
            && value.Version == retired.Definition.Version
            && CryptographicOperations.FixedTimeEquals(
                value.Checksum.AsSpan(), retired.Definition.Checksum.AsSpan()));
        await using (SqliteCommand activation = connection.CreateCommand())
        {
            activation.Transaction = transaction;
            activation.CommandText = $"SELECT definition_id,definition_version,definition_checksum,terminal_generation,terminal_control_checksum,terminal_receipt_checksum,occurrence_checksum,result_checksum,prune_authority_generation,application_id,logical_store_id,store_instance_id,restore_epoch,publication_authority_checksum,authority_checksum FROM {_names.ActivationPruneFloors} WHERE activation_id=$id;";
            activation.Parameters.AddWithValue("$id", retired.ActivationId);
            await using SqliteDataReader reader = await activation.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)
                || reader.GetString(0) != semanticDefinition.Activation.Id || reader.GetInt32(1) != semanticDefinition.Activation.Version
                || !CryptographicOperations.FixedTimeEquals((byte[])reader[2], semanticDefinition.Activation.Checksum.AsSpan())
                ) return false;
            var evidence = new BaseActivationPruneEvidence
            {
                ActivationId = retired.ActivationId,
                Definition = new BaseActivationDefinitionKey { Id = reader.GetString(0), Version = reader.GetInt32(1), Checksum = ((byte[])reader[2]).ToImmutableArray() },
                TerminalGeneration = reader.GetInt64(3), TerminalControlChecksum = ((byte[])reader[4]).ToImmutableArray(),
                TerminalReceiptChecksum = ((byte[])reader[5]).ToImmutableArray(),
                OccurrenceChecksum = reader.IsDBNull(6) ? null : ((byte[])reader[6]).ToImmutableArray(),
                ResultChecksum = reader.IsDBNull(7) ? null : ((byte[])reader[7]).ToImmutableArray(),
                PruneAuthorityGeneration = reader.GetInt64(8), ApplicationId = reader.GetString(9), LogicalStoreId = reader.GetString(10),
                StoreInstanceId = reader.GetString(11), RestoreEpoch = reader.GetInt64(12),
                PublicationAuthorityChecksum = ((byte[])reader[13]).ToImmutableArray(), Checksum = ((byte[])reader[14]).ToImmutableArray(),
            };
            if (!BaseActivationPruneEvidenceContract.IsValid(evidence)
                || !BaseSemanticActivationEvidenceContract.PruneEvidenceDominatesRetirement(evidence, retired)) return false;
            await reader.DisposeAsync().ConfigureAwait(false);
            await using SqliteCommand store = connection.CreateCommand(); store.Transaction = transaction;
            store.CommandText = $"SELECT COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch'),0);";
            long currentRestoreEpoch = Convert.ToInt64(await store.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (evidence.ApplicationId != _options.SemanticActivationApplicationId || evidence.LogicalStoreId != _options.StoreId
                || evidence.StoreInstanceId != CurrentStoreInstanceId || evidence.RestoreEpoch != currentRestoreEpoch) return false;
            byte[] publication = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"base.activation.publicationAuthority.v1\0{evidence.ApplicationId}\n{evidence.LogicalStoreId}\n{evidence.StoreInstanceId}\n{evidence.RestoreEpoch}\n{evidence.PruneAuthorityGeneration}"));
            if (!CryptographicOperations.FixedTimeEquals(publication, evidence.PublicationAuthorityChecksum.AsSpan())) return false;
        }
        if (!await LifecycleProjectionFloorSatisfiedAsync(connection, transaction, binding, retired.SubjectLifetime, token).ConfigureAwait(false))
            return false;
        await using SqliteCommand floor = connection.CreateCommand(); floor.Transaction = transaction;
        floor.CommandText = $"SELECT state,slot_generation,authority_json,receipt_fingerprint,receipt_structural_digest,receipt_result_json FROM {_names.SemanticActivationRecoveryFloors} WHERE definition_id=$definition AND binding_id=$binding AND key_digest=$key;";
        floor.Parameters.AddWithValue("$definition", definitionId); floor.Parameters.Add("$binding", SqliteType.Blob).Value = binding; floor.Parameters.Add("$key", SqliteType.Blob).Value = key;
        await using SqliteDataReader floorReader = await floor.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await floorReader.ReadAsync(token).ConfigureAwait(false) || floorReader.GetInt32(0) != 2
            || floorReader.GetInt64(1) != retired.SlotGeneration || floorReader.IsDBNull(3) || floorReader.IsDBNull(4) || floorReader.IsDBNull(5)) return false;
        byte[] authority = (byte[])floorReader[2];
        BaseSemanticActivationRetirementAuthority? stored = JsonSerializer.Deserialize(authority,
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority);
        if (stored is null || !CryptographicOperations.FixedTimeEquals(stored.Checksum.AsSpan(), retired.Checksum.AsSpan())) return false;
        byte[] receiptJson = (byte[])floorReader[5];
        BaseAtomicReceiptWire? receipt = JsonSerializer.Deserialize(receiptJson, HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        BaseSemanticActivationReceiptEvidence? semantic = receipt?.Materialize().ModuleMutation?.SemanticActivation;
        bool receiptMatches = semantic is { Operation: BaseSemanticActivationOperationKind.Retire, State: BaseSemanticActivationSlotState.Retired }
            && semantic.DefinitionId == definitionId && KeyBytes(semantic.Key).AsSpan().SequenceEqual(key)
            && semantic.SlotGeneration == retired.SlotGeneration
            && CryptographicOperations.FixedTimeEquals(semantic.SlotChecksum.AsSpan(), retired.Checksum.AsSpan());
        return receiptMatches;
    }

    private async ValueTask<bool> LifecycleProjectionFloorSatisfiedAsync(SqliteConnection connection, SqliteTransaction transaction,
        byte[] bindingId, BaseSemanticActivationSubjectLifetimeBinding lifetime, CancellationToken token)
    {
        BaseSemanticActivationScopeBinding binding;
        await using (SqliteCommand scope = connection.CreateCommand())
        {
            scope.Transaction = transaction;
            scope.CommandText = $"SELECT binding_json FROM {_names.SemanticActivationScopes} WHERE binding_id=$binding;";
            scope.Parameters.Add("$binding", SqliteType.Blob).Value = bindingId;
            if (await scope.ExecuteScalarAsync(token).ConfigureAwait(false) is not byte[] json
                || JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding) is not { } value)
                return false;
            binding = value;
        }
        await using SqliteCommand pending = connection.CreateCommand(); pending.Transaction = transaction;
        pending.CommandText = $"""
SELECT COUNT(*)
FROM {_names.SubjectLifecycleMemberships} m
LEFT JOIN {_names.SubjectLifecycleCheckpoints} c
  ON c.consumer_id=m.consumer_id AND c.consumer_version=m.consumer_version
 AND c.scope_kind=m.scope_kind AND c.scope_index_digest=m.scope_index_digest
WHERE m.contract_id=$contract AND m.contract_version=$version AND m.scope_kind=$scopeKind
  AND m.scope_index_digest=$scope AND m.subject_id=$subject AND m.authority_epoch=$epoch AND m.incarnation=$incarnation
  AND (c.consumer_id IS NULL OR NOT (c.state=1 OR (c.through_position,c.through_subject_id,c.through_authority_epoch,c.through_incarnation,c.through_sequence)
      >=(m.commit_position,m.subject_id,m.authority_epoch,m.incarnation,m.subject_sequence)));
""";
        pending.Parameters.AddWithValue("$contract", lifetime.ContractId); pending.Parameters.AddWithValue("$version", lifetime.ContractVersion);
        pending.Parameters.AddWithValue("$scopeKind", (int)binding.Kind); pending.Parameters.Add("$scope", SqliteType.Blob).Value = binding.SeekDigest.ToArray();
        pending.Parameters.AddWithValue("$subject", lifetime.SubjectId.Value); pending.Parameters.Add("$epoch", SqliteType.Blob).Value = lifetime.AuthorityEpoch.ToArray();
        pending.Parameters.Add("$incarnation", SqliteType.Blob).Value = lifetime.Incarnation.ToArray();
        return Convert.ToInt64(await pending.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) == 0;
    }

    private async ValueTask UpsertRecoveryFloorAsync(SqliteConnection connection, SqliteTransaction transaction,
        string definitionId, byte[] binding, byte[] key, int state, long generation, byte[] authority, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {_names.SemanticActivationRecoveryFloors}(definition_id,binding_id,key_digest,state,slot_generation,authority_json) VALUES($id,$binding,$key,$state,$generation,$authority) ON CONFLICT(definition_id,binding_id,key_digest) DO UPDATE SET state=excluded.state,slot_generation=excluded.slot_generation,authority_json=excluded.authority_json,receipt_scope=NULL,receipt_operation=NULL,receipt_key=NULL,receipt_fingerprint=NULL,receipt_structural_digest=NULL,receipt_result_json=NULL,receipt_authority_checksum=NULL,receipt_slot_authority_json=NULL WHERE excluded.slot_generation>=slot_generation;";
        command.Parameters.AddWithValue("$id", definitionId); command.Parameters.Add("$binding", SqliteType.Blob).Value = binding;
        command.Parameters.Add("$key", SqliteType.Blob).Value = key; command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$generation", generation); command.Parameters.Add("$authority", SqliteType.Blob).Value = authority;
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private async ValueTask<BaseSemanticActivationKeyDefinition> ReadDefinitionAsync(SqliteConnection connection,
        SqliteTransaction transaction, BaseSemanticActivationDefinitionKey key, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT definition_json FROM {_names.SemanticActivationDefinitions} WHERE definition_id=$id AND definition_version=$version AND definition_checksum=$checksum;";
        command.Parameters.AddWithValue("$id", key.Id); command.Parameters.AddWithValue("$version", key.Version);
        command.Parameters.Add("$checksum", SqliteType.Blob).Value = key.Checksum.ToArray();
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not byte[] json)
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.MigrationBlocked,
                "Semantic activation migration requirements are not satisfied.");
        return JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationKeyDefinition)
            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private static bool Migratable(BaseSemanticActivationKeyDefinition from, BaseSemanticActivationKeyDefinition to) =>
        from.Id == to.Id && from.OwningApplicationId == to.OwningApplicationId && from.OwningModuleId == to.OwningModuleId
        && from.ScopeKind == to.ScopeKind && from.RequestTypeId == to.RequestTypeId
        && from.RequestSerializerChecksum.AsSpan().SequenceEqual(to.RequestSerializerChecksum.AsSpan())
        && from.KeyExpressionChecksum.AsSpan().SequenceEqual(to.KeyExpressionChecksum.AsSpan())
        && from.Activation.Id == to.Activation.Id && from.Activation.Version == to.Activation.Version
        && from.Activation.Checksum.AsSpan().SequenceEqual(to.Activation.Checksum.AsSpan())
        && JsonSerializer.SerializeToUtf8Bytes(from.Compaction, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationCompactionContract)
            .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(to.Compaction, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationCompactionContract));

    private BaseSemanticActivationDefinitionIdentity DefinitionIdentity(BaseSemanticActivationDefinitionKey key)
    {
        BaseSemanticActivationKeyDefinition definition = _options.SemanticActivations.Single(value => value.Id == key.Id
            && value.Version == key.Version && value.Checksum.AsSpan().SequenceEqual(key.Checksum.AsSpan()));
        return new BaseSemanticActivationDefinitionIdentity
        {
            Id = key.Id, Version = key.Version, Checksum = key.Checksum.ToArray().ToImmutableArray(),
            OwnerGeneration = _options.SemanticActivationOwnerGeneration, OwningModuleId = definition.OwningModuleId,
            RetirementOperation = definition.RetirementOperation with { },
        };
    }

    private static void ValidateMaintenanceWork(BaseSemanticActivationMaintenanceLimits limits, long rows, long bytes)
    {
        if (rows > limits.MaximumRows || bytes > limits.MaximumBytes
            || rows > checked((long)limits.PageSize * limits.MaximumPages))
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.BudgetExceeded,
                "The semantic activation operation exceeded its installed limits.");
    }

    private async ValueTask<long[]> StateCountsAsync(SqliteConnection connection, SqliteTransaction transaction,
        string definitionId, CancellationToken token)
    {
        long[] counts = new long[3]; await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT state,COUNT(*) FROM {_names.SemanticActivationSlots} WHERE definition_id=$id GROUP BY state;";
        command.Parameters.AddWithValue("$id", definitionId); await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) counts[reader.GetInt32(0) - 1] = reader.GetInt64(1);
        return counts;
    }

    private async ValueTask<byte[]> DefinitionStateChecksumAsync(SqliteConnection connection, SqliteTransaction transaction,
        string definitionId, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData("base.semanticActivation.definitionState.v1\0"u8);
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT binding_id,key_digest,state,slot_generation,authority_json FROM {_names.SemanticActivationSlots} WHERE definition_id=$id ORDER BY binding_id,key_digest;";
        command.Parameters.AddWithValue("$id", definitionId); await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            hash.AppendData((byte[])reader[0]); hash.AppendData((byte[])reader[1]); hash.AppendData([(byte)reader.GetInt32(2)]);
            hash.AppendData(ToInt64(reader.GetInt64(3))); hash.AppendData((byte[])reader[4]);
        }
        return hash.GetHashAndReset();
    }

    private async ValueTask<byte[]> NegativeAuthorityChecksumAsync(SqliteConnection connection, SqliteTransaction transaction,
        string definitionId, CancellationToken token)
    {
        var rows = new List<byte[]>(); await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT binding_id,key_digest,state,authority_json FROM {_names.SemanticActivationSlots} WHERE definition_id=$id AND state IN (2,3) ORDER BY binding_id,key_digest;";
        command.Parameters.AddWithValue("$id", definitionId); await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            rows.Add(HistoricalNegativeRow((byte[])reader[0], (byte[])reader[1], reader.GetInt32(2), (byte[])reader[3]));
        return OrderedRowsChecksum(rows);
    }

    private bool InstalledMigrationMatches(BaseSemanticActivationDefinitionMigrationAuthority authority) =>
        _options.SemanticActivationMigrations.Count(value => value.Id == authority.MigrationId
            && value.Version == authority.MigrationVersion && DefinitionEqual(value.From, authority.From)
            && DefinitionEqual(value.To, authority.To)) == 1;

    private static byte[] HistoricalNegativeRow(byte[] binding, byte[] key, int state, byte[] authority) =>
        SemanticAdminHash("base.semanticActivation.historicalNegativeRow.v1\0",
            binding, key, ToInt64(state), authority).ToArray();

    private static bool DefinitionEqual(BaseSemanticActivationDefinitionKey left, BaseSemanticActivationDefinitionKey right) =>
        left.Id == right.Id && left.Version == right.Version
        && CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static byte[] OrderedRowsChecksum(IEnumerable<byte[]> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData("base.semanticActivation.orderedRows.v1\0"u8);
        byte[] length = new byte[4];
        foreach (byte[] row in rows) { System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, row.Length); hash.AppendData(length); hash.AppendData(row); }
        return hash.GetHashAndReset();
    }

    private static BaseSemanticActivationMaintenanceResult MaintenanceResult(ImmutableArray<byte> providerIncarnation,
        long previous, long resulting,
        long examined, long changed, long bytes, byte[] authority)
    {
        var value = new BaseSemanticActivationMaintenanceResult
        {
            ProviderIncarnation = providerIncarnation.ToArray().ToImmutableArray(),
            Disposition = BaseSemanticActivationMaintenanceDisposition.Completed, PreviousAuthorityGeneration = previous,
            ResultingAuthorityGeneration = resulting, ExaminedRows = examined, ChangedRows = changed, CanonicalBytes = bytes,
            AuthorityChecksum = authority.ToImmutableArray(), ResultChecksum = [], Checkpoint = null,
            ReceiptDisposition = BaseMutationRequestDisposition.Committed, CommitObservationChecksum = [],
        };
        ImmutableArray<byte> result = BaseSemanticActivationMaintenanceContract.ResultChecksum(value, authority);
        return value with { ResultChecksum = result, CommitObservationChecksum = BaseSemanticActivationMaintenanceContract.CommitObservationChecksum(result.AsSpan()) };
    }

    private static byte[] MaintenanceFingerprint(BaseSemanticActivationMaintenanceRequest request) =>
        BaseSemanticActivationMaintenanceContract.RequestFingerprint(request).ToArray();

    private static ImmutableArray<byte> MaintenanceReceiptChecksum(BaseMutationRequestIdentity identity,
        byte[] fingerprint, BaseSemanticActivationMaintenanceResult result) => SemanticAdminHash(
            "base.semanticActivation.maintenanceReceipt.v1\0", Encoding.UTF8.GetBytes(identity.Scope),
            Encoding.UTF8.GetBytes(identity.Operation), Encoding.UTF8.GetBytes(identity.IdempotencyKey), fingerprint,
            ToInt64((int)result.Disposition), ToInt64(result.PreviousAuthorityGeneration),
            ToInt64(result.ResultingAuthorityGeneration), ToInt64(result.ExaminedRows), ToInt64(result.ChangedRows),
            ToInt64(result.CanonicalBytes), result.ResultChecksum.ToArray());

    private async ValueTask<BaseSemanticActivationMaintenanceResult?> ReadMaintenanceResultAsync(SqliteConnection connection,
        SqliteTransaction? transaction, BaseMutationRequestIdentity identity, byte[] fingerprint, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT request_fingerprint,disposition,previous_generation,resulting_generation,examined_rows,changed_rows,canonical_bytes,result_checksum,commit_checksum,maintenance_id,operation_kind,definition_id,definition_version,definition_checksum,after_binding,after_key,completed_pages,rolling_checksum,checkpoint_checksum FROM {_names.SemanticActivationMaintenance} WHERE request_scope=$scope AND request_operation=$operation AND request_key=$key;";
        command.Parameters.AddWithValue("$scope", identity.Scope); command.Parameters.AddWithValue("$operation", identity.Operation);
        command.Parameters.AddWithValue("$key", identity.IdempotencyKey); await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        if (!CryptographicOperations.FixedTimeEquals((byte[])reader[0], fingerprint))
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.FingerprintConflict,
                "The semantic identity was used with different activation semantics.");
        var disposition = (BaseSemanticActivationMaintenanceDisposition)reader.GetInt32(1);
        BaseSemanticActivationMaintenanceCheckpoint? checkpoint = null;
        if (disposition == BaseSemanticActivationMaintenanceDisposition.InProgress)
        {
            BaseSemanticActivationRecoveryBoundary? after = reader.IsDBNull(14) ? null : new BaseSemanticActivationRecoveryBoundary
            {
                DefinitionId = reader.GetString(11), ScopeBindingId = ((byte[])reader[14]).ToImmutableArray(),
                Key = BaseSemanticActivationKeyDigest.Create((byte[])reader[15]),
            };
            checkpoint = new BaseSemanticActivationMaintenanceCheckpoint
            {
                MaintenanceId = reader.GetString(9), ProviderIncarnation = ProviderIncarnation,
                CapturedStoreGeneration = reader.GetInt64(2), CapturedDefinitionGeneration = reader.GetInt64(2),
                FenceToken = SHA256.HashData((byte[])reader[0]).ToImmutableArray(),
                OperationKind = reader.GetInt32(10) switch { 1 => "compact", 2 => "migrate", 3 => "remove", _ => "invalid" },
                Definition = new BaseSemanticActivationDefinitionKey { Id = reader.GetString(11), Version = reader.GetInt32(12), Checksum = ((byte[])reader[13]).ToImmutableArray() },
                ExpectedAuthorityGeneration = reader.GetInt64(2), After = after, CompletedPages = reader.GetInt32(16),
                CompletedRows = reader.GetInt64(4), CompletedBytes = reader.GetInt64(6), RollingChecksum = ((byte[])reader[17]).ToImmutableArray(),
                RequestFingerprint = ((byte[])reader[0]).ToImmutableArray(), Checksum = ((byte[])reader[18]).ToImmutableArray(),
            };
        }
        return new BaseSemanticActivationMaintenanceResult
        {
            ProviderIncarnation = ProviderIncarnation,
            Disposition = disposition, PreviousAuthorityGeneration = reader.GetInt64(2),
            ResultingAuthorityGeneration = reader.GetInt64(3), ExaminedRows = reader.GetInt64(4), ChangedRows = reader.GetInt64(5),
            CanonicalBytes = reader.GetInt64(6), AuthorityChecksum = ((byte[])reader[17]).ToImmutableArray(),
            ResultChecksum = reader.IsDBNull(7) ? [] : ((byte[])reader[7]).ToImmutableArray(), Checkpoint = checkpoint,
            ReceiptDisposition = disposition == BaseSemanticActivationMaintenanceDisposition.InProgress ? BaseMutationRequestDisposition.Committed : BaseMutationRequestDisposition.Duplicate,
            CommitObservationChecksum = reader.IsDBNull(8) ? [] : ((byte[])reader[8]).ToImmutableArray(),
        };
    }

    private async ValueTask StoreMaintenanceResultAsync(SqliteConnection connection, SqliteTransaction transaction,
        BaseSemanticActivationMaintenanceRequest request, byte[] fingerprint, BaseSemanticActivationMaintenanceResult result, CancellationToken token)
    {
        BaseMutationRequestIdentity identity = request.Identity;
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {_names.SemanticActivationMaintenance}(request_scope,request_operation,request_key,request_fingerprint,maintenance_id,operation_kind,definition_id,definition_version,definition_checksum,disposition,previous_generation,resulting_generation,examined_rows,changed_rows,canonical_bytes,after_binding,after_key,completed_pages,rolling_checksum,checkpoint_checksum,result_checksum,commit_checksum) VALUES($scope,$operation,$key,$fingerprint,$maintenance,$kind,$definition,$definitionVersion,$definitionChecksum,$disposition,$previous,$resulting,$examined,$changed,$bytes,$afterBinding,$afterKey,$pages,$rolling,$checkpoint,$result,$commit) ON CONFLICT(request_scope,request_operation,request_key) DO UPDATE SET disposition=excluded.disposition,resulting_generation=excluded.resulting_generation,examined_rows=excluded.examined_rows,changed_rows=excluded.changed_rows,canonical_bytes=excluded.canonical_bytes,after_binding=excluded.after_binding,after_key=excluded.after_key,completed_pages=excluded.completed_pages,rolling_checksum=excluded.rolling_checksum,checkpoint_checksum=excluded.checkpoint_checksum,result_checksum=excluded.result_checksum,commit_checksum=excluded.commit_checksum WHERE request_fingerprint=excluded.request_fingerprint;";
        command.Parameters.AddWithValue("$scope", identity.Scope); command.Parameters.AddWithValue("$operation", identity.Operation);
        command.Parameters.AddWithValue("$key", identity.IdempotencyKey); command.Parameters.Add("$fingerprint", SqliteType.Blob).Value = fingerprint;
        command.Parameters.AddWithValue("$kind", request switch { BaseSemanticActivationCompactRequest => 1, BaseSemanticActivationMigrateRequest => 2, BaseSemanticActivationRemoveRequest => 3, _ => 0 });
        command.Parameters.AddWithValue("$definition", request.Definition.Id);
        command.Parameters.AddWithValue("$definitionVersion", request.Definition.Version);
        command.Parameters.Add("$definitionChecksum", SqliteType.Blob).Value = request.Definition.Checksum.ToArray();
        BaseSemanticActivationMaintenanceCheckpoint? checkpoint = result.Checkpoint;
        command.Parameters.AddWithValue("$maintenance", checkpoint?.MaintenanceId ?? Convert.ToHexStringLower(fingerprint)); command.Parameters.AddWithValue("$disposition", (int)result.Disposition);
        command.Parameters.AddWithValue("$previous", result.PreviousAuthorityGeneration); command.Parameters.AddWithValue("$resulting", result.ResultingAuthorityGeneration);
        command.Parameters.AddWithValue("$examined", result.ExaminedRows); command.Parameters.AddWithValue("$changed", result.ChangedRows);
        command.Parameters.AddWithValue("$bytes", result.CanonicalBytes);
        command.Parameters.Add("$afterBinding", SqliteType.Blob).Value = checkpoint?.After is null ? DBNull.Value : checkpoint.After.ScopeBindingId.ToArray();
        command.Parameters.Add("$afterKey", SqliteType.Blob).Value = checkpoint?.After is null ? DBNull.Value : KeyBytes(checkpoint.After.Key);
        command.Parameters.AddWithValue("$pages", checkpoint?.CompletedPages ?? 0);
        command.Parameters.Add("$rolling", SqliteType.Blob).Value = (checkpoint?.RollingChecksum ?? result.AuthorityChecksum).ToArray();
        command.Parameters.Add("$checkpoint", SqliteType.Blob).Value = checkpoint is null ? DBNull.Value : checkpoint.Checksum.ToArray();
        command.Parameters.Add("$result", SqliteType.Blob).Value = checkpoint is null ? result.ResultChecksum.ToArray() : DBNull.Value;
        command.Parameters.Add("$commit", SqliteType.Blob).Value = checkpoint is null ? result.CommitObservationChecksum.ToArray() : DBNull.Value;
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private async ValueTask PersistRemovedDefinitionAuthorityAsync(SqliteConnection connection, SqliteTransaction transaction,
        BaseSemanticActivationRemoveRequest request, byte[] fingerprint, BaseSemanticActivationMaintenanceResult result,
        CancellationToken token)
    {
        ImmutableArray<byte> receipt = MaintenanceReceiptChecksum(request.Identity, fingerprint, result);
        byte[] removalJson = JsonSerializer.SerializeToUtf8Bytes(request.RemovalAuthority,
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRemovalAuthority);
        ImmutableArray<byte> authority = SemanticAdminHash("base.semanticActivation.removedDefinition.v1\0",
            Encoding.UTF8.GetBytes(request.Definition.Id), ToInt64(request.Definition.Version), request.Definition.Checksum.ToArray(),
            request.RemovalAuthority.Checksum.ToArray(), ToInt64(request.ExpectedAbsenceCount),
            request.ExpectedAbsenceAuthorityChecksum.ToArray(), ToInt64(result.ResultingAuthorityGeneration), receipt.ToArray());
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {_names.SemanticActivationRemovedDefinitions}(definition_id,definition_version,definition_checksum,removal_id,removal_version,removal_authority_json,absence_count,absence_checksum,publication_generation,receipt_checksum,authority_checksum) VALUES($id,$version,$definitionChecksum,$removal,$removalVersion,$json,$absence,$absenceChecksum,$generation,$receipt,$authority) ON CONFLICT(definition_id,definition_version) DO NOTHING;";
        command.Parameters.AddWithValue("$id", request.Definition.Id); command.Parameters.AddWithValue("$version", request.Definition.Version);
        command.Parameters.Add("$definitionChecksum", SqliteType.Blob).Value = request.Definition.Checksum.ToArray();
        command.Parameters.AddWithValue("$removal", request.RemovalAuthority.Id); command.Parameters.AddWithValue("$removalVersion", request.RemovalAuthority.Version);
        command.Parameters.Add("$json", SqliteType.Blob).Value = removalJson; command.Parameters.AddWithValue("$absence", request.ExpectedAbsenceCount);
        command.Parameters.Add("$absenceChecksum", SqliteType.Blob).Value = request.ExpectedAbsenceAuthorityChecksum.ToArray();
        command.Parameters.AddWithValue("$generation", result.ResultingAuthorityGeneration); command.Parameters.Add("$receipt", SqliteType.Blob).Value = receipt.ToArray();
        command.Parameters.Add("$authority", SqliteType.Blob).Value = authority.ToArray();
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private static BaseAtomicReadIntervalEvidence InspectionInterval(BaseSemanticActivationProviderInspectionRequest request,
        ImmutableArray<BaseSemanticActivationProviderInspectionItem> items)
    {
        byte[] lower = request.After is null ? Encoding.UTF8.GetBytes(request.Definition.Id) : request.After.RuntimeBoundaryChecksum.ToArray();
        byte[] upper = items.IsDefaultOrEmpty ? lower : items[^1].Boundary.RuntimeBoundaryChecksum.ToArray();
        return new BaseAtomicReadIntervalEvidence
        {
            LogicalAccessPathId = "base.semanticActivation.inspection", CanonicalLowerBound = lower.ToImmutableArray(),
            CanonicalUpperBound = upper.ToImmutableArray(), LowerInclusive = false, UpperInclusive = true,
        };
    }

    private static ImmutableArray<byte> InspectionPageChecksum(BaseSemanticActivationProviderInspectionRequest request,
        BaseSemanticActivationProviderInspectionPage page)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.inspectionPage.v1\0"u8);
        hash.AppendData(request.RuntimeRequestAuthorityChecksum.AsSpan());
        foreach (BaseSemanticActivationProviderInspectionItem item in page.Items)
        {
            hash.AppendData(item.Boundary.RuntimeBoundaryChecksum.AsSpan()); hash.AppendData(item.StateChecksum.AsSpan());
            hash.AppendData(ToInt64(item.SlotGeneration)); hash.AppendData([(byte)item.State]);
        }
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static ImmutableArray<byte> SemanticAdminHash(string purpose, params byte[][] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData(Encoding.UTF8.GetBytes(purpose));
        byte[] length = new byte[4];
        foreach (byte[] value in values) { System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length); hash.AppendData(value); }
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static byte[] ToInt64(long value) { byte[] bytes = new byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); return bytes; }

    private static BaseActivationAccounting EmptyActivationAccounting() => new()
    {
        Candidates = 0, Comparisons = 0, ReadIntervals = 0, IndexOperations = 0,
        EvidenceBytes = 0, TransientBytes = 0,
    };

    private static BaseResult<T> SemanticFailure<T>(OperationStatus status, string code, ErrorCategory category, string message) =>
        BaseProviderResultContract.Failure<T>(status, new BaseError { Code = code, Message = message, Category = category });

    private sealed class SemanticMaintenanceBlockedException(string code, string message) : Exception(message)
    {
        internal string Code { get; } = code;
    }
}
