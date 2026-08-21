using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

internal sealed class SqliteTextProvider(SqliteRecordStore store, BaseCollectionRegistry collections, SqliteTextModel model) : IBaseTextProvider, IBaseTextAuthority
{
    public IBaseTextAuthority Authority => this;
    public BaseTextProviderDescriptor Descriptor { get; } = new()
    {
        Id = "sqlite.fts5", Version = 1, ProviderClass = BaseTextProviderClass.CoLocatedTransactional,
        Capability = BaseTextPlatform.ProviderCapability(BaseTextProviderClass.CoLocatedTransactional),
        NativeDependencyReceipts = ["sqlite-bundled"], CertificationReceipt = ImmutableArray.Create(Convert.FromHexString("5d7ae5c0f421bde25eaba0b623e53163d44dffcfad8257ba67d4cef67f3d0726")),
    };

    public async ValueTask<OperationResult<IBaseTextHydrationSession>> OpenAsync(BaseTextAuthorityOpenRequest request, CancellationToken cancellationToken = default)
    {
        if (!collections.Collections.TryGetValue(request.CollectionId, out CollectionDefinition? collection)) return Missing();
        BaseTextIndexDefinition? index = collection.TextIndexes?.SingleOrDefault(value => value.Id == request.TextIndexId && value.Version == request.TextIndexVersion); if (index is null) return Missing();
        IAsyncDisposable lease = await store.AcquireVectorGenerationSharedAsync(cancellationToken).ConfigureAwait(false); SqliteConnection? connection = null;
        try
        {
            connection = await store.VectorConnections.OpenAsync(cancellationToken).ConfigureAwait(false); SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
            BaseTextAuthoritySnapshot snapshot = await Snapshot(connection, transaction, collection, index, cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok<IBaseTextHydrationSession>(new Session(store, connection, transaction, lease, collection, index, model.Get(collection.Id, index.Id), Descriptor, snapshot));
        }
        catch { if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false); await lease.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private async ValueTask<BaseTextAuthoritySnapshot> Snapshot(SqliteConnection connection, SqliteTransaction transaction, CollectionDefinition collection, BaseTextIndexDefinition index, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = store.VectorCommandTimeoutSeconds;
        command.CommandText = $"SELECT i.store_instance_id,COALESCE((SELECT CAST(value AS INTEGER) FROM {store.VectorNames.ProviderState} WHERE key='restore_epoch'),0),COALESCE((SELECT MAX(generation) FROM {store.VectorNames.SchemaBaseline}),0),t.purge_generation,COALESCE((SELECT MAX(position) FROM {store.VectorNames.MutationJournal}),0),t.generation,t.applied_position,t.definition_checksum,t.state FROM {store.VectorNames.SchemaIdentity} i JOIN {SqliteTextModel.StateTable} t ON t.collection_id=$collection AND t.index_id=$index LIMIT 1;"; command.Parameters.AddWithValue("$collection", collection.Id); command.Parameters.AddWithValue("$index", index.Id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException(BaseTextErrorCodes.IndexUnavailable);
        if (!string.Equals(reader.GetString(8), "ready", StringComparison.Ordinal) || !reader.GetFieldValue<byte[]>(7).AsSpan().SequenceEqual(index.DefinitionChecksum.AsSpan())) throw new InvalidOperationException(BaseTextErrorCodes.RebuildRequired);
        long head = reader.GetInt64(4); long applied = reader.GetInt64(6);
        return new BaseTextAuthoritySnapshot { StoreIdentityDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reader.GetString(0)))), RestoreEpoch = reader.GetInt64(1), SchemaGeneration = reader.GetInt64(2), CollectionId = collection.Id, PurgeGeneration = reader.GetInt64(3), TextIndexId = index.Id, TextIndexVersion = index.Version, TextIndexGeneration = reader.GetInt64(5), AuthoritativeHead = new(head), AppliedThrough = new(applied), SearchVisibleThrough = new(applied), AnalyzerReceipt = BaseTextContractReceipts.AnalyzerReceipt, ScoringReceipt = BaseTextContractReceipts.ScoringReceipt };
    }
    private static OperationResult<IBaseTextHydrationSession> Missing() => OperationResults.NotFound<IBaseTextHydrationSession>(new BaseError { Code = BaseTextErrorCodes.IndexNotFound, Message = "The text search index was not found.", Category = ErrorCategory.NotFound });

    public async ValueTask<OperationResult<BaseTextIndexStatus[]>> ListAsync(CancellationToken cancellationToken = default)
    { var values = new List<BaseTextIndexStatus>(); foreach (SqliteTextModel.IndexModel index in model.Indexes) { OperationResult<BaseTextIndexStatus> status = await GetAsync(index.Collection.Id, index.Definition.Id, cancellationToken).ConfigureAwait(false); if (!status.IsSuccess() || status.Value is null) return new() { Status = status.Status, Error = status.Error }; values.Add(status.Value); } return OperationResults.Ok(values.ToArray()); }
    public async ValueTask<OperationResult<BaseTextIndexStatus>> GetAsync(string collectionId, string textIndexId, CancellationToken cancellationToken = default)
    {
        SqliteTextModel.IndexModel? index = model.Indexes.SingleOrDefault(value => value.Collection.Id == collectionId && value.Definition.Id == textIndexId); if (index is null) return OperationResults.NotFound<BaseTextIndexStatus>(new BaseError { Code = BaseTextErrorCodes.IndexNotFound, Message = "The text search index was not found.", Category = ErrorCategory.NotFound });
        await using SqliteConnection connection = await store.VectorConnections.OpenAsync(cancellationToken).ConfigureAwait(false); await using SqliteCommand command = connection.CreateCommand(); command.CommandTimeout = store.VectorCommandTimeoutSeconds; command.CommandText = $"SELECT generation,purge_generation,applied_position,state,(SELECT COUNT(*) FROM {index.Table} carriers WHERE carriers.generation={SqliteTextModel.StateTable}.generation) FROM {SqliteTextModel.StateTable} WHERE collection_id=$collection AND index_id=$index;"; command.Parameters.AddWithValue("$collection", collectionId); command.Parameters.AddWithValue("$index", textIndexId); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return OperationResults.NotFound<BaseTextIndexStatus>(new BaseError { Code = BaseTextErrorCodes.IndexNotFound, Message = "The text search index was not found.", Category = ErrorCategory.NotFound }); long applied = reader.GetInt64(2); string state = reader.GetString(3); return OperationResults.Ok(new BaseTextIndexStatus { CollectionId = collectionId, TextIndexId = textIndexId, Version = index.Definition.Version, ProviderId = Descriptor.Id, Generation = reader.GetInt64(0), PurgeGeneration = reader.GetInt64(1), State = state == "ready" ? BaseTextIndexState.Ready : state == "building" ? BaseTextIndexState.Building : BaseTextIndexState.RebuildRequired, AppliedThrough = new(applied), SearchVisibleThrough = new(applied), CarrierCount = reader.GetInt64(4) });
    }
    public async ValueTask<OperationResult<BaseTextRebuildResult>> RebuildAsync(BaseTextRebuildRequest request, CancellationToken cancellationToken = default)
    {
        SqliteTextModel.IndexModel? index = model.Indexes.SingleOrDefault(value => value.Collection.Id == request.CollectionId && value.Definition.Id == request.TextIndexId);
        if (index is null) return OperationResults.NotFound<BaseTextRebuildResult>(new BaseError { Code = BaseTextErrorCodes.IndexNotFound, Message = "The text search index was not found.", Category = ErrorCategory.NotFound });
        byte[] fingerprint = TextRebuildFingerprint(request);
        await using IAsyncDisposable lease = await store.AcquireVectorGenerationExclusiveAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await store.VectorConnections.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await ReadRebuildReceiptAsync(connection, request, fingerprint, cancellationToken).ConfigureAwait(false) is { } historical) { if (historical.IsSuccess() && historical.Value is { } prior) await CleanupPublishedAsync(connection, index, request, prior, cancellationToken).ConfigureAwait(false); return historical; }
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (await InitializeRebuildAsync(connection, index, request, fingerprint, cancellationToken).ConfigureAwait(false) is { } initializationFailure) return initializationFailure;
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        RebuildProgress progress = await ReadProgressAsync(connection, null, request, fingerprint, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException(BaseTextErrorCodes.ProviderContractInvalid);
                        if (progress.ScanComplete) break;
                        await StageRebuildPageAsync(connection, index, request, fingerprint, progress, cancellationToken).ConfigureAwait(false);
                    }
                    return await PublishRebuildAsync(connection, index, request, fingerprint, cancellationToken).ConfigureAwait(false);
                }
                catch (TextRebuildSourceChangedException) when (attempt < 2) { }
            }
            return OperationResults.Conflict<BaseTextRebuildResult>(new BaseError { Code = BaseTextErrorCodes.GenerationChanged, Message = "The text search generation changed.", Category = ErrorCategory.Conflict });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TextRebuildBudgetException) { return new() { Status = OperationStatus.ValidationFailed, Error = new BaseError { Code = BaseTextErrorCodes.BudgetExceeded, Message = "The text rebuild exceeded an installed bound.", Category = ErrorCategory.Validation } }; }
        catch (InvalidDataException) { return new() { Status = OperationStatus.StoreError, Error = new BaseError { Code = BaseTextErrorCodes.ProviderContractInvalid, Message = "The text provider returned invalid rebuild evidence.", Category = ErrorCategory.Store } }; }
        catch { return Failure(); }
    }

    private static async ValueTask<OperationResult<BaseTextRebuildResult>?> ReadRebuildReceiptAsync(SqliteConnection connection, BaseTextRebuildRequest request, byte[] fingerprint, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = $"SELECT fingerprint,previous_generation,published_generation,visible_through,record_count,publication_checksum FROM {SqliteTextModel.RebuildReceiptTable} WHERE scope=$scope AND operation=$operation AND idempotency_key=$key;"; Identity(command, request);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (!CryptographicOperations.FixedTimeEquals(reader.GetFieldValue<byte[]>(0), fingerprint)) return OperationResults.Conflict<BaseTextRebuildResult>(new BaseError { Code = BaseMutationRequestErrorCodes.FingerprintConflict, Message = "The text rebuild identity conflicts with stored evidence.", Category = ErrorCategory.Conflict });
        return OperationResults.Ok(new BaseTextRebuildResult { PreviousGeneration = reader.GetInt64(1), PublishedGeneration = reader.GetInt64(2), VisibleThrough = new(reader.GetInt64(3)), RecordCount = reader.GetInt64(4), PublicationChecksum = ImmutableArray.Create(reader.GetFieldValue<byte[]>(5)) });
    }

    private async ValueTask<OperationResult<BaseTextRebuildResult>?> InitializeRebuildAsync(SqliteConnection connection, SqliteTextModel.IndexModel index, BaseTextRebuildRequest request, byte[] fingerprint, CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = connection.BeginTransaction();
        try
        {
            (long generation, long head) = await CurrentRebuildAuthorityAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            if (generation != request.ExpectedGeneration) { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); return OperationResults.Conflict<BaseTextRebuildResult>(new BaseError { Code = BaseTextErrorCodes.RebuildRequired, Message = "The text rebuild conflicts with current index state.", Category = ErrorCategory.Conflict }); }
            RebuildProgress? existing = await ReadProgressAsync(connection, transaction, request, fingerprint, cancellationToken).ConfigureAwait(false);
            if (existing is not null && existing.SourceHead == head) { await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); return null; }
            await DeleteStagingAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            StageEvidence empty = StageEvidence.Empty;
            await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction = transaction; insert.CommandText = $"INSERT INTO {SqliteTextModel.RebuildProgressTable}(scope,operation,idempotency_key,fingerprint,collection_id,index_id,expected_generation,staging_generation,source_head,last_record_id,record_count,canonical_bytes,rolling_checksum,scan_complete) VALUES($scope,$operation,$key,$fingerprint,$collection,$index,$expected,$staging,$head,NULL,0,$bytes,$checksum,0);"; Identity(insert, request); insert.Parameters.AddWithValue("$fingerprint", fingerprint); insert.Parameters.AddWithValue("$collection", request.CollectionId); insert.Parameters.AddWithValue("$index", request.TextIndexId); insert.Parameters.AddWithValue("$expected", request.ExpectedGeneration); insert.Parameters.AddWithValue("$staging", checked(request.ExpectedGeneration + 1)); insert.Parameters.AddWithValue("$head", head); insert.Parameters.AddWithValue("$bytes", empty.CanonicalBytes); insert.Parameters.AddWithValue("$checksum", empty.Checksum); await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); return null;
        }
        catch { try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { } throw; }
    }

    private async ValueTask StageRebuildPageAsync(SqliteConnection connection, SqliteTextModel.IndexModel index, BaseTextRebuildRequest request, byte[] fingerprint, RebuildProgress expected, CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = connection.BeginTransaction();
        try
        {
            RebuildProgress current = await ReadProgressAsync(connection, transaction, request, fingerprint, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException(BaseTextErrorCodes.ProviderContractInvalid);
            if (!ProgressEquals(current, expected)) throw new InvalidDataException(BaseTextErrorCodes.ProviderContractInvalid);
            (long generation, long head) = await CurrentRebuildAuthorityAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false); if (generation != request.ExpectedGeneration) throw new InvalidDataException(BaseTextErrorCodes.RebuildRequired); if (head != current.SourceHead) throw new TextRebuildSourceChangedException();
            SqlitePhysicalModel.CollectionModel physical = store.VectorPhysicalModel.Collection(request.CollectionId); var records = new List<RecordEnvelope>(1024);
            await using (SqliteCommand scan = connection.CreateCommand())
            {
                scan.Transaction = transaction; scan.CommandText = $"SELECT {physical.SelectList} FROM {physical.Table} WHERE ($last IS NULL OR record_id COLLATE BINARY > $last COLLATE BINARY) ORDER BY record_id COLLATE BINARY LIMIT 1024;"; scan.Parameters.AddWithValue("$last", (object?)current.LastRecordId ?? DBNull.Value);
                await using SqliteDataReader reader = await scan.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) records.Add(physical.ReadEnvelope(reader, store.VectorStoreId));
            }
            foreach (RecordEnvelope record in records)
            {
                string content = BaseTextSemanticEvaluator.NormalizedCarrierText(record.Payload, index.Definition); await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction = transaction; insert.CommandText = $"INSERT INTO {SqliteTextModel.RebuildStageTable}(scope,operation,idempotency_key,record_id,revision,journal_position,content) VALUES($scope,$operation,$key,$record,$revision,$position,$content) ON CONFLICT(scope,operation,idempotency_key,record_id) DO UPDATE SET revision=excluded.revision,journal_position=excluded.journal_position,content=excluded.content;"; Identity(insert, request); insert.Parameters.AddWithValue("$record", record.Id.Value); insert.Parameters.AddWithValue("$revision", record.Metadata.Revision!.Value.Value); insert.Parameters.AddWithValue("$position", current.SourceHead); insert.Parameters.AddWithValue("$content", content); await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            StageEvidence evidence = await ComputeStageEvidenceAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false); if (evidence.RecordCount > Descriptor.Capability.MaximumRebuildStagingRows || evidence.CanonicalBytes > Descriptor.Capability.MaximumRebuildBytes) throw new TextRebuildBudgetException(); string? last = records.Count == 0 ? current.LastRecordId : records[^1].Id.Value; bool complete = records.Count < 1024;
            await using SqliteCommand update = connection.CreateCommand(); update.Transaction = transaction; update.CommandText = $"UPDATE {SqliteTextModel.RebuildProgressTable} SET last_record_id=$last,record_count=$count,canonical_bytes=$bytes,rolling_checksum=$checksum,scan_complete=$complete WHERE scope=$scope AND operation=$operation AND idempotency_key=$key AND source_head=$head AND record_count=$previous;"; Identity(update, request); update.Parameters.AddWithValue("$last", (object?)last ?? DBNull.Value); update.Parameters.AddWithValue("$count", evidence.RecordCount); update.Parameters.AddWithValue("$bytes", evidence.CanonicalBytes); update.Parameters.AddWithValue("$checksum", evidence.Checksum); update.Parameters.AddWithValue("$complete", complete ? 1 : 0); update.Parameters.AddWithValue("$head", current.SourceHead); update.Parameters.AddWithValue("$previous", current.RecordCount); if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseTextErrorCodes.ProviderContractInvalid);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await store.BeforeTextAdministrationPhaseAsync("textRebuildPageCommitted", cancellationToken).ConfigureAwait(false);
        }
        catch { try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { } throw; }
    }

    private async ValueTask<OperationResult<BaseTextRebuildResult>> PublishRebuildAsync(SqliteConnection connection, SqliteTextModel.IndexModel index, BaseTextRebuildRequest request, byte[] fingerprint, CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = connection.BeginTransaction();
        try
        {
            RebuildProgress progress = await ReadProgressAsync(connection, transaction, request, fingerprint, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException(BaseTextErrorCodes.ProviderContractInvalid); if (!progress.ScanComplete) throw new InvalidDataException(BaseTextErrorCodes.ProviderContractInvalid);
            (long generation, long head) = await CurrentRebuildAuthorityAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false); if (generation != progress.ExpectedGeneration) { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); return OperationResults.Conflict<BaseTextRebuildResult>(new BaseError { Code = BaseTextErrorCodes.RebuildRequired, Message = "The text rebuild conflicts with current index state.", Category = ErrorCategory.Conflict }); } if (head != progress.SourceHead) throw new TextRebuildSourceChangedException();
            StageEvidence evidence = await ComputeStageEvidenceAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false); if (evidence.RecordCount != progress.RecordCount || evidence.CanonicalBytes != progress.CanonicalBytes || !CryptographicOperations.FixedTimeEquals(evidence.Checksum, progress.RollingChecksum)) throw new InvalidDataException(BaseTextErrorCodes.ProviderContractInvalid);
            await using (SqliteCommand carriers = connection.CreateCommand()) { carriers.Transaction = transaction; carriers.CommandText = $"INSERT INTO {index.Table}(generation,record_id,revision,journal_position) SELECT $generation,record_id,revision,journal_position FROM {SqliteTextModel.RebuildStageTable} WHERE scope=$scope AND operation=$operation AND idempotency_key=$key ORDER BY record_id COLLATE BINARY;"; Identity(carriers, request); carriers.Parameters.AddWithValue("$generation", progress.StagingGeneration); await carriers.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
            await using (SqliteCommand fts = connection.CreateCommand()) { fts.Transaction = transaction; fts.CommandText = $"INSERT INTO {index.FtsTable}(generation,record_id,content) SELECT $generation,record_id,content FROM {SqliteTextModel.RebuildStageTable} WHERE scope=$scope AND operation=$operation AND idempotency_key=$key ORDER BY record_id COLLATE BINARY;"; Identity(fts, request); fts.Parameters.AddWithValue("$generation", progress.StagingGeneration); await fts.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
            await using (SqliteCommand publish = connection.CreateCommand()) { publish.Transaction = transaction; publish.CommandText = $"UPDATE {SqliteTextModel.StateTable} SET generation=$published,applied_position=$head,state='ready' WHERE collection_id=$collection AND index_id=$index AND generation=$expected;"; publish.Parameters.AddWithValue("$published", progress.StagingGeneration); publish.Parameters.AddWithValue("$head", progress.SourceHead); publish.Parameters.AddWithValue("$collection", request.CollectionId); publish.Parameters.AddWithValue("$index", request.TextIndexId); publish.Parameters.AddWithValue("$expected", progress.ExpectedGeneration); if (await publish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseTextErrorCodes.ProviderContractInvalid); }
            var result = new BaseTextRebuildResult { PreviousGeneration = progress.ExpectedGeneration, PublishedGeneration = progress.StagingGeneration, VisibleThrough = new(progress.SourceHead), RecordCount = evidence.RecordCount, PublicationChecksum = ImmutableArray.Create(evidence.Checksum) };
            await using (SqliteCommand receipt = connection.CreateCommand()) { receipt.Transaction = transaction; receipt.CommandText = $"INSERT INTO {SqliteTextModel.RebuildReceiptTable}(scope,operation,idempotency_key,fingerprint,previous_generation,published_generation,visible_through,record_count,publication_checksum) VALUES($scope,$operation,$key,$fingerprint,$previous,$published,$visible,$count,$checksum);"; Identity(receipt, request); receipt.Parameters.AddWithValue("$fingerprint", fingerprint); receipt.Parameters.AddWithValue("$previous", result.PreviousGeneration); receipt.Parameters.AddWithValue("$published", result.PublishedGeneration); receipt.Parameters.AddWithValue("$visible", result.VisibleThrough.Value); receipt.Parameters.AddWithValue("$count", result.RecordCount); receipt.Parameters.AddWithValue("$checksum", evidence.Checksum); await receipt.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
            await store.BeforeTextAdministrationPhaseAsync("textRebuildBeforePublicationCommit", cancellationToken).ConfigureAwait(false); await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await CleanupPublishedAsync(connection, index, request, result, cancellationToken).ConfigureAwait(false); return OperationResults.Ok(result);
        }
        catch { try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { } throw; }
    }

    private static async ValueTask CleanupPublishedAsync(SqliteConnection connection, SqliteTextModel.IndexModel index, BaseTextRebuildRequest request, BaseTextRebuildResult result, CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await using (SqliteCommand probe = connection.CreateCommand()) { probe.Transaction = transaction; probe.CommandText = $"SELECT COUNT(*) FROM {index.Table} WHERE generation=$generation;"; probe.Parameters.AddWithValue("$generation", result.PublishedGeneration); if (Convert.ToInt64(await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != result.RecordCount) throw new InvalidDataException(BaseTextErrorCodes.ProviderContractInvalid); }
        await using (SqliteCommand cleanup = connection.CreateCommand()) { cleanup.Transaction = transaction; cleanup.CommandText = $"DELETE FROM {index.Table} WHERE generation<>$generation; DELETE FROM {index.FtsTable} WHERE generation<>$generation;"; cleanup.Parameters.AddWithValue("$generation", result.PublishedGeneration); await cleanup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        await DeleteStagingAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false); await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<(long Generation, long Head)> CurrentRebuildAuthorityAsync(SqliteConnection connection, SqliteTransaction transaction, BaseTextRebuildRequest request, CancellationToken cancellationToken)
    {
        await using SqliteCommand state = connection.CreateCommand(); state.Transaction = transaction; state.CommandText = $"SELECT generation,COALESCE((SELECT MAX(position) FROM {store.VectorNames.MutationJournal}),0) FROM {SqliteTextModel.StateTable} WHERE collection_id=$collection AND index_id=$index;"; state.Parameters.AddWithValue("$collection", request.CollectionId); state.Parameters.AddWithValue("$index", request.TextIndexId); await using SqliteDataReader reader = await state.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException(BaseTextErrorCodes.IndexUnavailable); return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static async ValueTask<RebuildProgress?> ReadProgressAsync(SqliteConnection connection, SqliteTransaction? transaction, BaseTextRebuildRequest request, byte[] fingerprint, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = $"SELECT fingerprint,collection_id,index_id,expected_generation,staging_generation,source_head,last_record_id,record_count,canonical_bytes,rolling_checksum,scan_complete FROM {SqliteTextModel.RebuildProgressTable} WHERE scope=$scope AND operation=$operation AND idempotency_key=$key;"; Identity(command, request); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        byte[] stored = reader.GetFieldValue<byte[]>(0); byte[] checksum = reader.GetFieldValue<byte[]>(9); if (!CryptographicOperations.FixedTimeEquals(stored, fingerprint) || reader.GetString(1) != request.CollectionId || reader.GetString(2) != request.TextIndexId || reader.GetInt64(3) != request.ExpectedGeneration || reader.GetInt64(4) != checked(request.ExpectedGeneration + 1) || checksum.Length != 32) throw new InvalidDataException(BaseTextErrorCodes.ProviderContractInvalid);
        return new RebuildProgress(reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt64(7), reader.GetInt64(8), checksum, reader.GetInt64(10) == 1);
    }

    private static async ValueTask<StageEvidence> ComputeStageEvidenceAsync(SqliteConnection connection, SqliteTransaction transaction, BaseTextRebuildRequest request, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(); stream.Write("HPDB-TEXT-REBUILD-STAGE-1\0"u8); long count = 0; await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = $"SELECT record_id,revision,journal_position,content FROM {SqliteTextModel.RebuildStageTable} WHERE scope=$scope AND operation=$operation AND idempotency_key=$key ORDER BY record_id COLLATE BINARY;"; Identity(command, request); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { count++; WriteCanonical(stream, reader.GetString(0)); WriteCanonical(stream, reader.GetString(1)); byte[] number = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(number, reader.GetInt64(2)); stream.Write(number); WriteCanonical(stream, reader.GetString(3)); }
        return new StageEvidence(count, stream.Length, SHA256.HashData(stream.ToArray()));
    }

    private static async ValueTask DeleteStagingAsync(SqliteConnection connection, SqliteTransaction transaction, BaseTextRebuildRequest request, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = $"DELETE FROM {SqliteTextModel.RebuildStageTable} WHERE scope=$scope AND operation=$operation AND idempotency_key=$key; DELETE FROM {SqliteTextModel.RebuildProgressTable} WHERE scope=$scope AND operation=$operation AND idempotency_key=$key;"; Identity(command, request); await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
    private static void Identity(SqliteCommand command, BaseTextRebuildRequest request) { command.Parameters.AddWithValue("$scope", request.Identity.Scope); command.Parameters.AddWithValue("$operation", request.Identity.Operation); command.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey); }
    private static void WriteCanonical(Stream stream, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> count = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(count, checked((uint)bytes.Length)); stream.Write(count); stream.Write(bytes); }
    private static bool ProgressEquals(RebuildProgress left, RebuildProgress right) => left.ExpectedGeneration == right.ExpectedGeneration && left.StagingGeneration == right.StagingGeneration && left.SourceHead == right.SourceHead && left.LastRecordId == right.LastRecordId && left.RecordCount == right.RecordCount && left.CanonicalBytes == right.CanonicalBytes && left.ScanComplete == right.ScanComplete && CryptographicOperations.FixedTimeEquals(left.RollingChecksum, right.RollingChecksum);
    private sealed record RebuildProgress(long ExpectedGeneration, long StagingGeneration, long SourceHead, string? LastRecordId, long RecordCount, long CanonicalBytes, byte[] RollingChecksum, bool ScanComplete);
    private sealed record StageEvidence(long RecordCount, long CanonicalBytes, byte[] Checksum) { internal static StageEvidence Empty { get; } = EmptyEvidence(); private static StageEvidence EmptyEvidence() { byte[] marker = "HPDB-TEXT-REBUILD-STAGE-1\0"u8.ToArray(); return new(0, marker.Length, SHA256.HashData(marker)); } }
    private sealed class TextRebuildBudgetException : Exception;
    private sealed class TextRebuildSourceChangedException : Exception;
    private static async ValueTask Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token) { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
    private static byte[] TextRebuildFingerprint(BaseTextRebuildRequest request)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write("base.text.rebuild.v1"); writer.Write(request.Identity.Scope); writer.Write(request.Identity.Operation); writer.Write(request.Identity.IdempotencyKey); writer.Write(request.CollectionId); writer.Write(request.TextIndexId); writer.Write(request.ExpectedGeneration); writer.Write(request.Identity.Fingerprint.ToArray());
        return SHA256.HashData(stream.ToArray());
    }
    private static OperationResult<BaseTextRebuildResult> Failure() => new() { Status = OperationStatus.StoreError, Error = new BaseError { Code = BaseTextErrorCodes.CommitIndeterminate, Message = "The text rebuild could not be completed.", Category = ErrorCategory.Store } };

    private sealed class Session(SqliteRecordStore store, SqliteConnection connection, SqliteTransaction transaction, IAsyncDisposable lease, CollectionDefinition collection, BaseTextIndexDefinition index, SqliteTextModel.IndexModel indexModel, BaseTextProviderDescriptor descriptor, BaseTextAuthoritySnapshot snapshot) : IBaseTextHydrationSession
    {
        private readonly HashSet<Plan> _plans = new(ReferenceEqualityComparer.Instance); private int _disposed;
        public BaseTextAuthoritySnapshot Snapshot { get; } = snapshot;
        public ValueTask<OperationResult<BaseTextConstraintPreparation>> PrepareAsync(BaseTextProviderPreparationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); if (request.Snapshot != Snapshot || !request.Index.DefinitionChecksum.AsSpan().SequenceEqual(index.DefinitionChecksum.AsSpan())) return ValueTask.FromResult(Invalid<BaseTextConstraintPreparation>());
            BaseTextLoweringReceipt receipt = BaseTextProviderEvidence.CreateLoweringReceipt(descriptor, Snapshot, index, request.QueryDigest, request.ConstraintDigest, request.InfluenceConstraints, request.Limits); var plan = new Plan(request.NormalizedQuery, request.Constraint, request.QueryDigest, request.ConstraintDigest, request.InfluenceConstraints, receipt); _plans.Add(plan);
            return ValueTask.FromResult(OperationResults.Ok(new BaseTextConstraintPreparation { QueryDigest = request.QueryDigest, ConstraintDigest = request.ConstraintDigest, Enforcement = BaseTextConstraintEnforcement.CompleteBeforeMatchingAndRanking, Receipt = receipt, Plan = plan }));
        }
        public async ValueTask<OperationResult<BaseTextProviderResult>> SearchAsync(BaseTextExecutionRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Plan is not Plan plan || !_plans.Remove(plan) || Interlocked.Exchange(ref plan.Consumed, 1) != 0 || request.Snapshot != Snapshot) return Invalid<BaseTextProviderResult>(); long started = System.Diagnostics.Stopwatch.GetTimestamp();
            SqlitePhysicalModel.CollectionModel physical = store.VectorPhysicalModel.Collection(collection.Id); await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = store.VectorCommandTimeoutSeconds; string select = string.Join(", ", physical.SelectList.Split(", ", StringSplitOptions.None).Select(static column => "r." + column));
            string coarseMatch = CoarseMatch(plan.Query);
            string candidateConstraint = LowerConstraint(plan.Constraint, physical, command);
            command.CommandText = $"SELECT {select} FROM {indexModel.Table} x JOIN {indexModel.FtsTable} ON {indexModel.FtsTable}.record_id=x.record_id AND CAST({indexModel.FtsTable}.generation AS INTEGER)=x.generation JOIN {physical.Table} r ON r.record_id=x.record_id AND ('sqlite:' || CAST(r.revision AS TEXT))=x.revision WHERE x.generation=$generation AND ({candidateConstraint}) AND {indexModel.FtsTable} MATCH $match ORDER BY x.record_id COLLATE BINARY ASC;"; command.Parameters.AddWithValue("$generation", Snapshot.TextIndexGeneration); command.Parameters.AddWithValue("$match", coarseMatch);
            var candidates = new List<BaseTextCandidate>(); long examined = 0, proofBytes = 0, orderingBytes = 0, prefixCount = 0, prefixBytes = 0; await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                examined++; if (examined > descriptor.Capability.MaximumIndexedRecords) return Budget<BaseTextProviderResult>(); RecordEnvelope record = physical.ReadEnvelope(reader, store.VectorStoreId); if (!BaseTextSemanticEvaluator.ConstraintMatches(record.Payload, index, plan.Constraint)) continue; BaseTextEvaluatedCandidate? evaluated = BaseTextSemanticEvaluator.Evaluate(record.Payload, index, plan.Query, plan.QueryDigest, plan.Influences); if (evaluated is null) continue;
                ImmutableArray<byte> boundary = BaseTextSemanticEvaluator.OrderingBoundary(evaluated.Score, record.Id); proofBytes = checked(proofBytes + BaseTextSemanticEvaluator.ProofRetainedBytes(evaluated.Proof)); orderingBytes = checked(orderingBytes + boundary.Length); prefixCount = checked(prefixCount + BaseTextSemanticEvaluator.PrefixExpansionCount(evaluated.Proof)); prefixBytes = checked(prefixBytes + BaseTextSemanticEvaluator.PrefixExpansionBytes(evaluated.Proof)); if (prefixCount > request.Limits.MaximumPrefixExpansions || prefixBytes > request.Limits.MaximumPrefixExpansionBytes || proofBytes > request.Limits.MaximumScoreProofBytes || orderingBytes > request.Limits.MaximumOrderingBytes || checked(proofBytes + orderingBytes + prefixBytes) > request.Limits.MaximumTransientBytes) return Budget<BaseTextProviderResult>(); candidates.Add(new BaseTextCandidate { RecordId = record.Id, Revision = record.Metadata.Revision!.Value, IndexedPosition = Snapshot.SearchVisibleThrough, Score = evaluated.Score, CanonicalOrderingBoundary = boundary, ScoreProof = evaluated.Proof });
            }
            BaseTextCandidate[] result = candidates.OrderByDescending(static value => value.Score.Units).ThenBy(static value => value.RecordId.Value, StringComparer.Ordinal).Where(value => request.AfterBoundary is null || value.CanonicalOrderingBoundary.AsSpan().SequenceCompareTo(request.AfterBoundary.Value.AsSpan()) > 0).Take(request.TakePlusOne).ToArray();
            long returnedProofBytes = result.Sum(static value => BaseTextSemanticEvaluator.ProofRetainedBytes(value.ScoreProof)), returnedOrderingBytes = result.Sum(static value => (long)value.CanonicalOrderingBoundary.Length), returnedPrefixCount = result.Sum(static value => (long)BaseTextSemanticEvaluator.PrefixExpansionCount(value.ScoreProof)), returnedPrefixBytes = result.Sum(static value => BaseTextSemanticEvaluator.PrefixExpansionBytes(value.ScoreProof));
            ImmutableArray<BaseTextCandidate> page = [.. result]; long queryBytes = BaseTextQueryContract.Encode(plan.Query).Length; long constraintBytes = BaseTextSemanticEvaluator.ConstraintEncoding(plan.Constraint).Length;
            return OperationResults.Ok(new BaseTextProviderResult { Snapshot = Snapshot, Candidates = page, Completeness = BaseTextProviderEvidence.CreateCompleteness(descriptor, Snapshot, plan.Lowering, page, request.TakePlusOne), Accounting = new BaseTextProviderAccounting { InputBytes = checked(queryBytes + constraintBytes), QueryBytes = queryBytes, ConstraintBytes = constraintBytes, StatementParameters = BaseTextProviderEvidence.StatementParameterCount(plan.Query, plan.Constraint), AuthorizedRecordsExamined = examined, PostingsExamined = examined, PrefixExpansionCount = returnedPrefixCount, PrefixExpansionBytes = returnedPrefixBytes, ScoreProofBytes = returnedProofBytes, CandidateCount = result.Length, OrderingBytes = returnedOrderingBytes, ExactHydrationBytes = 0, ResultBytes = 0, CursorBytes = 0, RetainedTransientBytes = checked(queryBytes + constraintBytes + proofBytes + orderingBytes + prefixBytes), Elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started) } });
        }
        public async ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition requestedCollection, BaseTextCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default)
        {
            SqlitePhysicalModel.CollectionModel physical = store.VectorPhysicalModel.Collection(requestedCollection.Id); var records = new List<RecordEnvelope>(candidates.Length);
            foreach (BaseTextCandidateIdentity candidate in candidates)
            {
                if (!SqliteRecordMapper.TryParseRevision(candidate.IndexedRevision, out long revision)) return Conflict(); await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = store.VectorCommandTimeoutSeconds; command.CommandText = $"SELECT {physical.SelectList} FROM {physical.Table} WHERE record_id=$id AND revision=$revision LIMIT 1;"; command.Parameters.AddWithValue("$id", candidate.RecordId.Value); command.Parameters.AddWithValue("$revision", revision); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return Conflict(); records.Add(physical.ReadEnvelope(reader, store.VectorStoreId));
            }
            return OperationResults.Ok(records.ToArray());
        }
        public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; try { await transaction.DisposeAsync().ConfigureAwait(false); await connection.DisposeAsync().ConfigureAwait(false); } finally { await lease.DisposeAsync().ConfigureAwait(false); } }
        private static OperationResult<RecordEnvelope[]> Conflict() => OperationResults.Conflict<RecordEnvelope[]>(new BaseError { Code = BaseTextErrorCodes.SnapshotChanged, Message = "The text snapshot changed.", Category = ErrorCategory.Conflict });
        private static OperationResult<T> Invalid<T>() => new() { Status = OperationStatus.StoreError, Error = new BaseError { Code = BaseTextErrorCodes.ProviderContractInvalid, Message = "The text provider returned invalid evidence.", Category = ErrorCategory.Store } };
        private static OperationResult<T> Budget<T>() => new() { Status = OperationStatus.ValidationFailed, Error = new BaseError { Code = BaseTextErrorCodes.BudgetExceeded, Message = "The text operation exceeded an installed bound.", Category = ErrorCategory.Validation } };
        private static string CoarseMatch(BaseTextQuery query)
        {
            static IEnumerable<(string Value, bool Prefix)> Positive(BaseTextQuery node, bool excluded = false) => node switch
            {
                BaseTextQuery.Term value when !excluded => [(value.Value, false)],
                BaseTextQuery.Prefix value when !excluded => [(value.Value, true)],
                BaseTextQuery.Phrase value when !excluded => value.Terms.Select(static term => (term, false)),
                BaseTextQuery.Field value => Positive(value.Child, excluded),
                BaseTextQuery.Not value => Positive(value.Child, true),
                BaseTextQuery.And value => value.Children.SelectMany(child => Positive(child, excluded)),
                BaseTextQuery.Or value => value.Children.SelectMany(child => Positive(child, excluded)),
                _ => [],
            };
            string[] features = Positive(query).Distinct().OrderBy(static value => value.Value, StringComparer.Ordinal).ThenBy(static value => value.Prefix)
                .Select(static value => "\"" + value.Value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" + (value.Prefix ? "*" : string.Empty)).ToArray();
            if (features.Length == 0) throw new InvalidOperationException(BaseTextErrorCodes.QueryInvalid);
            return string.Join(" OR ", features);
        }
        private static string LowerConstraint(BaseTextCandidateConstraint constraint, SqlitePhysicalModel.CollectionModel collection, SqliteCommand command)
        {
            int parameter = 0;
            string Lower(BaseTextCandidateConstraint node) => node switch
            {
                BaseTextCandidateConstraint.True => "1=1",
                BaseTextCandidateConstraint.False => "1=0",
                BaseTextCandidateConstraint.And value => "(" + string.Join(" AND ", value.Children.Select(Lower)) + ")",
                BaseTextCandidateConstraint.Or value => "(" + string.Join(" OR ", value.Children.Select(Lower)) + ")",
                BaseTextCandidateConstraint.IsMissing value => Missing(Field(value.Field)),
                BaseTextCandidateConstraint.IsNull value => Null(Field(value.Field)),
                BaseTextCandidateConstraint.Equal value => Equal(Field(value.Field), value.Value),
                BaseTextCandidateConstraint.In value => In(Field(value.Field), value.Values),
                _ => throw new InvalidOperationException(BaseTextErrorCodes.ProviderContractInvalid),
            };
            SqlitePhysicalModel.FieldModel Field(BaseTextFilterField field)
            {
                SqlitePhysicalModel.FieldModel? physical = collection.Fields.SingleOrDefault(value => string.Equals(value.Definition.Id, field.StableFieldId, StringComparison.Ordinal));
                if (physical is null || !ValueKindMatches(physical.Definition, field.ValueKind)) throw new InvalidOperationException(BaseTextErrorCodes.ProviderContractInvalid);
                return physical;
            }
            static string Missing(SqlitePhysicalModel.FieldModel field) => field.PresenceColumn is null ? "1=0" : $"r.{field.PresenceColumn}=0";
            static string Null(SqlitePhysicalModel.FieldModel field) => field.PresenceColumn is null ? $"r.{field.Column} IS NULL" : $"r.{field.PresenceColumn}=1 AND r.{field.Column} IS NULL";
            string Equal(SqlitePhysicalModel.FieldModel field, BaseTextFilterValue value)
            {
                string name = "$constraint" + parameter++;
                command.Parameters.AddWithValue(name, EncodeFilterValue(field, value));
                string presence = field.PresenceColumn is null ? string.Empty : $"r.{field.PresenceColumn}=1 AND ";
                return $"({presence}r.{field.Column}={name})";
            }
            string In(SqlitePhysicalModel.FieldModel field, ImmutableArray<BaseTextFilterValue> values) => values.Length == 0
                ? "1=0"
                : "(" + string.Join(" OR ", values.Select(value => Equal(field, value))) + ")";
            return Lower(constraint);
        }
        private static bool ValueKindMatches(FieldDefinition field, BaseTextFilterValueKind kind) => kind switch
        {
            BaseTextFilterValueKind.Id => field.Type is "id" || field.Format == "record-id",
            BaseTextFilterValueKind.String => field.Type == "string" && field.Format != "record-id",
            BaseTextFilterValueKind.Boolean => field.Type == "boolean",
            BaseTextFilterValueKind.Integer => field.Type == "integer",
            _ => false,
        };
        private static object EncodeFilterValue(SqlitePhysicalModel.FieldModel field, BaseTextFilterValue value) => value.Kind switch
        {
            BaseTextFilterValueKind.String when value.StringValue is not null && field.Definition.Type == "string" => value.StringValue,
            BaseTextFilterValueKind.Id when value.StringValue is not null && (field.Definition.Type == "id" || field.Definition.Format == "record-id") => value.StringValue,
            BaseTextFilterValueKind.Boolean when value.BooleanValue.HasValue && field.Definition.Type == "boolean" => value.BooleanValue.Value ? 1L : 0L,
            BaseTextFilterValueKind.Integer when value.IntegerValue.HasValue && field.Definition.Type == "integer" => value.IntegerValue.Value,
            _ => throw new InvalidOperationException(BaseTextErrorCodes.ProviderContractInvalid),
        };
        private sealed class Plan(BaseTextQuery query, BaseTextCandidateConstraint constraint, ImmutableArray<byte> queryDigest, ImmutableArray<byte> constraintDigest, ImmutableArray<BaseTextFieldInfluenceConstraint> influences, BaseTextLoweringReceipt lowering) : BaseTextProviderPlan { internal BaseTextQuery Query { get; } = query; internal BaseTextCandidateConstraint Constraint { get; } = constraint; internal ImmutableArray<byte> QueryDigest { get; } = queryDigest; internal ImmutableArray<byte> ConstraintDigest { get; } = constraintDigest; internal ImmutableArray<BaseTextFieldInfluenceConstraint> Influences { get; } = influences; internal BaseTextLoweringReceipt Lowering { get; } = lowering; internal int Consumed; }
    }
}
