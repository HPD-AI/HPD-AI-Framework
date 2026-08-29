using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore
{
    private async ValueTask<bool> ActivationRowCapacityAllowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
              COALESCE(SUM(CASE WHEN state IN ($pending,$retry,$yield) THEN 1 ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN state IN ($claimed,$effect) THEN 1 ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN state NOT IN ($pending,$retry,$yield,$claimed,$effect) THEN 1 ELSE 0 END),0)
            FROM {_names.Activations};
            """;
        command.Parameters.AddWithValue("$pending", (int)BaseActivationState.Pending);
        command.Parameters.AddWithValue("$retry", (int)BaseActivationState.RetryPending);
        command.Parameters.AddWithValue("$yield", (int)BaseActivationState.YieldPending);
        command.Parameters.AddWithValue("$claimed", (int)BaseActivationState.Claimed);
        command.Parameters.AddWithValue("$effect", (int)BaseActivationState.EffectStarted);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return false;
        BaseActivationProviderCapability capability = ((IBaseActivationProvider)this).Descriptor.Capability;
        return reader.GetInt64(0) <= capability.MaximumPendingRows
            && reader.GetInt64(1) <= capability.MaximumClaimedRows
            && reader.GetInt64(2) <= capability.MaximumTerminalRows;
    }

    private async ValueTask TransformRestoredActivationAuthoritiesAsync(
        SqliteConnection connection, long sourceRestoreEpoch, long restoreEpoch, long artifactSchemaGeneration,
        long preRestoreActivationGeneration,
        ImmutableArray<BaseScheduleRecoveryFloor> recoveryFloors,
        SemanticRecoverySnapshot? semanticRecovery,
        BaseSemanticRecoveryRestoreAuthority? externalSemanticRecovery,
        string recoveryDatabasePath,
        ImmutableArray<string> consumedRecoveryNonces,
        string? consumedManifestNonce,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (long artifactActivationGeneration, _) = await ReadActivationAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        long resultingActivationGeneration = checked(Math.Max(artifactActivationGeneration, preRestoreActivationGeneration) + 1);
        long acceptedNow;
        await using (SqliteCommand time = connection.CreateCommand())
        {
            time.Transaction = transaction;
            time.CommandText = $"SELECT COALESCE(CAST(value AS INTEGER),0) FROM {_names.ProviderState} WHERE key='activation_accepted_utc';";
            acceptedNow = Convert.ToInt64(await time.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        var claimed = new List<string>();
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"SELECT activation_id,generation FROM {_names.Activations} WHERE state=$claimed ORDER BY activation_id;";
            read.Parameters.AddWithValue("$claimed", (int)BaseActivationState.Claimed);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) claimed.Add(reader.GetString(0));
        }
        foreach (string id in claimed)
        {
            SqliteActivationRow row = await ReadActivationAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("base.activation.restoreConflict");
            long prior = row.Generation;
            long generation = checked(prior + 1);
            await using SqliteCommand recover = connection.CreateCommand(); recover.Transaction = transaction;
            recover.CommandText = $"UPDATE {_names.Activations} SET state=$retry,generation=$generation,claim_fence=NULL,claim_worker=NULL,lease_revision=NULL,lease_expires_at=NULL,effective_due_at=$now,eligible=1,control_checksum=$checksum WHERE activation_id=$id AND generation=$prior AND state=$claimed;";
            recover.Parameters.AddWithValue("$retry", (int)BaseActivationState.RetryPending); recover.Parameters.AddWithValue("$generation", generation);
            recover.Parameters.AddWithValue("$now", acceptedNow); recover.Parameters.Add("$checksum", SqliteType.Blob).Value = ActivationControlChecksum(
                id, generation, BaseActivationState.RetryPending, acceptedNow, row.YieldCount,
                row.MaximumYields, row.ExecutionSliceOrdinal, row.AttemptStartedAt,
                row.SliceStartedAt, null, null);
            recover.Parameters.AddWithValue("$id", id); recover.Parameters.AddWithValue("$prior", prior); recover.Parameters.AddWithValue("$claimed", (int)BaseActivationState.Claimed);
            if (await recover.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException("base.activation.restoreConflict");
        }
        await using (SqliteCommand executors = connection.CreateCommand())
        {
            executors.Transaction = transaction;
            executors.CommandText = $"UPDATE {_names.Executors} SET retired=1,heartbeat_expires_at=MIN(heartbeat_expires_at,$now) WHERE retired=0;";
            executors.Parameters.AddWithValue("$now", acceptedNow);
            await executors.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (SqliteCommand effects = connection.CreateCommand())
        {
            effects.Transaction = transaction;
            effects.CommandText = $"UPDATE {_names.ActivationEffects} SET heartbeat_expires_at=MIN(heartbeat_expires_at,$now);";
            effects.Parameters.AddWithValue("$now", acceptedNow);
            await effects.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        foreach (BaseScheduleRecoveryFloor floor in recoveryFloors)
        {
            await using SqliteCommand schedules = connection.CreateCommand(); schedules.Transaction = transaction;
            schedules.CommandText = $"SELECT definition_json,definition_generation,enabled,schedule_epoch,last_nominal,next_nominal FROM {_names.ActivationSchedules} ORDER BY schedule_id,schedule_version;";
            await using SqliteDataReader reader = await schedules.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var matched = new List<BaseScheduleAuthority>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                BaseScheduleDefinition definition = JsonSerializer.Deserialize((byte[])reader[0], HPDBaseJsonSerializerContext.Default.BaseScheduleDefinition)
                    ?? throw new InvalidDataException("base.activation.scheduleInvalid");
                if (!CryptographicOperations.FixedTimeEquals(ScheduleRecoveryKeyDigest(definition.Id, definition.Version), floor.ProtectedScheduleKeyDigest.AsSpan())) continue;
                long epoch = Math.Max(reader.GetInt64(3), floor.ScheduleEpoch);
                long? restoredLast = reader.IsDBNull(4) ? null : reader.GetInt64(4);
                long? last = restoredLast is null ? floor.LastConsideredNominal
                    : floor.LastConsideredNominal is null ? restoredLast : Math.Max(restoredLast.Value, floor.LastConsideredNominal.Value);
                long? next = reader.IsDBNull(5) ? null : reader.GetInt64(5);
                if (last is not null && next is not null && next <= last) next = null;
                matched.Add(SqliteScheduleAuthority(definition, reader.GetInt64(1), reader.GetInt64(2) != 0, epoch, last, next));
            }
            await reader.DisposeAsync().ConfigureAwait(false);
            if (matched.Count != 1) throw new InvalidDataException("base.activation.recoveryManifestInvalid");
            await WriteScheduleAsync(connection, transaction, matched[0], cancellationToken).ConfigureAwait(false);
        }
        foreach (string nonce in consumedRecoveryNonces.Append(consumedManifestNonce).OfType<string>())
        {
            await using SqliteCommand write = connection.CreateCommand(); write.Transaction = transaction;
            write.CommandText = $"INSERT OR IGNORE INTO {_names.ProviderState}(key,value) VALUES($key,'1');";
            write.Parameters.AddWithValue("$key", $"activation_recovery_nonce_{nonce}");
            await write.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await RebindActivationPruneFloorsAsync(connection, transaction, sourceRestoreEpoch, restoreEpoch, cancellationToken).ConfigureAwait(false);
        await RebindActivationBackupCoverageCheckpointsAsync(
            connection, transaction, sourceRestoreEpoch, restoreEpoch, cancellationToken).ConfigureAwait(false);
        await RestoreSemanticRecoverySnapshotAsync(connection, transaction, sourceRestoreEpoch, restoreEpoch, artifactSchemaGeneration,
            externalSemanticRecovery is null ? semanticRecovery : null, externalSemanticRecovery,
            recoveryDatabasePath, preRestoreActivationGeneration, resultingActivationGeneration, cancellationToken).ConfigureAwait(false);
        await RebindAllActivationPruneFloorsGenerationAsync(connection, transaction, restoreEpoch, resultingActivationGeneration, cancellationToken).ConfigureAwait(false);
        await using (SqliteCommand publish = connection.CreateCommand())
        {
            publish.Transaction = transaction;
            publish.CommandText = $"UPDATE {_names.ProviderState} SET value=$resulting WHERE key='activation_generation' AND CAST(value AS INTEGER)=$artifact;";
            publish.Parameters.AddWithValue("$resulting", resultingActivationGeneration); publish.Parameters.AddWithValue("$artifact", artifactActivationGeneration);
            if (await publish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException("base.activation.restoreConflict");
        }
        if (!await ActivationRowCapacityAllowsAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("base.activation.capacityUnavailable");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RebindActivationBackupCoverageCheckpointsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long sourceRestoreEpoch,
        long restoreEpoch,
        CancellationToken cancellationToken)
    {
        var checkpoints = new List<BaseActivationBackupCoverageCheckpoint>();
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"SELECT artifact_id,artifact_sha256,application_id,logical_store_id,store_instance_id,restore_epoch,receipt_sequence,receipt_ordered_checksum,checkpoint_generation,committed_at,checkpoint_checksum FROM {_names.ActivationBackupCoverageCheckpoints} ORDER BY checkpoint_generation;";
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                checkpoints.Add(new BaseActivationBackupCoverageCheckpoint
                {
                    FormatVersion = 1, ArtifactId = reader.GetString(0),
                    ArtifactSha256 = ((byte[])reader[1]).ToImmutableArray(), ApplicationId = reader.GetString(2),
                    LogicalStoreId = reader.GetString(3), StoreInstanceId = reader.GetString(4),
                    RestoreEpoch = reader.GetInt64(5), ReceiptSequence = reader.GetInt64(6),
                    ReceiptOrderedChecksum = ((byte[])reader[7]).ToImmutableArray(), Generation = reader.GetInt64(8),
                    CommittedAt = reader.GetInt64(9), Checksum = ((byte[])reader[10]).ToImmutableArray(),
                });
        }
        foreach (BaseActivationBackupCoverageCheckpoint prior in checkpoints)
        {
            if (!BaseActivationBackupCoverageCheckpointContract.IsValid(prior)
                || prior.ApplicationId != _options.SemanticActivationApplicationId
                || prior.LogicalStoreId != _options.StoreId
                || prior.RestoreEpoch != sourceRestoreEpoch)
                throw new InvalidDataException("base.activation.restoreConflict");
            BaseActivationBackupCoverageCheckpoint rebound = BaseActivationBackupCoverageCheckpointContract.Create(
                prior.ArtifactId, prior.ArtifactSha256.AsSpan(), prior.ApplicationId, prior.LogicalStoreId,
                prior.StoreInstanceId, restoreEpoch, prior.ReceiptSequence,
                prior.ReceiptOrderedChecksum.AsSpan(), prior.Generation, prior.CommittedAt);
            await using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {_names.ActivationBackupCoverageCheckpoints} SET restore_epoch=$restore,checkpoint_checksum=$checksum WHERE artifact_id=$artifact AND checkpoint_generation=$generation;";
            update.Parameters.AddWithValue("$restore", restoreEpoch);
            update.Parameters.Add("$checksum", SqliteType.Blob).Value = rebound.Checksum.ToArray();
            update.Parameters.AddWithValue("$artifact", prior.ArtifactId);
            update.Parameters.AddWithValue("$generation", prior.Generation);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException("base.activation.restoreConflict");
        }
    }

    private async ValueTask RebindAllActivationPruneFloorsGenerationAsync(SqliteConnection connection, SqliteTransaction transaction,
        long restoreEpoch, long resultingGeneration, CancellationToken cancellationToken)
    {
        var rows = new List<BaseActivationPruneEvidence>();
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"SELECT activation_id,definition_id,definition_version,definition_checksum,terminal_generation,terminal_control_checksum,terminal_receipt_checksum,occurrence_checksum,result_checksum,prune_authority_generation,application_id,logical_store_id,store_instance_id,restore_epoch,publication_authority_checksum,authority_checksum FROM {_names.ActivationPruneFloors} ORDER BY activation_id;";
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add(new BaseActivationPruneEvidence
            {
                ActivationId=reader.GetString(0),Definition=new(){Id=reader.GetString(1),Version=reader.GetInt32(2),Checksum=((byte[])reader[3]).ToImmutableArray()},TerminalGeneration=reader.GetInt64(4),TerminalControlChecksum=((byte[])reader[5]).ToImmutableArray(),TerminalReceiptChecksum=((byte[])reader[6]).ToImmutableArray(),OccurrenceChecksum=reader.IsDBNull(7)?null:((byte[])reader[7]).ToImmutableArray(),ResultChecksum=reader.IsDBNull(8)?null:((byte[])reader[8]).ToImmutableArray(),PruneAuthorityGeneration=reader.GetInt64(9),ApplicationId=reader.GetString(10),LogicalStoreId=reader.GetString(11),StoreInstanceId=reader.GetString(12),RestoreEpoch=reader.GetInt64(13),PublicationAuthorityChecksum=((byte[])reader[14]).ToImmutableArray(),Checksum=((byte[])reader[15]).ToImmutableArray()
            });
        }
        foreach (BaseActivationPruneEvidence prior in rows)
        {
            if (!BaseActivationPruneEvidenceContract.IsValid(prior) || prior.RestoreEpoch != restoreEpoch) throw new InvalidDataException("base.activation.restoreConflict");
            byte[] publication=SHA256.HashData(Encoding.UTF8.GetBytes($"base.activation.publicationAuthority.v1\0{prior.ApplicationId}\n{prior.LogicalStoreId}\n{prior.StoreInstanceId}\n{restoreEpoch}\n{resultingGeneration}"));
            BaseActivationPruneEvidence replacement=prior with{PruneAuthorityGeneration=resultingGeneration,PublicationAuthorityChecksum=publication.ToImmutableArray(),Checksum=[]}; replacement=replacement with{Checksum=BaseActivationPruneEvidenceContract.Checksum(replacement)};
            await using SqliteCommand update=connection.CreateCommand();update.Transaction=transaction;update.CommandText=$"UPDATE {_names.ActivationPruneFloors} SET prune_authority_generation=$generation,publication_authority_checksum=$publication,authority_checksum=$checksum WHERE activation_id=$id;";update.Parameters.AddWithValue("$generation",resultingGeneration);update.Parameters.Add("$publication",SqliteType.Blob).Value=publication;update.Parameters.Add("$checksum",SqliteType.Blob).Value=replacement.Checksum.ToArray();update.Parameters.AddWithValue("$id",prior.ActivationId);if(await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)!=1)throw new InvalidDataException("base.activation.restoreConflict");
        }
    }

    private async ValueTask RebindActivationPruneFloorsAsync(SqliteConnection connection, SqliteTransaction transaction,
        long sourceRestoreEpoch, long restoreEpoch, CancellationToken cancellationToken)
    {
        var rows = new List<BaseActivationPruneEvidence>();
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"SELECT activation_id,definition_id,definition_version,definition_checksum,terminal_generation,terminal_control_checksum,terminal_receipt_checksum,occurrence_checksum,result_checksum,prune_authority_generation,application_id,logical_store_id,store_instance_id,restore_epoch,publication_authority_checksum,authority_checksum FROM {_names.ActivationPruneFloors} ORDER BY activation_id;";
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                rows.Add(new BaseActivationPruneEvidence
                {
                    ActivationId = reader.GetString(0), Definition = new BaseActivationDefinitionKey { Id = reader.GetString(1), Version = reader.GetInt32(2), Checksum = ((byte[])reader[3]).ToImmutableArray() },
                    TerminalGeneration = reader.GetInt64(4), TerminalControlChecksum = ((byte[])reader[5]).ToImmutableArray(), TerminalReceiptChecksum = ((byte[])reader[6]).ToImmutableArray(),
                    OccurrenceChecksum = reader.IsDBNull(7) ? null : ((byte[])reader[7]).ToImmutableArray(), ResultChecksum = reader.IsDBNull(8) ? null : ((byte[])reader[8]).ToImmutableArray(),
                    PruneAuthorityGeneration = reader.GetInt64(9), ApplicationId = reader.GetString(10), LogicalStoreId = reader.GetString(11), StoreInstanceId = reader.GetString(12),
                    RestoreEpoch = reader.GetInt64(13), PublicationAuthorityChecksum = ((byte[])reader[14]).ToImmutableArray(), Checksum = ((byte[])reader[15]).ToImmutableArray(),
                });
        }
        foreach (BaseActivationPruneEvidence prior in rows)
        {
            byte[] priorPublication = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"base.activation.publicationAuthority.v1\0{prior.ApplicationId}\n{prior.LogicalStoreId}\n{prior.StoreInstanceId}\n{prior.RestoreEpoch}\n{prior.PruneAuthorityGeneration}"));
            if (!BaseActivationPruneEvidenceContract.IsValid(prior)
                || prior.RestoreEpoch != sourceRestoreEpoch
                || !CryptographicOperations.FixedTimeEquals(priorPublication, prior.PublicationAuthorityChecksum.AsSpan()))
                throw new InvalidDataException("base.activation.restoreConflict");
            byte[] publication = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"base.activation.publicationAuthority.v1\0{prior.ApplicationId}\n{prior.LogicalStoreId}\n{prior.StoreInstanceId}\n{restoreEpoch}\n{prior.PruneAuthorityGeneration}"));
            BaseActivationPruneEvidence replacement = prior with
            {
                RestoreEpoch = restoreEpoch,
                PublicationAuthorityChecksum = publication.ToImmutableArray(),
                Checksum = [],
            };
            replacement = replacement with { Checksum = BaseActivationPruneEvidenceContract.Checksum(replacement) };
            await using SqliteCommand update = connection.CreateCommand(); update.Transaction = transaction;
            update.CommandText = $"UPDATE {_names.ActivationPruneFloors} SET restore_epoch=$restore,publication_authority_checksum=$publication,authority_checksum=$authority WHERE activation_id=$id;";
            update.Parameters.AddWithValue("$restore", restoreEpoch); update.Parameters.Add("$publication", SqliteType.Blob).Value = publication;
            update.Parameters.Add("$authority", SqliteType.Blob).Value = replacement.Checksum.ToArray(); update.Parameters.AddWithValue("$id", prior.ActivationId);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException("base.activation.restoreConflict");
        }
    }

    private async ValueTask<ImmutableArray<BaseScheduleRecoveryFloor>> CaptureScheduleRecoveryFloorsAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        var floors = ImmutableArray.CreateBuilder<BaseScheduleRecoveryFloor>();
        var authorities = new List<(string Id, int Version, long Epoch, long? Last)>();
        await using SqliteCommand schedules = connection.CreateCommand();
        schedules.CommandText = $"SELECT schedule_id,schedule_version,schedule_epoch,last_nominal FROM {_names.ActivationSchedules} ORDER BY schedule_id,schedule_version;";
        await using SqliteDataReader reader = await schedules.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            authorities.Add((reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetInt64(3)));
        await reader.DisposeAsync().ConfigureAwait(false);
        foreach ((string id, int version, long epoch, long? last) in authorities)
        {
            var occurrenceChecksums = new List<byte[]>();
            await using SqliteCommand occurrences = connection.CreateCommand();
            occurrences.CommandText = $"SELECT fact_checksum FROM {_names.ActivationOccurrences} WHERE schedule_id=$id AND schedule_version=$version ORDER BY schedule_epoch,nominal_at,overlap_ordinal,occurrence_id;";
            occurrences.Parameters.AddWithValue("$id", id); occurrences.Parameters.AddWithValue("$version", version);
            await using SqliteDataReader occurrenceReader = await occurrences.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await occurrenceReader.ReadAsync(cancellationToken).ConfigureAwait(false)) occurrenceChecksums.Add((byte[])occurrenceReader[0]);
            using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (byte[] checksum in occurrenceChecksums) aggregate.AppendData(checksum);
            byte[] occurrenceChecksum = aggregate.GetHashAndReset();
            byte[] lineage = occurrenceChecksums.Count == 0 ? SHA256.HashData("base.activation.emptyLineage.v1"u8) : SHA256.HashData(occurrenceChecksums[^1]);
            floors.Add(new BaseScheduleRecoveryFloor
            {
                ProtectedScheduleKeyDigest = ScheduleRecoveryKeyDigest(id, version).ToImmutableArray(), ScheduleEpoch = epoch,
                LastConsideredNominal = last, OccurrenceCount = occurrenceChecksums.Count,
                OccurrenceChecksum = occurrenceChecksum.ToImmutableArray(), LatestActivationLineageChecksum = lineage.ToImmutableArray(),
            });
        }
        return floors.ToImmutable();
    }

    private async ValueTask<ImmutableArray<string>> ReadConsumedRecoveryNoncesAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        const string prefix = "activation_recovery_nonce_";
        var values = ImmutableArray.CreateBuilder<string>();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT key FROM {_names.ProviderState} WHERE key GLOB $pattern ORDER BY key;";
        command.Parameters.AddWithValue("$pattern", prefix + "*");
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) values.Add(reader.GetString(0)[prefix.Length..]);
        return values.ToImmutable();
    }

    private static byte[] ScheduleRecoveryKeyDigest(string id, int version) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"base.activation.scheduleRecoveryKey.v1\0{id}\n{version}"));

    private BaseActivationProviderDescriptor? _activationDescriptor;

    private static BaseActivationProviderDescriptor CreateActivationDescriptor(bool durableRecovery, HPDBaseSqliteOptions options) =>
        BaseActivationCertificationReceiptContract.FromSuccessfulReport(
            "hpd.base.sqlite.activations", "1", CreateActivationCapability(durableRecovery, options),
            ImmutableArray.CreateRange(Convert.FromHexString(durableRecovery
                ? "76ec60b6e206e167343e6266e73c80e9eac7300927dafa7a039c5a7993247c13"
                : "76ec60b6e206e167343e6266e73c80e9eac7300927dafa7a039c5a7993247c13")), "Microsoft.Data.Sqlite");

    internal static BaseActivationProviderCapability CreateActivationCapability(bool durableRecovery, HPDBaseSqliteOptions options) => new()
        {
            AtomicCreationSupported = true,
            SelectionTargetSupported = true,
            ModuleTargetSupported = true,
            GuardedChildrenSupported = true,
            DurableYieldSupported = true,
            RestoreFencingSupported = true,
            DueInvalidation = BaseDueInvalidationClass.BoundedPolling,
            ScheduleKinds = [BaseScheduleKind.Once, BaseScheduleKind.Interval, BaseScheduleKind.Cron, BaseScheduleKind.Calendar],
            ExecutionClasses = [BaseActivationExecutionClass.TransactionalOperation, BaseActivationExecutionClass.AtLeastOnceWorker, BaseActivationExecutionClass.AtMostOnceEffect],
            MaximumActivationsPerTransaction = 256,
            MaximumDueCandidates = 256,
            MaximumReadIntervals = 4096,
            MaximumIndexOperations = 4096,
            MaximumInputBytes = 4L * 1024 * 1024,
            MaximumResultBytes = 4L * 1024 * 1024,
            MaximumEvidenceBytes = 16L * 1024 * 1024,
            MaximumTransientBytes = 16L * 1024 * 1024,
            MaximumReceiptBytes = 16L * 1024 * 1024,
            MaximumPendingRows = options.MaxPendingActivationRows,
            MaximumClaimedRows = options.MaxClaimedActivationRows,
            MaximumTerminalRows = options.MaxTerminalActivationRows,
            MaximumAttempts = 1024,
            MaximumYieldsPerActivation = 1_000_000,
            MaximumReservedYieldReceiptSlots = 1_000_000_000_000,
            MaximumRenewalsPerSlice = 4096,
            MaximumChildrenPerSlice = 4096,
            MaximumLineageDepth = 256,
            MaximumOccurrencePage = 256,
            MaximumPriorityAgingBoost = 32,
            PriorityAgingInterval = TimeSpan.FromMinutes(1),
            ObservationTokenLifetime = TimeSpan.FromMinutes(5),
            MaximumTimeZoneBytes = 64L * 1024 * 1024,
            MaximumHandlerDependencies = 4096,
            AcquisitionDeadline = TimeSpan.FromSeconds(5),
            TransactionDeadline = TimeSpan.FromSeconds(30),
            ObservationWaitDeadline = TimeSpan.FromMinutes(5),
            RenewalDeadline = TimeSpan.FromSeconds(5),
            CommitObservationDeadline = TimeSpan.FromSeconds(30),
            ReceiptResolutionDeadline = TimeSpan.FromSeconds(30),
            MaintenanceDeadline = TimeSpan.FromMinutes(5),
            ShutdownDrainDeadline = TimeSpan.FromSeconds(60),
            ProviderQuarantineSlots = 32,
            HandlerQuarantineSlots = 32,
            BackupModes = durableRecovery ? [BaseActivationBackupMode.WholeStoreAtomic] : [],
            RestoreModes = durableRecovery
                ? [BaseActivationRestoreMode.InPlaceRecovery, BaseActivationRestoreMode.NewDisasterDomain]
                : [],
            CanonicalChecksum = ImmutableArray.CreateRange(SHA256.HashData("hpd.base.sqlite.activations.v2"u8)),
        };

    BaseActivationProviderDescriptor IBaseActivationProvider.Descriptor =>
        _activationDescriptor ?? throw new InvalidOperationException("base.activation.providerUnavailable");

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationYieldReservationState>> ReadYieldReservationStateAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        BaseActivationYieldReservationState state = await ReadYieldReservationStateAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(state);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationReceiptCompactionAuthority>> CaptureReceiptCompactionAuthorityAsync(
        BaseActivationReceiptCompactionAuthorityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ApplicationId != _options.SemanticActivationApplicationId
            || request.Definition.Version < 1 || request.Definition.Checksum.Length != 32
            || request.Scope.ProtectedIndexDigest.Length != 32
            || request.ReceiptRetention.FormatVersion != 1
            || request.ReceiptRetention.DuplicateResolutionLifetime < TimeSpan.FromHours(1)
            || request.ReceiptRetention.DuplicateResolutionLifetime > TimeSpan.FromDays(90)
            || !Enum.IsDefined(request.ReceiptRetention.ProtectedBackupCoverage)
            || !ActivationLimitsValid(request.Limits))
            return ActivationFailure<BaseActivationReceiptCompactionAuthority>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        BaseActivationYieldReservationState reservation = await ReadYieldReservationStateAsync(
            connection, transaction, cancellationToken).ConfigureAwait(false);
        BaseActivationReceiptBackupFloor backupFloor;
        if (request.ReceiptRetention.ProtectedBackupCoverage == BaseActivationProtectedBackupCoverage.NotRequired)
        {
            backupFloor = new BaseActivationReceiptBackupFloor
            {
                Kind = BaseActivationReceiptBackupFloorKind.NotApplicable,
            };
        }
        else
        {
            string? storeInstanceId = await ReadStoreInstanceIdAsync(connection, cancellationToken).ConfigureAwait(false);
            (_, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            BaseActivationBackupCoverageCheckpoint? checkpoint = null;
            await using SqliteCommand read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = $"SELECT artifact_id,artifact_sha256,application_id,logical_store_id,store_instance_id,restore_epoch,receipt_sequence,receipt_ordered_checksum,checkpoint_generation,committed_at,checkpoint_checksum FROM {_names.ActivationBackupCoverageCheckpoints} WHERE application_id=$application AND logical_store_id=$store AND store_instance_id=$instance AND restore_epoch=$restore ORDER BY checkpoint_generation DESC LIMIT 1;";
            read.Parameters.AddWithValue("$application", request.ApplicationId);
            read.Parameters.AddWithValue("$store", _options.StoreId);
            read.Parameters.AddWithValue("$instance", (object?)storeInstanceId ?? DBNull.Value);
            read.Parameters.AddWithValue("$restore", restoreEpoch);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                checkpoint = new BaseActivationBackupCoverageCheckpoint
                {
                    FormatVersion = 1, ArtifactId = reader.GetString(0),
                    ArtifactSha256 = ((byte[])reader[1]).ToImmutableArray(), ApplicationId = reader.GetString(2),
                    LogicalStoreId = reader.GetString(3), StoreInstanceId = reader.GetString(4), RestoreEpoch = reader.GetInt64(5),
                    ReceiptSequence = reader.GetInt64(6), ReceiptOrderedChecksum = ((byte[])reader[7]).ToImmutableArray(),
                    Generation = reader.GetInt64(8), CommittedAt = reader.GetInt64(9),
                    Checksum = ((byte[])reader[10]).ToImmutableArray(),
                };
            if (!BaseActivationBackupCoverageCheckpointContract.IsValid(checkpoint))
                return ActivationFailure<BaseActivationReceiptCompactionAuthority>(
                    "base.activation.removalBlocked", OperationStatus.Conflict, ErrorCategory.Conflict);
            backupFloor = new BaseActivationReceiptBackupFloor
            {
                Kind = BaseActivationReceiptBackupFloorKind.Checkpoint,
                Checkpoint = checkpoint,
            };
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(new BaseActivationReceiptCompactionAuthority
        {
            Reservation = reservation,
            BackupFloor = backupFloor,
        });
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationMaintenancePage>> AdvanceMaintenanceAsync(
        BaseActivationMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind) || request.Take is < 1 or > 256 || !ActivationLimitsValid(request.Limits)
            || !await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationMaintenancePage>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool maintenanceFound, OperationResult<BaseActivationMaintenancePage> maintenanceReplay) = await ReadControlReceiptAsync(
            connection, transaction, request.Identity, "activation-maintenance",
            HPDBaseJsonSerializerContext.Default.BaseActivationMaintenancePage,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, cancellationToken).ConfigureAwait(false);
        if (maintenanceFound) return maintenanceReplay;
        var candidates = new List<(string Id, long Generation, BaseActivationState State)>();
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            string effectJoin = request.Kind == BaseActivationMaintenanceKind.RecoverExpiredEffects
                ? $" JOIN {_names.ActivationEffects} f ON f.activation_id=a.activation_id LEFT JOIN {_names.Executors} e ON e.application_id=f.executor_application AND e.host_id=f.executor_host AND e.process_incarnation_id=f.executor_process " : string.Empty;
            string eligibility = request.Kind == BaseActivationMaintenanceKind.RecoverExpiredClaims
                ? "a.state=$state AND a.lease_expires_at<=$now"
                : "a.state=$state AND f.heartbeat_expires_at<=$now AND (e.application_id IS NULL OR e.retired=1 OR e.heartbeat_expires_at<=$now OR e.executor_generation<>f.executor_generation OR e.restore_epoch<>f.executor_restore_epoch OR e.authority_checksum<>f.executor_checksum)";
            read.CommandText = $"SELECT a.activation_id,a.generation,a.state FROM {_names.Activations} a {effectJoin} WHERE a.definition_id=$definition AND a.definition_version=$version AND a.definition_checksum=$checksum AND a.scope_kind=$scope AND a.scope_digest=$scopeDigest AND a.activation_id>$after AND {eligibility} ORDER BY a.activation_id LIMIT $take;";
            read.Parameters.AddWithValue("$definition", request.Definition.Id); read.Parameters.AddWithValue("$version", request.Definition.Version);
            read.Parameters.Add("$checksum", SqliteType.Blob).Value = request.Definition.Checksum.ToArray(); read.Parameters.AddWithValue("$scope", (int)request.Scope.Kind);
            read.Parameters.Add("$scopeDigest", SqliteType.Blob).Value = request.Scope.ProtectedIndexDigest.ToArray(); read.Parameters.AddWithValue("$after", request.AfterActivationId ?? string.Empty);
            read.Parameters.AddWithValue("$state", (int)(request.Kind == BaseActivationMaintenanceKind.RecoverExpiredClaims ? BaseActivationState.Claimed : BaseActivationState.EffectStarted));
            read.Parameters.AddWithValue("$now", request.AcceptedTime.CapturedUtc); read.Parameters.AddWithValue("$take", checked(request.Take + 1));
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) candidates.Add((reader.GetString(0), reader.GetInt64(1), (BaseActivationState)reader.GetInt32(2)));
        }
        bool completed = candidates.Count <= request.Take; (string Id, long Generation, BaseActivationState State)[] page = candidates.Take(request.Take).ToArray();
        var items = ImmutableArray.CreateBuilder<BaseActivationMaintenanceItem>(page.Length);
        foreach ((string id, long prior, BaseActivationState priorState) in page)
        {
            SqliteActivationRow row = await ReadActivationAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("base.activation.providerContractInvalid");
            BaseActivationState resulting = request.Kind == BaseActivationMaintenanceKind.RecoverExpiredClaims ? BaseActivationState.RetryPending : BaseActivationState.OutcomeUnknown;
            long generation = checked(prior + 1);
            long effectiveDueAt = request.Kind == BaseActivationMaintenanceKind.RecoverExpiredClaims
                ? request.AcceptedTime.CapturedUtc : row.EffectiveDueAt;
            byte[] checksum = ActivationControlChecksum(id, generation, resulting, effectiveDueAt,
                row.YieldCount, row.MaximumYields, row.ExecutionSliceOrdinal,
                row.AttemptStartedAt, row.SliceStartedAt, null, null);
            await using SqliteCommand update = connection.CreateCommand(); update.Transaction = transaction;
            update.CommandText = $"UPDATE {_names.Activations} SET state=$resulting,generation=$generation,claim_fence=NULL,claim_worker=NULL,lease_revision=NULL,lease_expires_at=NULL,effective_due_at=CASE WHEN $retry=1 THEN $now ELSE effective_due_at END,eligible=CASE WHEN $retry=1 THEN 1 ELSE eligible END,control_checksum=$control WHERE activation_id=$id AND generation=$prior AND state=$state;";
            update.Parameters.AddWithValue("$resulting", (int)resulting); update.Parameters.AddWithValue("$generation", generation);
            update.Parameters.AddWithValue("$retry", request.Kind == BaseActivationMaintenanceKind.RecoverExpiredClaims ? 1 : 0); update.Parameters.AddWithValue("$now", request.AcceptedTime.CapturedUtc);
            update.Parameters.Add("$control", SqliteType.Blob).Value = checksum; update.Parameters.AddWithValue("$id", id);
            update.Parameters.AddWithValue("$prior", prior); update.Parameters.AddWithValue("$state", (int)priorState);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return ActivationFailure<BaseActivationMaintenancePage>("base.activation.conflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            items.Add(new BaseActivationMaintenanceItem { ActivationId = id, PreviousGeneration = prior, ResultingGeneration = generation,
                PreviousState = priorState, ResultingState = resulting, ControlChecksum = checksum.ToImmutableArray() });
        }
        if (page.Length != 0) await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var result = new BaseActivationMaintenancePage
        {
            Items = items.MoveToImmutable(), NextActivationId = completed || page.Length == 0 ? null : page[^1].Id,
            Completed = completed, Accounting = ActivationAccounting(candidates.Count, items.Count * 32L),
            Disposition = BaseMutationRequestDisposition.Committed,
        };
        if (!await ActivationRowCapacityAllowsAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationMaintenancePage>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        await WriteControlReceiptAsync(connection, transaction, request.Identity, "activation-maintenance", result,
            HPDBaseJsonSerializerContext.Default.BaseActivationMaintenancePage, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationPrunePage>> PruneAsync(
        BaseActivationPruneRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Take is < 1 or > 256 || !ActivationLimitsValid(request.Limits) || request.Take > request.Limits.MaximumCandidates
            || !await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationPrunePage>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool pruneFound, OperationResult<BaseActivationPrunePage> pruneReplay) = await ReadControlReceiptAsync(
            connection, transaction, request.Identity, "activation-pruned", HPDBaseJsonSerializerContext.Default.BaseActivationPrunePage,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, cancellationToken).ConfigureAwait(false);
        if (pruneFound) return pruneReplay;
        var candidates = new List<string>();
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction; read.CommandText = $"SELECT activation_id FROM {_names.Activations} WHERE definition_id=$definition AND definition_version=$version AND definition_checksum=$checksum AND scope_kind=$scope AND scope_digest=$scopeDigest AND state=$disposed AND activation_id>$after ORDER BY activation_id LIMIT $take;";
            read.Parameters.AddWithValue("$definition", request.Definition.Id); read.Parameters.AddWithValue("$version", request.Definition.Version);
            read.Parameters.Add("$checksum", SqliteType.Blob).Value = request.Definition.Checksum.ToArray(); read.Parameters.AddWithValue("$scope", (int)request.Scope.Kind);
            read.Parameters.Add("$scopeDigest", SqliteType.Blob).Value = request.Scope.ProtectedIndexDigest.ToArray(); read.Parameters.AddWithValue("$disposed", (int)BaseActivationState.Disposed);
            read.Parameters.AddWithValue("$after", request.AfterActivationId ?? string.Empty); read.Parameters.AddWithValue("$take", request.Take);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) candidates.Add(reader.GetString(0));
        }
        string[] page = candidates.ToArray();
        bool hasBoundaryCandidate = false;
        if (page.Length == request.Take)
        {
            await using SqliteCommand boundary = connection.CreateCommand();
            boundary.Transaction = transaction;
            boundary.CommandText = $"SELECT EXISTS(SELECT 1 FROM {_names.Activations} WHERE definition_id=$definition AND definition_version=$version AND definition_checksum=$checksum AND scope_kind=$scope AND scope_digest=$scopeDigest AND state=$disposed AND activation_id>$after LIMIT 1);";
            boundary.Parameters.AddWithValue("$definition", request.Definition.Id);
            boundary.Parameters.AddWithValue("$version", request.Definition.Version);
            boundary.Parameters.Add("$checksum", SqliteType.Blob).Value = request.Definition.Checksum.ToArray();
            boundary.Parameters.AddWithValue("$scope", (int)request.Scope.Kind);
            boundary.Parameters.Add("$scopeDigest", SqliteType.Blob).Value = request.Scope.ProtectedIndexDigest.ToArray();
            boundary.Parameters.AddWithValue("$disposed", (int)BaseActivationState.Disposed);
            boundary.Parameters.AddWithValue("$after", page[^1]);
            hasBoundaryCandidate = Convert.ToInt64(
                await boundary.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
        bool completed = !hasBoundaryCandidate;
        (long currentAuthorityGeneration, _) = await ReadActivationAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        long pruneAuthorityGeneration = page.Length == 0 ? currentAuthorityGeneration : checked(currentAuthorityGeneration + 1);
        var evidence = ImmutableArray.CreateBuilder<BaseActivationPruneEvidence>(page.Length);
        BaseActivationYieldReservationState priorReservation = await ReadYieldReservationStateAsync(
            connection, transaction, cancellationToken).ConfigureAwait(false);
        BaseActivationInstanceReceiptChainState priorChain = await ReadInstanceReceiptChainAsync(
            connection, transaction, cancellationToken).ConfigureAwait(false);
        int deletedReceiptCount = 0;
        int deletedYieldReceiptCount = 0;
        string pruneReceiptKey = SqliteActivationReceiptKey(request.Identity);
        foreach (string id in page)
        {
            List<SqlitePruneReceipt> receipts = await ReadPruneReceiptsAsync(
                connection, transaction, id, cancellationToken).ConfigureAwait(false);
            foreach (SqlitePruneReceipt receipt in receipts)
            {
                if (receipt.DuplicateResolveUntil > request.AcceptedTime.CapturedUtc
                    || !await ReceiptBackupFloorSatisfiedForPruneAsync(
                        connection, transaction, receipt, cancellationToken).ConfigureAwait(false))
                    return ActivationFailure<BaseActivationPrunePage>(
                        "base.activation.removalBlocked", OperationStatus.Conflict, ErrorCategory.Conflict);
            }
            BaseActivationPruneEvidence? item = await PersistActivationPruneFloorAsync(connection, transaction, request.ApplicationId,
                id, pruneAuthorityGeneration, cancellationToken).ConfigureAwait(false);
            if (item is null)
                return ActivationFailure<BaseActivationPrunePage>("base.activation.removalBlocked", OperationStatus.Conflict, ErrorCategory.Conflict);
            evidence.Add(item);
            foreach (SqlitePruneReceipt receipt in receipts)
            {
                BaseActivationCompactedReceiptFact fact = BaseActivationCompactedReceiptFactContract.Create(
                    receipt.ReceiptSequence, receipt.ReceiptKey, receipt.AuthorityChecksum,
                    receipt.PriorOrderedChecksum, receipt.OrderedChecksum, pruneReceiptKey);
                await using SqliteCommand compact = connection.CreateCommand(); compact.Transaction = transaction;
                compact.CommandText = $"INSERT INTO {_names.ActivationInstanceReceiptCompactionFacts}(receipt_sequence,receipt_key,authority_checksum,prior_ordered_checksum,ordered_checksum,compaction_receipt_key,fact_checksum) VALUES($sequence,$key,$authority,$prior,$ordered,$compaction,$checksum); DELETE FROM {_names.ActivationInstanceReceipts} WHERE receipt_key=$key AND receipt_sequence=$sequence AND authority_checksum=$authority;";
                compact.Parameters.AddWithValue("$sequence", fact.ReceiptSequence);
                compact.Parameters.AddWithValue("$key", fact.ReceiptKey);
                compact.Parameters.Add("$authority", SqliteType.Blob).Value = fact.ReceiptAuthorityChecksum.ToArray();
                compact.Parameters.Add("$prior", SqliteType.Blob).Value = fact.PriorOrderedChecksum.ToArray();
                compact.Parameters.Add("$ordered", SqliteType.Blob).Value = fact.OrderedChecksum.ToArray();
                compact.Parameters.AddWithValue("$compaction", fact.CompactionReceiptKey);
                compact.Parameters.Add("$checksum", SqliteType.Blob).Value = fact.Checksum.ToArray();
                if (await compact.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
                    return ActivationFailure<BaseActivationPrunePage>(
                        "base.activation.conflict", OperationStatus.Conflict, ErrorCategory.Conflict);
                deletedReceiptCount = checked(deletedReceiptCount + 1);
                if (receipt.OperationKind == "activation-yielded-v1")
                    deletedYieldReceiptCount = checked(deletedYieldReceiptCount + 1);
            }
            await using SqliteCommand remove = connection.CreateCommand(); remove.Transaction = transaction;
            remove.CommandText = $"DELETE FROM {_names.Activations} WHERE activation_id=$id AND state=$disposed;";
            remove.Parameters.AddWithValue("$id", id); remove.Parameters.AddWithValue("$disposed", (int)BaseActivationState.Disposed);
            if (await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return ActivationFailure<BaseActivationPrunePage>("base.activation.conflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        }
        if (deletedYieldReceiptCount > priorReservation.RetainedUsedSlots)
            return ActivationFailure<BaseActivationPrunePage>(
                "base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
        if (deletedYieldReceiptCount > 0)
        {
            BaseActivationYieldReservationState resultingReservation = BaseActivationYieldReservationContract.Create(
                checked(priorReservation.Generation + 1), priorReservation.MaximumSlots,
                priorReservation.ReservedUnusedSlots,
                checked(priorReservation.RetainedUsedSlots - deletedYieldReceiptCount));
            await WriteYieldReservationStateAsync(
                connection, transaction, resultingReservation, cancellationToken).ConfigureAwait(false);
        }
        if (deletedReceiptCount > 0)
        {
            BaseActivationInstanceReceiptChainState resultingChain = BaseActivationInstanceReceiptChainContract.Create(
                priorChain.CurrentSequence, priorChain.OrderedChecksum.AsSpan(), checked(priorChain.Generation + 1));
            await WriteInstanceReceiptChainAsync(connection, transaction, resultingChain, cancellationToken).ConfigureAwait(false);
        }
        BaseActivationInstanceReceiptChainState resultingPruneChain = deletedReceiptCount == 0
            ? priorChain
            : BaseActivationInstanceReceiptChainContract.Create(
                priorChain.CurrentSequence, priorChain.OrderedChecksum.AsSpan(), checked(priorChain.Generation + 1));
        BaseActivationYieldReservationState resultingPruneReservation = deletedYieldReceiptCount == 0
            ? priorReservation
            : BaseActivationYieldReservationContract.Create(
                checked(priorReservation.Generation + 1), priorReservation.MaximumSlots,
                priorReservation.ReservedUnusedSlots,
                checked(priorReservation.RetainedUsedSlots - deletedYieldReceiptCount));
        if (page.Length != 0) await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        long evidenceBytes = 0;
        foreach (BaseActivationPruneEvidence item in evidence) evidenceBytes = checked(evidenceBytes + BaseActivationPruneEvidenceContract.MeasureCanonicalBytes(item));
        long transientBytes = checked(evidenceBytes + candidates.Sum(static id => 4L + Encoding.UTF8.GetByteCount(id)));
        int readIntervals = hasBoundaryCandidate ? 2 : 1;
        int indexOperations = checked(1 + (hasBoundaryCandidate ? 1 : 0) + page.Length * 2 + deletedReceiptCount * 2);
        if (candidates.Count > request.Limits.MaximumCandidates || evidenceBytes > request.Limits.MaximumEvidenceBytes
            || transientBytes > request.Limits.MaximumTransientBytes || indexOperations > request.Limits.MaximumIndexOperations
            || readIntervals > request.Limits.MaximumReadIntervals)
            return ActivationFailure<BaseActivationPrunePage>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        var result = new BaseActivationPrunePage { Items = evidence.MoveToImmutable(), NextActivationId = completed || page.Length == 0 ? null : page[^1],
            DeletedReceiptCount = deletedReceiptCount, DeletedYieldReceiptCount = deletedYieldReceiptCount,
            PriorChain = priorChain, ResultingChain = resultingPruneChain,
            PriorReservation = priorReservation, ResultingReservation = resultingPruneReservation,
            Completed = completed, Accounting = new BaseActivationAccounting
            {
                Candidates = candidates.Count, Comparisons = candidates.Count, IndexOperations = indexOperations,
                ReadIntervals = readIntervals, EvidenceBytes = evidenceBytes, TransientBytes = transientBytes,
            }, Disposition = BaseMutationRequestDisposition.Committed };
        await WriteControlReceiptAsync(connection, transaction, request.Identity, "activation-pruned", result,
            HPDBaseJsonSerializerContext.Default.BaseActivationPrunePage, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    private async ValueTask<List<SqlitePruneReceipt>> ReadPruneReceiptsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string activationId,
        CancellationToken cancellationToken)
    {
        var receipts = new List<SqlitePruneReceipt>();
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT receipt_key,operation_kind,receipt_backup_coverage,duplicate_resolve_until,receipt_sequence,authority_checksum,prior_ordered_checksum,ordered_checksum FROM {_names.ActivationInstanceReceipts} WHERE activation_id=$id ORDER BY receipt_sequence;";
        command.Parameters.AddWithValue("$id", activationId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            receipts.Add(new SqlitePruneReceipt(
                reader.GetString(0), reader.GetString(1),
                (BaseActivationProtectedBackupCoverage)reader.GetInt32(2), reader.GetInt64(3), reader.GetInt64(4),
                (byte[])reader[5], (byte[])reader[6], (byte[])reader[7]));
        return receipts;
    }

    private async ValueTask<bool> ReceiptBackupFloorSatisfiedForPruneAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqlitePruneReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (receipt.BackupCoverage == BaseActivationProtectedBackupCoverage.NotRequired) return true;
        if (receipt.BackupCoverage != BaseActivationProtectedBackupCoverage.Required) return false;
        string? storeInstanceId = await ReadStoreInstanceIdAsync(connection, cancellationToken).ConfigureAwait(false);
        if (storeInstanceId is null) return false;
        (_, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT artifact_id,artifact_sha256,application_id,logical_store_id,store_instance_id,restore_epoch,receipt_sequence,receipt_ordered_checksum,checkpoint_generation,committed_at,checkpoint_checksum FROM {_names.ActivationBackupCoverageCheckpoints} WHERE application_id=$application AND logical_store_id=$store AND store_instance_id=$instance AND restore_epoch=$restore AND receipt_sequence>=$sequence ORDER BY receipt_sequence LIMIT 1;";
        command.Parameters.AddWithValue("$application", _options.SemanticActivationApplicationId);
        command.Parameters.AddWithValue("$store", _options.StoreId);
        command.Parameters.AddWithValue("$instance", storeInstanceId);
        command.Parameters.AddWithValue("$restore", restoreEpoch);
        command.Parameters.AddWithValue("$sequence", receipt.ReceiptSequence);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return false;
        var checkpoint = new BaseActivationBackupCoverageCheckpoint
        {
            FormatVersion = 1, ArtifactId = reader.GetString(0), ArtifactSha256 = ((byte[])reader[1]).ToImmutableArray(),
            ApplicationId = reader.GetString(2), LogicalStoreId = reader.GetString(3), StoreInstanceId = reader.GetString(4),
            RestoreEpoch = reader.GetInt64(5), ReceiptSequence = reader.GetInt64(6),
            ReceiptOrderedChecksum = ((byte[])reader[7]).ToImmutableArray(), Generation = reader.GetInt64(8),
            CommittedAt = reader.GetInt64(9), Checksum = ((byte[])reader[10]).ToImmutableArray(),
        };
        return BaseActivationBackupCoverageCheckpointContract.IsValid(checkpoint)
            && checkpoint.ReceiptSequence >= receipt.ReceiptSequence;
    }

    private async ValueTask<BaseActivationPruneEvidence?> PersistActivationPruneFloorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string applicationId,
        string activationId,
        long pruneAuthorityGeneration,
        CancellationToken cancellationToken)
    {
        string definitionId;
        int definitionVersion;
        byte[] definitionChecksum;
        long terminalGeneration;
        byte[] terminalControlChecksum;
        byte[] terminalReceiptChecksum;
        byte[]? occurrenceChecksum;
        byte[]? resultChecksum;
        long effectiveDueAt;
        long yieldCount;
        long maximumYields;
        long executionSliceOrdinal;
        long? attemptStartedAt;
        long? sliceStartedAt;
        BaseActivationYieldDisposition? terminalYieldDisposition;
        string? terminalYieldFailureCode;
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"""
SELECT a.definition_id,a.definition_version,a.definition_checksum,a.generation,a.control_checksum,
       a.terminal_receipt_checksum,o.fact_checksum,a.canonical_result,a.effective_due_at,
       a.yield_count,a.maximum_yields,a.execution_slice_ordinal,a.attempt_started_at,a.slice_started_at,
       a.yield_terminal_disposition,a.yield_terminal_failure_code
FROM {_names.Activations} a
LEFT JOIN {_names.ActivationOccurrences} o ON o.occurrence_id=a.occurrence_id
WHERE a.activation_id=$id AND a.state=$disposed
  AND a.claim_fence IS NULL AND a.claim_worker IS NULL AND a.lease_revision IS NULL AND a.lease_expires_at IS NULL
  AND NOT EXISTS(SELECT 1 FROM {_names.ActivationEffects} e WHERE e.activation_id=a.activation_id);
""";
            read.Parameters.AddWithValue("$id", activationId);
            read.Parameters.AddWithValue("$disposed", (int)BaseActivationState.Disposed);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            definitionId = reader.GetString(0); definitionVersion = reader.GetInt32(1); definitionChecksum = (byte[])reader[2];
            terminalGeneration = reader.GetInt64(3); terminalControlChecksum = (byte[])reader[4];
            terminalReceiptChecksum = reader.IsDBNull(5) ? [] : (byte[])reader[5];
            occurrenceChecksum = reader.IsDBNull(6) ? null : (byte[])reader[6];
            resultChecksum = reader.IsDBNull(7) ? null : SHA256.HashData((byte[])reader[7]);
            effectiveDueAt = reader.GetInt64(8); yieldCount = reader.GetInt64(9);
            maximumYields = reader.GetInt64(10); executionSliceOrdinal = reader.GetInt64(11);
            attemptStartedAt = reader.IsDBNull(12) ? null : reader.GetInt64(12);
            sliceStartedAt = reader.IsDBNull(13) ? null : reader.GetInt64(13);
            terminalYieldDisposition = reader.IsDBNull(14) ? null : (BaseActivationYieldDisposition)reader.GetInt32(14);
            terminalYieldFailureCode = reader.IsDBNull(15) ? null : reader.GetString(15);
        }
        if (terminalReceiptChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(terminalControlChecksum,
                ActivationControlChecksum(activationId, terminalGeneration, BaseActivationState.Disposed,
                    effectiveDueAt, yieldCount, maximumYields, executionSliceOrdinal,
                    attemptStartedAt, sliceStartedAt, terminalYieldDisposition,
                    terminalYieldFailureCode))) return null;
        string storeInstanceId = await ReadStoreInstanceIdAsync(connection, cancellationToken).ConfigureAwait(false) ?? _options.StoreId;
        (_, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        byte[] publicationAuthority = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.publicationAuthority.v1\0{applicationId}\n{_options.StoreId}\n{storeInstanceId}\n{restoreEpoch}\n{pruneAuthorityGeneration}"));
        var evidence = new BaseActivationPruneEvidence
        {
            ActivationId = activationId,
            Definition = new BaseActivationDefinitionKey { Id = definitionId, Version = definitionVersion, Checksum = definitionChecksum.ToImmutableArray() },
            TerminalGeneration = terminalGeneration, TerminalControlChecksum = terminalControlChecksum.ToImmutableArray(),
            TerminalReceiptChecksum = terminalReceiptChecksum.ToImmutableArray(),
            OccurrenceChecksum = occurrenceChecksum?.ToImmutableArray(), ResultChecksum = resultChecksum?.ToImmutableArray(),
            PruneAuthorityGeneration = pruneAuthorityGeneration, ApplicationId = applicationId, LogicalStoreId = _options.StoreId,
            StoreInstanceId = storeInstanceId, RestoreEpoch = restoreEpoch,
            PublicationAuthorityChecksum = publicationAuthority.ToImmutableArray(), Checksum = [],
        };
        evidence = evidence with { Checksum = BaseActivationPruneEvidenceContract.Checksum(evidence) };
        await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {_names.ActivationPruneFloors}(activation_id,definition_id,definition_version,definition_checksum,terminal_generation,terminal_control_checksum,terminal_receipt_checksum,occurrence_checksum,result_checksum,prune_authority_generation,application_id,logical_store_id,store_instance_id,restore_epoch,publication_authority_checksum,authority_checksum) VALUES($id,$definition,$version,$definitionChecksum,$generation,$control,$terminalReceipt,$occurrence,$result,$authorityGeneration,$application,$logicalStore,$storeInstance,$restore,$publication,$authority);";
        insert.Parameters.AddWithValue("$id", activationId); insert.Parameters.AddWithValue("$definition", definitionId);
        insert.Parameters.AddWithValue("$version", definitionVersion); insert.Parameters.Add("$definitionChecksum", SqliteType.Blob).Value = definitionChecksum;
        insert.Parameters.AddWithValue("$generation", terminalGeneration); insert.Parameters.Add("$control", SqliteType.Blob).Value = terminalControlChecksum;
        insert.Parameters.Add("$terminalReceipt", SqliteType.Blob).Value = terminalReceiptChecksum;
        insert.Parameters.Add("$occurrence", SqliteType.Blob).Value = (object?)occurrenceChecksum ?? DBNull.Value;
        insert.Parameters.Add("$result", SqliteType.Blob).Value = (object?)resultChecksum ?? DBNull.Value;
        insert.Parameters.AddWithValue("$authorityGeneration", pruneAuthorityGeneration); insert.Parameters.AddWithValue("$application", applicationId);
        insert.Parameters.AddWithValue("$logicalStore", _options.StoreId); insert.Parameters.AddWithValue("$storeInstance", storeInstanceId);
        insert.Parameters.AddWithValue("$restore", restoreEpoch); insert.Parameters.Add("$publication", SqliteType.Blob).Value = publicationAuthority;
        insert.Parameters.Add("$authority", SqliteType.Blob).Value = evidence.Checksum.ToArray();
        return await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1 ? evidence : null;
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationIndeterminateResolution>> ResolveIndeterminateAsync(
        BaseActivationIndeterminateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); OperationResult<BaseActivationTransitionResult> result = await TransitionAsync(request.Reconciliation, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess() && result.Value is not null ? OperationResults.Ok(new BaseActivationIndeterminateResolution { Transition = result.Value })
            : new OperationResult<BaseActivationIndeterminateResolution> { Status = result.Status, Error = result.Error };
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<BaseActivationQuarantinePage>> ReadQuarantineAsync(
        BaseActivationQuarantineRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        if (request.Take is < 1 or > 256 || request.AfterSequence < 0)
            return ValueTask.FromResult(ActivationFailure<BaseActivationQuarantinePage>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation));
        return ValueTask.FromResult(OperationResults.Ok(new BaseActivationQuarantinePage { Items = [], NextSequence = null }));
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationDependencyResult>> ReadDependenciesAsync(
        BaseActivationDependencyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ApplicationId) || request.MaximumDefinitions is < 1 or > 4096
            || request.DeadlineUtc.ToUnixTimeMilliseconds() < 0)
            return ActivationFailure<BaseActivationDependencyResult>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!await ActivationSchemaExistsAsync(connection, cancellationToken).ConfigureAwait(false))
            return OperationResults.Ok(new BaseActivationDependencyResult
            {
                Dependencies = [],
                CapturedGeneration = 0,
                Accounting = new BaseActivationAccounting
                {
                    Candidates = 0,
                    Comparisons = 0,
                    IndexOperations = 1,
                    ReadIntervals = 1,
                    EvidenceBytes = 0,
                    TransientBytes = 0,
                },
            });
        (long generation, _) = await ReadActivationAuthorityAsync(connection, null, cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<string, (BaseActivationDefinitionKey Definition, bool Activation, bool Schedule)>(StringComparer.Ordinal);
        await using (SqliteCommand activations = connection.CreateCommand())
        {
            activations.CommandText = $"SELECT DISTINCT definition_id,definition_version,definition_checksum FROM {_names.Activations};";
            await using SqliteDataReader reader = await activations.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                Merge(new BaseActivationDefinitionKey
                {
                    Id = reader.GetString(0), Version = reader.GetInt32(1),
                    Checksum = ((byte[])reader[2]).ToImmutableArray(),
                }, activation: true, schedule: false);
        }
        await using (SqliteCommand schedules = connection.CreateCommand())
        {
            schedules.CommandText = $"SELECT definition_json FROM {_names.ActivationSchedules};";
            await using SqliteDataReader reader = await schedules.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                BaseScheduleDefinition definition = JsonSerializer.Deserialize(
                    (byte[])reader[0], HPDBaseJsonSerializerContext.Default.BaseScheduleDefinition)
                    ?? throw new InvalidOperationException("base.activation.scheduleInvalid");
                Merge(BaseScheduleDefinitionBuilder.Create(definition).Activation, activation: false, schedule: true);
            }
        }
        if (values.Count > request.MaximumDefinitions)
            return ActivationFailure<BaseActivationDependencyResult>(
                "base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        BaseActivationDefinitionDependency[] dependencies = values.Values
            .OrderBy(static item => item.Definition.Id, StringComparer.Ordinal)
            .ThenBy(static item => item.Definition.Version)
            .ThenBy(static item => Convert.ToHexString(item.Definition.Checksum.AsSpan()), StringComparer.Ordinal)
            .Select(static item => new BaseActivationDefinitionDependency
            {
                Definition = item.Definition with { Checksum = item.Definition.Checksum.ToArray().ToImmutableArray() },
                ReferencedByActivation = item.Activation, ReferencedBySchedule = item.Schedule,
            }).ToArray();
        long evidenceBytes = dependencies.Sum(static item =>
            Encoding.UTF8.GetByteCount(item.Definition.Id) + item.Definition.Checksum.Length + 18L);
        return OperationResults.Ok(new BaseActivationDependencyResult
        {
            Dependencies = dependencies.ToImmutableArray(), CapturedGeneration = generation,
            Accounting = new BaseActivationAccounting
            {
                Candidates = dependencies.Length, Comparisons = dependencies.Length,
                IndexOperations = 2, ReadIntervals = 2, EvidenceBytes = evidenceBytes,
                TransientBytes = evidenceBytes,
            },
        });

        void Merge(BaseActivationDefinitionKey definition, bool activation, bool schedule)
        {
            string key = $"{definition.Id}\n{definition.Version}\n{Convert.ToHexString(definition.Checksum.AsSpan())}";
            if (values.TryGetValue(key, out var current))
                values[key] = (current.Definition, current.Activation || activation, current.Schedule || schedule);
            else
                values.Add(key, (definition with { Checksum = definition.Checksum.ToArray().ToImmutableArray() }, activation, schedule));
        }
    }

    private async ValueTask<bool> ActivationSchemaExistsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
        command.Parameters.AddWithValue("$name", _names.Activations);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is not null && value is not DBNull;
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationDueObservation>> ObserveDueAsync(
        BaseActivationDueObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationDueObservation>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (!ActivationLimitsValid(request.Limits) || request.MaximumCandidates is < 1 or > 256)
            return ActivationFailure<BaseActivationDueObservation>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        (long generation, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, null, cancellationToken).ConfigureAwait(false);
        List<SqliteActivationRow> rows = await ReadDueRowsAsync(connection, null, request.Definitions, request.Scope,
            request.AcceptedTime.CapturedUtc, request.After, request.MaximumCandidates, cancellationToken).ConfigureAwait(false);
        SqliteActivationRow? first = rows.FirstOrDefault();
        BaseActivationDueBoundary? boundary = first is null ? null : ActivationBoundary(first, request.AcceptedTime.CapturedUtc);
        byte[] token = ActivationDueToken(generation, restoreEpoch, request.AcceptedTime.CapturedUtc,
            request.Scope.ProtectedIndexDigest.AsSpan(), request.Definitions, boundary);
        BaseAtomicReadIntervalEvidence interval = ActivationDueInterval(request.Scope, request.AcceptedTime.CapturedUtc, request.After, boundary);
        long evidenceBytes = checked(token.Length + ActivationIntervalBytes(interval));
        if (evidenceBytes > request.Limits.MaximumEvidenceBytes)
            return ActivationFailure<BaseActivationDueObservation>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        return OperationResults.Ok(new BaseActivationDueObservation
        {
            Earliest = boundary,
            Token = new BaseDueObservationToken { Value = token.ToImmutableArray() },
            Intervals = [interval],
            Accounting = ActivationAccounting(rows.Count, evidenceBytes),
        });
    }

    /// <inheritdoc />
    public async ValueTask<BaseDueWaitResult> WaitForDueChangeAsync(
        BaseDueObservationToken token,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        (long expectedGeneration, long expectedRestore, long acceptedAt) = DecodeActivationTokenAuthority(token.Value.AsSpan());
        if (expectedGeneration < 0 || _timeProvider.GetUtcNow().ToUnixTimeMilliseconds() - acceptedAt > 300_000)
            return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.TokenInvalid };
        while (_timeProvider.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            (long generation, long restore) = await ReadActivationAuthorityAsync(connection, null, cancellationToken).ConfigureAwait(false);
            if (restore != expectedRestore)
                return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.TokenInvalid };
            if (generation != expectedGeneration)
                return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.Changed };
            TimeSpan remaining = deadline - _timeProvider.GetUtcNow();
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.Deadline };
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationClaimResult>> TryClaimNextAsync(
        BaseActivationClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationClaimResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (!ActivationLimitsValid(request.Limits) || request.LeaseMilliseconds <= 0)
            return ActivationFailure<BaseActivationClaimResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool claimReceiptFound, OperationResult<BaseActivationClaimResult> claimReceipt) = await ReadInstanceReceiptAsync(
            connection, transaction, request.Identity, "activation-claimed", HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult,
            static value => value, request.AcceptedTime.CapturedUtc, cancellationToken).ConfigureAwait(false);
        if (claimReceiptFound)
            return await ResolveSqliteClaimReplayAsync(connection, transaction, claimReceipt,
                request.AcceptedTime.CapturedUtc, cancellationToken).ConfigureAwait(false);
        (long generation, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        (long expectedGeneration, long expectedRestore, long tokenAcceptedAt) = DecodeActivationTokenAuthority(request.Observation.Value.AsSpan());
        if (expectedGeneration < 0 || request.AcceptedTime.CapturedUtc - tokenAcceptedAt > 300_000)
            return ActivationFailure<BaseActivationClaimResult>("base.activation.observationTokenInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (generation != expectedGeneration || restoreEpoch != expectedRestore)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationObservationChangedResult(
                new BaseDueObservationToken { Value = ActivationDueToken(generation, restoreEpoch, request.AcceptedTime.CapturedUtc,
                    request.Worker.Scope.ProtectedIndexDigest.AsSpan(), request.Worker.Definitions, null).ToImmutableArray() }));
        }
        List<SqliteActivationRow> rows = await ReadDueRowsAsync(connection, transaction, request.Worker.Definitions,
            request.Worker.Scope, request.AcceptedTime.CapturedUtc, null, 1, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimEmptyResult(request.Observation));
        }
        SqliteActivationRow row = rows[0];
        if (row.State == BaseActivationState.Claimed)
        {
            long recovered = checked(row.Generation + 1);
            await UpdateRecoveredAsync(connection, transaction, row, recovered,
                request.AcceptedTime.CapturedUtc, cancellationToken).ConfigureAwait(false);
            await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (!await ActivationRowCapacityAllowsAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
                return ActivationFailure<BaseActivationClaimResult>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            BaseActivationClaimResult recoveredResult = new BaseActivationRecoveredClaimResult(row.ActivationId, recovered);
            await WriteInstanceReceiptAsync(connection, transaction, request.Identity, "activation-claimed", row,
                request.AcceptedTime.CapturedUtc, recoveredResult,
                HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok(recoveredResult);
        }

        bool resumedYield = row.State == BaseActivationState.YieldPending;
        int attempt = resumedYield ? row.AttemptNumber : checked(row.AttemptNumber + 1);
        long claimEpoch = checked(row.ClaimEpoch + 1);
        long executionSliceOrdinal = checked(row.ExecutionSliceOrdinal + 1);
        long attemptStartedAt = resumedYield
            ? row.AttemptStartedAt ?? throw new InvalidOperationException("base.activation.providerContractInvalid")
            : request.AcceptedTime.CapturedUtc;
        long sliceStartedAt = request.AcceptedTime.CapturedUtc;
        long resultingGeneration = checked(row.Generation + 1);
        byte[] fence = BaseActivationClaimChecksumContract.Create(row.ActivationId, attempt,
            claimEpoch, executionSliceOrdinal, attemptStartedAt, sliceStartedAt,
            row.YieldCount, row.MaximumYields, request.Worker.WorkerIdentity).ToArray();
        long leaseExpires = checked(request.AcceptedTime.CapturedUtc + request.LeaseMilliseconds);
        byte[] leaseChecksum = ActivationHash($"base.activation.lease.v2\0{row.ActivationId}\n1\n{leaseExpires}");
        byte[] controlChecksum = ActivationControlChecksum(row.ActivationId, resultingGeneration,
            BaseActivationState.Claimed, row.EffectiveDueAt, row.YieldCount, row.MaximumYields,
            executionSliceOrdinal, attemptStartedAt, sliceStartedAt, null, null);
        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {_names.Activations} SET state=$state,generation=$generation,attempt_number=$attempt,execution_slice_ordinal=$slice,attempt_started_at=$attempt_started,slice_started_at=$slice_started,claim_epoch=$epoch,claim_fence=$fence,claim_worker=$worker,lease_revision=1,lease_expires_at=$expires,yield_terminal_disposition=NULL,yield_terminal_failure_code=NULL,control_checksum=$checksum WHERE activation_id=$id AND generation=$expected AND state IN ($pending,$retry,$yield);";
            update.Parameters.AddWithValue("$state", (int)BaseActivationState.Claimed);
            update.Parameters.AddWithValue("$generation", resultingGeneration);
            update.Parameters.AddWithValue("$attempt", attempt);
            update.Parameters.AddWithValue("$slice", executionSliceOrdinal);
            update.Parameters.AddWithValue("$attempt_started", attemptStartedAt);
            update.Parameters.AddWithValue("$slice_started", sliceStartedAt);
            update.Parameters.AddWithValue("$epoch", claimEpoch);
            update.Parameters.Add("$fence", SqliteType.Blob).Value = fence;
            update.Parameters.AddWithValue("$worker", request.Worker.WorkerIdentity);
            update.Parameters.AddWithValue("$expires", leaseExpires);
            update.Parameters.Add("$checksum", SqliteType.Blob).Value = controlChecksum;
            update.Parameters.AddWithValue("$id", row.ActivationId);
            update.Parameters.AddWithValue("$expected", row.Generation);
            update.Parameters.AddWithValue("$pending", (int)BaseActivationState.Pending);
            update.Parameters.AddWithValue("$retry", (int)BaseActivationState.RetryPending);
            update.Parameters.AddWithValue("$yield", (int)BaseActivationState.YieldPending);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return ActivationFailure<BaseActivationClaimResult>("base.activation.claimConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            }
        }
        await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var claim = new BaseActivationClaimAuthority
        {
            ActivationId = row.ActivationId, AttemptNumber = attempt, ClaimEpoch = claimEpoch,
            ActivationGeneration = resultingGeneration,
            ExecutionSliceOrdinal = executionSliceOrdinal, AttemptStartedAt = attemptStartedAt,
            SliceStartedAt = sliceStartedAt, YieldCount = row.YieldCount, MaximumYields = row.MaximumYields,
            FencingToken = fence.ToImmutableArray(), WorkerIdentity = request.Worker.WorkerIdentity,
            CancellationGeneration = 0, StoreInstanceId = CurrentStoreInstanceId, RestoreEpoch = restoreEpoch,
            DefinitionChecksum = row.DefinitionChecksum.ToImmutableArray(),
        };
        var lease = new BaseActivationLeaseObservation
        {
            LeaseRevision = 1, LeaseExpiresAt = leaseExpires, Checksum = leaseChecksum.ToImmutableArray(),
        };
        var attemptEvidence = new BaseActivationAttemptEvidence
        {
            AttemptId = $"{row.ActivationId}:{attempt}", AttemptNumber = attempt,
            StartedAt = request.AcceptedTime.CapturedUtc,
            Checksum = ActivationHash($"base.activation.attempt.v2\0{row.ActivationId}\n{attempt}").ToImmutableArray(),
        };
        BaseActivationClaimResult claimedResult = new BaseActivationClaimedResult(
            row.Payload(), claim, lease, attemptEvidence,
            [ActivationDueInterval(request.Worker.Scope, request.AcceptedTime.CapturedUtc, null,
                ActivationBoundary(row, request.AcceptedTime.CapturedUtc))],
            ActivationAccounting(1, 128));
        if (!await ActivationRowCapacityAllowsAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationClaimResult>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        await WriteInstanceReceiptAsync(connection, transaction, request.Identity, "activation-claimed", row,
            request.AcceptedTime.CapturedUtc, claimedResult,
            HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(claimedResult);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseTransactionalActivationCandidate>> ReadTransactionalCandidateAsync(
        BaseTransactionalActivationCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false)
            || !ActivationLimitsValid(request.Limits))
            return ActivationFailure<BaseTransactionalActivationCandidate>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        (long generation, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, null, cancellationToken).ConfigureAwait(false);
        (long expectedGeneration, long expectedRestore, long observedAt) = DecodeActivationTokenAuthority(request.Observation.Value.AsSpan());
        if (generation != expectedGeneration || restoreEpoch != expectedRestore
            || request.AcceptedTime.CapturedUtc - observedAt > 300_000)
            return ActivationFailure<BaseTransactionalActivationCandidate>(
                "base.activation.claimUnavailable", OperationStatus.Conflict, ErrorCategory.Conflict);
        List<SqliteActivationRow> rows = await ReadDueRowsAsync(
            connection, null, [request.Definition], request.Scope, request.AcceptedTime.CapturedUtc, null, 1, cancellationToken).ConfigureAwait(false);
        if (rows.Count != 1)
            return ActivationFailure<BaseTransactionalActivationCandidate>(
                "base.activation.notDue", OperationStatus.Conflict, ErrorCategory.Conflict);
        SqliteActivationRow row = rows[0];
        BaseAtomicReadIntervalEvidence interval = ActivationDueInterval(
            request.Scope, request.AcceptedTime.CapturedUtc, null, ActivationBoundary(row, request.AcceptedTime.CapturedUtc));
        long evidenceBytes = checked(row.CanonicalInput.LongLength + row.ControlChecksum.LongLength + ActivationIntervalBytes(interval));
        if (evidenceBytes > request.Limits.MaximumEvidenceBytes || row.CanonicalInput.LongLength > request.Limits.MaximumInputBytes)
            return ActivationFailure<BaseTransactionalActivationCandidate>(
                "base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        return OperationResults.Ok(new BaseTransactionalActivationCandidate
        {
            Payload = row.Payload(),
            ActivationGeneration = row.Generation,
            AcceptedAt = request.AcceptedTime.CapturedUtc,
            ControlChecksum = row.ControlChecksum.ToImmutableArray(),
            ReadIntervals = [interval],
            Accounting = ActivationAccounting(1, evidenceBytes),
            Limits = request.Limits with { },
        });
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(
        BaseActivationRenewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationRenewResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool found, OperationResult<BaseActivationRenewResult> receipt) = await ReadInstanceReceiptAsync(
            connection, transaction, request.Identity, "activation-renewed", HPDBaseJsonSerializerContext.Default.BaseActivationRenewResult,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate },
            request.AcceptedTime.CapturedUtc, cancellationToken).ConfigureAwait(false);
        if (found) return receipt;
        (long _, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (restoreEpoch != request.Claim.RestoreEpoch)
            return ActivationFailure<BaseActivationRenewResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        SqliteActivationRow? renewalRow = await ReadActivationAsync(
            connection, transaction, request.Claim.ActivationId, cancellationToken).ConfigureAwait(false);
        if (renewalRow is null)
            return ActivationFailure<BaseActivationRenewResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        long revision = checked(request.ExpectedLeaseRevision + 1);
        long expires = checked(request.AcceptedTime.CapturedUtc + request.ExtensionMilliseconds);
        byte[] checksum = ActivationHash($"base.activation.lease.v2\0{request.Claim.ActivationId}\n{revision}\n{expires}");
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.Activations} SET lease_revision=$next,lease_expires_at=$expires WHERE activation_id=$id AND state=$state AND attempt_number=$attempt AND claim_epoch=$epoch AND claim_fence=$fence AND claim_worker=$worker AND lease_revision=$expected AND lease_expires_at>$now;";
        command.Parameters.AddWithValue("$next", revision); command.Parameters.AddWithValue("$expires", expires);
        command.Parameters.AddWithValue("$id", request.Claim.ActivationId); command.Parameters.AddWithValue("$state", (int)BaseActivationState.Claimed);
        command.Parameters.AddWithValue("$attempt", request.Claim.AttemptNumber); command.Parameters.AddWithValue("$epoch", request.Claim.ClaimEpoch);
        command.Parameters.Add("$fence", SqliteType.Blob).Value = request.Claim.FencingToken.ToArray(); command.Parameters.AddWithValue("$worker", request.Claim.WorkerIdentity);
        command.Parameters.AddWithValue("$expected", request.ExpectedLeaseRevision); command.Parameters.AddWithValue("$now", request.AcceptedTime.CapturedUtc);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return ActivationFailure<BaseActivationRenewResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        }
        var result = new BaseActivationRenewResult
        {
            Claim = request.Claim,
            Lease = new BaseActivationLeaseObservation { LeaseRevision = revision, LeaseExpiresAt = expires, Checksum = checksum.ToImmutableArray() },
            Accounting = ActivationAccounting(1, 64), Disposition = BaseMutationRequestDisposition.Committed,
        };
        await WriteInstanceReceiptAsync(connection, transaction, request.Identity, "activation-renewed", renewalRow,
            request.AcceptedTime.CapturedUtc, result,
            HPDBaseJsonSerializerContext.Default.BaseActivationRenewResult, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationTransitionResult>> TransitionAsync(
        BaseActivationTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string receiptKind = SqliteActivationTransitionReceiptKind(request);
        if (request is BaseActivationYieldRequest)
        {
            (bool yieldFound, OperationResult<BaseActivationYieldReceipt> yieldReplay) = await ReadInstanceReceiptAsync(
                connection, transaction, request.Identity, receiptKind,
                HPDBaseJsonSerializerContext.Default.BaseActivationYieldReceipt,
                static value => value, request.AcceptedTime.CapturedUtc, cancellationToken).ConfigureAwait(false);
            if (yieldFound)
                return yieldReplay.IsSuccess() && yieldReplay.Value is { } storedYield
                    ? OperationResults.Ok(storedYield.ToTransitionResult(BaseMutationRequestDisposition.Duplicate))
                    : new OperationResult<BaseActivationTransitionResult>
                    { Status = yieldReplay.Status, Error = yieldReplay.Error };
        }
        (bool found, OperationResult<BaseActivationTransitionResult> receipt) = await ReadInstanceReceiptAsync(
            connection, transaction, request.Identity, receiptKind, HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate },
            request.AcceptedTime.CapturedUtc, cancellationToken).ConfigureAwait(false);
        if (found) return receipt;
        SqliteActivationRow? row = await ReadActivationAsync(connection, transaction, request.ActivationId, cancellationToken).ConfigureAwait(false);
        if (row is null)
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.notFound", OperationStatus.NotFound, ErrorCategory.NotFound);
        BaseEffectExecutionAuthority? storedEffect = await ReadEffectAsync(connection, transaction, row.ActivationId, cancellationToken).ConfigureAwait(false);
        if (request is BaseActivationEffectHeartbeatRequest effectHeartbeat)
        {
            SqliteExecutorRow? executor = storedEffect is null ? null : await ReadExecutorAsync(connection, transaction,
                storedEffect.Executor.ApplicationId, storedEffect.Executor.HostId, storedEffect.Executor.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
            if (row.State != BaseActivationState.EffectStarted || storedEffect is null || !SqliteEffectMatches(storedEffect, effectHeartbeat.Effect) ||
                storedEffect.HeartbeatRevision != effectHeartbeat.ExpectedHeartbeatRevision || effectHeartbeat.ExtensionMilliseconds <= 0 ||
                executor is null || executor.Retired || !SqliteExecutorMatches(executor.Authority, storedEffect.Executor) || executor.Heartbeat.HeartbeatExpiresAt <= request.AcceptedTime.CapturedUtc)
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.effectLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            BaseEffectExecutionAuthority replacement = SqliteEffect(storedEffect.Claim, storedEffect.Executor, storedEffect.EffectStartGeneration,
                checked(storedEffect.HeartbeatRevision + 1), checked(request.AcceptedTime.CapturedUtc + effectHeartbeat.ExtensionMilliseconds));
            await WriteEffectAsync(connection, transaction, replacement, cancellationToken).ConfigureAwait(false);
            var heartbeatResult = new BaseActivationTransitionResult
            {
                State = row.State, Generation = row.Generation, ControlChecksum = row.ControlChecksum.ToImmutableArray(),
                Accounting = ActivationAccounting(1, 128), Disposition = BaseMutationRequestDisposition.Committed, Effect = replacement,
            };
            await WriteInstanceReceiptAsync(connection, transaction, request.Identity, receiptKind, row,
                request.AcceptedTime.CapturedUtc, heartbeatResult,
                HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok(heartbeatResult);
        }
        BaseActivationState state;
        byte[]? result = null;
        BaseActivationClaimAuthority? claim = null;
        BaseEffectExecutionAuthority? resultingEffect = null;
        BaseActivationYieldRequest? yieldRequest = null;
        BaseActivationYieldDisposition? yieldDisposition = null;
        long resultingYieldCount = row.YieldCount;
        if (request is BaseActivationCompleteRequest complete)
        {
            claim = complete.Claim;
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(complete.CanonicalResult.AsSpan()), complete.ResultChecksum.AsSpan()))
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            state = BaseActivationState.Succeeded; result = complete.CanonicalResult.ToArray();
        }
        else if (request is BaseActivationFailRequest failed)
        {
            if ((failed.Disposition == BaseActivationFailureDisposition.Retry) != failed.RetryDueAt.HasValue || failed.RetryDueAt is < 0)
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            claim = failed.Claim;
            state = failed.Disposition == BaseActivationFailureDisposition.Retry ? BaseActivationState.RetryPending : BaseActivationState.Exhausted;
        }
        else if (request is BaseActivationYieldRequest yielded)
        {
            claim = yielded.Claim;
            long? requestedResumeAt = SqliteCanonicalYieldResumeAt(yielded.RequestedResumeAt);
            long expectedEffectiveDueAt = requestedResumeAt.HasValue
                ? Math.Max(requestedResumeAt.Value, request.AcceptedTime.CapturedUtc)
                : request.AcceptedTime.CapturedUtc;
            if (yielded.ProgressFingerprint.Length != 32 || yielded.ExpectedYieldCount != row.YieldCount
                || yielded.MaximumYields != row.MaximumYields || yielded.MaximumYields <= 0
                || expectedEffectiveDueAt < 0 || yielded.EffectiveDueAt != expectedEffectiveDueAt)
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.yieldInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            yieldRequest = yielded;
            if (row.YieldCount == row.MaximumYields)
            {
                state = BaseActivationState.Exhausted;
                yieldDisposition = BaseActivationYieldDisposition.LimitExceeded;
            }
            else
            {
                state = BaseActivationState.YieldPending;
                yieldDisposition = BaseActivationYieldDisposition.Yielded;
                resultingYieldCount = checked(row.YieldCount + 1);
            }
        }
        else if (request is BaseActivationCancelRequest cancel && cancel.ExpectedGeneration == row.Generation)
        {
            state = row.State == BaseActivationState.EffectStarted
                ? BaseActivationState.EffectStarted
                : BaseActivationState.Cancelled;
            resultingEffect = row.State == BaseActivationState.EffectStarted ? storedEffect : null;
        }
        else if (request is BaseActivationBeginEffectRequest begin)
        {
            SqliteExecutorRow? executor = await ReadExecutorAsync(connection, transaction, begin.Executor.ApplicationId,
                begin.Executor.HostId, begin.Executor.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
            if (!SqliteClaimMatches(row, begin.Claim) || begin.HeartbeatMilliseconds <= 0 || executor is null || executor.Retired ||
                !SqliteExecutorMatches(executor.Authority, begin.Executor) || !SqliteHeartbeatsEqual(executor.Heartbeat, begin.ExecutorHeartbeat) ||
                executor.Heartbeat.HeartbeatExpiresAt <= request.AcceptedTime.CapturedUtc)
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.executorLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            state = BaseActivationState.EffectStarted;
            resultingEffect = SqliteEffect(begin.Claim, begin.Executor, checked(row.Generation + 1), 1,
                checked(request.AcceptedTime.CapturedUtc + begin.HeartbeatMilliseconds));
        }
        else if (request is BaseActivationCompleteEffectRequest completeEffect && row.State == BaseActivationState.EffectStarted &&
            storedEffect is not null && SqliteEffectMatches(storedEffect, completeEffect.Effect))
        {
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(completeEffect.CanonicalResult.AsSpan()), completeEffect.ResultChecksum.AsSpan()))
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            state = BaseActivationState.Succeeded; result = completeEffect.CanonicalResult.ToArray();
        }
        else if (request is BaseActivationRecoverEffectRequest recover && row.State == BaseActivationState.EffectStarted &&
            storedEffect is not null && SqliteEffectMatches(storedEffect, recover.Effect) && storedEffect.HeartbeatExpiresAt <= request.AcceptedTime.CapturedUtc)
        {
            SqliteExecutorRow? executor = await ReadExecutorAsync(connection, transaction, storedEffect.Executor.ApplicationId,
                storedEffect.Executor.HostId, storedEffect.Executor.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
            if (executor is not null && !executor.Retired && SqliteExecutorMatches(executor.Authority, storedEffect.Executor) &&
                executor.Heartbeat.HeartbeatExpiresAt > request.AcceptedTime.CapturedUtc)
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.effectOwned", OperationStatus.Conflict, ErrorCategory.Conflict);
            state = BaseActivationState.OutcomeUnknown;
        }
        else if (request is BaseActivationReconcileEffectRequest reconcile && row.State == BaseActivationState.OutcomeUnknown &&
            storedEffect is not null && row.Generation == reconcile.ExpectedGeneration &&
            storedEffect.EffectStartGeneration == reconcile.ExpectedEffectStartGeneration &&
            reconcile.ExpectedEffectChecksum.Length == 32 &&
            CryptographicOperations.FixedTimeEquals(storedEffect.Checksum.AsSpan(), reconcile.ExpectedEffectChecksum.AsSpan()))
        {
            if (reconcile.VerificationEvidence.IsDefaultOrEmpty || reconcile.VerificationChecksum.Length != 32 ||
                !Enum.IsDefined(reconcile.Disposition) ||
                !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(reconcile.VerificationEvidence.AsSpan()), reconcile.VerificationChecksum.AsSpan()))
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            state = reconcile.Disposition switch
            {
                BaseEffectReconciliationDisposition.Succeeded => BaseActivationState.Succeeded,
                BaseEffectReconciliationDisposition.Exhausted => BaseActivationState.Exhausted,
                BaseEffectReconciliationDisposition.Disposed => BaseActivationState.Disposed,
                _ => BaseActivationState.OutcomeUnknown,
            };
            result = reconcile.Disposition == BaseEffectReconciliationDisposition.Succeeded
                ? reconcile.VerificationEvidence.ToArray()
                : null;
        }
        else if (request is BaseActivationOperatorRetryRequest retry && row.State == BaseActivationState.Exhausted &&
            row.Generation == retry.ExpectedGeneration && retry.RetryDueAt >= request.AcceptedTime.CapturedUtc)
            state = BaseActivationState.RetryPending;
        else if (request is BaseActivationDisposeRequest dispose && row.Generation == dispose.ExpectedGeneration &&
            row.State is BaseActivationState.Succeeded or BaseActivationState.Exhausted or BaseActivationState.Cancelled or BaseActivationState.Migrated)
            state = BaseActivationState.Disposed;
        else
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        if (claim is not null && !SqliteClaimMatches(row, claim))
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        long generation = checked(row.Generation + 1);
        long resultingEffectiveDueAt = yieldRequest is not null
            ? yieldRequest.EffectiveDueAt
            : state == BaseActivationState.RetryPending
            ? request switch
            {
                BaseActivationFailRequest retry => retry.RetryDueAt!.Value,
                BaseActivationOperatorRetryRequest retry => retry.RetryDueAt,
                _ => row.EffectiveDueAt,
            }
            : row.EffectiveDueAt;
        BaseActivationYieldDisposition? terminalYieldDisposition = yieldDisposition == BaseActivationYieldDisposition.LimitExceeded
            ? BaseActivationYieldDisposition.LimitExceeded : null;
        string? terminalYieldFailureCode = terminalYieldDisposition.HasValue
            ? "base.activation.yieldLimitExceeded" : null;
        byte[] control = ActivationControlChecksum(row.ActivationId, generation, state,
            resultingEffectiveDueAt, resultingYieldCount, row.MaximumYields,
            row.ExecutionSliceOrdinal, row.AttemptStartedAt, row.SliceStartedAt,
            terminalYieldDisposition, terminalYieldFailureCode);
        var transitionResult = new BaseActivationTransitionResult
        {
            State = state, Generation = generation, ControlChecksum = control.ToImmutableArray(),
            Accounting = ActivationAccounting(1, 64), Disposition = BaseMutationRequestDisposition.Committed, Effect = resultingEffect,
            CanonicalResult = result?.ToImmutableArray() ?? ImmutableArray<byte>.Empty,
            YieldCount = resultingYieldCount,
            ExecutionSliceOrdinal = row.ExecutionSliceOrdinal,
            EffectiveDueAt = yieldRequest?.EffectiveDueAt,
            YieldDisposition = yieldDisposition,
            YieldTerminalFailureCode = yieldDisposition == BaseActivationYieldDisposition.LimitExceeded
                ? "base.activation.yieldLimitExceeded" : null,
        };
        BaseActivationYieldReceipt? yieldReceipt = yieldRequest is null ? null : new()
        {
            Definition = new BaseActivationDefinitionKey
            {
                Id = row.DefinitionId, Version = row.DefinitionVersion,
                Checksum = row.DefinitionChecksum.ToImmutableArray(),
            },
            ActivationId = row.ActivationId,
            PriorGeneration = row.Generation,
            ResultingGeneration = generation,
            AttemptNumber = row.AttemptNumber,
            ExecutionSliceOrdinal = row.ExecutionSliceOrdinal,
            AttemptStartedAt = row.AttemptStartedAt!.Value,
            SliceStartedAt = row.SliceStartedAt!.Value,
            PriorYieldCount = row.YieldCount,
            ResultingYieldCount = resultingYieldCount,
            EffectiveDueAt = resultingEffectiveDueAt,
            ProgressFingerprint = yieldRequest.ProgressFingerprint.ToArray().ToImmutableArray(),
            ResultingState = state,
            Disposition = yieldDisposition!.Value,
            FailureCode = terminalYieldFailureCode,
            ControlChecksum = control.ToImmutableArray(),
            Accounting = transitionResult.Accounting with { },
        };
        bool terminalTransition = state is BaseActivationState.Succeeded or BaseActivationState.Exhausted
            or BaseActivationState.Cancelled or BaseActivationState.Migrated or BaseActivationState.Disposed;
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.Activations} SET state=$state,generation=$generation,claim_fence=NULL,claim_worker=NULL,lease_revision=NULL,lease_expires_at=NULL,canonical_result=$result,effective_due_at=CASE WHEN $state IN ($retry,$yield) THEN $now ELSE effective_due_at END,eligible=CASE WHEN $state IN ($retry,$yield) THEN 1 ELSE 0 END,yield_count=$yield_count,yield_terminal_disposition=$yield_disposition,yield_terminal_failure_code=$yield_failure,control_checksum=$checksum,terminal_receipt_checksum=$terminalReceipt WHERE activation_id=$id AND generation=$expected;";
        command.Parameters.AddWithValue("$state", (int)state); command.Parameters.AddWithValue("$generation", generation);
        command.Parameters.Add("$result", SqliteType.Blob).Value = (object?)result ?? DBNull.Value;
        command.Parameters.AddWithValue("$retry", (int)BaseActivationState.RetryPending);
        command.Parameters.AddWithValue("$yield", (int)BaseActivationState.YieldPending);
        command.Parameters.AddWithValue("$yield_count", resultingYieldCount);
        command.Parameters.AddWithValue("$yield_disposition", yieldDisposition == BaseActivationYieldDisposition.LimitExceeded
            ? (int)BaseActivationYieldDisposition.LimitExceeded : DBNull.Value);
        command.Parameters.AddWithValue("$yield_failure", yieldDisposition == BaseActivationYieldDisposition.LimitExceeded
            ? "base.activation.yieldLimitExceeded" : DBNull.Value);
        command.Parameters.AddWithValue("$now", request switch
        {
            BaseActivationFailRequest retry => (object?)retry.RetryDueAt ?? request.AcceptedTime.CapturedUtc,
            BaseActivationOperatorRetryRequest retry => retry.RetryDueAt,
            BaseActivationYieldRequest yielded => yielded.EffectiveDueAt,
            _ => request.AcceptedTime.CapturedUtc,
        });
        command.Parameters.Add("$checksum", SqliteType.Blob).Value = control; command.Parameters.AddWithValue("$id", row.ActivationId); command.Parameters.AddWithValue("$expected", row.Generation);
        command.Parameters.Add("$terminalReceipt", SqliteType.Blob).Value = DBNull.Value;
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        await ApplyYieldReceiptReservationTransitionAsync(
            connection, transaction, row, state, yieldDisposition, cancellationToken).ConfigureAwait(false);
        if (resultingEffect is not null) await WriteEffectAsync(connection, transaction, resultingEffect, cancellationToken).ConfigureAwait(false);
        await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (!await ActivationRowCapacityAllowsAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        byte[] instanceReceiptAuthority = yieldReceipt is null
            ? await WriteInstanceReceiptAsync(connection, transaction, request.Identity, receiptKind, row,
                request.AcceptedTime.CapturedUtc, transitionResult,
                HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult, cancellationToken).ConfigureAwait(false)
            : await WriteInstanceReceiptAsync(connection, transaction, request.Identity, receiptKind, row,
                request.AcceptedTime.CapturedUtc, yieldReceipt,
                HPDBaseJsonSerializerContext.Default.BaseActivationYieldReceipt, cancellationToken).ConfigureAwait(false);
        if (terminalTransition)
        {
            await using SqliteCommand terminalReceipt = connection.CreateCommand();
            terminalReceipt.Transaction = transaction;
            terminalReceipt.CommandText = $"UPDATE {_names.Activations} SET terminal_receipt_checksum=$receipt WHERE activation_id=$id AND generation=$generation;";
            terminalReceipt.Parameters.Add("$receipt", SqliteType.Blob).Value = instanceReceiptAuthority;
            terminalReceipt.Parameters.AddWithValue("$id", row.ActivationId);
            terminalReceipt.Parameters.AddWithValue("$generation", generation);
            if (await terminalReceipt.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.providerContractInvalid", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(transitionResult);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationMigrationCandidate>> ReadMigrationCandidateAsync(
        BaseActivationMigrationCandidateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedGeneration < 1 || request.SourceDefinition.Checksum.Length != 32
            || !ActivationLimitsValid(request.Limits) || !await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationMigrationCandidate>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        SqliteActivationRow? row = await ReadActivationAsync(connection, transaction, request.ActivationId, cancellationToken).ConfigureAwait(false);
        if (row is null || row.Generation != request.ExpectedGeneration || !SqliteMigrationState(row.State) || row.MaximumYields > 0
            || row.DefinitionId != request.SourceDefinition.Id || row.DefinitionVersion != request.SourceDefinition.Version
            || !CryptographicOperations.FixedTimeEquals(row.DefinitionChecksum, request.SourceDefinition.Checksum.AsSpan())
            || row.ScopeKind != request.Scope.Kind
            || !CryptographicOperations.FixedTimeEquals(
                ActivationHash($"base.activation.scope.v2\0{(int)row.ScopeKind}\n{row.ScopeValue}"), request.Scope.ProtectedIndexDigest.AsSpan()))
            return ActivationFailure<BaseActivationMigrationCandidate>("base.activation.migrationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        long bytes = row.CanonicalInput.LongLength + row.InputChecksum.LongLength + row.ControlChecksum.LongLength;
        if (bytes > request.Limits.MaximumEvidenceBytes || bytes > request.Limits.MaximumTransientBytes)
            return ActivationFailure<BaseActivationMigrationCandidate>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        return OperationResults.Ok(new BaseActivationMigrationCandidate
        {
            ActivationId = row.ActivationId,
            SourceDefinition = new BaseActivationDefinitionKey
            {
                Id = row.DefinitionId, Version = row.DefinitionVersion,
                Checksum = row.DefinitionChecksum.ToImmutableArray(),
            },
            Generation = row.Generation, State = row.State,
            EffectiveDueAt = row.EffectiveDueAt, YieldCount = row.YieldCount,
            MaximumYields = row.MaximumYields, ExecutionSliceOrdinal = row.ExecutionSliceOrdinal,
            AttemptStartedAt = row.AttemptStartedAt, SliceStartedAt = row.SliceStartedAt,
            TerminalYieldDisposition = row.YieldTerminalDisposition,
            TerminalYieldFailureCode = row.YieldTerminalFailureCode,
            CanonicalInput = row.CanonicalInput.ToImmutableArray(), InputChecksum = row.InputChecksum.ToImmutableArray(),
            ControlChecksum = row.ControlChecksum.ToImmutableArray(),
            Accounting = ActivationAccounting(1, bytes) with { Comparisons = 4 },
        });
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationMigrationResult>> MigrateAsync(
        BaseActivationMigrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedSourceGeneration < 1 || request.ExpectedSourceInputChecksum.Length != 32
            || string.IsNullOrWhiteSpace(request.ReplacementActivationId) || request.MigrationVersion < 1
            || request.MigrationChecksum.Length != 32 || request.Replacement.InputChecksum.Length != 32
            || !ActivationLimitsValid(request.Limits) || !await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationMigrationResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool found, OperationResult<BaseActivationMigrationResult> receipt) = await ReadControlReceiptAsync(
            connection, transaction, request.Identity, "activation-migrated", HPDBaseJsonSerializerContext.Default.BaseActivationMigrationResult,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, cancellationToken).ConfigureAwait(false);
        if (found) return receipt;
        SqliteActivationRow? source = await ReadActivationAsync(connection, transaction, request.SourceActivationId, cancellationToken).ConfigureAwait(false);
        byte[] replacementScope = ActivationHash($"base.activation.scope.v2\0{(int)request.Replacement.Scope.Kind}\n{request.Replacement.Scope.Value ?? string.Empty}");
        if (source is null || source.Generation != request.ExpectedSourceGeneration || !SqliteMigrationState(source.State) || source.MaximumYields > 0
            || source.DefinitionId != request.SourceDefinition.Id || source.DefinitionVersion != request.SourceDefinition.Version
            || !CryptographicOperations.FixedTimeEquals(source.DefinitionChecksum, request.SourceDefinition.Checksum.AsSpan())
            || source.ScopeKind != request.Scope.Kind
            || !CryptographicOperations.FixedTimeEquals(ActivationHash($"base.activation.scope.v2\0{(int)source.ScopeKind}\n{source.ScopeValue}"), request.Scope.ProtectedIndexDigest.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(source.InputChecksum, request.ExpectedSourceInputChecksum.AsSpan())
            || request.Replacement.Scope.Kind != request.Scope.Kind
            || !CryptographicOperations.FixedTimeEquals(replacementScope, request.Scope.ProtectedIndexDigest.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(request.Replacement.CanonicalInput.AsSpan()), request.Replacement.InputChecksum.AsSpan()))
            return ActivationFailure<BaseActivationMigrationResult>("base.activation.migrationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);

        long sourceGeneration = checked(source.Generation + 1);
        byte[] sourceControl = ActivationControlChecksum(source.ActivationId, sourceGeneration,
            BaseActivationState.Migrated, source.EffectiveDueAt, source.YieldCount,
            source.MaximumYields, source.ExecutionSliceOrdinal, source.AttemptStartedAt,
            source.SliceStartedAt, null, null);
        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {_names.Activations} SET state=$state,generation=$generation,claim_fence=NULL,claim_worker=NULL,lease_revision=NULL,lease_expires_at=NULL,eligible=0,control_checksum=$control WHERE activation_id=$id AND generation=$expected;";
            update.Parameters.AddWithValue("$state", (int)BaseActivationState.Migrated); update.Parameters.AddWithValue("$generation", sourceGeneration);
            update.Parameters.Add("$control", SqliteType.Blob).Value = sourceControl; update.Parameters.AddWithValue("$id", source.ActivationId);
            update.Parameters.AddWithValue("$expected", source.Generation);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return ActivationFailure<BaseActivationMigrationResult>("base.activation.migrationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        }
        byte[] replacementControl = ActivationControlChecksum(request.ReplacementActivationId, 1,
            BaseActivationState.Pending, request.Replacement.EffectiveDueAt ?? request.Replacement.RequestedDueAt,
            0, request.Replacement.MaximumYields, 0, null, null, null, null);
        byte[] fingerprint = ActivationHash($"base.activation.migration.create.v1\0{request.MigrationId}\n{request.MigrationVersion}\n{Convert.ToHexString(request.MigrationChecksum.AsSpan())}\n{request.SourceActivationId}\n{request.ReplacementActivationId}\n{Convert.ToHexString(request.Replacement.InputChecksum.AsSpan())}");
        if (!await TryReserveYieldReceiptSlotsAsync(connection, transaction, request.Replacement.MaximumYields, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationMigrationResult>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {_names.Activations}(activation_id,definition_id,definition_version,definition_checksum,receipt_format_version,receipt_duplicate_lifetime_ms,receipt_backup_coverage,canonical_input,input_checksum,scope_kind,scope_value,scope_digest,payload_checksum,fingerprint,state,generation,requested_due_at,effective_due_at,occurrence_id,priority,overlap_key,overlap_policy,eligible,control_checksum,maximum_yields) VALUES($id,$definition,$version,$definition_checksum,$receipt_format,$receipt_lifetime,$receipt_backup,$input,$input_checksum,$scope_kind,$scope_value,$scope_digest,$payload_checksum,$fingerprint,$state,1,$requested,$effective,$occurrence,$priority,$overlap_key,$overlap_policy,$eligible,$control,$maximum_yields);";
            insert.Parameters.AddWithValue("$id", request.ReplacementActivationId); insert.Parameters.AddWithValue("$definition", request.Replacement.Definition.Id); insert.Parameters.AddWithValue("$version", request.Replacement.Definition.Version);
            insert.Parameters.Add("$definition_checksum", SqliteType.Blob).Value = request.Replacement.Definition.Checksum.ToArray(); insert.Parameters.Add("$input", SqliteType.Blob).Value = request.Replacement.CanonicalInput.ToArray(); insert.Parameters.Add("$input_checksum", SqliteType.Blob).Value = request.Replacement.InputChecksum.ToArray();
            insert.Parameters.AddWithValue("$receipt_format", request.Replacement.ReceiptRetention.FormatVersion);
            insert.Parameters.AddWithValue("$receipt_lifetime", request.Replacement.ReceiptRetention.DuplicateResolutionLifetime.Ticks / TimeSpan.TicksPerMillisecond);
            insert.Parameters.AddWithValue("$receipt_backup", (int)request.Replacement.ReceiptRetention.ProtectedBackupCoverage);
            insert.Parameters.AddWithValue("$scope_kind", (int)request.Replacement.Scope.Kind); insert.Parameters.AddWithValue("$scope_value", request.Replacement.Scope.Value ?? string.Empty); insert.Parameters.Add("$scope_digest", SqliteType.Blob).Value = replacementScope;
            insert.Parameters.Add("$payload_checksum", SqliteType.Blob).Value = SHA256.HashData(request.Replacement.CanonicalInput.AsSpan()); insert.Parameters.Add("$fingerprint", SqliteType.Blob).Value = fingerprint; insert.Parameters.AddWithValue("$state", (int)BaseActivationState.Pending);
            insert.Parameters.AddWithValue("$requested", request.Replacement.RequestedDueAt); insert.Parameters.AddWithValue("$effective", request.Replacement.EffectiveDueAt ?? request.Replacement.RequestedDueAt);
            insert.Parameters.AddWithValue("$occurrence", (object?)request.Replacement.OccurrenceId ?? DBNull.Value); insert.Parameters.AddWithValue("$priority", request.Replacement.Priority);
            insert.Parameters.Add("$overlap_key", SqliteType.Blob).Value = request.Replacement.OverlapKey.IsDefaultOrEmpty ? DBNull.Value : request.Replacement.OverlapKey.ToArray(); insert.Parameters.AddWithValue("$overlap_policy", (int)request.Replacement.OverlapPolicy);
            insert.Parameters.AddWithValue("$eligible", request.Replacement.InitiallyEligible ? 1 : 0); insert.Parameters.Add("$control", SqliteType.Blob).Value = replacementControl;
            insert.Parameters.AddWithValue("$maximum_yields", request.Replacement.MaximumYields);
            try { await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            { return ActivationFailure<BaseActivationMigrationResult>("base.activation.migrationConflict", OperationStatus.Conflict, ErrorCategory.Conflict); }
        }
        await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var result = new BaseActivationMigrationResult
        {
            SourceActivationId = source.ActivationId, SourceGeneration = sourceGeneration,
            SourceDefinition = new BaseActivationDefinitionKey
            {
                Id = source.DefinitionId,
                Version = source.DefinitionVersion,
                Checksum = source.DefinitionChecksum.ToImmutableArray(),
            },
            SourceControlChecksum = sourceControl.ToImmutableArray(), ReplacementActivationId = request.ReplacementActivationId,
            ReplacementDefinition = request.Replacement.Definition with
            { Checksum = request.Replacement.Definition.Checksum.ToArray().ToImmutableArray() },
            ReplacementGeneration = 1, ReplacementControlChecksum = replacementControl.ToImmutableArray(),
            MigrationId = new string(request.MigrationId.AsSpan()),
            MigrationVersion = request.MigrationVersion,
            MigrationChecksum = request.MigrationChecksum.ToArray().ToImmutableArray(),
            Accounting = ActivationAccounting(1, 64) with { Comparisons = 8, IndexOperations = 2 },
            Disposition = BaseMutationRequestDisposition.Committed,
        };
        if (!await ActivationRowCapacityAllowsAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationMigrationResult>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        byte[] terminalReceiptChecksum = await WriteControlReceiptAsync(connection, transaction, request.Identity, "activation-migrated", result,
            HPDBaseJsonSerializerContext.Default.BaseActivationMigrationResult, cancellationToken).ConfigureAwait(false);
        await using (SqliteCommand bindReceipt = connection.CreateCommand())
        {
            bindReceipt.Transaction = transaction;
            bindReceipt.CommandText = $"UPDATE {_names.Activations} SET terminal_receipt_checksum=$receipt WHERE activation_id=$id AND generation=$generation AND state=$state;";
            bindReceipt.Parameters.Add("$receipt", SqliteType.Blob).Value = terminalReceiptChecksum;
            bindReceipt.Parameters.AddWithValue("$id", source.ActivationId);
            bindReceipt.Parameters.AddWithValue("$generation", sourceGeneration);
            bindReceipt.Parameters.AddWithValue("$state", (int)BaseActivationState.Migrated);
            if (await bindReceipt.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return ActivationFailure<BaseActivationMigrationResult>("base.activation.migrationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorRegistrationResult>> RegisterExecutorAsync(
        BaseExecutorRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (string.IsNullOrWhiteSpace(request.ApplicationId) || string.IsNullOrWhiteSpace(request.HostId) ||
            string.IsNullOrWhiteSpace(request.ProcessIncarnationId) || request.WorkerDefinitionSetChecksum.Length != 32 ||
            request.RequestedHeartbeatMilliseconds <= 0 || request.AcceptedTime.ApplicationId != request.ApplicationId)
            return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool found, OperationResult<BaseExecutorRegistrationResult> receipt) = await ReadControlReceiptAsync(
            connection, transaction, request.Identity, "executor-registered", HPDBaseJsonSerializerContext.Default.BaseExecutorRegistrationResult,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, cancellationToken).ConfigureAwait(false);
        if (found) return receipt;
        SqliteExecutorRow? existing = await ReadExecutorAsync(connection, transaction, request.ApplicationId, request.HostId, request.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
        if (existing is { Retired: false })
            return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.executorConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        long generation;
        await using (SqliteCommand maximum = connection.CreateCommand())
        {
            maximum.Transaction = transaction;
            maximum.CommandText = $"SELECT COALESCE(MAX(executor_generation),0)+1 FROM {_names.Executors};";
            generation = Convert.ToInt64(await maximum.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }
        (_, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        byte[] authorityChecksum = ActivationHash($"base.activation.executor.v2\0{request.ApplicationId}\n{request.HostId}\n{request.ProcessIncarnationId}\n{generation}\n{CurrentStoreInstanceId}\n{restoreEpoch}\n{Convert.ToHexString(request.WorkerDefinitionSetChecksum.AsSpan())}");
        var authority = new BaseExecutorIncarnationAuthority
        {
            ApplicationId = request.ApplicationId, HostId = request.HostId, ProcessIncarnationId = request.ProcessIncarnationId,
            ExecutorGeneration = generation, StoreInstanceId = CurrentStoreInstanceId, RestoreEpoch = restoreEpoch,
            WorkerDefinitionSetChecksum = request.WorkerDefinitionSetChecksum.ToArray().ToImmutableArray(), Checksum = authorityChecksum.ToImmutableArray(),
        };
        BaseExecutorHeartbeatObservation heartbeat = ExecutorHeartbeat(authority, 1, checked(request.AcceptedTime.CapturedUtc + request.RequestedHeartbeatMilliseconds));
        await WriteExecutorAsync(connection, transaction, authority, heartbeat, false, cancellationToken).ConfigureAwait(false);
        var result = new BaseExecutorRegistrationResult
        { Executor = authority, Heartbeat = heartbeat, Accounting = ActivationAccounting(1, 128), Disposition = BaseMutationRequestDisposition.Committed };
        await WriteControlReceiptAsync(connection, transaction, request.Identity, "executor-registered", result,
            HPDBaseJsonSerializerContext.Default.BaseExecutorRegistrationResult, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorHeartbeatResult>> HeartbeatExecutorAsync(
        BaseExecutorHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseExecutorHeartbeatResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool found, OperationResult<BaseExecutorHeartbeatResult> receipt) = await ReadControlReceiptAsync(
            connection, transaction, request.Identity, "executor-heartbeat", HPDBaseJsonSerializerContext.Default.BaseExecutorHeartbeatResult,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, cancellationToken).ConfigureAwait(false);
        if (found) return receipt;
        SqliteExecutorRow? row = await ReadExecutorAsync(connection, transaction, request.Executor.ApplicationId, request.Executor.HostId, request.Executor.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
        if (row is null || row.Retired || !SqliteExecutorMatches(row.Authority, request.Executor) ||
            row.Heartbeat.HeartbeatRevision != request.ExpectedHeartbeatRevision || row.Heartbeat.HeartbeatExpiresAt < request.AcceptedTime.CapturedUtc)
            return ActivationFailure<BaseExecutorHeartbeatResult>("base.activation.executorLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        BaseExecutorHeartbeatObservation heartbeat = ExecutorHeartbeat(row.Authority, checked(row.Heartbeat.HeartbeatRevision + 1),
            checked(request.AcceptedTime.CapturedUtc + request.ExtensionMilliseconds));
        await WriteExecutorAsync(connection, transaction, row.Authority, heartbeat, false, cancellationToken).ConfigureAwait(false);
        var result = new BaseExecutorHeartbeatResult
        { Executor = row.Authority, Heartbeat = heartbeat, Accounting = ActivationAccounting(1, 128), Disposition = BaseMutationRequestDisposition.Committed };
        await WriteControlReceiptAsync(connection, transaction, request.Identity, "executor-heartbeat", result,
            HPDBaseJsonSerializerContext.Default.BaseExecutorHeartbeatResult, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorRetirementResult>> RetireExecutorAsync(
        BaseExecutorRetirementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseExecutorRetirementResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool found, OperationResult<BaseExecutorRetirementResult> receipt) = await ReadControlReceiptAsync(
            connection, transaction, request.Identity, "executor-retired", HPDBaseJsonSerializerContext.Default.BaseExecutorRetirementResult,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, cancellationToken).ConfigureAwait(false);
        if (found) return receipt;
        SqliteExecutorRow? row = await ReadExecutorAsync(connection, transaction, request.Executor.ApplicationId, request.Executor.HostId, request.Executor.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
        if (row is null || row.Retired || !SqliteExecutorMatches(row.Authority, request.Executor) || row.Heartbeat.HeartbeatRevision != request.ExpectedHeartbeatRevision)
            return ActivationFailure<BaseExecutorRetirementResult>("base.activation.executorLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        await WriteExecutorAsync(connection, transaction, row.Authority, row.Heartbeat, true, cancellationToken).ConfigureAwait(false);
        byte[] checksum = ActivationHash($"base.activation.executor.retired.v2\0{Convert.ToHexString(row.Authority.Checksum.AsSpan())}\n{row.Heartbeat.HeartbeatRevision}");
        var result = new BaseExecutorRetirementResult
        {
            Executor = row.Authority, HeartbeatRevision = row.Heartbeat.HeartbeatRevision, RetirementChecksum = checksum.ToImmutableArray(),
            Accounting = ActivationAccounting(1, 128), Disposition = BaseMutationRequestDisposition.Committed,
        };
        await WriteControlReceiptAsync(connection, transaction, request.Identity, "executor-retired", result,
            HPDBaseJsonSerializerContext.Default.BaseExecutorRetirementResult, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleAuthority>> ReadScheduleAsync(
        string scheduleId, int scheduleVersion, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        BaseScheduleAuthority? authority = await ReadScheduleCoreAsync(connection, null, scheduleId, scheduleVersion, cancellationToken).ConfigureAwait(false);
        return authority is null
            ? ActivationFailure<BaseScheduleAuthority>("base.activation.scheduleNotFound", OperationStatus.NotFound, ErrorCategory.NotFound)
            : OperationResults.Ok(authority);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleMutationResult>> MutateScheduleAsync(
        BaseScheduleMutationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseScheduleMutationResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        BaseScheduleDefinition definition;
        try { definition = BaseScheduleDefinitionBuilder.Create(request.Definition); }
        catch { return ActivationFailure<BaseScheduleMutationResult>("base.activation.scheduleInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation); }
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool found, OperationResult<BaseScheduleMutationResult> receipt) = await ReadControlReceiptAsync(
            connection, transaction, request.Identity, "schedule-mutated", HPDBaseJsonSerializerContext.Default.BaseScheduleMutationResult,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, cancellationToken).ConfigureAwait(false);
        if (found) return receipt;
        BaseScheduleAuthority? existing = await ReadScheduleCoreAsync(connection, transaction, definition.Id, definition.Version, cancellationToken).ConfigureAwait(false);
        if (request.Kind == BaseScheduleMutationKind.Create && existing is not null ||
            request.Kind != BaseScheduleMutationKind.Create && (existing is null || existing.DefinitionGeneration != request.ExpectedDefinitionGeneration))
            return ActivationFailure<BaseScheduleMutationResult>("base.activation.scheduleConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        if (request.Kind == BaseScheduleMutationKind.Remove)
        {
            await using SqliteCommand remove = connection.CreateCommand(); remove.Transaction = transaction;
            remove.CommandText = $"DELETE FROM {_names.ActivationSchedules} WHERE schedule_id=$id AND schedule_version=$version;";
            remove.Parameters.AddWithValue("$id", definition.Id); remove.Parameters.AddWithValue("$version", definition.Version);
            await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var removed = new BaseScheduleMutationResult { Authority = null, Accounting = ActivationAccounting(1, 64), Disposition = BaseMutationRequestDisposition.Committed };
            await WriteControlReceiptAsync(connection, transaction, request.Identity, "schedule-mutated", removed,
                HPDBaseJsonSerializerContext.Default.BaseScheduleMutationResult, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok(removed);
        }
        long generation = existing is null ? 1 : checked(existing.DefinitionGeneration + 1);
        long epoch = existing is null ? 1 : request.Kind == BaseScheduleMutationKind.Update ? checked(existing.ScheduleEpoch + 1) : existing.ScheduleEpoch;
        bool enabled = request.Kind switch { BaseScheduleMutationKind.Disable => false, BaseScheduleMutationKind.Enable => true, _ => existing?.Enabled ?? true };
        long? last = request.Kind == BaseScheduleMutationKind.Update ? null : existing?.LastConsideredNominal;
        long? following = request.Kind == BaseScheduleMutationKind.Update || existing is null ? request.InitialNextNominal : existing.NextNominal;
        BaseScheduleAuthority authority = SqliteScheduleAuthority(definition, generation, enabled, epoch, last, following);
        await WriteScheduleAsync(connection, transaction, authority, cancellationToken).ConfigureAwait(false);
        var result = new BaseScheduleMutationResult { Authority = authority, Accounting = ActivationAccounting(1, 128), Disposition = BaseMutationRequestDisposition.Committed };
        await WriteControlReceiptAsync(connection, transaction, request.Identity, "schedule-mutated", result,
            HPDBaseJsonSerializerContext.Default.BaseScheduleMutationResult, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleMaintenancePage>> AdvanceSchedulesAsync(
        BaseScheduleMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (request.Occurrences.Length is < 1 or > 256)
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool found, OperationResult<BaseScheduleMaintenancePage> receipt) = await ReadControlReceiptAsync(
            connection, transaction, request.Identity, "occurrence-page", HPDBaseJsonSerializerContext.Default.BaseScheduleMaintenancePage,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, cancellationToken).ConfigureAwait(false);
        if (found) return receipt;
        BaseScheduleAuthority? authority = await ReadScheduleCoreAsync(connection, transaction, request.ScheduleId, request.ScheduleVersion, cancellationToken).ConfigureAwait(false);
        if (authority is null || !authority.Enabled || !CryptographicOperations.FixedTimeEquals(authority.Checksum.AsSpan(), request.ExpectedAuthorityChecksum.AsSpan()))
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.scheduleConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        long previous = authority.LastConsideredNominal ?? -1;
        var committedFacts = ImmutableArray.CreateBuilder<BaseScheduleOccurrenceFact>(request.Occurrences.Length);
        var cancellations = ImmutableArray.CreateBuilder<BaseScheduleCancellationAuthority>();
        foreach (BaseScheduleOccurrenceProposal proposal in request.Occurrences)
        {
            OperationResult<BaseScheduleOccurrenceProposal> overlap = await ResolveSqliteOverlapAsync(
                connection, transaction, proposal, cancellationToken).ConfigureAwait(false);
            if (!overlap.IsSuccess() || overlap.Value is null)
                return new OperationResult<BaseScheduleMaintenancePage> { Status = overlap.Status, Error = overlap.Error };
            BaseScheduleOccurrenceProposal effectiveProposal = overlap.Value;
            BaseScheduleOccurrenceFact fact = effectiveProposal.Fact;
            if (fact.ScheduleId != authority.Definition.Id || fact.ScheduleEpoch != authority.ScheduleEpoch || fact.NominalAt <= previous ||
                !SqliteOccurrenceShapeValid(effectiveProposal) || !CryptographicOperations.FixedTimeEquals(fact.Checksum.AsSpan(), SqliteOccurrenceChecksum(fact)))
                return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.occurrenceInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            previous = fact.NominalAt;
            await using (SqliteCommand occurrence = connection.CreateCommand())
            {
                occurrence.Transaction = transaction;
                occurrence.CommandText = $"INSERT INTO {_names.ActivationOccurrences}(occurrence_id,schedule_id,schedule_version,schedule_epoch,nominal_at,effective_at,overlap_ordinal,fact_json,fact_checksum) VALUES($occurrence,$schedule,$version,$epoch,$nominal,$effective,$ordinal,$json,$checksum);";
                occurrence.Parameters.AddWithValue("$occurrence", fact.OccurrenceId); occurrence.Parameters.AddWithValue("$schedule", fact.ScheduleId); occurrence.Parameters.AddWithValue("$version", request.ScheduleVersion);
                occurrence.Parameters.AddWithValue("$epoch", fact.ScheduleEpoch); occurrence.Parameters.AddWithValue("$nominal", fact.NominalAt); occurrence.Parameters.AddWithValue("$effective", fact.EffectiveAt); occurrence.Parameters.AddWithValue("$ordinal", fact.OverlapOrdinal);
                occurrence.Parameters.Add("$json", SqliteType.Blob).Value = JsonSerializer.SerializeToUtf8Bytes(fact, HPDBaseJsonSerializerContext.Default.BaseScheduleOccurrenceFact);
                occurrence.Parameters.Add("$checksum", SqliteType.Blob).Value = fact.Checksum.ToArray();
                try { await occurrence.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                { return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.occurrenceConflict", OperationStatus.Conflict, ErrorCategory.Conflict); }
            }
            committedFacts.Add(fact);
            if (effectiveProposal.Activation is { } activation)
            {
                string activationId = ((BaseOccurrenceMaterialized)fact.Disposition).ActivationId;
                List<(string Id, long Generation, long DueAt)> cancellationBlockers = activation.OverlapPolicy == BaseScheduleOverlapPolicy.CancelPrevious
                    ? await ReadSqliteOverlapRowsAsync(connection, transaction, activation.OverlapKey, 1_000_001, cancellationToken).ConfigureAwait(false)
                    : [];
                if (cancellationBlockers.Count > 1_000_000)
                    return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                byte[] fingerprint = SqliteScheduleActivationFingerprint(activation, fact.OccurrenceId);
                if (!await TryReserveYieldReceiptSlotsAsync(connection, transaction, activation.MaximumYields, cancellationToken).ConfigureAwait(false))
                    return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
                await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction = transaction;
                insert.CommandText = $"INSERT INTO {_names.Activations}(activation_id,definition_id,definition_version,definition_checksum,receipt_format_version,receipt_duplicate_lifetime_ms,receipt_backup_coverage,canonical_input,input_checksum,scope_kind,scope_value,scope_digest,payload_checksum,fingerprint,state,generation,requested_due_at,effective_due_at,occurrence_id,priority,overlap_key,overlap_policy,eligible,control_checksum,maximum_yields) VALUES($id,$definition,$version,$definition_checksum,$receipt_format,$receipt_lifetime,$receipt_backup,$input,$input_checksum,$scope_kind,$scope_value,$scope_digest,$payload_checksum,$fingerprint,$state,1,$requested,$effective,$occurrence,$priority,$overlap_key,$overlap_policy,$eligible,$control,$maximum_yields);";
                insert.Parameters.AddWithValue("$id", activationId); insert.Parameters.AddWithValue("$definition", activation.Definition.Id); insert.Parameters.AddWithValue("$version", activation.Definition.Version);
                insert.Parameters.Add("$definition_checksum", SqliteType.Blob).Value = activation.Definition.Checksum.ToArray(); insert.Parameters.Add("$input", SqliteType.Blob).Value = activation.CanonicalInput.ToArray(); insert.Parameters.Add("$input_checksum", SqliteType.Blob).Value = activation.InputChecksum.ToArray();
                insert.Parameters.AddWithValue("$receipt_format", activation.ReceiptRetention.FormatVersion);
                insert.Parameters.AddWithValue("$receipt_lifetime", activation.ReceiptRetention.DuplicateResolutionLifetime.Ticks / TimeSpan.TicksPerMillisecond);
                insert.Parameters.AddWithValue("$receipt_backup", (int)activation.ReceiptRetention.ProtectedBackupCoverage);
                insert.Parameters.AddWithValue("$scope_kind", (int)activation.Scope.Kind); insert.Parameters.AddWithValue("$scope_value", activation.Scope.Value ?? string.Empty); insert.Parameters.Add("$scope_digest", SqliteType.Blob).Value = ActivationHash($"base.activation.scope.v2\0{(int)activation.Scope.Kind}\n{activation.Scope.Value ?? string.Empty}");
                insert.Parameters.Add("$payload_checksum", SqliteType.Blob).Value = SHA256.HashData(activation.CanonicalInput.AsSpan()); insert.Parameters.Add("$fingerprint", SqliteType.Blob).Value = fingerprint; insert.Parameters.AddWithValue("$state", (int)BaseActivationState.Pending);
                insert.Parameters.AddWithValue("$requested", activation.RequestedDueAt); insert.Parameters.AddWithValue("$effective", activation.EffectiveDueAt ?? activation.RequestedDueAt);
                insert.Parameters.AddWithValue("$occurrence", (object?)activation.OccurrenceId ?? DBNull.Value); insert.Parameters.AddWithValue("$priority", activation.Priority);
                insert.Parameters.Add("$overlap_key", SqliteType.Blob).Value = activation.OverlapKey.IsDefaultOrEmpty ? DBNull.Value : activation.OverlapKey.ToArray();
                insert.Parameters.AddWithValue("$overlap_policy", (int)activation.OverlapPolicy);
                insert.Parameters.AddWithValue("$eligible", cancellationBlockers.Count == 0 &&
                    (activation.OverlapPolicy == BaseScheduleOverlapPolicy.CancelPrevious || activation.InitiallyEligible) ? 1 : 0);
                insert.Parameters.Add("$control", SqliteType.Blob).Value = ActivationControlChecksum(
                    activationId, 1, BaseActivationState.Pending,
                    activation.EffectiveDueAt ?? activation.RequestedDueAt, 0,
                    activation.MaximumYields, 0, null, null, null, null);
                insert.Parameters.AddWithValue("$maximum_yields", activation.MaximumYields);
                try { await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                { return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict); }
                if (cancellationBlockers.Count != 0)
                {
                    (string highId, _, long highDue) = cancellationBlockers[^1];
                    string maintenanceId = Convert.ToHexStringLower(ActivationHash(
                        $"base.activation.schedule.cancelPrevious.v2\0{fact.OccurrenceId}\n{activationId}"));
                    await using SqliteCommand maintenance = connection.CreateCommand(); maintenance.Transaction = transaction;
                    maintenance.CommandText = $"INSERT INTO {_names.ActivationScheduleCancellations}(maintenance_id,replacement_activation_id,overlap_key,high_due_at,high_activation_id,after_due_at,after_activation_id,completed) VALUES($id,$replacement,$key,$due,$high,NULL,NULL,0);";
                    maintenance.Parameters.AddWithValue("$id", maintenanceId); maintenance.Parameters.AddWithValue("$replacement", activationId);
                    maintenance.Parameters.Add("$key", SqliteType.Blob).Value = activation.OverlapKey.ToArray();
                    maintenance.Parameters.AddWithValue("$due", highDue); maintenance.Parameters.AddWithValue("$high", highId);
                    await maintenance.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    cancellations.Add(new BaseScheduleCancellationAuthority
                    {
                        MaintenanceId = maintenanceId, ReplacementActivationId = activationId,
                        OverlapKey = activation.OverlapKey.ToArray().ToImmutableArray(),
                        HighWater = new BaseScheduleCancellationBoundary { EffectiveDueAt = highDue, ActivationId = highId },
                    });
                }
            }
        }
        if (previous != request.ResultingLastConsideredNominal)
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.occurrenceInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        BaseScheduleAuthority replacement = SqliteScheduleAuthority(authority.Definition, authority.DefinitionGeneration, true, authority.ScheduleEpoch,
            request.ResultingLastConsideredNominal, request.ResultingNextNominal);
        await WriteScheduleAsync(connection, transaction, replacement, cancellationToken).ConfigureAwait(false);
        await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var result = new BaseScheduleMaintenancePage { Authority = replacement,
            Occurrences = committedFacts.MoveToImmutable(), Cancellations = cancellations.ToImmutable(), Accounting = ActivationAccounting(request.Occurrences.Length, request.Occurrences.Length * 128L),
            Disposition = BaseMutationRequestDisposition.Committed };
        if (!await ActivationRowCapacityAllowsAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        await WriteControlReceiptAsync(connection, transaction, request.Identity, "occurrence-page", result,
            HPDBaseJsonSerializerContext.Default.BaseScheduleMaintenancePage, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    private async ValueTask<OperationResult<BaseScheduleOccurrenceProposal>> ResolveSqliteOverlapAsync(
        SqliteConnection connection, SqliteTransaction transaction, BaseScheduleOccurrenceProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Activation is not { } activation || activation.OverlapKey.IsDefaultOrEmpty ||
            activation.OverlapPolicy is BaseScheduleOverlapPolicy.Allow or BaseScheduleOverlapPolicy.Queue)
            return OperationResults.Ok(proposal);
        List<(string Id, long Generation, long DueAt)> blockers = await ReadSqliteOverlapRowsAsync(
            connection, transaction, activation.OverlapKey, 1, cancellationToken).ConfigureAwait(false);
        if (activation.OverlapPolicy == BaseScheduleOverlapPolicy.SkipWhileActive && blockers.Count != 0)
        {
            BaseScheduleOccurrenceFact skipped = proposal.Fact with
            { Disposition = new BaseOccurrenceSkippedOverlap(blockers[0].Id), Checksum = [] };
            skipped = skipped with { Checksum = SqliteOccurrenceChecksum(skipped).ToImmutableArray() };
            return OperationResults.Ok(new BaseScheduleOccurrenceProposal { Fact = skipped });
        }
        return OperationResults.Ok(proposal);
    }

    private async ValueTask<List<(string Id, long Generation, long DueAt)>> ReadSqliteOverlapRowsAsync(
        SqliteConnection connection, SqliteTransaction transaction, ImmutableArray<byte> overlapKey, int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<(string, long, long)>();
        await using SqliteCommand read = connection.CreateCommand(); read.Transaction = transaction;
        read.CommandText = $"SELECT activation_id,generation,effective_due_at FROM {_names.Activations} WHERE overlap_key=$key AND state IN ($pending,$retry,$yield,$claimed,$effect) ORDER BY effective_due_at,activation_id LIMIT $limit;";
        read.Parameters.Add("$key", SqliteType.Blob).Value = overlapKey.ToArray(); read.Parameters.AddWithValue("$limit", limit);
        read.Parameters.AddWithValue("$pending", (int)BaseActivationState.Pending); read.Parameters.AddWithValue("$retry", (int)BaseActivationState.RetryPending);
        read.Parameters.AddWithValue("$yield", (int)BaseActivationState.YieldPending);
        read.Parameters.AddWithValue("$claimed", (int)BaseActivationState.Claimed); read.Parameters.AddWithValue("$effect", (int)BaseActivationState.EffectStarted);
        await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        return rows;
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleCancellationMaintenancePage>> AdvanceScheduleCancellationAsync(
        BaseScheduleCancellationMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false) ||
            request.OverlapKey.Length != 32 || request.Limits.MaximumCandidates is < 1 or > 256)
            return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool found, OperationResult<BaseScheduleCancellationMaintenancePage> receipt) = await ReadControlReceiptAsync(
            connection, transaction, request.Identity, "cancellation-maintenance", HPDBaseJsonSerializerContext.Default.BaseScheduleCancellationMaintenancePage,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, cancellationToken).ConfigureAwait(false);
        if (found) return receipt;
        string replacement; byte[] key; long highDue; string highId; long? afterDue; string? afterId; bool completed;
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"SELECT replacement_activation_id,overlap_key,high_due_at,high_activation_id,after_due_at,after_activation_id,completed FROM {_names.ActivationScheduleCancellations} WHERE maintenance_id=$id;";
            read.Parameters.AddWithValue("$id", request.MaintenanceId);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            replacement = reader.GetString(0); key = (byte[])reader[1]; highDue = reader.GetInt64(2); highId = reader.GetString(3);
            afterDue = reader.IsDBNull(4) ? null : reader.GetInt64(4); afterId = reader.IsDBNull(5) ? null : reader.GetString(5); completed = reader.GetInt64(6) != 0;
        }
        if (completed || replacement != request.ReplacementActivationId || !CryptographicOperations.FixedTimeEquals(key, request.OverlapKey.AsSpan()) ||
            highDue != request.HighWater.EffectiveDueAt || highId != request.HighWater.ActivationId ||
            afterDue != request.After?.EffectiveDueAt || afterId != request.After?.ActivationId)
            return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        var page = new List<(string Id, long Generation, long DueAt)>();
        await using (SqliteCommand candidates = connection.CreateCommand())
        {
            candidates.Transaction = transaction;
            candidates.CommandText = $"SELECT activation_id,generation,effective_due_at FROM {_names.Activations} WHERE overlap_key=$key AND activation_id<>$replacement AND state IN ($pending,$retry,$yield,$claimed,$effect) AND (($after_due IS NULL) OR effective_due_at>$after_due OR (effective_due_at=$after_due AND activation_id>$after_id)) AND (effective_due_at<$high_due OR (effective_due_at=$high_due AND activation_id<=$high_id)) ORDER BY effective_due_at,activation_id LIMIT $limit;";
            candidates.Parameters.Add("$key", SqliteType.Blob).Value = key; candidates.Parameters.AddWithValue("$replacement", replacement);
            candidates.Parameters.AddWithValue("$after_due", (object?)afterDue ?? DBNull.Value); candidates.Parameters.AddWithValue("$after_id", (object?)afterId ?? DBNull.Value);
            candidates.Parameters.AddWithValue("$high_due", highDue); candidates.Parameters.AddWithValue("$high_id", highId);
            candidates.Parameters.AddWithValue("$limit", Math.Min(256, request.Limits.MaximumCandidates));
            candidates.Parameters.AddWithValue("$pending", (int)BaseActivationState.Pending); candidates.Parameters.AddWithValue("$retry", (int)BaseActivationState.RetryPending);
            candidates.Parameters.AddWithValue("$yield", (int)BaseActivationState.YieldPending);
            candidates.Parameters.AddWithValue("$claimed", (int)BaseActivationState.Claimed); candidates.Parameters.AddWithValue("$effect", (int)BaseActivationState.EffectStarted);
            await using SqliteDataReader reader = await candidates.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) page.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        }
        foreach ((string id, long generation, _) in page)
        {
            SqliteActivationRow blocker = await ReadActivationAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("base.activation.providerContractInvalid");
            long next = checked(generation + 1);
            await using SqliteCommand cancel = connection.CreateCommand(); cancel.Transaction = transaction;
            cancel.CommandText = $"UPDATE {_names.Activations} SET state=$cancelled,generation=$next,claim_fence=NULL,claim_worker=NULL,lease_revision=NULL,lease_expires_at=NULL,eligible=0,control_checksum=$checksum WHERE activation_id=$id AND generation=$generation;";
            cancel.Parameters.AddWithValue("$cancelled", (int)BaseActivationState.Cancelled); cancel.Parameters.AddWithValue("$next", next);
            cancel.Parameters.Add("$checksum", SqliteType.Blob).Value = ActivationControlChecksum(
                id, next, BaseActivationState.Cancelled, blocker.EffectiveDueAt,
                blocker.YieldCount, blocker.MaximumYields, blocker.ExecutionSliceOrdinal,
                blocker.AttemptStartedAt, blocker.SliceStartedAt, null, null);
            cancel.Parameters.AddWithValue("$id", id); cancel.Parameters.AddWithValue("$generation", generation);
            if (await cancel.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            await ApplyYieldReceiptReservationTransitionAsync(
                connection, transaction, blocker, BaseActivationState.Cancelled, null, cancellationToken).ConfigureAwait(false);
        }
        long? nextDue = page.Count == 0 ? afterDue : page[^1].DueAt;
        string? nextId = page.Count == 0 ? afterId : page[^1].Id;
        bool hasMore;
        await using (SqliteCommand more = connection.CreateCommand())
        {
            more.Transaction = transaction;
            more.CommandText = $"SELECT 1 FROM {_names.Activations} WHERE overlap_key=$key AND activation_id<>$replacement AND state IN ($pending,$retry,$yield,$claimed,$effect) AND (($after_due IS NULL) OR effective_due_at>$after_due OR (effective_due_at=$after_due AND activation_id>$after_id)) AND (effective_due_at<$high_due OR (effective_due_at=$high_due AND activation_id<=$high_id)) LIMIT 1;";
            more.Parameters.Add("$key", SqliteType.Blob).Value = key; more.Parameters.AddWithValue("$replacement", replacement);
            more.Parameters.AddWithValue("$after_due", (object?)nextDue ?? DBNull.Value); more.Parameters.AddWithValue("$after_id", (object?)nextId ?? DBNull.Value);
            more.Parameters.AddWithValue("$high_due", highDue); more.Parameters.AddWithValue("$high_id", highId);
            more.Parameters.AddWithValue("$pending", (int)BaseActivationState.Pending); more.Parameters.AddWithValue("$retry", (int)BaseActivationState.RetryPending);
            more.Parameters.AddWithValue("$yield", (int)BaseActivationState.YieldPending);
            more.Parameters.AddWithValue("$claimed", (int)BaseActivationState.Claimed); more.Parameters.AddWithValue("$effect", (int)BaseActivationState.EffectStarted);
            hasMore = await more.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
        }
        if (!hasMore)
        {
            await using SqliteCommand publish = connection.CreateCommand(); publish.Transaction = transaction;
            publish.CommandText = $"UPDATE {_names.Activations} SET eligible=1 WHERE activation_id=$id AND state=$pending AND eligible=0;";
            publish.Parameters.AddWithValue("$id", replacement); publish.Parameters.AddWithValue("$pending", (int)BaseActivationState.Pending);
            if (await publish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        }
        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {_names.ActivationScheduleCancellations} SET after_due_at=$due,after_activation_id=$after,completed=$completed WHERE maintenance_id=$id AND completed=0;";
            update.Parameters.AddWithValue("$due", (object?)nextDue ?? DBNull.Value); update.Parameters.AddWithValue("$after", (object?)nextId ?? DBNull.Value);
            update.Parameters.AddWithValue("$completed", hasMore ? 0 : 1); update.Parameters.AddWithValue("$id", request.MaintenanceId);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        }
        await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var result = new BaseScheduleCancellationMaintenancePage
        {
            MaintenanceId = request.MaintenanceId, CancelledCount = page.Count,
            Next = hasMore ? new BaseScheduleCancellationBoundary { EffectiveDueAt = nextDue!.Value, ActivationId = nextId! } : null,
            Completed = !hasMore, Accounting = ActivationAccounting(page.Count, page.Count * 96L),
            Disposition = BaseMutationRequestDisposition.Committed,
        };
        if (!await ActivationRowCapacityAllowsAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        await WriteControlReceiptAsync(connection, transaction, request.Identity, "cancellation-maintenance", result,
            HPDBaseJsonSerializerContext.Default.BaseScheduleCancellationMaintenancePage, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationAdministrationPage>> ReadAdministrationAsync(
        BaseActivationAdministrationQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false)
            || !ActivationLimitsValid(request.Limits)
            || request.Take is < 1 or > 256 || !Enum.IsDefined(request.States)
            || request.Scope.ProtectedIndexDigest.Length != SHA256.HashSizeInBytes)
            return ActivationFailure<BaseActivationAdministrationPage>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        (long generation, _) = await ReadActivationAuthorityAsync(connection, null, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        string statePredicate = request.States switch
        {
            BaseActivationStateSelector.All => "1=1",
            BaseActivationStateSelector.Runnable => $"a.state IN ({(int)BaseActivationState.Pending},{(int)BaseActivationState.RetryPending},{(int)BaseActivationState.YieldPending})",
            BaseActivationStateSelector.Active => $"a.state IN ({(int)BaseActivationState.Claimed},{(int)BaseActivationState.EffectStarted})",
            BaseActivationStateSelector.Terminal => $"a.state IN ({(int)BaseActivationState.Succeeded},{(int)BaseActivationState.Exhausted},{(int)BaseActivationState.Cancelled},{(int)BaseActivationState.Disposed},{(int)BaseActivationState.Migrated})",
            BaseActivationStateSelector.OutcomeUnknown => $"a.state={(int)BaseActivationState.OutcomeUnknown}",
            _ => "0=1",
        };
        command.CommandText = $"SELECT a.activation_id,a.definition_id,a.definition_version,a.definition_checksum,a.state,a.generation,a.effective_due_at,a.occurrence_id,a.attempt_number,a.canonical_result IS NOT NULL,EXISTS(SELECT 1 FROM {_names.ActivationEffects} e WHERE e.activation_id=a.activation_id),a.control_checksum,a.execution_slice_ordinal,a.yield_count,a.maximum_yields,a.yield_terminal_disposition,a.yield_terminal_failure_code,a.attempt_started_at,a.slice_started_at FROM {_names.Activations} a INDEXED BY {_names.Prefix}activation_due_idx WHERE a.scope_kind=$scope_kind AND a.scope_digest=$scope_digest AND ($definition IS NULL OR (a.definition_id=$definition AND a.definition_version=$version AND a.definition_checksum=$checksum)) AND ({statePredicate}) AND ($after_definition IS NULL OR a.definition_id>$after_definition OR (a.definition_id=$after_definition AND (a.definition_version>$after_version OR (a.definition_version=$after_version AND (a.effective_due_at>$after_due OR (a.effective_due_at=$after_due AND a.activation_id>$after_id)))))) ORDER BY a.definition_id,a.definition_version,a.effective_due_at,a.activation_id LIMIT $take;";
        command.Parameters.AddWithValue("$scope_kind", (int)request.Scope.Kind);
        command.Parameters.Add("$scope_digest", SqliteType.Blob).Value = request.Scope.ProtectedIndexDigest.ToArray();
        command.Parameters.AddWithValue("$definition", (object?)request.Definition?.Id ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", (object?)request.Definition?.Version ?? DBNull.Value);
        command.Parameters.Add("$checksum", SqliteType.Blob).Value = (object?)request.Definition?.Checksum.ToArray() ?? DBNull.Value;
        command.Parameters.AddWithValue("$after_definition", (object?)request.After?.DefinitionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$after_version", (object?)request.After?.DefinitionVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$after_due", (object?)request.After?.EffectiveDueAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$after_id", (object?)request.After?.ActivationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$take", request.Take + 1);
        var items = new List<BaseActivationAdministrationItem>(request.Take + 1);
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new BaseActivationAdministrationItem
                {
                    ActivationId = reader.GetString(0),
                    Definition = new BaseActivationDefinitionKey
                    {
                        Id = reader.GetString(1), Version = reader.GetInt32(2),
                        Checksum = ((byte[])reader[3]).ToImmutableArray(),
                    },
                    State = (BaseActivationState)reader.GetInt32(4),
                    Generation = reader.GetInt64(5), EffectiveDueAt = reader.GetInt64(6),
                    OccurrenceId = reader.IsDBNull(7) ? null : reader.GetString(7),
                    AttemptNumber = reader.GetInt32(8), ResultRetained = reader.GetBoolean(9),
                    EffectAuthorityRetained = reader.GetBoolean(10),
                    ControlChecksum = ((byte[])reader[11]).ToImmutableArray(),
                    ExecutionSliceOrdinal = reader.GetInt64(12), YieldCount = reader.GetInt64(13),
                    MaximumYields = reader.GetInt64(14),
                    TerminalYieldDisposition = reader.IsDBNull(15) ? null : (BaseActivationYieldDisposition)reader.GetInt32(15),
                    TerminalYieldFailureCode = reader.IsDBNull(16) ? null : reader.GetString(16),
                    AttemptStartedAt = reader.IsDBNull(17) ? null : reader.GetInt64(17),
                    SliceStartedAt = reader.IsDBNull(18) ? null : reader.GetInt64(18),
                });
            }
        }
        bool hasMore = items.Count > request.Take;
        if (hasMore) items.RemoveAt(items.Count - 1);
        BaseActivationAdministrationBoundary? next = hasMore && items.Count != 0
            ? new BaseActivationAdministrationBoundary
            {
                DefinitionId = items[^1].Definition.Id,
                DefinitionVersion = items[^1].Definition.Version,
                EffectiveDueAt = items[^1].EffectiveDueAt,
                ActivationId = items[^1].ActivationId,
            } : null;
        BaseAtomicReadIntervalEvidence interval = ActivationAdministrationInterval(request, next);
        long evidenceBytes = checked(ActivationIntervalBytes(interval) + items.Sum(static item =>
            Encoding.UTF8.GetByteCount(item.ActivationId) + Encoding.UTF8.GetByteCount(item.Definition.Id)
            + item.Definition.Checksum.Length + item.ControlChecksum.Length
            + (item.TerminalYieldFailureCode is null ? 0 : Encoding.UTF8.GetByteCount(item.TerminalYieldFailureCode)) + 80L));
        if (items.Count > request.Limits.MaximumCandidates || evidenceBytes > request.Limits.MaximumEvidenceBytes
            || evidenceBytes > request.Limits.MaximumTransientBytes)
            return ActivationFailure<BaseActivationAdministrationPage>(
                "base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        return OperationResults.Ok(new BaseActivationAdministrationPage
        {
            Items = items.ToImmutableArray(), Next = next, CapturedIndexGeneration = generation,
            Intervals = [interval], Accounting = new BaseActivationAccounting
            {
                Candidates = items.Count, Comparisons = items.Count, IndexOperations = 0,
                ReadIntervals = 1, EvidenceBytes = evidenceBytes, TransientBytes = evidenceBytes,
            },
        });
    }

    private static BaseAtomicReadIntervalEvidence ActivationAdministrationInterval(
        BaseActivationAdministrationQueryRequest request,
        BaseActivationAdministrationBoundary? next) => new()
    {
        LogicalAccessPathId = "base.activation.administration.byScopeDefinitionStateDue.v1",
        CanonicalLowerBound = Encoding.UTF8.GetBytes(
            $"{Convert.ToHexString(request.Scope.ProtectedIndexDigest.AsSpan())}\n{request.After?.DefinitionId ?? string.Empty}\n{request.After?.DefinitionVersion ?? 0}\n{request.After?.EffectiveDueAt ?? -1}\n{request.After?.ActivationId ?? string.Empty}").ToImmutableArray(),
        LowerInclusive = false,
        CanonicalUpperBound = Encoding.UTF8.GetBytes(
            $"{Convert.ToHexString(request.Scope.ProtectedIndexDigest.AsSpan())}\n{request.Definition?.Id ?? string.Empty}\n{request.Definition?.Version ?? 0}\n{(int)request.States}\n{next?.ActivationId ?? string.Empty}").ToImmutableArray(),
        UpperInclusive = true,
    };

    private async ValueTask<SqliteExecutorRow?> ReadExecutorAsync(
        SqliteConnection connection, SqliteTransaction transaction, string applicationId, string hostId, string processId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT executor_generation,store_instance_id,restore_epoch,worker_set_checksum,authority_checksum,heartbeat_revision,heartbeat_expires_at,heartbeat_checksum,retired FROM {_names.Executors} WHERE application_id=$application AND host_id=$host AND process_incarnation_id=$process;";
        command.Parameters.AddWithValue("$application", applicationId); command.Parameters.AddWithValue("$host", hostId); command.Parameters.AddWithValue("$process", processId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var authority = new BaseExecutorIncarnationAuthority
        {
            ApplicationId = applicationId, HostId = hostId, ProcessIncarnationId = processId, ExecutorGeneration = reader.GetInt64(0),
            StoreInstanceId = reader.GetString(1), RestoreEpoch = reader.GetInt64(2), WorkerDefinitionSetChecksum = ((byte[])reader[3]).ToImmutableArray(),
            Checksum = ((byte[])reader[4]).ToImmutableArray(),
        };
        return new SqliteExecutorRow(authority, new BaseExecutorHeartbeatObservation
        {
            HeartbeatRevision = reader.GetInt64(5), HeartbeatExpiresAt = reader.GetInt64(6),
            ExecutorAuthorityChecksum = authority.Checksum, Checksum = ((byte[])reader[7]).ToImmutableArray(),
        }, reader.GetInt64(8) != 0);
    }

    private async ValueTask<BaseScheduleAuthority?> ReadScheduleCoreAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string id, int version, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT definition_json,definition_generation,enabled,schedule_epoch,last_nominal,next_nominal,authority_checksum FROM {_names.ActivationSchedules} WHERE schedule_id=$id AND schedule_version=$version;";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$version", version);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        BaseScheduleDefinition definition = JsonSerializer.Deserialize((byte[])reader[0], HPDBaseJsonSerializerContext.Default.BaseScheduleDefinition)
            ?? throw new InvalidOperationException("base.activation.scheduleInvalid");
        definition = BaseScheduleDefinitionBuilder.Create(definition);
        var authority = new BaseScheduleAuthority { Definition = definition, DefinitionGeneration = reader.GetInt64(1), Enabled = reader.GetInt64(2) != 0,
            ScheduleEpoch = reader.GetInt64(3), LastConsideredNominal = reader.IsDBNull(4) ? null : reader.GetInt64(4), NextNominal = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            Checksum = ((byte[])reader[6]).ToImmutableArray() };
        BaseScheduleAuthority expected = SqliteScheduleAuthority(definition, authority.DefinitionGeneration, authority.Enabled, authority.ScheduleEpoch, authority.LastConsideredNominal, authority.NextNominal);
        if (!CryptographicOperations.FixedTimeEquals(authority.Checksum.AsSpan(), expected.Checksum.AsSpan())) throw new InvalidOperationException("base.activation.scheduleInvalid");
        return authority;
    }

    private async ValueTask WriteScheduleAsync(SqliteConnection connection, SqliteTransaction transaction, BaseScheduleAuthority authority, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT OR REPLACE INTO {_names.ActivationSchedules}(schedule_id,schedule_version,definition_json,definition_generation,enabled,schedule_epoch,last_nominal,next_nominal,authority_checksum) VALUES($id,$version,$definition,$generation,$enabled,$epoch,$last,$next,$checksum);";
        command.Parameters.AddWithValue("$id", authority.Definition.Id); command.Parameters.AddWithValue("$version", authority.Definition.Version);
        command.Parameters.Add("$definition", SqliteType.Blob).Value = JsonSerializer.SerializeToUtf8Bytes(authority.Definition, HPDBaseJsonSerializerContext.Default.BaseScheduleDefinition);
        command.Parameters.AddWithValue("$generation", authority.DefinitionGeneration); command.Parameters.AddWithValue("$enabled", authority.Enabled ? 1 : 0); command.Parameters.AddWithValue("$epoch", authority.ScheduleEpoch);
        command.Parameters.AddWithValue("$last", (object?)authority.LastConsideredNominal ?? DBNull.Value); command.Parameters.AddWithValue("$next", (object?)authority.NextNominal ?? DBNull.Value);
        command.Parameters.Add("$checksum", SqliteType.Blob).Value = authority.Checksum.ToArray(); await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BaseScheduleAuthority SqliteScheduleAuthority(BaseScheduleDefinition definition, long generation, bool enabled, long epoch, long? last, long? next)
    {
        byte[] checksum = ActivationHash($"base.activation.schedule.authority.v2\0{definition.Id}\n{definition.Version}\n{Convert.ToHexString(definition.Checksum.AsSpan())}\n{generation}\n{enabled}\n{epoch}\n{last?.ToString() ?? "none"}\n{next?.ToString() ?? "none"}");
        return new BaseScheduleAuthority { Definition = BaseScheduleDefinitionBuilder.Create(definition), DefinitionGeneration = generation, Enabled = enabled,
            ScheduleEpoch = epoch, LastConsideredNominal = last, NextNominal = next, Checksum = checksum.ToImmutableArray() };
    }

    private static bool SqliteOccurrenceShapeValid(BaseScheduleOccurrenceProposal proposal) => proposal.Fact.Disposition switch
    {
        BaseOccurrenceMaterialized value => proposal.Activation is not null && value.ActivationId.Length > 0,
        BaseOccurrenceSkippedMisfire => proposal.Activation is null,
        BaseOccurrenceSkippedOverlap value => proposal.Activation is null && value.BlockingActivationId.Length > 0,
        BaseOccurrenceCancelled value => proposal.Activation is null && value.CancellationReceiptId.Length > 0,
        BaseOccurrenceSuppressedByReplacement value => proposal.Activation is null && value.ReplacementGeneration > 0,
        BaseOccurrenceSuppressedByRestoreFloor value => proposal.Activation is null && value.FloorChecksum.Length == 32,
        _ => false,
    };

    private static byte[] SqliteOccurrenceChecksum(BaseScheduleOccurrenceFact fact) => ActivationHash(
        $"base.activation.schedule.occurrence.v2\0{fact.OccurrenceId}\n{fact.ScheduleId}\n{fact.ScheduleEpoch}\n{fact.NominalAt}\n{fact.EffectiveAt}\n{fact.OverlapOrdinal}\n{SqliteDispositionText(fact.Disposition)}");
    private static string SqliteDispositionText(BaseScheduleOccurrenceDisposition disposition) => disposition switch
    {
        BaseOccurrenceMaterialized value => $"materialized:{value.ActivationId}", BaseOccurrenceSkippedMisfire => "skipped-misfire",
        BaseOccurrenceSkippedOverlap value => $"skipped-overlap:{value.BlockingActivationId}", BaseOccurrenceCancelled value => $"cancelled:{value.CancellationReceiptId}",
        BaseOccurrenceSuppressedByReplacement value => $"replacement:{value.ReplacementGeneration}", BaseOccurrenceSuppressedByRestoreFloor value => $"restore:{Convert.ToHexString(value.FloorChecksum.AsSpan())}",
        _ => throw new InvalidOperationException("base.activation.occurrenceInvalid"),
    };
    private static byte[] SqliteScheduleActivationFingerprint(BaseActivationCreateIntent activation, string occurrenceId) =>
        ActivationHash($"base.activation.schedule.create.v3\0{occurrenceId}\n{activation.Definition.Id}\n{activation.Definition.Version}\n{activation.MaximumYields}\n{Convert.ToHexString(activation.InputChecksum.AsSpan())}\n{activation.RequestedDueAt}\n{activation.EffectiveDueAt ?? activation.RequestedDueAt}");

    private async ValueTask<bool> AcceptActivationTimeAsync(BaseAcceptedTimeReceipt receipt, CancellationToken cancellationToken)
    {
        long native = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (!BaseActivationAcceptedTimeAuthority.Verify(receipt, native)) return false;
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        long persisted;
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='activation_accepted_utc';";
            persisted = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        if (receipt.CapturedUtc < persisted) return false;
        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {_names.ProviderState} SET value=$value WHERE key='activation_accepted_utc';";
            update.Parameters.AddWithValue("$value", receipt.CapturedUtc.ToString(CultureInfo.InvariantCulture));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) return false;
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<BaseEffectExecutionAuthority?> ReadEffectAsync(
        SqliteConnection connection, SqliteTransaction transaction, string activationId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT claim_attempt,claim_activation_generation,claim_slice,claim_attempt_started_at,claim_slice_started_at,claim_yield_count,claim_maximum_yields,claim_epoch,claim_fence,claim_worker,cancellation_generation,claim_store_id,claim_restore_epoch,definition_checksum,executor_application,executor_host,executor_process,executor_generation,executor_store_id,executor_restore_epoch,worker_set_checksum,executor_checksum,effect_start_generation,heartbeat_revision,heartbeat_expires_at,effect_checksum FROM {_names.ActivationEffects} WHERE activation_id=$id;";
        command.Parameters.AddWithValue("$id", activationId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var claim = new BaseActivationClaimAuthority
        {
            ActivationId = activationId, AttemptNumber = reader.GetInt32(0), ActivationGeneration = reader.GetInt64(1),
            ExecutionSliceOrdinal = reader.GetInt64(2), AttemptStartedAt = reader.GetInt64(3), SliceStartedAt = reader.GetInt64(4),
            YieldCount = reader.GetInt64(5), MaximumYields = reader.GetInt64(6), ClaimEpoch = reader.GetInt64(7),
            FencingToken = ((byte[])reader[8]).ToImmutableArray(), WorkerIdentity = reader.GetString(9),
            CancellationGeneration = reader.GetInt64(10), StoreInstanceId = reader.GetString(11),
            RestoreEpoch = reader.GetInt64(12), DefinitionChecksum = ((byte[])reader[13]).ToImmutableArray(),
        };
        var executor = new BaseExecutorIncarnationAuthority
        {
            ApplicationId = reader.GetString(14), HostId = reader.GetString(15), ProcessIncarnationId = reader.GetString(16),
            ExecutorGeneration = reader.GetInt64(17), StoreInstanceId = reader.GetString(18), RestoreEpoch = reader.GetInt64(19),
            WorkerDefinitionSetChecksum = ((byte[])reader[20]).ToImmutableArray(), Checksum = ((byte[])reader[21]).ToImmutableArray(),
        };
        return new BaseEffectExecutionAuthority
        {
            Claim = claim, Executor = executor, EffectStartGeneration = reader.GetInt64(22), HeartbeatRevision = reader.GetInt64(23),
            HeartbeatExpiresAt = reader.GetInt64(24), Checksum = ((byte[])reader[25]).ToImmutableArray(),
        };
    }

    private async ValueTask WriteEffectAsync(SqliteConnection connection, SqliteTransaction transaction,
        BaseEffectExecutionAuthority effect, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT OR REPLACE INTO {_names.ActivationEffects}(activation_id,claim_attempt,claim_activation_generation,claim_slice,claim_attempt_started_at,claim_slice_started_at,claim_yield_count,claim_maximum_yields,claim_epoch,claim_fence,claim_worker,cancellation_generation,claim_store_id,claim_restore_epoch,definition_checksum,executor_application,executor_host,executor_process,executor_generation,executor_store_id,executor_restore_epoch,worker_set_checksum,executor_checksum,effect_start_generation,heartbeat_revision,heartbeat_expires_at,effect_checksum) VALUES($id,$attempt,$claim_generation,$slice,$attempt_started,$slice_started,$yield_count,$maximum_yields,$epoch,$fence,$worker,$cancel,$claim_store,$claim_restore,$definition,$application,$host,$process,$generation,$executor_store,$executor_restore,$worker_set,$executor_checksum,$start,$revision,$expires,$effect_checksum);";
        command.Parameters.AddWithValue("$id", effect.Claim.ActivationId); command.Parameters.AddWithValue("$attempt", effect.Claim.AttemptNumber); command.Parameters.AddWithValue("$epoch", effect.Claim.ClaimEpoch);
        command.Parameters.AddWithValue("$claim_generation", effect.Claim.ActivationGeneration);
        command.Parameters.AddWithValue("$slice", effect.Claim.ExecutionSliceOrdinal);
        command.Parameters.AddWithValue("$attempt_started", effect.Claim.AttemptStartedAt);
        command.Parameters.AddWithValue("$slice_started", effect.Claim.SliceStartedAt);
        command.Parameters.AddWithValue("$yield_count", effect.Claim.YieldCount);
        command.Parameters.AddWithValue("$maximum_yields", effect.Claim.MaximumYields);
        command.Parameters.Add("$fence", SqliteType.Blob).Value = effect.Claim.FencingToken.ToArray(); command.Parameters.AddWithValue("$worker", effect.Claim.WorkerIdentity);
        command.Parameters.AddWithValue("$cancel", effect.Claim.CancellationGeneration); command.Parameters.AddWithValue("$claim_store", effect.Claim.StoreInstanceId); command.Parameters.AddWithValue("$claim_restore", effect.Claim.RestoreEpoch);
        command.Parameters.Add("$definition", SqliteType.Blob).Value = effect.Claim.DefinitionChecksum.ToArray(); command.Parameters.AddWithValue("$application", effect.Executor.ApplicationId);
        command.Parameters.AddWithValue("$host", effect.Executor.HostId); command.Parameters.AddWithValue("$process", effect.Executor.ProcessIncarnationId); command.Parameters.AddWithValue("$generation", effect.Executor.ExecutorGeneration);
        command.Parameters.AddWithValue("$executor_store", effect.Executor.StoreInstanceId); command.Parameters.AddWithValue("$executor_restore", effect.Executor.RestoreEpoch);
        command.Parameters.Add("$worker_set", SqliteType.Blob).Value = effect.Executor.WorkerDefinitionSetChecksum.ToArray(); command.Parameters.Add("$executor_checksum", SqliteType.Blob).Value = effect.Executor.Checksum.ToArray();
        command.Parameters.AddWithValue("$start", effect.EffectStartGeneration); command.Parameters.AddWithValue("$revision", effect.HeartbeatRevision); command.Parameters.AddWithValue("$expires", effect.HeartbeatExpiresAt);
        command.Parameters.Add("$effect_checksum", SqliteType.Blob).Value = effect.Checksum.ToArray(); await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BaseEffectExecutionAuthority SqliteEffect(BaseActivationClaimAuthority claim, BaseExecutorIncarnationAuthority executor,
        long generation, long revision, long expiresAt)
    {
        byte[] checksum = ActivationHash($"base.activation.effect.v2\0{claim.ActivationId}\n{Convert.ToHexString(claim.FencingToken.AsSpan())}\n{Convert.ToHexString(executor.Checksum.AsSpan())}\n{generation}\n{revision}\n{expiresAt}");
        return new BaseEffectExecutionAuthority { Claim = claim, Executor = executor, EffectStartGeneration = generation,
            HeartbeatRevision = revision, HeartbeatExpiresAt = expiresAt, Checksum = checksum.ToImmutableArray() };
    }

    private static bool SqliteEffectMatches(BaseEffectExecutionAuthority left, BaseEffectExecutionAuthority right) =>
        left.EffectStartGeneration == right.EffectStartGeneration && left.HeartbeatRevision == right.HeartbeatRevision && left.HeartbeatExpiresAt == right.HeartbeatExpiresAt &&
        left.Claim.ActivationId == right.Claim.ActivationId && left.Claim.AttemptNumber == right.Claim.AttemptNumber && left.Claim.ClaimEpoch == right.Claim.ClaimEpoch &&
        CryptographicOperations.FixedTimeEquals(left.Claim.FencingToken.AsSpan(), right.Claim.FencingToken.AsSpan()) &&
        SqliteExecutorMatches(left.Executor, right.Executor) && CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private async ValueTask WriteExecutorAsync(SqliteConnection connection, SqliteTransaction transaction,
        BaseExecutorIncarnationAuthority authority, BaseExecutorHeartbeatObservation heartbeat, bool retired, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT OR REPLACE INTO {_names.Executors}(application_id,host_id,process_incarnation_id,executor_generation,store_instance_id,restore_epoch,worker_set_checksum,authority_checksum,heartbeat_revision,heartbeat_expires_at,heartbeat_checksum,retired) VALUES($application,$host,$process,$generation,$store,$restore,$workers,$authority,$revision,$expires,$heartbeat,$retired);";
        command.Parameters.AddWithValue("$application", authority.ApplicationId); command.Parameters.AddWithValue("$host", authority.HostId); command.Parameters.AddWithValue("$process", authority.ProcessIncarnationId);
        command.Parameters.AddWithValue("$generation", authority.ExecutorGeneration); command.Parameters.AddWithValue("$store", authority.StoreInstanceId); command.Parameters.AddWithValue("$restore", authority.RestoreEpoch);
        command.Parameters.Add("$workers", SqliteType.Blob).Value = authority.WorkerDefinitionSetChecksum.ToArray(); command.Parameters.Add("$authority", SqliteType.Blob).Value = authority.Checksum.ToArray();
        command.Parameters.AddWithValue("$revision", heartbeat.HeartbeatRevision); command.Parameters.AddWithValue("$expires", heartbeat.HeartbeatExpiresAt); command.Parameters.Add("$heartbeat", SqliteType.Blob).Value = heartbeat.Checksum.ToArray();
        command.Parameters.AddWithValue("$retired", retired ? 1 : 0); await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BaseExecutorHeartbeatObservation ExecutorHeartbeat(BaseExecutorIncarnationAuthority authority, long revision, long expiresAt)
    {
        byte[] checksum = ActivationHash($"base.activation.executor.heartbeat.v2\0{Convert.ToHexString(authority.Checksum.AsSpan())}\n{revision}\n{expiresAt}");
        return new BaseExecutorHeartbeatObservation { HeartbeatRevision = revision, HeartbeatExpiresAt = expiresAt,
            ExecutorAuthorityChecksum = authority.Checksum.ToArray().ToImmutableArray(), Checksum = checksum.ToImmutableArray() };
    }

    private static bool SqliteExecutorMatches(BaseExecutorIncarnationAuthority left, BaseExecutorIncarnationAuthority right) =>
        left.ApplicationId == right.ApplicationId && left.HostId == right.HostId && left.ProcessIncarnationId == right.ProcessIncarnationId &&
        left.ExecutorGeneration == right.ExecutorGeneration && left.StoreInstanceId == right.StoreInstanceId && left.RestoreEpoch == right.RestoreEpoch &&
        CryptographicOperations.FixedTimeEquals(left.WorkerDefinitionSetChecksum.AsSpan(), right.WorkerDefinitionSetChecksum.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static bool SqliteHeartbeatsEqual(BaseExecutorHeartbeatObservation left, BaseExecutorHeartbeatObservation right) =>
        left.HeartbeatRevision == right.HeartbeatRevision && left.HeartbeatExpiresAt == right.HeartbeatExpiresAt &&
        CryptographicOperations.FixedTimeEquals(left.ExecutorAuthorityChecksum.AsSpan(), right.ExecutorAuthorityChecksum.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private sealed record SqliteExecutorRow(BaseExecutorIncarnationAuthority Authority, BaseExecutorHeartbeatObservation Heartbeat, bool Retired);

    private async ValueTask<List<SqliteActivationRow>> ReadDueRowsAsync(
        SqliteConnection connection, SqliteTransaction? transaction, ImmutableArray<BaseActivationDefinitionKey> definitions,
        BaseOwnedScopeSeekAuthority scope, long now, BaseActivationDueBoundary? after, int take, CancellationToken cancellationToken)
    {
        if (definitions.IsDefaultOrEmpty) return [];
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        var predicate = new StringBuilder();
        for (int i = 0; i < definitions.Length; i++)
        {
            if (i > 0) predicate.Append(" OR ");
            predicate.Append($"(definition_id=$definition{i} AND definition_version=$version{i} AND definition_checksum=$checksum{i})");
            command.Parameters.AddWithValue($"$definition{i}", definitions[i].Id);
            command.Parameters.AddWithValue($"$version{i}", definitions[i].Version);
            command.Parameters.Add($"$checksum{i}", SqliteType.Blob).Value = definitions[i].Checksum.ToArray();
        }
        command.CommandText = $"SELECT activation_id,definition_id,definition_version,definition_checksum,receipt_format_version,receipt_duplicate_lifetime_ms,receipt_backup_coverage,canonical_input,input_checksum,scope_kind,scope_value,payload_checksum,state,generation,requested_due_at,effective_due_at,control_checksum,attempt_number,execution_slice_ordinal,attempt_started_at,slice_started_at,yield_count,maximum_yields,yield_terminal_disposition,yield_terminal_failure_code,claim_epoch,claim_fence,claim_worker,lease_revision,lease_expires_at,occurrence_id,priority,overlap_key,overlap_policy,eligible FROM {_names.Activations} INDEXED BY {_names.Prefix}activation_due_idx WHERE scope_kind=$scope_kind AND scope_digest=$scope_digest AND eligible=1 AND ((state IN ($pending,$retry,$yield) AND effective_due_at<=$now) OR (state=$claimed AND lease_expires_at<=$now)) AND (overlap_policy<>$queue OR overlap_key IS NULL OR NOT EXISTS(SELECT 1 FROM {_names.Activations} b WHERE b.overlap_key={_names.Activations}.overlap_key AND b.activation_id<>{_names.Activations}.activation_id AND b.state IN ($pending,$retry,$yield,$claimed) AND (b.effective_due_at<{_names.Activations}.effective_due_at OR (b.effective_due_at={_names.Activations}.effective_due_at AND b.activation_id<{_names.Activations}.activation_id)))) AND ({predicate}) AND ($after_priority IS NULL OR MIN(32,priority+CAST(MAX(0,$now-effective_due_at)/60000 AS INTEGER))<$after_priority OR (MIN(32,priority+CAST(MAX(0,$now-effective_due_at)/60000 AS INTEGER))=$after_priority AND (effective_due_at>$after_due OR (effective_due_at=$after_due AND (COALESCE(occurrence_id,'')>$after_occurrence OR (COALESCE(occurrence_id,'')=$after_occurrence AND activation_id>$after_id)))))) ORDER BY MIN(32,priority+CAST(MAX(0,$now-effective_due_at)/60000 AS INTEGER)) DESC,effective_due_at,COALESCE(occurrence_id,''),activation_id LIMIT $take;";
        command.Parameters.AddWithValue("$scope_kind", (int)scope.Kind); command.Parameters.Add("$scope_digest", SqliteType.Blob).Value = scope.ProtectedIndexDigest.ToArray();
        command.Parameters.AddWithValue("$pending", (int)BaseActivationState.Pending); command.Parameters.AddWithValue("$retry", (int)BaseActivationState.RetryPending);
        command.Parameters.AddWithValue("$yield", (int)BaseActivationState.YieldPending);
        command.Parameters.AddWithValue("$claimed", (int)BaseActivationState.Claimed); command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$queue", (int)BaseScheduleOverlapPolicy.Queue);
        command.Parameters.AddWithValue("$after_priority", (object?)after?.EffectiveAgedPriority ?? DBNull.Value);
        command.Parameters.AddWithValue("$after_due", (object?)after?.EffectiveDueAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$after_occurrence", after?.OccurrenceId ?? string.Empty); command.Parameters.AddWithValue("$after_id", after?.ActivationId ?? string.Empty);
        command.Parameters.AddWithValue("$take", take);
        var result = new List<SqliteActivationRow>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadActivationRow(reader));
        return result;
    }

    private async ValueTask<SqliteActivationRow?> ReadActivationAsync(SqliteConnection connection, SqliteTransaction transaction, string id, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT activation_id,definition_id,definition_version,definition_checksum,receipt_format_version,receipt_duplicate_lifetime_ms,receipt_backup_coverage,canonical_input,input_checksum,scope_kind,scope_value,payload_checksum,state,generation,requested_due_at,effective_due_at,control_checksum,attempt_number,execution_slice_ordinal,attempt_started_at,slice_started_at,yield_count,maximum_yields,yield_terminal_disposition,yield_terminal_failure_code,claim_epoch,claim_fence,claim_worker,lease_revision,lease_expires_at,occurrence_id,priority,overlap_key,overlap_policy,eligible FROM {_names.Activations} WHERE activation_id=$id;";
        command.Parameters.AddWithValue("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadActivationRow(reader) : null;
    }

    private static SqliteActivationRow ReadActivationRow(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetInt32(2), (byte[])reader[3],
        new BaseActivationReceiptRetentionPolicy
        {
            FormatVersion = reader.GetInt32(4),
            DuplicateResolutionLifetime = TimeSpan.FromMilliseconds(reader.GetInt64(5)),
            ProtectedBackupCoverage = (BaseActivationProtectedBackupCoverage)reader.GetInt32(6),
        },
        (byte[])reader[7], (byte[])reader[8], (BaseSubjectScopeKind)reader.GetInt32(9), reader.GetString(10), (byte[])reader[11], (BaseActivationState)reader.GetInt32(12),
        reader.GetInt64(13), reader.GetInt64(14), reader.GetInt64(15), (byte[])reader[16], reader.GetInt32(17),
        reader.GetInt64(18), reader.IsDBNull(19) ? null : reader.GetInt64(19), reader.IsDBNull(20) ? null : reader.GetInt64(20),
        reader.GetInt64(21), reader.GetInt64(22), reader.IsDBNull(23) ? null : (BaseActivationYieldDisposition?)reader.GetInt32(23),
        reader.IsDBNull(24) ? null : reader.GetString(24), reader.GetInt64(25),
        reader.IsDBNull(26) ? null : (byte[])reader[26], reader.IsDBNull(27) ? null : reader.GetString(27),
        reader.IsDBNull(28) ? null : reader.GetInt64(28), reader.IsDBNull(29) ? null : reader.GetInt64(29),
        reader.IsDBNull(30) ? null : reader.GetString(30), reader.GetInt32(31), reader.IsDBNull(32) ? null : (byte[])reader[32],
        (BaseScheduleOverlapPolicy)reader.GetInt32(33), reader.GetInt32(34) == 1);

    private async ValueTask<(long Generation, long RestoreEpoch)> ReadActivationAuthorityAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT (SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='activation_generation'),(SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch');";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("Activation authority is unavailable.");
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private async ValueTask<BaseActivationYieldReservationState> ReadYieldReservationStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
              (SELECT value FROM {_names.ProviderState} WHERE key='activation_yield_reservation_format'),
              (SELECT value FROM {_names.ProviderState} WHERE key='activation_yield_reservation_generation'),
              (SELECT value FROM {_names.ProviderState} WHERE key='activation_yield_reservation_maximum'),
              (SELECT value FROM {_names.ProviderState} WHERE key='activation_yield_reserved_unused'),
              (SELECT value FROM {_names.ProviderState} WHERE key='activation_yield_retained_used'),
              (SELECT value FROM {_names.ProviderState} WHERE key='activation_yield_reservation_checksum');
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("base.activation.providerContractInvalid");
        BaseActivationYieldReservationState state;
        try
        {
            state = new BaseActivationYieldReservationState
            {
                FormatVersion = int.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                Generation = long.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                MaximumSlots = long.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                ReservedUnusedSlots = long.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                RetainedUsedSlots = long.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                Checksum = Convert.FromHexString(reader.GetString(5)).ToImmutableArray(),
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCastException)
        {
            throw new InvalidDataException("base.activation.providerContractInvalid", exception);
        }
        if (!BaseActivationYieldReservationContract.IsValid(state)
            || state.MaximumSlots != ((IBaseActivationProvider)this).Descriptor.Capability.MaximumReservedYieldReceiptSlots)
            throw new InvalidDataException("base.activation.providerContractInvalid");
        return state;
    }

    private async ValueTask WriteYieldReservationStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BaseActivationYieldReservationState state,
        CancellationToken cancellationToken)
    {
        if (!BaseActivationYieldReservationContract.IsValid(state))
            throw new InvalidDataException("base.activation.providerContractInvalid");
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {_names.ProviderState} SET value=$generation WHERE key='activation_yield_reservation_generation';
            UPDATE {_names.ProviderState} SET value=$reserved WHERE key='activation_yield_reserved_unused';
            UPDATE {_names.ProviderState} SET value=$used WHERE key='activation_yield_retained_used';
            UPDATE {_names.ProviderState} SET value=$checksum WHERE key='activation_yield_reservation_checksum';
            """;
        command.Parameters.AddWithValue("$generation", state.Generation.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$reserved", state.ReservedUnusedSlots.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$used", state.RetainedUsedSlots.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$checksum", Convert.ToHexStringLower(state.Checksum.AsSpan()));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 4)
            throw new InvalidDataException("base.activation.providerContractInvalid");
    }

    private async ValueTask<bool> TryReserveYieldReceiptSlotsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long maximumYields,
        CancellationToken cancellationToken)
    {
        if (maximumYields == 0) return true;
        BaseActivationYieldReservationState current = await ReadYieldReservationStateAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        long reserved = checked(current.ReservedUnusedSlots + maximumYields + 1);
        if (checked(reserved + current.RetainedUsedSlots) > current.MaximumSlots) return false;
        BaseActivationYieldReservationState next = BaseActivationYieldReservationContract.Create(
            checked(current.Generation + 1), current.MaximumSlots, reserved, current.RetainedUsedSlots);
        await WriteYieldReservationStateAsync(connection, transaction, next, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask ApplyYieldReceiptReservationTransitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteActivationRow row,
        BaseActivationState resultingState,
        BaseActivationYieldDisposition? yieldDisposition,
        CancellationToken cancellationToken)
    {
        if (row.MaximumYields == 0) return;
        BaseActivationYieldReservationState current = await ReadYieldReservationStateAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        long reserved = current.ReservedUnusedSlots;
        long used = current.RetainedUsedSlots;
        if (yieldDisposition is BaseActivationYieldDisposition.Yielded or BaseActivationYieldDisposition.LimitExceeded)
        {
            reserved = checked(reserved - 1);
            used = checked(used + 1);
        }
        else if (resultingState is BaseActivationState.Succeeded or BaseActivationState.Exhausted or
            BaseActivationState.Cancelled or BaseActivationState.Disposed or BaseActivationState.Migrated)
        {
            if (row.State is BaseActivationState.Succeeded or BaseActivationState.Exhausted or
                BaseActivationState.Cancelled or BaseActivationState.Disposed or BaseActivationState.Migrated) return;
            reserved = checked(reserved - checked(row.MaximumYields + 1 - row.YieldCount));
        }
        else
        {
            return;
        }
        BaseActivationYieldReservationState next = BaseActivationYieldReservationContract.Create(
            checked(current.Generation + 1), current.MaximumSlots, reserved, used);
        await WriteYieldReservationStateAsync(connection, transaction, next, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ReleaseYieldReservationForActivationRemovalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string activationId,
        CancellationToken cancellationToken)
    {
        SqliteActivationRow? row = await ReadActivationAsync(
            connection, transaction, activationId, cancellationToken).ConfigureAwait(false);
        if (row is null) return;
        await using SqliteCommand count = connection.CreateCommand();
        count.Transaction = transaction;
        count.CommandText = $"SELECT COUNT(*) FROM {_names.ActivationInstanceReceipts} WHERE activation_id=$id AND operation_kind='activation-yielded-v1';";
        count.Parameters.AddWithValue("$id", activationId);
        long retainedYieldReceipts = Convert.ToInt64(
            await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        long remainingReserved = row.MaximumYields > 0 && row.State is not (
            BaseActivationState.Succeeded or BaseActivationState.Exhausted or BaseActivationState.Cancelled
            or BaseActivationState.Disposed or BaseActivationState.Migrated)
            ? checked(row.MaximumYields + 1 - row.YieldCount)
            : 0;
        if (remainingReserved == 0 && retainedYieldReceipts == 0) return;
        BaseActivationYieldReservationState current = await ReadYieldReservationStateAsync(
            connection, transaction, cancellationToken).ConfigureAwait(false);
        if (current.ReservedUnusedSlots < remainingReserved
            || current.RetainedUsedSlots < retainedYieldReceipts)
            throw new InvalidDataException("base.activation.providerContractInvalid");
        BaseActivationYieldReservationState next = BaseActivationYieldReservationContract.Create(
            checked(current.Generation + 1), current.MaximumSlots,
            checked(current.ReservedUnusedSlots - remainingReserved),
            checked(current.RetainedUsedSlots - retainedYieldReceipts));
        await WriteYieldReservationStateAsync(connection, transaction, next, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask IncrementActivationGenerationAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.ProviderState} SET value=CAST(CAST(value AS INTEGER)+1 AS TEXT) WHERE key='activation_generation';";
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidOperationException("Activation generation update failed.");
    }

    private async ValueTask UpdateRecoveredAsync(SqliteConnection connection, SqliteTransaction transaction, SqliteActivationRow row, long generation, long now, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.Activations} SET state=$state,generation=$generation,claim_fence=NULL,claim_worker=NULL,lease_revision=NULL,lease_expires_at=NULL,effective_due_at=$now,control_checksum=$checksum WHERE activation_id=$id AND state=$claimed AND lease_expires_at<=$now;";
        command.Parameters.AddWithValue("$state", (int)BaseActivationState.RetryPending); command.Parameters.AddWithValue("$generation", generation);
        command.Parameters.AddWithValue("$now", now); command.Parameters.Add("$checksum", SqliteType.Blob).Value = ActivationControlChecksum(
            row.ActivationId, generation, BaseActivationState.RetryPending, now,
            row.YieldCount, row.MaximumYields, row.ExecutionSliceOrdinal,
            row.AttemptStartedAt, row.SliceStartedAt, null, null);
        command.Parameters.AddWithValue("$id", row.ActivationId); command.Parameters.AddWithValue("$claimed", (int)BaseActivationState.Claimed);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidOperationException("Expired activation recovery conflicted.");
    }

    private static bool SqliteClaimMatches(SqliteActivationRow row, BaseActivationClaimAuthority claim) =>
        row.State == BaseActivationState.Claimed && row.Generation == claim.ActivationGeneration &&
        row.AttemptNumber == claim.AttemptNumber && row.ClaimEpoch == claim.ClaimEpoch &&
        row.ExecutionSliceOrdinal == claim.ExecutionSliceOrdinal && row.AttemptStartedAt == claim.AttemptStartedAt &&
        row.SliceStartedAt == claim.SliceStartedAt && row.YieldCount == claim.YieldCount && row.MaximumYields == claim.MaximumYields &&
        row.ClaimFence is not null && row.ClaimWorker == claim.WorkerIdentity &&
        CryptographicOperations.FixedTimeEquals(row.ClaimFence, claim.FencingToken.AsSpan());

    private static bool SqliteMigrationState(BaseActivationState state) => state is
        BaseActivationState.Pending or BaseActivationState.RetryPending or BaseActivationState.YieldPending or BaseActivationState.Exhausted
        or BaseActivationState.Cancelled;

    private static BaseActivationDueBoundary ActivationBoundary(SqliteActivationRow row, long now) => new()
    {
        EffectiveAgedPriority = Math.Min(32, row.Priority + checked((int)Math.Min(int.MaxValue, Math.Max(0, now - row.EffectiveDueAt) / 60_000))),
        EffectiveDueAt = row.EffectiveDueAt, OccurrenceId = row.OccurrenceId, ActivationId = row.ActivationId,
    };

    private static byte[] ActivationDueToken(long generation, long restoreEpoch, long now, ReadOnlySpan<byte> scope,
        ImmutableArray<BaseActivationDefinitionKey> definitions, BaseActivationDueBoundary? first)
    {
        string definitionText = string.Join("\n", definitions.Select(static item => $"{item.Id}:{item.Version}:{Convert.ToHexString(item.Checksum.AsSpan())}"));
        byte[] digest = ActivationHash($"base.activation.due.token.v2\0{generation}\n{restoreEpoch}\n{now}\n{Convert.ToHexString(scope)}\n{definitionText}\n{first?.ActivationId ?? string.Empty}");
        byte[] token = new byte[56]; BinaryPrimitives.WriteInt64BigEndian(token, generation); BinaryPrimitives.WriteInt64BigEndian(token.AsSpan(8), restoreEpoch); BinaryPrimitives.WriteInt64BigEndian(token.AsSpan(16), now); digest.CopyTo(token, 24); return token;
    }

    private static (long Generation, long RestoreEpoch, long AcceptedAt) DecodeActivationTokenAuthority(ReadOnlySpan<byte> token) =>
        token.Length == 56
            ? (BinaryPrimitives.ReadInt64BigEndian(token), BinaryPrimitives.ReadInt64BigEndian(token[8..]), BinaryPrimitives.ReadInt64BigEndian(token[16..]))
            : (-1, -1, -1);

    private static BaseAtomicReadIntervalEvidence ActivationDueInterval(BaseOwnedScopeSeekAuthority scope, long now,
        BaseActivationDueBoundary? after, BaseActivationDueBoundary? result) => new()
    {
        LogicalAccessPathId = "base.activation.due.byScopeDefinitionPriorityTime.v1",
        CanonicalLowerBound = Encoding.UTF8.GetBytes(after?.ActivationId ?? string.Empty).ToImmutableArray(), LowerInclusive = false,
        CanonicalUpperBound = Encoding.UTF8.GetBytes($"{now}\n{result?.ActivationId ?? string.Empty}\n{Convert.ToHexString(scope.ProtectedIndexDigest.AsSpan())}").ToImmutableArray(), UpperInclusive = true,
    };

    private static long ActivationIntervalBytes(BaseAtomicReadIntervalEvidence interval) =>
        checked(Encoding.UTF8.GetByteCount(interval.LogicalAccessPathId) + interval.CanonicalLowerBound.Length + interval.CanonicalUpperBound.Length + 2);

    private static BaseActivationAccounting ActivationAccounting(int candidates, long evidence) => new()
    { Candidates = candidates, Comparisons = candidates, IndexOperations = 1, ReadIntervals = 1, EvidenceBytes = evidence, TransientBytes = evidence };

    private static bool ActivationLimitsValid(BaseActivationExecutionLimits limits) => limits.MaximumCandidates is > 0 and <= 256 &&
        limits.MaximumInputBytes is > 0 and <= 4L * 1024 * 1024 && limits.MaximumResultBytes is > 0 and <= 4L * 1024 * 1024 &&
        limits.MaximumEvidenceBytes is > 0 and <= 16L * 1024 * 1024 && limits.MaximumTransientBytes is > 0 and <= 16L * 1024 * 1024 &&
        limits.MaximumReadIntervals > 0 && limits.MaximumIndexOperations > 0;

    private static byte[] ActivationControlChecksum(
        string id, long generation, BaseActivationState state, long effectiveDueAt,
        long yieldCount, long maximumYields, long executionSliceOrdinal,
        long? attemptStartedAt, long? sliceStartedAt,
        BaseActivationYieldDisposition? terminalYieldDisposition, string? terminalYieldFailureCode) =>
        BaseActivationControlChecksumContract.Create(id, generation, state, effectiveDueAt,
            yieldCount, maximumYields, executionSliceOrdinal, attemptStartedAt, sliceStartedAt,
            terminalYieldDisposition, terminalYieldFailureCode).ToArray();

    private static long? SqliteCanonicalYieldResumeAt(DateTimeOffset? value)
    {
        if (value is null) return null;
        if (value.Value.Offset != TimeSpan.Zero || value.Value.Ticks % TimeSpan.TicksPerMillisecond != 0)
            return -1;
        try { return value.Value.ToUnixTimeMilliseconds(); }
        catch (ArgumentOutOfRangeException) { return -1; }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationReceiptCompactionResult>> CompactActivationReceiptsAsync(
        BaseActivationReceiptCompactionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ApplicationId != _options.SemanticActivationApplicationId
            || request.AcceptedTime.ApplicationId != request.ApplicationId
            || request.Definition.Version < 1 || request.Definition.Checksum.Length != 32
            || request.Take is < 1 or > 256 || request.After is { ReceiptSequence: < 1 }
            || request.Take > request.Limits.MaximumCandidates
            || !BaseActivationYieldReservationContract.IsValid(request.ExpectedReservation)
            || !Enum.IsDefined(request.BackupFloor.Kind)
            || request.BackupFloor.Kind == BaseActivationReceiptBackupFloorKind.NotApplicable
                && (request.BackupFloor.Checkpoint is not null
                    || request.ReceiptRetention.ProtectedBackupCoverage != BaseActivationProtectedBackupCoverage.NotRequired)
            || request.BackupFloor.Kind == BaseActivationReceiptBackupFloorKind.Checkpoint
                && !BaseActivationBackupCoverageCheckpointContract.IsValid(request.BackupFloor.Checkpoint)
            || !ActivationLimitsValid(request.Limits)
            || !await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationReceiptCompactionResult>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (bool found, OperationResult<BaseActivationReceiptCompactionResult> replay) = await ReadControlReceiptAsync(
            connection, transaction, request.Identity, "activation-receipts-compacted",
            HPDBaseJsonSerializerContext.Default.BaseActivationReceiptCompactionResult,
            static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, cancellationToken).ConfigureAwait(false);
        if (found) return replay;
        BaseActivationYieldReservationState priorReservation = await ReadYieldReservationStateAsync(
            connection, transaction, cancellationToken).ConfigureAwait(false);
        if (!SqliteReservationMatches(request.ExpectedReservation, priorReservation))
            return ActivationFailure<BaseActivationReceiptCompactionResult>(
                "base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        if (!await CompactionBackupFloorMatchesAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationReceiptCompactionResult>(
                "base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        BaseActivationInstanceReceiptChainState priorChain = await ReadInstanceReceiptChainAsync(
            connection, transaction, cancellationToken).ConfigureAwait(false);
        var candidates = new List<SqliteReceiptCompactionCandidate>();
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"""
                SELECT r.receipt_key,r.operation_kind,r.activation_id,r.result_json,r.result_checksum,r.authority_checksum,
                       r.committed_at,r.duplicate_resolve_until,r.receipt_sequence,r.prior_ordered_checksum,r.ordered_checksum,
                       COALESCE(a.state,0),COALESCE(a.generation,0),COALESCE(a.execution_slice_ordinal,0),COALESCE(a.yield_count,0),
                       CASE WHEN a.activation_id IS NULL THEN 1 ELSE 0 END
                FROM {_names.ActivationInstanceReceipts} r
                LEFT JOIN {_names.Activations} a ON a.activation_id=r.activation_id
                LEFT JOIN {_names.ActivationReceiptRecoveryFloors} f ON f.activation_id=r.activation_id
                WHERE r.definition_id=$definition AND r.definition_version=$version AND r.definition_checksum=$definitionChecksum
                  AND r.receipt_format_version=$format AND r.receipt_duplicate_lifetime_ms=$lifetime
                  AND r.receipt_backup_coverage=$backup
                  AND ((a.activation_id IS NOT NULL AND a.scope_kind=$scopeKind AND a.scope_digest=$scopeDigest)
                    OR (a.activation_id IS NULL AND f.activation_id IS NOT NULL
                      AND f.definition_id=r.definition_id AND f.definition_version=r.definition_version
                      AND f.definition_checksum=r.definition_checksum
                      AND f.scope_kind=$scopeKind AND f.scope_digest=$scopeDigest))
                  AND (r.operation_kind='activation-yielded-v1' OR a.activation_id IS NULL)
                  AND ($afterId IS NULL OR r.activation_id>$afterId OR (r.activation_id=$afterId AND r.receipt_sequence>$afterSequence))
                ORDER BY r.activation_id,r.receipt_sequence LIMIT $take;
                """;
            read.Parameters.AddWithValue("$definition", request.Definition.Id);
            read.Parameters.AddWithValue("$version", request.Definition.Version);
            read.Parameters.Add("$definitionChecksum", SqliteType.Blob).Value = request.Definition.Checksum.ToArray();
            read.Parameters.AddWithValue("$format", request.ReceiptRetention.FormatVersion);
            read.Parameters.AddWithValue("$lifetime", request.ReceiptRetention.DuplicateResolutionLifetime.Ticks / TimeSpan.TicksPerMillisecond);
            read.Parameters.AddWithValue("$backup", (int)request.ReceiptRetention.ProtectedBackupCoverage);
            read.Parameters.AddWithValue("$scopeKind", (int)request.Scope.Kind);
            read.Parameters.Add("$scopeDigest", SqliteType.Blob).Value = request.Scope.ProtectedIndexDigest.ToArray();
            read.Parameters.AddWithValue("$afterId", (object?)request.After?.ActivationId ?? DBNull.Value);
            read.Parameters.AddWithValue("$afterSequence", request.After?.ReceiptSequence ?? 0);
            read.Parameters.AddWithValue("$take", request.Take);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                candidates.Add(new SqliteReceiptCompactionCandidate(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), (byte[])reader[3], (byte[])reader[4], (byte[])reader[5],
                    reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8), (byte[])reader[9], (byte[])reader[10],
                    (BaseActivationState)reader.GetInt32(11), reader.GetInt64(12), reader.GetInt64(13), reader.GetInt64(14),
                    reader.GetInt32(15) == 1));
        }
        SqliteReceiptCompactionCandidate[] examined = candidates.ToArray();
        bool hasMore = false;
        if (examined.Length == request.Take)
        {
            SqliteReceiptCompactionCandidate boundary = examined[^1];
            await using SqliteCommand more = connection.CreateCommand();
            more.Transaction = transaction;
            more.CommandText = $"""
                SELECT EXISTS(
                  SELECT 1
                  FROM {_names.ActivationInstanceReceipts} r
                  LEFT JOIN {_names.Activations} a ON a.activation_id=r.activation_id
                  LEFT JOIN {_names.ActivationReceiptRecoveryFloors} f ON f.activation_id=r.activation_id
                  WHERE r.definition_id=$definition AND r.definition_version=$version AND r.definition_checksum=$definitionChecksum
                    AND r.receipt_format_version=$format AND r.receipt_duplicate_lifetime_ms=$lifetime
                    AND r.receipt_backup_coverage=$backup
                    AND ((a.activation_id IS NOT NULL AND a.scope_kind=$scopeKind AND a.scope_digest=$scopeDigest)
                      OR (a.activation_id IS NULL AND f.activation_id IS NOT NULL
                        AND f.definition_id=r.definition_id AND f.definition_version=r.definition_version
                        AND f.definition_checksum=r.definition_checksum
                        AND f.scope_kind=$scopeKind AND f.scope_digest=$scopeDigest))
                    AND (r.operation_kind='activation-yielded-v1' OR a.activation_id IS NULL)
                    AND (r.activation_id>$afterId OR (r.activation_id=$afterId AND r.receipt_sequence>$afterSequence))
                  LIMIT 1);
                """;
            more.Parameters.AddWithValue("$definition", request.Definition.Id);
            more.Parameters.AddWithValue("$version", request.Definition.Version);
            more.Parameters.Add("$definitionChecksum", SqliteType.Blob).Value = request.Definition.Checksum.ToArray();
            more.Parameters.AddWithValue("$format", request.ReceiptRetention.FormatVersion);
            more.Parameters.AddWithValue("$lifetime", request.ReceiptRetention.DuplicateResolutionLifetime.Ticks / TimeSpan.TicksPerMillisecond);
            more.Parameters.AddWithValue("$backup", (int)request.ReceiptRetention.ProtectedBackupCoverage);
            more.Parameters.AddWithValue("$scopeKind", (int)request.Scope.Kind);
            more.Parameters.Add("$scopeDigest", SqliteType.Blob).Value = request.Scope.ProtectedIndexDigest.ToArray();
            more.Parameters.AddWithValue("$afterId", boundary.ActivationId);
            more.Parameters.AddWithValue("$afterSequence", boundary.ReceiptSequence);
            hasMore = Convert.ToInt64(await more.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture) == 1;
        }
        var deleted = new List<SqliteReceiptCompactionCandidate>();
        foreach (SqliteReceiptCompactionCandidate candidate in examined)
        {
            if (request.BackupFloor is
                {
                    Kind: BaseActivationReceiptBackupFloorKind.Checkpoint,
                    Checkpoint: { } checkpoint,
                }
                && candidate.ReceiptSequence > checkpoint.ReceiptSequence) continue;
            if (candidate.DuplicateResolveUntil > request.AcceptedTime.CapturedUtc) continue;
            if (candidate.OperationKind == "activation-yielded-v1")
            {
                BaseActivationYieldReceipt? yielded = JsonSerializer.Deserialize(
                    candidate.Result, HPDBaseJsonSerializerContext.Default.BaseActivationYieldReceipt);
                if (yielded is null || !candidate.RecoverySuppressed
                    && (candidate.ExecutionSliceOrdinal <= yielded.ExecutionSliceOrdinal
                    || candidate.State == BaseActivationState.YieldPending
                        && candidate.Generation == yielded.ResultingGeneration
                        && candidate.YieldCount == yielded.ResultingYieldCount)) continue;
            }
            deleted.Add(candidate);
        }
        string compactionReceiptKey = SqliteActivationReceiptKey(request.Identity);
        foreach (SqliteReceiptCompactionCandidate candidate in deleted)
        {
            BaseActivationCompactedReceiptFact fact = BaseActivationCompactedReceiptFactContract.Create(
                candidate.ReceiptSequence, candidate.ReceiptKey, candidate.AuthorityChecksum,
                candidate.PriorOrderedChecksum, candidate.OrderedChecksum, compactionReceiptKey);
            await using SqliteCommand compact = connection.CreateCommand(); compact.Transaction = transaction;
            compact.CommandText = $"INSERT INTO {_names.ActivationInstanceReceiptCompactionFacts}(receipt_sequence,receipt_key,authority_checksum,prior_ordered_checksum,ordered_checksum,compaction_receipt_key,fact_checksum) VALUES($sequence,$key,$authority,$prior,$ordered,$compaction,$checksum); DELETE FROM {_names.ActivationInstanceReceipts} WHERE receipt_key=$key AND receipt_sequence=$sequence AND authority_checksum=$authority;";
            compact.Parameters.AddWithValue("$sequence", fact.ReceiptSequence); compact.Parameters.AddWithValue("$key", fact.ReceiptKey);
            compact.Parameters.Add("$authority", SqliteType.Blob).Value = fact.ReceiptAuthorityChecksum.ToArray();
            compact.Parameters.Add("$prior", SqliteType.Blob).Value = fact.PriorOrderedChecksum.ToArray();
            compact.Parameters.Add("$ordered", SqliteType.Blob).Value = fact.OrderedChecksum.ToArray();
            compact.Parameters.AddWithValue("$compaction", fact.CompactionReceiptKey);
            compact.Parameters.Add("$checksum", SqliteType.Blob).Value = fact.Checksum.ToArray();
            if (await compact.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
                return ActivationFailure<BaseActivationReceiptCompactionResult>(
                    "base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        }
        int deletedYieldCount = deleted.Count(static candidate => candidate.OperationKind == "activation-yielded-v1");
        if (deletedYieldCount > priorReservation.RetainedUsedSlots)
            return ActivationFailure<BaseActivationReceiptCompactionResult>(
                "base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
        BaseActivationYieldReservationState resultingReservation = deletedYieldCount == 0 ? priorReservation
            : BaseActivationYieldReservationContract.Create(
                checked(priorReservation.Generation + 1), priorReservation.MaximumSlots,
                priorReservation.ReservedUnusedSlots, priorReservation.RetainedUsedSlots - deletedYieldCount);
        if (deletedYieldCount > 0)
            await WriteYieldReservationStateAsync(connection, transaction, resultingReservation, cancellationToken).ConfigureAwait(false);
        BaseActivationInstanceReceiptChainState resultingChain = deleted.Count == 0 ? priorChain
            : BaseActivationInstanceReceiptChainContract.Create(
                priorChain.CurrentSequence, priorChain.OrderedChecksum.AsSpan(), checked(priorChain.Generation + 1));
        if (deleted.Count > 0)
            await WriteInstanceReceiptChainAsync(connection, transaction, resultingChain, cancellationToken).ConfigureAwait(false);
        foreach (string activationId in deleted.Select(static candidate => candidate.ActivationId).Distinct(StringComparer.Ordinal))
        {
            await using SqliteCommand releaseFloor = connection.CreateCommand();
            releaseFloor.Transaction = transaction;
            releaseFloor.CommandText = $"DELETE FROM {_names.ActivationReceiptRecoveryFloors} WHERE activation_id=$id AND NOT EXISTS(SELECT 1 FROM {_names.ActivationInstanceReceipts} WHERE activation_id=$id);";
            releaseFloor.Parameters.AddWithValue("$id", activationId);
            await releaseFloor.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        BaseActivationReceiptCompactionCursor? cursor = examined.Length == 0 ? request.After
            : new BaseActivationReceiptCompactionCursor
            { ActivationId = examined[^1].ActivationId, ReceiptSequence = examined[^1].ReceiptSequence };
        var result = new BaseActivationReceiptCompactionResult
        {
            ExaminedCount = examined.Length, DeletedCount = deleted.Count,
            DeletedYieldReceiptCount = deletedYieldCount,
            Next = hasMore ? cursor : null,
            PriorChain = priorChain, ResultingChain = resultingChain,
            PriorReservation = priorReservation, ResultingReservation = resultingReservation,
            DeletedAuthorityOrderedDigest = SqliteDeletedReceiptAuthorityDigest(
                deleted.Select(static candidate => candidate.AuthorityChecksum)),
            Completed = !hasMore,
            Accounting = ActivationAccounting(candidates.Count, deleted.Count * 32L) with
            {
                Comparisons = candidates.Count, IndexOperations = deleted.Count * 2,
                TransientBytes = candidates.Sum(static candidate => (long)candidate.Result.Length),
            },
            Disposition = BaseMutationRequestDisposition.Committed,
        };
        long resultBytes = JsonSerializer.SerializeToUtf8Bytes(
            result, HPDBaseJsonSerializerContext.Default.BaseActivationReceiptCompactionResult).LongLength;
        if (resultBytes > request.Limits.MaximumResultBytes || resultBytes > request.Limits.MaximumTransientBytes)
            return ActivationFailure<BaseActivationReceiptCompactionResult>(
                "base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await WriteControlReceiptAsync(connection, transaction, request.Identity, "activation-receipts-compacted", result,
            HPDBaseJsonSerializerContext.Default.BaseActivationReceiptCompactionResult, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(result);
    }

    private async ValueTask<bool> CompactionBackupFloorMatchesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BaseActivationReceiptCompactionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.BackupFloor.Kind == BaseActivationReceiptBackupFloorKind.NotApplicable)
            return request.BackupFloor.Checkpoint is null;
        BaseActivationBackupCoverageCheckpoint checkpoint = request.BackupFloor.Checkpoint!;
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT artifact_sha256,application_id,logical_store_id,store_instance_id,restore_epoch,receipt_sequence,receipt_ordered_checksum,checkpoint_generation,committed_at,checkpoint_checksum FROM {_names.ActivationBackupCoverageCheckpoints} WHERE artifact_id=$artifact;";
        command.Parameters.AddWithValue("$artifact", checkpoint.ArtifactId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return false;
        bool exact = ((byte[])reader[0]).AsSpan().SequenceEqual(checkpoint.ArtifactSha256.AsSpan())
            && reader.GetString(1) == checkpoint.ApplicationId && reader.GetString(2) == checkpoint.LogicalStoreId
            && reader.GetString(3) == checkpoint.StoreInstanceId && reader.GetInt64(4) == checkpoint.RestoreEpoch
            && reader.GetInt64(5) == checkpoint.ReceiptSequence
            && ((byte[])reader[6]).AsSpan().SequenceEqual(checkpoint.ReceiptOrderedChecksum.AsSpan())
            && reader.GetInt64(7) == checkpoint.Generation && reader.GetInt64(8) == checkpoint.CommittedAt
            && ((byte[])reader[9]).AsSpan().SequenceEqual(checkpoint.Checksum.AsSpan());
        return exact && !await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool SqliteReservationMatches(
        BaseActivationYieldReservationState expected,
        BaseActivationYieldReservationState actual) =>
        expected.FormatVersion == actual.FormatVersion && expected.Generation == actual.Generation
        && expected.MaximumSlots == actual.MaximumSlots && expected.ReservedUnusedSlots == actual.ReservedUnusedSlots
        && expected.RetainedUsedSlots == actual.RetainedUsedSlots
        && CryptographicOperations.FixedTimeEquals(expected.Checksum.AsSpan(), actual.Checksum.AsSpan());

    private static ImmutableArray<byte> SqliteDeletedReceiptAuthorityDigest(IEnumerable<byte[]> authorities)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.activation.receiptCompaction.deleted.v1\0"u8);
        Span<byte> length = stackalloc byte[4];
        foreach (byte[] authority in authorities)
        {
            BinaryPrimitives.WriteInt32BigEndian(length, authority.Length);
            hash.AppendData(length); hash.AppendData(authority);
        }
        return hash.GetHashAndReset().ToImmutableArray();
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationReceiptResolution>> ResolveReceiptAsync(
        BaseActivationReceiptResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ActivationLimitsValid(request.Limits)
            || !await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationReceiptResolution>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        string receiptKey = SqliteActivationReceiptKey(request.Identity);
        command.CommandText = $"SELECT operation_kind,fingerprint,result_json,result_checksum,authority_checksum,0 FROM {_names.ActivationInstanceReceipts} WHERE receipt_key=$key UNION ALL SELECT operation_kind,fingerprint,result_json,result_checksum,authority_checksum,1 FROM {_names.ActivationControlReceipts} WHERE receipt_key=$key;";
        command.Parameters.AddWithValue("$key", receiptKey);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationReceiptResolution>(
                "base.activation.receiptNotFound", OperationStatus.NotFound, ErrorCategory.NotFound);
        string kind = reader.GetString(0);
        byte[] fingerprint = (byte[])reader[1];
        byte[] bytes = (byte[])reader[2];
        byte[] checksum = (byte[])reader[3];
        byte[] authorityChecksum = (byte[])reader[4];
        bool controlReceipt = reader.GetInt32(5) == 1;
        bool additional = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        await reader.DisposeAsync().ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(fingerprint, request.Identity.Fingerprint.ToArray()))
            return ActivationFailure<BaseActivationReceiptResolution>(
                "base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        if (additional || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), checksum)
            || controlReceipt && !CryptographicOperations.FixedTimeEquals(
                BaseActivationControlReceiptContract.AuthorityChecksum(receiptKey, kind, fingerprint, checksum).AsSpan(), authorityChecksum))
            return ActivationFailure<BaseActivationReceiptResolution>(
                "base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
        if (kind == "activation-claimed")
        {
            BaseActivationClaimResult? stored = JsonSerializer.Deserialize(
                bytes, HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult);
            if (stored is null)
                return ActivationFailure<BaseActivationReceiptResolution>(
                    "base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
            OperationResult<BaseActivationClaimResult> resolved = await ResolveSqliteClaimReplayAsync(
                connection, transaction, OperationResults.Ok(stored), request.AcceptedTime.CapturedUtc,
                cancellationToken).ConfigureAwait(false);
            if (!resolved.IsSuccess() || resolved.Value is null)
                return ActivationFailure<BaseActivationReceiptResolution>(
                    resolved.Error?.Code ?? "base.activation.receiptCorrupt", resolved.Status,
                    resolved.Error?.Category ?? ErrorCategory.Store);
            bytes = JsonSerializer.SerializeToUtf8Bytes(
                resolved.Value, HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult);
        }
        if (bytes.LongLength > request.Limits.MaximumResultBytes
            || bytes.LongLength > request.Limits.MaximumEvidenceBytes
            || bytes.LongLength > request.Limits.MaximumTransientBytes)
            return ActivationFailure<BaseActivationReceiptResolution>(
                "base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        return OperationResults.Ok(new BaseActivationReceiptResolution
        {
            OperationKind = kind,
            Fingerprint = fingerprint.ToImmutableArray(),
            CanonicalResult = bytes.ToImmutableArray(),
            Accounting = new BaseActivationAccounting
            {
                Candidates = 1, Comparisons = 1, IndexOperations = 1, ReadIntervals = 0,
                EvidenceBytes = bytes.LongLength, TransientBytes = bytes.LongLength,
            },
        });
    }

    private async ValueTask<(bool Found, OperationResult<T> Result)> ReadControlReceiptAsync<T>(
        SqliteConnection connection, SqliteTransaction transaction, BaseMutationRequestIdentity identity,
        string kind, JsonTypeInfo<T> typeInfo, Func<T, T> duplicate, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        string key = SqliteActivationReceiptKey(identity);
        command.CommandText = $"SELECT operation_kind,fingerprint,result_json,result_checksum,authority_checksum FROM {_names.ActivationControlReceipts} WHERE receipt_key=$key;";
        command.Parameters.AddWithValue("$key", key);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return (false, default!);
        string storedKind = reader.GetString(0); byte[] fingerprint = (byte[])reader[1]; byte[] bytes = (byte[])reader[2]; byte[] checksum = (byte[])reader[3]; byte[] authority = (byte[])reader[4];
        if (storedKind != kind || !CryptographicOperations.FixedTimeEquals(fingerprint, identity.Fingerprint.ToArray()))
            return (true, ActivationFailure<T>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict));
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), checksum)
            || !CryptographicOperations.FixedTimeEquals(
                BaseActivationControlReceiptContract.AuthorityChecksum(key, storedKind, fingerprint, checksum).AsSpan(), authority))
            return (true, ActivationFailure<T>("base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store));
        T? value = JsonSerializer.Deserialize(bytes, typeInfo);
        return value is null
            ? (true, ActivationFailure<T>("base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store))
            : (true, OperationResults.Ok(duplicate(value)));
    }

    private async ValueTask<(bool Found, OperationResult<T> Result)> ReadInstanceReceiptAsync<T>(
        SqliteConnection connection, SqliteTransaction transaction, BaseMutationRequestIdentity identity,
        string kind, JsonTypeInfo<T> typeInfo, Func<T, T> duplicate, long acceptedAt,
        CancellationToken cancellationToken)
    {
        string key = SqliteActivationReceiptKey(identity);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT operation_kind,activation_id,definition_id,definition_version,definition_checksum,receipt_format_version,receipt_duplicate_lifetime_ms,receipt_backup_coverage,fingerprint,result_json,result_checksum,authority_checksum,committed_at,duplicate_resolve_until,receipt_sequence,prior_ordered_checksum,ordered_checksum FROM {_names.ActivationInstanceReceipts} WHERE receipt_key=$key;";
        command.Parameters.AddWithValue("$key", key);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return (false, default!);
        string storedKind = reader.GetString(0);
        string activationId = reader.GetString(1);
        string definitionId = reader.GetString(2);
        int definitionVersion = reader.GetInt32(3);
        byte[] definitionChecksum = (byte[])reader[4];
        int formatVersion = reader.GetInt32(5);
        long lifetimeMilliseconds = reader.GetInt64(6);
        var backupCoverage = (BaseActivationProtectedBackupCoverage)reader.GetInt32(7);
        byte[] fingerprint = (byte[])reader[8];
        byte[] bytes = (byte[])reader[9];
        byte[] resultChecksum = (byte[])reader[10];
        byte[] authorityChecksum = (byte[])reader[11];
        long committedAt = reader.GetInt64(12);
        long duplicateResolveUntil = reader.GetInt64(13);
        long sequence = reader.GetInt64(14);
        byte[] priorOrderedChecksum = (byte[])reader[15];
        byte[] orderedChecksum = (byte[])reader[16];
        if (storedKind != kind || !CryptographicOperations.FixedTimeEquals(fingerprint, identity.Fingerprint.ToArray()))
            return (true, ActivationFailure<T>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict));
        if (acceptedAt >= duplicateResolveUntil)
            return (true, ActivationFailure<T>("base.activation.receiptNotFound", OperationStatus.NotFound, ErrorCategory.NotFound));
        var definition = new BaseActivationDefinitionKey
        {
            Id = definitionId, Version = definitionVersion, Checksum = definitionChecksum.ToImmutableArray(),
        };
        var retention = new BaseActivationReceiptRetentionPolicy
        {
            FormatVersion = formatVersion,
            DuplicateResolutionLifetime = TimeSpan.FromMilliseconds(lifetimeMilliseconds),
            ProtectedBackupCoverage = backupCoverage,
        };
        ImmutableArray<byte> expectedAuthority = BaseActivationInstanceReceiptChainContract.ReceiptAuthorityChecksum(
            key, storedKind, activationId, definition, retention, fingerprint, resultChecksum,
            committedAt, duplicateResolveUntil, sequence, priorOrderedChecksum);
        ImmutableArray<byte> expectedOrdered;
        try
        {
            expectedOrdered = BaseActivationInstanceReceiptChainContract.Append(
                sequence, priorOrderedChecksum, authorityChecksum, key);
        }
        catch (ArgumentOutOfRangeException)
        {
            return (true, ActivationFailure<T>("base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store));
        }
        if (formatVersion != 1 || !Enum.IsDefined(backupCoverage)
            || lifetimeMilliseconds is < 3_600_000 or > 7_776_000_000
            || committedAt < 0 || duplicateResolveUntil != checked(committedAt + lifetimeMilliseconds)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), resultChecksum)
            || !CryptographicOperations.FixedTimeEquals(expectedAuthority.AsSpan(), authorityChecksum)
            || !CryptographicOperations.FixedTimeEquals(expectedOrdered.AsSpan(), orderedChecksum))
            return (true, ActivationFailure<T>("base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store));
        T? value = JsonSerializer.Deserialize(bytes, typeInfo);
        return value is null
            ? (true, ActivationFailure<T>("base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store))
            : (true, OperationResults.Ok(duplicate(value)));
    }

    private async ValueTask<byte[]> WriteControlReceiptAsync<T>(SqliteConnection connection, SqliteTransaction transaction,
        BaseMutationRequestIdentity identity, string kind, T result, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result, typeInfo);
        byte[] resultChecksum = SHA256.HashData(bytes);
        string key = SqliteActivationReceiptKey(identity);
        byte[] authorityChecksum = BaseActivationControlReceiptContract.AuthorityChecksum(
            key, kind, identity.Fingerprint.ToArray(), resultChecksum).ToArray();
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {_names.ActivationControlReceipts}(receipt_key,operation_kind,fingerprint,result_json,result_checksum,authority_checksum) VALUES($key,$kind,$fingerprint,$result,$checksum,$authority);";
        command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.Add("$fingerprint", SqliteType.Blob).Value = identity.Fingerprint.ToArray(); command.Parameters.Add("$result", SqliteType.Blob).Value = bytes;
        command.Parameters.Add("$checksum", SqliteType.Blob).Value = resultChecksum;
        command.Parameters.Add("$authority", SqliteType.Blob).Value = authorityChecksum;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return authorityChecksum;
    }

    private async ValueTask<byte[]> WriteInstanceReceiptAsync<T>(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BaseMutationRequestIdentity identity,
        string kind,
        SqliteActivationRow activation,
        long committedAt,
        T result,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        string key = SqliteActivationReceiptKey(identity);
        BaseActivationInstanceReceiptChainState priorState = await ReadInstanceReceiptChainAsync(
            connection, transaction, cancellationToken).ConfigureAwait(false);
        long sequence = checked(priorState.CurrentSequence + 1);
        long lifetimeMilliseconds = activation.ReceiptRetention.DuplicateResolutionLifetime.Ticks / TimeSpan.TicksPerMillisecond;
        long duplicateResolveUntil = checked(committedAt + lifetimeMilliseconds);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result, typeInfo);
        byte[] resultChecksum = SHA256.HashData(bytes);
        byte[] authorityChecksum = BaseActivationInstanceReceiptChainContract.ReceiptAuthorityChecksum(
            key, kind, activation.ActivationId,
            new BaseActivationDefinitionKey
            {
                Id = activation.DefinitionId, Version = activation.DefinitionVersion,
                Checksum = activation.DefinitionChecksum.ToImmutableArray(),
            },
            activation.ReceiptRetention, identity.Fingerprint.ToArray(), resultChecksum,
            committedAt, duplicateResolveUntil, sequence, priorState.OrderedChecksum.AsSpan()).ToArray();
        byte[] orderedChecksum = BaseActivationInstanceReceiptChainContract.Append(
            sequence, priorState.OrderedChecksum.AsSpan(), authorityChecksum, key).ToArray();
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {_names.ActivationInstanceReceipts}(receipt_key,operation_kind,activation_id,definition_id,definition_version,definition_checksum,receipt_format_version,receipt_duplicate_lifetime_ms,receipt_backup_coverage,fingerprint,result_json,result_checksum,authority_checksum,committed_at,duplicate_resolve_until,receipt_sequence,prior_ordered_checksum,ordered_checksum) VALUES($key,$kind,$activation,$definition,$version,$definition_checksum,$receipt_format,$receipt_lifetime,$receipt_backup,$fingerprint,$result,$result_checksum,$authority,$committed,$resolve_until,$sequence,$prior,$ordered);";
        command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$activation", activation.ActivationId);
        command.Parameters.AddWithValue("$definition", activation.DefinitionId);
        command.Parameters.AddWithValue("$version", activation.DefinitionVersion);
        command.Parameters.Add("$definition_checksum", SqliteType.Blob).Value = activation.DefinitionChecksum;
        command.Parameters.AddWithValue("$receipt_format", activation.ReceiptRetention.FormatVersion);
        command.Parameters.AddWithValue("$receipt_lifetime", lifetimeMilliseconds);
        command.Parameters.AddWithValue("$receipt_backup", (int)activation.ReceiptRetention.ProtectedBackupCoverage);
        command.Parameters.Add("$fingerprint", SqliteType.Blob).Value = identity.Fingerprint.ToArray(); command.Parameters.Add("$result", SqliteType.Blob).Value = bytes;
        command.Parameters.Add("$result_checksum", SqliteType.Blob).Value = resultChecksum;
        command.Parameters.Add("$authority", SqliteType.Blob).Value = authorityChecksum;
        command.Parameters.AddWithValue("$committed", committedAt);
        command.Parameters.AddWithValue("$resolve_until", duplicateResolveUntil);
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.Add("$prior", SqliteType.Blob).Value = priorState.OrderedChecksum.ToArray();
        command.Parameters.Add("$ordered", SqliteType.Blob).Value = orderedChecksum;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        BaseActivationInstanceReceiptChainState resultingState = BaseActivationInstanceReceiptChainContract.Create(
            sequence, orderedChecksum, checked(priorState.Generation + 1));
        await WriteInstanceReceiptChainAsync(connection, transaction, resultingState, cancellationToken).ConfigureAwait(false);
        return authorityChecksum;
    }

    private async ValueTask<BaseActivationInstanceReceiptChainState> ReadInstanceReceiptChainAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT (SELECT value FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_format'),(SELECT value FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_sequence'),(SELECT value FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_ordered_checksum'),(SELECT value FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_generation'),(SELECT value FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_checksum');";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("base.activation.receiptCorrupt");
        BaseActivationInstanceReceiptChainState value;
        try
        {
            value = new BaseActivationInstanceReceiptChainState
            {
                FormatVersion = int.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                CurrentSequence = long.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                OrderedChecksum = Convert.FromHexString(reader.GetString(2)).ToImmutableArray(),
                Generation = long.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                Checksum = Convert.FromHexString(reader.GetString(4)).ToImmutableArray(),
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCastException)
        {
            throw new InvalidDataException("base.activation.receiptCorrupt", exception);
        }
        if (!BaseActivationInstanceReceiptChainContract.IsValid(value))
            throw new InvalidDataException("base.activation.receiptCorrupt");
        return value;
    }

    private async ValueTask WriteInstanceReceiptChainAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        BaseActivationInstanceReceiptChainState value, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.ProviderState} SET value=CASE key WHEN 'activation_instance_receipt_chain_format' THEN $format WHEN 'activation_instance_receipt_chain_sequence' THEN $sequence WHEN 'activation_instance_receipt_chain_ordered_checksum' THEN $ordered WHEN 'activation_instance_receipt_chain_generation' THEN $generation WHEN 'activation_instance_receipt_chain_checksum' THEN $checksum END WHERE key IN ('activation_instance_receipt_chain_format','activation_instance_receipt_chain_sequence','activation_instance_receipt_chain_ordered_checksum','activation_instance_receipt_chain_generation','activation_instance_receipt_chain_checksum');";
        command.Parameters.AddWithValue("$format", value.FormatVersion.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sequence", value.CurrentSequence.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ordered", Convert.ToHexStringLower(value.OrderedChecksum.AsSpan()));
        command.Parameters.AddWithValue("$generation", value.Generation.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$checksum", Convert.ToHexStringLower(value.Checksum.AsSpan()));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 5)
            throw new InvalidDataException("base.activation.receiptCorrupt");
    }

    private static string SqliteActivationReceiptKey(BaseMutationRequestIdentity identity) =>
        $"{identity.Scope}\n{identity.Operation}\n{identity.IdempotencyKey}";

    private async ValueTask<OperationResult<BaseActivationClaimResult>> ResolveSqliteClaimReplayAsync(
        SqliteConnection connection, SqliteTransaction transaction, OperationResult<BaseActivationClaimResult> replay,
        long acceptedNow, CancellationToken cancellationToken)
    {
        if (!replay.IsSuccess() || replay.Value is not BaseActivationClaimedResult claimed) return replay;
        SqliteActivationRow? row = await ReadActivationAsync(connection, transaction, claimed.Claim.ActivationId, cancellationToken).ConfigureAwait(false);
        if (row is null) return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimSupersededResult(claimed.Claim.ActivationId));
        if (row.State == BaseActivationState.Cancelled)
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimCancelledResult(row.ActivationId));
        if (row.State is BaseActivationState.Succeeded or BaseActivationState.Exhausted or BaseActivationState.OutcomeUnknown or BaseActivationState.Disposed or BaseActivationState.Migrated)
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimTerminalResult(row.ActivationId, row.State));
        if (!SqliteClaimMatches(row, claimed.Claim))
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimSupersededResult(row.ActivationId));
        if (row.LeaseRevision is null || row.LeaseExpiresAt is null || row.LeaseExpiresAt <= acceptedNow)
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimExpiredResult(row.ActivationId));
        byte[] checksum = ActivationHash($"base.activation.lease.v2\0{row.ActivationId}\n{row.LeaseRevision}\n{row.LeaseExpiresAt}");
        return OperationResults.Ok<BaseActivationClaimResult>(claimed with
        {
            Lease = new BaseActivationLeaseObservation
            { LeaseRevision = row.LeaseRevision.Value, LeaseExpiresAt = row.LeaseExpiresAt.Value, Checksum = checksum.ToImmutableArray() },
        });
    }

    private static string SqliteActivationTransitionReceiptKind(BaseActivationTransitionRequest request) => request switch
    {
        BaseActivationCompleteRequest => "activation-completed",
        BaseActivationFailRequest failed when failed.Disposition == BaseActivationFailureDisposition.Retry => "activation-retried",
        BaseActivationFailRequest => "activation-failed-terminal",
        BaseActivationYieldRequest => "activation-yielded-v1",
        BaseActivationCancelRequest => "activation-cancelled",
        BaseActivationBeginEffectRequest => "effect-started",
        BaseActivationEffectHeartbeatRequest => "effect-heartbeat",
        BaseActivationCompleteEffectRequest => "effect-completed",
        BaseActivationRecoverEffectRequest => "effect-outcome-unknown",
        BaseActivationReconcileEffectRequest => "effect-reconciled",
        BaseActivationOperatorRetryRequest => "activation-operator-retried",
        BaseActivationDisposeRequest => "activation-disposed",
        _ => throw new InvalidOperationException("base.activation.invalid"),
    };
    private static byte[] ActivationHash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static OperationResult<T> ActivationFailure<T>(string code, OperationStatus status, ErrorCategory category) => new()
    { Status = status, Error = new BaseError { Code = code, Message = "The activation operation could not be completed.", Category = category } };

    private sealed record SqliteActivationRow(
        string ActivationId, string DefinitionId, int DefinitionVersion, byte[] DefinitionChecksum,
        BaseActivationReceiptRetentionPolicy ReceiptRetention, byte[] CanonicalInput,
        byte[] InputChecksum, BaseSubjectScopeKind ScopeKind, string ScopeValue, byte[] PayloadChecksum,
        BaseActivationState State, long Generation, long RequestedDueAt, long EffectiveDueAt, byte[] ControlChecksum,
        int AttemptNumber, long ExecutionSliceOrdinal, long? AttemptStartedAt, long? SliceStartedAt,
        long YieldCount, long MaximumYields, BaseActivationYieldDisposition? YieldTerminalDisposition, string? YieldTerminalFailureCode,
        long ClaimEpoch, byte[]? ClaimFence, string? ClaimWorker, long? LeaseRevision, long? LeaseExpiresAt,
        string? OccurrenceId, int Priority, byte[]? OverlapKey, BaseScheduleOverlapPolicy OverlapPolicy, bool Eligible)
    {
        internal BaseActivationPayload Payload() => new()
        {
            ActivationId = ActivationId,
            Definition = new BaseActivationDefinitionKey { Id = DefinitionId, Version = DefinitionVersion, Checksum = DefinitionChecksum.ToImmutableArray() },
            ReceiptRetention = ReceiptRetention with { },
            CanonicalInput = CanonicalInput.ToImmutableArray(), InputChecksum = InputChecksum.ToImmutableArray(),
            Scope = new BaseOwnedSubjectScopeEvidence { Kind = ScopeKind, Value = ScopeValue.Length == 0 ? null : ScopeValue },
            OccurrenceId = OccurrenceId, RequestedDueAt = RequestedDueAt, EffectiveDueAt = EffectiveDueAt,
            Checksum = PayloadChecksum.ToImmutableArray(),
        };
    }

    private sealed record SqliteReceiptCompactionCandidate(
        string ReceiptKey,
        string OperationKind,
        string ActivationId,
        byte[] Result,
        byte[] ResultChecksum,
        byte[] AuthorityChecksum,
        long CommittedAt,
        long DuplicateResolveUntil,
        long ReceiptSequence,
        byte[] PriorOrderedChecksum,
        byte[] OrderedChecksum,
        BaseActivationState State,
        long Generation,
        long ExecutionSliceOrdinal,
        long YieldCount,
        bool RecoverySuppressed);

    private sealed record SqlitePruneReceipt(
        string ReceiptKey,
        string OperationKind,
        BaseActivationProtectedBackupCoverage BackupCoverage,
        long DuplicateResolveUntil,
        long ReceiptSequence,
        byte[] AuthorityChecksum,
        byte[] PriorOrderedChecksum,
        byte[] OrderedChecksum);
}
