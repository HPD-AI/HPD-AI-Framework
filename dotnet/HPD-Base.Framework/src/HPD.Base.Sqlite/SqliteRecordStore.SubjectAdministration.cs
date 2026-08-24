using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore
{
    /// <inheritdoc />
    public ValueTask<OperationResult<BaseSubjectValidationPlanReceipt[]>> ReadSubjectValidationPlanReceiptsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long schemaGeneration = Volatile.Read(ref _schemaGeneration);
        BaseSubjectValidationPlanReceipt[] receipts = _options.ExportedSubjects
            .OrderBy(static value => value.ValidationPlan.Id, StringComparer.Ordinal)
            .ThenBy(static value => value.ValidationPlan.Version)
            .Select(value => new BaseSubjectValidationPlanReceipt
            {
                PlanId = new string(value.ValidationPlan.Id.AsSpan()),
                PlanVersion = value.ValidationPlan.Version,
                PlanChecksum = BaseSubjectContractNormalizer.NormalizePlan(value.ValidationPlan).Checksum,
                StoreInstanceId = new string(CurrentStoreInstanceId.AsSpan()),
                SchemaGeneration = schemaGeneration,
                Access = value.ValidationPlan.Access,
                LoweringFormatVersion = 1,
            })
            .ToArray();
        return ValueTask.FromResult(OperationResults.Ok(receipts));
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSubjectCurrentPublicationState[]>> ReadCurrentSubjectPublicationsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using IAsyncDisposable lease = await _schemaGenerationGate.AcquireSharedAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteConnection connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandTimeout = TimeoutSeconds();
            command.CommandText = $"SELECT contract_id,contract_version,contract_checksum,authority_epoch,restore_epoch,state_generation,publication_previous_generation,publication_kind,publication_position,publication_digest FROM {_names.SubjectContracts} ORDER BY contract_id COLLATE BINARY,contract_version;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var values = new List<BaseSubjectCurrentPublicationState>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = new SqliteSubjectContractRow(
                    reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                    new BaseSubjectAuthorityEpoch((byte[])reader.GetValue(3)), reader.GetInt64(4), reader.GetInt64(5),
                    reader.GetInt64(6), (BaseSubjectAuthorityPublicationKind)reader.GetInt32(7), reader.GetInt64(8), reader.GetString(9));
                if (!ValidSubjectPublicationReceipt(row))
                    return SubjectAdministrationFailure<BaseSubjectCurrentPublicationState[]>(BaseSubjectErrorCodes.ProviderContractInvalid);
                values.Add(new BaseSubjectCurrentPublicationState
                {
                    ContractId = new string(row.ContractId.AsSpan()),
                    ContractVersion = row.ContractVersion,
                    ContractChecksum = new string(row.ContractChecksum.AsSpan()),
                    AuthorityEpoch = new BaseSubjectAuthorityEpoch(row.AuthorityEpoch.ToArray()),
                    Receipt = new BaseSubjectCurrentPublicationReceipt
                    {
                        PreviousStateGeneration = row.PreviousStateGeneration,
                        PublishedStateGeneration = row.StateGeneration,
                        RestoreEpoch = row.RestoreEpoch,
                        Kind = row.PublicationKind,
                        OriginalPublicationPosition = new BaseMutationJournalPosition(row.PublicationPosition),
                        PublicationDigest = new string(row.PublicationDigest.AsSpan()),
                    },
                });
            }
            return OperationResults.Ok(values.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return SubjectAdministrationFailure<BaseSubjectCurrentPublicationState[]>(BaseSubjectErrorCodes.ProviderContractInvalid);
        }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSubjectEpochRotationResult>> RotateEpochAsync(
        BaseSubjectEpochRotationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_options.AdministrationEnabled
            || request.ContractVersion <= 0
            || request.ExpectedStateGeneration <= 0
            || string.IsNullOrWhiteSpace(request.ContractId)
            || !string.Equals(request.DestructiveIntent, "rotate-subject-authority-epoch", StringComparison.Ordinal))
        {
            return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(
                BaseSubjectErrorCodes.ContractInvalid,
                OperationStatus.ValidationFailed,
                ErrorCategory.Validation);
        }

        bool slot = false;
        try
        {
            using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acquisition.CancelAfter(_options.AdministrationAcquisitionTimeout);
            await _administrationExecutionSlots.WaitAsync(acquisition.Token).ConfigureAwait(false);
            slot = true;
            await using IAsyncDisposable lease = await _schemaGenerationGate
                .AcquireExclusiveAsync(acquisition.Token).ConfigureAwait(false);
            if (_quarantinedMutations.Count != 0 || _quarantinedAdministration.Count != 0)
                return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.ValidationUnavailable);

            await using SqliteConnection connection = await OpenSubjectMaintenanceAsync(cancellationToken).ConfigureAwait(false);
            SqliteSubjectContractRow? contract;
            await using (SqliteTransaction readTransaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
            {
                contract = await ReadSubjectContractAsync(
                    connection, readTransaction, request.ContractId, request.ContractVersion, cancellationToken).ConfigureAwait(false);
                await readTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            if (contract is null)
                return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(
                    BaseSubjectErrorCodes.ContractInvalid,
                    OperationStatus.ValidationFailed,
                    ErrorCategory.Validation);
            if (contract.StateGeneration != request.ExpectedStateGeneration)
                return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(
                    BaseSubjectErrorCodes.SchemaGenerationChanged,
                    OperationStatus.Conflict,
                    ErrorCategory.Conflict);
            if (contract.StateGeneration == long.MaxValue || !ValidSubjectPublicationReceipt(contract))
                return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.ProviderContractInvalid);

            SubjectRewriteCheckpoint checkpoint = await StageSubjectReferenceRewriteAsync(
                connection, contract, cancellationToken).ConfigureAwait(false);
            long publishedGeneration = checked(contract.StateGeneration + 1);
            BaseSubjectAuthorityEpoch replacement = checkpoint.NewEpoch;
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            SqliteSubjectContractRow? current = await ReadSubjectContractAsync(
                connection, transaction, request.ContractId, request.ContractVersion, cancellationToken).ConfigureAwait(false);
            if (current is null || current.StateGeneration != contract.StateGeneration
                || !current.AuthorityEpoch.Equals(contract.AuthorityEpoch))
                return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.SchemaGenerationChanged);
            long publicationPosition = await AppendSubjectPublicationAsync(
                connection,
                transaction,
                request.ContractId,
                request.ContractVersion,
                contract.StateGeneration,
                publishedGeneration,
                contract.RestoreEpoch,
                BaseSubjectAuthorityPublicationKind.EpochRotation,
                cancellationToken).ConfigureAwait(false);

            await ApplyStagedSubjectReferenceRewriteAsync(
                connection, transaction, checkpoint, publicationPosition, cancellationToken).ConfigureAwait(false);

            string digest = BaseSubjectPublicationIntegrity.Compute(
                request.ContractId,
                request.ContractVersion,
                contract.ContractChecksum,
                contract.StateGeneration,
                publishedGeneration,
                contract.RestoreEpoch,
                BaseSubjectAuthorityPublicationKind.EpochRotation,
                new BaseMutationJournalPosition(publicationPosition),
                replacement);
            await using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandTimeout = TimeoutSeconds();
                update.CommandText = $"""
UPDATE {_names.SubjectContracts}
SET authority_epoch=$epoch, state_generation=$published,
    publication_previous_generation=$previous, publication_kind=$kind,
    publication_position=$position, publication_digest=$digest
WHERE contract_id=$contract AND contract_version=$version AND state_generation=$previous;
""";
                update.Parameters.Add("$epoch", SqliteType.Blob).Value = replacement.ToArray();
                update.Parameters.AddWithValue("$published", publishedGeneration);
                update.Parameters.AddWithValue("$previous", contract.StateGeneration);
                update.Parameters.AddWithValue("$kind", (int)BaseSubjectAuthorityPublicationKind.EpochRotation);
                update.Parameters.AddWithValue("$position", publicationPosition);
                update.Parameters.AddWithValue("$digest", digest);
                update.Parameters.AddWithValue("$contract", request.ContractId);
                update.Parameters.AddWithValue("$version", request.ContractVersion);
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.SchemaGenerationChanged);
            }

            await _administrationOperations.BeforePhaseAsync("subjectRewriteBeforePublicationCommit", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok(new BaseSubjectEpochRotationResult
            {
                ContractId = new string(request.ContractId.AsSpan()),
                ContractVersion = request.ContractVersion,
                PreviousStateGeneration = contract.StateGeneration,
                PublishedStateGeneration = publishedGeneration,
                PublicationPosition = new BaseMutationJournalPosition(publicationPosition),
                ExaminedRecords = checkpoint.Examined,
                RewrittenReferences = checkpoint.Rewritten,
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.ValidationUnavailable);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OverflowException)
        {
            return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.ProviderContractInvalid);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(
                BaseSubjectErrorCodes.TransactionConflict,
                OperationStatus.Conflict,
                ErrorCategory.Conflict);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.ProviderContractInvalid);
        }
        finally
        {
            if (slot)
                _administrationExecutionSlots.Release();
        }
    }

    private async ValueTask<IReadOnlyDictionary<string, long>> ReadSubjectStateGenerationsAsync(
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT contract_id,contract_version,state_generation FROM {_names.SubjectContracts} ORDER BY contract_id COLLATE BINARY,contract_version;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var generations = new Dictionary<string, long>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            generations.Add(SubjectContractKey(reader.GetString(0), reader.GetInt32(1)), reader.GetInt64(2));
        return generations;
    }

    private async ValueTask TransformRestoredSubjectAuthoritiesAsync(
        SqliteConnection connection,
        long restoreEpoch,
        IReadOnlyDictionary<string, long> preRestoreGenerations,
        long preRestoreLifecycleDeliveryEpoch,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand updateRestore = connection.CreateCommand())
        {
            updateRestore.CommandTimeout = TimeoutSeconds();
            updateRestore.CommandText = $"INSERT INTO {_names.ProviderState}(key,value) VALUES ('restore_epoch',$epoch) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
            updateRestore.Parameters.AddWithValue("$epoch", restoreEpoch.ToString(CultureInfo.InvariantCulture));
            await updateRestore.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (BaseExportedSubjectDefinition definition in _options.ExportedSubjects
            .OrderBy(static definition => definition.Id, StringComparer.Ordinal)
            .ThenBy(static definition => definition.Version))
        {
            SqliteSubjectContractRow? artifact;
            await using (SqliteTransaction readTransaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
            {
                artifact = await ReadSubjectContractAsync(
                    connection, readTransaction, definition.Id, definition.Version, cancellationToken).ConfigureAwait(false);
                await readTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            if (artifact is null
                || !string.Equals(artifact.ContractChecksum, definition.ValidationPlan.ContractChecksum, StringComparison.Ordinal)
                || !ValidSubjectPublicationReceipt(artifact))
                throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);

            preRestoreGenerations.TryGetValue(SubjectContractKey(definition.Id, definition.Version), out long preRestore);
            long previousGeneration = Math.Max(preRestore, artifact.StateGeneration);
            long publishedGeneration = checked(previousGeneration + 1);
            SubjectRewriteCheckpoint checkpoint = await StageSubjectReferenceRewriteAsync(
                connection, artifact, cancellationToken,
                revisionFactory: revision => RestoreDerivedRevision(restoreEpoch, revision)).ConfigureAwait(false);
            BaseSubjectAuthorityEpoch replacement = checkpoint.NewEpoch;
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            long publicationPosition = await AppendSubjectPublicationAsync(
                connection,
                transaction,
                definition.Id,
                definition.Version,
                previousGeneration,
                publishedGeneration,
                restoreEpoch,
                BaseSubjectAuthorityPublicationKind.RestoreTransformation,
                cancellationToken).ConfigureAwait(false);
            await ApplyStagedSubjectReferenceRewriteAsync(
                connection, transaction, checkpoint, publicationPosition, cancellationToken).ConfigureAwait(false);

            string digest = BaseSubjectPublicationIntegrity.Compute(
                definition.Id,
                definition.Version,
                artifact.ContractChecksum,
                previousGeneration,
                publishedGeneration,
                restoreEpoch,
                BaseSubjectAuthorityPublicationKind.RestoreTransformation,
                new BaseMutationJournalPosition(publicationPosition),
                replacement);
            await using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandTimeout = TimeoutSeconds();
            update.CommandText = $"""
UPDATE {_names.SubjectContracts}
SET authority_epoch=$epoch, restore_epoch=$restore, state_generation=$published,
    publication_previous_generation=$previous, publication_kind=$kind,
    publication_position=$position, publication_digest=$digest
WHERE contract_id=$contract AND contract_version=$version AND state_generation=$artifact;
""";
            update.Parameters.Add("$epoch", SqliteType.Blob).Value = replacement.ToArray();
            update.Parameters.AddWithValue("$restore", restoreEpoch);
            update.Parameters.AddWithValue("$published", publishedGeneration);
            update.Parameters.AddWithValue("$previous", previousGeneration);
            update.Parameters.AddWithValue("$kind", (int)BaseSubjectAuthorityPublicationKind.RestoreTransformation);
            update.Parameters.AddWithValue("$position", publicationPosition);
            update.Parameters.AddWithValue("$digest", digest);
            update.Parameters.AddWithValue("$contract", definition.Id);
            update.Parameters.AddWithValue("$version", definition.Version);
            update.Parameters.AddWithValue("$artifact", artifact.StateGeneration);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException(BaseSubjectErrorCodes.SchemaGenerationChanged);
            await _administrationOperations.BeforePhaseAsync("subjectRewriteBeforePublicationCommit", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        await TransformRestoredRetirementAuthorityAsync(connection, restoreEpoch, cancellationToken).ConfigureAwait(false);
        await PublishRestoredLifecycleDeliveryAuthorityAsync(
            connection,
            preRestoreLifecycleDeliveryEpoch,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask TransformRestoredRetirementAuthorityAsync(SqliteConnection connection,long restoreEpoch,CancellationToken cancellationToken)
    {
        if(!_options.SubjectRetirementPolicies.Any())return;
        long after=0,examined=0,changed=0,canonicalBytes=0;byte[] rolling=SHA256.HashData("base.subjectRetirement.restore.empty.v1"u8);
        var contractEvidence=new Dictionary<(string Id,int Version),(long Barriers,long Acknowledgements,byte[] Checksum)>();
        while(true)
        {
            await using SqliteTransaction transaction=(SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,cancellationToken).ConfigureAwait(false);
            await using SqliteCommand select=connection.CreateCommand();select.Transaction=transaction;select.CommandTimeout=TimeoutSeconds();select.CommandText=$"SELECT b.rowid,b.scope_kind,b.scope_index_digest,b.contract_id,b.contract_version,b.subject_id,b.authority_epoch,b.incarnation,b.tombstone_sequence,b.required_consumer_set_checksum,b.created_at,b.deadline_at,b.state,b.generation,b.barrier_checksum,c.authority_epoch FROM {_names.SubjectRetirementBarriers} b JOIN {_names.SubjectContracts} c ON c.contract_id=b.contract_id AND c.contract_version=b.contract_version WHERE b.rowid>$after ORDER BY b.rowid LIMIT 256;";select.Parameters.AddWithValue("$after",after);
            var rows=new List<(long RowId,int ScopeKind,byte[] Digest,string Contract,int Version,string Subject,byte[] PriorEpoch,byte[] Incarnation,long Sequence,string ConsumerSet,string Created,string Deadline,int State,long Generation,string Checksum,byte[] Epoch)>();
            await using(SqliteDataReader reader=await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))rows.Add((reader.GetInt64(0),reader.GetInt32(1),(byte[])reader.GetValue(2),reader.GetString(3),reader.GetInt32(4),reader.GetString(5),(byte[])reader.GetValue(6),(byte[])reader.GetValue(7),reader.GetInt64(8),reader.GetString(9),reader.GetString(10),reader.GetString(11),reader.GetInt32(12),reader.GetInt64(13),reader.GetString(14),(byte[])reader.GetValue(15)));
            foreach(var row in rows)
            {
                BaseExportedSubjectDefinition definition=_options.ExportedSubjects.Single(value=>value.Id==row.Contract&&value.Version==row.Version);
                var acknowledgements=new List<string>();await using(SqliteCommand acks=connection.CreateCommand()){acks.Transaction=transaction;acks.CommandTimeout=TimeoutSeconds();acks.CommandText=$"SELECT consumer_id,consumer_version,consumer_checksum,through_sequence,disposition,retirement_position FROM {_names.SubjectRetirementAcknowledgements} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation ORDER BY consumer_id,consumer_version;";acks.Parameters.AddWithValue("$scopeKind",row.ScopeKind);acks.Parameters.Add("$scopeDigest",SqliteType.Blob).Value=row.Digest;acks.Parameters.AddWithValue("$contract",row.Contract);acks.Parameters.AddWithValue("$version",row.Version);acks.Parameters.AddWithValue("$subject",row.Subject);acks.Parameters.Add("$epoch",SqliteType.Blob).Value=row.PriorEpoch;acks.Parameters.Add("$incarnation",SqliteType.Blob).Value=row.Incarnation;await using SqliteDataReader reader=await acks.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))acknowledgements.Add(BaseSubjectRetirementRegistry.AcknowledgementChecksumInput(reader.GetString(0),reader.GetInt32(1),reader.GetString(2),reader.GetInt64(3),(BaseSubjectAcknowledgementDisposition)reader.GetInt32(4),reader.GetInt64(5)));}
                var prior=new BaseSubjectRetirementBarrier{ContractId=row.Contract,ContractVersion=row.Version,SubjectId=BaseSubjectId.Create(row.Subject,definition.SubjectIdKind,definition.MaximumSubjectIdUtf8Bytes),AuthorityEpoch=new(row.PriorEpoch),Incarnation=new(row.Incarnation),TombstoneSequence=row.Sequence,RequiredConsumerSetChecksum=row.ConsumerSet,CreatedAtUtc=DateTimeOffset.Parse(row.Created,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),DeadlineUtc=DateTimeOffset.Parse(row.Deadline,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),State=(BaseSubjectRetirementBarrierState)row.State,Generation=row.Generation,BarrierChecksum=row.Checksum};
                if(!string.Equals(BaseSubjectRetirementRegistry.BarrierChecksum(prior,acknowledgements),row.Checksum,StringComparison.Ordinal))throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                BaseSubjectRetirementBarrier replacement=prior with{AuthorityEpoch=new(row.Epoch),Generation=checked(prior.Generation+1),BarrierChecksum=string.Empty};replacement=replacement with{BarrierChecksum=BaseSubjectRetirementRegistry.BarrierChecksum(replacement,acknowledgements)};
                await using(SqliteCommand updateAcks=connection.CreateCommand()){updateAcks.Transaction=transaction;updateAcks.CommandTimeout=TimeoutSeconds();updateAcks.CommandText=$"UPDATE {_names.SubjectRetirementAcknowledgements} SET authority_epoch=$replacement WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$prior AND incarnation=$incarnation;";updateAcks.Parameters.Add("$replacement",SqliteType.Blob).Value=row.Epoch;updateAcks.Parameters.AddWithValue("$scopeKind",row.ScopeKind);updateAcks.Parameters.Add("$scopeDigest",SqliteType.Blob).Value=row.Digest;updateAcks.Parameters.AddWithValue("$contract",row.Contract);updateAcks.Parameters.AddWithValue("$version",row.Version);updateAcks.Parameters.AddWithValue("$subject",row.Subject);updateAcks.Parameters.Add("$prior",SqliteType.Blob).Value=row.PriorEpoch;updateAcks.Parameters.Add("$incarnation",SqliteType.Blob).Value=row.Incarnation;await updateAcks.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);}
                await using(SqliteCommand update=connection.CreateCommand()){update.Transaction=transaction;update.CommandTimeout=TimeoutSeconds();update.CommandText=$"UPDATE {_names.SubjectRetirementBarriers} SET authority_epoch=$epoch,generation=$generation,barrier_checksum=$checksum WHERE rowid=$rowid AND authority_epoch=$prior AND barrier_checksum=$priorChecksum;";update.Parameters.Add("$epoch",SqliteType.Blob).Value=row.Epoch;update.Parameters.AddWithValue("$generation",replacement.Generation);update.Parameters.AddWithValue("$checksum",replacement.BarrierChecksum);update.Parameters.AddWithValue("$rowid",row.RowId);update.Parameters.Add("$prior",SqliteType.Blob).Value=row.PriorEpoch;update.Parameters.AddWithValue("$priorChecksum",row.Checksum);if(await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)!=1)throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);}
                byte[] canonical=Encoding.UTF8.GetBytes($"{row.RowId}\0{Convert.ToHexStringLower(row.PriorEpoch)}\0{Convert.ToHexStringLower(row.Epoch)}\0{row.Generation}\0{replacement.Generation}\0{row.Checksum}\0{replacement.BarrierChecksum}");rolling=SHA256.HashData([..rolling,..canonical]);checked{examined++;changed++;canonicalBytes+=canonical.LongLength;}after=row.RowId;
                var evidenceKey=(row.Contract,row.Version);var priorEvidence=contractEvidence.GetValueOrDefault(evidenceKey,(0L,0L,SHA256.HashData("base.subjectRetirement.restore.contract.empty.v1"u8)));contractEvidence[evidenceKey]=(checked(priorEvidence.Item1+1),checked(priorEvidence.Item2+acknowledgements.Count),SHA256.HashData([..priorEvidence.Item3,..canonical]));
            }
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);if(rows.Count!=0)await _administrationOperations.BeforePhaseAsync("subjectRetirementRestorePageCommitted",cancellationToken).ConfigureAwait(false);if(rows.Count<256)break;
        }
        await using SqliteTransaction publishTransaction=(SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,cancellationToken).ConfigureAwait(false);BaseSubjectRetirementPolicy[] policies=[.._options.SubjectRetirementPolicies.OrderBy(value=>value.ContractId,StringComparer.Ordinal).ThenBy(value=>value.ContractVersion)];long publishedControl;await using(SqliteCommand advance=connection.CreateCommand()){advance.Transaction=publishTransaction;advance.CommandTimeout=TimeoutSeconds();advance.CommandText=$"UPDATE {_names.ProviderState} SET value=CAST(value AS INTEGER)+$count WHERE key='subject_retirement_position' RETURNING CAST(value AS INTEGER);";advance.Parameters.AddWithValue("$count",policies.Length);publishedControl=Convert.ToInt64(await advance.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),CultureInfo.InvariantCulture);}long previousControl=checked(publishedControl-policies.Length);long position=checked(previousControl+1);
        foreach(BaseSubjectRetirementPolicy policy in policies){var contract=contractEvidence.GetValueOrDefault((policy.ContractId,policy.ContractVersion),(0L,0L,SHA256.HashData("base.subjectRetirement.restore.contract.empty.v1"u8)));var fact=BaseSubjectRetirementRegistry.SealPublication(new BaseSubjectRetirementPublicationFact{Position=new(position),Kind=BaseSubjectRetirementPublicationKind.RestoreTransformed,Restore=new(){ContractId=policy.ContractId,ContractVersion=policy.ContractVersion,RestoreEpoch=restoreEpoch,PreviousControlGeneration=previousControl,PublishedControlGeneration=publishedControl,TransformedBarrierCount=checked((int)contract.Item1),TransformedAcknowledgementCount=checked((int)contract.Item2),TransformationChecksum=Convert.ToHexStringLower(contract.Item3)}});BaseSubjectRetirementRegistry.ValidatePublication(new(){Scope=null,Fact=fact});byte[] payload=JsonSerializer.SerializeToUtf8Bytes(fact,HPDBaseJsonSerializerContext.Default.BaseSubjectRetirementPublicationFact);await using SqliteCommand insert=connection.CreateCommand();insert.Transaction=publishTransaction;insert.CommandTimeout=TimeoutSeconds();insert.CommandText=$"INSERT INTO {_names.SubjectRetirementPublications}(position,kind,scope_kind,scope_index_digest,protected_scope_value,payload) VALUES($position,$kind,NULL,NULL,NULL,$payload);";insert.Parameters.AddWithValue("$position",position);insert.Parameters.AddWithValue("$kind",(int)fact.Kind);insert.Parameters.Add("$payload",SqliteType.Blob).Value=payload;await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);position=checked(position+1);}
        await _administrationOperations.BeforePhaseAsync("subjectRetirementBeforeRestorePublicationCommit",cancellationToken).ConfigureAwait(false);await publishTransaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask PublishRestoredLifecycleDeliveryAuthorityAsync(
        SqliteConnection connection,
        long preRestoreLifecycleDeliveryEpoch,
        CancellationToken cancellationToken)
    {
        long artifactDeliveryEpoch;
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.CommandTimeout = TimeoutSeconds();
            read.CommandText = $"SELECT COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='subject_lifecycle_delivery_epoch'),1);";
            artifactDeliveryEpoch = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        long replacementDeliveryEpoch = checked(Math.Max(preRestoreLifecycleDeliveryEpoch, artifactDeliveryEpoch) + 1);

        long consumerCount = await CountLifecycleRowsAsync(connection, null, _names.SubjectLifecycleConsumers, cancellationToken).ConfigureAwait(false);
        long membershipCount = await CountLifecycleRowsAsync(connection, null, _names.SubjectLifecycleMemberships, cancellationToken).ConfigureAwait(false);
        long checkpointCount = await CountLifecycleRowsAsync(connection, null, _names.SubjectLifecycleCheckpoints, cancellationToken).ConfigureAwait(false);

        LifecycleRestoreEvidence evidence = new(0, 0, 0, SHA256.HashData("base.subjectLifecycle.restore.empty.v1"u8));
        evidence = await TransformRestoredLifecycleRowsAsync(connection, _names.SubjectLifecycleConsumers, checkpoint: false, evidence, cancellationToken).ConfigureAwait(false);
        evidence = await TransformRestoredLifecycleRowsAsync(connection, _names.SubjectLifecycleMemberships, checkpoint: false, evidence, cancellationToken).ConfigureAwait(false);
        evidence = await TransformRestoredLifecycleRowsAsync(connection, _names.SubjectLifecycleCheckpoints, checkpoint: true, evidence, cancellationToken).ConfigureAwait(false);
        if (evidence.Rows != checked(consumerCount + membershipCount + checkpointCount) || evidence.Bytes < 0 || evidence.Checksum.Length != 32)
            throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);

        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (SqliteCommand publish = connection.CreateCommand())
        {
            publish.Transaction = transaction;
            publish.CommandTimeout = TimeoutSeconds();
            publish.CommandText = $"""
INSERT INTO {_names.ProviderState}(key,value)
VALUES ('subject_lifecycle_delivery_epoch',$delivery)
ON CONFLICT(key) DO UPDATE SET value=excluded.value;
""";
            publish.Parameters.AddWithValue("$delivery", replacementDeliveryEpoch.ToString(CultureInfo.InvariantCulture));
            await publish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (consumerCount != await CountLifecycleRowsAsync(connection, transaction, _names.SubjectLifecycleConsumers, cancellationToken).ConfigureAwait(false)
            || membershipCount != await CountLifecycleRowsAsync(connection, transaction, _names.SubjectLifecycleMemberships, cancellationToken).ConfigureAwait(false)
            || checkpointCount != await CountLifecycleRowsAsync(connection, transaction, _names.SubjectLifecycleCheckpoints, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);

        await _administrationOperations.BeforePhaseAsync("subjectLifecycleBeforeRestorePublicationCommit", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<LifecycleRestoreEvidence> TransformRestoredLifecycleRowsAsync(
        SqliteConnection connection,
        string table,
        bool checkpoint,
        LifecycleRestoreEvidence prior,
        CancellationToken cancellationToken)
    {
        long after = 0;
        LifecycleRestoreEvidence evidence = prior;
        while (true)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandTimeout = TimeoutSeconds();
            select.CommandText = checkpoint
                ? $"SELECT rowid,projection_generation,checkpoint_generation FROM {table} WHERE rowid>$after ORDER BY rowid LIMIT 256;"
                : $"SELECT rowid,projection_generation FROM {table} WHERE rowid>$after ORDER BY rowid LIMIT 256;";
            select.Parameters.AddWithValue("$after", after);
            var rows = new List<(long RowId, long Projection, long? Checkpoint)>();
            await using (SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    rows.Add((reader.GetInt64(0), reader.GetInt64(1), checkpoint ? reader.GetInt64(2) : null));
            foreach ((long rowId, long projection, long? checkpointGeneration) in rows)
            {
                long nextProjection = checked(projection + 1);
                long? nextCheckpoint = checkpointGeneration is null ? null : checked(checkpointGeneration.Value + 1);
                await using SqliteCommand update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandTimeout = TimeoutSeconds();
                update.CommandText = checkpoint
                    ? $"UPDATE {table} SET projection_generation=$projection,checkpoint_generation=$checkpoint WHERE rowid=$rowid AND projection_generation=$priorProjection AND checkpoint_generation=$priorCheckpoint;"
                    : $"UPDATE {table} SET projection_generation=$projection WHERE rowid=$rowid AND projection_generation=$priorProjection;";
                update.Parameters.AddWithValue("$projection", nextProjection);
                update.Parameters.AddWithValue("$priorProjection", projection);
                update.Parameters.AddWithValue("$rowid", rowId);
                if (checkpoint)
                {
                    update.Parameters.AddWithValue("$checkpoint", nextCheckpoint!.Value);
                    update.Parameters.AddWithValue("$priorCheckpoint", checkpointGeneration!.Value);
                }
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                byte[] canonical = Encoding.UTF8.GetBytes($"{table}\0{rowId}\0{projection}\0{nextProjection}\0{checkpointGeneration}\0{nextCheckpoint}");
                evidence = evidence.Add(canonical);
                after = rowId;
            }
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            if (rows.Count != 0)
            {
                evidence = evidence with { Pages = checked(evidence.Pages + 1) };
                await _administrationOperations.BeforePhaseAsync("subjectLifecycleRestorePageCommitted", cancellationToken).ConfigureAwait(false);
            }
            if (rows.Count < 256) return evidence;
        }
    }

    private async ValueTask<long> CountLifecycleRowsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string table,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private sealed record LifecycleRestoreEvidence(long Rows, long Bytes, long Pages, byte[] Checksum)
    {
        internal LifecycleRestoreEvidence Add(byte[] canonical)
        {
            byte[] input = new byte[checked(Checksum.Length + 4 + canonical.Length)];
            Checksum.CopyTo(input, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(Checksum.Length, 4), canonical.Length);
            canonical.CopyTo(input, Checksum.Length + 4);
            return new(checked(Rows + 1), checked(Bytes + 4L + canonical.Length), Pages, SHA256.HashData(input));
        }
    }

    private async ValueTask<long> ReadSubjectLifecycleDeliveryEpochAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='subject_lifecycle_delivery_epoch'),1);";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private async ValueTask<SubjectRewriteCheckpoint> StageSubjectReferenceRewriteAsync(
        SqliteConnection connection,
        SqliteSubjectContractRow contract,
        CancellationToken cancellationToken,
        Func<long, long>? revisionFactory = null)
    {
        SubjectRewriteCheckpoint? existing = await ReadSubjectRewriteCheckpointAsync(connection, cancellationToken).ConfigureAwait(false);
        SubjectRewriteCheckpoint checkpoint;
        if (existing is null)
        {
            checkpoint = new SubjectRewriteCheckpoint(
                contract.ContractId, contract.ContractVersion, contract.StateGeneration,
                contract.AuthorityEpoch, BaseSubjectAuthorityEpoch.Create(), 0, string.Empty, 0, 0, 0,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData([])));
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandTimeout = TimeoutSeconds();
            clear.CommandText = $"DELETE FROM {_names.SubjectRewriteStage};";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await WriteSubjectRewriteCheckpointAsync(connection, transaction, checkpoint, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            checkpoint = existing;
            if (!string.Equals(checkpoint.ContractId, contract.ContractId, StringComparison.Ordinal)
                || checkpoint.ContractVersion != contract.ContractVersion
                || checkpoint.ExpectedGeneration != contract.StateGeneration
                || !checkpoint.OldEpoch.Equals(contract.AuthorityEpoch))
                throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);
            await ValidateSubjectRewriteEvidenceAsync(connection, checkpoint, complete: false, revisionFactory, cancellationToken).ConfigureAwait(false);
        }

        for (int collectionOrdinal = checkpoint.CollectionOrdinal; collectionOrdinal < _physical.Collections.Length; collectionOrdinal++)
        {
            SqlitePhysicalModel.CollectionModel collection = _physical.Collections[collectionOrdinal];
            FieldDefinition[] fields = collection.Fields.Where(field => field.Definition.SubjectReference is { } reference
                    && string.Equals(reference.ContractId, contract.ContractId, StringComparison.Ordinal)
                    && reference.ContractVersion == contract.ContractVersion)
                .Select(static field => field.Definition).ToArray();
            string after = collectionOrdinal == checkpoint.CollectionOrdinal ? checkpoint.LastRecordId : string.Empty;
            while (fields.Length != 0)
            {
                var page = new List<RecordEnvelope>(256);
                await using (SqliteCommand select = connection.CreateCommand())
                {
                    select.CommandTimeout = TimeoutSeconds();
                    select.CommandText = $"SELECT {collection.SelectList} FROM {collection.Table} WHERE record_id COLLATE BINARY > $after COLLATE BINARY ORDER BY record_id COLLATE BINARY LIMIT 256;";
                    select.Parameters.AddWithValue("$after", after);
                    await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) page.Add(collection.ReadEnvelope(reader, _options.StoreId));
                }
                if (page.Count == 0) break;
                await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                foreach (RecordEnvelope before in page)
                {
                    after = before.Id.Value;
                    long priorExamined = checkpoint.Examined;
                    long priorRewritten = checkpoint.Rewritten;
                    long priorBytes = checkpoint.CanonicalBytes;
                    string priorChecksum = checkpoint.Checksum;
                    Dictionary<string, JsonElement> values = SqliteRecordSerializer.NormalizeObjectPayload(before.Payload).Fields ?? [];
                    bool changed = false;
                    foreach (FieldDefinition field in fields)
                    {
                        if (!values.TryGetValue(field.WireName, out JsonElement value) || value.ValueKind == JsonValueKind.Null) continue;
                        if (!BaseSubjectReferenceEncoding.TryRewriteAuthorityEpoch(value, contract.AuthorityEpoch, checkpoint.NewEpoch, out JsonElement next))
                            throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);
                        values[field.WireName] = next;
                        changed = true;
                        checkpoint = checkpoint with { Rewritten = checked(checkpoint.Rewritten + 1) };
                    }
                    checkpoint = checkpoint with { Examined = checked(checkpoint.Examined + 1) };
                    if (!changed) continue;
                    long previousRevision = ParseSqliteRevision(before.Metadata.Revision);
                    long replacementRevision = revisionFactory is null
                        ? checked(previousRevision + 1)
                        : revisionFactory(previousRevision);
                    string payload = SqliteRecordSerializer.Serialize(new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = values });
                    byte[] payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
                    byte[] previousPayloadBytes = System.Text.Encoding.UTF8.GetBytes(SqliteRecordSerializer.Serialize(before.Payload));
                    checkpoint = checkpoint with
                    {
                        CanonicalBytes = checked(checkpoint.CanonicalBytes + payloadBytes.LongLength),
                        Checksum = ExtendSubjectRewriteChecksum(
                            checkpoint.Checksum, collection.Definition.Id, before.Id.Value,
                            previousRevision, replacementRevision, payloadBytes),
                    };
                    await using SqliteCommand stage = connection.CreateCommand();
                    stage.Transaction = transaction;
                    stage.CommandTimeout = TimeoutSeconds();
                    stage.CommandText = $"INSERT INTO {_names.SubjectRewriteStage}(collection_id,record_id,previous_revision,replacement_revision,previous_payload_json,payload_json) VALUES($collection,$record,$previous,$replacement,$before,$payload) ON CONFLICT(collection_id,record_id) DO NOTHING;";
                    stage.Parameters.AddWithValue("$collection", collection.Definition.Id);
                    stage.Parameters.AddWithValue("$record", before.Id.Value);
                    stage.Parameters.AddWithValue("$previous", previousRevision);
                    stage.Parameters.AddWithValue("$replacement", replacementRevision);
                    stage.Parameters.Add("$before", SqliteType.Blob).Value = previousPayloadBytes;
                    stage.Parameters.Add("$payload", SqliteType.Blob).Value = payloadBytes;
                    int stagedCount = await stage.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    if (stagedCount == 0)
                    {
                        checkpoint = checkpoint with
                        {
                            Examined = priorExamined,
                            Rewritten = priorRewritten,
                            CanonicalBytes = priorBytes,
                            Checksum = priorChecksum,
                        };
                    }
                }
                checkpoint = checkpoint with { CollectionOrdinal = collectionOrdinal, LastRecordId = after };
                await WriteSubjectRewriteCheckpointAsync(connection, transaction, checkpoint, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                await _administrationOperations.BeforePhaseAsync("subjectRewritePageCommitted", cancellationToken).ConfigureAwait(false);
            }
            checkpoint = checkpoint with { CollectionOrdinal = collectionOrdinal + 1, LastRecordId = string.Empty };
            await using SqliteTransaction progress = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await WriteSubjectRewriteCheckpointAsync(connection, progress, checkpoint, cancellationToken).ConfigureAwait(false);
            await progress.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        await ValidateSubjectRewriteEvidenceAsync(connection, checkpoint, complete: true, revisionFactory, cancellationToken).ConfigureAwait(false);
        return checkpoint;
    }

    private async ValueTask ApplyStagedSubjectReferenceRewriteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SubjectRewriteCheckpoint checkpoint,
        long publicationPosition,
        CancellationToken cancellationToken)
    {
        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        {
            string after = string.Empty;
            while (true)
            {
                var staged = new List<(string RecordId, long Previous, long Replacement, RecordPayload Before, RecordPayload After)>(256);
                await using (SqliteCommand select = connection.CreateCommand())
                {
                    select.Transaction = transaction;
                    select.CommandTimeout = TimeoutSeconds();
                    select.CommandText = $"SELECT record_id,previous_revision,replacement_revision,previous_payload_json,payload_json FROM {_names.SubjectRewriteStage} WHERE collection_id=$collection AND record_id COLLATE BINARY > $after COLLATE BINARY ORDER BY record_id COLLATE BINARY LIMIT 256;";
                    select.Parameters.AddWithValue("$collection", collection.Definition.Id);
                    select.Parameters.AddWithValue("$after", after);
                    await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        staged.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
                            SqliteRecordSerializer.Deserialize(System.Text.Encoding.UTF8.GetString((byte[])reader.GetValue(3))),
                            SqliteRecordSerializer.Deserialize(System.Text.Encoding.UTF8.GetString((byte[])reader.GetValue(4)))));
                }
                if (staged.Count == 0) break;
                var previousEnvelopes = new Dictionary<string, RecordEnvelope>(staged.Count, StringComparer.Ordinal);
            foreach ((string recordId, long previous, long replacement, RecordPayload expectedBefore, RecordPayload payload) in staged)
                {
                    after = recordId;
                    RecordEnvelope? currentRecord = await ReadAsync(
                        connection, collection.Definition.Id, recordId, cancellationToken, transaction, TimeoutSeconds()).ConfigureAwait(false);
                    if (currentRecord is null || ParseSqliteRevision(currentRecord.Metadata.Revision) != previous
                        || !string.Equals(SqliteRecordSerializer.Serialize(currentRecord.Payload), SqliteRecordSerializer.Serialize(expectedBefore), StringComparison.Ordinal))
                        throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                    previousEnvelopes.Add(recordId, currentRecord);
                    await using SqliteCommand update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandTimeout = TimeoutSeconds();
                    update.CommandText = $"UPDATE {collection.Table} SET revision=$replacement,updated_at=$updated,latest_mutation_position=$position{collection.PayloadAssignmentClause} WHERE record_id=$record AND revision=$previous;";
                    update.Parameters.AddWithValue("$replacement", replacement);
                    update.Parameters.AddWithValue("$updated", _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
                    update.Parameters.AddWithValue("$position", publicationPosition);
                    update.Parameters.AddWithValue("$record", recordId);
                    update.Parameters.AddWithValue("$previous", previous);
                    collection.AddPayloadParameters(update, payload, includeExtensions: true);
                    if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                        throw new InvalidOperationException(BaseSubjectErrorCodes.TransactionConflict);
                }
                var projectionFacts = new List<BaseRecordMutationFact>(staged.Count);
                foreach ((string recordId, long previous, long replacement, RecordPayload beforePayload, RecordPayload _) in staged)
                {
                    RecordEnvelope? afterRecord = await ReadAsync(
                        connection, collection.Definition.Id, recordId, cancellationToken, transaction, TimeoutSeconds()).ConfigureAwait(false);
                    if (afterRecord is null || ParseSqliteRevision(afterRecord.Metadata.Revision) != replacement)
                        throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                    string[] changedFields = collection.Fields
                        .Where(field => field.Definition.SubjectReference is { } reference
                            && string.Equals(reference.ContractId, checkpoint.ContractId, StringComparison.Ordinal)
                            && reference.ContractVersion == checkpoint.ContractVersion
                            && beforePayload.Fields?.TryGetValue(field.Definition.WireName, out JsonElement beforeValue) == true
                            && afterRecord.Payload.Fields?.TryGetValue(field.Definition.WireName, out JsonElement afterValue) == true
                            && !JsonElement.DeepEquals(beforeValue, afterValue))
                        .Select(field => field.Definition.WireName).Order(StringComparer.Ordinal).ToArray();
                    projectionFacts.Add(new BaseRecordMutationFact
                    {
                        RequestedOperation = BaseRecordMutationKind.Replace,
                        CommittedOperation = BaseCommittedRecordMutationKind.Replace,
                        Collection = collection.Definition,
                        Event = new EventReference
                        {
                            EventId = $"subject-maintenance:{publicationPosition}:{collection.Definition.Id}:{recordId}",
                            Type = "base.subject.authorityRotation",
                            Resource = recordId,
                            PublishedAt = _timeProvider.GetUtcNow(),
                            Guarantee = EventDeliveryGuarantee.Transactional,
                        },
                        JournalPosition = new BaseMutationJournalPosition(publicationPosition),
                        Before = previousEnvelopes[recordId] with { Payload = beforePayload },
                        After = afterRecord,
                        ChangedFields = changedFields,
                    });
                }
                if (projectionFacts.Count != 0 && !await ApplyAdministrationProjectionsAsync(
                        connection, transaction, projectionFacts.ToArray(), cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            }
        }
        await using SqliteCommand cleanup = connection.CreateCommand();
        cleanup.Transaction = transaction;
        cleanup.CommandTimeout = TimeoutSeconds();
        cleanup.CommandText = $"DELETE FROM {_names.SubjectRewriteStage}; DELETE FROM {_names.SubjectMaintenance} WHERE singleton=1;";
        await cleanup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ValidateSubjectRewriteEvidenceAsync(
        SqliteConnection connection,
        SubjectRewriteCheckpoint checkpoint,
        bool complete,
        Func<long, long>? revisionFactory,
        CancellationToken cancellationToken)
    {
        long examined = 0;
        long rewritten = 0;
        long canonicalBytes = 0;
        string checksum = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData([]));
        int ordinal = 0;
        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        {
            FieldDefinition[] fields = collection.Fields.Where(field => field.Definition.SubjectReference is { } reference
                    && string.Equals(reference.ContractId, checkpoint.ContractId, StringComparison.Ordinal)
                    && reference.ContractVersion == checkpoint.ContractVersion)
                .Select(static field => field.Definition).ToArray();
            bool fullyExamined = ordinal < checkpoint.CollectionOrdinal;
            string boundary = fullyExamined ? "" : ordinal == checkpoint.CollectionOrdinal ? checkpoint.LastRecordId : "";
            if (fields.Length != 0 && (fullyExamined || boundary.Length != 0))
            {
                await using SqliteCommand count = connection.CreateCommand();
                count.CommandTimeout = TimeoutSeconds();
                count.CommandText = fullyExamined
                    ? $"SELECT COUNT(*) FROM {collection.Table};"
                    : $"SELECT COUNT(*) FROM {collection.Table} WHERE record_id COLLATE BINARY <= $boundary COLLATE BINARY;";
                if (!fullyExamined) count.Parameters.AddWithValue("$boundary", boundary);
                examined = checked(examined + Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture));
            }
            ordinal++;
        }

        foreach (SqlitePhysicalModel.CollectionModel orderedCollection in _physical.Collections)
        {
            await using SqliteCommand select = connection.CreateCommand();
            select.CommandTimeout = TimeoutSeconds();
            select.CommandText = $"SELECT record_id,previous_revision,replacement_revision,previous_payload_json,payload_json FROM {_names.SubjectRewriteStage} WHERE collection_id=$collection ORDER BY record_id COLLATE BINARY;";
            select.Parameters.AddWithValue("$collection", orderedCollection.Definition.Id);
            await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string recordId = reader.GetString(0);
                long previousRevision = reader.GetInt64(1);
                long replacementRevision = reader.GetInt64(2);
                byte[] previousBytes = (byte[])reader.GetValue(3);
                byte[] replacementBytes = (byte[])reader.GetValue(4);
                if (previousRevision <= 0 || replacementRevision <= 0)
                    throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);
                long expectedReplacement;
                try
                {
                    expectedReplacement = revisionFactory is null
                        ? checked(previousRevision + 1)
                        : revisionFactory(previousRevision);
                }
                catch (OverflowException exception)
                {
                    throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid, exception);
                }
                if (replacementRevision != expectedReplacement)
                    throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);
                RecordPayload before = SqliteRecordSerializer.Deserialize(System.Text.Encoding.UTF8.GetString(previousBytes));
                RecordPayload replacement = SqliteRecordSerializer.Deserialize(System.Text.Encoding.UTF8.GetString(replacementBytes));
                if (!previousBytes.AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(SqliteRecordSerializer.Serialize(before)))
                    || !replacementBytes.AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(SqliteRecordSerializer.Serialize(replacement))))
                    throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);
                Dictionary<string, JsonElement> expectedValues = SqliteRecordSerializer.NormalizeObjectPayload(before).Fields ?? [];
                long rowRewrites = 0;
                foreach (FieldDefinition field in orderedCollection.Fields.Select(static value => value.Definition))
                {
                    if (field.SubjectReference is not { } reference
                        || !string.Equals(reference.ContractId, checkpoint.ContractId, StringComparison.Ordinal)
                        || reference.ContractVersion != checkpoint.ContractVersion
                        || !expectedValues.TryGetValue(field.WireName, out JsonElement value)
                        || value.ValueKind == JsonValueKind.Null) continue;
                    if (!BaseSubjectReferenceEncoding.TryRewriteAuthorityEpoch(value, checkpoint.OldEpoch, checkpoint.NewEpoch, out JsonElement next))
                        throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);
                    expectedValues[field.WireName] = next;
                    rowRewrites++;
                }
                byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(SqliteRecordSerializer.Serialize(
                    new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = expectedValues }));
                if (rowRewrites == 0 || !expectedBytes.AsSpan().SequenceEqual(replacementBytes))
                    throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);
                rewritten = checked(rewritten + rowRewrites);
                canonicalBytes = checked(canonicalBytes + replacementBytes.LongLength);
                checksum = ExtendSubjectRewriteChecksum(
                    checksum, orderedCollection.Definition.Id, recordId,
                    previousRevision, replacementRevision, replacementBytes);
            }
        }
        if (examined != checkpoint.Examined || rewritten != checkpoint.Rewritten
            || canonicalBytes != checkpoint.CanonicalBytes || !string.Equals(checksum, checkpoint.Checksum, StringComparison.Ordinal)
            || complete && checkpoint.CollectionOrdinal != _physical.Collections.Length)
            throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);
    }

    private async ValueTask<SubjectRewriteCheckpoint?> ReadSubjectRewriteCheckpointAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT contract_id,contract_version,expected_generation,old_epoch,new_epoch,collection_ordinal,last_record_id,examined_count,rewritten_count,canonical_bytes,checksum FROM {_names.SubjectMaintenance} WHERE singleton=1;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new SubjectRewriteCheckpoint(reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2),
            new BaseSubjectAuthorityEpoch((byte[])reader.GetValue(3)), new BaseSubjectAuthorityEpoch((byte[])reader.GetValue(4)),
            reader.GetInt32(5), reader.GetString(6), reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9), reader.GetString(10));
    }

    private async ValueTask WriteSubjectRewriteCheckpointAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SubjectRewriteCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"INSERT INTO {_names.SubjectMaintenance}(singleton,contract_id,contract_version,expected_generation,old_epoch,new_epoch,collection_ordinal,last_record_id,examined_count,rewritten_count,canonical_bytes,checksum) VALUES(1,$contract,$version,$generation,$old,$new,$collection,$record,$examined,$rewritten,$bytes,$checksum) ON CONFLICT(singleton) DO UPDATE SET collection_ordinal=excluded.collection_ordinal,last_record_id=excluded.last_record_id,examined_count=excluded.examined_count,rewritten_count=excluded.rewritten_count,canonical_bytes=excluded.canonical_bytes,checksum=excluded.checksum;";
        command.Parameters.AddWithValue("$contract", checkpoint.ContractId);
        command.Parameters.AddWithValue("$version", checkpoint.ContractVersion);
        command.Parameters.AddWithValue("$generation", checkpoint.ExpectedGeneration);
        command.Parameters.Add("$old", SqliteType.Blob).Value = checkpoint.OldEpoch.ToArray();
        command.Parameters.Add("$new", SqliteType.Blob).Value = checkpoint.NewEpoch.ToArray();
        command.Parameters.AddWithValue("$collection", checkpoint.CollectionOrdinal);
        command.Parameters.AddWithValue("$record", checkpoint.LastRecordId);
        command.Parameters.AddWithValue("$examined", checkpoint.Examined);
        command.Parameters.AddWithValue("$rewritten", checkpoint.Rewritten);
        command.Parameters.AddWithValue("$bytes", checkpoint.CanonicalBytes);
        command.Parameters.AddWithValue("$checksum", checkpoint.Checksum);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ExtendSubjectRewriteChecksum(
        string prior,
        string collection,
        string record,
        long previousRevision,
        long replacementRevision,
        byte[] payload)
    {
        byte[] prefix = System.Text.Encoding.UTF8.GetBytes(prior + "\n" + collection + "\n" + record + "\n");
        byte[] input = new byte[prefix.Length + 16 + payload.Length];
        prefix.CopyTo(input, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(input.AsSpan(prefix.Length, 8), previousRevision);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(input.AsSpan(prefix.Length + 8, 8), replacementRevision);
        payload.CopyTo(input, prefix.Length + 16);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(input));
    }

    private sealed record SubjectRewriteCheckpoint(
        string ContractId,
        int ContractVersion,
        long ExpectedGeneration,
        BaseSubjectAuthorityEpoch OldEpoch,
        BaseSubjectAuthorityEpoch NewEpoch,
        int CollectionOrdinal,
        string LastRecordId,
        long Examined,
        long Rewritten,
        long CanonicalBytes,
        string Checksum);

    private async ValueTask<bool> ApplyAdministrationProjectionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BaseRecordMutationFact[] facts,
        CancellationToken cancellationToken)
    {
        foreach (ISqliteAtomicMutationProjection contributor in _mutationProjectionContributors)
        {
            var context = new SubjectAdministrationProjectionContext(
                this,
                connection,
                transaction,
                (ISqliteAtomicMutationProjectionCatalog)contributor);
            OperationResult projected = await contributor.ApplyAsync(
                context,
                BaseAtomicMutationProjectionFactory.Create(facts),
                cancellationToken).ConfigureAwait(false);
            if (!projected.IsSuccess())
                return false;
        }
        return true;
    }

    internal static long RestoreDerivedRevision(long restoreEpoch, long artifactRevision)
    {
        Span<byte> source = stackalloc byte[24];
        "hpd-rv1"u8.CopyTo(source);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(source[8..], restoreEpoch);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(source[16..], artifactRevision);
        Span<byte> digest = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(source, digest);
        long value = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(digest) & long.MaxValue;
        return value == 0 ? 1 : value;
    }

    private static string SubjectContractKey(string contractId, int contractVersion) =>
        contractId + "\u001f" + contractVersion.ToString(CultureInfo.InvariantCulture);

    private async ValueTask<SqliteSubjectContractRow?> ReadSubjectContractAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string contractId,
        int contractVersion,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT contract_checksum,authority_epoch,restore_epoch,state_generation,publication_previous_generation,publication_kind,publication_position,publication_digest FROM {_names.SubjectContracts} WHERE contract_id=$contract AND contract_version=$version;";
        command.Parameters.AddWithValue("$contract", contractId);
        command.Parameters.AddWithValue("$version", contractVersion);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new SqliteSubjectContractRow(
            contractId,
            contractVersion,
            reader.GetString(0),
            new BaseSubjectAuthorityEpoch((byte[])reader.GetValue(1)),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            (BaseSubjectAuthorityPublicationKind)reader.GetInt32(5),
            reader.GetInt64(6),
            reader.GetString(7));
    }

    private bool ValidSubjectPublicationReceipt(SqliteSubjectContractRow row)
    {
        if (!Enum.IsDefined(row.PublicationKind)
            || row.StateGeneration <= 0
            || row.PublicationPosition <= 0
            || row.PreviousStateGeneration < 0)
            return false;
        string digest = BaseSubjectPublicationIntegrity.Compute(
            row.ContractId,
            row.ContractVersion,
            row.ContractChecksum,
            row.PreviousStateGeneration,
            row.StateGeneration,
            row.RestoreEpoch,
            row.PublicationKind,
            new BaseMutationJournalPosition(row.PublicationPosition),
            row.AuthorityEpoch);
        return string.Equals(digest, row.PublicationDigest, StringComparison.Ordinal);
    }

    private async ValueTask<long> AppendSubjectPublicationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string contractId,
        int contractVersion,
        long previousGeneration,
        long publishedGeneration,
        long restoreEpoch,
        BaseSubjectAuthorityPublicationKind kind,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"INSERT INTO {_names.MutationJournal}(entry_kind,subject_contract_id,subject_contract_version,subject_previous_generation,subject_published_generation,subject_restore_epoch,subject_publication_kind) VALUES(1,$contract,$version,$previous,$published,$restore,$kind) RETURNING position;";
        command.Parameters.AddWithValue("$contract", contractId);
        command.Parameters.AddWithValue("$version", contractVersion);
        command.Parameters.AddWithValue("$previous", previousGeneration);
        command.Parameters.AddWithValue("$published", publishedGeneration);
        command.Parameters.AddWithValue("$restore", restoreEpoch);
        command.Parameters.AddWithValue("$kind", (int)kind);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static long ParseSqliteRevision(RevisionToken? revision)
    {
        if (!SqliteRecordMapper.TryParseRevision(revision, out long value))
            throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);
        return value;
    }

    private static OperationResult<T> SubjectAdministrationFailure<T>(
        string code,
        OperationStatus status = OperationStatus.StoreError,
        ErrorCategory category = ErrorCategory.Store) => new()
        {
            Status = status,
            Error = new BaseError
            {
                Code = code,
                Message = SubjectPublicMessage(code),
                Category = category,
            },
        };

    private static string SubjectPublicMessage(string code) => code switch
    {
        BaseSubjectErrorCodes.ContractInvalid => "The subject contract is invalid.",
        BaseSubjectErrorCodes.SchemaGenerationChanged => "The subject validation authority changed.",
        BaseSubjectErrorCodes.TransactionConflict => "The subject validation transaction conflicted.",
        BaseSubjectErrorCodes.ValidationUnavailable => "Subject validation is unavailable.",
        _ => "The subject validation provider returned an invalid result.",
    };

    private sealed record SqliteSubjectContractRow(
        string ContractId,
        int ContractVersion,
        string ContractChecksum,
        BaseSubjectAuthorityEpoch AuthorityEpoch,
        long RestoreEpoch,
        long StateGeneration,
        long PreviousStateGeneration,
        BaseSubjectAuthorityPublicationKind PublicationKind,
        long PublicationPosition,
        string PublicationDigest);

    private sealed class SubjectAdministrationProjectionContext(
        SqliteRecordStore owner,
        SqliteConnection connection,
        SqliteTransaction transaction,
        ISqliteAtomicMutationProjectionCatalog catalog) : ISqliteAtomicProjectionContext
    {
        public long SchemaGeneration => owner.VectorSchemaGeneration;

        public async ValueTask<OperationResult<int>> ExecuteAsync(
            string statementId,
            ImmutableArray<SqliteProjectionValue> parameters,
            CancellationToken cancellationToken = default)
        {
            SqliteProjectionStatement? statement = catalog.Statements.SingleOrDefault(item =>
                string.Equals(item.Id, statementId, StringComparison.Ordinal));
            if (statement is null
                || parameters.IsDefault
                || parameters.Select(static item => item.Name).Distinct(StringComparer.Ordinal).Count() != parameters.Length
                || !statement.ParameterNames.SequenceEqual(parameters.Select(static item => item.Name), StringComparer.Ordinal))
            {
                return SubjectAdministrationFailure<int>(BaseSubjectErrorCodes.ProviderContractInvalid);
            }
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = owner.VectorCommandTimeoutSeconds;
            command.CommandText = statement.Sql;
            foreach (SqliteProjectionValue parameter in parameters)
                command.Parameters.AddWithValue("$" + parameter.Name, parameter.Value);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return affected <= statement.MaximumAffectedRows
                ? OperationResults.Ok(affected)
                : SubjectAdministrationFailure<int>(BaseSubjectErrorCodes.ProviderContractInvalid);
        }
    }
}
