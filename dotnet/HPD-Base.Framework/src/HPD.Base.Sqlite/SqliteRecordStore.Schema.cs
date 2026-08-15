using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

/// <summary>Represents a sqlite record store.</summary>
public sealed partial class SqliteRecordStore
{
    private const string SchemaProviderId = "sqlite";
    private const string SchemaPlannerVersion = "sqlite-l35-v1";

    /// <inheritdoc />
    public BaseSchemaExecutionCapability SchemaExecution { get; } = new()
    {
        Inspect = true, Prepare = true, Apply = true, History = true,
        Classifications = [BaseSchemaPlanClassification.NoChanges, BaseSchemaPlanClassification.SafeStructural, BaseSchemaPlanClassification.Destructive, BaseSchemaPlanClassification.DataMigrationRequired, BaseSchemaPlanClassification.DriftBlocked]
    };

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSchemaObservedState>> InspectSchemaAsync(BaseSchemaInspectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await using var generationLease = await _schemaGenerationGate.AcquireSharedAsync(cancellationToken).ConfigureAwait(false);
            await EnsureKeepAliveAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            string? storeInstanceId = await ReadStoreInstanceIdAsync(connection, cancellationToken).ConfigureAwait(false);
            if (!await SchemaTableExistsAsync(connection, _names.SchemaBaseline, cancellationToken).ConfigureAwait(false))
                return OperationResults.Ok(MissingBaseline(storeInstanceId));

            BaselineRow? baseline = await ReadBaselineAsync(connection, request.ApplicationId, cancellationToken).ConfigureAwait(false);
            if (baseline is null) return OperationResults.Ok(MissingBaseline(storeInstanceId));
            if (storeInstanceId is null || !string.Equals(storeInstanceId, baseline.StoreInstanceId, StringComparison.Ordinal))
                return OperationResults.Ok(new BaseSchemaObservedState
                {
                    StoreId = _options.StoreId, PersistedStoreInstanceId = storeInstanceId,
                    AcceptedBaselineId = baseline.BaselineId, AcceptedChecksum = baseline.Checksum,
                    Generation = baseline.Generation, Compatibility = BaseSchemaCompatibility.Drifted,
                    Assets = [], MigrationState = BaseSchemaMigrationState.Failed,
                    LastAppliedPlanId = baseline.LastPlanId
                });
            Volatile.Write(ref _schemaGeneration, baseline.Generation);
            BaseSchemaObservedAsset[] assets = await ReadAssetsAsync(connection, request.ApplicationId, cancellationToken).ConfigureAwait(false);
            bool checksumMatches = string.Equals(baseline.Checksum, request.ExpectedLogicalChecksum, StringComparison.Ordinal);
            string[] missing = checksumMatches
                ? await _schema.GetMissingSchemaPartsAsync(connection, cancellationToken).ConfigureAwait(false)
                : await GetAcceptedDriftAsync(connection, assets, cancellationToken).ConfigureAwait(false);
            BaseSchemaCompatibility compatibility = missing.Length != 0
                ? BaseSchemaCompatibility.Drifted
                : checksumMatches
                    ? BaseSchemaCompatibility.Compatible
                    : BaseSchemaCompatibility.MigrationRequired;
            if (missing.Length != 0)
                assets = assets.Select(asset => asset with { State = BaseSchemaAssetState.Drifted }).ToArray();
            return OperationResults.Ok(new BaseSchemaObservedState
            {
                StoreId = _options.StoreId, PersistedStoreInstanceId = baseline.StoreInstanceId,
                AcceptedBaselineId = baseline.BaselineId, AcceptedChecksum = baseline.Checksum, Generation = baseline.Generation,
                Compatibility = compatibility, Assets = assets,
                MigrationState = missing.Length == 0 ? BaseSchemaMigrationState.Ready : BaseSchemaMigrationState.Failed,
                LastAppliedPlanId = baseline.LastPlanId
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (SqliteException ex) { return MapSqlite<BaseSchemaObservedState>(BaseOperationKind.SchemaRead, ex); }
        catch { return SchemaFailure<BaseSchemaObservedState>(BaseSchemaErrorCodes.VerifyFailed, "SQLite schema verification failed."); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSchemaPreparedPlan>> PrepareSchemaPlanAsync(BaseSchemaPreparationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.ObservedState.StoreId, _options.StoreId, StringComparison.Ordinal) || request.ExpectedGeneration != request.ObservedState.Generation)
            return SchemaConflict<BaseSchemaPreparedPlan>(BaseSchemaErrorCodes.PlanStale, "The observed schema generation is stale.");
        string instanceId;
        try
        {
            instanceId = request.ObservedState.PersistedStoreInstanceId
                ?? await GetOrCreateStoreInstanceIdAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (SqliteException ex) { return MapSqlite<BaseSchemaPreparedPlan>(BaseOperationKind.SchemaWrite, ex); }
        catch { return SchemaFailure<BaseSchemaPreparedPlan>(BaseSchemaErrorCodes.MigrationFailed, "SQLite schema planning failed."); }
        SchemaAssetValue[] assets = CurrentSchemaAssets()
            .Concat(request.LogicalDelta.Where(static operation => operation.LogicalId.StartsWith("q:", StringComparison.Ordinal) && operation.Kind != BaseSchemaOperationKind.RemoveRead)
                .Select(static operation => new SchemaAssetValue(operation.LogicalId, "registered")))
            .GroupBy(static asset => asset.LogicalId, StringComparer.Ordinal).Select(static group => group.Last())
            .OrderBy(static asset => asset.LogicalId, StringComparer.Ordinal).ToArray();
        string[] statements;
        BaseSchemaPlanClassification? refined = null;
        try
        {
            if (request.Classification == BaseSchemaPlanClassification.Destructive &&
                await HasDestructiveDataAsync(request, cancellationToken).ConfigureAwait(false))
            {
                refined = BaseSchemaPlanClassification.DataMigrationRequired;
                statements = [];
            }
            else statements = PrepareExecutionStatements(request);
        }
        catch (OperationCanceledException) { throw; }
        catch { return SchemaValidation<BaseSchemaPreparedPlan>(BaseSchemaErrorCodes.MigrationUnsupported, "SQLite cannot lower the requested schema change safely."); }
        CollectionMapping[] mappings = _physical.Collections.Select(static collection => new CollectionMapping(collection.Definition.Id, collection.Table)).ToArray();
        byte[] artifact = EncodeProviderArtifact(request.ApplicationId, instanceId, request.ExpectedGeneration, request.BaselineChecksum, request.TargetChecksum, assets, statements, mappings);
        var summaries = assets.Select(asset => new BaseSchemaSafePhysicalSummary { LogicalId = asset.LogicalId, Summary = "Prepared provider asset." }).ToArray();
        return OperationResults.Ok(new BaseSchemaPreparedPlan
        {
            RefinedClassification = refined,
            SafePhysicalSummary = summaries, ProviderId = SchemaProviderId, ProviderVersion = _options.StoreVersion,
            PlannerVersion = SchemaPlannerVersion, PersistedStoreInstanceId = instanceId,
            ProviderApplyArtifact = artifact, ProviderApplyArtifactDigest = Digest(artifact)
        });
    }

    private async ValueTask<bool> HasDestructiveDataAsync(BaseSchemaPreparationRequest request, CancellationToken cancellationToken)
    {
        await EnsureKeepAliveAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (BaseSchemaLogicalOperation operation in request.LogicalDelta.Where(static operation => operation.Destructive))
        {
            string[] parts = operation.LogicalId.Split(':');
            string? sql = operation.Kind switch
            {
                BaseSchemaOperationKind.RemoveCollection => $"SELECT EXISTS(SELECT 1 FROM {NativeSchemaName("b_c_", parts[1])} LIMIT 1);",
                BaseSchemaOperationKind.RemoveField => RemovedFieldHasValueSql(request, operation, parts),
                BaseSchemaOperationKind.RemoveRelation => RemovedRelationHasValueSql(request, operation, parts),
                _ => null,
            };
            if (sql is null) continue;
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandTimeout = TimeoutSeconds();
            command.CommandText = sql;
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0)
                return true;
        }
        return false;
    }

    private static string RemovedFieldHasValueSql(BaseSchemaPreparationRequest request, BaseSchemaLogicalOperation operation, string[] parts)
    {
        BaseSchemaObservedAsset prior = request.ObservedState.Assets.Single(asset => asset.LogicalId == operation.LogicalId);
        string[] summary = (prior.SafeSummary ?? "").Split('\u001f');
        bool hadPresence = summary.Length == 4 && !(summary[2] == "1" && summary[3] == "0");
        string table = NativeSchemaName("b_c_", parts[1]);
        string predicate = hadPresence ? NativeSchemaName("p_", parts[2]) + " = 1" : "1 = 1";
        return $"SELECT EXISTS(SELECT 1 FROM {table} WHERE {predicate} LIMIT 1);";
    }

    private static string? RemovedRelationHasValueSql(BaseSchemaPreparationRequest request, BaseSchemaLogicalOperation operation, string[] parts)
    {
        BaseSchemaObservedAsset prior = request.ObservedState.Assets.Single(asset => asset.LogicalId == operation.LogicalId);
        string[] summary = (prior.SafeSummary ?? "").Split('\u001f');
        bool hasTable = summary.Length >= 6 && summary[4] == nameof(BaseRelationOwningSide.Source) && summary[5] == nameof(BaseRelationMultiplicity.Many);
        return hasTable ? $"SELECT EXISTS(SELECT 1 FROM {NativeSchemaName("b_r_", parts[1])} LIMIT 1);" : null;
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSchemaApplyResult>> ApplySchemaAsync(BaseSchemaProviderApplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        BaseSchemaProviderVerifiedEnvelope? envelope;
        ProviderArtifact artifact;
        try
        {
            envelope = JsonSerializer.Deserialize(request.VerifiedPlanEnvelope, HPDBaseJsonSerializerContext.Default.BaseSchemaProviderVerifiedEnvelope);
            artifact = DecodeProviderArtifact(request.ProviderApplyArtifact);
        }
        catch { return SchemaValidation<BaseSchemaApplyResult>(BaseSchemaErrorCodes.PlanInvalid, "The verified SQLite schema plan is invalid."); }
        if (envelope is null || envelope.StoreId != _options.StoreId || envelope.ProviderId != SchemaProviderId ||
            envelope.ProviderVersion != _options.StoreVersion || envelope.PlannerVersion != SchemaPlannerVersion ||
            envelope.PersistedStoreInstanceId != artifact.StoreInstanceId || envelope.ApplicationId != artifact.ApplicationId ||
            envelope.ProviderApplyArtifactDigest != Digest(request.ProviderApplyArtifact) || artifact.ExpectedGeneration != request.ExpectedGeneration ||
            artifact.BaselineChecksum != request.ExpectedBaselineChecksum || artifact.TargetChecksum != request.ExpectedTargetChecksum)
            return SchemaValidation<BaseSchemaApplyResult>(BaseSchemaErrorCodes.PlanInvalid, "The verified SQLite schema plan bindings are invalid.");

        using var leaseLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        leaseLifetime.CancelAfter(request.LeaseTimeout);
        IAsyncDisposable generationLease;
        try
        {
            generationLease = await _schemaGenerationGate.AcquireExclusiveAsync(leaseLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SchemaCapability<BaseSchemaApplyResult>(BaseSchemaErrorCodes.MigrationBusy, "SQLite schema migration ownership is busy.");
        }
        await using (generationLease.ConfigureAwait(false))
        {
        await EnsureKeepAliveAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        bool transaction = false;
        try
        {
            await ExecuteSchemaCommandAsync(connection, "BEGIN IMMEDIATE;", request.LeaseTimeout, cancellationToken).ConfigureAwait(false);
            transaction = true;
            string? persistedStoreInstanceId = await ReadStoreInstanceIdAsync(connection, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(persistedStoreInstanceId, artifact.StoreInstanceId, StringComparison.Ordinal))
            {
                await ExecuteSchemaCommandAsync(connection, "ROLLBACK;", request.CommitCompletionTimeout, CancellationToken.None).ConfigureAwait(false);
                transaction = false;
                return SchemaConflict<BaseSchemaApplyResult>(BaseSchemaErrorCodes.PlanStale, "The SQLite schema plan belongs to a different physical store.");
            }
            bool hasBaseline = await SchemaTableExistsAsync(connection, _names.SchemaBaseline, cancellationToken).ConfigureAwait(false);
            BaselineRow? current = hasBaseline ? await ReadBaselineAsync(connection, artifact.ApplicationId, cancellationToken).ConfigureAwait(false) : null;
            if ((current?.Generation ?? 0) != request.ExpectedGeneration || current?.Checksum != request.ExpectedBaselineChecksum ||
                (current is not null && current.StoreInstanceId != artifact.StoreInstanceId))
            {
                await ExecuteSchemaCommandAsync(connection, "ROLLBACK;", request.CommitCompletionTimeout, CancellationToken.None).ConfigureAwait(false);
                transaction = false;
                return SchemaConflict<BaseSchemaApplyResult>(BaseSchemaErrorCodes.PlanStale, "The SQLite schema plan is stale.");
            }

            if (envelope.Classification == BaseSchemaPlanClassification.NoChanges && current is not null)
            {
                await ExecuteSchemaCommandAsync(connection, "COMMIT;", request.CommitCompletionTimeout, CancellationToken.None).ConfigureAwait(false);
                transaction = false;
                return OperationResults.Ok(new BaseSchemaApplyResult { Outcome = BaseSchemaApplyOutcome.NoChanges, Generation = current.Generation, BaselineId = current.BaselineId, Checksum = current.Checksum, State = BaseSchemaMigrationState.Ready });
            }

            foreach (string statement in artifact.ExecutionStatements)
                await ExecuteSchemaCommandAsync(connection, statement, request.ApplyTimeout, cancellationToken).ConfigureAwait(false);
            if (!await AcquirePersistedSchemaLeaseAsync(connection, artifact.ApplicationId, request.ExpectedGeneration, envelope.PlanId, cancellationToken).ConfigureAwait(false))
            {
                await ExecuteSchemaCommandAsync(connection, "ROLLBACK;", request.CommitCompletionTimeout, CancellationToken.None).ConfigureAwait(false);
                transaction = false;
                return SchemaCapability<BaseSchemaApplyResult>(BaseSchemaErrorCodes.MigrationBusy, "SQLite schema migration ownership is busy.");
            }
            await ApplyCollectionMappingsAsync(connection, artifact.CollectionMappings, cancellationToken).ConfigureAwait(false);
            await InitializeSubjectContractsForSchemaApplyAsync(connection, cancellationToken).ConfigureAwait(false);
            await InitializeModuleMutationDefinitionsForSchemaApplyAsync(connection, cancellationToken).ConfigureAwait(false);
            string[] missing = await _schema.GetMissingSchemaPartsAsync(connection, cancellationToken).ConfigureAwait(false);
            if (missing.Length != 0)
                throw new InvalidOperationException("The authenticated SQLite schema plan did not produce the required physical state.");

            long generation = checked(request.ExpectedGeneration + 1);
            string now = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
            await UpsertBaselineAsync(connection, artifact, envelope, generation, now, cancellationToken).ConfigureAwait(false);
            await ReplaceAssetsAsync(connection, artifact.ApplicationId, artifact.Assets, cancellationToken).ConfigureAwait(false);
            await InsertHistoryAsync(connection, artifact.ApplicationId, envelope, generation, artifact.TargetChecksum, now, cancellationToken).ConfigureAwait(false);
            await PublishPersistedSchemaLeaseAsync(connection, artifact.ApplicationId, request.ExpectedGeneration, generation, envelope.PlanId, cancellationToken).ConfigureAwait(false);
            await ExecuteSchemaCommandAsync(connection, "COMMIT;", request.CommitCompletionTimeout, CancellationToken.None).ConfigureAwait(false);
            transaction = false;
            Volatile.Write(ref _schemaGeneration, generation);
            return OperationResults.Ok(new BaseSchemaApplyResult { Outcome = BaseSchemaApplyOutcome.Applied, Generation = generation, BaselineId = envelope.TargetBaselineId, Checksum = artifact.TargetChecksum, State = BaseSchemaMigrationState.Ready });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (transaction && !await TryRollbackSchemaAsync(connection, request.CommitCompletionTimeout).ConfigureAwait(false))
                return SchemaFailure<BaseSchemaApplyResult>(BaseSchemaErrorCodes.MigrationIndeterminate, "SQLite schema migration completion is indeterminate.");
            throw;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            if (transaction && !await TryRollbackSchemaAsync(connection, request.CommitCompletionTimeout).ConfigureAwait(false))
                return SchemaFailure<BaseSchemaApplyResult>(BaseSchemaErrorCodes.MigrationIndeterminate, "SQLite schema migration completion is indeterminate.");
            return SchemaCapability<BaseSchemaApplyResult>(BaseSchemaErrorCodes.MigrationBusy, "SQLite schema migration ownership is busy.");
        }
        catch
        {
            if (transaction && !await TryRollbackSchemaAsync(connection, request.CommitCompletionTimeout).ConfigureAwait(false))
                return SchemaFailure<BaseSchemaApplyResult>(BaseSchemaErrorCodes.MigrationIndeterminate, "SQLite schema migration completion is indeterminate.");
            return SchemaFailure<BaseSchemaApplyResult>(BaseSchemaErrorCodes.MigrationRolledBack, "SQLite schema migration failed and rollback was confirmed.");
        }
        }
    }

    private async ValueTask InitializeSubjectContractsForSchemaApplyAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var installed = _options.ExportedSubjects
            .Select(static subject => (subject.Id, subject.Version))
            .ToHashSet();
        var stale = new List<(string Id, int Version)>();
        await using (SqliteCommand existingContracts = connection.CreateCommand())
        {
            existingContracts.CommandTimeout = TimeoutSeconds();
            existingContracts.CommandText = $"SELECT contract_id,contract_version FROM {_names.SubjectContracts} ORDER BY contract_id COLLATE BINARY,contract_version;";
            await using SqliteDataReader reader = await existingContracts.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = (reader.GetString(0), reader.GetInt32(1));
                if (!installed.Contains(key)) stale.Add(key);
            }
        }
        foreach ((string id, int version) in stale)
        {
            await using SqliteCommand removeLifetimes = connection.CreateCommand();
            removeLifetimes.CommandTimeout = TimeoutSeconds();
            removeLifetimes.CommandText = $"DELETE FROM {_names.SubjectLifetimes} WHERE contract_id=$id AND contract_version=$version;";
            removeLifetimes.Parameters.AddWithValue("$id", id);
            removeLifetimes.Parameters.AddWithValue("$version", version);
            await removeLifetimes.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await using SqliteCommand removeContract = connection.CreateCommand();
            removeContract.CommandTimeout = TimeoutSeconds();
            removeContract.CommandText = $"DELETE FROM {_names.SubjectContracts} WHERE contract_id=$id AND contract_version=$version;";
            removeContract.Parameters.AddWithValue("$id", id);
            removeContract.Parameters.AddWithValue("$version", version);
            if (await removeContract.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
        }

        foreach (BaseExportedSubjectDefinition subject in _options.ExportedSubjects
            .OrderBy(static value => value.Id, StringComparer.Ordinal)
            .ThenBy(static value => value.Version))
        {
            await using SqliteCommand current = connection.CreateCommand();
            current.CommandTimeout = TimeoutSeconds();
            current.CommandText = $"SELECT contract_checksum FROM {_names.SubjectContracts} WHERE contract_id=$id AND contract_version=$version;";
            current.Parameters.AddWithValue("$id", subject.Id);
            current.Parameters.AddWithValue("$version", subject.Version);
            object? existing = await current.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!string.Equals(Convert.ToString(existing, CultureInfo.InvariantCulture),
                    subject.ValidationPlan.ContractChecksum, StringComparison.Ordinal))
                    throw new InvalidOperationException(BaseSubjectErrorCodes.RegistrationConflict);
                continue;
            }

            long restoreEpoch;
            await using (SqliteCommand restore = connection.CreateCommand())
            {
                restore.CommandTimeout = TimeoutSeconds();
                restore.CommandText = $"SELECT COALESCE(CAST(value AS INTEGER),0) FROM {_names.ProviderState} WHERE key='restore_epoch';";
                restoreEpoch = Convert.ToInt64(await restore.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            BaseSubjectAuthorityEpoch epoch = BaseSubjectAuthorityEpoch.Create();
            long position;
            await using (SqliteCommand publication = connection.CreateCommand())
            {
                publication.CommandTimeout = TimeoutSeconds();
                publication.CommandText = $"INSERT INTO {_names.MutationJournal}(entry_kind,subject_contract_id,subject_contract_version,subject_previous_generation,subject_published_generation,subject_restore_epoch,subject_publication_kind) VALUES(1,$id,$version,0,1,$restore,0) RETURNING position;";
                publication.Parameters.AddWithValue("$id", subject.Id);
                publication.Parameters.AddWithValue("$version", subject.Version);
                publication.Parameters.AddWithValue("$restore", restoreEpoch);
                position = Convert.ToInt64(await publication.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            string digest = BaseSubjectPublicationIntegrity.Compute(
                subject.Id, subject.Version, subject.ValidationPlan.ContractChecksum,
                0, 1, restoreEpoch, BaseSubjectAuthorityPublicationKind.InitialInstallation,
                new BaseMutationJournalPosition(position), epoch);
            await using SqliteCommand insert = connection.CreateCommand();
            insert.CommandTimeout = TimeoutSeconds();
            insert.CommandText = $"INSERT INTO {_names.SubjectContracts}(contract_id,contract_version,contract_checksum,authority_epoch,restore_epoch,state_generation,publication_previous_generation,publication_kind,publication_position,publication_digest) VALUES($id,$version,$checksum,$epoch,$restore,1,0,0,$position,$digest);";
            insert.Parameters.AddWithValue("$id", subject.Id);
            insert.Parameters.AddWithValue("$version", subject.Version);
            insert.Parameters.AddWithValue("$checksum", subject.ValidationPlan.ContractChecksum);
            insert.Parameters.Add("$epoch", SqliteType.Blob).Value = epoch.ToArray();
            insert.Parameters.AddWithValue("$restore", restoreEpoch);
            insert.Parameters.AddWithValue("$position", position);
            insert.Parameters.AddWithValue("$digest", digest);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask InitializeModuleMutationDefinitionsForSchemaApplyAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var installedOperations = _options.ModuleMutations.Select(static value => (value.Id, value.Version)).ToHashSet();
        await using (SqliteCommand existing = connection.CreateCommand())
        {
            existing.CommandTimeout = TimeoutSeconds();
            existing.CommandText = $"SELECT operation_id,operation_version FROM {_names.ModuleMutationDefinitions} ORDER BY operation_id COLLATE BINARY,operation_version;";
            await using SqliteDataReader reader = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var retired = new List<(string Id, int Version)>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                if (!installedOperations.Contains((reader.GetString(0), reader.GetInt32(1))))
                    retired.Add((reader.GetString(0), reader.GetInt32(1)));
            await reader.DisposeAsync().ConfigureAwait(false);
            foreach ((string id, int version) in retired)
            {
                await using var retained = connection.CreateCommand();
                retained.CommandTimeout = TimeoutSeconds();
                retained.CommandText = $"SELECT 1 FROM {_names.OperationReceipts} WHERE operation=$operation AND expires_at>$now LIMIT 1;";
                retained.Parameters.AddWithValue("$operation", id);
                retained.Parameters.AddWithValue("$now", _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
                if (await retained.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                    throw new InvalidOperationException("base.moduleMutation.removalRequired");
                await using var remove = connection.CreateCommand();
                remove.CommandTimeout = TimeoutSeconds();
                remove.CommandText = $"DELETE FROM {_names.ModuleMutationDefinitions} WHERE operation_id=$id AND operation_version=$version;";
                remove.Parameters.AddWithValue("$id", id); remove.Parameters.AddWithValue("$version", version);
                if (await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("base.moduleMutation.schemaDrift");
            }
        }
        foreach (BaseRegisteredModuleMutationDefinition operation in _options.ModuleMutations
            .OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version))
        {
            string checksum = Convert.ToHexStringLower(operation.Checksum.ToArray());
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandTimeout = TimeoutSeconds();
            command.CommandText = $"INSERT INTO {_names.ModuleMutationDefinitions}(operation_id,operation_version,owning_module_id,operation_checksum) VALUES($id,$version,$owner,$checksum) ON CONFLICT(operation_id,operation_version) DO UPDATE SET owning_module_id=excluded.owning_module_id,operation_checksum=excluded.operation_checksum WHERE owning_module_id=excluded.owning_module_id AND operation_checksum=excluded.operation_checksum;";
            command.Parameters.AddWithValue("$id", operation.Id); command.Parameters.AddWithValue("$version", operation.Version);
            command.Parameters.AddWithValue("$owner", operation.OwningModuleId); command.Parameters.AddWithValue("$checksum", checksum);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("base.moduleMutation.schemaDrift");
        }

        var installedCells = _options.ModuleGenerationCells.Select(static value => (value.Id, value.Version)).ToHashSet();
        await using (SqliteCommand existing = connection.CreateCommand())
        {
            existing.CommandTimeout = TimeoutSeconds();
            existing.CommandText = $"SELECT cell_id,cell_version FROM {_names.ModuleGenerationDefinitions} ORDER BY cell_id COLLATE BINARY,cell_version;";
            await using SqliteDataReader reader = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var retired = new List<(string Id, int Version)>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                if (!installedCells.Contains((reader.GetString(0), reader.GetInt32(1))))
                    retired.Add((reader.GetString(0), reader.GetInt32(1)));
            await reader.DisposeAsync().ConfigureAwait(false);
            foreach ((string id, int version) in retired)
            {
                await using var retained = connection.CreateCommand();
                retained.CommandTimeout = TimeoutSeconds();
                retained.CommandText = $"SELECT 1 FROM {_names.ModuleGenerations} WHERE cell_id=$id AND cell_version=$version LIMIT 1;";
                retained.Parameters.AddWithValue("$id", id); retained.Parameters.AddWithValue("$version", version);
                if (await retained.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                    throw new InvalidOperationException("base.moduleMutation.removalRequired");
                await using var remove = connection.CreateCommand();
                remove.CommandTimeout = TimeoutSeconds();
                remove.CommandText = $"DELETE FROM {_names.ModuleGenerationDefinitions} WHERE cell_id=$id AND cell_version=$version;";
                remove.Parameters.AddWithValue("$id", id); remove.Parameters.AddWithValue("$version", version);
                if (await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("base.moduleMutation.schemaDrift");
            }
        }
        foreach (BaseModuleGenerationCellDefinition cell in _options.ModuleGenerationCells
            .OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version))
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandTimeout = TimeoutSeconds();
            command.CommandText = $"INSERT INTO {_names.ModuleGenerationDefinitions}(cell_id,cell_version,owning_module_id,scope_kind,maximum_key_bytes,maximum_cells,definition_checksum) VALUES($id,$version,$owner,$scope,$keyBytes,$cells,$checksum) ON CONFLICT(cell_id,cell_version) DO UPDATE SET owning_module_id=excluded.owning_module_id,scope_kind=excluded.scope_kind,maximum_key_bytes=excluded.maximum_key_bytes,maximum_cells=excluded.maximum_cells,definition_checksum=excluded.definition_checksum WHERE owning_module_id=excluded.owning_module_id AND scope_kind=excluded.scope_kind AND maximum_key_bytes=excluded.maximum_key_bytes AND maximum_cells=excluded.maximum_cells AND definition_checksum=excluded.definition_checksum;";
            command.Parameters.AddWithValue("$id", cell.Id); command.Parameters.AddWithValue("$version", cell.Version);
            command.Parameters.AddWithValue("$owner", cell.OwningModuleId); command.Parameters.AddWithValue("$scope", (int)cell.Scope);
            command.Parameters.AddWithValue("$keyBytes", cell.MaximumKeyUtf8Bytes); command.Parameters.AddWithValue("$cells", cell.MaximumCellsPerOperation);
            command.Parameters.AddWithValue("$checksum", BaseModuleMutationContract.ComputeCellChecksum(cell));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("base.moduleMutation.schemaDrift");
        }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSchemaHistoryPage>> ReadSchemaHistoryAsync(BaseSchemaHistoryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit < 1 || request.Limit > 1_000) return SchemaValidation<BaseSchemaHistoryPage>(BaseSchemaErrorCodes.Invalid, "The schema history limit is invalid.");
        try
        {
            await using var generationLease = await _schemaGenerationGate.AcquireSharedAsync(cancellationToken).ConfigureAwait(false);
            await EnsureKeepAliveAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await SchemaTableExistsAsync(connection, _names.SchemaHistory, cancellationToken).ConfigureAwait(false))
                return OperationResults.Ok(new BaseSchemaHistoryPage { Items = [] });
            await using var command = connection.CreateCommand();
            command.CommandTimeout = TimeoutSeconds();
            command.CommandText = $"SELECT generation, baseline_id, checksum, plan_id, classification, outcome, provider_version, applied_at, structural_verification, external_data_migration, semantic_conversion, external_attestation_id, external_signer_id FROM {_names.SchemaHistory} WHERE ($before IS NULL OR generation < $before) ORDER BY generation DESC LIMIT $limit;";
            command.Parameters.AddWithValue("$before", request.BeforeGeneration is null ? DBNull.Value : request.BeforeGeneration.Value);
            command.Parameters.AddWithValue("$limit", request.Limit);
            var items = new List<BaseSchemaHistoryEntry>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(new BaseSchemaHistoryEntry
            {
                Generation = reader.GetInt64(0), BaselineId = reader.GetString(1), Checksum = reader.GetString(2), PlanId = reader.GetString(3), Classification = (BaseSchemaPlanClassification)reader.GetInt32(4), Outcome = (BaseSchemaApplyOutcome)reader.GetInt32(5), ProviderVersion = reader.GetString(6),
                AppliedAt = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), StructuralVerification = (BaseSchemaStructuralVerification)reader.GetInt32(8), ExternalDataMigration = (BaseExternalDataMigrationVerification)reader.GetInt32(9), SemanticConversion = (BaseSemanticConversionVerification)reader.GetInt32(10),
                ExternalAttestationId = reader.IsDBNull(11) ? null : reader.GetString(11), ExternalSignerId = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
            return OperationResults.Ok(new BaseSchemaHistoryPage { Items = items.ToArray(), BeforeGeneration = items.Count == request.Limit ? items[^1].Generation : null });
        }
        catch (OperationCanceledException) { throw; }
        catch { return SchemaFailure<BaseSchemaHistoryPage>(BaseSchemaErrorCodes.VerifyFailed, "SQLite schema history could not be read."); }
    }

    private BaseSchemaObservedState MissingBaseline(string? storeInstanceId) => new()
    {
        StoreId = _options.StoreId, PersistedStoreInstanceId = storeInstanceId,
        Generation = 0, Compatibility = BaseSchemaCompatibility.MigrationRequired,
        Assets = [], MigrationState = BaseSchemaMigrationState.None
    };

    private async ValueTask<string?> ReadStoreInstanceIdAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!await SchemaTableExistsAsync(connection, _names.SchemaIdentity, cancellationToken).ConfigureAwait(false)) return null;
        await using var command = connection.CreateCommand();
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT store_instance_id FROM {_names.SchemaIdentity} WHERE singleton = 1;";
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    private async ValueTask<string> GetOrCreateStoreInstanceIdAsync(CancellationToken cancellationToken)
    {
        await EnsureKeepAliveAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        bool transaction = false;
        try
        {
            await ExecuteStoreIdentityCommandAsync(connection, "BEGIN IMMEDIATE;", cancellationToken).ConfigureAwait(false);
            transaction = true;
            await using (var create = connection.CreateCommand())
            {
                create.CommandTimeout = TimeoutSeconds();
                create.CommandText = $"CREATE TABLE IF NOT EXISTS {_names.SchemaIdentity} (singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1), store_instance_id TEXT NOT NULL);";
                await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            string candidate = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandTimeout = TimeoutSeconds();
                insert.CommandText = $"INSERT OR IGNORE INTO {_names.SchemaIdentity}(singleton, store_instance_id) VALUES (1, $instance);";
                insert.Parameters.AddWithValue("$instance", candidate);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            string instanceId = await ReadStoreInstanceIdAsync(connection, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("SQLite did not persist its physical store identity.");
            await ExecuteStoreIdentityCommandAsync(connection, "COMMIT;", CancellationToken.None).ConfigureAwait(false);
            transaction = false;
            return instanceId;
        }
        catch
        {
            if (transaction)
            {
                try { await ExecuteStoreIdentityCommandAsync(connection, "ROLLBACK;", CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
            throw;
        }
    }

    private async ValueTask ExecuteStoreIdentityCommandAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> SchemaTableExistsAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;"; command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0;
    }

    private async ValueTask<string[]> GetAcceptedDriftAsync(SqliteConnection connection, BaseSchemaObservedAsset[] assets, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (string core in new[] { _names.Collections, _names.ProviderState, _names.MutationJournal, _names.OperationReceipts, _names.SchemaIdentity, _names.SchemaBaseline, _names.SchemaAssets, _names.SchemaHistory, _names.SchemaLease })
            if (!await SchemaObjectExistsAsync(connection, "table", core, cancellationToken).ConfigureAwait(false)) missing.Add("table:" + core);
        foreach (BaseSchemaObservedAsset asset in assets)
        {
            string[] id = asset.LogicalId.Split(':');
            if (id[0] == "c" && !await SchemaObjectExistsAsync(connection, "table", NativeSchemaName("b_c_", id[1]), cancellationToken).ConfigureAwait(false)) missing.Add("asset:" + asset.LogicalId);
            else if (id[0] == "f")
            {
                string table = NativeSchemaName("b_c_", id[1]);
                if (!await SchemaColumnExistsAsync(connection, table, NativeSchemaName("f_", id[2]), cancellationToken).ConfigureAwait(false)) missing.Add("asset:" + asset.LogicalId);
                string[] summary = (asset.SafeSummary ?? "").Split('\u001f'); bool presence = summary.Length == 4 && !(summary[2] == "1" && summary[3] == "0");
                if (presence && !await SchemaColumnExistsAsync(connection, table, NativeSchemaName("p_", id[2]), cancellationToken).ConfigureAwait(false)) missing.Add("asset:" + asset.LogicalId + ":presence");
            }
            else if (id[0] == "i" && !await SchemaObjectExistsAsync(connection, "index", NativeSchemaName("b_i_", id[2]), cancellationToken).ConfigureAwait(false)) missing.Add("asset:" + asset.LogicalId);
            else if (id[0] == "r")
            {
                string[] summary = (asset.SafeSummary ?? "").Split('\u001f');
                bool hasTable = summary.Length >= 6 && summary[4] == nameof(BaseRelationOwningSide.Source) && summary[5] == nameof(BaseRelationMultiplicity.Many);
                if (hasTable && !await SchemaObjectExistsAsync(connection, "table", NativeSchemaName("b_r_", id[1]), cancellationToken).ConfigureAwait(false)) missing.Add("asset:" + asset.LogicalId);
            }
        }
        return missing.Distinct(StringComparer.Ordinal).ToArray();
    }

    private async ValueTask<bool> SchemaObjectExistsAsync(SqliteConnection connection, string type, string name, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds(); command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type=$type AND name=$name;";
        command.Parameters.AddWithValue("$type", type); command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0;
    }

    private async ValueTask<bool> SchemaColumnExistsAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds(); command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) if (reader.GetString(1) == column) return true;
        return false;
    }

    private async ValueTask<BaselineRow?> ReadBaselineAsync(SqliteConnection connection, string applicationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT store_instance_id, baseline_id, checksum, generation, last_plan_id FROM {_names.SchemaBaseline} WHERE application_id = $application;";
        command.Parameters.AddWithValue("$application", applicationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new BaselineRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4)) : null;
    }

    private async ValueTask<BaseSchemaObservedAsset[]> ReadAssetsAsync(SqliteConnection connection, string applicationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT logical_id, safe_summary, state FROM {_names.SchemaAssets} WHERE application_id = $application ORDER BY logical_id;";
        command.Parameters.AddWithValue("$application", applicationId); var result = new List<BaseSchemaObservedAsset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(new BaseSchemaObservedAsset { LogicalId = reader.GetString(0), SafeSummary = reader.GetString(1), State = (BaseSchemaAssetState)reader.GetInt32(2) });
        return result.ToArray();
    }

    private SchemaAssetValue[] CurrentSchemaAssets()
    {
        var assets = new List<SchemaAssetValue>();
        foreach (CollectionDefinition collection in _options.Collections)
        {
            assets.Add(new SchemaAssetValue("c:" + collection.Id, collection.Name));
            foreach (FieldDefinition field in collection.Fields ?? []) assets.Add(new SchemaAssetValue($"f:{collection.Id}:{field.Id}", string.Join('\u001f', field.WireName, field.Type, field.Required ? "1" : "0", field.Nullable ? "1" : "0")));
            foreach (RelationDefinition relation in (collection.Fields ?? []).Select(static field => field.Relation).Where(static relation => relation is not null).Cast<RelationDefinition>())
                assets.Add(new SchemaAssetValue("r:" + relation.Id, string.Join('\u001f', relation.SourceCollectionId, relation.SourceFieldId, relation.TargetCollectionId, relation.TargetFieldId, relation.OwningSide, relation.LocalMultiplicity, relation.InverseMultiplicity, relation.Required, relation.Ordered, relation.DeleteBehavior)));
            foreach (IndexDefinition index in collection.Indexes ?? []) assets.Add(new SchemaAssetValue($"i:{collection.Id}:{index.Id}", string.Join('\u001f', index.Unique ? "1" : "0", string.Join('\u001e', (index.Parts ?? []).Select(static part => part.FieldId)))));
        }
        return assets.OrderBy(static asset => asset.LogicalId, StringComparer.Ordinal).ToArray();
    }

    private async ValueTask UpsertBaselineAsync(SqliteConnection connection, ProviderArtifact artifact, BaseSchemaProviderVerifiedEnvelope envelope, long generation, string now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"INSERT INTO {_names.SchemaBaseline}(application_id, store_instance_id, baseline_id, checksum, generation, last_plan_id, applied_at) VALUES ($app,$instance,$baseline,$checksum,$generation,$plan,$time) ON CONFLICT(application_id) DO UPDATE SET store_instance_id=excluded.store_instance_id, baseline_id=excluded.baseline_id, checksum=excluded.checksum, generation=excluded.generation, last_plan_id=excluded.last_plan_id, applied_at=excluded.applied_at;";
        command.Parameters.AddWithValue("$app", artifact.ApplicationId); command.Parameters.AddWithValue("$instance", artifact.StoreInstanceId); command.Parameters.AddWithValue("$baseline", envelope.TargetBaselineId);
        command.Parameters.AddWithValue("$checksum", artifact.TargetChecksum); command.Parameters.AddWithValue("$generation", generation); command.Parameters.AddWithValue("$plan", envelope.PlanId); command.Parameters.AddWithValue("$time", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> AcquirePersistedSchemaLeaseAsync(SqliteConnection connection, string applicationId, long expectedGeneration, string ownerToken, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"INSERT INTO {_names.SchemaLease}(application_id,generation,owner_token,acquired_at) VALUES ($app,$generation,$owner,$time) ON CONFLICT(application_id) DO UPDATE SET owner_token=excluded.owner_token, acquired_at=excluded.acquired_at WHERE {_names.SchemaLease}.generation=$generation AND {_names.SchemaLease}.owner_token IS NULL;";
        command.Parameters.AddWithValue("$app", applicationId); command.Parameters.AddWithValue("$generation", expectedGeneration); command.Parameters.AddWithValue("$owner", ownerToken);
        command.Parameters.AddWithValue("$time", _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private async ValueTask PublishPersistedSchemaLeaseAsync(SqliteConnection connection, string applicationId, long expectedGeneration, long generation, string ownerToken, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"UPDATE {_names.SchemaLease} SET generation=$generation, owner_token=NULL, acquired_at=NULL WHERE application_id=$app AND generation=$expected AND owner_token=$owner;";
        command.Parameters.AddWithValue("$app", applicationId); command.Parameters.AddWithValue("$expected", expectedGeneration); command.Parameters.AddWithValue("$generation", generation); command.Parameters.AddWithValue("$owner", ownerToken);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("SQLite schema migration ownership was lost before publication.");
    }

    private async ValueTask ReplaceAssetsAsync(SqliteConnection connection, string applicationId, SchemaAssetValue[] assets, CancellationToken cancellationToken)
    {
        await using (var remove = connection.CreateCommand()) { remove.CommandTimeout = TimeoutSeconds(); remove.CommandText = $"DELETE FROM {_names.SchemaAssets} WHERE application_id=$app;"; remove.Parameters.AddWithValue("$app", applicationId); await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        foreach (SchemaAssetValue asset in assets)
        {
            await using var command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
            command.CommandText = $"INSERT INTO {_names.SchemaAssets}(application_id, logical_id, safe_summary, state) VALUES ($app,$id,$summary,$state);";
            command.Parameters.AddWithValue("$app", applicationId); command.Parameters.AddWithValue("$id", asset.LogicalId); command.Parameters.AddWithValue("$summary", asset.SafeSummary); command.Parameters.AddWithValue("$state", (int)BaseSchemaAssetState.Ready);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask InsertHistoryAsync(SqliteConnection connection, string applicationId, BaseSchemaProviderVerifiedEnvelope envelope, long generation, string checksum, string now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"INSERT INTO {_names.SchemaHistory}(application_id,generation,baseline_id,checksum,plan_id,classification,outcome,provider_version,structural_verification,external_data_migration,semantic_conversion,external_attestation_id,external_signer_id,applied_at) VALUES ($app,$generation,$baseline,$checksum,$plan,$classification,$outcome,$provider,$structural,$external,$semantic,$attestation,$signer,$time);";
        command.Parameters.AddWithValue("$app", applicationId); command.Parameters.AddWithValue("$generation", generation); command.Parameters.AddWithValue("$baseline", envelope.TargetBaselineId); command.Parameters.AddWithValue("$checksum", checksum);
        command.Parameters.AddWithValue("$plan", envelope.PlanId); command.Parameters.AddWithValue("$classification", (int)envelope.Classification); command.Parameters.AddWithValue("$time", now);
        command.Parameters.AddWithValue("$outcome", (int)BaseSchemaApplyOutcome.Applied); command.Parameters.AddWithValue("$provider", envelope.ProviderVersion); command.Parameters.AddWithValue("$structural", (int)envelope.StructuralVerification);
        command.Parameters.AddWithValue("$external", (int)envelope.ExternalDataMigration); command.Parameters.AddWithValue("$semantic", (int)envelope.SemanticConversion);
        command.Parameters.AddWithValue("$attestation", envelope.ExternalAttestationId is null ? DBNull.Value : envelope.ExternalAttestationId); command.Parameters.AddWithValue("$signer", envelope.ExternalSignerId is null ? DBNull.Value : envelope.ExternalSignerId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ExecuteSchemaCommandAsync(SqliteConnection connection, string sql, TimeSpan timeout, CancellationToken cancellationToken)
        => await _schemaCommands.ExecuteAsync(connection, sql, timeout, cancellationToken).ConfigureAwait(false);
    private async ValueTask<bool> TryRollbackSchemaAsync(SqliteConnection connection, TimeSpan timeout)
    { try { await ExecuteSchemaCommandAsync(connection, "ROLLBACK;", timeout, CancellationToken.None).ConfigureAwait(false); return true; } catch { return false; } }

    private string[] PrepareExecutionStatements(BaseSchemaPreparationRequest request)
    {
        if (request.Classification is BaseSchemaPlanClassification.DataMigrationRequired or BaseSchemaPlanClassification.DriftBlocked) return [];
        if (request.ExpectedGeneration == 0) return _schema.GetExecutionStatements();
        var statements = new List<string>();
        statements.AddRange(PrepareDestructiveDataGuards(request));
        HashSet<string> createdCollections = request.LogicalDelta.Where(static operation => operation.Kind == BaseSchemaOperationKind.CreateCollection)
            .Select(static operation => operation.LogicalId[2..]).ToHashSet(StringComparer.Ordinal);
        HashSet<string> rebuiltCollections = request.LogicalDelta
            .Where(static operation => operation.Kind == BaseSchemaOperationKind.RemoveField)
            .Select(static operation => operation.LogicalId.Split(':')[1])
            .ToHashSet(StringComparer.Ordinal);
        foreach (string collectionId in rebuiltCollections.Order(StringComparer.Ordinal))
            statements.AddRange(PrepareCollectionRebuild(request, collectionId));
        foreach (BaseSchemaLogicalOperation operation in request.LogicalDelta)
        {
            string[] parts = operation.LogicalId.Split(':');
            switch (operation.Kind)
            {
                case BaseSchemaOperationKind.CreateCollection:
                {
                    SqlitePhysicalModel.CollectionModel collection = _physical.Collection(parts[1]);
                    statements.Add(collection.CreateSql());
                    statements.Add($"CREATE INDEX IF NOT EXISTS ix_{collection.Table}_updated ON {collection.Table}(updated_at, record_id);");
                    statements.AddRange(collection.Indexes.Select(index => index.CreateSql(collection)));
                    break;
                }
                case BaseSchemaOperationKind.AddField when !createdCollections.Contains(parts[1]):
                {
                    SqlitePhysicalModel.FieldModel field = _physical.Collection(parts[1]).Fields.Single(item => item.Definition.Id == parts[2]);
                    if (field.PresenceColumn is null) throw new InvalidOperationException();
                    statements.Add($"ALTER TABLE {_physical.Collection(parts[1]).Table} ADD COLUMN {field.PresenceColumn} INTEGER NOT NULL DEFAULT 0 CHECK ({field.PresenceColumn} IN (0,1));");
                    statements.Add($"ALTER TABLE {_physical.Collection(parts[1]).Table} ADD COLUMN {field.Column} {field.SqlType} NULL;");
                    break;
                }
                case BaseSchemaOperationKind.RemoveField:
                    break;
                case BaseSchemaOperationKind.AddIndex when !createdCollections.Contains(parts[1]):
                {
                    SqlitePhysicalModel.CollectionModel collection = _physical.Collection(parts[1]);
                    statements.Add(collection.Indexes.Single(item => item.Definition.Id == parts[2]).CreateSql(collection));
                    break;
                }
                case BaseSchemaOperationKind.AlterIndex:
                {
                    SqlitePhysicalModel.CollectionModel collection = _physical.Collection(parts[1]); SqlitePhysicalModel.IndexModel index = collection.Indexes.Single(item => item.Definition.Id == parts[2]);
                    statements.Add($"DROP INDEX IF EXISTS {index.Name};"); statements.Add(index.CreateSql(collection)); break;
                }
                case BaseSchemaOperationKind.RemoveIndex: statements.Add($"DROP INDEX IF EXISTS {NativeSchemaName("b_i_", parts[2])};"); break;
                case BaseSchemaOperationKind.AddRelation:
                {
                    SqlitePhysicalModel.RelationModel? relation = _physical.Relations.SingleOrDefault(item => item.Definition.Id == parts[1]);
                    if (relation is not null) { statements.Add(relation.CreateSql()); statements.Add($"CREATE INDEX IF NOT EXISTS {relation.SourceIndex} ON {relation.Table}(source_record_id, ordinal);"); statements.Add($"CREATE INDEX IF NOT EXISTS {relation.TargetIndex} ON {relation.Table}(target_record_id, source_record_id);"); }
                    break;
                }
                case BaseSchemaOperationKind.AlterRelation:
                {
                    statements.Add($"DROP TABLE IF EXISTS {NativeSchemaName("b_r_", parts[1])};");
                    SqlitePhysicalModel.RelationModel? relation = _physical.Relations.SingleOrDefault(item => item.Definition.Id == parts[1]);
                    if (relation is not null) { statements.Add(relation.CreateSql()); statements.Add($"CREATE INDEX IF NOT EXISTS {relation.SourceIndex} ON {relation.Table}(source_record_id, ordinal);"); statements.Add($"CREATE INDEX IF NOT EXISTS {relation.TargetIndex} ON {relation.Table}(target_record_id, source_record_id);"); }
                    break;
                }
                case BaseSchemaOperationKind.RemoveRelation: statements.Add($"DROP TABLE IF EXISTS {NativeSchemaName("b_r_", parts[1])};"); break;
                case BaseSchemaOperationKind.RemoveCollection: statements.Add($"DROP TABLE IF EXISTS {NativeSchemaName("b_c_", parts[1])};"); break;
                case BaseSchemaOperationKind.RenameCollection or BaseSchemaOperationKind.RenameField or BaseSchemaOperationKind.AddRead or BaseSchemaOperationKind.AlterRead or BaseSchemaOperationKind.RemoveRead or BaseSchemaOperationKind.VerifyAsset:
                case BaseSchemaOperationKind.AdoptExternalBaseline:
                    break;
                default: throw new InvalidOperationException();
            }
        }
        return statements.ToArray();
    }

    private static IEnumerable<string> PrepareDestructiveDataGuards(BaseSchemaPreparationRequest request)
    {
        const string guard = "hpd_base_schema_destructive_guard";
        bool initialized = false;
        foreach (BaseSchemaLogicalOperation operation in request.LogicalDelta.Where(static operation => operation.Destructive))
        {
            string[] parts = operation.LogicalId.Split(':');
            string? query = operation.Kind switch
            {
                BaseSchemaOperationKind.RemoveCollection => $"SELECT EXISTS(SELECT 1 FROM {NativeSchemaName("b_c_", parts[1])} LIMIT 1)",
                BaseSchemaOperationKind.RemoveField => RemovedFieldHasValueSql(request, operation, parts).TrimEnd(';'),
                BaseSchemaOperationKind.RemoveRelation => RemovedRelationHasValueSql(request, operation, parts)?.TrimEnd(';'),
                _ => null,
            };
            if (query is null) continue;
            if (!initialized)
            {
                yield return $"CREATE TEMP TABLE IF NOT EXISTS {guard} (value INTEGER NOT NULL CHECK(value = 0));";
                initialized = true;
            }
            yield return $"DELETE FROM {guard};";
            yield return $"INSERT INTO {guard}(value) {query};";
        }
        if (initialized) yield return $"DROP TABLE {guard};";
    }

    private IEnumerable<string> PrepareCollectionRebuild(BaseSchemaPreparationRequest request, string collectionId)
    {
        SqlitePhysicalModel.CollectionModel collection = _physical.Collection(collectionId);
        string replacement = collection.Table + "_rebuild";
        HashSet<string> priorFields = request.ObservedState.Assets
            .Where(asset => asset.LogicalId.StartsWith("f:" + collectionId + ":", StringComparison.Ordinal))
            .Select(static asset => asset.LogicalId.Split(':')[2])
            .ToHashSet(StringComparer.Ordinal);
        var columns = new List<string> { "record_id", "revision", "created_at", "updated_at", "append_position", "latest_mutation_position" };
        var values = new List<string>(columns);
        foreach (SqlitePhysicalModel.FieldModel field in collection.Fields)
        {
            bool existed = priorFields.Contains(field.Definition.Id);
            if (field.PresenceColumn is not null)
            {
                columns.Add(field.PresenceColumn);
                values.Add(existed ? field.PresenceColumn : "0");
            }
            columns.Add(field.Column);
            values.Add(existed ? field.Column : "NULL");
        }
        if (collection.HasExtensionJson)
        {
            columns.Add("extension_json");
            values.Add("extension_json");
        }
        yield return $"DROP TABLE IF EXISTS {replacement};";
        yield return collection.CreateSql(replacement);
        yield return $"INSERT INTO {replacement} ({string.Join(", ", columns)}) SELECT {string.Join(", ", values)} FROM {collection.Table};";
        yield return $"DROP TABLE {collection.Table};";
        yield return $"ALTER TABLE {replacement} RENAME TO {collection.Table};";
        yield return $"CREATE INDEX IF NOT EXISTS ix_{collection.Table}_updated ON {collection.Table}(updated_at, record_id);";
        foreach (SqlitePhysicalModel.IndexModel index in collection.Indexes)
            yield return index.CreateSql(collection);
    }

    private static string NativeSchemaName(string prefix, string id) => prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))).Substring(0, 32);

    private async ValueTask ApplyCollectionMappingsAsync(SqliteConnection connection, CollectionMapping[] mappings, CancellationToken cancellationToken)
    {
        foreach (CollectionMapping mapping in mappings)
        {
            await using var command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
            CollectionDefinition definition = _options.Collections.Single(item => string.Equals(item.Id, mapping.CollectionId, StringComparison.Ordinal));
            command.CommandText = $"INSERT INTO {_names.Collections}(collection_id,schema_hash,registered_at,native_name,mutation_mode,next_append_position,purge_generation,descriptor_json) VALUES ($id,NULL,$at,$native,$mode,0,0,NULL) ON CONFLICT(collection_id) DO UPDATE SET native_name=excluded.native_name, mutation_mode=excluded.mutation_mode;";
            command.Parameters.AddWithValue("$id", mapping.CollectionId); command.Parameters.AddWithValue("$at", _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$native", mapping.NativeName); command.Parameters.AddWithValue("$mode", (int)definition.MutationMode);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static byte[] EncodeProviderArtifact(string applicationId, string instanceId, long generation, string? baselineChecksum, string targetChecksum, SchemaAssetValue[] assets, string[] statements, CollectionMapping[] mappings)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        WriteSchemaString(writer, "sqlite-l35-artifact-v2"); WriteSchemaString(writer, applicationId); WriteSchemaString(writer, instanceId); writer.Write(generation); WriteNullableSchemaString(writer, baselineChecksum); WriteSchemaString(writer, targetChecksum); writer.Write(assets.Length);
        foreach (SchemaAssetValue asset in assets) { WriteSchemaString(writer, asset.LogicalId); WriteSchemaString(writer, asset.SafeSummary); }
        writer.Write(statements.Length); foreach (string statement in statements) WriteSchemaMaterial(writer, statement);
        writer.Write(mappings.Length); foreach (CollectionMapping mapping in mappings) { WriteSchemaString(writer, mapping.CollectionId); WriteSchemaString(writer, mapping.NativeName); }
        writer.Flush(); return stream.ToArray();
    }
    private static ProviderArtifact DecodeProviderArtifact(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        if (ReadSchemaString(reader) != "sqlite-l35-artifact-v2") throw new InvalidDataException();
        string application = ReadSchemaString(reader); string instance = ReadSchemaString(reader); long generation = reader.ReadInt64(); string? baseline = ReadNullableSchemaString(reader); string target = ReadSchemaString(reader);
        int count = reader.ReadInt32(); if (count < 0 || count > 20_000) throw new InvalidDataException(); var assets = new SchemaAssetValue[count];
        for (int index = 0; index < count; index++) assets[index] = new SchemaAssetValue(ReadSchemaString(reader), ReadSchemaString(reader));
        int statementCount = reader.ReadInt32(); if (statementCount < 0 || statementCount > 20_000) throw new InvalidDataException(); var statements = new string[statementCount]; for (int index = 0; index < statementCount; index++) statements[index] = ReadSchemaMaterial(reader);
        int mappingCount = reader.ReadInt32(); if (mappingCount < 0 || mappingCount > 10_000) throw new InvalidDataException(); var mappings = new CollectionMapping[mappingCount]; for (int index = 0; index < mappingCount; index++) mappings[index] = new CollectionMapping(ReadSchemaString(reader), ReadSchemaString(reader));
        if (stream.Position != stream.Length) throw new InvalidDataException(); return new ProviderArtifact(application, instance, generation, baseline, target, assets, statements, mappings);
    }
    private static void WriteSchemaString(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); if (bytes.Length > 16_384) throw new InvalidDataException(); writer.Write(bytes.Length); writer.Write(bytes); }
    private static string ReadSchemaString(BinaryReader reader) { int length = reader.ReadInt32(); if (length < 0 || length > 16_384) throw new InvalidDataException(); byte[] value = reader.ReadBytes(length); if (value.Length != length) throw new EndOfStreamException(); return Encoding.UTF8.GetString(value); }
    private static void WriteSchemaMaterial(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); if (bytes.Length > 1_048_576) throw new InvalidDataException(); writer.Write(bytes.Length); writer.Write(bytes); }
    private static string ReadSchemaMaterial(BinaryReader reader) { int length = reader.ReadInt32(); if (length < 0 || length > 1_048_576) throw new InvalidDataException(); byte[] value = reader.ReadBytes(length); if (value.Length != length) throw new EndOfStreamException(); return Encoding.UTF8.GetString(value); }
    private static void WriteNullableSchemaString(BinaryWriter writer, string? value) { writer.Write(value is not null); if (value is not null) WriteSchemaString(writer, value); }
    private static string? ReadNullableSchemaString(BinaryReader reader) => reader.ReadBoolean() ? ReadSchemaString(reader) : null;
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static OperationResult<T> SchemaValidation<T>(string code, string message) => new() { Status = OperationStatus.ValidationFailed, Error = new BaseError { Code = code, Message = message, Category = ErrorCategory.Validation } };
    private static OperationResult<T> SchemaConflict<T>(string code, string message) => new() { Status = OperationStatus.Conflict, Error = new BaseError { Code = code, Message = message, Category = ErrorCategory.Conflict } };
    private static OperationResult<T> SchemaCapability<T>(string code, string message) => new() { Status = OperationStatus.CapabilityUnavailable, Error = new BaseError { Code = code, Message = message, Category = ErrorCategory.Capability } };
    private static OperationResult<T> SchemaFailure<T>(string code, string message) => new() { Status = OperationStatus.StoreError, Error = new BaseError { Code = code, Message = message, Category = ErrorCategory.Store } };
    private sealed record BaselineRow(string StoreInstanceId, string BaselineId, string Checksum, long Generation, string LastPlanId);
    private sealed record SchemaAssetValue(string LogicalId, string SafeSummary);
    private sealed record CollectionMapping(string CollectionId, string NativeName);
    private sealed record ProviderArtifact(string ApplicationId, string StoreInstanceId, long ExpectedGeneration, string? BaselineChecksum, string TargetChecksum, SchemaAssetValue[] Assets, string[] ExecutionStatements, CollectionMapping[] CollectionMappings);
}
