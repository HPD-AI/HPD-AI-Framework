using HPD.Events;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

/// <summary>
/// Process-local, thread-safe, non-durable HPD.BASE record store implementation.
/// </summary>
internal sealed partial class InMemoryRecordStore : IAtomicRecordStore, IStreamingRecordStore, IRelationalReadStore, IConsistentRecordIncludeStore
{
    private readonly HPDBaseInMemoryStoreOptions _options;
    private readonly BaseQueryCursorCodec _queryCursors;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private InMemoryStoreState _publishedState = new();
    private long _generation;

    /// <summary>
    /// Initializes a new store using configured options.
    /// </summary>
    /// <param name="options">The configured InMemory options.</param>
    public InMemoryRecordStore(
        IOptions<HPDBaseInMemoryStoreOptions> options,
        BaseOpaqueTokenProtector tokenProtector,
        TimeProvider timeProvider)
        : this(options.Value, tokenProtector, timeProvider)
    {
    }

    /// <summary>
    /// Initializes a new store using the supplied options, or defaults when omitted.
    /// </summary>
    /// <param name="options">The InMemory options.</param>
    public InMemoryRecordStore(HPDBaseInMemoryStoreOptions? options = null)
        : this(options, CreateProcessLocalTokenProtector(), TimeProvider.System)
    {
    }

    internal InMemoryRecordStore(
        HPDBaseInMemoryStoreOptions? options,
        BaseOpaqueTokenProtector tokenProtector,
        TimeProvider timeProvider)
    {
        _options = options ?? new HPDBaseInMemoryStoreOptions();
        _queryCursors = new BaseQueryCursorCodec(tokenProtector, timeProvider);
        ValidateOptions(_options);
        Capabilities = CreateCapabilities(_options);
        Includes = new RecordIncludeExecutionCapability
        {
            Supported = true,
            MaxDepth = 3,
            MaxIncludes = 8,
            MaxRecords = Math.Min(1_000, _options.MaxPageSize),
            SnapshotConsistency = true,
        };
    }

    private static BaseOpaqueTokenProtector CreateProcessLocalTokenProtector() =>
        new(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 0,
                Key = RandomNumberGenerator.GetBytes(32)
            }
        }));

    /// <inheritdoc />
    public StoreCapabilityDescriptor Capabilities { get; }

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordPage>> ListAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        HPDBaseInMemoryTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.StoreList,
            BaseOperationKind.List,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => ListCoreAsync(collection, query, context, cancellationToken));

    private ValueTask<OperationResult<RecordPage>> ListCoreAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (InMemoryValidation.ValidateCollectionId<RecordPage>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (ValidateUnsupportedQuery<RecordPage>(query, allowCount: true) is { } queryError)
        {
            return ValueTask.FromResult(queryError);
        }

        var published = Volatile.Read(ref _publishedState);
        InMemoryCollectionState? collectionState = GetCollectionOrNull(published, collection.Id);
        var snapshot = collectionState?.RecordsById.Values
            .OrderBy(record => record.AppendPosition)
            .ThenBy(record => record.Id.Value, StringComparer.Ordinal)
            .ToArray() ?? [];

        var filtered = new List<StoredRecord>(snapshot.Length);
        foreach (var record in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query.Filter is null || MatchesFilter(record, query.Filter))
            {
                filtered.Add(record);
            }
        }

        var sortedResult = ApplySort<RecordPage>(filtered, query.Sort);
        if (sortedResult.Result is not null)
        {
            return ValueTask.FromResult(sortedResult.Result);
        }

        var sorted = sortedResult.Value!;
        var total = sorted.Count;
        var pageResult = ApplyPage<RecordPage>(sorted, query, collection, context, collectionState, out var pageInfo);
        if (pageResult.Result is not null)
        {
            return ValueTask.FromResult(pageResult.Result);
        }

        var page = pageResult.Value!;
        var items = ApplySelect(page, query.Select)
            .Select(RecordCloneHelpers.CloneEnvelope)
            .ToArray();

        var recordPage = new RecordPage
        {
            Items = items,
            Page = pageInfo,
            Count = query.Count == QueryCountMode.None
                ? null
                : new CountInfo { Mode = query.Count, Total = total, IsExact = true }
        };

        return ValueTask.FromResult(OperationResults.Ok(recordPage));
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        HPDBaseInMemoryTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.StoreGet,
            BaseOperationKind.Get,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => GetCoreAsync(collection, id, context, cancellationToken));

    private ValueTask<OperationResult<RecordEnvelope>> GetCoreAsync(
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var published = Volatile.Read(ref _publishedState);
        return GetFromStateAsync(published, collection, id, context, cancellationToken);
    }

    private static ValueTask<OperationResult<RecordEnvelope>> GetFromStateAsync(
        InMemoryStoreState state,
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        var record = GetCollectionOrNull(state, collection.Id)?.RecordsById.GetValueOrDefault(id.Value);
        if (record is null)
        {
            return ValueTask.FromResult(InMemoryResultFactory.NotFound<RecordEnvelope>(id.Value));
        }

        var envelope = RecordCloneHelpers.CloneEnvelope(record);
        return ValueTask.FromResult(InMemoryResultFactory.WithRevision(OperationResults.Ok(envelope), record.Metadata));
    }

    /// <inheritdoc />
    public ValueTask<RecordMutationExecutionResult> ExecuteSingleAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(processor, request, cancellationToken);

    /// <inheritdoc />
    public ValueTask<RecordMutationExecutionResult> ExecuteAtomicAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(processor, request, cancellationToken);

    private async ValueTask<RecordMutationExecutionResult> ExecuteMutationAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(request);
        ValidateExecutionRequest(request);

        InMemoryStoreState working;
        long capturedGeneration;
        using var acquisitionLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acquisitionLifetime.CancelAfter(request.AcquisitionTimeout);
        try
        {
            await _stateGate.WaitAsync(acquisitionLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? CancelledRollback("The mutation was cancelled before its state snapshot was acquired.")
                : Rollback(
                    BaseMutationErrorCodes.TransactionTimeout,
                    "The mutation state snapshot could not be acquired in time.");
        }

        try
        {
            capturedGeneration = _generation;
            working = Volatile.Read(ref _publishedState).Clone();
        }
        finally
        {
            _stateGate.Release();
        }

        if (acquisitionLifetime.IsCancellationRequested)
        {
            return cancellationToken.IsCancellationRequested
                ? CancelledRollback("The mutation was cancelled while its state snapshot was acquired.")
                : Rollback(
                    BaseMutationErrorCodes.TransactionTimeout,
                    "The mutation state snapshot could not be acquired in time.");
        }

        string? receiptKey = request.AtomicRequest is null ? null : ReceiptKey(request.AtomicRequest.Identity);
        if (receiptKey is not null && working.Receipts.TryGetValue(receiptKey, out InMemoryMutationReceipt? receipt))
        {
            if (receipt.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                working.Receipts.Remove(receiptKey);
            }
            else
            {
                AtomicMutationProcessingResult resolved = await processor.ResolveReceiptAsync(
                    receipt.Mutations.Select(RecordCloneHelpers.CloneMutationFact).ToArray(),
                    cancellationToken).ConfigureAwait(false);
                if (resolved.Outcome != AtomicMutationProcessingOutcome.ReadyToCommit)
                    return new RecordMutationExecutionResult(RecordMutationExecutionOutcome.RollbackConfirmed, resolved, resolved.Error);

                bool fingerprintsMatch = CryptographicOperations.FixedTimeEquals(
                    request.AtomicRequest!.Identity.Fingerprint.ToArray(), receipt.Fingerprint);
                bool structuresMatch = CryptographicOperations.FixedTimeEquals(
                    request.AtomicRequest.StructuralDigest, receipt.StructuralDigest);
                if (!fingerprintsMatch || !structuresMatch)
                    return Rollback(BaseMutationRequestErrorCodes.FingerprintConflict, "The mutation request identity conflicts with an existing receipt.");

                return new RecordMutationExecutionResult(RecordMutationExecutionOutcome.Committed, resolved)
                {
                    RequestDisposition = BaseMutationRequestDisposition.Duplicate,
                };
            }
        }

        using var processingLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        processingLifetime.CancelAfter(request.TransactionTimeout);
        var session = new AtomicSession(this, working);
        AtomicMutationProcessingResult processing;
        try
        {
            var processingTask =
                processor.ProcessAsync(session, processingLifetime.Token).AsTask();
            try
            {
                processing = await processingTask
                    .WaitAsync(processingLifetime.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                ObserveCompletion(processingTask);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            await session.CloseAsync().ConfigureAwait(false);
            return cancellationToken.IsCancellationRequested
                ? CancelledRollback("The mutation was cancelled before commit.")
                : Rollback(
                    BaseMutationErrorCodes.TransactionTimeout,
                    "The mutation transaction exceeded its bounded lifetime.");
        }
        catch
        {
            await session.CloseAsync().ConfigureAwait(false);
            return Rollback(
                InMemoryErrorCodes.MutationProcessorFailed,
                "The mutation processor failed.");
        }

        await session.CloseAsync().ConfigureAwait(false);
        if (processingLifetime.IsCancellationRequested)
        {
            return cancellationToken.IsCancellationRequested
                ? CancelledRollback("The mutation was cancelled before commit.", processing)
                : Rollback(
                    BaseMutationErrorCodes.TransactionTimeout,
                    "The mutation transaction exceeded its bounded lifetime.",
                    processing);
        }

        if (processing.Outcome != AtomicMutationProcessingOutcome.ReadyToCommit)
        {
            return new RecordMutationExecutionResult(
                RecordMutationExecutionOutcome.RollbackConfirmed,
                processing,
                processing.Error);
        }

        if (request.AtomicRequest is { } identified)
        {
            int receiptBytes = JsonSerializer.SerializeToUtf8Bytes(
                processing.Mutations,
                HPDBaseJsonSerializerContext.Default.BaseRecordMutationFactArray).Length;
            if (receiptBytes > identified.MaxReceiptBytes)
                return Rollback(BaseMutationRequestErrorCodes.ReceiptTooLarge, "The mutation receipt exceeds its configured bound.", processing);
            working.Receipts[receiptKey!] = new InMemoryMutationReceipt(
                identified.Identity.Fingerprint.ToArray(),
                [.. identified.StructuralDigest],
                processing.Mutations.Select(RecordCloneHelpers.CloneMutationFact).ToArray(),
                identified.ExpiresAt);
        }

        using var commitLifetime = new CancellationTokenSource(request.CommitCompletionTimeout);
        try
        {
            await _stateGate.WaitAsync(commitLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Rollback(
                BaseMutationErrorCodes.TransactionTimeout,
                "The mutation state could not be published in time.",
                processing);
        }

        try
        {
            if (_generation != capturedGeneration)
            {
                return new RecordMutationExecutionResult(
                    RecordMutationExecutionOutcome.ConflictRollbackConfirmed,
                    processing,
                    Error(
                        BaseMutationErrorCodes.TransactionConflict,
                        "The InMemory mutation snapshot was superseded by a concurrent commit."));
            }

            Volatile.Write(ref _publishedState, working);
            _generation++;
            return new RecordMutationExecutionResult(
                RecordMutationExecutionOutcome.Committed,
                processing);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private static void ValidateExecutionRequest(RecordMutationExecutionRequest request)
    {
        if (request.AcquisitionTimeout <= TimeSpan.Zero
            || request.TransactionTimeout <= TimeSpan.Zero
            || request.CommitCompletionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Execution timeouts must be positive.");
        }
        if (request.AtomicRequest is { StructuralDigest.Length: not 32 } or { MaxReceiptBytes: < 4096 })
            throw new ArgumentOutOfRangeException(nameof(request), "The identified mutation request bounds are invalid.");
    }

    private static string ReceiptKey(BaseMutationRequestIdentity identity) =>
        string.Concat(identity.Scope, "\u001f", identity.Operation, "\u001f", identity.IdempotencyKey);

    private static RecordMutationExecutionResult Rollback(
        string code,
        string message,
        AtomicMutationProcessingResult? processing = null) =>
        new(
            RecordMutationExecutionOutcome.RollbackConfirmed,
            processing ?? FailedProcessing(code, message),
            Error(code, message));

    private static RecordMutationExecutionResult CancelledRollback(
        string message,
        AtomicMutationProcessingResult? processing = null) =>
        new(
            RecordMutationExecutionOutcome.CancelledRollbackConfirmed,
            processing ?? FailedProcessing(BaseMutationErrorCodes.TransactionTimeout, message),
            Error(BaseMutationErrorCodes.TransactionTimeout, message));

    private static AtomicMutationProcessingResult FailedProcessing(string code, string message) =>
        new(
            AtomicMutationProcessingOutcome.Failed,
            [],
            Error(code, message));

    private static BaseError Error(string code, string message) => new()
    {
        Code = code,
        Message = message,
        Category = ErrorCategory.Store,
        Store = new StoreErrorInfo { Retryable = false }
    };

    private static OperationResult<T>? MutationModeFailure<T>(
        CollectionDefinition collection,
        BaseOperationKind operation)
    {
        bool allowed = operation switch
        {
            BaseOperationKind.Create => collection.MutationMode is
                BaseCollectionMutationMode.Mutable or
                BaseCollectionMutationMode.AppendOnly or
                BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge,
            BaseOperationKind.Patch or BaseOperationKind.Replace =>
                collection.MutationMode == BaseCollectionMutationMode.Mutable,
            BaseOperationKind.Delete =>
                collection.MutationMode == BaseCollectionMutationMode.Mutable,
            BaseOperationKind.Purge =>
                collection.MutationMode == BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge,
            _ => false
        };
        if (allowed) return null;
        string code = !Enum.IsDefined(collection.MutationMode)
            ? BaseCollectionErrorCodes.MutationModeInvalid
            : collection.MutationMode == BaseCollectionMutationMode.ReadOnly
                ? BaseCollectionErrorCodes.ReadOnlyMutationForbidden
                : operation is BaseOperationKind.Patch or BaseOperationKind.Replace
                    ? BaseCollectionErrorCodes.AppendOnlyUpdateForbidden
                    : operation == BaseOperationKind.Delete
                        ? BaseCollectionErrorCodes.AppendOnlyDeleteForbidden
                        : BaseCollectionErrorCodes.PurgeUnsupported;
        return InMemoryResultFactory.Unsupported<T>(code, "The collection mutation mode does not permit this operation.");
    }

    private static void ObserveCompletion(Task task) =>
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private ValueTask<OperationResult<RecordEnvelope>> CreateCoreAsync(
        InMemoryStoreState working,
        CollectionDefinition collection,
        RecordCreateRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (MutationModeFailure<RecordEnvelope>(collection, BaseOperationKind.Create) is { } modeError)
            return ValueTask.FromResult(modeError);

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        var normalizedCreatePayload = NormalizeObjectPayload<RecordEnvelope>(request.Payload);
        if (normalizedCreatePayload.Value is not { } payload)
        {
            return ValueTask.FromResult(normalizedCreatePayload.Result!);
        }

        foreach (var field in payload.Fields ?? [])
        {
            if (InMemoryValidation.ValidateFieldName<RecordEnvelope>(field.Key) is { } fieldError)
            {
                return ValueTask.FromResult(fieldError);
            }
        }

        var id = request.RequestedId ?? new RecordId(NextRecordId(working));
        if (request.RequestedId is not null && !_options.AllowClientRequestedIds)
        {
            return ValueTask.FromResult(InMemoryResultFactory.Unsupported<RecordEnvelope>(
                InMemoryErrorCodes.RequestedIdUnsupported,
                "Client-requested ids are disabled for this InMemory store.",
                id.Value));
        }

        if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
            return ValueTask.FromResult(idError);

        var state = GetOrCreateCollection(working, collection.Id);
        if (state.RecordsById.ContainsKey(id.Value))
            return ValueTask.FromResult(InMemoryResultFactory.DuplicateId<RecordEnvelope>(id.Value));

        var now = Now(context);
        var revision = NextRevision(working);
        var metadata = new RecordMetadata
        {
            CreatedAt = now,
            UpdatedAt = now,
            Revision = revision,
            ETag = ETag(revision),
            StoreId = _options.StoreId
        };
        InMemoryCollectionState collectionState = GetOrCreateCollection(working, collection.Id);
        if (collectionState.NextAppendPosition == long.MaxValue)
            return ValueTask.FromResult(InMemoryResultFactory.StoreError<RecordEnvelope>("base.collection.appendPosition.exhausted", "The collection append position is exhausted."));
        var record = new StoredRecord(collection.Id, id, payload, metadata, ++collectionState.NextAppendPosition);
        state.RecordsById.Add(id.Value, record);
        return ValueTask.FromResult(InMemoryResultFactory.WithRevision(
            OperationResults.Created(RecordCloneHelpers.CloneEnvelope(record)), metadata));
    }

    private ValueTask<OperationResult<DeleteResult>> DeleteCoreAsync(
        InMemoryStoreState working,
        CollectionDefinition collection,
        RecordId id,
        RecordDeleteRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (MutationModeFailure<DeleteResult>(collection, context.Operation) is { } modeError)
            return ValueTask.FromResult(modeError);

        if (InMemoryValidation.ValidateCollectionId<DeleteResult>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<DeleteResult>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        var state = GetCollectionOrNull(working, collection.Id);
        if (state is null || !state.RecordsById.TryGetValue(id.Value, out var current))
            return ValueTask.FromResult(InMemoryResultFactory.NotFound<DeleteResult>(id.Value));

        if (request.ExpectedRevision is { } expected && !RevisionEquals(current.Metadata.Revision, expected))
        {
            return ValueTask.FromResult(InMemoryResultFactory.RevisionConflict<DeleteResult>(expected, current.Metadata.Revision, id.Value));
        }

        if (HasRestrictedIncomingReference(working, collection.Id, id.Value))
        {
            return ValueTask.FromResult(OperationResults.Conflict<DeleteResult>(new BaseError
            {
                Code = "base.relation.deleteRestricted",
                Message = "The record cannot be deleted while it is referenced.",
                Category = ErrorCategory.Conflict
            }));
        }

        var previous = request.ReturnPrevious ? RecordCloneHelpers.CloneEnvelope(current) : null;
        state.RecordsById.Remove(id.Value);
        var result = OperationResults.Deleted(new DeleteResult { Id = id, Deleted = true, Previous = previous });
        return ValueTask.FromResult(InMemoryResultFactory.WithRevision(result, current.Metadata));
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<AsyncStream<RecordEnvelope>>> OpenStreamAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        HPDBaseInMemoryTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.StoreStreamOpen,
            BaseOperationKind.RealtimeSubscribe,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => OpenStreamCoreAsync(collection, query, context, cancellationToken));

    private ValueTask<OperationResult<AsyncStream<RecordEnvelope>>> OpenStreamCoreAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.EnableStreamingCapability)
        {
            return ValueTask.FromResult(InMemoryResultFactory.Unsupported<AsyncStream<RecordEnvelope>>(
                InMemoryErrorCodes.UnsupportedQuery,
                "Streaming is disabled for this HPD.BASE InMemory store.",
                collection.Id));
        }

        if (ValidateUnsupportedQuery<RecordEnvelope>(query, allowCount: false) is { } queryError)
        {
            return ValueTask.FromResult(new OperationResult<AsyncStream<RecordEnvelope>>
            {
                Status = queryError.Status,
                Error = queryError.Error,
                Warnings = queryError.Warnings,
                Diagnostics = queryError.Diagnostics,
                Revision = queryError.Revision,
                Events = queryError.Events
            });
        }

        var stream = new AsyncStream<RecordEnvelope>
        {
            Items = StreamItemsAsync(collection, query, context, cancellationToken),
            Descriptor = new AsyncStreamDescriptor
            {
                StreamId = $"{_options.StoreId}:{collection.Id}",
                Backpressure = AsyncStreamBackpressureMode.Wait,
                DeliveryGuarantee = AsyncStreamDeliveryGuarantee.BestEffort
            }
        };

        return ValueTask.FromResult(OperationResults.Ok(stream));
    }

    private async IAsyncEnumerable<RecordEnvelope> StreamItemsAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var queryWithoutCount = query with { Count = QueryCountMode.None };
        var list = await ListAsync(collection, queryWithoutCount, context, cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess() || list.Value is null)
        {
            yield break;
        }

        var yielded = 0;
        foreach (var item in list.Value.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_options.MaxStreamItems is { } maxItems && yielded >= maxItems)
            {
                yield break;
            }

            yielded++;
            yield return RecordCloneHelpers.CloneEnvelope(new StoredRecord(
                item.CollectionId,
                item.Id,
                item.Payload,
                item.Metadata,
                0));
        }
    }

    private ValueTask<OperationResult<RecordEnvelope>> PatchCoreAsync(
        InMemoryStoreState working,
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (MutationModeFailure<RecordEnvelope>(collection, BaseOperationKind.Patch) is { } modeError)
            return ValueTask.FromResult(modeError);

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        if (request.Patch.Kind != RecordPayloadKind.FieldMap)
        {
            return ValueTask.FromResult(InMemoryResultFactory.Unsupported<RecordEnvelope>(
                InMemoryErrorCodes.PatchUnsupportedShape,
                "Portable InMemory patch requires a field-map payload.",
                id.Value));
        }

        if (request.Patch.Fields is null || request.Patch.Fields.Count == 0)
        {
            return ValueTask.FromResult(InMemoryResultFactory.Validation<RecordEnvelope>(
                InMemoryErrorCodes.EmptyPatch,
                "Patch must contain at least one top-level field.",
                id.Value));
        }

        foreach (var field in request.Patch.Fields)
        {
            if (InMemoryValidation.ValidateFieldName<RecordEnvelope>(field.Key) is { } fieldError)
            {
                return ValueTask.FromResult(fieldError);
            }
        }

        var state = GetCollectionOrNull(working, collection.Id);
        if (state is null || !state.RecordsById.TryGetValue(id.Value, out var current))
            return ValueTask.FromResult(InMemoryResultFactory.NotFound<RecordEnvelope>(id.Value));

        if (request.ExpectedRevision is { } expected && !RevisionEquals(current.Metadata.Revision, expected))
        {
            return ValueTask.FromResult(InMemoryResultFactory.RevisionConflict<RecordEnvelope>(expected, current.Metadata.Revision, id.Value));
        }

        var existingFields = ToFieldMap<RecordEnvelope>(current.Payload);
        if (existingFields.Value is not { } fields)
            return ValueTask.FromResult(existingFields.Result!);

        foreach (var field in request.Patch.Fields)
            fields[field.Key] = field.Value.Clone();

        var updatedPayload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
        var updated = MutateRecord(working, current, updatedPayload, context);
        state.RecordsById[id.Value] = updated;
        return ValueTask.FromResult(InMemoryResultFactory.WithRevision(
            OperationResults.Updated(RecordCloneHelpers.CloneEnvelope(updated)), updated.Metadata));
    }

    private ValueTask<OperationResult<RecordEnvelope>> ReplaceCoreAsync(
        InMemoryStoreState working,
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (MutationModeFailure<RecordEnvelope>(collection, BaseOperationKind.Replace) is { } modeError)
            return ValueTask.FromResult(modeError);

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        var normalizedReplacePayload = NormalizeObjectPayload<RecordEnvelope>(request.Payload);
        if (normalizedReplacePayload.Value is not { } payload)
        {
            return ValueTask.FromResult(normalizedReplacePayload.Result!);
        }

        foreach (var field in payload.Fields ?? [])
        {
            if (InMemoryValidation.ValidateFieldName<RecordEnvelope>(field.Key) is { } fieldError)
            {
                return ValueTask.FromResult(fieldError);
            }
        }

        var state = GetCollectionOrNull(working, collection.Id);
        if (state is null || !state.RecordsById.TryGetValue(id.Value, out var current))
            return ValueTask.FromResult(InMemoryResultFactory.NotFound<RecordEnvelope>(id.Value));

        if (request.ExpectedRevision is { } expected && !RevisionEquals(current.Metadata.Revision, expected))
        {
            return ValueTask.FromResult(InMemoryResultFactory.RevisionConflict<RecordEnvelope>(expected, current.Metadata.Revision, id.Value));
        }

        var updated = MutateRecord(working, current, payload, context);
        state.RecordsById[id.Value] = updated;
        return ValueTask.FromResult(InMemoryResultFactory.WithRevision(
            OperationResults.Updated(RecordCloneHelpers.CloneEnvelope(updated)), updated.Metadata));
    }

    private StoredRecord MutateRecord(
        InMemoryStoreState working,
        StoredRecord current,
        RecordPayload payload,
        OperationContext context)
    {
        var revision = NextRevision(working);
        var metadata = current.Metadata with
        {
            UpdatedAt = Now(context),
            Revision = revision,
            ETag = ETag(revision)
        };
        return current with
        {
            Payload = RecordCloneHelpers.ClonePayload(payload),
            Metadata = metadata
        };
    }

    private static InMemoryCollectionState GetOrCreateCollection(InMemoryStoreState state, string collectionId)
    {
        if (state.Collections.TryGetValue(collectionId, out var collection))
        {
            return collection;
        }

        collection = new InMemoryCollectionState();
        state.Collections.Add(collectionId, collection);
        return collection;
    }

    private static InMemoryCollectionState? GetCollectionOrNull(InMemoryStoreState state, string collectionId) =>
        state.Collections.GetValueOrDefault(collectionId);

    private bool HasRestrictedIncomingReference(InMemoryStoreState state, string targetCollectionId, string targetRecordId)
    {
        foreach (CollectionDefinition source in _options.Collections ?? [])
        {
            InMemoryCollectionState? sourceState = GetCollectionOrNull(state, source.Id);
            if (sourceState is null) continue;
            foreach (FieldDefinition field in source.Fields ?? [])
            {
                if (field.Relation is not { OwningSide: BaseRelationOwningSide.Source, DeleteBehavior: BaseRelationDeleteBehavior.Restrict } relation ||
                    !string.Equals(relation.TargetCollectionId, targetCollectionId, StringComparison.Ordinal)) continue;
                foreach (StoredRecord record in sourceState.RecordsById.Values)
                    if (RelationContains(record.Payload, field.Name, targetRecordId)) return true;
            }
        }
        return false;
    }

    private static bool RelationContains(RecordPayload payload, string fieldName, string targetRecordId)
    {
        if (payload.Fields?.TryGetValue(fieldName, out JsonElement value) != true) return false;
        return value.ValueKind == JsonValueKind.String
            ? string.Equals(value.GetString(), targetRecordId, StringComparison.Ordinal)
            : value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), targetRecordId, StringComparison.Ordinal));
    }

    private static string NextRecordId(InMemoryStoreState state) => $"mem:{++state.NextRecordId:x16}";

    private static RevisionToken NextRevision(InMemoryStoreState state) => new($"mem:{++state.NextRevision:x16}");

    private static string ETag(RevisionToken revision) => $"\"{revision.Value}\"";

    private static DateTimeOffset Now(OperationContext context) =>
        context.Now == default ? DateTimeOffset.UtcNow : context.Now;

    private static bool RevisionEquals(RevisionToken? left, RevisionToken right) =>
        left is { } current && string.Equals(current.Value, right.Value, StringComparison.Ordinal);

    private static PayloadNormalizeResult<T> NormalizeObjectPayload<T>(RecordPayload payload)
    {
        if (payload is null)
        {
            return PayloadNormalizeResult<T>.Failure(InMemoryResultFactory.Validation<T>(
                InMemoryErrorCodes.PayloadRequired,
                "A record payload is required."));
        }

        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            return PayloadNormalizeResult<T>.Success(new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = RecordCloneHelpers.CloneFields(payload.Fields)
            });
        }

        if (payload.Json.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return PayloadNormalizeResult<T>.Failure(InMemoryResultFactory.Validation<T>(
                InMemoryErrorCodes.ObjectPayloadRequired,
                "JSON record payloads must be objects."));
        }

        var fields = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);
        foreach (var property in payload.Json.EnumerateObject())
        {
            fields[property.Name] = property.Value.Clone();
        }

        return PayloadNormalizeResult<T>.Success(new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = fields
        });
    }

    private static PayloadFieldsResult<T> ToFieldMap<T>(RecordPayload payload)
    {
        var normalized = NormalizeObjectPayload<T>(payload);
        return normalized.Value is null
            ? PayloadFieldsResult<T>.Failure(normalized.Result!)
            : PayloadFieldsResult<T>.Success(RecordCloneHelpers.CloneFields(normalized.Value.Fields));
    }

    private static QueryResult<List<StoredRecord>, T> ApplySort<T>(
        List<StoredRecord> records,
        QuerySort[]? sort)
    {
        if (sort is null || sort.Length == 0)
        {
            return QueryResult<List<StoredRecord>, T>.Success(records);
        }

        foreach (var sortField in sort)
        {
            foreach (var record in records)
            {
                if (TryReadField(record.Payload, sortField.Field, out var sortValue)
                    && sortValue.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    return QueryResult<List<StoredRecord>, T>.Failure(InMemoryResultFactory.Validation<T>(
                        InMemoryErrorCodes.InvalidQuery,
                        "Object and array values cannot be used as sort keys.",
                        sortField.Field));
                }
            }
        }

        records.Sort((left, right) =>
        {
            foreach (var sortField in sort)
            {
                var leftPresent = TryReadField(left.Payload, sortField.Field, out var leftValue);
                var rightPresent = TryReadField(right.Payload, sortField.Field, out var rightValue);
                var compare = CompareSortValues(leftPresent, leftValue, rightPresent, rightValue, sortField.Nulls);
                if (compare != 0)
                {
                    return sortField.Direction == QuerySortDirection.Desc ? -compare : compare;
                }
            }

            return string.Compare(left.Id.Value, right.Id.Value, StringComparison.Ordinal);
        });

        return QueryResult<List<StoredRecord>, T>.Success(records);
    }

    private QueryResult<StoredRecord[], T> ApplyPage<T>(
        List<StoredRecord> snapshot,
        RecordQuery query,
        CollectionDefinition collection,
        OperationContext context,
        InMemoryCollectionState? collectionState,
        out PageInfo pageInfo)
    {
        var page = query.Page;
        pageInfo = new PageInfo();
        var limit = page?.Limit ?? page?.PerPage ?? _options.DefaultPageSize;

        int offset = 0;
        BaseQueryCursorPayload? cursorPayload = null;
        QueryCursorGuarantee guarantee = collection.MutationMode is
            BaseCollectionMutationMode.AppendOnly or
            BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge
                ? QueryCursorGuarantee.StableHistory
                : QueryCursorGuarantee.Seek;
        long appendHighWater = collectionState?.NextAppendPosition ?? 0;
        long purgeGeneration = collectionState?.PurgeGeneration ?? 0;
        switch (page?.Mode)
        {
            case QueryPaginationMode.Offset:
                offset = page.Offset ?? 0;
                break;
            case QueryPaginationMode.Cursor:
                if (page.CursorDirection != QueryCursorDirection.After)
                {
                    return CursorFailure<T>(BaseQueryErrorCodes.CursorDirectionUnsupported,
                        "The requested cursor direction is not supported.");
                }
                if (!string.IsNullOrWhiteSpace(page.Cursor))
                {
                    BaseQueryCursorReadResult decoded = _queryCursors.Unprotect(
                        page.Cursor, query, limit, _options.StoreId, collection.Id, context,
                        restoreEpoch: 0, schemaGeneration: 0, guarantee, purgeGeneration);
                    if (decoded.Status != BaseQueryCursorStatus.Valid)
                        return CursorFailure<T>(CursorErrorCode(decoded.Status), "The query cursor cannot be continued.");
                    cursorPayload = decoded.Payload;
                    appendHighWater = cursorPayload!.AppendHighWater;
                    snapshot = snapshot
                        .Where(record => guarantee != QueryCursorGuarantee.StableHistory || record.AppendPosition <= appendHighWater)
                        .Where(record => CompareToCursor(record, query.Sort, cursorPayload) > 0)
                        .ToList();
                }
                break;
            case QueryPaginationMode.Page:
            default:
                offset = ((page?.Page ?? 1) - 1) * limit;
                break;
        }

        var items = snapshot.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + items.Length;
        bool hasMore = nextOffset < snapshot.Count;
        string? nextCursor = null;
        if (hasMore && items.Length != 0)
        {
            try
            {
                nextCursor = _queryCursors.Protect(new BaseQueryCursorPayload
                {
                    Guarantee = guarantee,
                    Direction = QueryCursorDirection.After,
                    RestoreEpoch = 0,
                    SchemaGeneration = 0,
                    AppendHighWater = appendHighWater,
                    PurgeGeneration = purgeGeneration,
                    Keys = CursorKeys(items[^1], query.Sort),
                    RecordId = items[^1].Id.Value
                }, query, limit, _options.StoreId, collection.Id, context);
            }
            catch (BaseQueryCursorKeyTooLargeException)
            {
                return CursorFailure<T>(BaseQueryErrorCodes.CursorKeyTooLarge,
                    "The query ordering key exceeds the cursor bound.");
            }
        }
        pageInfo = new PageInfo
        {
            Page = page?.Mode is null or QueryPaginationMode.Page ? page?.Page ?? 1 : null,
            PerPage = page?.Mode is null or QueryPaginationMode.Page ? limit : null,
            Offset = page?.Mode == QueryPaginationMode.Offset ? offset : null,
            Limit = page?.Mode == QueryPaginationMode.Offset ? limit : null,
            Cursor = page?.Cursor,
            NextCursor = nextCursor,
            HasMore = hasMore
        };
        return QueryResult<StoredRecord[], T>.Success(items);
    }

    private static QueryResult<StoredRecord[], T> CursorFailure<T>(string code, string message) =>
        QueryResult<StoredRecord[], T>.Failure(InMemoryResultFactory.Validation<T>(code, message));

    private static string CursorErrorCode(BaseQueryCursorStatus status) => status switch
    {
        BaseQueryCursorStatus.ScopeMismatch => BaseQueryErrorCodes.CursorScopeMismatch,
        BaseQueryCursorStatus.QueryMismatch => BaseQueryErrorCodes.CursorQueryMismatch,
        BaseQueryCursorStatus.Expired => BaseQueryErrorCodes.CursorExpired,
        BaseQueryCursorStatus.VersionUnsupported => BaseQueryErrorCodes.CursorVersionUnsupported,
        BaseQueryCursorStatus.SchemaChanged => BaseQueryErrorCodes.CursorSchemaChanged,
        BaseQueryCursorStatus.RestoreInvalidated => BaseQueryErrorCodes.CursorRestoreInvalidated,
        BaseQueryCursorStatus.GuaranteeUnavailable => BaseQueryErrorCodes.CursorGuaranteeUnavailable,
        BaseQueryCursorStatus.DirectionUnsupported => BaseQueryErrorCodes.CursorDirectionUnsupported,
        BaseQueryCursorStatus.KeyTooLarge => BaseQueryErrorCodes.CursorKeyTooLarge,
        _ => BaseQueryErrorCodes.CursorInvalid
    };

    private static BaseQueryCursorKey[] CursorKeys(StoredRecord record, QuerySort[]? sort)
    {
        if (sort is null || sort.Length == 0)
            return [new BaseQueryCursorKey(true, record.AppendPosition.ToString(CultureInfo.InvariantCulture))];
        return sort.Select(item => TryReadField(record.Payload, item.Field, out JsonElement value)
            ? new BaseQueryCursorKey(true, value.GetRawText())
            : new BaseQueryCursorKey(false, "null")).ToArray();
    }

    private static int CompareToCursor(StoredRecord record, QuerySort[]? sort, BaseQueryCursorPayload cursor)
    {
        if (sort is null || sort.Length == 0)
        {
            long value = long.Parse(cursor.Keys[0].Json, CultureInfo.InvariantCulture);
            int append = record.AppendPosition.CompareTo(value);
            return append != 0 ? append : string.Compare(record.Id.Value, cursor.RecordId, StringComparison.Ordinal);
        }
        if (cursor.Keys.Length != sort.Length) return 0;
        for (int index = 0; index < sort.Length; index++)
        {
            bool present = TryReadField(record.Payload, sort[index].Field, out JsonElement current);
            using JsonDocument document = JsonDocument.Parse(cursor.Keys[index].Json);
            int compared = CompareSortValues(present, current, cursor.Keys[index].Present, document.RootElement, sort[index].Nulls);
            if (compared != 0)
                return sort[index].Direction == QuerySortDirection.Desc ? -compared : compared;
        }
        return string.Compare(record.Id.Value, cursor.RecordId, StringComparison.Ordinal);
    }

    private static void AppendFilterShape(StringBuilder builder, FilterExpression? filter)
    {
        if (filter is null)
        {
            builder.Append("filter=null;");
            return;
        }

        builder.Append("filter=(")
            .Append(filter.Kind).Append(',')
            .Append(filter.Field).Append(',')
            .Append(filter.Operator).Append(',');
        AppendQueryValueShape(builder, filter.Value);
        AppendQueryValueArrayShape(builder, filter.Values);
        foreach (var child in filter.Children ?? [])
        {
            AppendFilterShape(builder, child);
        }

        builder.Append(')');
    }

    private static void AppendSortShape(StringBuilder builder, QuerySort[]? sort)
    {
        builder.Append("sort=[");
        foreach (var item in sort ?? [])
        {
            builder.Append(item.Field).Append(',')
                .Append(item.Direction).Append(',')
                .Append(item.Nulls).Append(';');
        }

        builder.Append("];");
    }

    private static void AppendStringArrayShape(StringBuilder builder, string name, string[]? values)
    {
        builder.Append(name).Append("=[");
        foreach (var value in values ?? [])
        {
            builder.Append(value).Append(';');
        }

        builder.Append("];");
    }

    private static void AppendQueryValueArrayShape(StringBuilder builder, QueryValue[]? values)
    {
        builder.Append('[');
        foreach (var value in values ?? [])
        {
            AppendQueryValueShape(builder, value);
        }

        builder.Append(']');
    }

    private static void AppendQueryValueShape(StringBuilder builder, QueryValue? value)
    {
        if (value is null)
        {
            builder.Append("null;");
            return;
        }

        builder.Append(value.Kind).Append(':')
            .Append(value.String).Append(':')
            .Append(value.Boolean?.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Integer?.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Number?.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Decimal).Append(':')
            .Append(value.DateTime?.ToString("O", CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Id).Append(':');
        AppendQueryValueArrayShape(builder, value.Array);
        builder.Append(';');
    }

    private static StoredRecord[] ApplySelect(StoredRecord[] records, string[]? select)
    {
        if (select is null || select.Length == 0)
        {
            return records;
        }

        return records.Select(record =>
        {
            var root = new SelectNode();
            foreach (var fieldPath in select)
            {
                if (TryReadField(record.Payload, fieldPath, out var selectedValue))
                {
                    AddSelectedValue(root, fieldPath, selectedValue);
                }
            }

            return record with
            {
                Payload = new RecordPayload
                {
                    Kind = RecordPayloadKind.FieldMap,
                    Fields = MaterializeSelectedFields(root)
                }
            };
        }).ToArray();
    }

    private static void AddSelectedValue(SelectNode root, string fieldPath, JsonElement value)
    {
        var parts = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var part in parts)
        {
            if (!current.Children.TryGetValue(part, out var child))
            {
                child = new SelectNode();
                current.Children.Add(part, child);
            }

            current = child;
        }

        current.Value = value.Clone();
    }

    private static Dictionary<string, JsonElement> MaterializeSelectedFields(SelectNode root)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var child in root.Children)
        {
            fields[child.Key] = MaterializeSelectedValue(child.Value);
        }

        return fields;
    }

    private static JsonElement MaterializeSelectedValue(SelectNode node)
    {
        if (node.Children.Count == 0 && node.Value is { } value)
        {
            return value.Clone();
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteSelectedObject(writer, node);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void WriteSelectedObject(Utf8JsonWriter writer, SelectNode node)
    {
        writer.WriteStartObject();
        foreach (var child in node.Children)
        {
            writer.WritePropertyName(child.Key);
            if (child.Value.Children.Count == 0 && child.Value.Value is { } value)
            {
                value.WriteTo(writer);
            }
            else
            {
                WriteSelectedObject(writer, child.Value);
            }
        }

        writer.WriteEndObject();
    }

    private OperationResult<T>? ValidateUnsupportedQuery<T>(RecordQuery query, bool allowCount)
    {
        if (query.Include is { Length: > 0 })
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Includes are not supported by HPD.BASE InMemory.");
        }

        if (query.Extensions is { Length: > 0 })
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Query extensions are not supported by HPD.BASE InMemory.");
        }

        if (!allowCount && query.Count != QueryCountMode.None)
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Streaming does not support count modes.");
        }

        if (query.Count is QueryCountMode.Estimated or QueryCountMode.Limited)
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Estimated and limited count modes are not supported by HPD.BASE InMemory.");
        }

        if ((query.Sort?.Length ?? 0) > _options.MaxSortFields)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query contains too many sort fields.");
        }

        if ((query.Select?.Length ?? 0) > _options.MaxSelectFields)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query contains too many selected fields.");
        }

        foreach (var selectedField in query.Select ?? [])
        {
            var segments = selectedField.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0
                || selectedField.StartsWith(".", StringComparison.Ordinal)
                || selectedField.EndsWith(".", StringComparison.Ordinal)
                || selectedField.Contains("..", StringComparison.Ordinal)
                || segments.Any(segment => InMemoryValidation.ValidateFieldName<T>(segment) is not null))
            {
                return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Selected payload fields must be valid field paths.");
            }
        }

        if (query.Page?.Limit is < 0 || query.Page?.PerPage is < 0 || query.Page?.Offset is < 0)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query pagination values must be non-negative.");
        }

        if (query.Page?.Page is <= 0)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Page mode is one-based.");
        }

        if ((query.Page?.Limit ?? query.Page?.PerPage) is { } requestedLimit && requestedLimit > _options.MaxPageSize)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query page size exceeds the InMemory store limit.");
        }

        if (query.Filter is not null)
        {
            var nodeCount = 0;
            if (ValidateFilter<T>(query.Filter, depth: 1, ref nodeCount) is { } filterError)
            {
                return filterError;
            }
        }

        return null;
    }

    private OperationResult<T>? ValidateFilter<T>(FilterExpression filter, int depth, ref int nodeCount)
    {
        nodeCount++;
        if (depth > _options.MaxFilterDepth || nodeCount > _options.MaxFilterNodes)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query filter exceeds InMemory limits.");
        }

        var error = filter.Kind switch
        {
            FilterNodeKind.Extension => InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Filter extensions are not supported by HPD.BASE InMemory."),
            FilterNodeKind.Compare when filter.Operator is FilterOperator.Like or FilterOperator.NotLike => InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Like and not-like filters are not supported by HPD.BASE InMemory."),
            FilterNodeKind.Compare when string.IsNullOrWhiteSpace(filter.Field) || filter.Value is null => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Compare filters require a field and value."),
            FilterNodeKind.In when string.IsNullOrWhiteSpace(filter.Field) || filter.Values is not { Length: > 0 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "In filters require a field and values."),
            FilterNodeKind.In when filter.Values!.Length > _options.MaxInValues => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "In filters contain too many values."),
            FilterNodeKind.Between when string.IsNullOrWhiteSpace(filter.Field) || filter.Values is not { Length: 2 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Between filters require a field and exactly two values."),
            FilterNodeKind.IsNull or FilterNodeKind.IsDefined when string.IsNullOrWhiteSpace(filter.Field) => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Field filter nodes require a field."),
            FilterNodeKind.Not when filter.Children is not { Length: 1 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Not filters require exactly one child."),
            FilterNodeKind.And or FilterNodeKind.Or when filter.Children is not { Length: > 0 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Boolean filters require children."),
            _ => null
        };

        if (error is not null)
        {
            return error;
        }

        foreach (var child in filter.Children ?? [])
        {
            if (ValidateFilter<T>(child, depth + 1, ref nodeCount) is { } childError)
            {
                return childError;
            }
        }

        return null;
    }

    private static bool MatchesFilter(StoredRecord record, FilterExpression filter) =>
        filter.Kind switch
        {
            FilterNodeKind.True => true,
            FilterNodeKind.False => false,
            FilterNodeKind.Not => filter.Children is [{ } child] && !MatchesFilter(record, child),
            FilterNodeKind.And => filter.Children is { Length: > 0 } children && children.All(child => MatchesFilter(record, child)),
            FilterNodeKind.Or => filter.Children is { Length: > 0 } children && children.Any(child => MatchesFilter(record, child)),
            FilterNodeKind.Compare => MatchesCompare(record, filter),
            FilterNodeKind.In => MatchesIn(record, filter),
            FilterNodeKind.Between => MatchesBetween(record, filter),
            FilterNodeKind.IsNull => TryReadField(record.Payload, filter.Field, out var value) && value.ValueKind == JsonValueKind.Null,
            FilterNodeKind.IsDefined => TryReadField(record.Payload, filter.Field, out _),
            _ => false
        };

    private static bool MatchesCompare(StoredRecord record, FilterExpression filter)
    {
        if (!TryReadField(record.Payload, filter.Field, out var fieldValue) || filter.Value is null)
        {
            return false;
        }

        return filter.Operator switch
        {
            FilterOperator.Equal => ValueEquals(fieldValue, filter.Value),
            FilterOperator.NotEqual => !ValueEquals(fieldValue, filter.Value),
            FilterOperator.LessThan => CompareValues(fieldValue, filter.Value) is < 0,
            FilterOperator.LessThanOrEqual => CompareValues(fieldValue, filter.Value) is <= 0,
            FilterOperator.GreaterThan => CompareValues(fieldValue, filter.Value) is > 0,
            FilterOperator.GreaterThanOrEqual => CompareValues(fieldValue, filter.Value) is >= 0,
            FilterOperator.Contains => ContainsValue(fieldValue, filter.Value),
            FilterOperator.NotContains => !ContainsValue(fieldValue, filter.Value),
            FilterOperator.StartsWith => fieldValue.ValueKind == JsonValueKind.String
                && filter.Value.String is { } prefix
                && (fieldValue.GetString() ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal),
            FilterOperator.EndsWith => fieldValue.ValueKind == JsonValueKind.String
                && filter.Value.String is { } suffix
                && (fieldValue.GetString() ?? string.Empty).EndsWith(suffix, StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool MatchesIn(StoredRecord record, FilterExpression filter)
    {
        if (!TryReadField(record.Payload, filter.Field, out var fieldValue) || filter.Values is null)
        {
            return false;
        }

        return fieldValue.ValueKind == JsonValueKind.Array
            ? fieldValue.EnumerateArray().Any(item => filter.Values.Any(queryValue => ValueEquals(item, queryValue)))
            : filter.Values.Any(queryValue => ValueEquals(fieldValue, queryValue));
    }

    private static bool MatchesBetween(StoredRecord record, FilterExpression filter)
    {
        if (!TryReadField(record.Payload, filter.Field, out var fieldValue)
            || filter.Values is not [{ } lower, { } upper])
        {
            return false;
        }

        return CompareValues(fieldValue, lower) is >= 0 && CompareValues(fieldValue, upper) is <= 0;
    }

    private static bool TryReadField(RecordPayload payload, string? fieldPath, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return false;
        }

        var parts = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            if (payload.Fields?.TryGetValue(parts[0], out value) != true)
            {
                return false;
            }
        }
        else
        {
            value = payload.Json;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(parts[0], out value))
            {
                return false;
            }
        }

        for (var index = 1; index < parts.Length; index++)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(parts[index], out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValueEquals(JsonElement fieldValue, QueryValue queryValue)
    {
        if (queryValue.Kind == QueryValueKind.Null)
        {
            return fieldValue.ValueKind == JsonValueKind.Null;
        }

        if (TryDecimal(fieldValue, out var fieldDecimal) && TryDecimal(queryValue, out var queryDecimal))
        {
            return fieldDecimal == queryDecimal;
        }

        if (queryValue.Kind == QueryValueKind.Boolean && fieldValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return fieldValue.GetBoolean() == queryValue.Boolean;
        }

        var fieldString = ScalarString(fieldValue);
        var queryString = ScalarString(queryValue);
        return fieldString is not null
            && queryString is not null
            && string.Equals(fieldString, queryString, StringComparison.Ordinal);
    }

    private static int? CompareValues(JsonElement fieldValue, QueryValue queryValue)
    {
        if (TryDecimal(fieldValue, out var fieldDecimal) && TryDecimal(queryValue, out var queryDecimal))
        {
            return fieldDecimal.CompareTo(queryDecimal);
        }

        if (queryValue.DateTime is { } queryDate
            && fieldValue.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(fieldValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fieldDate))
        {
            return fieldDate.ToUniversalTime().CompareTo(queryDate.ToUniversalTime());
        }

        var fieldString = ScalarString(fieldValue);
        var queryString = ScalarString(queryValue);
        return fieldString is null || queryString is null
            ? null
            : string.Compare(fieldString, queryString, StringComparison.Ordinal);
    }

    private static bool ContainsValue(JsonElement fieldValue, QueryValue queryValue)
    {
        if (fieldValue.ValueKind == JsonValueKind.Array)
        {
            return fieldValue.EnumerateArray().Any(item => ValueEquals(item, queryValue));
        }

        return fieldValue.ValueKind == JsonValueKind.String
            && queryValue.String is { } text
            && (fieldValue.GetString() ?? string.Empty).Contains(text, StringComparison.Ordinal);
    }

    private static int CompareSortValues(
        bool leftPresent,
        JsonElement left,
        bool rightPresent,
        JsonElement right,
        QueryNullOrder nullOrder)
    {
        var leftNull = !leftPresent || left.ValueKind == JsonValueKind.Null;
        var rightNull = !rightPresent || right.ValueKind == JsonValueKind.Null;
        if (leftNull || rightNull)
        {
            if (leftNull && rightNull)
            {
                return 0;
            }

            return nullOrder == QueryNullOrder.First
                ? leftNull ? -1 : 1
                : leftNull ? 1 : -1;
        }

        if (TryDecimal(left, out var leftDecimal) && TryDecimal(right, out var rightDecimal))
        {
            return leftDecimal.CompareTo(rightDecimal);
        }

        return string.Compare(ScalarString(left), ScalarString(right), StringComparison.Ordinal);
    }

    private static bool TryDecimal(JsonElement element, out decimal value)
    {
        value = default;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryDecimal(QueryValue queryValue, out decimal value)
    {
        value = default;
        return queryValue.Kind switch
        {
            QueryValueKind.Integer when queryValue.Integer is { } integer => TryAssign(integer, out value),
            QueryValueKind.Number when queryValue.Number is { } number && double.IsFinite(number) => TryAssign((decimal)number, out value),
            QueryValueKind.Decimal when queryValue.Decimal is { } text => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryAssign(decimal input, out decimal value)
    {
        value = input;
        return true;
    }

    private static string? ScalarString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };

    private static string? ScalarString(QueryValue queryValue) =>
        queryValue.Kind switch
        {
            QueryValueKind.String => queryValue.String,
            QueryValueKind.Id => queryValue.Id,
            QueryValueKind.Integer => queryValue.Integer?.ToString(CultureInfo.InvariantCulture),
            QueryValueKind.Number => queryValue.Number?.ToString(CultureInfo.InvariantCulture),
            QueryValueKind.Decimal => queryValue.Decimal,
            QueryValueKind.Boolean => queryValue.Boolean?.ToString(),
            QueryValueKind.DateTime => queryValue.DateTime?.ToString("O", CultureInfo.InvariantCulture),
            _ => null
        };

    private static void ValidateOptions(HPDBaseInMemoryStoreOptions options)
    {
        ValidateStableId(options.StoreId, nameof(options.StoreId));
        ValidateStableId(options.ModuleId, nameof(options.ModuleId));
        ValidateStableId(options.HealthRefId, nameof(options.HealthRefId));
        ValidateStableId(options.DiagnosticRefId, nameof(options.DiagnosticRefId));
        if (options.DefaultPageSize <= 0 || options.MaxPageSize <= 0 || options.DefaultPageSize > options.MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(options.DefaultPageSize), "Default and maximum page sizes must be positive and ordered.");
        }

        if (options.CollectionIds.Any(id => !InMemoryValidation.IsValidIdText(id)))
        {
            throw new ArgumentException("Collection ids must be non-empty and contain no control characters.", nameof(options.CollectionIds));
        }

        if (options.CollectionIds.Length > 0 && options.Collections is { Length: > 0 } collections)
        {
            var configured = options.CollectionIds.Order(StringComparer.Ordinal).ToArray();
            var contributed = collections.Select(collection => collection.Id).Order(StringComparer.Ordinal).ToArray();
            if (!configured.SequenceEqual(contributed, StringComparer.Ordinal))
            {
                throw new ArgumentException("CollectionIds and Collections must contain the same collection ids when both are configured.", nameof(options.Collections));
            }
        }
    }

    private static void ValidateStableId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Any(char.IsControl))
        {
            throw new ArgumentException("Identifier values must be trimmed and contain no control characters.", parameterName);
        }
    }

    private static StoreCapabilityDescriptor CreateCapabilities(HPDBaseInMemoryStoreOptions options) => new()
    {
        StoreId = options.StoreId,
        StoreKind = BaseStoreKinds.InMemory,
        StoreVersion = options.StoreVersion,
        Read = new RecordReadCapability
        {
            List = true,
            Get = true,
            MaxPageSize = options.MaxPageSize
        },
        Mutation = new RecordMutationCapability
        {
            Create = true,
            Patch = true,
            Replace = true,
            Delete = true,
            IdAuthority = options.AllowClientRequestedIds ? IdAuthority.Hybrid : IdAuthority.Store,
            TimestampAuthority = TimestampAuthority.Store,
            Consistency = ConsistencyModel.Strong
        },
        Query = new QueryCapability
        {
            Filter = new FilterCapability
            {
                Supported = true,
                Operators =
                [
                    FilterOperator.Equal,
                    FilterOperator.NotEqual,
                    FilterOperator.LessThan,
                    FilterOperator.LessThanOrEqual,
                    FilterOperator.GreaterThan,
                    FilterOperator.GreaterThanOrEqual,
                    FilterOperator.Contains,
                    FilterOperator.NotContains,
                    FilterOperator.StartsWith,
                    FilterOperator.EndsWith
                ],
                BooleanComposition = true,
                Not = true,
                NullChecks = true,
                MissingFieldChecks = true,
                NestedFieldPaths = true,
                ArrayMembership = true,
                MaxDepth = options.MaxFilterDepth,
                MaxNodes = options.MaxFilterNodes,
                MaxSerializedLength = options.MaxSerializedQueryLength,
                ExecutionMode = QueryExecutionMode.Native
            },
            Sort = new SortCapability
            {
                Supported = true,
                MaxFields = options.MaxSortFields,
                NestedFieldPaths = true,
                NullOrdering = true,
                StableTieBreaker = true
            },
            Pagination = new PaginationCapability
            {
                Page = true,
                Offset = true,
                Cursor = QueryCursorGuarantee.StableHistory,
                DefaultLimit = options.DefaultPageSize,
                MaxLimit = options.MaxPageSize,
                CursorRequiresStableSort = true
            },
            Count = new CountCapability
            {
                SupportedModes = [QueryCountMode.None, QueryCountMode.IfAvailable, QueryCountMode.Exact]
            },
            Select = new SelectCapability
            {
                PayloadFields = true,
                NestedFieldPaths = true
            },
            Include = new QueryIncludeCapability { Supported = true, MaxDepth = 3, BackRelations = true, IncludeFilters = true, IncludeSort = true, IncludeLimit = true, ExecutionMode = QueryExecutionMode.Native }
        },
        Revision = new RevisionCapability
        {
            Supported = true,
            Guarantee = RevisionGuarantee.Store,
            Patch = true,
            Replace = true,
            Delete = true
        },
        Batch = new StoreBatchCapability
        {
            Modes = [BaseRecordBatchExecutionMode.Atomic],
            MaxOperations = HPDBaseInMemoryDefaults.MaximumBatchOperations,
            MaxCanonicalPayloadBytes = HPDBaseInMemoryDefaults.MaximumBatchCanonicalPayloadBytes,
            MinimumAcquisitionTimeout = TimeSpan.FromMilliseconds(10),
            MinimumTransactionTimeout = TimeSpan.FromMilliseconds(10),
            MinimumCommitCompletionTimeout = TimeSpan.FromMilliseconds(10),
            TimeoutGranularity = TimeSpan.FromMilliseconds(10),
            Ordered = true,
            PartialResults = false,
            CrossCollectionAtomic = true,
            ReadYourWrites = true,
            Durable = false,
            TransactionalJournal = false,
            Isolation = BaseTransactionIsolation.Serializable,
            NestedTransactions = false,
            Savepoints = false
        },
        Upsert = options.AllowClientRequestedIds
            ? new StoreUpsertCapability
            {
                Atomic = true,
                UpdateModes =
                [
                    RecordUpsertUpdateMode.Patch,
                    RecordUpsertUpdateMode.Replace
                ],
                ExpectedRevision = true,
                ExistenceConditions = true
            }
            : null,
        AtomicRequest = new AtomicRequestCapability
        {
            Supported = true,
            Durability = BaseAtomicRequestDurability.ProcessLocal,
            DuplicateResultReplay = true,
            FingerprintConflictDetection = true,
            IndeterminateResolution = false,
            MaxIdentityBytes = 512,
            MaxReceiptBytes = 16_777_216,
            MinReceiptLifetime = TimeSpan.FromHours(1),
            MaxReceiptLifetime = TimeSpan.FromDays(90),
        },
        Streaming = new StreamingCapability
        {
            Supported = options.EnableStreamingCapability,
            MaxItems = options.MaxStreamItems,
            RequiresStableSort = true
        }
    };

    private static string CollectionIdForTelemetry(CollectionDefinition? collection) => collection?.Id ?? string.Empty;

    private sealed class AtomicSession : IAtomicRecordSession
    {
        private const int Active = 0;
        private const int Closing = 1;
        private const int Closed = 2;

        private readonly InMemoryRecordStore _owner;
        private readonly InMemoryStoreState _working;
        private readonly SemaphoreSlim _operationGate = new(1, 1);
        private int _lifetimeState;

        /// <summary>Initializes a new instance.</summary>
        public AtomicSession(InMemoryRecordStore owner, InMemoryStoreState working)
        {
            _owner = owner;
            _working = working;
        }

        /// <summary>Executes the get async operation.</summary>
        public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
            CollectionDefinition collection,
            RecordId id,
            OperationContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                cancellationToken,
                token => HPDBaseInMemoryTelemetry.TraceAsync(
                    HPDBaseTelemetrySpans.StoreGet,
                    BaseOperationKind.Get,
                    _owner._options.StoreId,
                    CollectionIdForTelemetry(collection),
                    () => GetFromStateAsync(
                        _working,
                        collection,
                        id,
                        context,
                        token)));

        /// <summary>Executes the create async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> CreateAsync(
            CollectionDefinition collection,
            RecordCreateRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                cancellationToken,
                async token =>
                {
                    ArgumentNullException.ThrowIfNull(context);
                    var result = await HPDBaseInMemoryTelemetry.TraceAsync(
                        HPDBaseTelemetrySpans.StoreCreate,
                        BaseOperationKind.Create,
                        _owner._options.StoreId,
                        CollectionIdForTelemetry(collection),
                        () => _owner.CreateCoreAsync(
                            _working,
                            collection,
                            request,
                            context.Operation,
                            token)).ConfigureAwait(false);
                    return ProjectMutation(
                        result,
                        collection,
                        context,
                        BaseCommittedRecordMutationKind.Create,
                        before: null,
                        after: result.Value,
                        delete: null,
                        changedFields: PayloadFieldNames(request.Payload));
                });

        /// <summary>Executes the patch async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> PatchAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordPatchRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                cancellationToken,
                async token =>
                {
                    ArgumentNullException.ThrowIfNull(context);
                    var before = SnapshotRecord(collection, id);
                    var result = await HPDBaseInMemoryTelemetry.TraceAsync(
                        HPDBaseTelemetrySpans.StorePatch,
                        BaseOperationKind.Patch,
                        _owner._options.StoreId,
                        CollectionIdForTelemetry(collection),
                        () => _owner.PatchCoreAsync(
                            _working,
                            collection,
                            id,
                            request,
                            context.Operation,
                            token)).ConfigureAwait(false);
                    return ProjectMutation(
                        result,
                        collection,
                        context,
                        BaseCommittedRecordMutationKind.Patch,
                        before,
                        result.Value,
                        delete: null,
                        changedFields: PayloadFieldNames(request.Patch));
                });

        /// <summary>Executes the replace async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> ReplaceAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordReplaceRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                cancellationToken,
                async token =>
                {
                    ArgumentNullException.ThrowIfNull(context);
                    var before = SnapshotRecord(collection, id);
                    var result = await HPDBaseInMemoryTelemetry.TraceAsync(
                        HPDBaseTelemetrySpans.StoreReplace,
                        BaseOperationKind.Replace,
                        _owner._options.StoreId,
                        CollectionIdForTelemetry(collection),
                        () => _owner.ReplaceCoreAsync(
                            _working,
                            collection,
                            id,
                            request,
                            context.Operation,
                            token)).ConfigureAwait(false);
                    return ProjectMutation(
                        result,
                        collection,
                        context,
                        BaseCommittedRecordMutationKind.Replace,
                        before,
                        result.Value,
                        delete: null,
                        changedFields: PayloadFieldNames(request.Payload));
                });

        /// <summary>Executes the delete async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> DeleteAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordDeleteRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                cancellationToken,
                async token =>
                {
                    ArgumentNullException.ThrowIfNull(context);
                    var before = SnapshotRecord(collection, id);
                    var result = await HPDBaseInMemoryTelemetry.TraceAsync(
                        HPDBaseTelemetrySpans.StoreDelete,
                        BaseOperationKind.Delete,
                        _owner._options.StoreId,
                        CollectionIdForTelemetry(collection),
                        () => _owner.DeleteCoreAsync(
                            _working,
                            collection,
                            id,
                            request,
                            context.Operation,
                            token)).ConfigureAwait(false);
                    return ProjectMutation(
                        result,
                        collection,
                        context,
                        BaseCommittedRecordMutationKind.Delete,
                        before,
                        after: null,
                        delete: result.Value,
                        changedFields: null);
                });

        public ValueTask<OperationResult<long>> AdvancePurgeGenerationAsync(
            CollectionDefinition collection,
            long? expectedGeneration,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(cancellationToken, _ =>
            {
                ArgumentNullException.ThrowIfNull(collection);
                if (collection.MutationMode != BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)
                    return ValueTask.FromResult(InMemoryResultFactory.Unsupported<long>(
                        BaseCollectionErrorCodes.PurgeUnsupported,
                        "The collection does not support administrative purge."));
                InMemoryCollectionState state = GetOrCreateCollection(_working, collection.Id);
                if (expectedGeneration is { } expected && expected != state.PurgeGeneration)
                    return ValueTask.FromResult(OperationResults.Conflict<long>(new BaseError
                    {
                        Code = BaseCollectionErrorCodes.PurgeGenerationConflict,
                        Message = "The purge generation did not match.",
                        Category = ErrorCategory.Conflict
                    }));
                if (state.PurgeGeneration == long.MaxValue)
                    return ValueTask.FromResult(InMemoryResultFactory.StoreError<long>(
                        BaseCollectionErrorCodes.PurgeFailed,
                        "The purge generation is exhausted."));
                return ValueTask.FromResult(OperationResults.Ok(++state.PurgeGeneration));
            });

        /// <summary>Executes the close async operation.</summary>
        public async ValueTask CloseAsync()
        {
            if (Interlocked.CompareExchange(
                    ref _lifetimeState,
                    Closing,
                    Active) != Active)
            {
                return;
            }

            await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            Volatile.Write(ref _lifetimeState, Closed);
            _operationGate.Release();
        }

        private async ValueTask<OperationResult<T>> ExecuteAsync<T>(
            CancellationToken cancellationToken,
            Func<CancellationToken, ValueTask<OperationResult<T>>> action)
        {
            if (Volatile.Read(ref _lifetimeState) != Active)
                return SessionClosed<T>();

            try
            {
                await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return SessionOperationCancelled<T>();
            }

            try
            {
                if (Volatile.Read(ref _lifetimeState) != Active)
                    return SessionClosed<T>();

                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return SessionOperationCancelled<T>();
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private RecordEnvelope? SnapshotRecord(CollectionDefinition collection, RecordId id)
        {
            var record = GetCollectionOrNull(_working, collection.Id)?
                .RecordsById.GetValueOrDefault(id.Value);
            return record is null ? null : RecordCloneHelpers.CloneEnvelope(record);
        }

        private static OperationResult<RecordMutationSessionResult> ProjectMutation<T>(
            OperationResult<T> result,
            CollectionDefinition collection,
            RecordMutationSessionContext context,
            BaseCommittedRecordMutationKind committedOperation,
            RecordEnvelope? before,
            RecordEnvelope? after,
            DeleteResult? delete,
            string[]? changedFields)
        {
            RecordMutationSessionResult? value = null;
            if (result.Value is not null)
            {
                RecordUpsertOutcome? upsertOutcome =
                    context.RequestedOperation == BaseRecordMutationKind.Upsert
                        ? committedOperation == BaseCommittedRecordMutationKind.Create
                            ? RecordUpsertOutcome.Created
                            : RecordUpsertOutcome.Updated
                        : null;
                var mutation = new BaseRecordMutationFact
                {
                    ItemId = context.ItemId,
                    RequestedOperation = context.RequestedOperation,
                    CommittedOperation = committedOperation,
                    UpsertOutcome = upsertOutcome,
                    Collection = collection,
                    Event = EventReference(
                        context.EventId,
                        committedOperation),
                    Before = before,
                    After = after,
                    Delete = delete,
                    ChangedFields = context.ChangedFields
                };
                value = new RecordMutationSessionResult
                {
                    Mutation = mutation,
                    Record = after,
                    Delete = delete
                };
            }

            return new OperationResult<RecordMutationSessionResult>
            {
                Status = result.Status,
                Value = value,
                Error = result.Error,
                Warnings = result.Warnings,
                Diagnostics = result.Diagnostics,
                Revision = result.Revision
            };
        }

        private static EventReference EventReference(
            string eventId,
            BaseCommittedRecordMutationKind operation) => new()
        {
            EventId = eventId,
            Type = operation switch
            {
                BaseCommittedRecordMutationKind.Create => BaseEventTypes.RecordCreated,
                BaseCommittedRecordMutationKind.Patch => BaseEventTypes.RecordPatched,
                BaseCommittedRecordMutationKind.Replace => BaseEventTypes.RecordUpdated,
                BaseCommittedRecordMutationKind.Delete => BaseEventTypes.RecordDeleted,
                _ => throw new InvalidOperationException("Unsupported committed mutation kind.")
            },
            Guarantee = EventDeliveryGuarantee.BestEffort
        };

        private static string[]? PayloadFieldNames(RecordPayload? payload)
        {
            if (payload is null)
                return null;
            if (payload.Kind == RecordPayloadKind.FieldMap)
                return payload.Fields?.Keys.Order(StringComparer.Ordinal).ToArray();
            if (payload.Json.ValueKind != JsonValueKind.Object)
                return null;
            return payload.Json
                .EnumerateObject()
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        private static OperationResult<T> SessionClosed<T>() =>
            InMemoryResultFactory.StoreError<T>(
                InMemoryErrorCodes.SessionClosed,
                "The InMemory mutation session is no longer active.");

        private static OperationResult<T> SessionOperationCancelled<T>() =>
            InMemoryResultFactory.StoreError<T>(
                InMemoryErrorCodes.SessionOperationCancelled,
                "The InMemory mutation session operation was cancelled.");
    }

    private readonly record struct PayloadNormalizeResult<T>(RecordPayload? Value, OperationResult<T>? Result)
    {
        /// <summary>Executes the success operation.</summary>
        public static PayloadNormalizeResult<T> Success(RecordPayload payload) => new(payload, null);
        /// <summary>Executes the failure operation.</summary>
        public static PayloadNormalizeResult<T> Failure(OperationResult<T> result) => new(null, result);
    }

    private readonly record struct PayloadFieldsResult<T>(Dictionary<string, System.Text.Json.JsonElement>? Value, OperationResult<T>? Result)
    {
        /// <summary>Executes the success operation.</summary>
        public static PayloadFieldsResult<T> Success(Dictionary<string, System.Text.Json.JsonElement> fields) => new(fields, null);
        /// <summary>Executes the failure operation.</summary>
        public static PayloadFieldsResult<T> Failure(OperationResult<T> result) => new(null, result);
    }

    private readonly record struct QueryResult<TValue, TResult>(TValue? Value, OperationResult<TResult>? Result)
        where TValue : class
    {
        /// <summary>Executes the success operation.</summary>
        public static QueryResult<TValue, TResult> Success(TValue value) => new(value, null);
        /// <summary>Executes the failure operation.</summary>
        public static QueryResult<TValue, TResult> Failure(OperationResult<TResult> result) => new(null, result);
    }

    private sealed class SelectNode
    {
        /// <summary>Gets or sets the value.</summary>
        public JsonElement? Value { get; set; }
        /// <summary>Gets the children.</summary>
        public Dictionary<string, SelectNode> Children { get; } = new(StringComparer.Ordinal);
    }
}
