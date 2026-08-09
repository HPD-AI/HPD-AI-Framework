using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed class InMemoryVectorProvider(
    InMemoryRecordStore store,
    BaseCollectionRegistry collections,
    BaseOpaqueTokenProtector tokens,
    HPDBaseVectorSnapshot options,
    TimeProvider timeProvider) :
    IBaseVectorProvider,
    IBaseVectorAuthority,
    IBaseVectorAdministrationProvider
{
    public BaseVectorProviderDescriptor Descriptor { get; } = new()
    {
        Id = "inmemory",
        Consistency = BaseVectorProviderConsistency.TransactionalCurrent,
        Exact = true,
        MaximumTopK = 1_000,
    };

    public async ValueTask<OperationResult<IBaseVectorHydrationSession>> OpenAsync(
        CollectionDefinition collection,
        VectorIndexDefinition index,
        BaseVectorConsistencyRequirement consistency,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OperationResult<IInMemoryProjectionReadSession> captured = await ((IInMemoryProjectionAuthority)store).CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (!captured.IsSuccess() || captured.Value is not InMemoryProjectionReadSession session)
            return new OperationResult<IBaseVectorHydrationSession> { Status = captured.Status, Error = captured.Error };
        BaseInMemoryProjectionIndexHandle? handle = session.ProjectionSnapshot.GetIndexHandles().SingleOrDefault(item =>
            string.Equals(item.Collection.Id, collection.Id, StringComparison.Ordinal) &&
            string.Equals(item.Index.Id, index.Id, StringComparison.Ordinal));
        if (handle is null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            return OperationResults.NotFound<IBaseVectorHydrationSession>(new BaseError { Code = BaseVectorErrorCodes.IndexNotFound, Message = "The vector index was not found.", Category = ErrorCategory.NotFound });
        }
        session.Bind(handle);
        return OperationResults.Ok<IBaseVectorHydrationSession>(session);
    }

    public ValueTask<BaseVectorConstraintPreparation> PrepareAsync(BaseVectorProviderPreparationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new BaseVectorConstraintPreparation
        {
            ConstraintDigest = request.ConstraintDigest,
            Enforcement = BaseVectorConstraintEnforcement.PreRankingExact,
            Plan = new Plan(request.Constraint),
        });
    }

    public ValueTask<BaseVectorProviderResult> SearchAsync(BaseVectorExecutionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Plan is not Plan plan) throw new InvalidOperationException("The InMemory vector plan is invalid.");
        InMemoryProjectionReadSession session = InMemoryProjectionReadSession.Find(request.Snapshot);
        IReadOnlyDictionary<string, StoredRecord> records = session.Records;
        CollectionDefinition collection = collections.Collections[request.Index.CollectionId];
        var ranked = new List<(StoredRecord Record, InMemoryVectorCarrier Carrier, double Measure)>();
        BaseInMemoryProjectionIndexHandle handle = session.ProjectionSnapshot.GetIndexHandles().Single(item =>
            string.Equals(item.Collection.Id, request.Index.CollectionId, StringComparison.Ordinal) &&
            string.Equals(item.Index.Id, request.Index.Id, StringComparison.Ordinal));
        foreach (InMemoryVectorCarrier carrier in session.State.GetCarriers(handle).Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!records.TryGetValue(carrier.RecordId.Value, out StoredRecord? record) || record.Metadata.Revision != carrier.Revision || !Matches(plan.Constraint, record, collection)) continue;
            ranked.Add((record, carrier, Measure(request.Index.Function, request.Vector, carrier.Vector)));
        }
        BaseVectorCandidate[] candidates = ranked
            .OrderBy(item => request.Index.Function == BaseVectorFunction.EuclideanDistance ? item.Measure : -item.Measure)
            .ThenBy(static item => item.Record.Id.Value, StringComparer.Ordinal)
            .ThenBy(static item => item.Record.Metadata.Revision!.Value.Value, StringComparer.Ordinal)
            .Take(request.Take)
            .Select((item, rank) => new BaseVectorCandidate
            {
                RecordId = item.Record.Id,
                IndexedRevision = item.Record.Metadata.Revision!.Value,
                IndexedPosition = new BaseMutationJournalPosition(item.Carrier.Position),
                Rank = rank + 1,
                Measure = new BaseVectorMeasure
                {
                    Function = request.Index.Function,
                    Value = item.Measure,
                    Direction = request.Index.Function == BaseVectorFunction.EuclideanDistance ? BaseVectorMeasureDirection.LowerIsNearer : BaseVectorMeasureDirection.HigherIsNearer,
                },
            }).ToArray();
        return ValueTask.FromResult(new BaseVectorProviderResult { Snapshot = request.Snapshot, Candidates = candidates, Accuracy = BaseVectorResultAccuracy.Exact });
    }

    public async ValueTask<OperationResult<BaseVectorIndexStatus[]>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OperationResult<IInMemoryProjectionReadSession> captured = await ((IInMemoryProjectionAuthority)store).CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (!captured.IsSuccess() || captured.Value is not InMemoryProjectionReadSession session)
            return CopyFailure<BaseVectorIndexStatus[], IInMemoryProjectionReadSession>(captured);
        await using (session.ConfigureAwait(false))
            return OperationResults.Ok(session.ProjectionSnapshot.GetIndexHandles().Select(Status).ToArray());
    }

    public async ValueTask<OperationResult<BaseVectorIndexStatus>> GetAsync(string collectionId, string vectorIndexId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OperationResult<IInMemoryProjectionReadSession> captured = await ((IInMemoryProjectionAuthority)store).CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (!captured.IsSuccess() || captured.Value is not InMemoryProjectionReadSession session)
            return CopyFailure<BaseVectorIndexStatus, IInMemoryProjectionReadSession>(captured);
        await using (session.ConfigureAwait(false))
        {
            BaseInMemoryProjectionIndexHandle? handle = session.ProjectionSnapshot.GetIndexHandles().SingleOrDefault(item => item.Collection.Id == collectionId && item.Index.Id == vectorIndexId);
            return handle is null
                ? OperationResults.NotFound<BaseVectorIndexStatus>(new BaseError { Code = BaseVectorErrorCodes.IndexNotFound, Message = "The vector index was not found.", Category = ErrorCategory.NotFound })
                : OperationResults.Ok(Status(handle));
        }
    }

    public async ValueTask<OperationResult<BaseVectorRebuildResult>> RebuildAsync(BaseVectorRebuildRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!collections.Collections.TryGetValue(request.CollectionId, out CollectionDefinition? collection) || (collection.VectorIndexes ?? []).All(index => index.Id != request.VectorIndexId))
            return OperationResults.NotFound<BaseVectorRebuildResult>(new BaseError { Code = BaseVectorErrorCodes.IndexNotFound, Message = "The vector index was not found.", Category = ErrorCategory.NotFound });
        VectorIndexDefinition index = collection.VectorIndexes!.Single(item => item.Id == request.VectorIndexId);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OperationResult<IInMemoryProjectionReadSession> captured = await ((IInMemoryProjectionAuthority)store).CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (!captured.IsSuccess() || captured.Value is not InMemoryProjectionReadSession session)
                return CopyFailure<BaseVectorRebuildResult, IInMemoryProjectionReadSession>(captured);
            await using (session.ConfigureAwait(false))
            {
                BaseInMemoryProjectionIndexHandle handle = session.ProjectionSnapshot.GetIndexHandles().Single(item =>
                    string.Equals(item.Collection.Id, request.CollectionId, StringComparison.Ordinal) &&
                    string.Equals(item.Index.Id, request.VectorIndexId, StringComparison.Ordinal));
                session.Bind(handle);
                if (request.ExpectedGeneration != handle.Generation || request.ExpectedPurgeGeneration != handle.PurgeGeneration)
                {
                    await Task.Yield();
                    continue;
                }
                OperationResult<IInMemoryProjectionReplacement> begun = await ((IInMemoryProjectionAuthority)store).BeginReplacementAsync(
                    session.ProjectionSnapshot.RootGeneration,
                    handle.Generation,
                    cancellationToken).ConfigureAwait(false);
                if (!begun.IsSuccess() || begun.Value is not InMemoryProjectionReplacement replacement)
                    return CopyFailure<BaseVectorRebuildResult, IInMemoryProjectionReplacement>(begun);
                await using (replacement.ConfigureAwait(false))
                {
                    replacement.Writer.EnsureIndex(handle);
                    BaseInMemoryProjectionSourceCursor? cursor = null;
                    do
                    {
                        OperationResult<BaseInMemoryProjectionSourcePage> page = await session.EnumerateProjectionSourceAsync(
                            handle,
                            new BaseInMemoryProjectionSourceScanRequest(1_024, cursor),
                            cancellationToken).ConfigureAwait(false);
                        if (!page.IsSuccess() || page.Value is null)
                            return CopyFailure<BaseVectorRebuildResult, BaseInMemoryProjectionSourcePage>(page);
                        foreach (BaseInMemoryProjectionSourceRecord record in page.Value.Records)
                            replacement.Writer.SetCarrier(handle, record);
                        cursor = page.Value.Cursor;
                    }
                    while (cursor is not null);
                    replacement.Writer.AdvanceAppliedPosition(handle, session.ProjectionSnapshot.GlobalMutationHighWater);
                    OperationResult<BaseInMemoryProjectionReplacementOutcome> publication = await replacement.PublishAsync(cancellationToken).ConfigureAwait(false);
                    if (!publication.IsSuccess())
                        return CopyFailure<BaseVectorRebuildResult, BaseInMemoryProjectionReplacementOutcome>(publication);
                    if (publication.Value is BaseInMemoryProjectionReplacementOutcome.RootGenerationChanged or BaseInMemoryProjectionReplacementOutcome.ProjectionGenerationChanged)
                    {
                        await Task.Yield();
                        continue;
                    }
                    if (publication.Value == BaseInMemoryProjectionReplacementOutcome.CapacityExceeded)
                        return Failure<BaseVectorRebuildResult>(OperationStatus.StoreError, "base.vector.inMemory.capacityExceeded", "The in-memory vector capacity was exceeded.", ErrorCategory.Store);
                    if (publication.Value != BaseInMemoryProjectionReplacementOutcome.Published)
                        return Failure<BaseVectorRebuildResult>(OperationStatus.StoreError, "base.vector.inMemory.projectionInvalid", "The in-memory vector projection is invalid.", ErrorCategory.Store);
                    BaseVectorAuthoritySnapshot snapshot = session.Snapshot with { VectorIndexGeneration = checked(handle.Generation + 1) };
                    DateTimeOffset completedAt = timeProvider.GetUtcNow();
                    return OperationResults.Ok(new BaseVectorRebuildResult { StoreId = request.StoreId, CollectionId = request.CollectionId, VectorIndexId = request.VectorIndexId, PreviousGeneration = handle.Generation, PublishedGeneration = checked(handle.Generation + 1), SourceSnapshot = snapshot, AppliedThrough = BaseVectorConsistencyTokenIssuer.Issue(snapshot, tokens, completedAt, checked(completedAt + options.ConsistencyTokenLifetime)), CompletedAt = completedAt });
                }
            }
        }
        return OperationResults.Conflict<BaseVectorRebuildResult>(new BaseError { Code = "base.vector.inMemory.generationChanged", Message = "The in-memory vector generation changed.", Category = ErrorCategory.Conflict });
    }

    private BaseVectorIndexStatus Status(BaseInMemoryProjectionIndexHandle handle) => new()
    {
        CollectionId = handle.Collection.Id,
        VectorIndexId = handle.Index.Id,
        VectorSpaceId = handle.Index.VectorSpaceId,
        Generation = handle.Generation,
        PurgeGeneration = handle.PurgeGeneration,
        AppliedThrough = new BaseMutationJournalPosition(handle.Owner.GlobalMutationHighWater),
        State = BaseVectorIndexState.Ready,
        ProviderId = Descriptor.Id,
    };

    private static OperationResult<TTarget> CopyFailure<TTarget, TSource>(OperationResult<TSource> result) => new()
    {
        Status = result.Status,
        Error = result.Error,
        Warnings = result.Warnings,
    };

    private static OperationResult<T> Failure<T>(OperationStatus status, string code, string message, ErrorCategory category) => new()
    {
        Status = status,
        Error = new BaseError { Code = code, Message = message, Category = category },
    };

    private static bool Matches(BaseVectorCandidateConstraint constraint, StoredRecord record, CollectionDefinition collection) => constraint switch
    {
        BaseVectorCandidateConstraint.True => true,
        BaseVectorCandidateConstraint.False => false,
        BaseVectorCandidateConstraint.And and => and.Children.All(child => Matches(child, record, collection)),
        BaseVectorCandidateConstraint.Or or => or.Children.Any(child => Matches(child, record, collection)),
        BaseVectorCandidateConstraint.Equal equal => TryReadFilter(record, equal.Field.StableFieldId, collection, out BaseVectorFilterValue value) && value.Equals(equal.Value),
        BaseVectorCandidateConstraint.In @in => TryReadFilter(record, @in.Field.StableFieldId, collection, out BaseVectorFilterValue value) && @in.Values.Contains(value),
        _ => false,
    };

    private static bool TryReadFilter(StoredRecord record, string fieldId, CollectionDefinition collection, out BaseVectorFilterValue value)
    {
        value = BaseVectorFilterValue.Null();
        string fieldName = collection.Fields?.SingleOrDefault(field => field.Id == fieldId)?.Name ?? fieldId;
        if (record.Payload.Fields is null ||
            !record.Payload.Fields.TryGetValue(fieldId, out JsonElement json) && !record.Payload.Fields.TryGetValue(fieldName, out json)) return false;
        bool identifier = collection.Fields?.SingleOrDefault(field => field.Id == fieldId)?.Type == BaseFieldTypes.Id;
        value = ReadFilterValue(json, identifier);
        return json.ValueKind is JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number or JsonValueKind.String;
    }

    internal static BaseVectorFilterValue ReadFilterValue(JsonElement json, bool identifier) => json.ValueKind switch
        {
            JsonValueKind.Null => BaseVectorFilterValue.Null(),
            JsonValueKind.True => BaseVectorFilterValue.FromBoolean(true),
            JsonValueKind.False => BaseVectorFilterValue.FromBoolean(false),
            JsonValueKind.Number when json.TryGetInt64(out long number) => BaseVectorFilterValue.FromInteger(number),
            JsonValueKind.String when identifier => BaseVectorFilterValue.FromId(json.GetString()!),
            JsonValueKind.String => BaseVectorFilterValue.FromString(json.GetString()!),
            _ => BaseVectorFilterValue.Null(),
        };

    private static double Measure(BaseVectorFunction function, BaseVector left, BaseVector right)
    {
        float[] a = left.ToArray(), b = right.ToArray();
        double dot = 0, aa = 0, bb = 0, squared = 0;
        for (int index = 0; index < a.Length; index++)
        {
            double leftValue = a[index], rightValue = b[index];
            dot += leftValue * rightValue;
            aa += leftValue * leftValue;
            bb += rightValue * rightValue;
            double difference = leftValue - rightValue;
            squared += difference * difference;
        }
        return function switch { BaseVectorFunction.CosineSimilarity => dot / Math.Sqrt(aa * bb), BaseVectorFunction.DotProductSimilarity => dot, _ => Math.Sqrt(squared) };
    }

    private sealed class Plan(BaseVectorCandidateConstraint constraint) : BaseVectorProviderPlan { internal BaseVectorCandidateConstraint Constraint { get; } = constraint; }

}
