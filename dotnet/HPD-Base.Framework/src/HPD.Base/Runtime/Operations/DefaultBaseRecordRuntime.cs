using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HPD.Base;

internal sealed class DefaultBaseRecordRuntime(
    IBaseSchemaProvider schema,
    IRecordStoreResolver storeResolver,
    IBaseQueryValidator queryValidator,
    IBasePolicyOrchestrator policy,
    IBaseRecordRedactor recordRedactor,
    IBaseResultNormalizer normalizer,
    IBaseOperationalFailureMapper failureMapper,
    IBaseMutationCoordinator mutations,
    ILogger<DefaultBaseRecordRuntime> logger) : IBaseRecordRuntime
{
    public async ValueTask<OperationResult<RecordPage>> ListAsync(
        string collectionId,
        RecordQuery? query,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Normalize(operation, BaseOperationKind.List, collectionId);
        using var activity = HPDBaseRuntimeTelemetry.StartRuntimeOperation(
            HPDBaseTelemetrySpans.RuntimeRecordsList,
            BaseOperationKind.List,
            collectionId,
            context);
        var startedAt = Stopwatch.GetTimestamp();
        var collectionResult = await ResolveCollectionAsync(
            collectionId,
            principal,
            context,
            cancellationToken).ConfigureAwait(false);
        if (!collectionResult.IsSuccess() || collectionResult.Value is null)
            return Finish(activity, Failure<RecordPage, CollectionDefinition>(collectionResult), BaseOperationKind.List, collectionId, context, startedAt);

        var collection = collectionResult.Value;
        if (!Allows(collection, static matrix => matrix.List, out var gate))
            return Finish(activity, gate.As<RecordPage>(), BaseOperationKind.List, collectionId, context, startedAt);

        var storeResult = storeResolver.Resolve(collection, context);
        if (!storeResult.IsSuccess() || storeResult.Value is null)
        {
            LogStoreUnavailableIfMissing(storeResult, context.Operation);
            return Finish(activity, Failure<RecordPage, IRecordStore>(storeResult), BaseOperationKind.List, collectionId, context, startedAt);
        }

        if (!StoreAllows(storeResult.Value.Capabilities.Read, BaseOperationKind.List, out var storeGate))
            return Finish(activity, storeGate.As<RecordPage>(), BaseOperationKind.List, collectionId, context, startedAt);

        var queryToRun = query ?? new RecordQuery();
        var queryValidation = await queryValidator.ValidateAsync(
            collection,
            queryToRun,
            storeResult.Value.Capabilities.Query,
            BaseQueryValidationUsage.ExternalQuery,
            context,
            cancellationToken).ConfigureAwait(false);
        if (!queryValidation.IsSuccess() || queryValidation.Value is null)
            return Finish(activity, Failure<RecordPage, ValidatedRecordQuery>(queryValidation), BaseOperationKind.List, collectionId, context, startedAt);

        var policyResult = await EvaluateReadPolicyAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = collection,
            ResourceKind = PolicyResourceKind.Query,
            Query = queryValidation.Value.Query
        }, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess())
            return Finish(activity, Failure<RecordPage, BasePolicyEvaluation>(policyResult), BaseOperationKind.List, collectionId, context, startedAt);

        var composedQuery = BasePolicyRuntimeSimulation.ComposePolicyFilter(
            queryValidation.Value.Query,
            policyResult.Value?.EffectiveRecordFilter);
        if (!ReferenceEquals(composedQuery, queryValidation.Value.Query))
        {
            var composedValidation = await queryValidator.ValidateAsync(
                collection,
                composedQuery,
                storeResult.Value.Capabilities.Query,
                BaseQueryValidationUsage.PolicyConstraint,
                context,
                cancellationToken).ConfigureAwait(false);
            if (!composedValidation.IsSuccess() || composedValidation.Value is null)
                return Finish(activity, Failure<RecordPage, ValidatedRecordQuery>(composedValidation), BaseOperationKind.List, collectionId, context, startedAt);
            composedQuery = composedValidation.Value.Query;
        }

        var result = await InvokeStoreAsync(
            () => storeResult.Value.ListAsync(collection, composedQuery, context, cancellationToken),
            context).ConfigureAwait(false);
        result = normalizer.NormalizeStoreResult(result, context);
        if (result.IsSuccess() && result.Value is not null && policyResult.Value is not null)
        {
            result = result with
            {
                Value = recordRedactor.RedactPage(
                    result.Value,
                    collection,
                    policyResult.Value,
                    BasePolicyRuntimeSimulation.ViewFor(principal, context))
            };
        }

        return Finish(activity, result, BaseOperationKind.List, collectionId, context, startedAt);
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
        using var activity = HPDBaseRuntimeTelemetry.StartRuntimeOperation(
            HPDBaseTelemetrySpans.RuntimeRecordsGet,
            BaseOperationKind.Get,
            collectionId,
            context);
        var startedAt = Stopwatch.GetTimestamp();
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            return Finish(
                activity,
                OperationResults.ValidationFailed<RecordEnvelope>(new BaseError
                {
                    Code = "base.runtime.recordId.invalid",
                    Message = "Record id must be non-empty.",
                    Category = ErrorCategory.Validation,
                    Target = "id"
                }),
                BaseOperationKind.Get,
                collectionId,
                context,
                startedAt);
        }

        var prepared = await PrepareReadStoreAsync<RecordEnvelope>(
            collectionId,
            principal,
            context,
            static matrix => matrix.Get,
            cancellationToken).ConfigureAwait(false);
        if (prepared.Result is not null)
            return Finish(activity, prepared.Result, BaseOperationKind.Get, collectionId, context, startedAt);

        var policyResult = await EvaluateReadPolicyAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = prepared.Collection!,
            ResourceKind = PolicyResourceKind.Record,
            RecordId = id
        }, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess())
            return Finish(activity, Failure<RecordEnvelope, BasePolicyEvaluation>(policyResult), BaseOperationKind.Get, collectionId, context, startedAt);

        var result = await InvokeStoreAsync(
            () => prepared.Store!.GetAsync(prepared.Collection!, id, context, cancellationToken),
            context).ConfigureAwait(false);
        result = normalizer.NormalizeStoreResult(result, context);
        if (!result.IsSuccess() || result.Value is null)
            return Finish(activity, result, BaseOperationKind.Get, collectionId, context, startedAt);

        var candidatePolicy = await EvaluateReadPolicyAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = prepared.Collection!,
            ResourceKind = PolicyResourceKind.Record,
            RecordId = id,
            ExistingRecord = result.Value
        }, cancellationToken).ConfigureAwait(false);
        if (!candidatePolicy.IsSuccess())
        {
            var failure = BasePolicyRuntimeSimulation.ViewFor(principal, context) == VisibilityLevel.Public
                ? OperationResults.NotFound<RecordEnvelope>(new BaseError
                {
                    Code = "base.runtime.record.notFound",
                    Message = "Record was not found.",
                    Category = ErrorCategory.NotFound,
                    Target = id.Value
                })
                : Failure<RecordEnvelope, BasePolicyEvaluation>(candidatePolicy);
            return Finish(activity, failure, BaseOperationKind.Get, collectionId, context, startedAt);
        }

        if (candidatePolicy.Value is not null)
        {
            result = result with
            {
                Value = recordRedactor.RedactRecord(
                    result.Value,
                    prepared.Collection!,
                    candidatePolicy.Value,
                    BasePolicyRuntimeSimulation.ViewFor(principal, context))
            };
        }

        return Finish(activity, result, BaseOperationKind.Get, collectionId, context, startedAt);
    }

    public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(
        string collectionId,
        RecordCreateRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        ExecuteRecordMutationAsync(
            collectionId,
            BaseOperationKind.Create,
            HPDBaseTelemetrySpans.RuntimeRecordsCreate,
            new BaseRecordBatchItem
            {
                ItemId = "single",
                CollectionId = collectionId,
                Kind = BaseRecordMutationKind.Create,
                Create = request
            },
            principal,
            operation,
            static item => item.Record,
            cancellationToken);

    public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(
        string collectionId,
        RecordId id,
        RecordPatchRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        ExecuteRecordMutationAsync(
            collectionId,
            BaseOperationKind.Patch,
            HPDBaseTelemetrySpans.RuntimeRecordsPatch,
            new BaseRecordBatchItem
            {
                ItemId = "single",
                CollectionId = collectionId,
                Kind = BaseRecordMutationKind.Patch,
                RecordId = id,
                Patch = request
            },
            principal,
            operation,
            static item => item.Record,
            cancellationToken);

    public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(
        string collectionId,
        RecordId id,
        RecordReplaceRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        ExecuteRecordMutationAsync(
            collectionId,
            BaseOperationKind.Replace,
            HPDBaseTelemetrySpans.RuntimeRecordsReplace,
            new BaseRecordBatchItem
            {
                ItemId = "single",
                CollectionId = collectionId,
                Kind = BaseRecordMutationKind.Replace,
                RecordId = id,
                Replace = request
            },
            principal,
            operation,
            static item => item.Record,
            cancellationToken);

    public ValueTask<OperationResult<DeleteResult>> DeleteAsync(
        string collectionId,
        RecordId id,
        RecordDeleteRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        ExecuteRecordMutationAsync(
            collectionId,
            BaseOperationKind.Delete,
            HPDBaseTelemetrySpans.RuntimeRecordsDelete,
            new BaseRecordBatchItem
            {
                ItemId = "single",
                CollectionId = collectionId,
                Kind = BaseRecordMutationKind.Delete,
                RecordId = id,
                Delete = request
            },
            principal,
            operation,
            static item => item.Delete,
            cancellationToken);

    public ValueTask<OperationResult<RecordUpsertResult>> UpsertAsync(
        string collectionId,
        RecordUpsertRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        ExecuteRecordMutationAsync(
            collectionId,
            BaseOperationKind.Upsert,
            "hpd.base.runtime.records.upsert",
            new BaseRecordBatchItem
            {
                ItemId = "single",
                CollectionId = collectionId,
                Kind = BaseRecordMutationKind.Upsert,
                Upsert = request
            },
            principal,
            operation,
            static item => item.Upsert,
            cancellationToken);

    public async ValueTask<OperationResult<BaseRecordBatchResult>> BatchAsync(
        BaseRecordBatchRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        var context = Normalize(operation, BaseOperationKind.Batch, operation.CollectionId);
        using var activity = HPDBaseRuntimeTelemetry.StartRuntimeOperation(
            "hpd.base.runtime.records.batch",
            BaseOperationKind.Batch,
            context.CollectionId,
            context);
        var startedAt = Stopwatch.GetTimestamp();
        var result = await mutations.ExecuteBatchAsync(
            request,
            principal,
            context,
            cancellationToken).ConfigureAwait(false);
        return Finish(activity, result, BaseOperationKind.Batch, context.CollectionId, context, startedAt);
    }

    private async ValueTask<OperationResult<T>> ExecuteRecordMutationAsync<T>(
        string collectionId,
        BaseOperationKind operationKind,
        string span,
        BaseRecordBatchItem item,
        PrincipalContext principal,
        OperationContext operation,
        Func<BaseRecordBatchItemResult, T?> value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Normalize(operation, operationKind, collectionId, TargetId(item));
        using var activity = HPDBaseRuntimeTelemetry.StartRuntimeOperation(
            span,
            operationKind,
            collectionId,
            context);
        var startedAt = Stopwatch.GetTimestamp();
        var result = await mutations.ExecuteSingleAsync(
            item,
            principal,
            context,
            cancellationToken).ConfigureAwait(false);
        var mapped = Map(result, value);
        return Finish(activity, mapped, operationKind, collectionId, context, startedAt);
    }

    private async ValueTask<PreparedReadStore<T>> PrepareReadStoreAsync<T>(
        string collectionId,
        PrincipalContext principal,
        OperationContext context,
        Func<CollectionOperationMatrix, bool> operationAllowed,
        CancellationToken cancellationToken)
    {
        var collectionResult = await ResolveCollectionAsync(
            collectionId,
            principal,
            context,
            cancellationToken).ConfigureAwait(false);
        if (!collectionResult.IsSuccess() || collectionResult.Value is null)
            return new PreparedReadStore<T>(Failure<T, CollectionDefinition>(collectionResult), null, null);

        var collection = collectionResult.Value;
        if (!Allows(collection, operationAllowed, out var gate))
            return new PreparedReadStore<T>(gate.As<T>(), null, null);

        var storeResult = storeResolver.Resolve(collection, context);
        if (!storeResult.IsSuccess() || storeResult.Value is null)
        {
            LogStoreUnavailableIfMissing(storeResult, context.Operation);
            return new PreparedReadStore<T>(Failure<T, IRecordStore>(storeResult), null, null);
        }

        if (!StoreAllows(storeResult.Value.Capabilities.Read, context.Operation, out var storeGate))
            return new PreparedReadStore<T>(storeGate.As<T>(), null, null);

        return new PreparedReadStore<T>(null, collection, storeResult.Value);
    }

    private async ValueTask<OperationResult<CollectionDefinition>> ResolveCollectionAsync(
        string collectionId,
        PrincipalContext principal,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        var result = await schema.GetCollectionAsync(
            collectionId,
            principal,
            context,
            VisibilityLevel.Internal,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null)
            return result;
        if (!result.Value.Enabled || !result.Value.Exposed)
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

    private async ValueTask<OperationResult<T>> InvokeStoreAsync<T>(
        Func<ValueTask<OperationResult<T>>> invoke,
        OperationContext context)
    {
        using var activity = HPDBaseRuntimeTelemetry.StartStoreInvocation(context);
        var startedAt = Stopwatch.GetTimestamp();
        OperationResult<T> result;
        try
        {
            result = await invoke().ConfigureAwait(false);
        }
        catch (Exception exception) when (failureMapper.TryMap(exception, context, out var error, out var status))
        {
            if (error.Store?.Retryable is true)
                HPDBaseRuntimeLog.StoreDependencyUnavailable(logger, HPDBaseRuntimeLog.OperationKind(context.Operation), "base.runtime.store.dependencyFailure");
            else
                HPDBaseRuntimeLog.StoreDependencyFailed(logger, HPDBaseRuntimeLog.OperationKind(context.Operation), "base.runtime.store.dependencyFailure");
            result = new OperationResult<T> { Status = status, Error = error };
        }

        return HPDBaseRuntimeTelemetry.FinishStoreInvocation(activity, result, context, startedAt);
    }

    private async ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateReadPolicyAsync(
        BasePolicyRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = HPDBaseRuntimeTelemetry.StartPolicyEvaluation(
            request.Operation,
            request.ResourceKind.ToString());
        var startedAt = Stopwatch.GetTimestamp();
        var result = await policy.EvaluateReadAsync(request, cancellationToken).ConfigureAwait(false);
        return HPDBaseRuntimeTelemetry.FinishPolicyEvaluation(
            activity,
            result,
            request.Operation,
            startedAt);
    }

    private void LogStoreUnavailableIfMissing<T>(OperationResult<T> result, BaseOperationKind operation)
    {
        if (string.Equals(result.Error?.Code, "base.runtime.store.missing", StringComparison.Ordinal))
            HPDBaseRuntimeLog.StoreUnavailable(logger, HPDBaseRuntimeLog.OperationKind(operation), "missingRegistration");
    }

    private static OperationResult<T> Map<T>(
        OperationResult<BaseRecordBatchItemResult> result,
        Func<BaseRecordBatchItemResult, T?> value)
    {
        if (!result.IsSuccess() || result.Value is null)
        {
            return new OperationResult<T>
            {
                Status = result.Status,
                Error = result.Error,
                Warnings = result.Warnings,
                Diagnostics = result.Diagnostics,
                Revision = result.Revision,
                Events = result.Events
            };
        }

        var mapped = value(result.Value);
        if (mapped is null)
        {
            return OperationResults.StoreError<T>(new BaseError
            {
                Code = "base.runtime.store.malformedMutationResult",
                Message = "The committed mutation result did not contain its required value.",
                Category = ErrorCategory.Store
            });
        }

        return new OperationResult<T>
        {
            Status = result.Value.Status,
            Value = mapped,
            Warnings = result.Value.Warnings,
            Revision = result.Value.Revision,
            Events = result.Value.Events
        };
    }

    private static bool Allows(
        CollectionDefinition collection,
        Func<CollectionOperationMatrix, bool> operationAllowed,
        out GateFailure gate)
    {
        if (collection.Operations is not null && !operationAllowed(collection.Operations))
        {
            gate = new GateFailure(new BaseError
            {
                Code = "base.runtime.collection.operationDisabled",
                Message = "Collection does not allow this operation.",
                Category = ErrorCategory.Unsupported
            });
            return false;
        }

        gate = default;
        return true;
    }

    private static bool StoreAllows(
        RecordReadCapability capability,
        BaseOperationKind operation,
        out GateFailure gate)
    {
        var supported = operation switch
        {
            BaseOperationKind.List => capability.List,
            BaseOperationKind.Get => capability.Get,
            _ => false
        };
        if (!supported)
        {
            gate = new GateFailure(new BaseError
            {
                Code = "base.runtime.store.operationUnsupported",
                Message = "The registered store does not support this operation.",
                Category = ErrorCategory.Unsupported
            });
            return false;
        }

        gate = default;
        return true;
    }

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

    private static RecordId? TargetId(BaseRecordBatchItem item) =>
        item.Kind == BaseRecordMutationKind.Upsert ? item.Upsert?.Id : item.RecordId;

    private static OperationResult<T> Finish<T>(
        Activity? activity,
        OperationResult<T> result,
        BaseOperationKind operation,
        string collectionId,
        OperationContext context,
        long startedAt) =>
        HPDBaseRuntimeTelemetry.FinishRuntimeOperation(
            activity,
            result,
            operation,
            collectionId,
            context,
            startedAt);

    private static OperationResult<T> Failure<T, TValue>(OperationResult<TValue> result) => new()
    {
        Status = result.Status,
        Error = result.Error,
        Warnings = result.Warnings,
        Diagnostics = result.Diagnostics,
        Revision = result.Revision,
        Events = result.Events
    };

    private readonly record struct PreparedReadStore<T>(
        OperationResult<T>? Result,
        CollectionDefinition? Collection,
        IRecordStore? Store);

    private readonly record struct GateFailure(BaseError Error)
    {
        public OperationResult<T> As<T>() => OperationResults.Unsupported<T>(Error);
    }
}
