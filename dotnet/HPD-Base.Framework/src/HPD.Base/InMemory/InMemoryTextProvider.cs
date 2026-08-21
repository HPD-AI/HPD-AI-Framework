using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

internal sealed class InMemoryTextProvider(InMemoryRecordStore store, BaseCollectionRegistry collections) : IBaseTextAuthority
{
    public BaseTextProviderDescriptor Descriptor { get; } = new()
    {
        Id = "inmemory.text", Version = 1, ProviderClass = BaseTextProviderClass.CoLocatedTransactional,
        Capability = new BaseTextProviderCapability { TransactionalMaintenanceSupported = true, ExactRevisionHydrationSupported = true, PhraseSupported = true, PrefixSupported = true, MaximumLimits = BaseTextPlatform.DefaultLimits },
        NativeDependencyReceipts = [], CertificationReceipt = ImmutableArray.Create(SHA256.HashData("HPDB-INMEMORY-TEXT-CERT-1"u8)),
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

    private static OperationResult<IBaseTextHydrationSession> Missing() => OperationResults.NotFound<IBaseTextHydrationSession>(new BaseError { Code = BaseTextErrorCodes.IndexUnavailable, Message = "The text index is unavailable.", Category = ErrorCategory.NotFound });

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
            var plan = new Plan(request.NormalizedQuery, request.Constraint, request.QueryDigest, request.ConstraintDigest); _plans.Add(plan);
            byte[] receipt = SHA256.HashData([.. request.QueryDigest, .. request.ConstraintDigest, .. _index.DefinitionChecksum]);
            return ValueTask.FromResult(OperationResults.Ok(new BaseTextConstraintPreparation
            {
                QueryDigest = request.QueryDigest, ConstraintDigest = request.ConstraintDigest, Enforcement = BaseTextConstraintEnforcement.CompleteBeforeMatchingAndRanking,
                Receipt = new BaseTextLoweringReceipt { ProviderId = _descriptor.Id, ProviderVersion = _descriptor.Version, QueryDigest = request.QueryDigest, ConstraintDigest = request.ConstraintDigest, ReceiptDigest = ImmutableArray.Create(receipt) }, Plan = plan,
            }));
        }
        public ValueTask<OperationResult<BaseTextProviderResult>> SearchAsync(BaseTextExecutionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Plan is not Plan plan || !_plans.Remove(plan) || Interlocked.Exchange(ref plan.Consumed, 1) != 0 || request.Snapshot != Snapshot) return ValueTask.FromResult(Invalid<BaseTextProviderResult>());
            long started = System.Diagnostics.Stopwatch.GetTimestamp(); long examined = 0, proofBytes = 0, orderingBytes = 0;
            var candidates = new List<BaseTextCandidate>();
            IReadOnlyDictionary<string, StoredRecord> records = _source.RecordsFor(_collection.Id); InMemoryTextProjectionState projection = _source.TextProjectionFor(_collection.Id, _index.Id);
            foreach (InMemoryTextCarrier carrier in projection.Carriers.Values)
            {
                cancellationToken.ThrowIfCancellationRequested(); examined++;
                if (!records.TryGetValue(carrier.RecordId.Value, out StoredRecord? record) || record.Metadata.Revision != carrier.Revision) return ValueTask.FromResult(Invalid<BaseTextProviderResult>());
                if (!BaseTextSemanticEvaluator.ConstraintMatches(record.Payload, _index, plan.Constraint)) continue;
                BaseTextEvaluatedCandidate? evaluated = BaseTextSemanticEvaluator.Evaluate(record.Payload, _index, plan.Query, plan.QueryDigest); if (evaluated is null) continue;
                ImmutableArray<byte> boundary = BaseTextSemanticEvaluator.OrderingBoundary(evaluated.Score, record.Id); proofBytes += evaluated.Proof.ProofDigest.Length; orderingBytes += boundary.Length;
                candidates.Add(new BaseTextCandidate { RecordId = record.Id, Revision = record.Metadata.Revision!.Value, IndexedPosition = Snapshot.SearchVisibleThrough, Score = evaluated.Score, CanonicalOrderingBoundary = boundary, ScoreProof = evaluated.Proof });
            }
            BaseTextCandidate[] ordered = candidates.OrderByDescending(static value => value.Score.Units).ThenBy(static value => value.RecordId.Value, StringComparer.Ordinal)
                .Where(value => request.AfterBoundary is null || value.CanonicalOrderingBoundary.AsSpan().SequenceCompareTo(request.AfterBoundary.Value.AsSpan()) > 0).Take(request.TakePlusOne).ToArray();
            bool more = ordered.Length == request.TakePlusOne;
            return ValueTask.FromResult(OperationResults.Ok(new BaseTextProviderResult
            {
                Snapshot = Snapshot, Candidates = [.. ordered],
                Completeness = new BaseTextCompletenessEvidence { RequestedTakePlusOne = request.TakePlusOne, ReturnedCandidateCount = ordered.Length, HasMore = more, ReceiptDigest = ImmutableArray.Create(SHA256.HashData(plan.QueryDigest.AsSpan())) },
                Accounting = new BaseTextProviderAccounting { AuthorizedRecordsExamined = examined, PostingsExamined = examined, PrefixExpansionCount = 0, PrefixExpansionBytes = 0, ScoreProofBytes = proofBytes, CandidateCount = ordered.Length, OrderingBytes = orderingBytes, RetainedTransientBytes = checked(proofBytes + orderingBytes), Elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started) },
            }));
        }
        public ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition collection, BaseTextCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); var result = new List<RecordEnvelope>(candidates.Length); IReadOnlyDictionary<string, StoredRecord> records = _source.RecordsFor(collection.Id);
            foreach (BaseTextCandidateIdentity candidate in candidates)
            {
                if (!records.TryGetValue(candidate.RecordId.Value, out StoredRecord? record) || record.Metadata.Revision != candidate.IndexedRevision) return ValueTask.FromResult(OperationResults.Conflict<RecordEnvelope[]>(new BaseError { Code = BaseTextErrorCodes.HydrationSnapshotConflict, Message = "The text snapshot changed.", Category = ErrorCategory.Conflict }));
                result.Add(new RecordEnvelope { CollectionId = collection.Id, Id = record.Id, Payload = RecordCloneHelpers.ClonePayload(record.Payload), Metadata = RecordCloneHelpers.CloneMetadata(record.Metadata) });
            }
            return ValueTask.FromResult(OperationResults.Ok(result.ToArray()));
        }
        public ValueTask DisposeAsync() => _source.DisposeAsync();
        private static OperationResult<T> Invalid<T>() => new() { Status = OperationStatus.StoreError, Error = new BaseError { Code = BaseTextErrorCodes.ProviderContractInvalid, Message = "The text provider returned invalid evidence.", Category = ErrorCategory.Store } };
        private sealed class Plan(BaseTextQuery query, BaseTextCandidateConstraint constraint, ImmutableArray<byte> queryDigest, ImmutableArray<byte> constraintDigest) : BaseTextProviderPlan { internal BaseTextQuery Query { get; } = query; internal BaseTextCandidateConstraint Constraint { get; } = constraint; internal ImmutableArray<byte> QueryDigest { get; } = queryDigest; internal ImmutableArray<byte> ConstraintDigest { get; } = constraintDigest; internal int Consumed; }
    }
}
