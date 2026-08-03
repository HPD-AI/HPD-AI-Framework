using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace HPD.Base;

internal sealed class DefaultBaseMutationCoordinator(
    IBaseSchemaProvider schema,
    IBaseSchemaValidator schemaValidator,
    IBaseStoreExecutionResolver storeResolver,
    IBasePolicyOrchestrator policy,
    IBaseResultNormalizer normalizer,
    IBaseOperationalFailureMapper failureMapper,
    IBaseMutationPostCommitDispatcher postCommit,
    IBaseDescriptorRegistry descriptors,
    IOptions<HPDBaseRuntimeOptions> options,
    ILogger<DefaultBaseMutationCoordinator> logger) : IBaseMutationCoordinator
{
    private readonly HPDBaseRuntimeMutationOptions _limits = options.Value.Mutations;
    private readonly HPDBaseRuntimeEventOptions _events = options.Value.Events;

    /// <summary>Executes the execute single async operation.</summary>
    public async ValueTask<OperationResult<BaseRecordBatchItemResult>> ExecuteSingleAsync(
        BaseRecordBatchItem item,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        var batch = new BaseRecordBatchRequest
        {
            Mode = BaseRecordBatchExecutionMode.OrderedStopOnFailure,
            Operations = [item]
        };
        var prepared = await PrepareAsync(batch, principal, operation, isPublicBatch: false, cancellationToken)
            .ConfigureAwait(false);
        if (!prepared.IsSuccess() || prepared.Value is null)
            return Failure<BaseRecordBatchItemResult, BaseMutationCommand[]>(prepared);

        var command = prepared.Value[0];
        var execution = await ExecuteBoundaryAsync(
            command.Store,
            [command],
            principal,
            atomicGroup: false,
            cancellationToken).ConfigureAwait(false);
        if (!execution.IsSuccess() || execution.Value is null)
            return Failure<BaseRecordBatchItemResult, BoundaryResult>(execution);

        if (execution.Value.Indeterminate)
            return Indeterminate<BaseRecordBatchItemResult>();

        if (!execution.Value.Committed)
        {
            var failed = FailedItem(command, execution.Value.Failure);
            return new OperationResult<BaseRecordBatchItemResult>
            {
                Status = failed.Status,
                Error = failed.Error
            };
        }

        var committed = await DispatchPostCommitAsync(
            execution.Value.Attempts[0],
            principal).ConfigureAwait(false);
        return new OperationResult<BaseRecordBatchItemResult>
        {
            Status = committed.Status,
            Value = committed,
            Warnings = committed.Warnings,
            Revision = committed.Revision,
            Events = committed.Events
        };
    }

    /// <summary>Executes the execute batch async operation.</summary>
    public async ValueTask<OperationResult<BaseRecordBatchResult>> ExecuteBatchAsync(
        BaseRecordBatchRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.RequestIdentity is not null)
        {
            if (request.Mode != BaseRecordBatchExecutionMode.Atomic)
            {
                return OperationResults.ValidationFailed<BaseRecordBatchResult>(Error(
                    "base.runtime.request.invalid",
                    "Mutation request identity is valid only for an atomic batch.",
                    ErrorCategory.Validation));
            }

        }

        var prepared = await PrepareAsync(request, principal, operation, isPublicBatch: true, cancellationToken)
            .ConfigureAwait(false);
        if (!prepared.IsSuccess() || prepared.Value is null)
            return Failure<BaseRecordBatchResult, BaseMutationCommand[]>(prepared);

        return request.Mode == BaseRecordBatchExecutionMode.Atomic
            ? await ExecuteAtomicBatchAsync(prepared.Value, request.RequestIdentity, principal, cancellationToken).ConfigureAwait(false)
            : await ExecuteOrderedBatchAsync(prepared.Value, principal, request.Mode, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OperationResult<BaseRecordBatchResult>> ExecuteAtomicBatchAsync(
        BaseMutationCommand[] commands,
        BaseMutationRequestIdentity? requestIdentity,
        PrincipalContext principal,
        CancellationToken cancellationToken)
    {
        var first = commands[0].Store;
        if (commands.Any(command =>
                !ReferenceEquals(command.Store.Registration, first.Registration)
                || !ReferenceEquals(command.Store.Store, first.Store)))
        {
            return OperationResults.Unsupported<BaseRecordBatchResult>(Error(
                BaseMutationErrorCodes.BatchMultipleStores,
                "Atomic batch operations must use one exact store registration and instance.",
                ErrorCategory.Unsupported));
        }

        if (first.AtomicStore is null
            || !SupportsMode(first.Store.Capabilities.Batch, BaseRecordBatchExecutionMode.Atomic)
            || first.Store.Capabilities.Batch?.ReadYourWrites != true)
        {
            return OperationResults.Unsupported<BaseRecordBatchResult>(Error(
                BaseMutationErrorCodes.BatchAtomicUnsupported,
                "The selected store does not support atomic batch execution.",
                ErrorCategory.Unsupported));
        }

        var distinctCollections = commands
            .Select(static command => command.CollectionId)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .Count();
        if (distinctCollections > 1 && first.Store.Capabilities.Batch?.CrossCollectionAtomic != true)
        {
            return OperationResults.Unsupported<BaseRecordBatchResult>(Error(
                BaseMutationErrorCodes.BatchCrossCollectionUnsupported,
                "The selected store does not support cross-collection atomic execution.",
                ErrorCategory.Unsupported));
        }

        if (requestIdentity is not null && first.Store.Capabilities.AtomicRequest?.Supported != true)
        {
            return OperationResults.Unsupported<BaseRecordBatchResult>(Error(
                BaseMutationRequestErrorCodes.Unsupported,
                "The selected store does not support identified atomic requests.",
                ErrorCategory.Unsupported));
        }

        BaseAtomicMutationExecutionRequest? atomicRequest = requestIdentity is null ? null : new()
        {
            Identity = requestIdentity,
            StructuralDigest = BaseAtomicStructureDigest.Compute(commands),
            ExpiresAt = DateTimeOffset.UtcNow + _limits.ReceiptLifetime,
            MaxReceiptBytes = _limits.MaxReceiptBytes,
        };

        var execution = await ExecuteBoundaryAsync(first, commands, principal, atomicGroup: true, cancellationToken, atomicRequest)
            .ConfigureAwait(false);
        if (!execution.IsSuccess() || execution.Value is null)
            return Failure<BaseRecordBatchResult, BoundaryResult>(execution);
        if (execution.Value.Indeterminate)
            return Indeterminate<BaseRecordBatchResult>();

        if (execution.Value.Committed)
        {
            var committed = new BaseRecordBatchItemResult[commands.Length];
            for (var index = 0; index < committed.Length; index++)
                committed[index] = execution.Value.RequestDisposition == BaseMutationRequestDisposition.Duplicate
                    ? await postCommit.ReplayAsync(execution.Value.Attempts[index], principal).ConfigureAwait(false)
                    : await DispatchPostCommitAsync(execution.Value.Attempts[index], principal).ConfigureAwait(false);
            return BatchResult(BaseRecordBatchOutcome.Committed, committed, disposition: execution.Value.RequestDisposition);
        }

        if (execution.Value.Failure?.Code == BaseMutationRequestErrorCodes.FingerprintConflict)
            return OperationResults.Conflict<BaseRecordBatchResult>(Error(
                BaseMutationRequestErrorCodes.FingerprintConflict,
                "The mutation request identity conflicts with an existing receipt.",
                ErrorCategory.Conflict));

        var attempts = execution.Value.Attempts;
        if (execution.Value.AggregateFailure)
        {
            var rolledBack = commands.Select(RolledBackItem).ToArray();
            return BatchResult(
                BaseRecordBatchOutcome.RolledBack,
                rolledBack,
                execution.Value.Failure);
        }

        var items = new BaseRecordBatchItemResult[commands.Length];
        var failingIndex = attempts.Count == 0
            ? 0
            : Math.Min(attempts.Count - 1, commands.Length - 1);
        for (var index = 0; index < items.Length; index++)
        {
            if (index < failingIndex)
                items[index] = RolledBackItem(commands[index]);
            else if (index == failingIndex)
                items[index] = FailedItem(commands[index], execution.Value.Failure);
            else
                items[index] = SkippedItem(commands[index]);
        }

        return BatchResult(BaseRecordBatchOutcome.RolledBack, items);
    }

    private async ValueTask<OperationResult<BaseRecordBatchResult>> ExecuteOrderedBatchAsync(
        BaseMutationCommand[] commands,
        PrincipalContext principal,
        BaseRecordBatchExecutionMode mode,
        CancellationToken cancellationToken)
    {
        var items = new BaseRecordBatchItemResult[commands.Length];
        var committedCount = 0;
        var failureCount = 0;
        var stop = false;
        for (var index = 0; index < commands.Length; index++)
        {
            if (stop)
            {
                items[index] = SkippedItem(commands[index]);
                continue;
            }

            var command = commands[index];
            var execution = await ExecuteBoundaryAsync(
                command.Store,
                [command],
                principal,
                atomicGroup: false,
                cancellationToken).ConfigureAwait(false);
            if (!execution.IsSuccess() || execution.Value is null)
                return Failure<BaseRecordBatchResult, BoundaryResult>(execution);
            if (execution.Value.Indeterminate)
                return Indeterminate<BaseRecordBatchResult>();

            if (execution.Value.Committed)
            {
                items[index] = await DispatchPostCommitAsync(
                    execution.Value.Attempts[0],
                    principal).ConfigureAwait(false);
                committedCount++;
                continue;
            }

            items[index] = FailedItem(command, execution.Value.Failure);
            failureCount++;
            stop = mode == BaseRecordBatchExecutionMode.OrderedStopOnFailure;
        }

        var outcome = failureCount == 0
            ? BaseRecordBatchOutcome.Committed
            : committedCount > 0
                ? BaseRecordBatchOutcome.PartiallyCommitted
                : BaseRecordBatchOutcome.Failed;
        return BatchResult(outcome, items);
    }

    private async ValueTask<OperationResult<BoundaryResult>> ExecuteBoundaryAsync(
        BaseResolvedMutationStore store,
        BaseMutationCommand[] commands,
        PrincipalContext principal,
        bool atomicGroup,
        CancellationToken cancellationToken,
        BaseAtomicMutationExecutionRequest? atomicRequest = null)
    {
        var processor = new DefaultBaseMutationProcessor(commands, principal, policy, normalizer, descriptors.Current.Schema.Collections ?? [], _limits.MaxTransactionDuration);
        var request = new RecordMutationExecutionRequest
        {
            AcquisitionTimeout = _limits.StoreAcquisitionTimeout,
            TransactionTimeout = _limits.MaxTransactionDuration,
            CommitCompletionTimeout = _limits.CommitCompletionTimeout,
            AtomicRequest = atomicRequest,
        };

        RecordMutationExecutionResult result;
        using var activity = HPDBaseRuntimeTelemetry.StartStoreInvocation(commands[0].Context);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            result = atomicGroup
                ? await store.AtomicStore!.ExecuteAtomicAsync(processor, request, cancellationToken).ConfigureAwait(false)
                : await store.Store.ExecuteSingleAsync(processor, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (failureMapper.TryMap(
            exception,
            commands[0].Context,
            out var mappedError,
            out var mappedStatus))
        {
            var safeMapped = Safe(mappedError);
            FinishStoreTelemetry(
                activity,
                commands[0].Context,
                startedAt,
                mappedStatus,
                safeMapped);
            LogStoreFailure(safeMapped, commands[0].Context);
            return new OperationResult<BoundaryResult>
            {
                Status = mappedStatus,
                Error = safeMapped
            };
        }

        var attempts = processor.Attempts;
        FinishStoreTelemetry(
            activity,
            commands[0].Context,
            startedAt,
            StatusForExecution(result),
            Safe(result.Error));
        switch (result.Outcome)
        {
            case RecordMutationExecutionOutcome.Committed:
                if (attempts.Count != commands.Length
                    || attempts.Any(static attempt => !attempt.Status.IsSuccess() || attempt.Mutation is null)
                    || result.Processing?.Mutations.Length != commands.Length)
                {
                    return OperationResults.StoreError<BoundaryResult>(Error(
                        "base.runtime.store.malformedMutationResult",
                        "The store returned an inconsistent committed mutation result.",
                        ErrorCategory.Store));
                }

                return OperationResults.Ok(new BoundaryResult(true, false, false, attempts, null)
                {
                    RequestDisposition = result.RequestDisposition,
                });

            case RecordMutationExecutionOutcome.Indeterminate:
                return OperationResults.Ok(new BoundaryResult(false, true, false, [], null));

            case RecordMutationExecutionOutcome.CancelledRollbackConfirmed:
                cancellationToken.ThrowIfCancellationRequested();
                return OperationResults.Ok(new BoundaryResult(
                    false,
                    false,
                    AllAttemptsSucceeded(attempts, commands.Length),
                    attempts,
                    Error(
                        BaseMutationErrorCodes.TransactionTimeout,
                        "The mutation transaction exceeded its bounded lifetime and rollback was confirmed.",
                        ErrorCategory.Store)));

            case RecordMutationExecutionOutcome.ConflictRollbackConfirmed:
                return OperationResults.Ok(new BoundaryResult(
                    false,
                    false,
                    AllAttemptsSucceeded(attempts, commands.Length),
                    attempts,
                    Error(
                        BaseMutationErrorCodes.TransactionConflict,
                        "The mutation transaction conflicted and was rolled back.",
                        ErrorCategory.Conflict)));

            case RecordMutationExecutionOutcome.RollbackConfirmed:
                return OperationResults.Ok(new BoundaryResult(
                    false,
                    false,
                    AllAttemptsSucceeded(attempts, commands.Length),
                    attempts,
                    attempts.LastOrDefault(static attempt => !attempt.Status.IsSuccess()) is { Error: { } attemptError } failedAttempt
                        ? failedAttempt.ProviderError ? Safe(attemptError) : attemptError
                        : Safe(result.Error) ?? Error(
                            BaseMutationErrorCodes.BatchRolledBack,
                            "The mutation transaction was rolled back.",
                            ErrorCategory.Store)));

            default:
                return OperationResults.StoreError<BoundaryResult>(Error(
                    "base.runtime.store.malformedMutationResult",
                    "The store returned an unknown mutation outcome.",
                    ErrorCategory.Store));
        }
    }

    private async ValueTask<OperationResult<BaseMutationCommand[]>> PrepareAsync(
        BaseRecordBatchRequest request,
        PrincipalContext principal,
        OperationContext aggregateOperation,
        bool isPublicBatch,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Mode))
            return Validation<BaseMutationCommand[]>(BaseMutationErrorCodes.BatchInvalid, "The batch execution mode is invalid.");
        if (request.Operations is not { Length: > 0 })
            return Validation<BaseMutationCommand[]>(BaseMutationErrorCodes.BatchInvalid, "A batch requires at least one operation.");
        if (request.Operations.Length > _limits.MaxOperations)
            return Validation<BaseMutationCommand[]>(BaseMutationErrorCodes.BatchOperationLimitExceeded, "The batch operation limit was exceeded.");

        int payloadBytes;
        try
        {
            payloadBytes = JsonSerializer.SerializeToUtf8Bytes(
                request,
                HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest).Length;
        }
        catch (JsonException)
        {
            return Validation<BaseMutationCommand[]>(BaseMutationErrorCodes.BatchInvalid, "The batch payload is not canonically serializable.");
        }

        if (payloadBytes > _limits.MaxCanonicalPayloadBytes)
            return Validation<BaseMutationCommand[]>(BaseMutationErrorCodes.BatchPayloadLimitExceeded, "The canonical batch payload limit was exceeded.");

        var timestamp = aggregateOperation.Now == default ? DateTimeOffset.UtcNow : aggregateOperation.Now;
        var aggregateCorrelation = string.IsNullOrWhiteSpace(aggregateOperation.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : aggregateOperation.CorrelationId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var commands = new BaseMutationCommand[request.Operations.Length];
        for (var index = 0; index < request.Operations.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = request.Operations[index];
            if (item is null
                || string.IsNullOrWhiteSpace(item.ItemId)
                || item.ItemId.Length > _limits.MaxItemIdLength
                || item.ItemId.Any(char.IsControl))
            {
                return Validation<BaseMutationCommand[]>(BaseMutationErrorCodes.BatchItemInvalid, "A batch item identifier is invalid.");
            }

            if (!seen.Add(item.ItemId))
                return Validation<BaseMutationCommand[]>(BaseMutationErrorCodes.BatchDuplicateItem, "Batch item identifiers must be unique.");
            if (string.IsNullOrWhiteSpace(item.CollectionId) || !ValidUnion(item))
                return Validation<BaseMutationCommand[]>(BaseMutationErrorCodes.BatchItemInvalid, "A batch item is invalid.");

            var kind = ToOperation(item.Kind);
            var recordId = TargetId(item);
            if (recordId is { } id && string.IsNullOrWhiteSpace(id.Value))
                return Validation<BaseMutationCommand[]>(BaseMutationErrorCodes.BatchItemInvalid, "A record identifier is invalid.");

            var context = aggregateOperation with
            {
                Operation = kind,
                CollectionId = item.CollectionId,
                RecordId = recordId?.Value,
                CorrelationId = $"{aggregateCorrelation}:{index}",
                Now = timestamp
            };
            var collectionResult = await schema.GetCollectionAsync(
                item.CollectionId,
                principal,
                context,
                VisibilityLevel.Internal,
                cancellationToken).ConfigureAwait(false);
            if (!collectionResult.IsSuccess() || collectionResult.Value is null)
                return Failure<BaseMutationCommand[], CollectionDefinition>(collectionResult);

            var collection = collectionResult.Value;
            if (!collection.Enabled
                || !collection.Exposed
                || !CollectionAllows(collection, item))
            {
                return OperationResults.Unsupported<BaseMutationCommand[]>(Error(
                    "base.runtime.collection.operationDisabled",
                    "The collection does not allow the requested mutation.",
                    ErrorCategory.Unsupported));
            }

            var storeResult = storeResolver.Resolve(collection, item.Kind, context);
            if (!storeResult.IsSuccess() || storeResult.Value is null)
                return Failure<BaseMutationCommand[], BaseResolvedMutationStore>(storeResult);
            var resolvedStore = storeResult.Value;

            if (_events.PublishFailureMode == BaseEventPublishFailureMode.RequireEnqueue
                && resolvedStore.Store is not ITransactionalMutationJournalStore)
            {
                return OperationResults.CapabilityUnavailable<BaseMutationCommand[]>(Error(
                    "base.runtime.events.transactionalJournalRequired",
                    "The selected record store does not support transactional mutation journaling.",
                    ErrorCategory.Capability));
            }

            if (isPublicBatch
                && request.Mode == BaseRecordBatchExecutionMode.Atomic
                && !SupportsMode(resolvedStore.Store.Capabilities.Batch, request.Mode))
            {
                return OperationResults.Unsupported<BaseMutationCommand[]>(Error(
                    BaseMutationErrorCodes.BatchModeUnsupported,
                    "The selected store does not support the requested batch mode.",
                    ErrorCategory.Unsupported));
            }

            var providerBatch = resolvedStore.Store.Capabilities.Batch;
            if (providerBatch is not null
                && (_limits.StoreAcquisitionTimeout < providerBatch.MinimumAcquisitionTimeout
                    || _limits.MaxTransactionDuration < providerBatch.MinimumTransactionTimeout
                    || _limits.CommitCompletionTimeout < providerBatch.MinimumCommitCompletionTimeout))
            {
                var code = item.Kind == BaseRecordMutationKind.Upsert
                    ? BaseMutationErrorCodes.UpsertUnsupported
                    : isPublicBatch && request.Mode == BaseRecordBatchExecutionMode.Atomic
                        ? BaseMutationErrorCodes.BatchModeUnsupported
                        : "base.runtime.store.operationUnsupported";
                return OperationResults.CapabilityUnavailable<BaseMutationCommand[]>(Error(
                    code,
                    "The configured mutation lifetimes are below the selected provider minimum.",
                    ErrorCategory.Capability));
            }

            if (isPublicBatch
                && request.Mode == BaseRecordBatchExecutionMode.Atomic
                && providerBatch is not null
                && (request.Operations.Length > providerBatch.MaxOperations
                    || payloadBytes > providerBatch.MaxCanonicalPayloadBytes))
            {
                return Validation<BaseMutationCommand[]>(
                    request.Operations.Length > providerBatch.MaxOperations
                        ? BaseMutationErrorCodes.BatchOperationLimitExceeded
                        : BaseMutationErrorCodes.BatchPayloadLimitExceeded,
                    "A provider batch limit was exceeded.");
            }

            var commandResult = await PrepareCommandAsync(
                item,
                index,
                collection,
                resolvedStore,
                context,
                principal,
                cancellationToken).ConfigureAwait(false);
            if (!commandResult.IsSuccess() || commandResult.Value is null)
                return Failure<BaseMutationCommand[], BaseMutationCommand>(commandResult);
            commands[index] = commandResult.Value;
        }

        return OperationResults.Ok(commands);
    }

    private async ValueTask<OperationResult<BaseMutationCommand>> PrepareCommandAsync(
        BaseRecordBatchItem item,
        int index,
        CollectionDefinition collection,
        BaseResolvedMutationStore store,
        OperationContext context,
        PrincipalContext principal,
        CancellationToken cancellationToken)
    {
        var mutation = store.Store.Capabilities.Mutation;
        if (item.Create is { } create)
        {
            if (create.RequestedId is { } requestedId
                && (string.IsNullOrWhiteSpace(requestedId.Value)
                    || mutation.IdAuthority is not (IdAuthority.Client or IdAuthority.Hybrid)))
            {
                return OperationResults.Unsupported<BaseMutationCommand>(Error(
                    "base.runtime.create.requestedIdUnsupported",
                    "The selected store does not support caller-supplied record identifiers.",
                    ErrorCategory.Unsupported));
            }
        }

        if (!RevisionSupported(store.Store.Capabilities.Revision, item))
        {
            return OperationResults.Unsupported<BaseMutationCommand>(Error(
                "base.runtime.revision.unsupported",
                "The selected store does not support the requested revision precondition.",
                ErrorCategory.Unsupported));
        }

        if (item.Upsert is { } upsert)
        {
            var capability = store.Store.Capabilities.Upsert;
            if (store.AtomicStore is null
                || capability?.Atomic != true
                || !capability.UpdateModes.Contains(upsert.UpdateMode)
                || upsert.ExpectedRevision is not null && !capability.ExpectedRevision
                || upsert.Condition != RecordUpsertExistenceCondition.Any && !capability.ExistenceConditions
                || mutation.IdAuthority is not (IdAuthority.Client or IdAuthority.Hybrid))
            {
                return OperationResults.Unsupported<BaseMutationCommand>(Error(
                    BaseMutationErrorCodes.UpsertUnsupported,
                    "The selected store does not support the requested atomic upsert.",
                    ErrorCategory.Unsupported));
            }
        }

        BaseValidatedPayload? createPayload = null;
        BaseValidatedPayload? updatePayload = null;
        if (item.Create is { } createRequest)
        {
            var validation = await schemaValidator.ValidateCreateAsync(new BasePayloadValidationRequest
            {
                Collection = collection,
                Principal = principal,
                Operation = context,
                Payload = createRequest.Payload
            }, cancellationToken).ConfigureAwait(false);
            if (!validation.IsSuccess() || validation.Value is null)
                return Failure<BaseMutationCommand, BaseValidatedPayload>(validation);
            createPayload = validation.Value;
        }
        else if (item.Patch is { } patchRequest)
        {
            var validation = await schemaValidator.ValidatePatchAsync(new BasePayloadValidationRequest
            {
                Collection = collection,
                Principal = principal,
                Operation = context,
                Patch = patchRequest.Patch
            }, cancellationToken).ConfigureAwait(false);
            if (!validation.IsSuccess() || validation.Value is null)
                return Failure<BaseMutationCommand, BaseValidatedPayload>(validation);
            updatePayload = validation.Value;
        }
        else if (item.Replace is { } replaceRequest)
        {
            var validation = await schemaValidator.ValidateReplaceAsync(new BasePayloadValidationRequest
            {
                Collection = collection,
                Principal = principal,
                Operation = context,
                Payload = replaceRequest.Payload
            }, cancellationToken).ConfigureAwait(false);
            if (!validation.IsSuccess() || validation.Value is null)
                return Failure<BaseMutationCommand, BaseValidatedPayload>(validation);
            updatePayload = validation.Value;
        }
        else if (item.Upsert is { } upsertRequest)
        {
            var createValidation = await schemaValidator.ValidateCreateAsync(new BasePayloadValidationRequest
            {
                Collection = collection,
                Principal = principal,
                Operation = context,
                Payload = upsertRequest.CreatePayload
            }, cancellationToken).ConfigureAwait(false);
            if (!createValidation.IsSuccess() || createValidation.Value is null)
                return Failure<BaseMutationCommand, BaseValidatedPayload>(createValidation);
            createPayload = createValidation.Value;

            var updateValidation = upsertRequest.UpdateMode == RecordUpsertUpdateMode.Patch
                ? await schemaValidator.ValidatePatchAsync(new BasePayloadValidationRequest
                {
                    Collection = collection,
                    Principal = principal,
                    Operation = context,
                    Patch = upsertRequest.UpdatePayload
                }, cancellationToken).ConfigureAwait(false)
                : await schemaValidator.ValidateReplaceAsync(new BasePayloadValidationRequest
                {
                    Collection = collection,
                    Principal = principal,
                    Operation = context,
                    Payload = upsertRequest.UpdatePayload
                }, cancellationToken).ConfigureAwait(false);
            if (!updateValidation.IsSuccess() || updateValidation.Value is null)
                return Failure<BaseMutationCommand, BaseValidatedPayload>(updateValidation);
            updatePayload = updateValidation.Value;
        }

        var preparedUpsert = item.Upsert;
        return OperationResults.Ok(new BaseMutationCommand
        {
            Index = index,
            ItemId = item.ItemId,
            CollectionId = item.CollectionId,
            Kind = item.Kind,
            Collection = collection,
            Context = context,
            EventId = Guid.NewGuid().ToString("N"),
            Store = store,
            Create = preparedUpsert is null
                ? item.Create
                : new RecordCreateRequest
                {
                    Payload = preparedUpsert.CreatePayload,
                    RequestedId = preparedUpsert.Id
                },
            RecordId = TargetId(item),
            Patch = preparedUpsert?.UpdateMode == RecordUpsertUpdateMode.Patch
                ? new RecordPatchRequest
                {
                    Patch = preparedUpsert.UpdatePayload,
                    ExpectedRevision = preparedUpsert.ExpectedRevision
                }
                : item.Patch,
            Replace = preparedUpsert?.UpdateMode == RecordUpsertUpdateMode.Replace
                ? new RecordReplaceRequest
                {
                    Payload = preparedUpsert.UpdatePayload,
                    ExpectedRevision = preparedUpsert.ExpectedRevision
                }
                : item.Replace,
            Delete = item.Delete,
            Upsert = preparedUpsert,
            CreatePayload = createPayload,
            UpdatePayload = updatePayload
        });
    }

    private static bool ValidUnion(BaseRecordBatchItem item)
    {
        var bodies = (item.Create is null ? 0 : 1)
            + (item.Patch is null ? 0 : 1)
            + (item.Replace is null ? 0 : 1)
            + (item.Delete is null ? 0 : 1)
            + (item.Upsert is null ? 0 : 1);
        if (bodies != 1 || !Enum.IsDefined(item.Kind))
            return false;

        return item.Kind switch
        {
            BaseRecordMutationKind.Create => item.Create is not null && item.RecordId is null,
            BaseRecordMutationKind.Patch => item.Patch is not null && item.RecordId is not null,
            BaseRecordMutationKind.Replace => item.Replace is not null && item.RecordId is not null,
            BaseRecordMutationKind.Delete => item.Delete is not null && item.RecordId is not null,
            BaseRecordMutationKind.Upsert => item.Upsert is not null && item.RecordId is null,
            _ => false
        };
    }

    private static bool RevisionSupported(RevisionCapability? capability, BaseRecordBatchItem item)
    {
        var requiresPatch = item.Patch?.ExpectedRevision is not null;
        var requiresReplace = item.Replace?.ExpectedRevision is not null;
        var requiresDelete = item.Delete?.ExpectedRevision is not null;
        if (!requiresPatch && !requiresReplace && !requiresDelete)
            return true;

        return capability is
        {
            Supported: true,
            Guarantee: RevisionGuarantee.Store or RevisionGuarantee.Native
        }
        && (!requiresPatch || capability.Patch)
        && (!requiresReplace || capability.Replace)
        && (!requiresDelete || capability.Delete);
    }

    private static bool CollectionAllows(CollectionDefinition collection, BaseRecordBatchItem item) =>
        collection.MutationMode switch
        {
            BaseCollectionMutationMode.Mutable => true,
            BaseCollectionMutationMode.AppendOnly or
            BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge =>
                item.Kind == BaseRecordMutationKind.Create
                || item is
                {
                    Kind: BaseRecordMutationKind.Upsert,
                    Upsert.Condition: RecordUpsertExistenceCondition.CreateOnly
                },
            BaseCollectionMutationMode.ReadOnly => false,
            _ => false
        };

    private static bool SupportsMode(StoreBatchCapability? capability, BaseRecordBatchExecutionMode mode) =>
        capability is not null
        && capability.Ordered
        && capability.Modes.Contains(mode);

    private static RecordId? TargetId(BaseRecordBatchItem item) =>
        item.Kind == BaseRecordMutationKind.Upsert ? item.Upsert?.Id : item.RecordId;

    private static BaseOperationKind ToOperation(BaseRecordMutationKind kind) =>
        kind switch
        {
            BaseRecordMutationKind.Create => BaseOperationKind.Create,
            BaseRecordMutationKind.Patch => BaseOperationKind.Patch,
            BaseRecordMutationKind.Replace => BaseOperationKind.Replace,
            BaseRecordMutationKind.Delete => BaseOperationKind.Delete,
            BaseRecordMutationKind.Upsert => BaseOperationKind.Upsert,
            _ => BaseOperationKind.Batch
        };

    private static BaseRecordBatchItemResult FailedItem(BaseMutationCommand command, BaseError? error) => new()
    {
        ItemId = command.ItemId,
        Index = command.Index,
        Kind = command.Kind,
        Disposition = BaseRecordBatchItemDisposition.Failed,
        Status = StatusFor(error),
        Error = error ?? Error(BaseMutationErrorCodes.BatchItemInvalid, "The batch item failed.", ErrorCategory.Unexpected)
    };

    private static BaseRecordBatchItemResult RolledBackItem(BaseMutationCommand command) => new()
    {
        ItemId = command.ItemId,
        Index = command.Index,
        Kind = command.Kind,
        Disposition = BaseRecordBatchItemDisposition.RolledBack,
        Status = OperationStatus.StoreError,
        Error = Error(BaseMutationErrorCodes.BatchRolledBack, "The provisional mutation was rolled back.", ErrorCategory.Store)
    };

    private static BaseRecordBatchItemResult SkippedItem(BaseMutationCommand command) => new()
    {
        ItemId = command.ItemId,
        Index = command.Index,
        Kind = command.Kind,
        Disposition = BaseRecordBatchItemDisposition.Skipped,
        Status = OperationStatus.StoreError,
        Error = Error(BaseMutationErrorCodes.BatchSkipped, "The batch item was skipped.", ErrorCategory.Store)
    };

    private static OperationStatus StatusFor(BaseError? error) =>
        error?.Category switch
        {
            ErrorCategory.Validation => OperationStatus.ValidationFailed,
            ErrorCategory.Authentication => OperationStatus.Unauthorized,
            ErrorCategory.Authorization => OperationStatus.PolicyDenied,
            ErrorCategory.NotFound => OperationStatus.NotFound,
            ErrorCategory.Conflict => OperationStatus.Conflict,
            ErrorCategory.Unsupported => OperationStatus.Unsupported,
            ErrorCategory.Capability => OperationStatus.CapabilityUnavailable,
            _ => OperationStatus.StoreError
        };

    private static OperationResult<BaseRecordBatchResult> BatchResult(
        BaseRecordBatchOutcome outcome,
        BaseRecordBatchItemResult[] items,
        BaseError? error = null,
        BaseMutationRequestDisposition disposition = BaseMutationRequestDisposition.Committed) =>
        OperationResults.Ok(new BaseRecordBatchResult
        {
            Outcome = outcome,
            Items = items,
            Error = error,
            RequestDisposition = disposition,
            PostCommitWarningCount = Math.Min(
                items.Sum(static item => item.Warnings?.Length ?? 0),
                1_000)
        });

    private static OperationResult<T> Indeterminate<T>() =>
        OperationResults.StoreError<T>(new BaseError
        {
            Code = BaseMutationErrorCodes.BatchIndeterminate,
            Message = "The provider could not determine whether the mutation committed.",
            Category = ErrorCategory.Store,
            Store = new StoreErrorInfo { Retryable = false }
        });

    private static OperationResult<T> Validation<T>(string code, string message) =>
        OperationResults.ValidationFailed<T>(Error(code, message, ErrorCategory.Validation));

    private static OperationResult<T> Failure<T, TValue>(OperationResult<TValue> result) => new()
    {
        Status = result.Status,
        Error = result.Error
    };

    private static BaseError? Safe(BaseError? error) =>
        error is null
            ? null
            : new BaseError
            {
                Code = error.Code,
                Message = "The mutation operation failed.",
                Category = error.Category,
                Conflict = error.Conflict is null
                    ? null
                    : new ConflictInfo { Kind = error.Conflict.Kind },
                Store = error.Store is null
                    ? null
                    : new StoreErrorInfo { Retryable = error.Store.Retryable }
            };

    private async ValueTask<BaseRecordBatchItemResult> DispatchPostCommitAsync(
        BaseMutationAttempt attempt,
        PrincipalContext principal)
    {
        try
        {
            return await postCommit.DispatchAsync(attempt, principal).ConfigureAwait(false);
        }
        catch (Exception)
        {
            HPDBaseRuntimeLog.MutationEventDispatchFailed(
                logger,
                HPDBaseRuntimeLog.OperationKind(attempt.Command.Context.Operation),
                "unexpected",
                "base.runtime.events.postCommitFailed");
            return CommittedFallback(attempt);
        }
    }

    private static BaseRecordBatchItemResult CommittedFallback(BaseMutationAttempt attempt)
    {
        var mutation = attempt.Mutation!;
        var record = mutation.After is null ? null : SafeCommittedRecord(mutation.After);
        var delete = mutation.CommittedOperation == BaseCommittedRecordMutationKind.Delete
            ? new DeleteResult
            {
                Id = mutation.Delete?.Id ?? mutation.Before!.Id,
                Deleted = true,
                Previous = null
            }
            : null;
        var upsert = attempt.Command.Kind == BaseRecordMutationKind.Upsert
            ? new RecordUpsertResult
            {
                Outcome = mutation.UpsertOutcome!.Value,
                Record = record!
            }
            : null;

        return new BaseRecordBatchItemResult
        {
            ItemId = attempt.Command.ItemId,
            Index = attempt.Command.Index,
            Kind = attempt.Command.Kind,
            Disposition = BaseRecordBatchItemDisposition.Committed,
            Status = attempt.Status,
            Record = upsert is null ? record : null,
            Delete = delete,
            Upsert = upsert,
            Revision = attempt.Revision,
            Events = [mutation.Event],
            Warnings =
            [
                new OperationWarning
                {
                    Code = "base.runtime.events.postCommitFailed",
                    Message = "Post-commit processing failed after the mutation committed."
                }
            ]
        };
    }

    private static RecordEnvelope SafeCommittedRecord(RecordEnvelope record) => new()
    {
        CollectionId = record.CollectionId,
        Id = record.Id,
        Payload = new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
        },
        Metadata = new RecordMetadata
        {
            CreatedAt = record.Metadata.CreatedAt,
            UpdatedAt = record.Metadata.UpdatedAt,
            Revision = record.Metadata.Revision,
            ETag = record.Metadata.ETag
        },
        Policy = new RecordPolicyMetadata
        {
            Redacted = true,
            ReasonCode = "base.runtime.events.postCommitFailed"
        }
    };

    private static bool AllAttemptsSucceeded(
        IReadOnlyList<BaseMutationAttempt> attempts,
        int commandCount) =>
        attempts.Count == commandCount
        && attempts.All(static attempt => attempt.Status.IsSuccess() && attempt.Mutation is not null);

    private static OperationStatus StatusForExecution(RecordMutationExecutionResult result) =>
        result.Outcome switch
        {
            RecordMutationExecutionOutcome.Committed => OperationStatus.Ok,
            RecordMutationExecutionOutcome.ConflictRollbackConfirmed => OperationStatus.Conflict,
            RecordMutationExecutionOutcome.CancelledRollbackConfirmed
                or RecordMutationExecutionOutcome.RollbackConfirmed
                or RecordMutationExecutionOutcome.Indeterminate => OperationStatus.StoreError,
            _ => OperationStatus.StoreError
        };

    private static void FinishStoreTelemetry(
        Activity? activity,
        OperationContext context,
        long startedAt,
        OperationStatus status,
        BaseError? error) =>
        HPDBaseRuntimeTelemetry.FinishStoreInvocation(
            activity,
            new OperationResult<object> { Status = status, Error = error },
            context,
            startedAt);

    private void LogStoreFailure(BaseError? error, OperationContext context)
    {
        if (error?.Store?.Retryable is true)
        {
            HPDBaseRuntimeLog.StoreDependencyUnavailable(
                logger,
                HPDBaseRuntimeLog.OperationKind(context.Operation),
                "base.runtime.store.dependencyFailure");
        }
        else
        {
            HPDBaseRuntimeLog.StoreDependencyFailed(
                logger,
                HPDBaseRuntimeLog.OperationKind(context.Operation),
                "base.runtime.store.dependencyFailure");
        }
    }

    private static BaseError Error(string code, string message, ErrorCategory category) => new()
    {
        Code = code,
        Message = message,
        Category = category
    };

    private sealed record BoundaryResult(
        bool Committed,
        bool Indeterminate,
        bool AggregateFailure,
        IReadOnlyList<BaseMutationAttempt> Attempts,
        BaseError? Failure)
    {
        public BaseMutationRequestDisposition RequestDisposition { get; init; } = BaseMutationRequestDisposition.Committed;
    }
}
