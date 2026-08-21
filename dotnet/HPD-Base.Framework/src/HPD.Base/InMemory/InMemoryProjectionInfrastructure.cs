using System.Text.Json;
using System.Collections.Immutable;

namespace HPD.Base;

internal interface IInMemoryAtomicMutationProjection
{
    string Id { get; }
    ValueTask<OperationResult> InitializeAsync(BaseInMemoryProjectionInitializationContext context, CancellationToken cancellationToken);
    ValueTask<OperationResult> ApplyAsync(BaseInMemoryProjectionMutationContext context, CancellationToken cancellationToken);
}

internal sealed class InMemoryCompositeMutationProjection(params IInMemoryAtomicMutationProjection[] projections) : IInMemoryAtomicMutationProjection
{
    public string Id => "hpd.base.inmemory.projections.v1";
    public async ValueTask<OperationResult> InitializeAsync(BaseInMemoryProjectionInitializationContext context, CancellationToken cancellationToken)
    { foreach (IInMemoryAtomicMutationProjection projection in projections) { OperationResult result = await projection.InitializeAsync(context, cancellationToken).ConfigureAwait(false); if (!result.IsSuccess()) return result; } return OperationResults.NoContent(); }
    public async ValueTask<OperationResult> ApplyAsync(BaseInMemoryProjectionMutationContext context, CancellationToken cancellationToken)
    { foreach (IInMemoryAtomicMutationProjection projection in projections) { OperationResult result = await projection.ApplyAsync(context, cancellationToken).ConfigureAwait(false); if (!result.IsSuccess()) return result; } return OperationResults.NoContent(); }
}

internal sealed class InMemoryTextMutationProjection : IInMemoryAtomicMutationProjection
{
    public string Id => "hpd.base.inmemory.text.v1";
    public ValueTask<OperationResult> InitializeAsync(BaseInMemoryProjectionInitializationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((context.Options.Collections ?? []).Sum(static collection => (collection.TextIndexes ?? []).Length) > 256) throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid);
        return ValueTask.FromResult(OperationResults.NoContent());
    }
    public ValueTask<OperationResult> ApplyAsync(BaseInMemoryProjectionMutationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (BaseAtomicMutationProjectionFact fact in context.Request.Mutations)
        {
            CollectionDefinition? collection = (context.Options.Collections ?? []).SingleOrDefault(value => value.Id == fact.CollectionId);
            foreach (BaseTextIndexDefinition index in collection?.TextIndexes ?? [])
            {
                string slot = fact.CollectionId + "\n" + index.Id; InMemoryTextProjectionState state = context.ReadTextState(slot, 0);
                if (fact.After is null) { state.Carriers.Remove(fact.Before!.Id.Value); state.AppliedThrough = Math.Max(state.AppliedThrough, fact.JournalPosition.Value); context.WriteTextState(slot, state); continue; }
                long total = 0;
                foreach (BaseTextIndexFieldDefinition declared in index.Fields)
                {
                    BaseAtomicProjectionField? field = fact.After.Fields.Cast<BaseAtomicProjectionField?>().SingleOrDefault(value => value!.Value.StableFieldId == declared.StableFieldId);
                    if (field is null || field.Value.Value.Kind == BaseAtomicProjectionValueKind.Null) continue;
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(field.Value.Value.CanonicalJsonUtf8.ToArray());
                        string? source = document.RootElement.GetString(); ImmutableArray<string> tokens = BaseTextAnalyzer.Analyze(source);
                        total = checked(total + tokens.Sum(static token => (long)System.Text.Encoding.UTF8.GetByteCount(token)));
                    }
                    catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or OverflowException)
                    { return ValueTask.FromResult(Failure()); }
                }
                if (total > index.Limits.MaximumNormalizedBytesPerRecord) return ValueTask.FromResult(Failure());
                state.Carriers[fact.After.Id.Value] = new InMemoryTextCarrier(fact.After.Id, fact.After.Revision, fact.JournalPosition.Value);
                state.AppliedThrough = Math.Max(state.AppliedThrough, fact.JournalPosition.Value); context.WriteTextState(slot, state);
            }
        }
        if (context.Request.Purge is { } purge)
        {
            CollectionDefinition? collection = (context.Options.Collections ?? []).SingleOrDefault(value => value.Id == purge.CollectionId);
            foreach (BaseTextIndexDefinition index in collection?.TextIndexes ?? []) { string slot = purge.CollectionId + "\n" + index.Id; InMemoryTextProjectionState state = context.ReadTextState(slot, purge.PreviousGeneration); state.PurgeGeneration = purge.PublishedGeneration; context.WriteTextState(slot, state); }
        }
        return ValueTask.FromResult(OperationResults.NoContent());
    }
    private static OperationResult Failure() => new() { Status = OperationStatus.ValidationFailed, Error = new BaseError { Code = BaseTextErrorCodes.BudgetExceeded, Message = "The indexed text exceeds its installed limit.", Category = ErrorCategory.Validation } };
}

internal sealed class BaseInMemoryProjectionInitializationContext
{
    internal BaseInMemoryProjectionInitializationContext(HPDBaseInMemoryStoreOptions options) => Options = options;
    internal HPDBaseInMemoryStoreOptions Options { get; }
}

internal sealed class BaseInMemoryProjectionMutationContext
{
    internal BaseInMemoryProjectionMutationContext(InMemoryStoreState working, HPDBaseInMemoryStoreOptions options, BaseAtomicMutationProjectionRequest request, long? currentPosition = null)
    { _working = working; _currentPosition = currentPosition ?? working.GlobalMutationPosition; Options = options; Request = BaseAtomicMutationProjectionFactory.Clone(request); }
    private readonly InMemoryStoreState _working;
    private readonly long _currentPosition;
    internal long CurrentPosition => _currentPosition;
    internal HPDBaseInMemoryStoreOptions Options { get; }
    internal BaseAtomicMutationProjectionRequest Request { get; }

    internal bool TrySetRecordPosition(string collectionId, string recordId, RevisionToken revision, long position)
    {
        if (!_working.Collections.TryGetValue(collectionId, out InMemoryCollectionState? collection) ||
            !collection.RecordsById.TryGetValue(recordId, out StoredRecord? record) ||
            record.Metadata.Revision != revision)
            return false;
        collection.RecordsById[recordId] = record with { LatestMutationPosition = position };
        return true;
    }

    internal bool TryInspectCollection(string collectionId, out int recordCount, out long purgeGeneration)
    {
        if (!_working.Collections.TryGetValue(collectionId, out InMemoryCollectionState? collection))
        {
            recordCount = 0;
            purgeGeneration = 0;
            return true;
        }
        recordCount = collection.RecordsById.Count;
        purgeGeneration = collection.PurgeGeneration;
        return collection.RecordIdsOrdinal is not null && collection.RecordIdsOrdinal.Count == recordCount;
    }

    internal InMemoryVectorProjectionState ReadVectorState(string slot, long initialPurgeGeneration) =>
        _working.VectorProjections.TryGetValue(slot, out InMemoryVectorProjectionState? state)
            ? state.Clone()
            : new InMemoryVectorProjectionState { AppliedThrough = CurrentPosition, PurgeGeneration = initialPurgeGeneration };

    internal void WriteVectorState(string slot, InMemoryVectorProjectionState state) =>
        _working.VectorProjections[slot] = state.Clone();
    internal InMemoryTextProjectionState ReadTextState(string slot, long purge) => _working.TextProjections.TryGetValue(slot, out InMemoryTextProjectionState? state) ? state.Clone() : new InMemoryTextProjectionState { AppliedThrough = CurrentPosition, PurgeGeneration = purge };
    internal void WriteTextState(string slot, InMemoryTextProjectionState state) => _working.TextProjections[slot] = state.Clone();
}

internal sealed class InMemoryVectorMutationProjection : IInMemoryAtomicMutationProjection
{
    internal const string ContributorId = "hpd.base.inmemory.vector.v1";
    public string Id => ContributorId;

    public ValueTask<OperationResult> InitializeAsync(BaseInMemoryProjectionInitializationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CollectionDefinition[] definitions = context.Options.Collections ?? [];
        int collections = definitions.Count(static collection => (collection.VectorIndexes ?? []).Length != 0);
        int indexes = definitions.Sum(static collection => (collection.VectorIndexes ?? []).Length);
        if (definitions.Length > 256 || collections > 256 || indexes > 256 || definitions.Any(static collection => (collection.VectorIndexes ?? []).Length > 32))
            throw new InvalidOperationException("base.vector.inMemory.schemaUnsupported");
        return ValueTask.FromResult(OperationResults.NoContent());
    }

    public ValueTask<OperationResult> ApplyAsync(BaseInMemoryProjectionMutationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long carriers = 0, vectorBytes = 0;
        long appliedThrough = checked(context.CurrentPosition + context.Request.Mutations.Length);
        var latestRecords = new Dictionary<(string CollectionId, string RecordId), (BaseAtomicProjectionRecord? After, long Position)>();
        for (int mutationIndex = 0; mutationIndex < context.Request.Mutations.Length; mutationIndex++)
        {
            BaseAtomicMutationProjectionFact fact = context.Request.Mutations[mutationIndex];
            BaseAtomicProjectionRecord? identity = fact.After ?? fact.Before;
            if (identity is null) continue;
            latestRecords[(fact.CollectionId, identity.Id.Value)] = (
                fact.After,
                checked(context.CurrentPosition + mutationIndex + 1));
        }
        foreach (((string collectionId, string recordId), (BaseAtomicProjectionRecord? after, long position)) in latestRecords)
        {
            if (after is null) continue;
            if (!context.TrySetRecordPosition(collectionId, recordId, after.Revision, position))
                return ValueTask.FromResult(Failure(OperationStatus.StoreError, ErrorCategory.Store, "base.vector.inMemory.projectionInvalid", "The in-memory mutation position target is invalid."));
        }
        foreach (CollectionDefinition collection in (context.Options.Collections ?? []).Where(static item => (item.VectorIndexes ?? []).Length != 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.TryInspectCollection(collection.Id, out int recordCount, out long purgeGeneration))
                return ValueTask.FromResult(Failure(OperationStatus.StoreError, ErrorCategory.Store, "base.vector.inMemory.projectionInvalid", "The in-memory projection source is invalid."));
            if (recordCount > context.Options.MaxVectorSourceRecordsPerCollection) return ValueTask.FromResult(Failure(OperationStatus.StoreError, ErrorCategory.Store, "base.vector.inMemory.sourceCapacityExceeded", "The in-memory vector collection source capacity was exceeded."));
            foreach (VectorIndexDefinition index in collection.VectorIndexes ?? [])
            {
                string key = collection.Id + "\n" + index.Id;
                InMemoryVectorProjectionState projection = context.ReadVectorState(key, purgeGeneration);
                for (int mutationIndex = 0; mutationIndex < context.Request.Mutations.Length; mutationIndex++)
                {
                    if ((mutationIndex & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                    BaseAtomicMutationProjectionFact fact = context.Request.Mutations[mutationIndex];
                    long position = checked(context.CurrentPosition + mutationIndex + 1);
                    if (projection.AppliedThrough != position - 1)
                        return ValueTask.FromResult(Failure(OperationStatus.StoreError, ErrorCategory.Store, "base.vector.inMemory.projectionInvalid", "The in-memory projection position is invalid."));
                    if (string.Equals(fact.CollectionId, collection.Id, StringComparison.Ordinal))
                    {
                        BaseAtomicProjectionRecord? identity = fact.After ?? fact.Before;
                        if (identity is null)
                            return ValueTask.FromResult(Failure(OperationStatus.ValidationFailed, ErrorCategory.Validation, "base.vector.inMemory.projectionInvalid", "The vector projection mutation is invalid."));
                        if (fact.After is null)
                        {
                            projection.Carriers.Remove(identity.Id.Value);
                        }
                        else
                        {
                            ProjectionVectorRead vectorRead = ReadVector(fact.After, index, cancellationToken, out BaseVector vector);
                            if (vectorRead == ProjectionVectorRead.Invalid)
                                return ValueTask.FromResult(Failure(OperationStatus.ValidationFailed, ErrorCategory.Validation, "base.vector.inMemory.projectionInvalid", "The vector projection is invalid."));
                            if (vectorRead == ProjectionVectorRead.Absent) projection.Carriers.Remove(identity.Id.Value);
                            else projection.Carriers[identity.Id.Value] = new InMemoryVectorCarrier(identity.Id, fact.After.Revision, position, vector);
                        }
                    }
                    projection.AppliedThrough = position;
                }
                if (context.Request.Purge is { } purge && string.Equals(purge.CollectionId, collection.Id, StringComparison.Ordinal))
                {
                    if (projection.PurgeGeneration != purge.PreviousGeneration || purge.PublishedGeneration != checked(purge.PreviousGeneration + 1))
                        return ValueTask.FromResult(Failure(OperationStatus.StoreError, ErrorCategory.Store, "base.vector.inMemory.projectionInvalid", "The vector purge generation is invalid."));
                    projection.PurgeGeneration = purge.PublishedGeneration;
                }
                if (projection.AppliedThrough != appliedThrough)
                    return ValueTask.FromResult(Failure(OperationStatus.StoreError, ErrorCategory.Store, "base.vector.inMemory.projectionInvalid", "The in-memory projection high-water is invalid."));
                context.WriteVectorState(key, projection);
                foreach (InMemoryVectorCarrier carrier in projection.Carriers.Values)
                {
                    carriers = checked(carriers + 1);
                    vectorBytes = checked(vectorBytes + (long)carrier.Vector.Dimensions * sizeof(float));
                }
            }
        }
        if (carriers > context.Options.MaxVectorIndexedRecords || vectorBytes > context.Options.MaxVectorBytes)
            return ValueTask.FromResult(Failure(OperationStatus.StoreError, ErrorCategory.Store, "base.vector.inMemory.capacityExceeded", "The in-memory vector capacity was exceeded."));
        return ValueTask.FromResult(OperationResults.NoContent());
    }

    private enum ProjectionVectorRead { Absent, Valid, Invalid }

    private static ProjectionVectorRead ReadVector(BaseAtomicProjectionRecord record, VectorIndexDefinition index, CancellationToken cancellationToken, out BaseVector vector)
    {
        vector = default;
        BaseAtomicProjectionField? field = record.Fields.Cast<BaseAtomicProjectionField?>().SingleOrDefault(item => item!.Value.StableFieldId == index.VectorFieldId);
        if (field is null || field.Value.Value.Kind == BaseAtomicProjectionValueKind.Null) return ProjectionVectorRead.Absent;
        if (field.Value.Value.Kind != BaseAtomicProjectionValueKind.Array) return ProjectionVectorRead.Invalid;
        try
        {
            using JsonDocument document = JsonDocument.Parse(field.Value.Value.CanonicalJsonUtf8.ToArray());
            return TryVector(document.RootElement, index, cancellationToken, out vector) ? ProjectionVectorRead.Valid : ProjectionVectorRead.Invalid;
        }
        catch (JsonException)
        {
            return ProjectionVectorRead.Invalid;
        }
    }

    internal static bool TryVector(JsonElement json, VectorIndexDefinition index, CancellationToken cancellationToken, out BaseVector vector)
    {
        vector = default;
        if (json.ValueKind != JsonValueKind.Array) return false;
        float[] values;
        try { values = json.EnumerateArray().Select(static item => item.GetSingle()).ToArray(); } catch (Exception) { return false; }
        if (values.Length != index.Dimensions) return false;
        for (int offset = 0; offset < values.Length; offset += 4_096) cancellationToken.ThrowIfCancellationRequested();
        if (values.Any(static value => !float.IsFinite(value)) || index.Function == BaseVectorFunction.CosineSimilarity && values.All(static value => value == 0)) return false;
        vector = BaseVector.Create(values); return true;
    }

    private static OperationResult Failure(OperationStatus status, ErrorCategory category, string code, string message) => new() { Status = status, Error = new BaseError { Code = code, Message = message, Category = category } };
}
