using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Events;
using HPD.Base.Runtime.Policy;
using HPD.Base.Runtime.Query;
using HPD.Base.Runtime.Results;
using HPD.Base.Runtime.Schema;
using HPD.Base.Runtime.Stores;
using HPD.Base.Schema;
using HPD.Base.Stores;

namespace HPD.Base.Runtime.Operations;

internal sealed class DefaultBaseRecordRuntime : IBaseRecordRuntime
{
    private readonly IBaseSchemaProvider _schema;
    private readonly IBaseSchemaValidator _schemaValidator;
    private readonly IRecordStoreResolver _storeResolver;
    private readonly IBaseQueryValidator _queryValidator;
    private readonly IBasePolicyOrchestrator _policy;
    private readonly IBaseRecordRedactor _recordRedactor;
    private readonly IBaseResultNormalizer _normalizer;
    private readonly IBaseOperationalFailureMapper _failureMapper;
    private readonly IBaseEventFactory _eventFactory;
    private readonly IBaseEventDispatcher _eventDispatcher;

    public DefaultBaseRecordRuntime(
        IBaseSchemaProvider schema,
        IBaseSchemaValidator schemaValidator,
        IRecordStoreResolver storeResolver,
        IBaseQueryValidator queryValidator,
        IBasePolicyOrchestrator policy,
        IBaseRecordRedactor recordRedactor,
        IBaseResultNormalizer normalizer,
        IBaseOperationalFailureMapper failureMapper,
        IBaseEventFactory eventFactory,
        IBaseEventDispatcher eventDispatcher)
    {
        _schema = schema;
        _schemaValidator = schemaValidator;
        _storeResolver = storeResolver;
        _queryValidator = queryValidator;
        _policy = policy;
        _recordRedactor = recordRedactor;
        _normalizer = normalizer;
        _failureMapper = failureMapper;
        _eventFactory = eventFactory;
        _eventDispatcher = eventDispatcher;
    }

    public async ValueTask<OperationResult<RecordPage>> ListAsync(
        string collectionId,
        RecordQuery? query,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Normalize(operation, BaseOperationKind.List, collectionId);
        var collectionResult = await ResolveCollectionAsync(collectionId, principal, context, cancellationToken).ConfigureAwait(false);
        if (!collectionResult.IsSuccess() || collectionResult.Value is null)
        {
            return Failure<RecordPage, CollectionDefinition>(collectionResult);
        }

        var collection = collectionResult.Value;
        if (!Allows(collection, static matrix => matrix.List, collectionId, out var gate))
        {
            return gate.As<RecordPage>();
        }

        var storeResult = _storeResolver.Resolve(collection, context);
        if (!storeResult.IsSuccess() || storeResult.Value is null)
        {
            return Failure<RecordPage, HPD.Base.Stores.IRecordStore>(storeResult);
        }

        if (!StoreAllows(storeResult.Value.Capabilities.Crud, BaseOperationKind.List, collectionId, out var storeGate))
        {
            return storeGate.As<RecordPage>();
        }

        var queryToRun = query ?? new RecordQuery();
        var queryValidation = await _queryValidator.ValidateAsync(
            collection,
            queryToRun,
            storeResult.Value.Capabilities.Query,
            BaseQueryValidationUsage.ExternalQuery,
            context,
            cancellationToken).ConfigureAwait(false);
        if (!queryValidation.IsSuccess() || queryValidation.Value is null)
        {
            return Failure<RecordPage, ValidatedRecordQuery>(queryValidation);
        }

        var policyResult = await _policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = collection,
            ResourceKind = HPD.Base.Policy.PolicyResourceKind.Query,
            Query = queryValidation.Value.Query
        }, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess())
        {
            return Failure<RecordPage, BasePolicyEvaluation>(policyResult);
        }

        var composedQuery = ComposePolicyFilter(queryValidation.Value.Query, policyResult.Value?.EffectiveRecordFilter);
        if (!ReferenceEquals(composedQuery, queryValidation.Value.Query))
        {
            var composedValidation = await _queryValidator.ValidateAsync(
                collection,
                composedQuery,
                storeResult.Value.Capabilities.Query,
                BaseQueryValidationUsage.PolicyConstraint,
                context,
                cancellationToken).ConfigureAwait(false);
            if (!composedValidation.IsSuccess() || composedValidation.Value is null)
            {
                return Failure<RecordPage, ValidatedRecordQuery>(composedValidation);
            }

            composedQuery = composedValidation.Value.Query;
        }

        var result = await InvokeStoreAsync(
            () => storeResult.Value.ListAsync(collection, composedQuery, context, cancellationToken),
            context).ConfigureAwait(false);
        result = _normalizer.NormalizeStoreResult(result, context);
        return result.IsSuccess() && result.Value is not null && policyResult.Value is not null
            ? result with { Value = _recordRedactor.RedactPage(result.Value, collection, policyResult.Value, ViewFor(principal, context)) }
            : result;
    }

    public async ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        string collectionId,
        RecordId id,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Normalize(operation, BaseOperationKind.Get, collectionId, id);
        var idValidation = ValidateRecordId<RecordEnvelope>(id);
        if (idValidation is not null)
        {
            return idValidation;
        }

        var prepared = await PrepareStoreAsync<RecordEnvelope>(collectionId, principal, context, static matrix => matrix.Get, cancellationToken).ConfigureAwait(false);
        if (prepared.Result is not null)
        {
            return prepared.Result;
        }

        var policyResult = await _policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = prepared.Collection!,
            ResourceKind = HPD.Base.Policy.PolicyResourceKind.Record,
            RecordId = id
        }, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess())
        {
            return Failure<RecordEnvelope, BasePolicyEvaluation>(policyResult);
        }

        var result = await InvokeStoreAsync(
            () => prepared.Store!.GetAsync(prepared.Collection!, id, context, cancellationToken),
            context).ConfigureAwait(false);
        result = _normalizer.NormalizeStoreResult(result, context);
        if (!result.IsSuccess() || result.Value is null)
        {
            return result;
        }

        var candidatePolicy = await _policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = prepared.Collection!,
            ResourceKind = HPD.Base.Policy.PolicyResourceKind.Record,
            RecordId = id,
            ExistingRecord = result.Value
        }, cancellationToken).ConfigureAwait(false);
        if (!candidatePolicy.IsSuccess())
        {
            return ViewFor(principal, context) == VisibilityLevel.Public
                ? OperationResults.NotFound<RecordEnvelope>(new BaseError
                {
                    Code = "base.runtime.record.notFound",
                    Message = "Record was not found.",
                    Category = ErrorCategory.NotFound,
                    Target = id.Value
                })
                : Failure<RecordEnvelope, BasePolicyEvaluation>(candidatePolicy);
        }

        return candidatePolicy.Value is not null
            ? result with { Value = _recordRedactor.RedactRecord(result.Value, prepared.Collection!, candidatePolicy.Value, ViewFor(principal, context)) }
            : result;
    }

    public async ValueTask<OperationResult<RecordEnvelope>> CreateAsync(
        string collectionId,
        RecordCreateRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Normalize(operation, BaseOperationKind.Create, collectionId);
        var prepared = await PrepareStoreAsync<RecordEnvelope>(collectionId, principal, context, static matrix => matrix.Create, cancellationToken).ConfigureAwait(false);
        if (prepared.Result is not null)
        {
            return prepared.Result;
        }

        var createRequestGate = ValidateCreateRequest(request, prepared.Store!.Capabilities.Crud, collectionId);
        if (createRequestGate is not null)
        {
            return createRequestGate;
        }

        var validation = await _schemaValidator.ValidateCreateAsync(new BasePayloadValidationRequest
        {
            Collection = prepared.Collection!,
            Principal = principal,
            Operation = context,
            Payload = request.Payload
        }, cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess() || validation.Value is null)
        {
            return Failure<RecordEnvelope, BaseValidatedPayload>(validation);
        }

        var policyResult = await EvaluateWriteAsync(prepared.Collection!, principal, context, HPD.Base.Policy.PolicyResourceKind.CreatePayload, validation.Value.Payload, null, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess())
        {
            return Failure<RecordEnvelope, BasePolicyEvaluation>(policyResult);
        }

        var writePolicy = EnforceWritePolicy<RecordEnvelope>(validation.Value.Payload, policyResult.Value);
        if (writePolicy is not null)
        {
            return writePolicy;
        }

        var requestToStore = request with { Payload = validation.Value.Payload };
        var result = await InvokeStoreAsync(
            () => prepared.Store!.CreateAsync(prepared.Collection!, requestToStore, context, cancellationToken),
            context).ConfigureAwait(false);
        result = _normalizer.NormalizeStoreResult(result, context);
        result = RedactMutationResult(result, prepared.Collection!, policyResult.Value, principal, context);
        return await DispatchMutationIfSuccessfulAsync(BaseOperationKind.Create, result, context, principal, prepared.Collection!, null, result.Value, null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OperationResult<RecordEnvelope>> PatchAsync(
        string collectionId,
        RecordId id,
        RecordPatchRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Normalize(operation, BaseOperationKind.Patch, collectionId, id);
        var idValidation = ValidateRecordId<RecordEnvelope>(id);
        if (idValidation is not null)
        {
            return idValidation;
        }

        var prepared = await PrepareStoreAsync<RecordEnvelope>(collectionId, principal, context, static matrix => matrix.Patch, cancellationToken).ConfigureAwait(false);
        if (prepared.Result is not null)
        {
            return prepared.Result;
        }

        var validation = await _schemaValidator.ValidatePatchAsync(new BasePayloadValidationRequest
        {
            Collection = prepared.Collection!,
            Principal = principal,
            Operation = context,
            Patch = request.Patch
        }, cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess() || validation.Value is null)
        {
            return Failure<RecordEnvelope, BaseValidatedPayload>(validation);
        }

        if (request.ExpectedRevision is not null && prepared.Store is not HPD.Base.Stores.IRevisionedRecordStore)
        {
            return OperationResults.Unsupported<RecordEnvelope>(RevisionRequiredError(collectionId));
        }

        var existing = await InvokeStoreAsync(
            () => prepared.Store!.GetAsync(prepared.Collection!, id, context, cancellationToken),
            context).ConfigureAwait(false);
        existing = _normalizer.NormalizeStoreResult(existing, context);
        if (!existing.IsSuccess() || existing.Value is null)
        {
            return existing;
        }

        var proposedPayload = BasePolicyRuntimeSimulation.MergePatchPayload(existing.Value.Payload, validation.Value.Payload);
        var proposedRecord = existing.Value with { Payload = proposedPayload };
        var policyResult = await EvaluateWriteAsync(
            prepared.Collection!,
            principal,
            context,
            HPD.Base.Policy.PolicyResourceKind.UpdatePayload,
            proposedPayload,
            id,
            existing.Value,
            proposedRecord,
            cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess())
        {
            return Failure<RecordEnvelope, BasePolicyEvaluation>(policyResult);
        }

        var writePolicy = EnforceWritePolicy<RecordEnvelope>(validation.Value.Payload, policyResult.Value);
        if (writePolicy is not null)
        {
            return writePolicy;
        }

        var requestToStore = request with { Patch = validation.Value.Payload };
        var result = request.ExpectedRevision is { } expected
            ? await InvokeStoreAsync(
                () => ((HPD.Base.Stores.IRevisionedRecordStore)prepared.Store!).PatchIfRevisionAsync(prepared.Collection!, id, requestToStore, expected, context, cancellationToken),
                context).ConfigureAwait(false)
            : await InvokeStoreAsync(
                () => prepared.Store!.PatchAsync(prepared.Collection!, id, requestToStore, context, cancellationToken),
                context).ConfigureAwait(false);

        result = _normalizer.NormalizeStoreResult(result, context);
        result = RedactMutationResult(result, prepared.Collection!, policyResult.Value, principal, context);
        return await DispatchMutationIfSuccessfulAsync(BaseOperationKind.Patch, result, context, principal, prepared.Collection!, existing.Value, result.Value, validation.Value.ChangedFields, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(
        string collectionId,
        RecordId id,
        RecordReplaceRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Normalize(operation, BaseOperationKind.Replace, collectionId, id);
        var idValidation = ValidateRecordId<RecordEnvelope>(id);
        if (idValidation is not null)
        {
            return idValidation;
        }

        var prepared = await PrepareStoreAsync<RecordEnvelope>(collectionId, principal, context, static matrix => matrix.Replace, cancellationToken).ConfigureAwait(false);
        if (prepared.Result is not null)
        {
            return prepared.Result;
        }

        var validation = await _schemaValidator.ValidateReplaceAsync(new BasePayloadValidationRequest
        {
            Collection = prepared.Collection!,
            Principal = principal,
            Operation = context,
            Payload = request.Payload
        }, cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess() || validation.Value is null)
        {
            return Failure<RecordEnvelope, BaseValidatedPayload>(validation);
        }

        var policyResult = await EvaluateWriteAsync(prepared.Collection!, principal, context, HPD.Base.Policy.PolicyResourceKind.UpdatePayload, validation.Value.Payload, id, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess())
        {
            return Failure<RecordEnvelope, BasePolicyEvaluation>(policyResult);
        }

        var writePolicy = EnforceWritePolicy<RecordEnvelope>(validation.Value.Payload, policyResult.Value);
        if (writePolicy is not null)
        {
            return writePolicy;
        }

        var requestToStore = request with { Payload = validation.Value.Payload };
        var result = request.ExpectedRevision is { } expected
            ? prepared.Store is HPD.Base.Stores.IRevisionedRecordStore revisioned
                ? await InvokeStoreAsync(
                    () => revisioned.ReplaceIfRevisionAsync(prepared.Collection!, id, requestToStore, expected, context, cancellationToken),
                    context).ConfigureAwait(false)
                : OperationResults.Unsupported<RecordEnvelope>(RevisionRequiredError(collectionId))
            : await InvokeStoreAsync(
                () => prepared.Store!.ReplaceAsync(prepared.Collection!, id, requestToStore, context, cancellationToken),
                context).ConfigureAwait(false);

        result = _normalizer.NormalizeStoreResult(result, context);
        result = RedactMutationResult(result, prepared.Collection!, policyResult.Value, principal, context);
        return await DispatchMutationIfSuccessfulAsync(BaseOperationKind.Replace, result, context, principal, prepared.Collection!, null, result.Value, null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OperationResult<DeleteResult>> DeleteAsync(
        string collectionId,
        RecordId id,
        RecordDeleteRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Normalize(operation, BaseOperationKind.Delete, collectionId, id);
        var idValidation = ValidateRecordId<DeleteResult>(id);
        if (idValidation is not null)
        {
            return idValidation;
        }

        var prepared = await PrepareStoreAsync<DeleteResult>(collectionId, principal, context, static matrix => matrix.Delete, cancellationToken).ConfigureAwait(false);
        if (prepared.Result is not null)
        {
            return prepared.Result;
        }

        if (request.ExpectedRevision is not null && !SupportsExpectedRevisionDelete(prepared.Store!))
        {
            return OperationResults.Unsupported<DeleteResult>(RevisionRequiredError(collectionId));
        }

        var existing = await InvokeStoreAsync(
            () => prepared.Store!.GetAsync(prepared.Collection!, id, context, cancellationToken),
            context).ConfigureAwait(false);
        existing = _normalizer.NormalizeStoreResult(existing, context);
        if (!existing.IsSuccess() || existing.Value is null)
        {
            return Failure<DeleteResult, RecordEnvelope>(existing);
        }

        var policyResult = await _policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = prepared.Collection!,
            ResourceKind = HPD.Base.Policy.PolicyResourceKind.DeleteCandidate,
            RecordId = id,
            ExistingRecord = existing.Value
        }, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess())
        {
            return Failure<DeleteResult, BasePolicyEvaluation>(policyResult);
        }

        var deleteWritePolicy = EnforceWritePolicy<DeleteResult>(null, policyResult.Value);
        if (deleteWritePolicy is not null)
        {
            return deleteWritePolicy;
        }

        var result = await InvokeStoreAsync(
            () => prepared.Store!.DeleteAsync(prepared.Collection!, id, request, context, cancellationToken),
            context).ConfigureAwait(false);
        result = _normalizer.NormalizeStoreResult(result, context);
        if (!result.IsSuccess() || result.Value is null)
        {
            return result;
        }

        var eventPreviousSnapshot = result.Value.Previous ?? (result.Value.Deleted ? existing.Value : null);
        var returnedPrevious = result.Value.Previous is not null && policyResult.Value is not null
            ? _recordRedactor.RedactRecord(result.Value.Previous, prepared.Collection!, policyResult.Value, ViewFor(principal, context))
            : result.Value.Previous;
        var eventPrevious = eventPreviousSnapshot is not null && policyResult.Value is not null
            ? _recordRedactor.RedactRecord(eventPreviousSnapshot, prepared.Collection!, policyResult.Value, ViewFor(principal, context))
            : eventPreviousSnapshot;
        result = result with { Value = result.Value with { Previous = returnedPrevious } };

        var @event = _eventFactory.CreateRecordMutationEvent(
            BaseOperationKind.Delete,
            context,
            principal,
            prepared.Collection!,
            eventPrevious,
            null,
            null);
        var events = await _eventDispatcher.DispatchMutationAsync(@event, cancellationToken).ConfigureAwait(false);
        return result with
        {
            Events = events.Value,
            Warnings = Combine(result.Warnings, events.Warnings),
            Diagnostics = result.Diagnostics ?? events.Diagnostics
        };
    }

    private OperationResult<RecordEnvelope> RedactMutationResult(
        OperationResult<RecordEnvelope> result,
        CollectionDefinition collection,
        BasePolicyEvaluation? policy,
        PrincipalContext principal,
        OperationContext context) =>
        result.IsSuccess() && result.Value is not null && policy is not null
            ? result with { Value = _recordRedactor.RedactRecord(result.Value, collection, policy, ViewFor(principal, context)) }
            : result;

    private static OperationResult<T>? EnforceWritePolicy<T>(
        RecordPayload? payload,
        BasePolicyEvaluation? policy)
    {
        if (policy?.Decision.Constraints?.WriteCheck is not null)
        {
            return OperationResults.Unsupported<T>(new BaseError
            {
                Code = "base.runtime.policy.writeCheck.unsupported",
                Message = "Policy write checks are not safely evaluable by this runtime.",
                Category = ErrorCategory.Unsupported
            });
        }

        if (policy?.EffectiveWriteMask is not { } mask)
        {
            return null;
        }

        var fields = payload is null ? [] : BasePolicyRuntimeSimulation.PayloadFields(payload);
        if (fields.Length == 0)
        {
            return null;
        }

        var denied = mask.Mode switch
        {
            FieldMaskMode.Unspecified or FieldMaskMode.AllowAll => null,
            FieldMaskMode.DenyAll => fields[0],
            FieldMaskMode.IncludeOnly => fields.FirstOrDefault(field => !(mask.Include ?? []).Contains(field, StringComparer.Ordinal)),
            FieldMaskMode.Exclude => fields.FirstOrDefault(field => (mask.Exclude ?? []).Contains(field, StringComparer.Ordinal)),
            _ => fields[0]
        };

        return denied is null
            ? null
            : OperationResults.PolicyDenied<T>(new BaseError
            {
                Code = "base.runtime.policy.writeMask.denied",
                Message = "Policy write mask does not allow this field to be written.",
                Category = ErrorCategory.Authorization,
                Target = denied,
                Policy = new PolicyErrorInfo { ReasonCode = "writeMask" }
            });
    }

    private static OperationResult<RecordEnvelope>? ValidateCreateRequest(
        RecordCreateRequest request,
        CrudCapability capability,
        string collectionId)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return OperationResults.Unsupported<RecordEnvelope>(new BaseError
            {
                Code = "base.runtime.create.idempotencyUnsupported",
                Message = "Idempotency keys are not supported by this runtime/store combination.",
                Category = ErrorCategory.Unsupported,
                Target = collectionId
            });
        }

        if (request.RequestedId is { } requestedId)
        {
            var idValidation = ValidateRecordId<RecordEnvelope>(requestedId);
            if (idValidation is not null)
            {
                return idValidation;
            }

            if (capability.IdAuthority is not (IdAuthority.Client or IdAuthority.Hybrid))
            {
                return OperationResults.Unsupported<RecordEnvelope>(new BaseError
                {
                    Code = "base.runtime.create.requestedIdUnsupported",
                    Message = "Client-requested ids are not supported by the selected store id authority.",
                    Category = ErrorCategory.Unsupported,
                    Target = collectionId
                });
            }
        }

        return null;
    }

    private static OperationResult<T>? ValidateRecordId<T>(RecordId id)
    {
        if (!string.IsNullOrWhiteSpace(id.Value))
        {
            return null;
        }

        return OperationResults.ValidationFailed<T>(new BaseError
        {
            Code = "base.runtime.recordId.invalid",
            Message = "Record id must be non-empty.",
            Category = ErrorCategory.Validation,
            Target = "id"
        });
    }

    private async ValueTask<PreparedStore<T>> PrepareStoreAsync<T>(
        string collectionId,
        PrincipalContext principal,
        OperationContext context,
        Func<CollectionOperationMatrix, bool> operationAllowed,
        CancellationToken cancellationToken)
    {
        var collectionResult = await ResolveCollectionAsync(collectionId, principal, context, cancellationToken).ConfigureAwait(false);
        if (!collectionResult.IsSuccess() || collectionResult.Value is null)
        {
            return new PreparedStore<T>(Failure<T, CollectionDefinition>(collectionResult), null, null);
        }

        var collection = collectionResult.Value;
        if (!Allows(collection, operationAllowed, collectionId, out var gate))
        {
            return new PreparedStore<T>(gate.As<T>(), null, null);
        }

        var storeResult = _storeResolver.Resolve(collection, context);
        if (!storeResult.IsSuccess() || storeResult.Value is null)
        {
            return new PreparedStore<T>(Failure<T, HPD.Base.Stores.IRecordStore>(storeResult), null, null);
        }

        if (!StoreAllows(storeResult.Value.Capabilities.Crud, context.Operation, collectionId, out var storeGate))
        {
            return new PreparedStore<T>(storeGate.As<T>(), null, null);
        }

        return new PreparedStore<T>(null, collection, storeResult.Value);
    }

    private async ValueTask<OperationResult<CollectionDefinition>> ResolveCollectionAsync(
        string collectionId,
        PrincipalContext principal,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        var result = await _schema.GetCollectionAsync(
            collectionId,
            principal,
            context,
            VisibilityLevel.Internal,
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess() || result.Value is null)
        {
            return result;
        }

        var collection = result.Value;
        if (!collection.Enabled || !collection.Exposed || collection.ReadOnly && context.Operation is BaseOperationKind.Create or BaseOperationKind.Patch or BaseOperationKind.Replace or BaseOperationKind.Delete)
        {
            return OperationResults.Unsupported<CollectionDefinition>(new BaseError
            {
                Code = "base.runtime.collection.disabled",
                Message = "Collection is not available for this operation.",
                Category = ErrorCategory.Unsupported,
                Target = collectionId
            });
        }

        return result;
    }

    private async ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateWriteAsync(
        CollectionDefinition collection,
        PrincipalContext principal,
        OperationContext context,
        HPD.Base.Policy.PolicyResourceKind resourceKind,
        RecordPayload proposedPayload,
        RecordId? recordId,
        RecordEnvelope? existingRecord,
        RecordEnvelope? proposedRecord,
        CancellationToken cancellationToken) =>
        await _policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = collection,
            ResourceKind = resourceKind,
            ProposedPayload = proposedPayload,
            ExistingRecord = existingRecord,
            ProposedRecord = proposedRecord,
            RecordId = recordId
        }, cancellationToken).ConfigureAwait(false);

    private ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateWriteAsync(
        CollectionDefinition collection,
        PrincipalContext principal,
        OperationContext context,
        HPD.Base.Policy.PolicyResourceKind resourceKind,
        RecordPayload proposedPayload,
        RecordId? recordId,
        CancellationToken cancellationToken) =>
        EvaluateWriteAsync(collection, principal, context, resourceKind, proposedPayload, recordId, null, null, cancellationToken);

    private async ValueTask<OperationResult<T>> InvokeStoreAsync<T>(
        Func<ValueTask<OperationResult<T>>> invoke,
        OperationContext context)
    {
        try
        {
            return await invoke().ConfigureAwait(false);
        }
        catch (Exception exception) when (_failureMapper.TryMap(exception, context, out var error, out var status))
        {
            return new OperationResult<T>
            {
                Status = status,
                Error = error
            };
        }
    }

    private async ValueTask<OperationResult<RecordEnvelope>> DispatchMutationIfSuccessfulAsync(
        BaseOperationKind operation,
        OperationResult<RecordEnvelope> result,
        OperationContext context,
        PrincipalContext principal,
        CollectionDefinition collection,
        RecordEnvelope? before,
        RecordEnvelope? after,
        string[]? changedFields,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess() || result.Value is null)
        {
            return result;
        }

        var @event = _eventFactory.CreateRecordMutationEvent(
            operation,
            context,
            principal,
            collection,
            before,
            after,
            changedFields);
        var events = await _eventDispatcher.DispatchMutationAsync(@event, cancellationToken).ConfigureAwait(false);
        return result with
        {
            Events = events.Value,
            Warnings = Combine(result.Warnings, events.Warnings),
            Diagnostics = result.Diagnostics ?? events.Diagnostics
        };
    }

    private static bool Allows(
        CollectionDefinition collection,
        Func<CollectionOperationMatrix, bool> operationAllowed,
        string collectionId,
        out GateFailure gate)
    {
        if (collection.Operations is not null && !operationAllowed(collection.Operations))
        {
            gate = new GateFailure(new BaseError
            {
                Code = "base.runtime.collection.operationDisabled",
                Message = "Collection does not allow this operation.",
                Category = ErrorCategory.Unsupported,
                Target = collectionId
            });
            return false;
        }

        gate = default;
        return true;
    }

    private static bool StoreAllows(
        CrudCapability capability,
        BaseOperationKind operation,
        string collectionId,
        out GateFailure gate)
    {
        var supported = operation switch
        {
            BaseOperationKind.List => capability.List,
            BaseOperationKind.Get => capability.Get,
            BaseOperationKind.Create => capability.Create,
            BaseOperationKind.Patch => capability.Patch,
            BaseOperationKind.Replace => capability.Replace,
            BaseOperationKind.Delete => capability.Delete,
            _ => false
        };

        if (!supported)
        {
            gate = new GateFailure(new BaseError
            {
                Code = "base.runtime.store.operationUnsupported",
                Message = "The registered store does not support this operation.",
                Category = ErrorCategory.Unsupported,
                Target = collectionId
            });
            return false;
        }

        gate = default;
        return true;
    }

    private static bool SupportsExpectedRevisionDelete(HPD.Base.Stores.IRecordStore store) =>
        store.Capabilities.Revision is
        {
            Supported: true,
            Delete: true,
            Guarantee: RevisionGuarantee.Store or RevisionGuarantee.Native
        };

    private static OperationContext Normalize(
        OperationContext operation,
        BaseOperationKind kind,
        string collectionId,
        RecordId? recordId = null) =>
        operation with
        {
            Operation = kind,
            CollectionId = collectionId,
            RecordId = recordId?.Value ?? operation.RecordId,
            Now = operation.Now == default ? DateTimeOffset.UtcNow : operation.Now
        };

    private static RecordQuery ComposePolicyFilter(RecordQuery query, FilterExpression? policyFilter) =>
        BasePolicyRuntimeSimulation.ComposePolicyFilter(query, policyFilter);

    private static VisibilityLevel ViewFor(PrincipalContext principal, OperationContext context) =>
        BasePolicyRuntimeSimulation.ViewFor(principal, context);

    private static OperationResult<T> Failure<T, TValue>(OperationResult<TValue> result) =>
        new()
        {
            Status = result.Status,
            Error = result.Error,
            Warnings = result.Warnings,
            Diagnostics = result.Diagnostics,
            Revision = result.Revision,
            Events = result.Events
        };

    private static OperationWarning[]? Combine(OperationWarning[]? first, OperationWarning[]? second)
    {
        if (first is null or { Length: 0 })
        {
            return second is { Length: > 0 } ? second : null;
        }

        if (second is null or { Length: 0 })
        {
            return first;
        }

        return [.. first, .. second];
    }

    private static BaseError RevisionRequiredError(string collectionId) => new()
    {
        Code = "base.runtime.revision.unsupported",
        Message = "Atomic expected-revision behavior is not available for this operation.",
        Category = ErrorCategory.Unsupported,
        Target = collectionId
    };

    private readonly record struct PreparedStore<T>(
        OperationResult<T>? Result,
        CollectionDefinition? Collection,
        HPD.Base.Stores.IRecordStore? Store);

    private readonly record struct GateFailure(BaseError Error)
    {
        public OperationResult<T> As<T>() => OperationResults.Unsupported<T>(Error);
    }
}
