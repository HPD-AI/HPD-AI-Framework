using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore
{
    private sealed record SemanticRecoverySnapshot(
        long AuthorityGeneration,
        ImmutableArray<byte> DefinitionSetChecksum,
        long RestoreEpoch,
        long SchemaGeneration,
        long RowCount,
        ImmutableArray<byte> OrderedChecksum);

    private sealed record SemanticRecoveryRow(
        string DefinitionId,
        byte[] BindingId,
        byte[] KeyDigest,
        int State,
        long SlotGeneration,
        byte[] AuthorityJson,
        string? ReceiptScope,
        string? ReceiptOperation,
        string? ReceiptKey,
        byte[]? ReceiptFingerprint,
        byte[]? ReceiptStructuralDigest,
        byte[]? ReceiptResultJson,
        byte[]? ReceiptAuthorityChecksum);

    private sealed record SemanticSlotRow(string DefinitionId, byte[] BindingId, byte[] KeyDigest,
        int State, long SlotGeneration, byte[] AuthorityJson);

    private async ValueTask<SemanticRecoverySnapshot?> CaptureSemanticRecoverySnapshotAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (_options.SemanticActivationOwnerGeneration <= 0) return null;
        long generation = await ReadSemanticAuthorityGenerationAsync(connection, null, cancellationToken).ConfigureAwait(false);
        long restoreEpoch;
        await using (SqliteCommand epoch = connection.CreateCommand())
        {
            epoch.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch';";
            restoreEpoch = Convert.ToInt64(await epoch.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (restoreEpoch < 0) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        byte[] definitionSet;
        await using (SqliteCommand authority = connection.CreateCommand())
        {
            authority.CommandText = $"SELECT value FROM {_names.ProviderState} WHERE key='semantic_activation_definition_set_checksum';";
            if (await authority.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not string text || text.Length != 64)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            definitionSet = Convert.FromHexString(text);
        }
        long rowCount = 0; long? capturedSchemaGeneration = null;
        using var rolling = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await foreach (SemanticRecoveryRow row in ReadSemanticRecoveryRowsAsync(connection, null, cancellationToken))
        {
            long rowSchema = SemanticStoreRequirement(row).SchemaGeneration;
            capturedSchemaGeneration ??= rowSchema;
            if (capturedSchemaGeneration != rowSchema) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            ValidateSemanticRecoveryRow(row, generation, definitionSet, restoreEpoch, rowSchema);
            await ValidateSemanticRecoveryDependenciesAsync(connection, null, row, cancellationToken).ConfigureAwait(false);
            rolling.AppendData(RecoveryRowChecksum(row));
            rowCount = checked(rowCount + 1);
        }
        return new SemanticRecoverySnapshot(generation, definitionSet.ToImmutableArray(), restoreEpoch,
            capturedSchemaGeneration ?? Volatile.Read(ref _schemaGeneration),
            rowCount, rolling.GetHashAndReset().ToImmutableArray());
    }

    private async ValueTask RebindCurrentSemanticSlotsAsync(SqliteConnection connection, SqliteTransaction transaction,
        long artifactGeneration, long resultingGeneration, byte[] definitionSet, long artifactRestoreEpoch,
        long restoreEpoch, long resultingSchemaGeneration, CancellationToken cancellationToken)
    {
        await foreach (SemanticSlotRow slot in ReadSemanticSlotRowsAsync(connection, transaction, cancellationToken))
        {
            string definition = slot.DefinitionId; byte[] binding = slot.BindingId; byte[] key = slot.KeyDigest;
            int state = slot.State; long slotGeneration = slot.SlotGeneration; byte[] authority = slot.AuthorityJson;
            byte[] replacement;
            if (state == (int)BaseSemanticActivationSlotState.Live)
            {
                BaseSemanticActivationLiveAuthority value = JsonSerializer.Deserialize(authority,
                    HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)
                    ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                ValidateSemanticStore(value.StoreAuthority, artifactGeneration, definitionSet, artifactRestoreEpoch, resultingSchemaGeneration);
                if (value.Definition.Id != definition || value.SlotGeneration != slotGeneration
                    || !KeyBytes(value.KeyDigest).AsSpan().SequenceEqual(key)
                    || !value.ScopeBinding.BindingId.AsSpan().SequenceEqual(binding)
                    || value.Scope.Kind != value.ScopeBinding.Kind
                    || value.SubjectLifetime is not null && !value.SubjectLifetime.ScopeBindingId.AsSpan().SequenceEqual(binding)
                    || !CryptographicOperations.FixedTimeEquals(BaseSemanticActivationEvidenceContract.ScopeBindingChecksum(value.ScopeBinding).AsSpan(), value.ScopeBinding.Checksum.AsSpan())
                    || !CryptographicOperations.FixedTimeEquals(BaseSemanticActivationEvidenceContract.LiveChecksum(value).AsSpan(), value.Checksum.AsSpan()))
                    throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                BaseSemanticActivationScopeBinding installedBinding = await ReadScopeBindingAsync(connection, transaction, binding.ToImmutableArray(), cancellationToken).ConfigureAwait(false);
                if (!ScopeBindingsEqual(installedBinding, value.ScopeBinding) || _subjectScopes is null || !_subjectScopes.Matches(new BaseProtectedSubjectScope
                    { Kind = installedBinding.Kind, IndexDigest = installedBinding.SeekDigest.ToArray(), ProtectedCanonicalValue = installedBinding.ProtectedCanonicalScope.ToArray() }, value.Scope))
                    throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                await RequireLiveActivationCorrespondenceAsync(connection, transaction, value, cancellationToken).ConfigureAwait(false);
                BaseSemanticActivationStoreAuthority store = ReboundStore(value.StoreAuthority, resultingGeneration, definitionSet, restoreEpoch, resultingSchemaGeneration);
                value = value with { StoreAuthority = store, Checksum = [] };
                value = value with { Checksum = BaseSemanticActivationEvidenceContract.LiveChecksum(value) };
                replacement = JsonSerializer.SerializeToUtf8Bytes(value, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority);
            }
            else
            {
                var row = new SemanticRecoveryRow(definition, binding, key, state, slotGeneration, authority,
                    null, null, null, null, null, null, null);
                ValidateSemanticRecoveryRow(row, artifactGeneration, definitionSet, artifactRestoreEpoch, resultingSchemaGeneration);
                replacement = RebindSemanticRecoveryRow(row, resultingGeneration, definitionSet, restoreEpoch, resultingSchemaGeneration).AuthorityJson;
            }
            await using SqliteCommand update = connection.CreateCommand(); update.Transaction = transaction;
            update.CommandText = $"UPDATE {_names.SemanticActivationSlots} SET authority_json=$authority WHERE definition_id=$definition AND binding_id=$binding AND key_digest=$key AND state=$state AND slot_generation=$generation;";
            update.Parameters.Add("$authority", SqliteType.Blob).Value = replacement; update.Parameters.AddWithValue("$definition", definition);
            update.Parameters.Add("$binding", SqliteType.Blob).Value = binding; update.Parameters.Add("$key", SqliteType.Blob).Value = key;
            update.Parameters.AddWithValue("$state", state); update.Parameters.AddWithValue("$generation", slotGeneration);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        }
    }

    private async IAsyncEnumerable<SemanticSlotRow> ReadSemanticSlotRowsAsync(SqliteConnection connection,
        SqliteTransaction transaction, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? afterDefinition = null; byte[]? afterBinding = null; byte[]? afterKey = null;
        while (true)
        {
            var page = new List<SemanticSlotRow>(256);
            await using (SqliteCommand read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = $"SELECT definition_id,binding_id,key_digest,state,slot_generation,authority_json FROM {_names.SemanticActivationSlots} WHERE $afterDefinition IS NULL OR (definition_id,binding_id,key_digest)>($afterDefinition,$afterBinding,$afterKey) ORDER BY definition_id,binding_id,key_digest LIMIT 256;";
                read.Parameters.AddWithValue("$afterDefinition", (object?)afterDefinition ?? DBNull.Value);
                read.Parameters.Add("$afterBinding", SqliteType.Blob).Value = (object?)afterBinding ?? DBNull.Value;
                read.Parameters.Add("$afterKey", SqliteType.Blob).Value = (object?)afterKey ?? DBNull.Value;
                await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    page.Add(new SemanticSlotRow(reader.GetString(0), (byte[])reader[1], (byte[])reader[2], reader.GetInt32(3), reader.GetInt64(4), (byte[])reader[5]));
            }
            if (page.Count == 0) yield break;
            foreach (SemanticSlotRow item in page) yield return item;
            SemanticSlotRow last = page[^1]; afterDefinition = last.DefinitionId; afterBinding = last.BindingId; afterKey = last.KeyDigest;
        }
    }

    private async ValueTask RestoreSemanticRecoverySnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long artifactRestoreEpoch,
        long restoreEpoch,
        long artifactSchemaGeneration,
        SemanticRecoverySnapshot? prior,
        string recoveryDatabasePath,
        long preRestoreActivationGeneration,
        long resultingActivationGeneration,
        CancellationToken cancellationToken)
    {
        if (_options.SemanticActivationOwnerGeneration <= 0)
        {
            if (prior is not null) throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
            return;
        }

        long artifactGeneration = await ReadSemanticAuthorityGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        long resultingGeneration = checked(Math.Max(artifactGeneration, prior?.AuthorityGeneration ?? 0) + 1);
        byte[] definitionSet;
        await using (SqliteCommand authority = connection.CreateCommand())
        {
            authority.Transaction = transaction;
            authority.CommandText = $"SELECT value FROM {_names.ProviderState} WHERE key='semantic_activation_definition_set_checksum';";
            if (await authority.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not string text || text.Length != 64)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            definitionSet = Convert.FromHexString(text);
        }
        if (prior is not null && !prior.DefinitionSetChecksum.AsSpan().SequenceEqual(definitionSet))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);

        await RequireArtifactNegativeCorrespondenceAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await RebindCurrentSemanticSlotsAsync(connection, transaction, artifactGeneration, resultingGeneration,
            definitionSet, artifactRestoreEpoch, restoreEpoch, artifactSchemaGeneration, cancellationToken).ConfigureAwait(false);

        await foreach (SemanticRecoveryRow row in ReadSemanticRecoveryRowsAsync(connection, transaction, cancellationToken))
        {
            ValidateSemanticRecoveryRow(row, artifactGeneration, definitionSet, artifactRestoreEpoch, artifactSchemaGeneration);
            await ValidateSemanticRecoveryDependenciesAsync(connection, transaction, row, cancellationToken).ConfigureAwait(false);
            SemanticRecoveryRow transformed = await TransformRecoveredSubjectLifetimeAsync(connection, transaction, row, cancellationToken).ConfigureAwait(false);
            await UpsertRestoredSemanticRecoveryRowAsync(connection, transaction,
                RebindSemanticRecoveryRow(transformed, resultingGeneration, definitionSet, restoreEpoch, artifactSchemaGeneration), cancellationToken).ConfigureAwait(false);
        }
        if (prior is not null)
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = recoveryDatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            long count = 0; using var rolling = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await foreach (SemanticRecoveryRow row in ReadSemanticRecoveryRowsAsync(source, null, cancellationToken))
            {
                ValidateSemanticRecoveryRow(row, prior.AuthorityGeneration, prior.DefinitionSetChecksum.ToArray(), prior.RestoreEpoch, prior.SchemaGeneration);
                await ValidateSemanticRecoveryDependenciesAsync(source, null, row, cancellationToken).ConfigureAwait(false);
                rolling.AppendData(RecoveryRowChecksum(row)); count = checked(count + 1);
                await EnsureRecoveredScopeBindingAsync(source, connection, transaction, row.BindingId, cancellationToken).ConfigureAwait(false);
                await EnsureRecoveredActivationAuthorityAsync(source, connection, transaction, row,
                    preRestoreActivationGeneration, resultingActivationGeneration, cancellationToken).ConfigureAwait(false);
                SemanticRecoveryRow transformed = await TransformRecoveredSubjectLifetimeAsync(connection, transaction, row, cancellationToken, source).ConfigureAwait(false);
                await UpsertRestoredSemanticRecoveryRowAsync(connection, transaction,
                    RebindSemanticRecoveryRow(transformed, resultingGeneration, definitionSet, restoreEpoch, artifactSchemaGeneration), cancellationToken).ConfigureAwait(false);
            }
            if (count != prior.RowCount || !CryptographicOperations.FixedTimeEquals(rolling.GetHashAndReset(), prior.OrderedChecksum.AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        }

        await using SqliteCommand publish = connection.CreateCommand(); publish.Transaction = transaction;
        publish.CommandText = $"UPDATE {_names.ProviderState} SET value=$generation WHERE key='semantic_activation_authority_generation' AND CAST(value AS INTEGER)=$artifact;";
        publish.Parameters.AddWithValue("$generation", resultingGeneration); publish.Parameters.AddWithValue("$artifact", artifactGeneration);
        if (await publish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
    }

    private async ValueTask<SemanticRecoveryRow> TransformRecoveredSubjectLifetimeAsync(
        SqliteConnection target, SqliteTransaction transaction, SemanticRecoveryRow row,
        CancellationToken cancellationToken, SqliteConnection? source = null)
    {
        BaseSemanticActivationSubjectLifetimeBinding? lifetime = row.State == (int)BaseSemanticActivationSlotState.Retired
            ? JsonSerializer.Deserialize(row.AuthorityJson, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)?.SubjectLifetime
            : JsonSerializer.Deserialize(row.AuthorityJson, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)?.SubjectLifetime;
        if (lifetime is null) return row;
        if (source is not null)
            await RequireSubjectTerminalLifetimeAsync(source, null, lifetime, cancellationToken).ConfigureAwait(false);
        BaseSemanticActivationSubjectLifetimeBinding replacement = await ReadRestoredSubjectLifetimeAsync(target, transaction, lifetime, cancellationToken).ConfigureAwait(false);
        if (row.State == (int)BaseSemanticActivationSlotState.Retired)
        {
            BaseSemanticActivationRetirementAuthority value = JsonSerializer.Deserialize(row.AuthorityJson, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            value = value with { SubjectLifetime = replacement, Checksum = [] };
            value = value with { Checksum = BaseSemanticActivationEvidenceContract.RetirementChecksum(value) };
            return row with { AuthorityJson = JsonSerializer.SerializeToUtf8Bytes(value, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority) };
        }
        BaseSemanticActivationAbsenceAuthority absent = JsonSerializer.Deserialize(row.AuthorityJson, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)
            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        absent = absent with { SubjectLifetime = replacement, Checksum = [] };
        absent = absent with { Checksum = BaseSemanticActivationEvidenceContract.AbsenceChecksum(absent) };
        return row with { AuthorityJson = JsonSerializer.SerializeToUtf8Bytes(absent, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority) };
    }

    private async ValueTask RequireSubjectTerminalLifetimeAsync(SqliteConnection connection, SqliteTransaction? transaction,
        BaseSemanticActivationSubjectLifetimeBinding lifetime, CancellationToken cancellationToken)
    {
        BaseSemanticActivationScopeBinding binding = await ReadScopeBindingAsync(connection, transaction, lifetime.ScopeBindingId, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM {_names.SubjectTerminalLifetimes} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND retired_authority_epoch=$epoch AND retired_incarnation=$incarnation;";
        command.Parameters.AddWithValue("$scopeKind", (int)binding.Kind); command.Parameters.Add("$scopeDigest", SqliteType.Blob).Value = binding.SeekDigest.ToArray();
        command.Parameters.AddWithValue("$contract", lifetime.ContractId); command.Parameters.AddWithValue("$version", lifetime.ContractVersion);
        command.Parameters.AddWithValue("$subject", lifetime.SubjectId.Value); command.Parameters.Add("$epoch", SqliteType.Blob).Value = lifetime.AuthorityEpoch.ToArray();
        command.Parameters.Add("$incarnation", SqliteType.Blob).Value = lifetime.Incarnation.ToArray();
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 1)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private async ValueTask<BaseSemanticActivationSubjectLifetimeBinding> ReadRestoredSubjectLifetimeAsync(
        SqliteConnection connection, SqliteTransaction transaction, BaseSemanticActivationSubjectLifetimeBinding prior,
        CancellationToken cancellationToken)
    {
        BaseSemanticActivationScopeBinding binding = await ReadScopeBindingAsync(connection, transaction, prior.ScopeBindingId, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT t.retired_authority_epoch,t.retired_incarnation,c.contract_checksum FROM {_names.SubjectTerminalLifetimes} t JOIN {_names.SubjectContracts} c ON c.contract_id=t.contract_id AND c.contract_version=t.contract_version WHERE t.scope_kind=$scopeKind AND t.scope_index_digest=$scopeDigest AND t.contract_id=$contract AND t.contract_version=$version AND t.subject_id=$subject AND t.retired_incarnation=$incarnation;";
        command.Parameters.AddWithValue("$scopeKind", (int)binding.Kind); command.Parameters.Add("$scopeDigest", SqliteType.Blob).Value = binding.SeekDigest.ToArray();
        command.Parameters.AddWithValue("$contract", prior.ContractId); command.Parameters.AddWithValue("$version", prior.ContractVersion);
        command.Parameters.AddWithValue("$subject", prior.SubjectId.Value); command.Parameters.Add("$incarnation", SqliteType.Blob).Value = prior.Incarnation.ToArray();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || !string.Equals(reader.GetString(2), Convert.ToHexStringLower(prior.ContractChecksum.AsSpan()), StringComparison.Ordinal))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        var replacement = prior with { AuthorityEpoch = new BaseSubjectAuthorityEpoch((byte[])reader[0]), Incarnation = new BaseSubjectIncarnation((byte[])reader[1]), Checksum = [] };
        return replacement with { Checksum = BaseSemanticActivationEvidenceContract.SubjectLifetimeChecksum(replacement) };
    }

    private async ValueTask<BaseSemanticActivationScopeBinding> ReadScopeBindingAsync(SqliteConnection connection,
        SqliteTransaction? transaction, ImmutableArray<byte> bindingId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT binding_json FROM {_names.SemanticActivationScopes} WHERE binding_id=$binding;";
        command.Parameters.Add("$binding", SqliteType.Blob).Value = bindingId.ToArray();
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not byte[] json
            || JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding) is not { } binding
            || !binding.BindingId.AsSpan().SequenceEqual(bindingId.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticActivationEvidenceContract.ScopeBindingChecksum(binding).AsSpan(), binding.Checksum.AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        return binding;
    }

    private static bool ScopeBindingsEqual(BaseSemanticActivationScopeBinding left, BaseSemanticActivationScopeBinding right) =>
        left.Kind == right.Kind && left.ProtectionKeyId == right.ProtectionKeyId && left.ProtectionKeyVersion == right.ProtectionKeyVersion
        && left.BindingId.AsSpan().SequenceEqual(right.BindingId.AsSpan())
        && left.ProtectedCanonicalScope.AsSpan().SequenceEqual(right.ProtectedCanonicalScope.AsSpan())
        && left.SeekDigest.AsSpan().SequenceEqual(right.SeekDigest.AsSpan())
        && left.Checksum.AsSpan().SequenceEqual(right.Checksum.AsSpan());

    private async ValueTask EnsureRecoveredActivationAuthorityAsync(SqliteConnection source, SqliteConnection target,
        SqliteTransaction transaction, SemanticRecoveryRow row, long preRestoreActivationGeneration,
        long resultingActivationGeneration, CancellationToken cancellationToken)
    {
        if (row.State != (int)BaseSemanticActivationSlotState.Retired) return;
        BaseSemanticActivationRetirementAuthority retired = JsonSerializer.Deserialize(row.AuthorityJson,
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        object?[] values; string[] columns;
        await using (SqliteCommand read = source.CreateCommand())
        {
            read.CommandText = $"SELECT * FROM {_names.Activations} WHERE activation_id=$id;";
            read.Parameters.AddWithValue("$id", retired.ActivationId);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                await reader.DisposeAsync().ConfigureAwait(false);
                await EnsureRecoveredPruneFloorAsync(source, target, transaction, retired, preRestoreActivationGeneration,
                    resultingActivationGeneration, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (reader.GetInt32(reader.GetOrdinal("state")) != (int)retired.TerminalState
                || reader.GetInt64(reader.GetOrdinal("generation")) != retired.TerminalActivationGeneration
                || !((byte[])reader[reader.GetOrdinal("control_checksum")]).AsSpan().SequenceEqual(retired.TerminalActivationChecksum.AsSpan())
                || !((byte[])reader[reader.GetOrdinal("terminal_receipt_checksum")]).AsSpan().SequenceEqual(retired.CompletionReceiptChecksum.AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            values = new object?[reader.FieldCount]; reader.GetValues(values);
        }
        await InsertExactRowAsync(target, transaction, _names.Activations, columns, values, "activation_id", retired.ActivationId,
            cancellationToken, replaceExisting: true).ConfigureAwait(false);

        await using SqliteCommand receipts = source.CreateCommand();
        receipts.CommandText = $"SELECT * FROM {_names.ActivationReceipts} WHERE activation_id=$id ORDER BY receipt_key;";
        receipts.Parameters.AddWithValue("$id", retired.ActivationId);
        await using SqliteDataReader receiptReader = await receipts.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var receiptRows = new List<(string[] Columns, object?[] Values, string Key)>();
        bool completionReceiptFound = false;
        while (await receiptReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string[] receiptColumns = Enumerable.Range(0, receiptReader.FieldCount).Select(receiptReader.GetName).ToArray();
            var receiptValues = new object?[receiptReader.FieldCount]; receiptReader.GetValues(receiptValues);
            int authorityOrdinal = receiptReader.GetOrdinal("authority_checksum");
            completionReceiptFound |= !receiptReader.IsDBNull(authorityOrdinal)
                && ((byte[])receiptReader[authorityOrdinal]).AsSpan().SequenceEqual(retired.CompletionReceiptChecksum.AsSpan());
            receiptRows.Add((receiptColumns, receiptValues, receiptReader.GetString(receiptReader.GetOrdinal("receipt_key"))));
        }
        if (receiptRows.Count == 0 || !completionReceiptFound) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        foreach ((string[] receiptColumns, object?[] receiptValues, string key) in receiptRows)
            await InsertExactRowAsync(target, transaction, _names.ActivationReceipts, receiptColumns, receiptValues,
                "receipt_key", key, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EnsureRecoveredPruneFloorAsync(SqliteConnection source, SqliteConnection target,
        SqliteTransaction transaction, BaseSemanticActivationRetirementAuthority retired, long preRestoreActivationGeneration,
        long resultingActivationGeneration, CancellationToken cancellationToken)
    {
        BaseActivationPruneEvidence prior;
        await using (SqliteCommand read = source.CreateCommand())
        {
            read.CommandText = $"SELECT definition_id,definition_version,definition_checksum,terminal_generation,terminal_control_checksum,terminal_receipt_checksum,occurrence_checksum,result_checksum,prune_authority_generation,application_id,logical_store_id,store_instance_id,restore_epoch,publication_authority_checksum,authority_checksum FROM {_names.ActivationPruneFloors} WHERE activation_id=$id;";
            read.Parameters.AddWithValue("$id", retired.ActivationId);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            prior = new BaseActivationPruneEvidence
            {
                ActivationId = retired.ActivationId,
                Definition = new BaseActivationDefinitionKey { Id = reader.GetString(0), Version = reader.GetInt32(1), Checksum = ((byte[])reader[2]).ToImmutableArray() },
                TerminalGeneration = reader.GetInt64(3), TerminalControlChecksum = ((byte[])reader[4]).ToImmutableArray(),
                TerminalReceiptChecksum = ((byte[])reader[5]).ToImmutableArray(), OccurrenceChecksum = reader.IsDBNull(6) ? null : ((byte[])reader[6]).ToImmutableArray(),
                ResultChecksum = reader.IsDBNull(7) ? null : ((byte[])reader[7]).ToImmutableArray(), PruneAuthorityGeneration = reader.GetInt64(8),
                ApplicationId = reader.GetString(9), LogicalStoreId = reader.GetString(10), StoreInstanceId = reader.GetString(11), RestoreEpoch = reader.GetInt64(12),
                PublicationAuthorityChecksum = ((byte[])reader[13]).ToImmutableArray(), Checksum = ((byte[])reader[14]).ToImmutableArray(),
            };
        }
        long sourceRestoreEpoch;
        string sourceStoreInstance;
        await using (SqliteCommand authority = source.CreateCommand())
        {
            authority.CommandText = $"SELECT i.store_instance_id,CAST(r.value AS INTEGER) FROM {_names.SchemaIdentity} i JOIN {_names.ProviderState} r ON r.key='restore_epoch' WHERE i.singleton=1;";
            await using SqliteDataReader reader = await authority.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            sourceStoreInstance = reader.GetString(0); sourceRestoreEpoch = reader.GetInt64(1);
        }
        byte[] priorPublication = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"base.activation.publicationAuthority.v1\0{prior.ApplicationId}\n{prior.LogicalStoreId}\n{prior.StoreInstanceId}\n{prior.RestoreEpoch}\n{prior.PruneAuthorityGeneration}"));
        if (!BaseActivationPruneEvidenceContract.IsValid(prior)
            || prior.ApplicationId != _options.SemanticActivationApplicationId || prior.LogicalStoreId != _options.StoreId
            || prior.StoreInstanceId != sourceStoreInstance || prior.RestoreEpoch != sourceRestoreEpoch
            || prior.PruneAuthorityGeneration > preRestoreActivationGeneration
            || !CryptographicOperations.FixedTimeEquals(priorPublication, prior.PublicationAuthorityChecksum.AsSpan())
            || prior.TerminalGeneration != retired.TerminalActivationGeneration
            || !prior.TerminalControlChecksum.AsSpan().SequenceEqual(retired.TerminalActivationChecksum.AsSpan())
            || !prior.TerminalReceiptChecksum.AsSpan().SequenceEqual(retired.CompletionReceiptChecksum.AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        long restoreEpoch = await ReadRestoreEpochAsync(target, transaction, cancellationToken).ConfigureAwait(false);
        byte[] publication = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"base.activation.publicationAuthority.v1\0{prior.ApplicationId}\n{prior.LogicalStoreId}\n{prior.StoreInstanceId}\n{restoreEpoch}\n{prior.PruneAuthorityGeneration}"));
        publication = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"base.activation.publicationAuthority.v1\0{prior.ApplicationId}\n{prior.LogicalStoreId}\n{prior.StoreInstanceId}\n{restoreEpoch}\n{resultingActivationGeneration}"));
        BaseActivationPruneEvidence replacement = prior with { RestoreEpoch = restoreEpoch, PruneAuthorityGeneration = resultingActivationGeneration, PublicationAuthorityChecksum = publication.ToImmutableArray(), Checksum = [] };
        replacement = replacement with { Checksum = BaseActivationPruneEvidenceContract.Checksum(replacement) };
        await using (SqliteCommand suppress = target.CreateCommand())
        {
            suppress.Transaction = transaction;
            suppress.CommandText = $"DELETE FROM {_names.ActivationEffects} WHERE activation_id=$id; DELETE FROM {_names.ActivationReceipts} WHERE activation_id=$id; DELETE FROM {_names.Activations} WHERE activation_id=$id;";
            suppress.Parameters.AddWithValue("$id", retired.ActivationId);
            await suppress.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using SqliteCommand write = target.CreateCommand(); write.Transaction = transaction;
        write.CommandText = $"INSERT INTO {_names.ActivationPruneFloors}(activation_id,definition_id,definition_version,definition_checksum,terminal_generation,terminal_control_checksum,terminal_receipt_checksum,occurrence_checksum,result_checksum,prune_authority_generation,application_id,logical_store_id,store_instance_id,restore_epoch,publication_authority_checksum,authority_checksum) VALUES($id,$definition,$version,$definitionChecksum,$generation,$control,$receipt,$occurrence,$result,$authorityGeneration,$application,$logical,$instance,$restore,$publication,$checksum) ON CONFLICT(activation_id) DO UPDATE SET definition_id=excluded.definition_id,definition_version=excluded.definition_version,definition_checksum=excluded.definition_checksum,terminal_generation=excluded.terminal_generation,terminal_control_checksum=excluded.terminal_control_checksum,terminal_receipt_checksum=excluded.terminal_receipt_checksum,occurrence_checksum=excluded.occurrence_checksum,result_checksum=excluded.result_checksum,prune_authority_generation=excluded.prune_authority_generation,application_id=excluded.application_id,logical_store_id=excluded.logical_store_id,store_instance_id=excluded.store_instance_id,restore_epoch=excluded.restore_epoch,publication_authority_checksum=excluded.publication_authority_checksum,authority_checksum=excluded.authority_checksum;";
        write.Parameters.AddWithValue("$id", replacement.ActivationId); write.Parameters.AddWithValue("$definition", replacement.Definition.Id); write.Parameters.AddWithValue("$version", replacement.Definition.Version);
        write.Parameters.Add("$definitionChecksum", SqliteType.Blob).Value = replacement.Definition.Checksum.ToArray(); write.Parameters.AddWithValue("$generation", replacement.TerminalGeneration);
        write.Parameters.Add("$control", SqliteType.Blob).Value = replacement.TerminalControlChecksum.ToArray(); write.Parameters.Add("$receipt", SqliteType.Blob).Value = replacement.TerminalReceiptChecksum.ToArray();
        write.Parameters.Add("$occurrence", SqliteType.Blob).Value = (object?)replacement.OccurrenceChecksum?.ToArray() ?? DBNull.Value; write.Parameters.Add("$result", SqliteType.Blob).Value = (object?)replacement.ResultChecksum?.ToArray() ?? DBNull.Value;
        write.Parameters.AddWithValue("$authorityGeneration", replacement.PruneAuthorityGeneration); write.Parameters.AddWithValue("$application", replacement.ApplicationId); write.Parameters.AddWithValue("$logical", replacement.LogicalStoreId); write.Parameters.AddWithValue("$instance", replacement.StoreInstanceId); write.Parameters.AddWithValue("$restore", replacement.RestoreEpoch);
        write.Parameters.Add("$publication", SqliteType.Blob).Value = replacement.PublicationAuthorityChecksum.ToArray(); write.Parameters.Add("$checksum", SqliteType.Blob).Value = replacement.Checksum.ToArray();
        await write.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<long> ReadRestoreEpochAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch';";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async ValueTask InsertExactRowAsync(SqliteConnection target, SqliteTransaction transaction,
        string table, string[] columns, object?[] values, string keyColumn, string keyValue,
        CancellationToken cancellationToken, bool replaceExisting = false)
    {
        static string Quote(string value) => '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
        await using SqliteCommand insert = target.CreateCommand(); insert.Transaction = transaction;
        string[] parameters = Enumerable.Range(0, columns.Length).Select(static index => "$p" + index).ToArray();
        string conflict = replaceExisting
            ? $" ON CONFLICT({Quote(keyColumn)}) DO UPDATE SET {string.Join(',', columns.Where(value => value != keyColumn).Select(value => $"{Quote(value)}=excluded.{Quote(value)}"))}"
            : " ON CONFLICT DO NOTHING";
        insert.CommandText = $"INSERT INTO {Quote(table)}({string.Join(',', columns.Select(Quote))}) VALUES({string.Join(',', parameters)}){conflict};";
        for (int index = 0; index < parameters.Length; index++) insert.Parameters.AddWithValue(parameters[index], values[index] ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand verify = target.CreateCommand(); verify.Transaction = transaction;
        verify.CommandText = $"SELECT {string.Join(',', columns.Select(Quote))} FROM {Quote(table)} WHERE {Quote(keyColumn)}=$key;";
        verify.Parameters.AddWithValue("$key", keyValue);
        await using SqliteDataReader reader = await verify.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        for (int index = 0; index < columns.Length; index++)
        {
            object actual = reader.GetValue(index); object expected = values[index] ?? DBNull.Value;
            bool equal = actual is byte[] actualBytes && expected is byte[] expectedBytes
                ? actualBytes.AsSpan().SequenceEqual(expectedBytes) : Equals(actual, expected);
            if (!equal) throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        }
    }

    private async ValueTask EnsureRecoveredScopeBindingAsync(SqliteConnection source, SqliteConnection target,
        SqliteTransaction transaction, byte[] bindingId, CancellationToken cancellationToken)
    {
        BaseSemanticActivationScopeBinding binding;
        await using (SqliteCommand read = source.CreateCommand())
        {
            read.CommandText = $"SELECT binding_json FROM {_names.SemanticActivationScopes} WHERE binding_id=$binding;";
            read.Parameters.Add("$binding", SqliteType.Blob).Value = bindingId;
            if (await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not byte[] json
                || JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding) is not { } value)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            binding = value;
        }
        if (!binding.BindingId.AsSpan().SequenceEqual(bindingId)
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticActivationEvidenceContract.ScopeBindingChecksum(binding).AsSpan(), binding.Checksum.AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(binding, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding);
        await using SqliteCommand insert = target.CreateCommand(); insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {_names.SemanticActivationScopes}(scope_kind,seek_digest,binding_id,binding_json) VALUES($kind,$seek,$binding,$json) ON CONFLICT(scope_kind,seek_digest) DO NOTHING;";
        insert.Parameters.AddWithValue("$kind", (int)binding.Kind); insert.Parameters.Add("$seek", SqliteType.Blob).Value = binding.SeekDigest.ToArray();
        insert.Parameters.Add("$binding", SqliteType.Blob).Value = bindingId; insert.Parameters.Add("$json", SqliteType.Blob).Value = jsonBytes;
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand verify = target.CreateCommand(); verify.Transaction = transaction;
        verify.CommandText = $"SELECT binding_json FROM {_names.SemanticActivationScopes} WHERE scope_kind=$kind AND seek_digest=$seek AND binding_id=$binding;";
        verify.Parameters.AddWithValue("$kind", (int)binding.Kind); verify.Parameters.Add("$seek", SqliteType.Blob).Value = binding.SeekDigest.ToArray();
        verify.Parameters.Add("$binding", SqliteType.Blob).Value = bindingId;
        if (await verify.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not byte[] stored
            || !stored.AsSpan().SequenceEqual(jsonBytes)) throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
    }

    private async ValueTask RequireArtifactNegativeCorrespondenceAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"""
SELECT
 (SELECT COUNT(*) FROM {_names.SemanticActivationSlots} s LEFT JOIN {_names.SemanticActivationRecoveryFloors} f
   ON f.definition_id=s.definition_id AND f.binding_id=s.binding_id AND f.key_digest=s.key_digest
   WHERE s.state IN (2,3) AND (f.definition_id IS NULL OR f.state<>s.state OR f.slot_generation<>s.slot_generation OR f.authority_json<>s.authority_json))
+(SELECT COUNT(*) FROM {_names.SemanticActivationRecoveryFloors} f LEFT JOIN {_names.SemanticActivationSlots} s
   ON s.definition_id=f.definition_id AND s.binding_id=f.binding_id AND s.key_digest=f.key_digest
   WHERE s.definition_id IS NULL OR s.state<>f.state OR s.slot_generation<>f.slot_generation OR s.authority_json<>f.authority_json);
""";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 0)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private async ValueTask ValidateSemanticRecoveryDependenciesAsync(SqliteConnection connection, SqliteTransaction? transaction,
        SemanticRecoveryRow row, CancellationToken cancellationToken)
    {
        BaseSemanticActivationDefinitionKey definition;
        ImmutableArray<byte> expectedSlotChecksum;
        BaseSemanticActivationScopeBinding? embeddedBinding = null;
        if (row.State == (int)BaseSemanticActivationSlotState.Retired)
        {
            BaseSemanticActivationRetirementAuthority retired = JsonSerializer.Deserialize(row.AuthorityJson,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            definition = retired.Definition;
            expectedSlotChecksum = retired.Checksum;
        }
        else
        {
            BaseSemanticActivationAbsenceAuthority absent = JsonSerializer.Deserialize(row.AuthorityJson,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            definition = new BaseSemanticActivationDefinitionKey
            { Id = absent.Definition.Id, Version = absent.Definition.Version, Checksum = absent.Definition.Checksum };
            expectedSlotChecksum = absent.Checksum;
        }
        await using (SqliteCommand installed = connection.CreateCommand())
        {
            installed.Transaction = transaction;
            installed.CommandText = $"SELECT COUNT(*) FROM {_names.SemanticActivationDefinitions} WHERE definition_id=$id AND definition_version=$version AND definition_checksum=$checksum;";
            installed.Parameters.AddWithValue("$id", definition.Id); installed.Parameters.AddWithValue("$version", definition.Version);
            installed.Parameters.Add("$checksum", SqliteType.Blob).Value = definition.Checksum.ToArray();
            if (Convert.ToInt64(await installed.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        await using (SqliteCommand scope = connection.CreateCommand())
        {
            scope.Transaction = transaction;
            scope.CommandText = $"SELECT binding_json FROM {_names.SemanticActivationScopes} WHERE binding_id=$binding;";
            scope.Parameters.Add("$binding", SqliteType.Blob).Value = row.BindingId;
            if (await scope.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not byte[] json
                || JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding) is not { } binding
                || !binding.BindingId.AsSpan().SequenceEqual(row.BindingId)
                || !CryptographicOperations.FixedTimeEquals(BaseSemanticActivationEvidenceContract.ScopeBindingChecksum(binding).AsSpan(), binding.Checksum.AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            embeddedBinding = binding;
        }
        if (row.State == (int)BaseSemanticActivationSlotState.Retired)
        {
            BaseSemanticActivationRetirementAuthority retired = JsonSerializer.Deserialize(row.AuthorityJson,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)!;
            if (retired.SubjectLifetime is not null && !retired.SubjectLifetime.ScopeBindingId.AsSpan().SequenceEqual(row.BindingId))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        else
        {
            BaseSemanticActivationAbsenceAuthority absent = JsonSerializer.Deserialize(row.AuthorityJson,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)!;
            if (!absent.ScopeBindingId.AsSpan().SequenceEqual(row.BindingId)
                || absent.SubjectLifetime is not null && !absent.SubjectLifetime.ScopeBindingId.AsSpan().SequenceEqual(row.BindingId))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        bool allReceiptNull = row.ReceiptScope is null && row.ReceiptOperation is null && row.ReceiptKey is null
            && row.ReceiptFingerprint is null && row.ReceiptStructuralDigest is null && row.ReceiptResultJson is null
            && row.ReceiptAuthorityChecksum is null;
        bool allReceiptPresent = !string.IsNullOrWhiteSpace(row.ReceiptScope) && !string.IsNullOrWhiteSpace(row.ReceiptOperation)
            && !string.IsNullOrWhiteSpace(row.ReceiptKey) && row.ReceiptFingerprint?.Length == 32
            && row.ReceiptStructuralDigest?.Length == 32 && row.ReceiptResultJson is { Length: > 0 }
            && row.ReceiptAuthorityChecksum?.Length == 32;
        if (row.State == (int)BaseSemanticActivationSlotState.Retired && !allReceiptPresent
            || row.State == (int)BaseSemanticActivationSlotState.CompactedAbsent && !allReceiptNull && !allReceiptPresent)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        if (allReceiptPresent)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    BaseSemanticActivationEvidenceContract.RecoveryReceiptChecksum(row.ReceiptScope!, row.ReceiptOperation!, row.ReceiptKey!,
                        row.ReceiptFingerprint!, row.ReceiptStructuralDigest!, row.ReceiptResultJson!).AsSpan(),
                    row.ReceiptAuthorityChecksum!))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            BaseAtomicReceiptWire wire;
            try { wire = JsonSerializer.Deserialize(row.ReceiptResultJson!, HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire)!; }
            catch (JsonException exception) { throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt, exception); }
            BaseSemanticActivationReceiptEvidence? semantic;
            try { semantic = wire?.Materialize().ModuleMutation?.SemanticActivation; }
            catch (InvalidOperationException exception) { throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt, exception); }
            if (semantic is null || semantic.DefinitionId != definition.Id || semantic.DefinitionVersion != definition.Version
                || !semantic.DefinitionChecksum.AsSpan().SequenceEqual(definition.Checksum.AsSpan())
                || !KeyBytes(semantic.Key).AsSpan().SequenceEqual(row.KeyDigest)
                || semantic.State != (BaseSemanticActivationSlotState)row.State || semantic.SlotGeneration != row.SlotGeneration
                || !semantic.SlotChecksum.AsSpan().SequenceEqual(expectedSlotChecksum.AsSpan())
                || row.State == (int)BaseSemanticActivationSlotState.Retired && semantic.Operation != BaseSemanticActivationOperationKind.Retire
                || !ValidRecoveredSemanticReceipt(semantic))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        _ = embeddedBinding;
    }

    private static bool ValidRecoveredSemanticReceipt(BaseSemanticActivationReceiptEvidence value)
    {
        if (value.SlotChecksum.Length != 32 || value.CommitEvidenceChecksum.Length != 32 || value.Checksum.Length != 32
            || value.JournalPosition <= 0 || value.DefinitionChecksum.Length != 32) return false;
        byte[] commit = SemanticRecoveryHash("base.semanticActivation.commit.v1\0", value.SlotChecksum.ToArray(),
            System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value.JournalPosition) is long reversed
                ? BitConverter.GetBytes(reversed) : []);
        Span<byte> key = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; value.Key.CopyTo(key);
        byte[] checksum = SemanticRecoveryHash("base.semanticActivation.receipt.v1\0", value.DefinitionChecksum.ToArray(),
            key.ToArray(), value.SlotChecksum.ToArray(), commit);
        return CryptographicOperations.FixedTimeEquals(commit, value.CommitEvidenceChecksum.AsSpan())
            && CryptographicOperations.FixedTimeEquals(checksum, value.Checksum.AsSpan());
    }

    private static byte[] SemanticRecoveryHash(string purpose, params byte[][] fields)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(purpose));
        Span<byte> length = stackalloc byte[4];
        foreach (byte[] field in fields)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, field.Length);
            hash.AppendData(length); hash.AppendData(field);
        }
        return hash.GetHashAndReset();
    }

    private async IAsyncEnumerable<SemanticRecoveryRow> ReadSemanticRecoveryRowsAsync(SqliteConnection source,
        SqliteTransaction? transaction,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? afterDefinition = null; byte[]? afterBinding = null; byte[]? afterKey = null;
        while (true)
        {
            var page = new List<SemanticRecoveryRow>(256);
            await using (SqliteCommand command = source.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"""
SELECT definition_id,binding_id,key_digest,state,slot_generation,authority_json,receipt_scope,receipt_operation,receipt_key,receipt_fingerprint,receipt_structural_digest,receipt_result_json,receipt_authority_checksum
FROM {_names.SemanticActivationRecoveryFloors}
WHERE $afterDefinition IS NULL OR (definition_id,binding_id,key_digest)>($afterDefinition,$afterBinding,$afterKey)
ORDER BY definition_id,binding_id,key_digest LIMIT 256;
""";
                command.Parameters.AddWithValue("$afterDefinition", (object?)afterDefinition ?? DBNull.Value);
                command.Parameters.Add("$afterBinding", SqliteType.Blob).Value = (object?)afterBinding ?? DBNull.Value;
                command.Parameters.Add("$afterKey", SqliteType.Blob).Value = (object?)afterKey ?? DBNull.Value;
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    page.Add(new SemanticRecoveryRow(reader.GetString(0), (byte[])reader[1], (byte[])reader[2], reader.GetInt32(3),
                        reader.GetInt64(4), (byte[])reader[5], reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                        reader.IsDBNull(9) ? null : (byte[])reader[9], reader.IsDBNull(10) ? null : (byte[])reader[10],
                        reader.IsDBNull(11) ? null : (byte[])reader[11], reader.IsDBNull(12) ? null : (byte[])reader[12]));
            }
            if (page.Count == 0) yield break;
            foreach (SemanticRecoveryRow row in page) yield return row;
            SemanticRecoveryRow last = page[^1]; afterDefinition = last.DefinitionId; afterBinding = last.BindingId; afterKey = last.KeyDigest;
        }
    }

    private static byte[] RecoveryRowChecksum(SemanticRecoveryRow row)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.recoveryRow.v1\0"u8);
        void Bytes(ReadOnlySpan<byte> value) { Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length); hash.AppendData(value); }
        Bytes(System.Text.Encoding.UTF8.GetBytes(row.DefinitionId)); Bytes(row.BindingId); Bytes(row.KeyDigest);
        Span<byte> integer = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(integer, row.State); hash.AppendData(integer);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(integer, row.SlotGeneration); hash.AppendData(integer); Bytes(row.AuthorityJson);
        Bytes(System.Text.Encoding.UTF8.GetBytes(row.ReceiptScope ?? string.Empty));
        Bytes(System.Text.Encoding.UTF8.GetBytes(row.ReceiptOperation ?? string.Empty));
        Bytes(System.Text.Encoding.UTF8.GetBytes(row.ReceiptKey ?? string.Empty));
        Bytes(row.ReceiptFingerprint ?? []); Bytes(row.ReceiptStructuralDigest ?? []); Bytes(row.ReceiptResultJson ?? []);
        Bytes(row.ReceiptAuthorityChecksum ?? []);
        return hash.GetHashAndReset();
    }

    private SemanticRecoveryRow RebindSemanticRecoveryRow(SemanticRecoveryRow row, long generation, byte[] definitionSet,
        long restoreEpoch, long schemaGeneration)
    {
        BaseSemanticActivationStoreAuthority Rebind(BaseSemanticActivationStoreAuthority store) =>
            ReboundStore(store, generation, definitionSet, restoreEpoch, schemaGeneration);
        if (row.State == (int)BaseSemanticActivationSlotState.Retired)
        {
            BaseSemanticActivationRetirementAuthority value = JsonSerializer.Deserialize(row.AuthorityJson,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            value = value with { StoreAuthority = Rebind(value.StoreAuthority), Checksum = [] };
            value = value with { Checksum = BaseSemanticActivationEvidenceContract.RetirementChecksum(value) };
            return row with { AuthorityJson = JsonSerializer.SerializeToUtf8Bytes(value, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority) };
        }
        BaseSemanticActivationAbsenceAuthority absent = JsonSerializer.Deserialize(row.AuthorityJson,
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)
            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        absent = absent with { StoreAuthority = Rebind(absent.StoreAuthority), Checksum = [] };
        absent = absent with { Checksum = BaseSemanticActivationEvidenceContract.AbsenceChecksum(absent) };
        return row with { AuthorityJson = JsonSerializer.SerializeToUtf8Bytes(absent, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority) };
    }

    private static BaseSemanticActivationStoreAuthority ReboundStore(BaseSemanticActivationStoreAuthority store,
        long generation, byte[] definitionSet, long restoreEpoch, long schemaGeneration)
    {
        BaseSemanticActivationStoreAuthorityRequirement requirement = store.Requirement with
        {
            RestoreEpoch = restoreEpoch,
            SchemaGeneration = schemaGeneration,
            SemanticAuthorityGeneration = generation,
            DefinitionSetChecksum = definitionSet.ToImmutableArray(),
        };
        return BaseSemanticActivationEvidenceContract.CreateStoreAuthority(requirement);
    }

    private void ValidateSemanticStore(BaseSemanticActivationStoreAuthority store, long generation,
        byte[] definitionSet, long restoreEpoch, long schemaGeneration)
    {
        BaseSemanticActivationStoreAuthorityRequirement requirement = store.Requirement;
        if (requirement.ApplicationId != _options.SemanticActivationApplicationId
            || requirement.LogicalStoreId != _options.StoreId || requirement.StoreInstanceId != _options.StoreId
            || requirement.SemanticAuthorityGeneration != generation || requirement.RestoreEpoch != restoreEpoch
            || requirement.SchemaGeneration != schemaGeneration
            || !requirement.DefinitionSetChecksum.AsSpan().SequenceEqual(definitionSet)
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticActivationEvidenceContract.StoreAuthorityChecksum(requirement).AsSpan(), store.Checksum.AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private async ValueTask RequireLiveActivationCorrespondenceAsync(SqliteConnection connection, SqliteTransaction transaction,
        BaseSemanticActivationLiveAuthority live, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT definition_id,definition_version,definition_checksum,canonical_input,input_checksum,scope_kind,scope_value,scope_digest,payload_checksum,fingerprint,state,generation,requested_due_at,effective_due_at,occurrence_id,priority,overlap_key,overlap_policy,eligible,control_checksum FROM {_names.Activations} WHERE activation_id=$id;";
        command.Parameters.AddWithValue("$id", live.ActivationId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || reader.GetString(0) != live.ActivationDefinition.Id || reader.GetInt32(1) != live.ActivationDefinition.Version
            || !((byte[])reader[2]).AsSpan().SequenceEqual(live.ActivationDefinition.Checksum.AsSpan())
            || !((byte[])reader[4]).AsSpan().SequenceEqual(live.InputChecksum.AsSpan())
            || reader.GetInt32(5) != (int)live.Scope.Kind || reader.GetString(6) != (live.Scope.Value ?? string.Empty)
            || !((byte[])reader[7]).AsSpan().SequenceEqual(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                $"base.activation.scope.v2\0{(int)live.Scope.Kind}\n{live.Scope.Value ?? string.Empty}")))
            || !((byte[])reader[8]).AsSpan().SequenceEqual(SHA256.HashData((byte[])reader[3]))
            || !((byte[])reader[9]).AsSpan().SequenceEqual(SHA256.HashData(((byte[])reader[3]).Concat((byte[])reader[4]).ToArray()))
            || !Enum.IsDefined((BaseActivationState)reader.GetInt32(10)) || reader.GetInt64(11) <= 0
            || reader.GetInt64(12) != live.Due.CanonicalUnixMilliseconds || reader.GetInt64(13) != live.Due.CanonicalUnixMilliseconds
            || !reader.IsDBNull(14) || reader.GetInt32(15) != 0 || !reader.IsDBNull(16) || reader.GetInt32(17) != 0
            || !((byte[])reader[19]).AsSpan().SequenceEqual(ActivationControlChecksum(live.ActivationId, reader.GetInt64(11), (BaseActivationState)reader.GetInt32(10))))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        BaseSemanticActivationScopeBinding binding = await ReadScopeBindingAsync(connection, transaction,
            live.ScopeBinding.BindingId, cancellationToken).ConfigureAwait(false);
        if (!ScopeBindingsEqual(binding, live.ScopeBinding) || _subjectScopes is null || !_subjectScopes.Matches(new BaseProtectedSubjectScope
            { Kind = binding.Kind, IndexDigest = binding.SeekDigest.ToArray(), ProtectedCanonicalValue = binding.ProtectedCanonicalScope.ToArray() }, live.Scope))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private void ValidateSemanticRecoveryRow(SemanticRecoveryRow row, long generation, byte[] definitionSet,
        long expectedRestoreEpoch, long expectedSchemaGeneration)
    {
        if (string.IsNullOrWhiteSpace(row.DefinitionId) || row.BindingId.Length != 32 || row.KeyDigest.Length != 32
            || row.SlotGeneration <= 0 || row.State is not ((int)BaseSemanticActivationSlotState.Retired) and not ((int)BaseSemanticActivationSlotState.CompactedAbsent))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        BaseSemanticActivationStoreAuthority store;
        ImmutableArray<byte> checksum;
        if (row.State == (int)BaseSemanticActivationSlotState.Retired)
        {
            BaseSemanticActivationRetirementAuthority value = JsonSerializer.Deserialize(row.AuthorityJson, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            store = value.StoreAuthority; checksum = BaseSemanticActivationEvidenceContract.RetirementChecksum(value);
            if (value.SlotGeneration != row.SlotGeneration || value.Definition.Id != row.DefinitionId
                || !KeyBytes(value.KeyDigest).AsSpan().SequenceEqual(row.KeyDigest)
                || !CryptographicOperations.FixedTimeEquals(checksum.AsSpan(), value.Checksum.AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        else
        {
            BaseSemanticActivationAbsenceAuthority value = JsonSerializer.Deserialize(row.AuthorityJson, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            store = value.StoreAuthority; checksum = BaseSemanticActivationEvidenceContract.AbsenceChecksum(value);
            if (value.FinalSlotGeneration != row.SlotGeneration || value.Definition.Id != row.DefinitionId
                || !KeyBytes(value.Key).AsSpan().SequenceEqual(row.KeyDigest)
                || !CryptographicOperations.FixedTimeEquals(checksum.AsSpan(), value.Checksum.AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        BaseSemanticActivationStoreAuthorityRequirement requirement = store.Requirement;
        if (requirement.SemanticAuthorityGeneration != generation
            || requirement.ApplicationId != _options.SemanticActivationApplicationId
            || requirement.LogicalStoreId != _options.StoreId || requirement.StoreInstanceId != _options.StoreId
            || requirement.RestoreEpoch != expectedRestoreEpoch
            || requirement.SchemaGeneration != expectedSchemaGeneration
            || !requirement.DefinitionSetChecksum.AsSpan().SequenceEqual(definitionSet)
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticActivationEvidenceContract.StoreAuthorityChecksum(requirement).AsSpan(), store.Checksum.AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private static BaseSemanticActivationStoreAuthorityRequirement SemanticStoreRequirement(SemanticRecoveryRow row)
    {
        if (row.State == (int)BaseSemanticActivationSlotState.Retired)
            return JsonSerializer.Deserialize(row.AuthorityJson, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)?.StoreAuthority.Requirement
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        return JsonSerializer.Deserialize(row.AuthorityJson, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)?.StoreAuthority.Requirement
            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private async ValueTask UpsertRestoredSemanticRecoveryRowAsync(SqliteConnection connection, SqliteTransaction transaction,
        SemanticRecoveryRow row, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"""
INSERT INTO {_names.SemanticActivationRecoveryFloors}(definition_id,binding_id,key_digest,state,slot_generation,authority_json,receipt_scope,receipt_operation,receipt_key,receipt_fingerprint,receipt_structural_digest,receipt_result_json,receipt_authority_checksum)
VALUES($definition,$binding,$key,$state,$generation,$authority,$scope,$operation,$receiptKey,$fingerprint,$structural,$result,$receiptAuthority)
ON CONFLICT(definition_id,binding_id,key_digest) DO UPDATE SET state=excluded.state,slot_generation=excluded.slot_generation,authority_json=excluded.authority_json,
 receipt_scope=excluded.receipt_scope,receipt_operation=excluded.receipt_operation,receipt_key=excluded.receipt_key,receipt_fingerprint=excluded.receipt_fingerprint,
 receipt_structural_digest=excluded.receipt_structural_digest,receipt_result_json=excluded.receipt_result_json,receipt_authority_checksum=excluded.receipt_authority_checksum
WHERE (excluded.state=3 AND {_names.SemanticActivationRecoveryFloors}.state<>3) OR excluded.slot_generation>={_names.SemanticActivationRecoveryFloors}.slot_generation;
INSERT INTO {_names.SemanticActivationSlots}(definition_id,binding_id,key_digest,state,slot_generation,activation_id,authority_json)
VALUES($definition,$binding,$key,$state,$generation,NULL,$authority)
ON CONFLICT(definition_id,binding_id,key_digest) DO UPDATE SET state=excluded.state,slot_generation=excluded.slot_generation,activation_id=NULL,authority_json=excluded.authority_json
WHERE (excluded.state=3 AND {_names.SemanticActivationSlots}.state<>3) OR excluded.slot_generation>={_names.SemanticActivationSlots}.slot_generation;
""";
        command.Parameters.AddWithValue("$definition", row.DefinitionId); command.Parameters.Add("$binding", SqliteType.Blob).Value = row.BindingId;
        command.Parameters.Add("$key", SqliteType.Blob).Value = row.KeyDigest; command.Parameters.AddWithValue("$state", row.State);
        command.Parameters.AddWithValue("$generation", row.SlotGeneration); command.Parameters.Add("$authority", SqliteType.Blob).Value = row.AuthorityJson;
        command.Parameters.AddWithValue("$scope", (object?)row.ReceiptScope ?? DBNull.Value); command.Parameters.AddWithValue("$operation", (object?)row.ReceiptOperation ?? DBNull.Value);
        command.Parameters.AddWithValue("$receiptKey", (object?)row.ReceiptKey ?? DBNull.Value); command.Parameters.Add("$fingerprint", SqliteType.Blob).Value = (object?)row.ReceiptFingerprint ?? DBNull.Value;
        command.Parameters.Add("$structural", SqliteType.Blob).Value = (object?)row.ReceiptStructuralDigest ?? DBNull.Value; command.Parameters.Add("$result", SqliteType.Blob).Value = (object?)row.ReceiptResultJson ?? DBNull.Value;
        command.Parameters.Add("$receiptAuthority", SqliteType.Blob).Value = (object?)row.ReceiptAuthorityChecksum ?? DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
