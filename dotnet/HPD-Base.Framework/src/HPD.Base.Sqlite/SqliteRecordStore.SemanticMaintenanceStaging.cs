using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore
{
    private async ValueTask<BaseSemanticActivationMaintenanceResult> ExecuteStagedSemanticMaintenanceAsync(
        SqliteConnection connection, SqliteTransaction transaction, BaseSemanticActivationMaintenanceRequest request,
        byte[] fingerprint, BaseSemanticActivationMaintenanceCheckpoint? prior, CancellationToken token)
    {
        string maintenanceId = Convert.ToHexStringLower(fingerprint);
        int kind = request is BaseSemanticActivationCompactRequest ? 1 : 2;
        BaseSemanticActivationMaintenanceCheckpoint checkpoint = prior ?? NewCheckpoint(request, fingerprint, maintenanceId, kind);
        if (!CheckpointMatches(checkpoint, request, fingerprint, maintenanceId, kind))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);

        (long stagedRows, long stagedBytes, byte[] stagedRolling, BaseSemanticActivationRecoveryBoundary? stagedAfter) =
            await RecomputeStageAsync(connection, transaction, maintenanceId, request.Definition.Id, token).ConfigureAwait(false);
        if (stagedRows != checkpoint.CompletedRows || stagedBytes != checkpoint.CompletedBytes
            || checkpoint.CompletedPages < 0 || !CryptographicOperations.FixedTimeEquals(stagedRolling, checkpoint.RollingChecksum.AsSpan())
            || !BoundaryEqual(stagedAfter, checkpoint.After))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);

        if (prior is null)
        {
            await using SqliteCommand close = connection.CreateCommand(); close.Transaction = transaction;
            close.CommandText = $"UPDATE {_names.SemanticActivationDefinitions} SET execution_enabled=0 WHERE definition_id=$id;";
            close.Parameters.AddWithValue("$id", request.Definition.Id);
            if (await close.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }

        List<SemanticStageRow> page = await ReadSemanticStagePageAsync(connection, transaction, request, checkpoint.After, token).ConfigureAwait(false);
        bool hasMore = page.Count > request.Limits.PageSize;
        if (hasMore) page.RemoveAt(page.Count - 1);
        foreach (SemanticStageRow row in page)
            await InsertSemanticStageRowAsync(connection, transaction, maintenanceId, request.Definition.Id, row, token).ConfigureAwait(false);

        (stagedRows, stagedBytes, stagedRolling, stagedAfter) =
            await RecomputeStageAsync(connection, transaction, maintenanceId, request.Definition.Id, token).ConfigureAwait(false);
        if (stagedRows > request.Limits.MaximumRows || stagedBytes > request.Limits.MaximumBytes)
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.BudgetExceeded,
                "The semantic activation operation exceeded its installed limits.");

        int completedPages = checked(checkpoint.CompletedPages + 1);
        if (completedPages > request.Limits.MaximumPages)
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.BudgetExceeded,
                "The semantic activation operation exceeded its installed limits.");
        if (hasMore)
        {
            BaseSemanticActivationMaintenanceCheckpoint next = CreateCheckpoint(request, fingerprint, maintenanceId, kind,
                stagedAfter, completedPages, stagedRows, stagedBytes, stagedRolling);
            return InProgressMaintenance(request.ExpectedSemanticAuthorityGeneration, next);
        }

        BaseSemanticActivationMaintenanceResult completed = request switch
        {
            BaseSemanticActivationCompactRequest compact => await PublishStagedCompactionAsync(connection, transaction,
                compact, maintenanceId, stagedRows, stagedBytes, stagedRolling, token).ConfigureAwait(false),
            BaseSemanticActivationMigrateRequest migrate => await PublishStagedMigrationAsync(connection, transaction,
                migrate, maintenanceId, stagedRows, stagedBytes, stagedRolling, fingerprint, token).ConfigureAwait(false),
            _ => throw new InvalidDataException(BaseSemanticActivationErrorCodes.ProviderContractInvalid),
        };
        await using SqliteCommand clear = connection.CreateCommand(); clear.Transaction = transaction;
        clear.CommandText = $"DELETE FROM {_names.SemanticActivationRewriteStage} WHERE maintenance_id=$maintenance;";
        clear.Parameters.AddWithValue("$maintenance", maintenanceId);
        await clear.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        return completed;
    }

    private async ValueTask<List<SemanticStageRow>> ReadSemanticStagePageAsync(SqliteConnection connection,
        SqliteTransaction transaction, BaseSemanticActivationMaintenanceRequest request,
        BaseSemanticActivationRecoveryBoundary? after, CancellationToken token)
    {
        int sourceState = request is BaseSemanticActivationCompactRequest ? 2 : 1;
        var rows = new List<SemanticStageRow>(checked(request.Limits.PageSize + 1));
        await using SqliteCommand select = connection.CreateCommand(); select.Transaction = transaction;
        select.CommandText = $"SELECT binding_id,key_digest,slot_generation,activation_id,authority_json FROM {_names.SemanticActivationSlots} WHERE definition_id=$id AND state=$state AND ($binding IS NULL OR binding_id>$binding OR (binding_id=$binding AND key_digest>$key)) ORDER BY binding_id,key_digest LIMIT $take;";
        select.Parameters.AddWithValue("$id", request.Definition.Id); select.Parameters.AddWithValue("$state", sourceState);
        select.Parameters.Add("$binding", SqliteType.Blob).Value = after is null ? DBNull.Value : after.ScopeBindingId.ToArray();
        select.Parameters.Add("$key", SqliteType.Blob).Value = after is null ? DBNull.Value : KeyBytes(after.Key);
        select.Parameters.AddWithValue("$take", checked(request.Limits.PageSize + 1));
        await using SqliteDataReader reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            byte[] binding = (byte[])reader[0]; byte[] key = (byte[])reader[1]; long generation = reader.GetInt64(2);
            string? activationId = reader.IsDBNull(3) ? null : reader.GetString(3); byte[] source = (byte[])reader[4];
            if (request is BaseSemanticActivationCompactRequest compact)
            {
                BaseSemanticActivationRetirementAuthority retired = JsonSerializer.Deserialize(source,
                    HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                    ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                BaseSemanticActivationKeyDefinition definition = _options.SemanticActivations.Single(value =>
                    value.Id == compact.Definition.Id && value.Version == compact.Definition.Version
                    && value.Checksum.AsSpan().SequenceEqual(compact.Definition.Checksum.AsSpan()));
                if (definition.Compaction is not BaseSemanticActivationSubjectRetirementCompaction contract
                    || retired.SubjectLifetime is not { } lifetime || lifetime.ContractId != contract.SubjectContract.ContractId
                    || lifetime.ContractVersion != contract.SubjectContract.ContractVersion
                    || !CryptographicOperations.FixedTimeEquals(lifetime.ContractChecksum.AsSpan(), contract.SubjectContract.ContractChecksum.AsSpan())
                    || !await TerminalLifetimeExistsAsync(connection, transaction, binding, retired, token).ConfigureAwait(false)
                    || !await TerminalActivationAndFloorMatchAsync(connection, transaction, request.Definition.Id, binding, key, retired, token).ConfigureAwait(false))
                    throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.CompactionBlocked,
                        "Semantic activation compaction is not currently permitted.");
                var absent = new BaseSemanticActivationAbsenceAuthority
                {
                    Key = BaseSemanticActivationKeyDigest.Create(key), Definition = DefinitionIdentity(request.Definition),
                    ScopeBindingId = binding.ToImmutableArray(), SubjectLifetime = lifetime,
                    FinalSlotGeneration = retired.SlotGeneration, AbsenceFloorGeneration = retired.SlotGeneration,
                    RetirementPosition = retired.RetirementPosition, StoreAuthority = retired.StoreAuthority, Checksum = [],
                };
                absent = absent with { Checksum = BaseSemanticActivationEvidenceContract.AbsenceChecksum(absent) };
                rows.Add(new(binding, key, 3, generation, null, source,
                    JsonSerializer.SerializeToUtf8Bytes(absent, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)));
            }
            else
            {
                BaseSemanticActivationMigrateRequest migrate = (BaseSemanticActivationMigrateRequest)request;
                BaseSemanticActivationLiveAuthority live = JsonSerializer.Deserialize(source,
                    HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)
                    ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                BaseSemanticActivationKeyDefinition to = _options.SemanticActivations.Single(value =>
                    value.Id == migrate.Migration.To.Id && value.Version == migrate.Migration.To.Version
                    && value.Checksum.AsSpan().SequenceEqual(migrate.Migration.To.Checksum.AsSpan()));
                BaseSemanticActivationLiveAuthority replacement = live with
                {
                    Definition = live.Definition with { Version = to.Version, Checksum = to.Checksum.ToArray().ToImmutableArray(), OwnerGeneration = _options.SemanticActivationOwnerGeneration },
                    Checksum = [],
                };
                replacement = replacement with { Checksum = BaseSemanticActivationEvidenceContract.LiveChecksum(replacement) };
                rows.Add(new(binding, key, 1, generation, activationId, source,
                    JsonSerializer.SerializeToUtf8Bytes(replacement, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)));
            }
        }
        return rows;
    }

    private async ValueTask InsertSemanticStageRowAsync(SqliteConnection connection, SqliteTransaction transaction,
        string maintenanceId, string definitionId, SemanticStageRow row, CancellationToken token)
    {
        byte[] sourceChecksum = SemanticAdminHash("base.semanticActivation.stageSource.v1\0", row.Binding, row.Key, row.SourceAuthority).ToArray();
        await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {_names.SemanticActivationRewriteStage}(maintenance_id,definition_id,binding_id,key_digest,state,slot_generation,activation_id,authority_json,source_authority_json,source_checksum) VALUES($maintenance,$definition,$binding,$key,$state,$generation,$activation,$authority,$source,$checksum);";
        insert.Parameters.AddWithValue("$maintenance", maintenanceId); insert.Parameters.AddWithValue("$definition", definitionId);
        insert.Parameters.Add("$binding", SqliteType.Blob).Value = row.Binding; insert.Parameters.Add("$key", SqliteType.Blob).Value = row.Key;
        insert.Parameters.AddWithValue("$state", row.State); insert.Parameters.AddWithValue("$generation", row.Generation);
        insert.Parameters.AddWithValue("$activation", row.ActivationId is null ? DBNull.Value : row.ActivationId);
        insert.Parameters.Add("$authority", SqliteType.Blob).Value = row.ReplacementAuthority;
        insert.Parameters.Add("$source", SqliteType.Blob).Value = row.SourceAuthority;
        insert.Parameters.Add("$checksum", SqliteType.Blob).Value = sourceChecksum;
        if (await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private async ValueTask<(long Rows, long Bytes, byte[] Rolling, BaseSemanticActivationRecoveryBoundary? After)> RecomputeStageAsync(
        SqliteConnection connection, SqliteTransaction transaction, string maintenanceId, string definitionId, CancellationToken token)
    {
        long rows = 0, bytes = 0; var authorities = new List<byte[]>(); byte[]? lastBinding = null, lastKey = null;
        await using SqliteCommand read = connection.CreateCommand(); read.Transaction = transaction;
        read.CommandText = $"SELECT binding_id,key_digest,source_authority_json,authority_json,source_checksum FROM {_names.SemanticActivationRewriteStage} WHERE maintenance_id=$maintenance AND definition_id=$definition ORDER BY binding_id,key_digest;";
        read.Parameters.AddWithValue("$maintenance", maintenanceId); read.Parameters.AddWithValue("$definition", definitionId);
        await using SqliteDataReader reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            byte[] binding = (byte[])reader[0], key = (byte[])reader[1], source = (byte[])reader[2], replacement = (byte[])reader[3];
            byte[] checksum = (byte[])reader[4]; byte[] expected = SemanticAdminHash("base.semanticActivation.stageSource.v1\0", binding, key, source).ToArray();
            if (!CryptographicOperations.FixedTimeEquals(checksum, expected)) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            rows = checked(rows + 1); bytes = checked(bytes + binding.Length + key.Length + source.Length + replacement.Length);
            authorities.Add(source); lastBinding = binding; lastKey = key;
        }
        byte[] rolling = OrderedRowsChecksum(authorities);
        BaseSemanticActivationRecoveryBoundary? after = lastBinding is null ? null : new()
        { DefinitionId = definitionId, ScopeBindingId = lastBinding.ToImmutableArray(), Key = BaseSemanticActivationKeyDigest.Create(lastKey!) };
        return (rows, bytes, rolling, after);
    }

    private async ValueTask<BaseSemanticActivationMaintenanceResult> PublishStagedCompactionAsync(SqliteConnection connection,
        SqliteTransaction transaction, BaseSemanticActivationCompactRequest request, string maintenanceId,
        long rows, long bytes, byte[] rolling, CancellationToken token)
    {
        if (rows != request.ExpectedRetiredCount || !CryptographicOperations.FixedTimeEquals(rolling, request.ExpectedRetiredChecksum.AsSpan()))
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.CompactionBlocked, "Semantic activation compaction is not currently permitted.");
        long[] counts = await StateCountsAsync(connection, transaction, request.Definition.Id, token).ConfigureAwait(false);
        BaseSemanticActivationKeyDefinition definition = _options.SemanticActivations.Single(value => value.Id == request.Definition.Id && value.Version == request.Definition.Version);
        if (checked(counts[2] + rows) > definition.Limits.MaximumAbsenceMarkers)
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.CapacityUnavailable, "Semantic activation capacity is unavailable.");
        await ValidateStagedSourcesAsync(connection, transaction, maintenanceId, request.Definition.Id, 2, token).ConfigureAwait(false);
        await using (SqliteCommand floors = connection.CreateCommand())
        {
            floors.Transaction = transaction;
            floors.CommandText = $"UPDATE {_names.SemanticActivationRecoveryFloors} AS f SET state=3,authority_json=(SELECT s.authority_json FROM {_names.SemanticActivationRewriteStage} s WHERE s.maintenance_id=$maintenance AND s.definition_id=f.definition_id AND s.binding_id=f.binding_id AND s.key_digest=f.key_digest) WHERE f.definition_id=$definition AND EXISTS(SELECT 1 FROM {_names.SemanticActivationRewriteStage} s WHERE s.maintenance_id=$maintenance AND s.definition_id=f.definition_id AND s.binding_id=f.binding_id AND s.key_digest=f.key_digest);";
            floors.Parameters.AddWithValue("$maintenance", maintenanceId); floors.Parameters.AddWithValue("$definition", request.Definition.Id);
            if (await floors.ExecuteNonQueryAsync(token).ConfigureAwait(false) != rows) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        await ApplyStagedSlotsAsync(connection, transaction, maintenanceId, request.Definition.Id, rows, token).ConfigureAwait(false);
        await EnableCurrentDefinitionAsync(connection, transaction, request.Definition, token).ConfigureAwait(false);
        return MaintenanceResult(request.ExpectedSemanticAuthorityGeneration, checked(request.ExpectedSemanticAuthorityGeneration + 1), rows, rows, bytes, rolling);
    }

    private async ValueTask<BaseSemanticActivationMaintenanceResult> PublishStagedMigrationAsync(SqliteConnection connection,
        SqliteTransaction transaction, BaseSemanticActivationMigrateRequest request, string maintenanceId,
        long rows, long bytes, byte[] rolling, byte[] fingerprint, CancellationToken token)
    {
        BaseSemanticActivationMigrationDefinition migration = BaseSemanticActivationMigrationContract.Seal(request.Migration);
        BaseSemanticActivationMigrationDefinition installed = _options.SemanticActivationMigrations.SingleOrDefault(value => value.Id == migration.Id && value.Version == migration.Version)
            ?? throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.MigrationBlocked, "Semantic activation migration requirements are not satisfied.");
        BaseSemanticActivationKeyDefinition from = await ReadDefinitionAsync(connection, transaction, migration.From, token).ConfigureAwait(false);
        BaseSemanticActivationKeyDefinition to = _options.SemanticActivations.Single(value => value.Id == migration.To.Id && value.Version == migration.To.Version);
        if (!DefinitionEqual(installed.From, migration.From) || !DefinitionEqual(installed.To, migration.To) || !Migratable(from, to))
            throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.MigrationBlocked, "Semantic activation migration requirements are not satisfied.");
        await ValidateStagedSourcesAsync(connection, transaction, maintenanceId, request.Definition.Id, 1, token).ConfigureAwait(false);
        long[] counts = await StateCountsAsync(connection, transaction, request.Definition.Id, token).ConfigureAwait(false);
        if (counts[0] != rows) throw new SemanticMaintenanceBlockedException(BaseSemanticActivationErrorCodes.MigrationBlocked, "Semantic activation migration requirements are not satisfied.");
        byte[] negative = await NegativeAuthorityChecksumAsync(connection, transaction, request.Definition.Id, token).ConfigureAwait(false);
        BaseSemanticActivationMaintenanceResult result = MaintenanceResult(request.ExpectedSemanticAuthorityGeneration,
            checked(request.ExpectedSemanticAuthorityGeneration + 1), rows, rows, bytes, rolling);
        var authority = new BaseSemanticActivationDefinitionMigrationAuthority
        {
            MigrationId = migration.Id, MigrationVersion = migration.Version, From = migration.From, To = migration.To,
            ExpectedLiveCount = counts[0], ExpectedRetiredCount = counts[1], ExpectedAbsenceCount = counts[2],
            OrderedNegativeAuthorityChecksum = negative.ToImmutableArray(), PublicationGeneration = result.ResultingAuthorityGeneration,
            ReceiptChecksum = MaintenanceReceiptChecksum(request.Identity, fingerprint, result), Checksum = [],
        };
        authority = authority with { Checksum = BaseSemanticActivationMigrationAuthorityContract.Checksum(authority) };
        await ApplyStagedSlotsAsync(connection, transaction, maintenanceId, request.Definition.Id, rows, token).ConfigureAwait(false);
        await PublishMigrationAuthorityAsync(connection, transaction, migration, authority, token).ConfigureAwait(false);
        await using SqliteCommand definitions = connection.CreateCommand(); definitions.Transaction = transaction;
        definitions.CommandText = $"UPDATE {_names.SemanticActivationDefinitions} SET execution_enabled=CASE WHEN definition_version=$version AND definition_checksum=$checksum THEN 1 ELSE 0 END WHERE definition_id=$id;";
        definitions.Parameters.AddWithValue("$id", migration.To.Id); definitions.Parameters.AddWithValue("$version", migration.To.Version);
        definitions.Parameters.Add("$checksum", SqliteType.Blob).Value = migration.To.Checksum.ToArray();
        if (await definitions.ExecuteNonQueryAsync(token).ConfigureAwait(false) < 2) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        await using SqliteCommand set = connection.CreateCommand(); set.Transaction = transaction;
        set.CommandText = $"UPDATE {_names.ProviderState} SET value=$checksum WHERE key='semantic_activation_definition_set_checksum';";
        set.Parameters.AddWithValue("$checksum", Convert.ToHexStringLower(_options.SemanticActivationDefinitionSetChecksum));
        if (await set.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        return result;
    }

    private async ValueTask ValidateStagedSourcesAsync(SqliteConnection connection, SqliteTransaction transaction,
        string maintenanceId, string definitionId, int state, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT s.binding_id,s.key_digest,s.source_authority_json,s.source_checksum,x.authority_json FROM {_names.SemanticActivationRewriteStage} s LEFT JOIN {_names.SemanticActivationSlots} x ON x.definition_id=s.definition_id AND x.binding_id=s.binding_id AND x.key_digest=s.key_digest AND x.state=$state WHERE s.maintenance_id=$maintenance AND s.definition_id=$definition ORDER BY s.binding_id,s.key_digest;";
        command.Parameters.AddWithValue("$state", state); command.Parameters.AddWithValue("$maintenance", maintenanceId); command.Parameters.AddWithValue("$definition", definitionId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (reader.IsDBNull(4)) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            byte[] binding = (byte[])reader[0], key = (byte[])reader[1], source = (byte[])reader[2], checksum = (byte[])reader[3], current = (byte[])reader[4];
            if (!CryptographicOperations.FixedTimeEquals(source, current)
                || !CryptographicOperations.FixedTimeEquals(checksum, SemanticAdminHash("base.semanticActivation.stageSource.v1\0", binding, key, source).AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
    }

    private async ValueTask ApplyStagedSlotsAsync(SqliteConnection connection, SqliteTransaction transaction,
        string maintenanceId, string definitionId, long expected, CancellationToken token)
    {
        await using SqliteCommand apply = connection.CreateCommand(); apply.Transaction = transaction;
        apply.CommandText = $"UPDATE {_names.SemanticActivationSlots} AS x SET state=(SELECT s.state FROM {_names.SemanticActivationRewriteStage} s WHERE s.maintenance_id=$maintenance AND s.definition_id=x.definition_id AND s.binding_id=x.binding_id AND s.key_digest=x.key_digest),activation_id=(SELECT s.activation_id FROM {_names.SemanticActivationRewriteStage} s WHERE s.maintenance_id=$maintenance AND s.definition_id=x.definition_id AND s.binding_id=x.binding_id AND s.key_digest=x.key_digest),authority_json=(SELECT s.authority_json FROM {_names.SemanticActivationRewriteStage} s WHERE s.maintenance_id=$maintenance AND s.definition_id=x.definition_id AND s.binding_id=x.binding_id AND s.key_digest=x.key_digest) WHERE x.definition_id=$definition AND EXISTS(SELECT 1 FROM {_names.SemanticActivationRewriteStage} s WHERE s.maintenance_id=$maintenance AND s.definition_id=x.definition_id AND s.binding_id=x.binding_id AND s.key_digest=x.key_digest);";
        apply.Parameters.AddWithValue("$maintenance", maintenanceId); apply.Parameters.AddWithValue("$definition", definitionId);
        if (await apply.ExecuteNonQueryAsync(token).ConfigureAwait(false) != expected) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private async ValueTask EnableCurrentDefinitionAsync(SqliteConnection connection, SqliteTransaction transaction,
        BaseSemanticActivationDefinitionKey definition, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.SemanticActivationDefinitions} SET execution_enabled=1 WHERE definition_id=$id AND definition_version=$version AND definition_checksum=$checksum;";
        command.Parameters.AddWithValue("$id", definition.Id); command.Parameters.AddWithValue("$version", definition.Version);
        command.Parameters.Add("$checksum", SqliteType.Blob).Value = definition.Checksum.ToArray();
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private async ValueTask PublishMigrationAuthorityAsync(SqliteConnection connection, SqliteTransaction transaction,
        BaseSemanticActivationMigrationDefinition migration, BaseSemanticActivationDefinitionMigrationAuthority authority, CancellationToken token)
    {
        await using SqliteCommand publish = connection.CreateCommand(); publish.Transaction = transaction;
        publish.CommandText = $"INSERT INTO {_names.SemanticActivationMigrations}(migration_id,migration_version,from_definition_id,from_version,from_checksum,to_definition_id,to_version,to_checksum,live_count,retired_count,absence_count,negative_checksum,publication_generation,receipt_checksum,authority_checksum) VALUES($id,$version,$fromId,$fromVersion,$fromChecksum,$toId,$toVersion,$toChecksum,$live,$retired,$absence,$negative,$generation,$receipt,$checksum);";
        publish.Parameters.AddWithValue("$id", migration.Id); publish.Parameters.AddWithValue("$version", migration.Version);
        publish.Parameters.AddWithValue("$fromId", migration.From.Id); publish.Parameters.AddWithValue("$fromVersion", migration.From.Version); publish.Parameters.Add("$fromChecksum", SqliteType.Blob).Value = migration.From.Checksum.ToArray();
        publish.Parameters.AddWithValue("$toId", migration.To.Id); publish.Parameters.AddWithValue("$toVersion", migration.To.Version); publish.Parameters.Add("$toChecksum", SqliteType.Blob).Value = migration.To.Checksum.ToArray();
        publish.Parameters.AddWithValue("$live", authority.ExpectedLiveCount); publish.Parameters.AddWithValue("$retired", authority.ExpectedRetiredCount); publish.Parameters.AddWithValue("$absence", authority.ExpectedAbsenceCount);
        publish.Parameters.Add("$negative", SqliteType.Blob).Value = authority.OrderedNegativeAuthorityChecksum.ToArray(); publish.Parameters.AddWithValue("$generation", authority.PublicationGeneration);
        publish.Parameters.Add("$receipt", SqliteType.Blob).Value = authority.ReceiptChecksum.ToArray(); publish.Parameters.Add("$checksum", SqliteType.Blob).Value = authority.Checksum.ToArray();
        if (await publish.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private static BaseSemanticActivationMaintenanceCheckpoint NewCheckpoint(BaseSemanticActivationMaintenanceRequest request,
        byte[] fingerprint, string maintenanceId, int kind) => CreateCheckpoint(request, fingerprint, maintenanceId, kind,
            null, 0, 0, 0, OrderedRowsChecksum([]));

    private static BaseSemanticActivationMaintenanceCheckpoint CreateCheckpoint(BaseSemanticActivationMaintenanceRequest request,
        byte[] fingerprint, string maintenanceId, int kind, BaseSemanticActivationRecoveryBoundary? after,
        int pages, long rows, long bytes, byte[] rolling)
    {
        var value = new BaseSemanticActivationMaintenanceCheckpoint
        {
            MaintenanceId = maintenanceId, OperationKind = kind == 1 ? "compact" : "migrate", Definition = request.Definition,
            ExpectedAuthorityGeneration = request.ExpectedSemanticAuthorityGeneration, After = after, CompletedPages = pages,
            CompletedRows = rows, CompletedBytes = bytes, RollingChecksum = rolling.ToImmutableArray(),
            RequestFingerprint = fingerprint.ToImmutableArray(), Checksum = [],
        };
        return value with { Checksum = CheckpointChecksum(value) };
    }

    private static ImmutableArray<byte> CheckpointChecksum(BaseSemanticActivationMaintenanceCheckpoint value) => SemanticAdminHash(
        "base.semanticActivation.maintenanceCheckpoint.v1\0", Encoding.UTF8.GetBytes(value.MaintenanceId),
        Encoding.UTF8.GetBytes(value.OperationKind), Encoding.UTF8.GetBytes(value.Definition.Id), ToInt64(value.Definition.Version),
        value.Definition.Checksum.ToArray(), ToInt64(value.ExpectedAuthorityGeneration), value.After?.ScopeBindingId.ToArray() ?? [],
        value.After is null ? [] : KeyBytes(value.After.Key), ToInt64(value.CompletedPages), ToInt64(value.CompletedRows),
        ToInt64(value.CompletedBytes), value.RollingChecksum.ToArray(), value.RequestFingerprint.ToArray());

    private static bool CheckpointMatches(BaseSemanticActivationMaintenanceCheckpoint checkpoint,
        BaseSemanticActivationMaintenanceRequest request, byte[] fingerprint, string maintenanceId, int kind) =>
        checkpoint.MaintenanceId == maintenanceId && checkpoint.OperationKind == (kind == 1 ? "compact" : "migrate")
        && DefinitionEqual(checkpoint.Definition, request.Definition) && checkpoint.ExpectedAuthorityGeneration == request.ExpectedSemanticAuthorityGeneration
        && CryptographicOperations.FixedTimeEquals(checkpoint.RequestFingerprint.AsSpan(), fingerprint)
        && CryptographicOperations.FixedTimeEquals(checkpoint.Checksum.AsSpan(), CheckpointChecksum(checkpoint).AsSpan());

    private static bool BoundaryEqual(BaseSemanticActivationRecoveryBoundary? left, BaseSemanticActivationRecoveryBoundary? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.DefinitionId == right.DefinitionId && left.ScopeBindingId.AsSpan().SequenceEqual(right.ScopeBindingId.AsSpan())
            && KeyBytes(left.Key).AsSpan().SequenceEqual(KeyBytes(right.Key));
    }

    private static BaseSemanticActivationMaintenanceResult InProgressMaintenance(long generation,
        BaseSemanticActivationMaintenanceCheckpoint checkpoint) => new()
    {
        Disposition = BaseSemanticActivationMaintenanceDisposition.InProgress, PreviousAuthorityGeneration = generation,
        ResultingAuthorityGeneration = generation, ExaminedRows = checkpoint.CompletedRows, ChangedRows = 0,
        CanonicalBytes = checkpoint.CompletedBytes, ResultChecksum = [], Checkpoint = checkpoint,
        ReceiptDisposition = BaseMutationRequestDisposition.Committed, CommitObservationChecksum = [],
    };

    private sealed record SemanticStageRow(byte[] Binding, byte[] Key, int State, long Generation,
        string? ActivationId, byte[] SourceAuthority, byte[] ReplacementAuthority);
}
