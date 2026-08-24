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

    internal sealed record SemanticRecoveryCertificationEvidence(
        long AuthorityGeneration,
        ImmutableArray<byte> DefinitionSetChecksum,
        long RestoreEpoch,
        long SchemaGeneration,
        long RowCount,
        ImmutableArray<byte> InvariantChecksum);

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
        byte[]? ReceiptAuthorityChecksum,
        byte[]? ReceiptSlotAuthorityJson);

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

    private async ValueTask<SemanticRecoveryCertificationEvidence?> CaptureSemanticRecoveryCertificationEvidenceAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        SemanticRecoverySnapshot? authority = await CaptureSemanticRecoverySnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
        if (authority is null) return null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.recoveryCertificationInvariant.v1\0"u8);
        await foreach (SemanticRecoveryRow row in ReadSemanticRecoveryRowsAsync(connection, null, cancellationToken))
        {
            AppendCertificationField(hash, row.DefinitionId);
            AppendCertificationField(hash, row.BindingId);
            AppendCertificationField(hash, row.KeyDigest);
            AppendCertificationField(hash, row.State);
            AppendCertificationField(hash, row.SlotGeneration);
            AppendCertificationField(hash, NormalizeCertificationAuthority(row.AuthorityJson, row.State));
            AppendCertificationField(hash, row.ReceiptScope);
            AppendCertificationField(hash, row.ReceiptOperation);
            AppendCertificationField(hash, row.ReceiptKey);
            AppendCertificationField(hash, row.ReceiptFingerprint);
            AppendCertificationField(hash, row.ReceiptStructuralDigest);
            AppendCertificationField(hash, row.ReceiptResultJson);
            AppendCertificationField(hash, row.ReceiptAuthorityChecksum);
            // Historical receipt evidence is immutable across restore. Only the current
            // floor authority above is rebound to the new store authority.
            AppendCertificationField(hash, row.ReceiptSlotAuthorityJson);
        }
        return new(authority.AuthorityGeneration, authority.DefinitionSetChecksum, authority.RestoreEpoch,
            authority.SchemaGeneration, authority.RowCount, hash.GetHashAndReset().ToImmutableArray());
    }

    private static byte[] NormalizeCertificationAuthority(byte[] json, int state)
    {
        BaseSemanticActivationStoreAuthority normalizedStore = new()
        {
            Requirement = new()
            {
                ApplicationId = string.Empty, LogicalStoreId = string.Empty, StoreInstanceId = string.Empty,
                RestoreEpoch = 0, SchemaGeneration = 0, SemanticAuthorityGeneration = 0,
                DefinitionSetChecksum = [],
            },
            Checksum = [],
        };
        if (state == (int)BaseSemanticActivationSlotState.Retired)
        {
            BaseSemanticActivationRetirementAuthority value = JsonSerializer.Deserialize(json,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            return JsonSerializer.SerializeToUtf8Bytes(value with { StoreAuthority = normalizedStore, Checksum = [] },
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority);
        }
        BaseSemanticActivationAbsenceAuthority absent = JsonSerializer.Deserialize(json,
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)
            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        return JsonSerializer.SerializeToUtf8Bytes(absent with { StoreAuthority = normalizedStore, Checksum = [] },
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority);
    }

    private static void AppendCertificationField(IncrementalHash hash, string? value) =>
        AppendCertificationField(hash, value is null ? null : System.Text.Encoding.UTF8.GetBytes(value));

    private static void AppendCertificationField(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        AppendCertificationField(hash, bytes.ToArray());
    }

    private static void AppendCertificationField(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        AppendCertificationField(hash, bytes.ToArray());
    }

    private static void AppendCertificationField(IncrementalHash hash, byte[]? value)
    {
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value?.Length ?? -1);
        hash.AppendData(length);
        if (value is not null) hash.AppendData(value);
    }

    private async ValueTask RebindCurrentSemanticSlotsAsync(SqliteConnection connection, SqliteTransaction transaction,
        long artifactGeneration, long resultingGeneration, byte[] artifactDefinitionSet, byte[] resultingDefinitionSet, long artifactRestoreEpoch,
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
                ValidateSemanticStore(value.StoreAuthority, artifactGeneration, artifactDefinitionSet, artifactRestoreEpoch, resultingSchemaGeneration);
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
                BaseSemanticActivationStoreAuthority store = ReboundStore(value.StoreAuthority, resultingGeneration, resultingDefinitionSet, restoreEpoch, resultingSchemaGeneration);
                value = value with { StoreAuthority = store, Checksum = [] };
                value = value with { Checksum = BaseSemanticActivationEvidenceContract.LiveChecksum(value) };
                replacement = JsonSerializer.SerializeToUtf8Bytes(value, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority);
            }
            else
            {
                var row = new SemanticRecoveryRow(definition, binding, key, state, slotGeneration, authority,
                    null, null, null, null, null, null, null, null);
                ValidateSemanticRecoveryRow(row, artifactGeneration, artifactDefinitionSet, artifactRestoreEpoch, resultingSchemaGeneration);
                replacement = RebindSemanticRecoveryRow(row, resultingGeneration, resultingDefinitionSet, restoreEpoch, resultingSchemaGeneration).AuthorityJson;
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
        BaseSemanticRecoveryRestoreAuthority? external,
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
        byte[] artifactDefinitionSet;
        await using (SqliteCommand authority = connection.CreateCommand())
        {
            authority.Transaction = transaction;
            authority.CommandText = $"SELECT value FROM {_names.ProviderState} WHERE key='semantic_activation_definition_set_checksum';";
            if (await authority.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not string text || text.Length != 64)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            artifactDefinitionSet = Convert.FromHexString(text);
        }
        byte[] definitionSet = artifactDefinitionSet;
        bool replacementSet = prior is not null && !prior.DefinitionSetChecksum.AsSpan().SequenceEqual(artifactDefinitionSet);
        if (replacementSet)
        {
            if (!await PriorRemovalAuthorityDominatesArtifactAsync(connection, transaction, recoveryDatabasePath,
                    artifactDefinitionSet, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
            definitionSet = _options.SemanticActivationDefinitionSetChecksum.ToArray();
        }

        await RequireArtifactNegativeCorrespondenceAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await RebindCurrentSemanticSlotsAsync(connection, transaction, artifactGeneration, resultingGeneration,
            artifactDefinitionSet, definitionSet, artifactRestoreEpoch, restoreEpoch, artifactSchemaGeneration, cancellationToken).ConfigureAwait(false);

        await foreach (SemanticRecoveryRow row in ReadSemanticRecoveryRowsAsync(connection, transaction, cancellationToken))
        {
            ValidateSemanticRecoveryRow(row, artifactGeneration, artifactDefinitionSet, artifactRestoreEpoch, artifactSchemaGeneration);
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
            await RestoreHistoricalSemanticTransitionAuthorityAsync(source, connection, transaction, cancellationToken).ConfigureAwait(false);
            await ValidatePublishedSemanticTransitionAuthorityAsync(connection, cancellationToken, transaction).ConfigureAwait(false);
            await RequireExactInstalledSemanticDefinitionSetAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
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
        if (external is not null)
        {
            foreach (BaseSemanticRecoveryPublicationEntry entryPublication in external.Publications)
                await ApplyExternalSemanticRecoveryPublicationAsync(connection, transaction, entryPublication,
                    resultingGeneration, definitionSet, restoreEpoch, artifactSchemaGeneration,
                    resultingActivationGeneration, external.AcceptedNow, cancellationToken).ConfigureAwait(false);
            await using SqliteCommand publication = connection.CreateCommand(); publication.Transaction = transaction;
            publication.CommandText = $"UPDATE {_names.ProviderState} SET value=CASE key WHEN 'semantic_terminal_publication_sequence' THEN $sequence ELSE $checksum END WHERE key IN ('semantic_terminal_publication_sequence','semantic_terminal_publication_checksum');";
            publication.Parameters.AddWithValue("$sequence", external.Head.PublishedSequence);
            publication.Parameters.AddWithValue("$checksum", Convert.ToHexStringLower(external.Head.OrderedEntrySetChecksum.AsSpan()));
            if (await publication.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        }

        await using SqliteCommand publish = connection.CreateCommand(); publish.Transaction = transaction;
        publish.CommandText = $"UPDATE {_names.ProviderState} SET value=$generation WHERE key='semantic_activation_authority_generation' AND CAST(value AS INTEGER)=$artifact;";
        publish.Parameters.AddWithValue("$generation", resultingGeneration); publish.Parameters.AddWithValue("$artifact", artifactGeneration);
        if (await publish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        await using SqliteCommand publishSet = connection.CreateCommand(); publishSet.Transaction = transaction;
        publishSet.CommandText = $"UPDATE {_names.ProviderState} SET value=$checksum WHERE key='semantic_activation_definition_set_checksum';";
        publishSet.Parameters.AddWithValue("$checksum", Convert.ToHexStringLower(definitionSet));
        if (await publishSet.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
    }

    private async ValueTask<bool> PriorRemovalAuthorityDominatesArtifactAsync(SqliteConnection artifact,
        SqliteTransaction transaction, string recoveryDatabasePath, byte[] artifactDefinitionSet, CancellationToken token)
    {
        if (_options.SemanticActivationRemovals.Length == 0) return false;
        await using var prior = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = recoveryDatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await prior.OpenAsync(token).ConfigureAwait(false);
        foreach (BaseSemanticActivationRemovalAuthority removal in _options.SemanticActivationRemovals)
        {
            if (!CryptographicOperations.FixedTimeEquals(removal.ResultingDefinitionSetChecksum.AsSpan(),
                    _options.SemanticActivationDefinitionSetChecksum)) return false;
            await using SqliteCommand tombstone = prior.CreateCommand();
            tombstone.CommandText = $"SELECT removal_authority_json FROM {_names.SemanticActivationRemovedDefinitions} WHERE definition_id=$id AND definition_version=$version;";
            tombstone.Parameters.AddWithValue("$id", removal.From.Id); tombstone.Parameters.AddWithValue("$version", removal.From.Version);
            if (await tombstone.ExecuteScalarAsync(token).ConfigureAwait(false) is not byte[] json) return false;
            BaseSemanticActivationRemovalAuthority? stored = JsonSerializer.Deserialize(json,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRemovalAuthority);
            if (stored is null || !CryptographicOperations.FixedTimeEquals(stored.Checksum.AsSpan(), removal.Checksum.AsSpan())) return false;
        }
        await using SqliteCommand definitions = artifact.CreateCommand(); definitions.Transaction = transaction;
        definitions.CommandText = $"SELECT definition_id,definition_version,definition_checksum FROM {_names.SemanticActivationDefinitions} WHERE execution_enabled=1 ORDER BY definition_id,definition_version;";
        await using SqliteDataReader reader = await definitions.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            string id = reader.GetString(0); int version = reader.GetInt32(1); byte[] checksum = (byte[])reader[2];
            bool current = _options.SemanticActivations.Any(value => value.Id == id && value.Version == version
                && CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(), checksum));
            bool removed = _options.SemanticActivationRemovals.Any(value => value.From.Id == id && value.From.Version == version
                && CryptographicOperations.FixedTimeEquals(value.From.Checksum.AsSpan(), checksum));
            if (!current && !removed) return false;
        }
        return true;
    }

    private async ValueTask RestoreHistoricalSemanticTransitionAuthorityAsync(SqliteConnection source,
        SqliteConnection target, SqliteTransaction transaction, CancellationToken token)
    {
        foreach (string table in new[] { _names.SemanticActivationMigrationHistory, _names.SemanticActivationMigrations,
            _names.SemanticActivationRemovedDefinitionHistory, _names.SemanticActivationRemovedDefinitions })
        {
            await using SqliteCommand clear = target.CreateCommand(); clear.Transaction = transaction;
            clear.CommandText = $"DELETE FROM {table};";
            await clear.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        await using (SqliteCommand clearDefinitions = target.CreateCommand())
        {
            clearDefinitions.Transaction = transaction;
            clearDefinitions.CommandText = $"DELETE FROM {_names.SemanticActivationDefinitions};";
            await clearDefinitions.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        await CopyExactSemanticAuthorityTableAsync(source, target, transaction, _names.SemanticActivationDefinitions,
            ["definition_id","definition_version","definition_checksum","owner_generation","application_id","definition_set_checksum","definition_json","execution_enabled"],
            ["definition_id","definition_version"], null, token).ConfigureAwait(false);
        await CopyExactSemanticAuthorityTableAsync(source, target, transaction, _names.SemanticActivationMigrations,
            ["migration_id","migration_version","from_definition_id","from_version","from_checksum","to_definition_id","to_version","to_checksum","live_count","retired_count","absence_count","negative_checksum","publication_generation","receipt_checksum","authority_checksum"],
            ["migration_id","migration_version"], null, token).ConfigureAwait(false);
        await CopyExactSemanticAuthorityTableAsync(source, target, transaction, _names.SemanticActivationMigrationHistory,
            ["migration_id","migration_version","binding_id","key_digest","state","authority_json"],
            ["migration_id","migration_version","binding_id","key_digest"], null, token).ConfigureAwait(false);
        await CopyExactSemanticAuthorityTableAsync(source, target, transaction, _names.SemanticActivationRemovedDefinitions,
            ["definition_id","definition_version","definition_checksum","removal_id","removal_version","removal_authority_json","absence_count","absence_checksum","publication_generation","receipt_checksum","authority_checksum"],
            ["definition_id","definition_version"], null, token).ConfigureAwait(false);
        await CopyExactSemanticAuthorityTableAsync(source, target, transaction, _names.SemanticActivationRemovedDefinitionHistory,
            ["definition_id","definition_version","binding_id","key_digest","authority_json"],
            ["definition_id","definition_version","binding_id","key_digest"], null, token).ConfigureAwait(false);
    }

    private async ValueTask RequireExactInstalledSemanticDefinitionSetAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken token)
    {
        var actual = new List<(string Id,int Version,byte[] Checksum)>();
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT definition_id,definition_version,definition_checksum FROM {_names.SemanticActivationDefinitions} WHERE execution_enabled=1 ORDER BY definition_id,definition_version;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            actual.Add((reader.GetString(0), reader.GetInt32(1), (byte[])reader[2]));
        BaseSemanticActivationKeyDefinition[] expected = _options.SemanticActivations
            .OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version).ToArray();
        if (actual.Count != expected.Length) throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        for (int index = 0; index < expected.Length; index++)
            if (actual[index].Id != expected[index].Id || actual[index].Version != expected[index].Version
                || !CryptographicOperations.FixedTimeEquals(actual[index].Checksum, expected[index].Checksum.AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
    }

    private static async ValueTask CopyExactSemanticAuthorityTableAsync(SqliteConnection source, SqliteConnection target,
        SqliteTransaction transaction, string table, string[] columns, string[] keys, string? where, CancellationToken token)
    {
        await using SqliteCommand read = source.CreateCommand();
        read.CommandText = $"SELECT {string.Join(',', columns)} FROM {table} {where ?? string.Empty} ORDER BY {string.Join(',', keys)};";
        await using SqliteDataReader reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
        string assignments = string.Join(',', columns.Where(column => !keys.Contains(column, StringComparer.Ordinal))
            .Select(column => $"{column}=excluded.{column}"));
        string exact = string.Join(" AND ", columns.Select(column => $"{table}.{column} IS excluded.{column}"));
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            await using SqliteCommand write = target.CreateCommand(); write.Transaction = transaction;
            write.CommandText = $"INSERT INTO {table}({string.Join(',', columns)}) VALUES({string.Join(',', columns.Select((_, index) => "$p" + index))}) ON CONFLICT({string.Join(',', keys)}) DO UPDATE SET {assignments} WHERE {exact};";
            for (int index = 0; index < columns.Length; index++)
                write.Parameters.AddWithValue("$p" + index, reader.IsDBNull(index) ? DBNull.Value : reader.GetValue(index));
            if (await write.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        }
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
            authority.CommandText = $"SELECT i.store_instance_id,COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch'),0) FROM {_names.SchemaIdentity} i WHERE i.singleton=1;";
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
        SqliteTransaction? transaction, CancellationToken cancellationToken)
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
            && row.ReceiptAuthorityChecksum is null && row.ReceiptSlotAuthorityJson is null;
        bool allReceiptPresent = !string.IsNullOrWhiteSpace(row.ReceiptScope) && !string.IsNullOrWhiteSpace(row.ReceiptOperation)
            && !string.IsNullOrWhiteSpace(row.ReceiptKey) && row.ReceiptFingerprint?.Length == 32
            && row.ReceiptStructuralDigest?.Length == 32 && row.ReceiptResultJson is { Length: > 0 }
            && row.ReceiptAuthorityChecksum?.Length == 32 && row.ReceiptSlotAuthorityJson is { Length: > 0 };
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
            ImmutableArray<byte> historicalSlotChecksum = HistoricalSlotAuthorityChecksum(row, definition);
            if (semantic is null || semantic.DefinitionId != definition.Id || semantic.DefinitionVersion != definition.Version
                || !semantic.DefinitionChecksum.AsSpan().SequenceEqual(definition.Checksum.AsSpan())
                || !KeyBytes(semantic.Key).AsSpan().SequenceEqual(row.KeyDigest)
                || semantic.State != (BaseSemanticActivationSlotState)row.State || semantic.SlotGeneration != row.SlotGeneration
                || !semantic.SlotChecksum.AsSpan().SequenceEqual(historicalSlotChecksum.AsSpan())
                || row.State == (int)BaseSemanticActivationSlotState.Retired && semantic.Operation != BaseSemanticActivationOperationKind.Retire
                || !ValidRecoveredSemanticReceipt(semantic))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        _ = embeddedBinding;
        _ = expectedSlotChecksum;
    }

    private static ImmutableArray<byte> HistoricalSlotAuthorityChecksum(
        SemanticRecoveryRow row, BaseSemanticActivationDefinitionKey definition)
    {
        if (row.ReceiptSlotAuthorityJson is null)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        try
        {
            if (row.State == (int)BaseSemanticActivationSlotState.Retired)
            {
                BaseSemanticActivationRetirementAuthority historical = JsonSerializer.Deserialize(row.ReceiptSlotAuthorityJson,
                    HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                    ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                if (historical.Definition.Id != definition.Id || historical.Definition.Version != definition.Version
                    || !historical.Definition.Checksum.AsSpan().SequenceEqual(definition.Checksum.AsSpan())
                    || historical.SlotGeneration != row.SlotGeneration
                    || !KeyBytes(historical.KeyDigest).AsSpan().SequenceEqual(row.KeyDigest)
                    || !CryptographicOperations.FixedTimeEquals(historical.Checksum.AsSpan(),
                        BaseSemanticActivationEvidenceContract.RetirementChecksum(historical).AsSpan()))
                    throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                return historical.Checksum;
            }
            BaseSemanticActivationAbsenceAuthority absent = JsonSerializer.Deserialize(row.ReceiptSlotAuthorityJson,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            if (absent.Definition.Id != definition.Id || absent.Definition.Version != definition.Version
                || !absent.Definition.Checksum.AsSpan().SequenceEqual(definition.Checksum.AsSpan())
                || absent.FinalSlotGeneration != row.SlotGeneration
                || !KeyBytes(absent.Key).AsSpan().SequenceEqual(row.KeyDigest)
                || !CryptographicOperations.FixedTimeEquals(absent.Checksum.AsSpan(),
                    BaseSemanticActivationEvidenceContract.AbsenceChecksum(absent).AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            return absent.Checksum;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt, exception);
        }
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
SELECT definition_id,binding_id,key_digest,state,slot_generation,authority_json,receipt_scope,receipt_operation,receipt_key,receipt_fingerprint,receipt_structural_digest,receipt_result_json,receipt_authority_checksum,receipt_slot_authority_json
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
                        reader.IsDBNull(11) ? null : (byte[])reader[11], reader.IsDBNull(12) ? null : (byte[])reader[12],
                        reader.IsDBNull(13) ? null : (byte[])reader[13]));
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
        Bytes(row.ReceiptSlotAuthorityJson ?? []);
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
            || requirement.LogicalStoreId != _options.StoreId || requirement.StoreInstanceId != CurrentStoreInstanceId
            || requirement.SemanticAuthorityGeneration != generation || requirement.RestoreEpoch != restoreEpoch
            || requirement.SchemaGeneration != schemaGeneration
            || !requirement.DefinitionSetChecksum.AsSpan().SequenceEqual(definitionSet)
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticActivationEvidenceContract.StoreAuthorityChecksum(requirement).AsSpan(), store.Checksum.AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private async ValueTask RequireLiveActivationCorrespondenceAsync(SqliteConnection connection, SqliteTransaction transaction,
        BaseSemanticActivationLiveAuthority live, CancellationToken cancellationToken, bool requireCurrentScopeBinding = true)
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
        if (requireCurrentScopeBinding)
        {
            BaseSemanticActivationScopeBinding binding = await ReadScopeBindingAsync(connection, transaction,
                live.ScopeBinding.BindingId, cancellationToken).ConfigureAwait(false);
            if (!ScopeBindingsEqual(binding, live.ScopeBinding) || _subjectScopes is null || !_subjectScopes.Matches(new BaseProtectedSubjectScope
                { Kind = binding.Kind, IndexDigest = binding.SeekDigest.ToArray(), ProtectedCanonicalValue = binding.ProtectedCanonicalScope.ToArray() }, live.Scope))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
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
            || requirement.LogicalStoreId != _options.StoreId || requirement.StoreInstanceId != CurrentStoreInstanceId
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
        if (row.State == (int)BaseSemanticActivationSlotState.CompactedAbsent)
            await SuppressArtifactLiveActivationAsync(connection, transaction, row, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"""
INSERT INTO {_names.SemanticActivationRecoveryFloors}(definition_id,binding_id,key_digest,state,slot_generation,authority_json,receipt_scope,receipt_operation,receipt_key,receipt_fingerprint,receipt_structural_digest,receipt_result_json,receipt_authority_checksum,receipt_slot_authority_json)
VALUES($definition,$binding,$key,$state,$generation,$authority,$scope,$operation,$receiptKey,$fingerprint,$structural,$result,$receiptAuthority,$receiptSlotAuthority)
ON CONFLICT(definition_id,binding_id,key_digest) DO UPDATE SET state=excluded.state,slot_generation=excluded.slot_generation,authority_json=excluded.authority_json,
 receipt_scope=excluded.receipt_scope,receipt_operation=excluded.receipt_operation,receipt_key=excluded.receipt_key,receipt_fingerprint=excluded.receipt_fingerprint,
 receipt_structural_digest=excluded.receipt_structural_digest,receipt_result_json=excluded.receipt_result_json,receipt_authority_checksum=excluded.receipt_authority_checksum,
 receipt_slot_authority_json=excluded.receipt_slot_authority_json
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
        command.Parameters.Add("$receiptSlotAuthority", SqliteType.Blob).Value = (object?)row.ReceiptSlotAuthorityJson ?? DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SuppressArtifactLiveActivationAsync(SqliteConnection connection, SqliteTransaction transaction,
        SemanticRecoveryRow dominating, CancellationToken token)
    {
        string? activationId = null; byte[]? authority = null;
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"SELECT activation_id,authority_json FROM {_names.SemanticActivationSlots} WHERE definition_id=$definition AND binding_id=$binding AND key_digest=$key AND state=1;";
            read.Parameters.AddWithValue("$definition", dominating.DefinitionId);
            read.Parameters.Add("$binding", SqliteType.Blob).Value = dominating.BindingId;
            read.Parameters.Add("$key", SqliteType.Blob).Value = dominating.KeyDigest;
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            { activationId = reader.IsDBNull(0) ? null : reader.GetString(0); authority = (byte[])reader[1]; }
        }
        if (authority is null) return;
        BaseSemanticActivationLiveAuthority live = JsonSerializer.Deserialize(authority,
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)
            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        if (activationId is null || activationId != live.ActivationId || live.Definition.Id != dominating.DefinitionId
            || !live.ScopeBinding.BindingId.AsSpan().SequenceEqual(dominating.BindingId)
            || !KeyBytes(live.KeyDigest).AsSpan().SequenceEqual(dominating.KeyDigest)
            || !CryptographicOperations.FixedTimeEquals(live.Checksum.AsSpan(),
                BaseSemanticActivationEvidenceContract.LiveChecksum(live).AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        await RequireLiveActivationCorrespondenceAsync(connection, transaction, live, token).ConfigureAwait(false);
        await using SqliteCommand suppress = connection.CreateCommand(); suppress.Transaction = transaction;
        suppress.CommandText = $"DELETE FROM {_names.ActivationEffects} WHERE activation_id=$id; DELETE FROM {_names.ActivationReceipts} WHERE activation_id=$id; DELETE FROM {_names.Activations} WHERE activation_id=$id;";
        suppress.Parameters.AddWithValue("$id", activationId);
        await suppress.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private async ValueTask ApplyExternalSemanticRecoveryPublicationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BaseSemanticRecoveryPublicationEntry publication,
        long semanticGeneration,
        byte[] definitionSet,
        long restoreEpoch,
        long schemaGeneration,
        long activationGeneration,
        long acceptedNow,
        CancellationToken cancellationToken)
    {
        if (!BaseSemanticRecoveryAuthorityContract.LocalReceiptEnvelopeIsValid(publication.LocalReceipt)
            || publication.Entry.State != BaseSemanticActivationSlotState.Retired
            || !BaseSemanticRecoveryAuthorityContract.TerminalActivationIsValid(publication.Entry.TerminalActivation)
            || !CryptographicOperations.FixedTimeEquals(
                BaseSemanticRecoveryAuthorityContract.RecoveryEntryChecksum(publication.Entry).AsSpan(),
                publication.Entry.Checksum.AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);

        BaseSemanticActivationRetirementAuthority retired = JsonSerializer.Deserialize(
            publication.Entry.AuthorityBytes.AsSpan(),
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        BaseSemanticRecoveryTerminalActivationAuthority terminal = publication.Entry.TerminalActivation;
        BaseSemanticActivationKeyDefinition? installedSemantic = _options.SemanticActivations.SingleOrDefault(value =>
            value.Id == retired.Definition.Id && value.Version == retired.Definition.Version
            && value.Checksum.AsSpan().SequenceEqual(retired.Definition.Checksum.AsSpan()));
        if (installedSemantic is null
            || installedSemantic.RetirementOperation.OperationId != publication.Entry.RetirementOperation.OperationId
            || installedSemantic.RetirementOperation.OperationVersion != publication.Entry.RetirementOperation.OperationVersion
            || installedSemantic.RetirementOperation.OperationChecksum != publication.Entry.RetirementOperation.OperationChecksum
            || installedSemantic.Activation.Id != terminal.Payload.Definition.Id
            || installedSemantic.Activation.Version != terminal.Payload.Definition.Version
            || !installedSemantic.Activation.Checksum.AsSpan().SequenceEqual(terminal.Payload.Definition.Checksum.AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.GraphChanged);
        byte[] key = new byte[BaseSemanticActivationKeyDigest.Length]; retired.KeyDigest.CopyTo(key);
        if (retired.Definition.Id != publication.Entry.Definition.Id
            || retired.Definition.Version != publication.Entry.Definition.Version
            || !retired.Definition.Checksum.AsSpan().SequenceEqual(publication.Entry.Definition.Checksum.AsSpan())
            || retired.ActivationId != terminal.Payload.ActivationId
            || retired.TerminalState != terminal.State || retired.TerminalActivationGeneration != terminal.Generation
            || !retired.TerminalActivationChecksum.AsSpan().SequenceEqual(terminal.ControlChecksum.AsSpan())
            || !retired.CompletionReceiptChecksum.AsSpan().SequenceEqual(terminal.TerminalReceipt.AuthorityChecksum.AsSpan())
            || retired.SlotGeneration != publication.Entry.SlotGeneration
            || !key.AsSpan().SequenceEqual(publication.Entry.Boundary.Key.ToArray())
            || !publication.Entry.ScopeBinding.BindingId.AsSpan().SequenceEqual(publication.Entry.Boundary.ScopeBindingId.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(
                BaseSemanticActivationEvidenceContract.RetirementChecksum(retired).AsSpan(), retired.Checksum.AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);

        if (_subjectScopes is null || _subjectScopeProtectionKey is null || _subjectScopeProtectionKeyId is null)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.ProviderContractInvalid);
        BaseProtectedSubjectScope protectedScope = _subjectScopes.Protect(terminal.Payload.Scope, _subjectScopeProtectionKey.Value);
        BaseSemanticActivationScopeBinding binding = BaseSemanticActivationEvidenceContract.CreateScopeBinding(
            terminal.Payload.Scope.Kind, publication.Entry.ScopeBinding.BindingId.AsSpan(),
            protectedScope.ProtectedCanonicalValue, protectedScope.IndexDigest,
            _subjectScopeProtectionKeyId, _subjectScopeProtectionKey.Value);
        await using (SqliteCommand priorScope = connection.CreateCommand())
        {
            priorScope.Transaction = transaction;
            priorScope.CommandText = $"SELECT binding_json FROM {_names.SemanticActivationScopes} WHERE binding_id=$binding;";
            priorScope.Parameters.Add("$binding", SqliteType.Blob).Value = binding.BindingId.ToArray();
            if (await priorScope.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is byte[] priorJson)
            {
                BaseSemanticActivationScopeBinding prior = JsonSerializer.Deserialize(priorJson,
                    HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding)
                    ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                if (prior.Kind != terminal.Payload.Scope.Kind || _subjectScopes is null
                    || !_subjectScopes.Matches(new BaseProtectedSubjectScope
                    {
                        Kind = prior.Kind,
                        IndexDigest = prior.SeekDigest.ToArray(),
                        ProtectedCanonicalValue = prior.ProtectedCanonicalScope.ToArray(),
                    }, terminal.Payload.Scope))
                    throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
                await priorScope.DisposeAsync().ConfigureAwait(false);
                await using SqliteCommand remove = connection.CreateCommand(); remove.Transaction = transaction;
                remove.CommandText = $"DELETE FROM {_names.SemanticActivationScopes} WHERE binding_id=$binding;";
                remove.Parameters.Add("$binding", SqliteType.Blob).Value = binding.BindingId.ToArray();
                if (await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
            }
        }
        byte[] bindingJson = JsonSerializer.SerializeToUtf8Bytes(binding,
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding);
        await using (SqliteCommand scope = connection.CreateCommand())
        {
            scope.Transaction = transaction;
            scope.CommandText = $"INSERT INTO {_names.SemanticActivationScopes}(scope_kind,seek_digest,binding_id,binding_json) VALUES($kind,$seek,$binding,$json) ON CONFLICT(scope_kind,seek_digest) DO UPDATE SET binding_id=excluded.binding_id,binding_json=excluded.binding_json WHERE binding_id=excluded.binding_id;";
            scope.Parameters.AddWithValue("$kind", (int)binding.Kind);
            scope.Parameters.Add("$seek", SqliteType.Blob).Value = binding.SeekDigest.ToArray();
            scope.Parameters.Add("$binding", SqliteType.Blob).Value = binding.BindingId.ToArray();
            scope.Parameters.Add("$json", SqliteType.Blob).Value = bindingJson;
            if (await scope.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        }

        bool artifactHasActivation = await RequireArtifactActivationCompatibleWithTerminalAsync(
            connection, transaction, terminal, cancellationToken).ConfigureAwait(false);

        if (artifactHasActivation) await using (SqliteCommand clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = $"DELETE FROM {_names.ActivationEffects} WHERE activation_id=$id;";
            clear.Parameters.AddWithValue("$id", terminal.Payload.ActivationId);
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        byte[] scopeDigest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"base.activation.scope.v2\0{(int)terminal.Payload.Scope.Kind}\n{terminal.Payload.Scope.Value ?? string.Empty}"));
        if (artifactHasActivation) await using (SqliteCommand activation = connection.CreateCommand())
        {
            activation.Transaction = transaction;
            activation.CommandText = $"""
INSERT INTO {_names.Activations}(activation_id,definition_id,definition_version,definition_checksum,canonical_input,input_checksum,scope_kind,scope_value,scope_digest,payload_checksum,fingerprint,state,generation,requested_due_at,effective_due_at,occurrence_id,priority,overlap_key,overlap_policy,eligible,control_checksum,attempt_number,claim_epoch,claim_fence,claim_worker,lease_revision,lease_expires_at,canonical_result,terminal_receipt_checksum)
VALUES($id,$definition,$version,$definitionChecksum,$input,$inputChecksum,$scopeKind,$scopeValue,$scopeDigest,$payloadChecksum,$fingerprint,$state,$generation,$requested,$effective,NULL,$priority,$overlapKey,$overlapPolicy,0,$control,$attempt,$claimEpoch,NULL,NULL,NULL,NULL,$result,$terminalReceipt)
ON CONFLICT(activation_id) DO UPDATE SET definition_id=excluded.definition_id,definition_version=excluded.definition_version,definition_checksum=excluded.definition_checksum,canonical_input=excluded.canonical_input,input_checksum=excluded.input_checksum,scope_kind=excluded.scope_kind,scope_value=excluded.scope_value,scope_digest=excluded.scope_digest,payload_checksum=excluded.payload_checksum,fingerprint=excluded.fingerprint,state=excluded.state,generation=excluded.generation,requested_due_at=excluded.requested_due_at,effective_due_at=excluded.effective_due_at,occurrence_id=NULL,priority=excluded.priority,overlap_key=excluded.overlap_key,overlap_policy=excluded.overlap_policy,eligible=0,control_checksum=excluded.control_checksum,attempt_number=excluded.attempt_number,claim_epoch=excluded.claim_epoch,claim_fence=NULL,claim_worker=NULL,lease_revision=NULL,lease_expires_at=NULL,canonical_result=excluded.canonical_result,terminal_receipt_checksum=excluded.terminal_receipt_checksum;
""";
            activation.Parameters.AddWithValue("$id", terminal.Payload.ActivationId);
            activation.Parameters.AddWithValue("$definition", terminal.Payload.Definition.Id);
            activation.Parameters.AddWithValue("$version", terminal.Payload.Definition.Version);
            activation.Parameters.Add("$definitionChecksum", SqliteType.Blob).Value = terminal.Payload.Definition.Checksum.ToArray();
            activation.Parameters.Add("$input", SqliteType.Blob).Value = terminal.Payload.CanonicalInput.ToArray();
            activation.Parameters.Add("$inputChecksum", SqliteType.Blob).Value = terminal.Payload.InputChecksum.ToArray();
            activation.Parameters.AddWithValue("$scopeKind", (int)terminal.Payload.Scope.Kind);
            activation.Parameters.AddWithValue("$scopeValue", terminal.Payload.Scope.Value ?? string.Empty);
            activation.Parameters.Add("$scopeDigest", SqliteType.Blob).Value = scopeDigest;
            activation.Parameters.Add("$payloadChecksum", SqliteType.Blob).Value = terminal.Payload.Checksum.ToArray();
            activation.Parameters.Add("$fingerprint", SqliteType.Blob).Value = terminal.CreationFingerprint.ToArray();
            activation.Parameters.AddWithValue("$state", (int)terminal.State);
            activation.Parameters.AddWithValue("$generation", terminal.Generation);
            activation.Parameters.AddWithValue("$requested", terminal.Payload.RequestedDueAt);
            activation.Parameters.AddWithValue("$effective", terminal.Payload.EffectiveDueAt);
            activation.Parameters.AddWithValue("$priority", terminal.Priority);
            activation.Parameters.Add("$overlapKey", SqliteType.Blob).Value = terminal.OverlapKey is { } overlap ? overlap.ToArray() : DBNull.Value;
            activation.Parameters.AddWithValue("$overlapPolicy", (int)terminal.OverlapPolicy);
            activation.Parameters.Add("$control", SqliteType.Blob).Value = terminal.ControlChecksum.ToArray();
            activation.Parameters.AddWithValue("$attempt", terminal.AttemptNumber);
            activation.Parameters.AddWithValue("$claimEpoch", terminal.ClaimEpoch);
            activation.Parameters.Add("$result", SqliteType.Blob).Value = terminal.CanonicalResult is { } result ? result.ToArray() : DBNull.Value;
            activation.Parameters.Add("$terminalReceipt", SqliteType.Blob).Value = terminal.TerminalReceipt.AuthorityChecksum.ToArray();
            await activation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        BaseSemanticRecoveryTerminalReceiptEvidence terminalReceipt = terminal.TerminalReceipt;
        if (artifactHasActivation) await using (SqliteCommand activationReceipt = connection.CreateCommand())
        {
            activationReceipt.Transaction = transaction;
            activationReceipt.CommandText = $"INSERT INTO {_names.ActivationReceipts}(receipt_key,operation_kind,fingerprint,result_json,result_checksum,activation_id,authority_checksum) VALUES($key,$kind,$fingerprint,$result,$resultChecksum,$id,$authority) ON CONFLICT(receipt_key) DO UPDATE SET operation_kind=excluded.operation_kind,fingerprint=excluded.fingerprint,result_json=excluded.result_json,result_checksum=excluded.result_checksum,activation_id=excluded.activation_id,authority_checksum=excluded.authority_checksum WHERE operation_kind=excluded.operation_kind AND fingerprint=excluded.fingerprint AND result_json=excluded.result_json AND result_checksum=excluded.result_checksum AND activation_id=excluded.activation_id AND authority_checksum=excluded.authority_checksum;";
            activationReceipt.Parameters.AddWithValue("$key", terminalReceipt.ReceiptKey);
            activationReceipt.Parameters.AddWithValue("$kind", terminalReceipt.OperationKind);
            activationReceipt.Parameters.Add("$fingerprint", SqliteType.Blob).Value = terminalReceipt.Fingerprint.ToArray();
            activationReceipt.Parameters.Add("$result", SqliteType.Blob).Value = terminalReceipt.ResultBytes.ToArray();
            activationReceipt.Parameters.Add("$resultChecksum", SqliteType.Blob).Value = terminalReceipt.ResultChecksum.ToArray();
            activationReceipt.Parameters.AddWithValue("$id", terminal.Payload.ActivationId);
            activationReceipt.Parameters.Add("$authority", SqliteType.Blob).Value = terminalReceipt.AuthorityChecksum.ToArray();
            if (await activationReceipt.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        }

        if (retired.SubjectLifetime is { } lifetime)
            retired = retired with
            {
                SubjectLifetime = await ReadRestoredSubjectLifetimeAsync(
                    connection, transaction, lifetime, cancellationToken).ConfigureAwait(false),
                Checksum = [],
            };
        BaseSemanticActivationStoreAuthority store = ReboundStore(retired.StoreAuthority, semanticGeneration,
            definitionSet, restoreEpoch, schemaGeneration);
        retired = retired with { StoreAuthority = store, Checksum = [] };
        retired = retired with { Checksum = BaseSemanticActivationEvidenceContract.RetirementChecksum(retired) };
        byte[] authorityJson = JsonSerializer.SerializeToUtf8Bytes(retired,
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority);
        BaseSemanticRecoveryLocalReceiptEnvelope envelope = publication.LocalReceipt;
        byte[] receiptAuthority = BaseSemanticActivationEvidenceContract.RecoveryReceiptChecksum(
            envelope.Identity.Scope, envelope.Identity.Operation, envelope.Identity.IdempotencyKey,
            envelope.Identity.Fingerprint.ToArray(), envelope.StructuralDigest.ToArray(), envelope.ReceiptBytes.ToArray()).ToArray();
        var row = new SemanticRecoveryRow(publication.Entry.Definition.Id, binding.BindingId.ToArray(), key,
            (int)BaseSemanticActivationSlotState.Retired, retired.SlotGeneration, authorityJson,
            envelope.Identity.Scope, envelope.Identity.Operation, envelope.Identity.IdempotencyKey,
            envelope.Identity.Fingerprint.ToArray(), envelope.StructuralDigest.ToArray(), envelope.ReceiptBytes.ToArray(), receiptAuthority,
            publication.Entry.AuthorityBytes.ToArray());
        await UpsertRestoredSemanticRecoveryRowAsync(connection, transaction, row, cancellationToken).ConfigureAwait(false);

        if (envelope.ExpiresAt.ToUnixTimeMilliseconds() > acceptedNow)
        {
            await using SqliteCommand operationReceipt = connection.CreateCommand(); operationReceipt.Transaction = transaction;
            operationReceipt.CommandText = $"INSERT INTO {_names.OperationReceipts}(scope,operation,idempotency_key,fingerprint,structural_digest,result_json,result_format_version,schema_generation,store_instance_id,committed_at,expires_at) VALUES($scope,$operation,$key,$fingerprint,$structural,$result,$format,$schema,$store,$committed,$expires) ON CONFLICT(scope,operation,idempotency_key) DO UPDATE SET fingerprint=excluded.fingerprint WHERE fingerprint=excluded.fingerprint AND structural_digest=excluded.structural_digest AND result_json=excluded.result_json AND result_format_version=excluded.result_format_version AND schema_generation=excluded.schema_generation AND store_instance_id=excluded.store_instance_id AND committed_at=excluded.committed_at AND expires_at=excluded.expires_at;";
            operationReceipt.Parameters.AddWithValue("$scope", envelope.Identity.Scope); operationReceipt.Parameters.AddWithValue("$operation", envelope.Identity.Operation);
            operationReceipt.Parameters.AddWithValue("$key", envelope.Identity.IdempotencyKey); operationReceipt.Parameters.Add("$fingerprint", SqliteType.Blob).Value = envelope.Identity.Fingerprint.ToArray();
            operationReceipt.Parameters.Add("$structural", SqliteType.Blob).Value = envelope.StructuralDigest.ToArray(); operationReceipt.Parameters.Add("$result", SqliteType.Blob).Value = envelope.ReceiptBytes.ToArray();
            operationReceipt.Parameters.AddWithValue("$format", envelope.ReceiptFormatVersion); operationReceipt.Parameters.AddWithValue("$schema", envelope.SchemaGeneration);
            operationReceipt.Parameters.AddWithValue("$store", envelope.StoreInstanceId); operationReceipt.Parameters.AddWithValue("$committed", envelope.CommittedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            operationReceipt.Parameters.AddWithValue("$expires", envelope.ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            if (await operationReceipt.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        }
        _ = activationGeneration;
    }

    private async ValueTask<bool> RequireArtifactActivationCompatibleWithTerminalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BaseSemanticRecoveryTerminalActivationAuthority terminal,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT definition_id,definition_version,definition_checksum,canonical_input,input_checksum,scope_kind,scope_value,payload_checksum,fingerprint,requested_due_at,effective_due_at,occurrence_id,priority,overlap_key,overlap_policy FROM {_names.Activations} WHERE activation_id=$id;";
        command.Parameters.AddWithValue("$id", terminal.Payload.ActivationId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return false;
        if (reader.GetString(0) != terminal.Payload.Definition.Id
            || reader.GetInt32(1) != terminal.Payload.Definition.Version
            || !((byte[])reader.GetValue(2)).AsSpan().SequenceEqual(terminal.Payload.Definition.Checksum.AsSpan())
            || !((byte[])reader.GetValue(3)).AsSpan().SequenceEqual(terminal.Payload.CanonicalInput.AsSpan())
            || !((byte[])reader.GetValue(4)).AsSpan().SequenceEqual(terminal.Payload.InputChecksum.AsSpan())
            || reader.GetInt32(5) != (int)terminal.Payload.Scope.Kind
            || reader.GetString(6) != (terminal.Payload.Scope.Value ?? string.Empty)
            || !((byte[])reader.GetValue(7)).AsSpan().SequenceEqual(terminal.Payload.Checksum.AsSpan())
            || !((byte[])reader.GetValue(8)).AsSpan().SequenceEqual(terminal.CreationFingerprint.AsSpan())
            || reader.GetInt64(9) != terminal.Payload.RequestedDueAt
            || reader.GetInt64(10) != terminal.Payload.EffectiveDueAt
            || !reader.IsDBNull(11)
            || reader.GetInt32(12) != terminal.Priority
            || !(reader.IsDBNull(13) ? terminal.OverlapKey is null
                : terminal.OverlapKey is { } overlap && ((byte[])reader.GetValue(13)).AsSpan().SequenceEqual(overlap.AsSpan()))
            || reader.GetInt32(14) != (int)terminal.OverlapPolicy)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.RestoreConflict);
        return true;
    }
}
