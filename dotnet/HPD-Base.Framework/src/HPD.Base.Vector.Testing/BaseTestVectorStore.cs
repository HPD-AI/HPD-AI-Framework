using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base.Vector.Testing;

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

    /// <summary>Replaces all test entries for one vector index using owned immutable copies.</summary>
    public void Seed(string collectionId, string vectorIndexId, IEnumerable<BaseTestVectorEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId); ArgumentException.ThrowIfNullOrWhiteSpace(vectorIndexId); ArgumentNullException.ThrowIfNull(entries);
        ImmutableArray<BaseTestVectorEntry> copy = entries.Select(static entry => entry.Record.Metadata.Revision is null ? throw new ArgumentException("Every test vector record requires a revision.", nameof(entries)) : entry with { Record = entry.Record with { Payload = entry.Record.Payload with { Fields = (entry.Record.Payload.Fields ?? []).ToDictionary(static pair => new string(pair.Key.AsSpan()), static pair => pair.Value.Clone(), StringComparer.Ordinal) } }, Vector = BaseVector.Create(entry.Vector.ToArray()), Filters = entry.Filters.ToImmutableDictionary(static pair => new string(pair.Key.AsSpan()), static pair => pair.Value, StringComparer.Ordinal) }).ToImmutableArray();
        lock (_gate) _entries = _entries.SetItem((new string(collectionId.AsSpan()), new string(vectorIndexId.AsSpan())), copy);
    }

    internal ImmutableArray<BaseTestVectorEntry> Read(string collectionId, string vectorIndexId) => _entries.TryGetValue((collectionId, vectorIndexId), out var entries) ? entries : [];
}

internal sealed class BaseTestVectorProvider(BaseTestVectorStore store) : IBaseVectorProvider, IBaseVectorAuthority
{
    public BaseVectorProviderDescriptor Descriptor { get; } = new() { Id = "testing", Consistency = BaseVectorProviderConsistency.TransactionalCurrent, Exact = true, MaximumTopK = 1_000 };

    public ValueTask<OperationResult<IBaseVectorHydrationSession>> OpenAsync(CollectionDefinition collection, VectorIndexDefinition index, BaseVectorConsistencyRequirement consistency, OperationContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ImmutableArray<BaseTestVectorEntry> entries = store.Read(collection.Id, index.Id);
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(collection.Id + ":" + index.Id)));
        return ValueTask.FromResult(OperationResults.Ok<IBaseVectorHydrationSession>(new Session(entries, new BaseVectorAuthoritySnapshot { StoreIdentityDigest = digest, RestoreEpoch = 0, SchemaGeneration = 1, CollectionId = collection.Id, PurgeGeneration = 0, VectorIndexId = index.Id, VectorIndexGeneration = 1, VectorSpaceId = index.VectorSpaceId, HighWatermark = new BaseMutationJournalPosition(entries.Length) })));
    }

    public ValueTask<BaseVectorConstraintPreparation> PrepareAsync(BaseVectorProviderPreparationRequest request, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(new BaseVectorConstraintPreparation { ConstraintDigest = request.ConstraintDigest, Enforcement = BaseVectorConstraintEnforcement.PreRankingExact, Plan = new Plan(request.Constraint) }); }

    public ValueTask<BaseVectorProviderResult> SearchAsync(BaseVectorExecutionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); if (request.Plan is not Plan plan) throw new InvalidOperationException("The test vector plan is invalid.");
        ImmutableArray<BaseTestVectorEntry> entries = store.Read(request.Index.CollectionId, request.Index.Id);
        var ranked = entries.Where(entry => Matches(plan.Constraint, entry.Filters)).Select(entry => (Entry: entry, Measure: Measure(request.Index.Function, request.Vector, entry.Vector))).OrderBy(item => request.Index.Function == BaseVectorFunction.CosineSimilarity ? -item.Measure : item.Measure).ThenBy(static item => item.Entry.Record.Id.Value, StringComparer.Ordinal).ThenBy(static item => item.Entry.Record.Metadata.Revision!.Value.Value, StringComparer.Ordinal).Take(request.Take).Select((item, rank) => new BaseVectorCandidate { RecordId = item.Entry.Record.Id, IndexedRevision = item.Entry.Record.Metadata.Revision!.Value, IndexedPosition = new BaseMutationJournalPosition(rank + 1), Rank = rank + 1, Measure = new BaseVectorMeasure { Function = request.Index.Function, Value = item.Measure, Direction = request.Index.Function == BaseVectorFunction.EuclideanDistance ? BaseVectorMeasureDirection.LowerIsNearer : BaseVectorMeasureDirection.HigherIsNearer } }).ToArray();
        return ValueTask.FromResult(new BaseVectorProviderResult { Snapshot = request.Snapshot, Candidates = ranked, Accuracy = BaseVectorResultAccuracy.Exact });
    }

    private static double Measure(BaseVectorFunction function, BaseVector left, BaseVector right)
    { float[] a = left.ToArray(), b = right.ToArray(); double dot = 0, aa = 0, bb = 0, squared = 0; for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; double d = a[i] - b[i]; squared += d * d; } return function switch { BaseVectorFunction.CosineSimilarity => dot / Math.Sqrt(aa * bb), BaseVectorFunction.DotProductSimilarity => dot, _ => Math.Sqrt(squared) }; }
    private static bool Matches(BaseVectorCandidateConstraint constraint, IReadOnlyDictionary<string, BaseVectorFilterValue> values) => constraint switch { BaseVectorCandidateConstraint.True => true, BaseVectorCandidateConstraint.False => false, BaseVectorCandidateConstraint.And and => and.Children.All(child => Matches(child, values)), BaseVectorCandidateConstraint.Or or => or.Children.Any(child => Matches(child, values)), BaseVectorCandidateConstraint.Equal equal => values.TryGetValue(equal.Field.StableFieldId, out var value) && value == equal.Value, BaseVectorCandidateConstraint.In @in => values.TryGetValue(@in.Field.StableFieldId, out var value) && @in.Values.Contains(value), _ => false };
    private sealed class Plan(BaseVectorCandidateConstraint constraint) : BaseVectorProviderPlan { internal BaseVectorCandidateConstraint Constraint { get; } = constraint; }
    private sealed class Session(ImmutableArray<BaseTestVectorEntry> entries, BaseVectorAuthoritySnapshot snapshot) : IBaseVectorHydrationSession
    {
        public BaseVectorAuthoritySnapshot Snapshot { get; } = snapshot;
        public ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition collection, BaseVectorCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); var records = new List<RecordEnvelope>(); foreach (var candidate in candidates) { BaseTestVectorEntry? entry = entries.SingleOrDefault(item => item.Record.Id == candidate.RecordId && item.Record.Metadata.Revision == candidate.IndexedRevision); if (entry is null) return ValueTask.FromResult(OperationResults.Conflict<RecordEnvelope[]>(new BaseError { Code = BaseVectorErrorCodes.SnapshotChanged, Message = "The test vector snapshot changed.", Category = ErrorCategory.Conflict })); records.Add(entry.Record); } return ValueTask.FromResult(OperationResults.Ok(records.ToArray())); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
