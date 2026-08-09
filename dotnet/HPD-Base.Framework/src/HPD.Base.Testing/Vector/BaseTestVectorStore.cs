using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base.Testing;

/// <summary>Contains one immutable record and vector installed in the deterministic test provider.</summary>
public sealed record BaseTestVectorEntry
{
    /// <summary>Gets the authoritative record.</summary>
    public required RecordEnvelope Record { get; init; }
    /// <summary>Gets the externally produced vector.</summary>
    public required BaseVector Vector { get; init; }
    /// <summary>Gets closed filter values by stable field identifier.</summary>
    public IReadOnlyDictionary<string, BaseVectorFilterValue> Filters { get; init; } = ImmutableDictionary<string, BaseVectorFilterValue>.Empty;
}

/// <summary>Provides deterministic, explicitly seeded vector state for tests.</summary>
public sealed class BaseTestVectorStore
{
    private readonly object _gate = new();
    private ImmutableDictionary<(string Collection, string Index), ImmutableArray<BaseTestVectorEntry>> _entries = ImmutableDictionary<(string, string), ImmutableArray<BaseTestVectorEntry>>.Empty;
    private ImmutableDictionary<(string Collection, string Index), DerivedState> _derived = ImmutableDictionary<(string, string), DerivedState>.Empty;
    private long _prepareCalls;
    private long _searchCalls;

    /// <summary>Gets the number of provider preparation calls.</summary>
    public long PrepareCalls => Interlocked.Read(ref _prepareCalls);
    /// <summary>Gets the number of provider search calls.</summary>
    public long SearchCalls => Interlocked.Read(ref _searchCalls);
    internal void CountPrepare() => Interlocked.Increment(ref _prepareCalls);
    internal void CountSearch() => Interlocked.Increment(ref _searchCalls);

    /// <summary>Replaces all test entries for one vector index using owned immutable copies.</summary>
    public void Seed(string collectionId, string vectorIndexId, IEnumerable<BaseTestVectorEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId); ArgumentException.ThrowIfNullOrWhiteSpace(vectorIndexId); ArgumentNullException.ThrowIfNull(entries);
        ImmutableArray<BaseTestVectorEntry> copy = entries.Select(static entry => entry.Record.Metadata.Revision is null ? throw new ArgumentException("Every test vector record requires a revision.", nameof(entries)) : entry with { Record = entry.Record with { Payload = entry.Record.Payload with { Fields = (entry.Record.Payload.Fields ?? []).ToDictionary(static pair => new string(pair.Key.AsSpan()), static pair => pair.Value.Clone(), StringComparer.Ordinal) } }, Vector = BaseVector.Create(entry.Vector.ToArray()), Filters = entry.Filters.ToImmutableDictionary(static pair => new string(pair.Key.AsSpan()), static pair => pair.Value, StringComparer.Ordinal) }).ToImmutableArray();
        lock (_gate)
        {
            (string Collection, string Index) key = (new string(collectionId.AsSpan()), new string(vectorIndexId.AsSpan()));
            _entries = _entries.SetItem(key, copy);
            _derived = _derived.SetItem(key, new DerivedState(copy.Length, copy.Length, DateTimeOffset.UtcNow, false));
        }
    }

    /// <summary>Sets bounded durable-watermark evidence for a derived-provider test.</summary>
    public void SetDerivedState(string collectionId, string vectorIndexId, long authoritativePosition, long appliedPosition, DateTimeOffset appliedAt, bool rebuildRequired = false)
    {
        if (authoritativePosition < 0 || appliedPosition < 0 || appliedPosition > authoritativePosition || appliedAt.Offset != TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(appliedPosition));
        lock (_gate) _derived = _derived.SetItem((new string(collectionId.AsSpan()), new string(vectorIndexId.AsSpan())), new DerivedState(authoritativePosition, appliedPosition, appliedAt, rebuildRequired));
    }

    /// <summary>Applies one ordered journal position idempotently or closes the fixture on a gap.</summary>
    public void ApplyDerivedPosition(string collectionId, string vectorIndexId, long position, DateTimeOffset appliedAt)
    {
        lock (_gate)
        {
            (string Collection, string Index) key = (collectionId, vectorIndexId);
            DerivedState state = _derived.TryGetValue(key, out DerivedState? existing) ? existing : new DerivedState(position, 0, appliedAt, false);
            if (position <= state.AppliedPosition) return;
            _derived = _derived.SetItem(key, position == state.AppliedPosition + 1
                ? state with { AuthoritativePosition = Math.Max(state.AuthoritativePosition, position), AppliedPosition = position, AppliedAt = appliedAt }
                : state with { AuthoritativePosition = Math.Max(state.AuthoritativePosition, position), RebuildRequired = true });
        }
    }

    /// <summary>Marks journal retention as having overtaken the derived watermark.</summary>
    public void OvertakeDerivedRetention(string collectionId, string vectorIndexId)
    { lock (_gate) { (string, string) key = (collectionId, vectorIndexId); if (_derived.TryGetValue(key, out DerivedState? state)) _derived = _derived.SetItem(key, state with { RebuildRequired = true }); } }

    internal ImmutableArray<BaseTestVectorEntry> Read(string collectionId, string vectorIndexId) => _entries.TryGetValue((collectionId, vectorIndexId), out var entries) ? entries : [];
    internal DerivedState ReadDerived(string collectionId, string vectorIndexId) => _derived.TryGetValue((collectionId, vectorIndexId), out DerivedState? state) ? state : new DerivedState(0, 0, DateTimeOffset.UnixEpoch, false);
    internal sealed record DerivedState(long AuthoritativePosition, long AppliedPosition, DateTimeOffset AppliedAt, bool RebuildRequired);
}

internal sealed class BaseTestVectorProvider(BaseTestVectorStore store, BaseTestVectorProviderSnapshot options, TimeProvider timeProvider, BaseCollectionRegistry collections) : IBaseVectorProvider, IBaseVectorAuthority, IBaseVectorAdministrationProvider
{
    public BaseVectorProviderDescriptor Descriptor { get; } = new() { Id = "testing", Consistency = options.Consistency, Exact = true, MaximumTopK = 1_000 };

    public async ValueTask<OperationResult<IBaseVectorHydrationSession>> OpenAsync(CollectionDefinition collection, VectorIndexDefinition index, BaseVectorConsistencyRequirement consistency, OperationContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ImmutableArray<BaseTestVectorEntry> entries = store.Read(collection.Id, index.Id);
        BaseTestVectorStore.DerivedState derived = store.ReadDerived(collection.Id, index.Id);
        if (options.Consistency == BaseVectorProviderConsistency.DerivedJournal)
        {
            long currentTarget = consistency is BaseVectorConsistencyRequirement.Current ? derived.AuthoritativePosition : 0;
            while (consistency is BaseVectorConsistencyRequirement.Current && derived.AppliedPosition < currentTarget && !derived.RebuildRequired)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
                derived = store.ReadDerived(collection.Id, index.Id);
            }
            if (derived.RebuildRequired)
                return OperationResults.CapabilityUnavailable<IBaseVectorHydrationSession>(new BaseError { Code = BaseVectorErrorCodes.RebuildRequired, Message = "The derived vector index requires rebuild.", Category = ErrorCategory.Capability });
            if (consistency is BaseVectorConsistencyRequirement.BoundedStaleness bounded && timeProvider.GetUtcNow() - derived.AppliedAt > bounded.MaximumAge)
                return OperationResults.CapabilityUnavailable<IBaseVectorHydrationSession>(new BaseError { Code = BaseVectorErrorCodes.ConsistencyUnavailable, Message = "The derived vector index exceeds the requested staleness bound.", Category = ErrorCategory.Capability });
        }
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(collection.Id + ":" + index.Id)));
        long highWatermark = options.Consistency == BaseVectorProviderConsistency.DerivedJournal ? derived.AppliedPosition : entries.Length;
        return OperationResults.Ok<IBaseVectorHydrationSession>(new Session(entries, new BaseVectorAuthoritySnapshot { StoreIdentityDigest = digest, RestoreEpoch = 0, SchemaGeneration = 1, CollectionId = collection.Id, PurgeGeneration = 0, VectorIndexId = index.Id, VectorIndexGeneration = 1, VectorSpaceId = index.VectorSpaceId, HighWatermark = new BaseMutationJournalPosition(highWatermark) }));
    }

    public ValueTask<BaseVectorConstraintPreparation> PrepareAsync(BaseVectorProviderPreparationRequest request, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); store.CountPrepare(); return ValueTask.FromResult(new BaseVectorConstraintPreparation { ConstraintDigest = request.ConstraintDigest, Enforcement = BaseVectorConstraintEnforcement.PreRankingExact, Plan = new Plan(request.Constraint) }); }

    public async ValueTask<BaseVectorProviderResult> SearchAsync(BaseVectorExecutionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); if (request.Plan is not Plan plan) throw new InvalidOperationException("The test vector plan is invalid.");
        store.CountSearch();
        if (options.SearchDelay > TimeSpan.Zero)
            await Task.Delay(options.SearchDelay, options.IgnoreSearchCancellation ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
        ImmutableArray<BaseTestVectorEntry> entries = store.Read(request.Index.CollectionId, request.Index.Id);
        var ranked = entries.Where(entry => Matches(plan.Constraint, entry.Filters)).Select(entry => (Entry: entry, Measure: Measure(request.Index.Function, request.Vector, entry.Vector))).OrderBy(item => request.Index.Function == BaseVectorFunction.CosineSimilarity ? -item.Measure : item.Measure).ThenBy(static item => item.Entry.Record.Id.Value, StringComparer.Ordinal).ThenBy(static item => item.Entry.Record.Metadata.Revision!.Value.Value, StringComparer.Ordinal).Take(request.Take).Select((item, rank) => new BaseVectorCandidate { RecordId = item.Entry.Record.Id, IndexedRevision = item.Entry.Record.Metadata.Revision!.Value, IndexedPosition = new BaseMutationJournalPosition(rank + 1), Rank = rank + 1, Measure = new BaseVectorMeasure { Function = request.Index.Function, Value = item.Measure, Direction = request.Index.Function == BaseVectorFunction.EuclideanDistance ? BaseVectorMeasureDirection.LowerIsNearer : BaseVectorMeasureDirection.HigherIsNearer } }).ToArray();
        return new BaseVectorProviderResult { Snapshot = request.Snapshot, Candidates = ranked, Accuracy = BaseVectorResultAccuracy.Exact };
    }

    public async ValueTask<OperationResult<BaseVectorIndexStatus[]>> ListAsync(CancellationToken cancellationToken)
    {
        await DelayAdministration(cancellationToken).ConfigureAwait(false);
        BaseVectorIndexStatus[] statuses = collections.Collections.Values
            .SelectMany(collection => (collection.VectorIndexes ?? []).Select(index => Status(collection, index)))
            .OrderBy(static status => status.CollectionId, StringComparer.Ordinal)
            .ThenBy(static status => status.VectorIndexId, StringComparer.Ordinal)
            .ToArray();
        return OperationResults.Ok(statuses);
    }

    public async ValueTask<OperationResult<BaseVectorIndexStatus>> GetAsync(string collectionId, string vectorIndexId, CancellationToken cancellationToken)
    {
        await DelayAdministration(cancellationToken).ConfigureAwait(false);
        if (!collections.Collections.TryGetValue(collectionId, out CollectionDefinition? collection) ||
            (collection.VectorIndexes ?? []).SingleOrDefault(index => index.Id == vectorIndexId) is not { } index)
            return OperationResults.NotFound<BaseVectorIndexStatus>(new BaseError { Code = BaseVectorErrorCodes.IndexNotFound, Message = "The vector index was not found.", Category = ErrorCategory.NotFound });
        return OperationResults.Ok(Status(collection, index));
    }

    public ValueTask<OperationResult<BaseVectorRebuildResult>> RebuildAsync(BaseVectorRebuildRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(OperationResults.CapabilityUnavailable<BaseVectorRebuildResult>(new BaseError { Code = BaseVectorErrorCodes.CapabilityUnavailable, Message = "The testing provider does not rebuild indexes.", Category = ErrorCategory.Capability }));

    private async ValueTask DelayAdministration(CancellationToken cancellationToken)
    {
        if (options.AdministrationDelay > TimeSpan.Zero)
            await Task.Delay(options.AdministrationDelay, options.IgnoreAdministrationCancellation ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
    }

    private BaseVectorIndexStatus Status(CollectionDefinition collection, VectorIndexDefinition index)
    {
        BaseTestVectorStore.DerivedState derived = store.ReadDerived(collection.Id, index.Id);
        return new BaseVectorIndexStatus
        {
            CollectionId = collection.Id,
            VectorIndexId = index.Id,
            VectorSpaceId = index.VectorSpaceId,
            Generation = 1,
            PurgeGeneration = 0,
            AppliedThrough = new BaseMutationJournalPosition(options.Consistency == BaseVectorProviderConsistency.DerivedJournal ? derived.AppliedPosition : store.Read(collection.Id, index.Id).Length),
            State = derived.RebuildRequired ? BaseVectorIndexState.RebuildRequired : BaseVectorIndexState.Ready,
            ProviderId = Descriptor.Id,
        };
    }

    private static double Measure(BaseVectorFunction function, BaseVector left, BaseVector right)
    { float[] a = left.ToArray(), b = right.ToArray(); double dot = 0, aa = 0, bb = 0, squared = 0; for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; double d = a[i] - b[i]; squared += d * d; } return function switch { BaseVectorFunction.CosineSimilarity => dot / Math.Sqrt(aa * bb), BaseVectorFunction.DotProductSimilarity => dot, _ => Math.Sqrt(squared) }; }
    private static bool Matches(BaseVectorCandidateConstraint constraint, IReadOnlyDictionary<string, BaseVectorFilterValue> values) => constraint switch { BaseVectorCandidateConstraint.True => true, BaseVectorCandidateConstraint.False => false, BaseVectorCandidateConstraint.And and => and.Children.All(child => Matches(child, values)), BaseVectorCandidateConstraint.Or or => or.Children.Any(child => Matches(child, values)), BaseVectorCandidateConstraint.Equal equal => values.TryGetValue(equal.Field.StableFieldId, out var value) && value.Equals(equal.Value), BaseVectorCandidateConstraint.In @in => values.TryGetValue(@in.Field.StableFieldId, out var value) && @in.Values.Contains(value), _ => false };
    private sealed class Plan(BaseVectorCandidateConstraint constraint) : BaseVectorProviderPlan { internal BaseVectorCandidateConstraint Constraint { get; } = constraint; }
    private sealed class Session(ImmutableArray<BaseTestVectorEntry> entries, BaseVectorAuthoritySnapshot snapshot) : IBaseVectorHydrationSession
    {
        public BaseVectorAuthoritySnapshot Snapshot { get; } = snapshot;
        public ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition collection, BaseVectorCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); var records = new List<RecordEnvelope>(); foreach (var candidate in candidates) { BaseTestVectorEntry? entry = entries.SingleOrDefault(item => item.Record.Id == candidate.RecordId && item.Record.Metadata.Revision == candidate.IndexedRevision); if (entry is null) return ValueTask.FromResult(OperationResults.Conflict<RecordEnvelope[]>(new BaseError { Code = BaseVectorErrorCodes.SnapshotChanged, Message = "The test vector snapshot changed.", Category = ErrorCategory.Conflict })); records.Add(entry.Record); } return ValueTask.FromResult(OperationResults.Ok(records.ToArray())); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
