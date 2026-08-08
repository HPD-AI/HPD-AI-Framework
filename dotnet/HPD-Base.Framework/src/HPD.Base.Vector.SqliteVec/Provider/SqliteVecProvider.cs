using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Vector.SqliteVec;

internal sealed class SqliteVecProvider(SqliteRecordStore store, SqliteVecModel model, TimeProvider timeProvider, BaseOpaqueTokenProtector tokens, HPDBaseVectorSnapshot options) : IBaseVectorProvider, IBaseVectorAuthority, IBaseVectorAdministrationProvider
{
    public BaseVectorProviderDescriptor Descriptor { get; } = new() { Id = "sqlitevec", Consistency = BaseVectorProviderConsistency.TransactionalCurrent, Exact = true, MaximumTopK = 1_000 };

    public ValueTask<BaseVectorConstraintPreparation> PrepareAsync(BaseVectorProviderPreparationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SqliteVecHydrationSession session = SqliteVecHydrationSession.Find(request.Snapshot);
        SqliteVecModel.IndexModel index = model.Get(request.Index.CollectionId, request.Index.Id);
        var parameters = new List<SqliteParameterValue>();
        string where = Lower(request.Constraint, index, parameters);
        return ValueTask.FromResult(new BaseVectorConstraintPreparation { ConstraintDigest = request.ConstraintDigest, Enforcement = BaseVectorConstraintEnforcement.PreRankingExact, Plan = new Plan(session, where, parameters.ToArray(), request.ConstraintDigest) });
    }

    public async ValueTask<BaseVectorProviderResult> SearchAsync(BaseVectorExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Plan is not Plan plan || !ReferenceEquals(plan.Session.Snapshot, request.Snapshot)) throw new InvalidOperationException("The SQLite vector plan is not bound to this authority snapshot.");
        SqliteVecModel.IndexModel index = model.Get(request.Index.CollectionId, request.Index.Id);
        string native = index.Definition.Function switch { BaseVectorFunction.CosineSimilarity => "vec_distance_cosine", BaseVectorFunction.EuclideanDistance => "vec_distance_L2", _ => throw new NotSupportedException("SQLiteVec does not support dot-product indexes in L39.") };
        string direction = index.Definition.Function == BaseVectorFunction.CosineSimilarity ? "ASC" : "ASC";
        await using SqliteCommand command = plan.Session.Connection.CreateCommand();
        command.Transaction = plan.Session.Transaction;
        command.CommandTimeout = store.VectorCommandTimeoutSeconds;
        command.CommandText = $"SELECT record_id,revision,journal_position,{native}(vector,$query) AS distance FROM {index.Table} WHERE {plan.WhereSql} ORDER BY distance {direction}, record_id COLLATE BINARY ASC, revision COLLATE BINARY ASC LIMIT $take;";
        command.Parameters.AddWithValue("$query", FloatBytes(request.Vector.ToArray()));
        command.Parameters.AddWithValue("$take", request.Take);
        foreach (SqliteParameterValue parameter in plan.Parameters) command.Parameters.AddWithValue("$" + parameter.Name, parameter.Value ?? DBNull.Value);
        var candidates = new List<BaseVectorCandidate>(request.Take);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            double distance = reader.GetDouble(3); if (!double.IsFinite(distance)) throw new InvalidOperationException("SQLiteVec returned a non-finite distance."); if (distance == 0D) distance = 0D;
            candidates.Add(new BaseVectorCandidate { RecordId = new RecordId(reader.GetString(0)), IndexedRevision = new RevisionToken(reader.GetString(1)), IndexedPosition = new BaseMutationJournalPosition(reader.GetInt64(2)), Rank = candidates.Count + 1, Measure = new BaseVectorMeasure { Function = index.Definition.Function, Value = index.Definition.Function == BaseVectorFunction.CosineSimilarity ? 1D - distance : distance, Direction = index.Definition.Function == BaseVectorFunction.CosineSimilarity ? BaseVectorMeasureDirection.HigherIsNearer : BaseVectorMeasureDirection.LowerIsNearer } });
        }
        return new BaseVectorProviderResult { Snapshot = request.Snapshot, Candidates = candidates.ToArray(), Accuracy = BaseVectorResultAccuracy.Exact };
    }

    public async ValueTask<OperationResult<IBaseVectorHydrationSession>> OpenAsync(CollectionDefinition collection, VectorIndexDefinition index, BaseVectorConsistencyRequirement consistency, OperationContext context, CancellationToken cancellationToken = default)
    {
        IAsyncDisposable generationLease = await store.AcquireVectorGenerationSharedAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection? connection = null;
        try
        {
            connection = await store.VectorConnections.OpenAsync(cancellationToken).ConfigureAwait(false);
            SqliteVecNative.Load(connection);
            SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
            BaseVectorAuthoritySnapshot snapshot = await Snapshot(connection, transaction, collection, index, cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok<IBaseVectorHydrationSession>(new SqliteVecHydrationSession(store, connection, transaction, generationLease, snapshot));
        }
        catch
        {
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            await generationLease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<BaseVectorAuthoritySnapshot> Snapshot(SqliteConnection connection, SqliteTransaction transaction, CollectionDefinition collection, VectorIndexDefinition index, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = store.VectorCommandTimeoutSeconds;
        command.CommandText = $"SELECT i.store_instance_id, COALESCE((SELECT CAST(value AS INTEGER) FROM {store.VectorNames.ProviderState} WHERE key='restore_epoch'),0), COALESCE((SELECT MAX(generation) FROM {store.VectorNames.SchemaBaseline}),0), c.purge_generation, COALESCE((SELECT MAX(position) FROM {store.VectorNames.MutationJournal}),0), v.generation FROM {store.VectorNames.SchemaIdentity} i JOIN {store.VectorNames.Collections} c ON c.collection_id=$collection JOIN {SqliteVecModel.StateTable} v ON v.collection_id=c.collection_id AND v.index_id=$index WHERE v.state='ready' LIMIT 1;";
        command.Parameters.AddWithValue("$collection", collection.Id);
        command.Parameters.AddWithValue("$index", index.Id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("SQLite vector authority state is unavailable.");
        return new BaseVectorAuthoritySnapshot { StoreIdentityDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reader.GetString(0)))), RestoreEpoch = reader.GetInt64(1), SchemaGeneration = reader.GetInt64(2), CollectionId = collection.Id, PurgeGeneration = reader.GetInt64(3), VectorIndexId = index.Id, VectorIndexGeneration = reader.GetInt64(5), VectorSpaceId = index.VectorSpaceId, HighWatermark = new BaseMutationJournalPosition(reader.GetInt64(4)) };
    }

    public async ValueTask<OperationResult<BaseVectorIndexStatus[]>> ListAsync(CancellationToken cancellationToken = default)
    {
        var values = new List<BaseVectorIndexStatus>(model.Indexes.Length);
        foreach (SqliteVecModel.IndexModel index in model.Indexes)
        {
            OperationResult<BaseVectorIndexStatus> value = await GetAsync(index.Definition.CollectionId, index.Definition.Id, cancellationToken).ConfigureAwait(false);
            if (!value.Status.IsSuccess() || value.Value is null) return new OperationResult<BaseVectorIndexStatus[]> { Status = value.Status, Error = value.Error };
            values.Add(value.Value);
        }
        return OperationResults.Ok(values.ToArray());
    }

    public async ValueTask<OperationResult<BaseVectorIndexStatus>> GetAsync(string collectionId, string vectorIndexId, CancellationToken cancellationToken = default)
    {
        SqliteVecModel.IndexModel index;
        try { index = model.Get(collectionId, vectorIndexId); }
        catch (InvalidOperationException) { return OperationResults.NotFound<BaseVectorIndexStatus>(new BaseError { Code = "base.vector.indexNotFound", Message = "The vector index was not found.", Category = ErrorCategory.NotFound }); }
        await using SqliteConnection connection = await store.VectorConnections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandTimeout = store.VectorCommandTimeoutSeconds;
        command.CommandText = $"SELECT v.generation,c.purge_generation,v.applied_position,v.state FROM {SqliteVecModel.StateTable} v JOIN {store.VectorNames.Collections} c ON c.collection_id=v.collection_id WHERE v.collection_id=$collection AND v.index_id=$index LIMIT 1;";
        command.Parameters.AddWithValue("$collection", collectionId); command.Parameters.AddWithValue("$index", vectorIndexId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return OperationResults.NotFound<BaseVectorIndexStatus>(new BaseError { Code = "base.vector.indexNotFound", Message = "The vector index was not found.", Category = ErrorCategory.NotFound });
        return OperationResults.Ok(new BaseVectorIndexStatus { CollectionId = collectionId, VectorIndexId = vectorIndexId, VectorSpaceId = index.Definition.VectorSpaceId, Generation = reader.GetInt64(0), PurgeGeneration = reader.GetInt64(1), AppliedThrough = new BaseMutationJournalPosition(reader.GetInt64(2)), State = ParseState(reader.GetString(3)), ProviderId = Descriptor.Id });
    }

    public async ValueTask<OperationResult<BaseVectorRebuildResult>> RebuildAsync(BaseVectorRebuildRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.StoreId, store.VectorStoreId, StringComparison.Ordinal) || !string.Equals(request.Confirmation, "REBUILD VECTOR INDEX", StringComparison.Ordinal) || request.ExpectedGeneration < 0 || request.ExpectedPurgeGeneration < 0)
            return OperationResults.ValidationFailed<BaseVectorRebuildResult>(new BaseError { Code = "base.vector.invalid", Message = "The vector rebuild request is invalid.", Category = ErrorCategory.Validation });
        SqliteVecModel.IndexModel index;
        try { index = model.Get(request.CollectionId, request.VectorIndexId); }
        catch (InvalidOperationException) { return OperationResults.NotFound<BaseVectorRebuildResult>(new BaseError { Code = "base.vector.indexNotFound", Message = "The vector index was not found.", Category = ErrorCategory.NotFound }); }
        await using IAsyncDisposable generationLease = await store.AcquireVectorGenerationExclusiveAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await store.VectorConnections.OpenAsync(cancellationToken).ConfigureAwait(false); SqliteVecNative.Load(connection);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        BaseVectorAuthoritySnapshot before = await Snapshot(connection, transaction, index.Collection, index.Definition, cancellationToken).ConfigureAwait(false);
        if (before.VectorIndexGeneration != request.ExpectedGeneration || before.PurgeGeneration != request.ExpectedPurgeGeneration)
            return OperationResults.Conflict<BaseVectorRebuildResult>(new BaseError { Code = "base.vector.snapshotChanged", Message = "The vector index generation changed.", Category = ErrorCategory.Conflict });
        long next = checked(before.VectorIndexGeneration + 1);
        SqlitePhysicalModel.CollectionModel physical = store.VectorPhysicalModel.Collection(request.CollectionId);
        SqlitePhysicalModel.FieldModel vectorField = physical.Fields.Single(field => field.Definition.Id == index.Definition.VectorFieldId);
        await using (SqliteCommand rebuild = connection.CreateCommand())
        {
            rebuild.Transaction = transaction; rebuild.CommandTimeout = store.VectorCommandTimeoutSeconds;
            string carrierColumns = string.Concat(index.Filters.Select(filter => $", {filter.PresenceColumn}, {filter.ValueColumn}"));
            string sourceColumns = string.Concat(index.Filters.Select(filter =>
            {
                SqlitePhysicalModel.FieldModel source = physical.Fields.Single(field => field.Definition.Id == filter.Definition.Id);
                return $", {(source.PresenceColumn is null ? "1" : source.PresenceColumn)}, {source.Column}";
            }));
            string vectorPredicate = vectorField.PresenceColumn is null ? $"{vectorField.Column} IS NOT NULL" : $"{vectorField.PresenceColumn}=1 AND {vectorField.Column} IS NOT NULL";
            rebuild.CommandText = $"DELETE FROM {index.Table}; INSERT INTO {index.Table}(record_id,revision,journal_position,vector{carrierColumns}) SELECT record_id,CAST(revision AS TEXT),$position,vec_f32({vectorField.Column}){sourceColumns} FROM {physical.Table} WHERE {vectorPredicate};";
            rebuild.Parameters.AddWithValue("$position", before.HighWatermark.Value);
            await rebuild.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using SqliteCommand update = connection.CreateCommand(); update.Transaction = transaction; update.CommandTimeout = store.VectorCommandTimeoutSeconds;
        update.CommandText = $"UPDATE {SqliteVecModel.StateTable} SET generation=$next,purge_generation=$purge,applied_position=$position,state='ready' WHERE collection_id=$collection AND index_id=$index AND generation=$expected;";
        update.Parameters.AddWithValue("$next", next); update.Parameters.AddWithValue("$purge", before.PurgeGeneration); update.Parameters.AddWithValue("$position", before.HighWatermark.Value); update.Parameters.AddWithValue("$collection", request.CollectionId); update.Parameters.AddWithValue("$index", request.VectorIndexId); update.Parameters.AddWithValue("$expected", request.ExpectedGeneration);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) return OperationResults.Conflict<BaseVectorRebuildResult>(new BaseError { Code = "base.vector.snapshotChanged", Message = "The vector index generation changed.", Category = ErrorCategory.Conflict });
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        BaseVectorAuthoritySnapshot published = before with { VectorIndexGeneration = next };
        DateTimeOffset completedAt = timeProvider.GetUtcNow();
        return OperationResults.Ok(new BaseVectorRebuildResult { StoreId = request.StoreId, CollectionId = request.CollectionId, VectorIndexId = request.VectorIndexId, PreviousGeneration = request.ExpectedGeneration, PublishedGeneration = next, SourceSnapshot = before, AppliedThrough = BaseVectorConsistencyTokenIssuer.Issue(published, tokens, completedAt, checked(completedAt + options.ConsistencyTokenLifetime)), CompletedAt = completedAt });
    }

    private static BaseVectorIndexState ParseState(string value) => value switch { "ready" => BaseVectorIndexState.Ready, "building" => BaseVectorIndexState.Building, "rebuildRequired" => BaseVectorIndexState.RebuildRequired, _ => BaseVectorIndexState.UnhealthyIndeterminate };

    private static string Lower(BaseVectorCandidateConstraint constraint, SqliteVecModel.IndexModel index, List<SqliteParameterValue> parameters) => constraint switch
    {
        BaseVectorCandidateConstraint.True => "1=1",
        BaseVectorCandidateConstraint.False => "1=0",
        BaseVectorCandidateConstraint.And and => "(" + string.Join(" AND ", and.Children.Select(child => Lower(child, index, parameters))) + ")",
        BaseVectorCandidateConstraint.Or or => "(" + string.Join(" OR ", or.Children.Select(child => Lower(child, index, parameters))) + ")",
        BaseVectorCandidateConstraint.Equal equal => EqualSql(equal.Field, equal.Value, index, parameters),
        BaseVectorCandidateConstraint.In @in => "(" + string.Join(" OR ", @in.Values.Select(value => EqualSql(@in.Field, value, index, parameters))) + ")",
        _ => throw new NotSupportedException(),
    };
    private static string EqualSql(BaseVectorFilterField field, BaseVectorFilterValue value, SqliteVecModel.IndexModel index, List<SqliteParameterValue> parameters)
    {
        SqliteVecModel.FilterModel filter = index.Filters.Single(item => item.Definition.Id == field.StableFieldId);
        if (value.Kind == BaseVectorFilterValueKind.Null) return $"({filter.PresenceColumn}=1 AND {filter.ValueColumn} IS NULL)";
        string name = "f" + parameters.Count; object parameter = value.Kind switch { BaseVectorFilterValueKind.Boolean => value.Boolean!.Value ? 1L : 0L, BaseVectorFilterValueKind.Integer => value.Integer!.Value, _ => value.Text! };
        parameters.Add(new SqliteParameterValue(name, parameter));
        return $"({filter.PresenceColumn}=1 AND {filter.ValueColumn}=${name})";
    }
    private static byte[] FloatBytes(float[] values) { byte[] bytes = new byte[values.Length * sizeof(float)]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); return bytes; }
    private sealed class Plan(SqliteVecHydrationSession session, string whereSql, SqliteParameterValue[] parameters, BaseVectorConstraintDigest digest) : BaseVectorProviderPlan
    {
        internal SqliteVecHydrationSession Session { get; } = session;
        internal string WhereSql { get; } = whereSql;
        internal SqliteParameterValue[] Parameters { get; } = parameters;
        internal BaseVectorConstraintDigest Digest { get; } = digest;
    }
    private sealed record SqliteParameterValue(string Name, object? Value);
}

internal sealed class SqliteVecHydrationSession : IBaseVectorHydrationSession
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BaseVectorAuthoritySnapshot, SqliteVecHydrationSession> Sessions = new();
    private readonly SqliteRecordStore _store;
    private int _disposed;
    private readonly IAsyncDisposable _generationLease;
    internal SqliteVecHydrationSession(SqliteRecordStore store, SqliteConnection connection, SqliteTransaction transaction, IAsyncDisposable generationLease, BaseVectorAuthoritySnapshot snapshot) { _store = store; Connection = connection; Transaction = transaction; _generationLease = generationLease; Snapshot = snapshot; Sessions.Add(snapshot, this); }
    internal SqliteConnection Connection { get; }
    internal SqliteTransaction Transaction { get; }
    public BaseVectorAuthoritySnapshot Snapshot { get; }
    internal static SqliteVecHydrationSession Find(BaseVectorAuthoritySnapshot snapshot) => Sessions.TryGetValue(snapshot, out SqliteVecHydrationSession? session) ? session : throw new InvalidOperationException("The SQLite vector authority snapshot is no longer active.");

    public async ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition collection, BaseVectorCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default)
    {
        SqlitePhysicalModel.CollectionModel physical = _store.VectorPhysicalModel.Collection(collection.Id);
        var records = new List<RecordEnvelope>(candidates.Length);
        foreach (BaseVectorCandidateIdentity candidate in candidates)
        {
            await using SqliteCommand command = Connection.CreateCommand(); command.Transaction = Transaction; command.CommandTimeout = _store.VectorCommandTimeoutSeconds;
            if (!SqliteRecordMapper.TryParseRevision(candidate.IndexedRevision, out long revision)) return OperationResults.Conflict<RecordEnvelope[]>(new BaseError { Code = "base.vector.snapshotChanged", Message = "The authoritative vector snapshot changed.", Category = ErrorCategory.Conflict });
            command.CommandText = $"SELECT {physical.SelectList} FROM {physical.Table} WHERE record_id=$id AND revision=$revision LIMIT 1;"; command.Parameters.AddWithValue("$id", candidate.RecordId.Value); command.Parameters.AddWithValue("$revision", revision);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return OperationResults.Conflict<RecordEnvelope[]>(new BaseError { Code = "base.vector.snapshotChanged", Message = "The authoritative vector snapshot changed.", Category = ErrorCategory.Conflict }); records.Add(physical.ReadEnvelope(reader, _store.VectorStoreId));
        }
        return OperationResults.Ok(records.ToArray());
    }
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; Sessions.Remove(Snapshot); try { await Transaction.DisposeAsync().ConfigureAwait(false); await Connection.DisposeAsync().ConfigureAwait(false); } finally { await _generationLease.DisposeAsync().ConfigureAwait(false); } }
}
