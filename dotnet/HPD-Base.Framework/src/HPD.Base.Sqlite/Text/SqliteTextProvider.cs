using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

internal sealed class SqliteTextProvider(SqliteRecordStore store, BaseCollectionRegistry collections, SqliteTextModel model) : IBaseTextAuthority
{
    public BaseTextProviderDescriptor Descriptor { get; } = new()
    {
        Id = "sqlite.fts5", Version = 1, ProviderClass = BaseTextProviderClass.CoLocatedTransactional,
        Capability = new BaseTextProviderCapability { TransactionalMaintenanceSupported = true, ExactRevisionHydrationSupported = true, PhraseSupported = true, PrefixSupported = true, MaximumLimits = BaseTextPlatform.DefaultLimits },
        NativeDependencyReceipts = ["sqlite-bundled"], CertificationReceipt = ImmutableArray.Create(SHA256.HashData("HPDB-SQLITE-TEXT-CERT-1"u8)),
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
    private static OperationResult<IBaseTextHydrationSession> Missing() => OperationResults.NotFound<IBaseTextHydrationSession>(new BaseError { Code = BaseTextErrorCodes.IndexUnavailable, Message = "The text index is unavailable.", Category = ErrorCategory.NotFound });

    private sealed class Session(SqliteRecordStore store, SqliteConnection connection, SqliteTransaction transaction, IAsyncDisposable lease, CollectionDefinition collection, BaseTextIndexDefinition index, SqliteTextModel.IndexModel indexModel, BaseTextProviderDescriptor descriptor, BaseTextAuthoritySnapshot snapshot) : IBaseTextHydrationSession
    {
        private readonly HashSet<Plan> _plans = new(ReferenceEqualityComparer.Instance); private int _disposed;
        public BaseTextAuthoritySnapshot Snapshot { get; } = snapshot;
        public ValueTask<OperationResult<BaseTextConstraintPreparation>> PrepareAsync(BaseTextProviderPreparationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); if (request.Snapshot != Snapshot || !request.Index.DefinitionChecksum.AsSpan().SequenceEqual(index.DefinitionChecksum.AsSpan())) return ValueTask.FromResult(Invalid<BaseTextConstraintPreparation>());
            var plan = new Plan(request.NormalizedQuery, request.Constraint, request.QueryDigest, request.ConstraintDigest); _plans.Add(plan); ImmutableArray<byte> receipt = ImmutableArray.Create(SHA256.HashData([.. request.QueryDigest, .. request.ConstraintDigest, .. index.DefinitionChecksum]));
            return ValueTask.FromResult(OperationResults.Ok(new BaseTextConstraintPreparation { QueryDigest = request.QueryDigest, ConstraintDigest = request.ConstraintDigest, Enforcement = BaseTextConstraintEnforcement.CompleteBeforeMatchingAndRanking, Receipt = new BaseTextLoweringReceipt { ProviderId = descriptor.Id, ProviderVersion = descriptor.Version, QueryDigest = request.QueryDigest, ConstraintDigest = request.ConstraintDigest, ReceiptDigest = receipt }, Plan = plan }));
        }
        public async ValueTask<OperationResult<BaseTextProviderResult>> SearchAsync(BaseTextExecutionRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Plan is not Plan plan || !_plans.Remove(plan) || Interlocked.Exchange(ref plan.Consumed, 1) != 0 || request.Snapshot != Snapshot) return Invalid<BaseTextProviderResult>(); long started = System.Diagnostics.Stopwatch.GetTimestamp();
            SqlitePhysicalModel.CollectionModel physical = store.VectorPhysicalModel.Collection(collection.Id); await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = store.VectorCommandTimeoutSeconds; string select = string.Join(", ", physical.SelectList.Split(", ", StringSplitOptions.None).Select(static column => "r." + column)); command.CommandText = $"SELECT {select} FROM {indexModel.Table} x JOIN {physical.Table} r ON r.record_id=x.record_id AND ('sqlite:' || CAST(r.revision AS TEXT))=x.revision ORDER BY x.record_id COLLATE BINARY ASC;";
            var candidates = new List<BaseTextCandidate>(); long examined = 0, proofBytes = 0, orderingBytes = 0; await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                examined++; RecordEnvelope record = physical.ReadEnvelope(reader, store.VectorStoreId); if (!BaseTextSemanticEvaluator.ConstraintMatches(record.Payload, index, plan.Constraint)) continue; BaseTextEvaluatedCandidate? evaluated = BaseTextSemanticEvaluator.Evaluate(record.Payload, index, plan.Query, plan.QueryDigest); if (evaluated is null) continue;
                ImmutableArray<byte> boundary = BaseTextSemanticEvaluator.OrderingBoundary(evaluated.Score, record.Id); proofBytes += evaluated.Proof.ProofDigest.Length; orderingBytes += boundary.Length; candidates.Add(new BaseTextCandidate { RecordId = record.Id, Revision = record.Metadata.Revision!.Value, IndexedPosition = Snapshot.SearchVisibleThrough, Score = evaluated.Score, CanonicalOrderingBoundary = boundary, ScoreProof = evaluated.Proof });
            }
            BaseTextCandidate[] result = candidates.OrderByDescending(static value => value.Score.Units).ThenBy(static value => value.RecordId.Value, StringComparer.Ordinal).Where(value => request.AfterBoundary is null || value.CanonicalOrderingBoundary.AsSpan().SequenceCompareTo(request.AfterBoundary.Value.AsSpan()) > 0).Take(request.TakePlusOne).ToArray();
            return OperationResults.Ok(new BaseTextProviderResult { Snapshot = Snapshot, Candidates = [.. result], Completeness = new BaseTextCompletenessEvidence { RequestedTakePlusOne = request.TakePlusOne, ReturnedCandidateCount = result.Length, HasMore = result.Length == request.TakePlusOne, ReceiptDigest = ImmutableArray.Create(SHA256.HashData(plan.QueryDigest.AsSpan())) }, Accounting = new BaseTextProviderAccounting { AuthorizedRecordsExamined = examined, PostingsExamined = examined, PrefixExpansionCount = 0, PrefixExpansionBytes = 0, ScoreProofBytes = proofBytes, CandidateCount = result.Length, OrderingBytes = orderingBytes, RetainedTransientBytes = checked(proofBytes + orderingBytes), Elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started) } });
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
        private static OperationResult<RecordEnvelope[]> Conflict() => OperationResults.Conflict<RecordEnvelope[]>(new BaseError { Code = BaseTextErrorCodes.HydrationSnapshotConflict, Message = "The text snapshot changed.", Category = ErrorCategory.Conflict });
        private static OperationResult<T> Invalid<T>() => new() { Status = OperationStatus.StoreError, Error = new BaseError { Code = BaseTextErrorCodes.ProviderContractInvalid, Message = "The text provider returned invalid evidence.", Category = ErrorCategory.Store } };
        private sealed class Plan(BaseTextQuery query, BaseTextCandidateConstraint constraint, ImmutableArray<byte> queryDigest, ImmutableArray<byte> constraintDigest) : BaseTextProviderPlan { internal BaseTextQuery Query { get; } = query; internal BaseTextCandidateConstraint Constraint { get; } = constraint; internal ImmutableArray<byte> QueryDigest { get; } = queryDigest; internal ImmutableArray<byte> ConstraintDigest { get; } = constraintDigest; internal int Consumed; }
    }
}
