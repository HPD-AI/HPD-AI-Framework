using System.Collections.Immutable;
using System.Globalization;
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
                StoreInstanceId = new string(_options.StoreId.AsSpan()),
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

            await using SqliteConnection connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            SqliteSubjectContractRow? contract = await ReadSubjectContractAsync(
                connection, transaction, request.ContractId, request.ContractVersion, cancellationToken).ConfigureAwait(false);
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

            long publishedGeneration = checked(contract.StateGeneration + 1);
            BaseSubjectAuthorityEpoch replacement = BaseSubjectAuthorityEpoch.Create();
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

            (long examined, long rewritten, BaseRecordMutationFact[] facts) = await RewriteCurrentSubjectReferencesAsync(
                connection,
                transaction,
                request.ContractId,
                request.ContractVersion,
                contract.AuthorityEpoch,
                replacement,
                publicationPosition,
                cancellationToken).ConfigureAwait(false);

            if (!await ApplyAdministrationProjectionsAsync(connection, transaction, facts, cancellationToken).ConfigureAwait(false))
                return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.ProviderContractInvalid);

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

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok(new BaseSubjectEpochRotationResult
            {
                ContractId = new string(request.ContractId.AsSpan()),
                ContractVersion = request.ContractVersion,
                PreviousStateGeneration = contract.StateGeneration,
                PublishedStateGeneration = publishedGeneration,
                PublicationPosition = new BaseMutationJournalPosition(publicationPosition),
                ExaminedRecords = examined,
                RewrittenReferences = rewritten,
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
            return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(
                BaseSubjectErrorCodes.ContractInvalid,
                OperationStatus.ValidationFailed,
                ErrorCategory.Validation);
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
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (SqliteCommand updateRestore = connection.CreateCommand())
        {
            updateRestore.Transaction = transaction;
            updateRestore.CommandTimeout = TimeoutSeconds();
            updateRestore.CommandText = $"INSERT INTO {_names.ProviderState}(key,value) VALUES ('restore_epoch',$epoch) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
            updateRestore.Parameters.AddWithValue("$epoch", restoreEpoch.ToString(CultureInfo.InvariantCulture));
            await updateRestore.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (BaseExportedSubjectDefinition definition in _options.ExportedSubjects
            .OrderBy(static definition => definition.Id, StringComparer.Ordinal)
            .ThenBy(static definition => definition.Version))
        {
            SqliteSubjectContractRow? artifact = await ReadSubjectContractAsync(
                connection,
                transaction,
                definition.Id,
                definition.Version,
                cancellationToken).ConfigureAwait(false);
            if (artifact is null
                || !string.Equals(artifact.ContractChecksum, definition.ValidationPlan.ContractChecksum, StringComparison.Ordinal)
                || !ValidSubjectPublicationReceipt(artifact))
                throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);

            preRestoreGenerations.TryGetValue(SubjectContractKey(definition.Id, definition.Version), out long preRestore);
            long previousGeneration = Math.Max(preRestore, artifact.StateGeneration);
            long publishedGeneration = checked(previousGeneration + 1);
            BaseSubjectAuthorityEpoch replacement = BaseSubjectAuthorityEpoch.Create();
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
            (_, _, BaseRecordMutationFact[] facts) = await RewriteCurrentSubjectReferencesAsync(
                connection,
                transaction,
                definition.Id,
                definition.Version,
                artifact.AuthorityEpoch,
                replacement,
                publicationPosition,
                cancellationToken,
                revisionFactory: revision => RestoreDerivedRevision(restoreEpoch, revision)).ConfigureAwait(false);
            if (!await ApplyAdministrationProjectionsAsync(connection, transaction, facts, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);

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
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<(long Examined, long Rewritten, BaseRecordMutationFact[] Facts)> RewriteCurrentSubjectReferencesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string contractId,
        int contractVersion,
        BaseSubjectAuthorityEpoch expected,
        BaseSubjectAuthorityEpoch replacement,
        long publicationPosition,
        CancellationToken cancellationToken,
        Func<long, long>? revisionFactory = null)
    {
        long examined = 0;
        long rewritten = 0;
        var facts = new List<BaseRecordMutationFact>();
        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        {
            FieldDefinition[] fields = collection.Fields
                .Where(field => field.Definition.SubjectReference is { } reference
                    && string.Equals(reference.ContractId, contractId, StringComparison.Ordinal)
                    && reference.ContractVersion == contractVersion)
                .Select(static field => field.Definition)
                .ToArray();
            if (fields.Length == 0)
                continue;

            var records = new List<RecordEnvelope>();
            await using (SqliteCommand select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandTimeout = TimeoutSeconds();
                select.CommandText = $"SELECT {collection.SelectList} FROM {collection.Table} ORDER BY record_id COLLATE BINARY;";
                await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    examined = checked(examined + 1);
                    records.Add(collection.ReadEnvelope(reader, _options.StoreId));
                }
            }

            foreach (RecordEnvelope before in records)
            {
                Dictionary<string, JsonElement> values = SqliteRecordSerializer.NormalizeObjectPayload(before.Payload).Fields ?? [];
                bool changed = false;
                foreach (FieldDefinition field in fields)
                {
                    if (!values.TryGetValue(field.WireName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
                        continue;
                    if (!BaseSubjectReferenceEncoding.TryRewriteAuthorityEpoch(value, expected, replacement, out JsonElement next))
                        throw new InvalidDataException(BaseSubjectErrorCodes.ProviderContractInvalid);
                    values[field.WireName] = next;
                    changed = true;
                    rewritten = checked(rewritten + 1);
                }
                if (!changed)
                    continue;

                long revision = ParseSqliteRevision(before.Metadata.Revision);
                long nextRevision = revisionFactory is null ? checked(revision + 1) : revisionFactory(revision);
                DateTimeOffset updatedAt = _timeProvider.GetUtcNow();
                var payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = values };
                await using (SqliteCommand update = connection.CreateCommand())
                {
                    update.Transaction = transaction;
                    update.CommandTimeout = TimeoutSeconds();
                    update.CommandText = $"UPDATE {collection.Table} SET revision=$revision, updated_at=$updated, latest_mutation_position=$position{collection.PayloadAssignmentClause} WHERE record_id=$id AND revision=$previous;";
                    update.Parameters.AddWithValue("$revision", nextRevision);
                    update.Parameters.AddWithValue("$updated", updatedAt.ToString("O", CultureInfo.InvariantCulture));
                    update.Parameters.AddWithValue("$position", publicationPosition);
                    update.Parameters.AddWithValue("$id", before.Id.Value);
                    update.Parameters.AddWithValue("$previous", revision);
                    collection.AddPayloadParameters(update, payload, includeExtensions: true);
                    if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                        throw new InvalidOperationException(BaseSubjectErrorCodes.TransactionConflict);
                }
                RecordEnvelope after = before with
                {
                    Payload = SqliteRecordSerializer.Clone(payload),
                    Metadata = SqliteRecordMapper.Metadata(nextRevision, before.Metadata.CreatedAt!.Value, updatedAt, _options.StoreId),
                };
                facts.Add(new BaseRecordMutationFact
                {
                    RequestedOperation = BaseRecordMutationKind.Replace,
                    CommittedOperation = BaseCommittedRecordMutationKind.Replace,
                    Collection = collection.Definition,
                    Event = new EventReference
                    {
                        EventId = $"subject-rotation:{contractId}:{contractVersion}:{publicationPosition}:{before.Id.Value}",
                        Type = "base.subject.authorityRotation",
                        Resource = before.Id.Value,
                        PublishedAt = updatedAt,
                        Guarantee = EventDeliveryGuarantee.Transactional,
                    },
                    JournalPosition = new BaseMutationJournalPosition(publicationPosition),
                    Before = before,
                    After = after,
                    ChangedFields = fields.Select(static field => field.WireName).ToArray(),
                });
            }
        }
        return (examined, rewritten, facts.ToArray());
    }

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

    private static long RestoreDerivedRevision(long restoreEpoch, long artifactRevision)
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
