using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

internal sealed class InMemoryTextProvider(InMemoryRecordStore store, BaseCollectionRegistry collections) : IBaseTextProvider, IBaseTextAuthority
{
    public IBaseTextAuthority Authority => this;
    public BaseTextProviderDescriptor Descriptor { get; } = new()
    {
        Id = "inmemory.text", Version = 1, ProviderClass = BaseTextProviderClass.CoLocatedTransactional,
        Capability = BaseTextPlatform.ProviderCapability(BaseTextProviderClass.CoLocatedTransactional),
        NativeDependencyReceipts = [], CertificationReceipt = ImmutableArray.Create(Convert.FromHexString("7999e8c9d8c5fe57dca7ae3eb6dfaec705c62f4cb93a6e3a50caff35314d54af")),
    };

    public async ValueTask<OperationResult<IBaseTextHydrationSession>> OpenAsync(BaseTextAuthorityOpenRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!collections.Collections.TryGetValue(request.CollectionId, out CollectionDefinition? collection)) return Missing();
        BaseTextIndexDefinition? index = collection.TextIndexes?.SingleOrDefault(value => value.Id == request.TextIndexId && value.Version == request.TextIndexVersion);
        if (index is null) return Missing();
        OperationResult<IInMemoryProjectionReadSession> captured = await ((IInMemoryProjectionAuthority)store).CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (!captured.IsSuccess() || captured.Value is not InMemoryProjectionReadSession session) return new() { Status = captured.Status, Error = captured.Error };
        return OperationResults.Ok<IBaseTextHydrationSession>(new Session(session, collection, index, Descriptor));
    }

    private static OperationResult<IBaseTextHydrationSession> Missing() => OperationResults.NotFound<IBaseTextHydrationSession>(new BaseError { Code = BaseTextErrorCodes.IndexNotFound, Message = "The text search index was not found.", Category = ErrorCategory.NotFound });

    public ValueTask<OperationResult<BaseTextIndexStatus[]>> ListAsync(CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); InMemoryStoreState root = store.CaptureVectorRoot(); return ValueTask.FromResult(OperationResults.Ok(collections.Collections.Values.SelectMany(collection => (collection.TextIndexes ?? []).Select(index => Status(root, collection, index))).ToArray())); }
    public ValueTask<OperationResult<BaseTextIndexStatus>> GetAsync(string collectionId, string textIndexId, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); if (!collections.Collections.TryGetValue(collectionId, out CollectionDefinition? collection) || (collection.TextIndexes ?? []).SingleOrDefault(value => value.Id == textIndexId) is not { } index) return ValueTask.FromResult(OperationResults.NotFound<BaseTextIndexStatus>(new BaseError { Code = BaseTextErrorCodes.IndexNotFound, Message = "The text search index was not found.", Category = ErrorCategory.NotFound })); return ValueTask.FromResult(OperationResults.Ok(Status(store.CaptureVectorRoot(), collection, index))); }
    public ValueTask<OperationResult<BaseTextRebuildResult>> RebuildAsync(BaseTextRebuildRequest request, CancellationToken cancellationToken = default)
    { if (!collections.Collections.TryGetValue(request.CollectionId, out CollectionDefinition? collection) || (collection.TextIndexes ?? []).SingleOrDefault(value => value.Id == request.TextIndexId) is not { } index) return ValueTask.FromResult(OperationResults.NotFound<BaseTextRebuildResult>(new BaseError { Code = BaseTextErrorCodes.IndexNotFound, Message = "The text search index was not found.", Category = ErrorCategory.NotFound })); return store.RebuildTextAsync(collection, index, request, cancellationToken); }
    private BaseTextIndexStatus Status(InMemoryStoreState root, CollectionDefinition collection, BaseTextIndexDefinition index) { InMemoryTextProjectionState state = root.TextProjections.GetValueOrDefault(collection.Id + "\n" + index.Id) ?? new InMemoryTextProjectionState { AppliedThrough = root.GlobalMutationPosition, PurgeGeneration = root.Collections.GetValueOrDefault(collection.Id)?.PurgeGeneration ?? 0 }; return new() { CollectionId = collection.Id, TextIndexId = index.Id, Version = index.Version, ProviderId = Descriptor.Id, Generation = state.Generation, PurgeGeneration = state.PurgeGeneration, State = BaseTextIndexState.Ready, AppliedThrough = new(state.AppliedThrough), SearchVisibleThrough = new(state.AppliedThrough), CarrierCount = state.Carriers.Count }; }

    private sealed class Session : IBaseTextHydrationSession
    {
        private readonly InMemoryProjectionReadSession _source;
        private readonly CollectionDefinition _collection;
        private readonly BaseTextIndexDefinition _index;
        private readonly BaseTextProviderDescriptor _descriptor;
        private readonly HashSet<Plan> _plans = new(ReferenceEqualityComparer.Instance);
        internal Session(InMemoryProjectionReadSession source, CollectionDefinition collection, BaseTextIndexDefinition index, BaseTextProviderDescriptor descriptor)
        {
            _source = source; _collection = collection; _index = index; _descriptor = descriptor;
            long head = source.ProjectionSnapshot.GlobalMutationHighWater; InMemoryTextProjectionState state = source.TextProjectionFor(collection.Id, index.Id);
            Snapshot = new BaseTextAuthoritySnapshot
            {
                StoreIdentityDigest = source.ProjectionSnapshot.StoreIdentityDigest, RestoreEpoch = source.ProjectionSnapshot.RestoreEpoch,
                SchemaGeneration = source.ProjectionSnapshot.SchemaGeneration, CollectionId = collection.Id,
                PurgeGeneration = source.ProjectionSnapshot.PurgeGenerations.GetValueOrDefault(collection.Id), TextIndexId = index.Id,
                TextIndexVersion = index.Version, TextIndexGeneration = state.Generation, AuthoritativeHead = new(head), AppliedThrough = new(state.AppliedThrough), SearchVisibleThrough = new(state.AppliedThrough),
                AnalyzerReceipt = BaseTextContractReceipts.AnalyzerReceipt, ScoringReceipt = BaseTextContractReceipts.ScoringReceipt,
            };
        }
        public BaseTextAuthoritySnapshot Snapshot { get; }
        public ValueTask<OperationResult<BaseTextConstraintPreparation>> PrepareAsync(BaseTextProviderPreparationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(request.Snapshot, Snapshot) && request.Snapshot != Snapshot || !request.Index.DefinitionChecksum.AsSpan().SequenceEqual(_index.DefinitionChecksum.AsSpan())) return ValueTask.FromResult(Invalid<BaseTextConstraintPreparation>());
            BaseTextLoweringReceipt receipt = BaseTextProviderEvidence.CreateLoweringReceipt(_descriptor, Snapshot, _index, request.QueryDigest, request.ConstraintDigest, request.InfluenceConstraints, request.Limits); var plan = new Plan(request.NormalizedQuery, request.Constraint, request.QueryDigest, request.ConstraintDigest, request.InfluenceConstraints, receipt); _plans.Add(plan);
            return ValueTask.FromResult(OperationResults.Ok(new BaseTextConstraintPreparation
            {
                QueryDigest = request.QueryDigest, ConstraintDigest = request.ConstraintDigest, Enforcement = BaseTextConstraintEnforcement.CompleteBeforeMatchingAndRanking,
                Receipt = receipt, Plan = plan,
            }));
        }
        public ValueTask<OperationResult<BaseTextProviderResult>> SearchAsync(BaseTextExecutionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Plan is not Plan plan || !_plans.Remove(plan) || Interlocked.Exchange(ref plan.Consumed, 1) != 0 || request.Snapshot != Snapshot) return ValueTask.FromResult(Invalid<BaseTextProviderResult>());
            long started = System.Diagnostics.Stopwatch.GetTimestamp(); long examined = 0, proofBytes = 0, orderingBytes = 0, prefixCount = 0, prefixBytes = 0;
            var candidates = new List<BaseTextCandidate>();
            IReadOnlyDictionary<string, StoredRecord> records = _source.RecordsFor(_collection.Id); InMemoryTextProjectionState projection = _source.TextProjectionFor(_collection.Id, _index.Id);
            foreach (InMemoryTextCarrier carrier in projection.Carriers.Values)
            {
                cancellationToken.ThrowIfCancellationRequested(); examined++;
                if (examined > _descriptor.Capability.MaximumIndexedRecords) return ValueTask.FromResult(Budget<BaseTextProviderResult>());
                if (!records.TryGetValue(carrier.RecordId.Value, out StoredRecord? record) || record.Metadata.Revision != carrier.Revision) return ValueTask.FromResult(Invalid<BaseTextProviderResult>());
                if (!BaseTextSemanticEvaluator.ConstraintMatches(record.Payload, _index, plan.Constraint)) continue;
                BaseTextEvaluatedCandidate? evaluated = BaseTextSemanticEvaluator.Evaluate(record.Payload, _index, plan.Query, plan.QueryDigest, plan.Influences); if (evaluated is null) continue;
                ImmutableArray<byte> boundary = BaseTextSemanticEvaluator.OrderingBoundary(evaluated.Score, record.Id); proofBytes = checked(proofBytes + BaseTextSemanticEvaluator.ProofRetainedBytes(evaluated.Proof)); orderingBytes = checked(orderingBytes + boundary.Length); prefixCount = checked(prefixCount + BaseTextSemanticEvaluator.PrefixExpansionCount(evaluated.Proof)); prefixBytes = checked(prefixBytes + BaseTextSemanticEvaluator.PrefixExpansionBytes(evaluated.Proof));
                candidates.Add(new BaseTextCandidate { RecordId = record.Id, Revision = record.Metadata.Revision!.Value, IndexedPosition = Snapshot.SearchVisibleThrough, Score = evaluated.Score, CanonicalOrderingBoundary = boundary, ScoreProof = evaluated.Proof });
                if (prefixCount > request.Limits.MaximumPrefixExpansions || prefixBytes > request.Limits.MaximumPrefixExpansionBytes || proofBytes > request.Limits.MaximumScoreProofBytes || orderingBytes > request.Limits.MaximumOrderingBytes || checked(proofBytes + orderingBytes + prefixBytes) > request.Limits.MaximumTransientBytes) return ValueTask.FromResult(Budget<BaseTextProviderResult>());
            }
            BaseTextCandidate[] ordered = candidates.OrderByDescending(static value => value.Score.Units).ThenBy(static value => value.RecordId.Value, StringComparer.Ordinal)
                .Where(value => request.AfterBoundary is null || value.CanonicalOrderingBoundary.AsSpan().SequenceCompareTo(request.AfterBoundary.Value.AsSpan()) > 0).Take(request.TakePlusOne).ToArray();
            bool more = ordered.Length == request.TakePlusOne;
            long returnedProofBytes = ordered.Sum(static value => BaseTextSemanticEvaluator.ProofRetainedBytes(value.ScoreProof)), returnedOrderingBytes = ordered.Sum(static value => (long)value.CanonicalOrderingBoundary.Length), returnedPrefixCount = ordered.Sum(static value => (long)BaseTextSemanticEvaluator.PrefixExpansionCount(value.ScoreProof)), returnedPrefixBytes = ordered.Sum(static value => BaseTextSemanticEvaluator.PrefixExpansionBytes(value.ScoreProof));
            ImmutableArray<BaseTextCandidate> page = [.. ordered]; long queryBytes = BaseTextQueryContract.Encode(plan.Query).Length; long constraintBytes = BaseTextSemanticEvaluator.ConstraintEncoding(plan.Constraint).Length;
            return ValueTask.FromResult(OperationResults.Ok(new BaseTextProviderResult
            {
                Snapshot = Snapshot, Candidates = page,
                Completeness = BaseTextProviderEvidence.CreateCompleteness(_descriptor, Snapshot, plan.Lowering, page, request.TakePlusOne),
                Accounting = new BaseTextProviderAccounting { InputBytes = checked(queryBytes + constraintBytes), QueryBytes = queryBytes, ConstraintBytes = constraintBytes, StatementParameters = BaseTextProviderEvidence.StatementParameterCount(plan.Query, plan.Constraint), AuthorizedRecordsExamined = examined, PostingsExamined = examined, PrefixExpansionCount = returnedPrefixCount, PrefixExpansionBytes = returnedPrefixBytes, ScoreProofBytes = returnedProofBytes, CandidateCount = ordered.Length, OrderingBytes = returnedOrderingBytes, ExactHydrationBytes = 0, ResultBytes = 0, CursorBytes = 0, RetainedTransientBytes = checked(queryBytes + constraintBytes + proofBytes + orderingBytes + prefixBytes), Elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started) },
            }));
        }
        public ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition collection, BaseTextCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); var result = new List<RecordEnvelope>(candidates.Length); IReadOnlyDictionary<string, StoredRecord> records = _source.RecordsFor(collection.Id);
            foreach (BaseTextCandidateIdentity candidate in candidates)
            {
                if (!records.TryGetValue(candidate.RecordId.Value, out StoredRecord? record) || record.Metadata.Revision != candidate.IndexedRevision) return ValueTask.FromResult(OperationResults.Conflict<RecordEnvelope[]>(new BaseError { Code = BaseTextErrorCodes.SnapshotChanged, Message = "The text snapshot changed.", Category = ErrorCategory.Conflict }));
                result.Add(new RecordEnvelope { CollectionId = collection.Id, Id = record.Id, Payload = RecordCloneHelpers.ClonePayload(record.Payload), Metadata = RecordCloneHelpers.CloneMetadata(record.Metadata) });
            }
            return ValueTask.FromResult(OperationResults.Ok(result.ToArray()));
        }
        public ValueTask DisposeAsync() => _source.DisposeAsync();
        private static OperationResult<T> Invalid<T>() => new() { Status = OperationStatus.StoreError, Error = new BaseError { Code = BaseTextErrorCodes.ProviderContractInvalid, Message = "The text provider returned invalid evidence.", Category = ErrorCategory.Store } };
        private static OperationResult<T> Budget<T>() => new() { Status = OperationStatus.ValidationFailed, Error = new BaseError { Code = BaseTextErrorCodes.BudgetExceeded, Message = "The text operation exceeded an installed bound.", Category = ErrorCategory.Validation } };
        private sealed class Plan(BaseTextQuery query, BaseTextCandidateConstraint constraint, ImmutableArray<byte> queryDigest, ImmutableArray<byte> constraintDigest, ImmutableArray<BaseTextFieldInfluenceConstraint> influences, BaseTextLoweringReceipt lowering) : BaseTextProviderPlan { internal BaseTextQuery Query { get; } = query; internal BaseTextCandidateConstraint Constraint { get; } = constraint; internal ImmutableArray<byte> QueryDigest { get; } = queryDigest; internal ImmutableArray<byte> ConstraintDigest { get; } = constraintDigest; internal ImmutableArray<BaseTextFieldInfluenceConstraint> Influences { get; } = influences; internal BaseTextLoweringReceipt Lowering { get; } = lowering; internal int Consumed; }
    }
}
