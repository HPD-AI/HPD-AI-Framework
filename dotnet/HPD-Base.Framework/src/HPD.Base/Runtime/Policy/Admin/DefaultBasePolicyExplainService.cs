using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultBasePolicyExplainService : IBasePolicyExplainService
{
    private readonly IBaseSchemaProvider _schema;
    private readonly IBaseSchemaValidator _schemaValidator;
    private readonly IRecordStoreResolver _storeResolver;
    private readonly IBaseQueryValidator _queryValidator;
    private readonly IBasePolicyOrchestrator _policy;
    private readonly IBaseResultNormalizer _normalizer;
    private readonly IBaseOperationalFailureMapper _failureMapper;
    private readonly BasePolicyExplainRedactor _redactor;
    private readonly HPDBasePolicyAdminOptions _options;

    /// <summary>Initializes a new instance.</summary>
    public DefaultBasePolicyExplainService(
        IBaseSchemaProvider schema,
        IBaseSchemaValidator schemaValidator,
        IRecordStoreResolver storeResolver,
        IBaseQueryValidator queryValidator,
        IBasePolicyOrchestrator policy,
        IBaseResultNormalizer normalizer,
        IBaseOperationalFailureMapper failureMapper,
        BasePolicyExplainRedactor redactor,
        IOptions<HPDBasePolicyAdminOptions> options)
    {
        _schema = schema;
        _schemaValidator = schemaValidator;
        _storeResolver = storeResolver;
        _queryValidator = queryValidator;
        _policy = policy;
        _normalizer = normalizer;
        _failureMapper = failureMapper;
        _redactor = redactor;
        _options = options.Value;
    }

    /// <summary>Executes the explain async operation.</summary>
    public async ValueTask<OperationResult<BasePolicyExplainResponse>> ExplainAsync(
        BasePolicyExplainRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(operation);

        return await HPDBaseRuntimeTelemetry.TraceRuntimeReadAsync(
            HPDBaseTelemetrySpans.RuntimePolicyExplain,
            BaseOperationKind.AdminInspect,
            request.CollectionId,
            VisibilityLevel.Admin,
            !string.IsNullOrWhiteSpace(operation.CorrelationId),
            countAsHealthRead: false,
            countAsDiagnosticRead: false,
            () => ExplainCoreAsync(request, principal, operation, cancellationToken)).ConfigureAwait(false);
    }

    private async ValueTask<OperationResult<BasePolicyExplainResponse>> ExplainCoreAsync(
        BasePolicyExplainRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken)
    {
        if (!CanExplain(principal, operation))
        {
            return principal.AuthenticationState == PrincipalAuthenticationState.Anonymous
                ? OperationResults.Unauthorized<BasePolicyExplainResponse>(new BaseError
                {
                    Code = "base.policyExplain.unauthorized",
                    Message = "Authentication is required to explain policy.",
                    Category = ErrorCategory.Authentication
                })
                : OperationResults.PolicyDenied<BasePolicyExplainResponse>(new BaseError
                {
                    Code = "base.policyExplain.adminRequired",
                    Message = "Admin authorization is required to explain policy.",
                    Category = ErrorCategory.Authorization
                });
        }

        if (string.IsNullOrWhiteSpace(request.CollectionId))
        {
            return OperationResults.ValidationFailed<BasePolicyExplainResponse>(new BaseError
            {
                Code = "base.policyExplain.collectionId.required",
                Message = "Collection id is required.",
                Category = ErrorCategory.Validation,
                Target = "collectionId"
            });
        }

        var context = Normalize(operation, ToOperationKind(request.Operation), request.CollectionId, request.RecordId);
        var collectionResult = await ResolveCollectionAsync(request.CollectionId, principal, context, cancellationToken).ConfigureAwait(false);
        if (!collectionResult.IsSuccess() || collectionResult.Value is null)
        {
            return Failure<CollectionDefinition>(collectionResult);
        }

        return request.Operation switch
        {
            BasePolicyExplainOperation.Collection => await ExplainCollectionAsync(request, collectionResult.Value, principal, context, cancellationToken).ConfigureAwait(false),
            BasePolicyExplainOperation.Query => await ExplainQueryAsync(request, collectionResult.Value, principal, context, cancellationToken).ConfigureAwait(false),
            BasePolicyExplainOperation.Record => await ExplainRecordAsync(request, collectionResult.Value, principal, context, cancellationToken).ConfigureAwait(false),
            BasePolicyExplainOperation.Create => await ExplainCreateAsync(request, collectionResult.Value, principal, context, cancellationToken).ConfigureAwait(false),
            BasePolicyExplainOperation.Patch => await ExplainPatchAsync(request, collectionResult.Value, principal, context, cancellationToken).ConfigureAwait(false),
            BasePolicyExplainOperation.Replace => await ExplainReplaceAsync(request, collectionResult.Value, principal, context, cancellationToken).ConfigureAwait(false),
            BasePolicyExplainOperation.Delete => await ExplainDeleteAsync(request, collectionResult.Value, principal, context, cancellationToken).ConfigureAwait(false),
            _ => OperationResults.Unsupported<BasePolicyExplainResponse>(UnsupportedOperationError(request.Operation))
        };
    }

    private async ValueTask<OperationResult<BasePolicyExplainResponse>> ExplainCollectionAsync(
        BasePolicyExplainRequest request,
        CollectionDefinition collection,
        PrincipalContext principal,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        var policy = await _policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = collection,
            ResourceKind = PolicyResourceKind.Collection
        }, cancellationToken).ConfigureAwait(false);

        return TargetPolicyResult(request, context, policy, null, new BasePolicyExplainRuntimeSummary(), request.Options);
    }

    private async ValueTask<OperationResult<BasePolicyExplainResponse>> ExplainQueryAsync(
        BasePolicyExplainRequest request,
        CollectionDefinition collection,
        PrincipalContext principal,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        if (!TryResolveStore(collection, context, BaseOperationKind.List, out var store, out var failure))
        {
            return failure;
        }

        var query = request.Query ?? new RecordQuery();
        var queryValidation = await _queryValidator.ValidateAsync(
            collection,
            query,
            store!.Capabilities.Query,
            BaseQueryValidationUsage.ExternalQuery,
            context,
            cancellationToken).ConfigureAwait(false);
        if (!queryValidation.IsSuccess() || queryValidation.Value is null)
        {
            return Failure<ValidatedRecordQuery>(queryValidation);
        }

        var policy = await _policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = collection,
            ResourceKind = PolicyResourceKind.Query,
            Query = queryValidation.Value.Query
        }, cancellationToken).ConfigureAwait(false);

        var includeAst = request.Options?.IncludeConstraintAst == true;
        var effectiveQuery = policy.IsSuccess() && policy.Value is not null
            ? BasePolicyRuntimeSimulation.ComposePolicyFilter(queryValidation.Value.Query, policy.Value.EffectiveRecordFilter)
            : queryValidation.Value.Query;

        if (!ReferenceEquals(effectiveQuery, queryValidation.Value.Query))
        {
            var composedValidation = await _queryValidator.ValidateAsync(
                collection,
                effectiveQuery,
                store.Capabilities.Query,
                BaseQueryValidationUsage.PolicyConstraint,
                context,
                cancellationToken).ConfigureAwait(false);
            if (!composedValidation.IsSuccess() || composedValidation.Value is null)
            {
                return Failure<ValidatedRecordQuery>(composedValidation);
            }

            effectiveQuery = composedValidation.Value.Query;
        }

        var runtime = new BasePolicyExplainRuntimeSummary
        {
            StoreMutationExecuted = false,
            UserFilterPresent = queryValidation.Value.Query.Filter is not null,
            PolicyFilterPresent = policy.Value?.EffectiveRecordFilter is not null,
            EffectiveFilterComposed = effectiveQuery.Filter is not null,
            UserFilter = _redactor.Filter(queryValidation.Value.Query.Filter, includeAst),
            PolicyFilter = _redactor.Filter(policy.Value?.EffectiveRecordFilter, includeAst),
            EffectiveFilter = _redactor.Filter(effectiveQuery.Filter, includeAst)
        };

        return TargetPolicyResult(request, context, policy, null, runtime, request.Options);
    }

    private async ValueTask<OperationResult<BasePolicyExplainResponse>> ExplainRecordAsync(
        BasePolicyExplainRequest request,
        CollectionDefinition collection,
        PrincipalContext principal,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        if (!TryRecordId(request.RecordId, out var id, out var idFailure))
        {
            return idFailure;
        }

        if (!TryResolveStore(collection, context, BaseOperationKind.Get, out var store, out var failure))
        {
            return failure;
        }

        var coarsePolicy = await _policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = collection,
            ResourceKind = PolicyResourceKind.Record,
            RecordId = id
        }, cancellationToken).ConfigureAwait(false);
        if (!coarsePolicy.IsSuccess())
        {
            return TargetPolicyResult(request, context, coarsePolicy, null, new BasePolicyExplainRuntimeSummary(), request.Options);
        }

        var existing = await GetExistingAsync(store!, collection, id, context, cancellationToken).ConfigureAwait(false);
        if (!existing.IsSuccess() || existing.Value is null)
        {
            return existing.Status == OperationStatus.NotFound
                ? OperationResults.Ok(Response(
                    request,
                    context,
                    BasePolicyExplainOutcome.NotFound,
                    decision: null,
                    payload: null,
                    runtime: new BasePolicyExplainRuntimeSummary
                    {
                        StoreMutationExecuted = false,
                        ExistingRecordLookupPerformed = true,
                        ExistingRecordFound = false
                    },
                    options: request.Options))
                : Failure<RecordEnvelope>(existing);
        }

        var candidatePolicy = await _policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = collection,
            ResourceKind = PolicyResourceKind.Record,
            RecordId = id,
            ExistingRecord = existing.Value
        }, cancellationToken).ConfigureAwait(false);

        var runtime = new BasePolicyExplainRuntimeSummary
        {
            StoreMutationExecuted = false,
            ExistingRecordLookupPerformed = true,
            ExistingRecordFound = true,
            CloakedNotFoundWouldBeReturnedToPublic = !candidatePolicy.IsSuccess(),
            HiddenFieldsWouldBeOmitted = candidatePolicy.Value?.EffectiveReadMask is not null
        };

        return TargetPolicyResult(request, context, candidatePolicy, null, runtime, request.Options);
    }

    private async ValueTask<OperationResult<BasePolicyExplainResponse>> ExplainCreateAsync(
        BasePolicyExplainRequest request,
        CollectionDefinition collection,
        PrincipalContext principal,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        if (request.Create is null)
        {
            return MissingPayload("create");
        }

        if (!TryResolveStore(collection, context, BaseOperationKind.Create, out _, out var failure))
        {
            return failure;
        }

        var validation = await _schemaValidator.ValidateCreateAsync(new BasePayloadValidationRequest
        {
            Collection = collection,
            Principal = principal,
            Operation = context,
            Payload = request.Create.Payload
        }, cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess() || validation.Value is null)
        {
            return Failure<BaseValidatedPayload>(validation);
        }

        var policy = await _policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = collection,
            ResourceKind = PolicyResourceKind.CreatePayload,
            ProposedPayload = validation.Value.Payload
        }, cancellationToken).ConfigureAwait(false);

        return WritePolicyResult(request, context, policy, validation.Value.Payload, validation.Value.Payload, new BasePolicyExplainRuntimeSummary(), request.Options);
    }

    private async ValueTask<OperationResult<BasePolicyExplainResponse>> ExplainPatchAsync(
        BasePolicyExplainRequest request,
        CollectionDefinition collection,
        PrincipalContext principal,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        if (request.Patch is null)
        {
            return MissingPayload("patch");
        }

        if (!TryRecordId(request.RecordId, out var id, out var idFailure))
        {
            return idFailure;
        }

        if (!TryResolveStore(collection, context, BaseOperationKind.Patch, out var store, out var failure))
        {
            return failure;
        }

        var validation = await _schemaValidator.ValidatePatchAsync(new BasePayloadValidationRequest
        {
            Collection = collection,
            Principal = principal,
            Operation = context,
            Patch = request.Patch.Patch
        }, cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess() || validation.Value is null)
        {
            return Failure<BaseValidatedPayload>(validation);
        }

        var existing = await GetExistingAsync(store!, collection, id, context, cancellationToken).ConfigureAwait(false);
        if (!existing.IsSuccess() || existing.Value is null)
        {
            return Failure<RecordEnvelope>(existing);
        }

        var proposedPayload = BasePolicyRuntimeSimulation.MergePatchPayload(existing.Value.Payload, validation.Value.Payload);
        var proposedRecord = existing.Value with { Payload = proposedPayload };
        var policy = await _policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = collection,
            ResourceKind = PolicyResourceKind.UpdatePayload,
            RecordId = id,
            ExistingRecord = existing.Value,
            ProposedPayload = proposedPayload,
            ProposedRecord = proposedRecord
        }, cancellationToken).ConfigureAwait(false);

        var runtime = new BasePolicyExplainRuntimeSummary
        {
            StoreMutationExecuted = false,
            ExistingRecordLookupPerformed = true,
            ExistingRecordFound = true,
            ProposedRecordComputed = true
        };

        return WritePolicyResult(request, context, policy, proposedPayload, validation.Value.Payload, runtime, request.Options);
    }

    private async ValueTask<OperationResult<BasePolicyExplainResponse>> ExplainReplaceAsync(
        BasePolicyExplainRequest request,
        CollectionDefinition collection,
        PrincipalContext principal,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        if (request.Replace is null)
        {
            return MissingPayload("replace");
        }

        if (!TryRecordId(request.RecordId, out var id, out var idFailure))
        {
            return idFailure;
        }

        if (!TryResolveStore(collection, context, BaseOperationKind.Replace, out _, out var failure))
        {
            return failure;
        }

        var validation = await _schemaValidator.ValidateReplaceAsync(new BasePayloadValidationRequest
        {
            Collection = collection,
            Principal = principal,
            Operation = context,
            Payload = request.Replace.Payload
        }, cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess() || validation.Value is null)
        {
            return Failure<BaseValidatedPayload>(validation);
        }

        var policy = await _policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = collection,
            ResourceKind = PolicyResourceKind.UpdatePayload,
            RecordId = id,
            ProposedPayload = validation.Value.Payload
        }, cancellationToken).ConfigureAwait(false);

        return WritePolicyResult(request, context, policy, validation.Value.Payload, validation.Value.Payload, new BasePolicyExplainRuntimeSummary(), request.Options);
    }

    private async ValueTask<OperationResult<BasePolicyExplainResponse>> ExplainDeleteAsync(
        BasePolicyExplainRequest request,
        CollectionDefinition collection,
        PrincipalContext principal,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        if (!TryRecordId(request.RecordId, out var id, out var idFailure))
        {
            return idFailure;
        }

        if (!TryResolveStore(collection, context, BaseOperationKind.Delete, out var store, out var failure))
        {
            return failure;
        }

        var existing = await GetExistingAsync(store!, collection, id, context, cancellationToken).ConfigureAwait(false);
        if (!existing.IsSuccess() || existing.Value is null)
        {
            return Failure<RecordEnvelope>(existing);
        }

        var policy = await _policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = context,
            Collection = collection,
            ResourceKind = PolicyResourceKind.DeleteCandidate,
            RecordId = id,
            ExistingRecord = existing.Value
        }, cancellationToken).ConfigureAwait(false);

        var runtime = new BasePolicyExplainRuntimeSummary
        {
            StoreMutationExecuted = false,
            ExistingRecordLookupPerformed = true,
            ExistingRecordFound = true
        };

        return WritePolicyResult(request, context, policy, existing.Value.Payload, null, runtime, request.Options);
    }

    private OperationResult<BasePolicyExplainResponse> WritePolicyResult(
        BasePolicyExplainRequest request,
        OperationContext context,
        OperationResult<BasePolicyEvaluation> policy,
        RecordPayload? predicatePayload,
        RecordPayload? changedPayload,
        BasePolicyExplainRuntimeSummary runtime,
        BasePolicyExplainOptions? options)
    {
        if (policy.IsSuccess() && policy.Value?.Decision.Constraints?.WriteCheck is { } writeCheck)
        {
            var writeCheckResult = BasePolicyWriteConstraintEvaluator.Evaluate(predicatePayload, writeCheck);
            if (writeCheckResult == BasePolicyWriteCheckEvaluation.Unsupported)
            {
                runtime = runtime with { WriteCheckUnsupportedByRuntime = true };
                return OperationResults.Ok(Response(
                    request,
                    context,
                    BasePolicyExplainOutcome.Unsupported,
                    policy.Value.Decision,
                    changedPayload,
                    runtime,
                    options));
            }

            if (writeCheckResult == BasePolicyWriteCheckEvaluation.Denied)
            {
                return OperationResults.Ok(Response(
                    request,
                    context,
                    BasePolicyExplainOutcome.Denied,
                    policy.Value.Decision with
                    {
                        Effect = PolicyEffect.Deny,
                        Outcome = PolicyOutcome.Denied,
                        ReasonCode = "writeCheck",
                        SafeMessage = "Policy write check denied the operation."
                    },
                    changedPayload,
                    runtime,
                    options));
            }
        }

        if (policy.IsSuccess() && policy.Value?.EffectiveWriteMask is { } mask && changedPayload is not null)
        {
            var denied = DeniedByWriteMask(mask, BasePolicyRuntimeSimulation.PayloadFields(changedPayload));
            if (denied is not null)
            {
                return OperationResults.Ok(Response(
                    request,
                    context,
                    BasePolicyExplainOutcome.Denied,
                    policy.Value.Decision with
                    {
                        Effect = PolicyEffect.Deny,
                        Outcome = PolicyOutcome.Denied,
                        ReasonCode = "writeMask",
                        SafeMessage = "Policy write mask does not allow this field to be written."
                    },
                    changedPayload,
                    runtime,
                    options));
            }
        }

        return TargetPolicyResult(request, context, policy, changedPayload, runtime, options);
    }

    private OperationResult<BasePolicyExplainResponse> TargetPolicyResult(
        BasePolicyExplainRequest request,
        OperationContext context,
        OperationResult<BasePolicyEvaluation> policy,
        RecordPayload? payload,
        BasePolicyExplainRuntimeSummary runtime,
        BasePolicyExplainOptions? options)
    {
        if (policy.IsSuccess() && policy.Value is not null)
        {
            return OperationResults.Ok(Response(request, context, Outcome(policy.Value.Decision), policy.Value.Decision, payload, runtime, options));
        }

        if (policy.Status == OperationStatus.PolicyDenied)
        {
            if (string.Equals(policy.Error?.Code, "base.runtime.policy.unavailable", StringComparison.Ordinal))
            {
                return Failure<BasePolicyEvaluation>(policy);
            }

            return OperationResults.Ok(Response(
                request,
                context,
                runtime.CloakedNotFoundWouldBeReturnedToPublic ? BasePolicyExplainOutcome.CloakedNotFound : BasePolicyExplainOutcome.Denied,
                DecisionFromFailure(policy),
                payload,
                runtime,
                options));
        }

        if (policy.Status == OperationStatus.Unsupported)
        {
            return OperationResults.Ok(Response(
                request,
                context,
                BasePolicyExplainOutcome.Unsupported,
                DecisionFromFailure(policy),
                payload,
                runtime,
                options,
                ConstraintSummaryFromFailure(policy)));
        }

        return Failure<BasePolicyEvaluation>(policy);
    }

    private BasePolicyExplainResponse Response(
        BasePolicyExplainRequest request,
        OperationContext context,
        BasePolicyExplainOutcome outcome,
        PolicyDecision? decision,
        RecordPayload? payload,
        BasePolicyExplainRuntimeSummary runtime,
        BasePolicyExplainOptions? options,
        BasePolicyExplainConstraintSummary? constraints = null)
    {
        var includeDiagnosticRefs = options?.IncludeDiagnosticRefs ?? _options.IncludeDiagnosticRefsByDefault;
        return new BasePolicyExplainResponse
        {
            ExplainId = $"exp_{Guid.NewGuid():N}",
            Operation = request.Operation,
            CollectionId = request.CollectionId,
            RecordId = request.RecordId,
            Outcome = outcome,
            Decision = decision is null ? null : _redactor.Decision(decision),
            Runtime = runtime,
            Constraints = constraints ?? (decision is null ? null : _redactor.Constraints(decision, options?.IncludeConstraintAst == true)),
            Redaction = _redactor.Redaction(payload, options?.IncludeRedactedPayloadShape == true),
            DiagnosticRefs = includeDiagnosticRefs ? DiagnosticRefs(runtime) : null,
            Advisory = "Explain results describe policy evaluation at explain time and do not reserve or mutate records.",
            CorrelationId = context.CorrelationId ?? decision?.Audit?.CorrelationId
        };
    }

    private static string[]? DiagnosticRefs(BasePolicyExplainRuntimeSummary runtime)
    {
        var refs = new List<string> { "hpd.base.policy.admin.redactionStrictMode" };
        if (runtime.WriteCheckUnsupportedByRuntime)
        {
            refs.Add("hpd.base.policy.admin.writeCheckRuntimeUnsupported");
        }

        return refs.ToArray();
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
        if (!collection.Enabled || !collection.Exposed)
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

    private bool TryResolveStore(
        CollectionDefinition collection,
        OperationContext context,
        BaseOperationKind operation,
        out IRecordStore? store,
        out OperationResult<BasePolicyExplainResponse> failure)
    {
        store = null;
        failure = default!;

        if (collection.ReadOnly && operation is BaseOperationKind.Create or BaseOperationKind.Patch or BaseOperationKind.Replace or BaseOperationKind.Delete)
        {
            failure = OperationResults.Unsupported<BasePolicyExplainResponse>(new BaseError
            {
                Code = "base.runtime.collection.disabled",
                Message = "Collection is not available for this operation.",
                Category = ErrorCategory.Unsupported,
                Target = collection.Id
            });
            return false;
        }

        if (!CollectionAllows(collection, operation))
        {
            failure = OperationResults.Unsupported<BasePolicyExplainResponse>(new BaseError
            {
                Code = "base.runtime.collection.operationDisabled",
                Message = "Collection does not allow this operation.",
                Category = ErrorCategory.Unsupported,
                Target = collection.Id
            });
            return false;
        }

        var storeResult = _storeResolver.Resolve(collection, context);
        if (!storeResult.IsSuccess() || storeResult.Value is null)
        {
            failure = Failure<IRecordStore>(storeResult);
            return false;
        }

        if (!StoreAllows(
            storeResult.Value.Capabilities.Read,
            storeResult.Value.Capabilities.Mutation,
            operation))
        {
            failure = OperationResults.Unsupported<BasePolicyExplainResponse>(new BaseError
            {
                Code = "base.runtime.store.operationUnsupported",
                Message = "The registered store does not support this operation.",
                Category = ErrorCategory.Unsupported,
                Target = collection.Id
            });
            return false;
        }

        store = storeResult.Value;
        return true;
    }

    private async ValueTask<OperationResult<RecordEnvelope>> GetExistingAsync(
        IRecordStore store,
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await store.GetAsync(collection, id, context, cancellationToken).ConfigureAwait(false);
            return _normalizer.NormalizeStoreResult(result, context);
        }
        catch (Exception exception) when (_failureMapper.TryMap(exception, context, out var error, out var status))
        {
            return new OperationResult<RecordEnvelope> { Status = status, Error = error };
        }
    }

    private bool CanExplain(PrincipalContext principal, OperationContext operation)
    {
        if (operation.Mode is not (OperationMode.Admin or OperationMode.System))
        {
            return false;
        }

        return principal.AuthenticationState switch
        {
            PrincipalAuthenticationState.Admin or PrincipalAuthenticationState.System => true,
            PrincipalAuthenticationState.Service => _options.AllowServicePrincipalExplain,
            _ => false
        };
    }

    private static bool TryRecordId(
        string? value,
        out RecordId id,
        out OperationResult<BasePolicyExplainResponse> failure)
    {
        id = default;
        failure = default!;
        if (!string.IsNullOrWhiteSpace(value))
        {
            id = new RecordId(value);
            return true;
        }

        failure = OperationResults.ValidationFailed<BasePolicyExplainResponse>(new BaseError
        {
            Code = "base.runtime.recordId.invalid",
            Message = "Record id must be non-empty.",
            Category = ErrorCategory.Validation,
            Target = "recordId"
        });
        return false;
    }

    private static OperationContext Normalize(
        OperationContext operation,
        BaseOperationKind kind,
        string collectionId,
        string? recordId) =>
        operation with
        {
            Operation = kind,
            CollectionId = collectionId,
            RecordId = recordId ?? operation.RecordId,
            Now = operation.Now == default ? DateTimeOffset.UtcNow : operation.Now
        };

    private static BaseOperationKind ToOperationKind(BasePolicyExplainOperation operation) =>
        operation switch
        {
            BasePolicyExplainOperation.Query => BaseOperationKind.List,
            BasePolicyExplainOperation.Record => BaseOperationKind.Get,
            BasePolicyExplainOperation.Create => BaseOperationKind.Create,
            BasePolicyExplainOperation.Patch => BaseOperationKind.Patch,
            BasePolicyExplainOperation.Replace => BaseOperationKind.Replace,
            BasePolicyExplainOperation.Delete => BaseOperationKind.Delete,
            _ => BaseOperationKind.AdminInspect
        };

    private static BasePolicyExplainOutcome Outcome(PolicyDecision decision) =>
        decision.Effect switch
        {
            PolicyEffect.Allow when decision.Constraints is not null => BasePolicyExplainOutcome.AllowedWithConstraints,
            PolicyEffect.Allow => BasePolicyExplainOutcome.Allowed,
            PolicyEffect.Deny => BasePolicyExplainOutcome.Denied,
            PolicyEffect.Abstain => BasePolicyExplainOutcome.Allowed,
            _ => BasePolicyExplainOutcome.Denied
        };

    private static PolicyDecision DecisionFromFailure(OperationResult<BasePolicyEvaluation> failure) => new()
    {
        Effect = PolicyEffect.Deny,
        Outcome = failure.Status == OperationStatus.Unsupported ? PolicyOutcome.Unsupported : PolicyOutcome.Denied,
        ReasonCode = failure.Error?.Policy?.ReasonCode ?? failure.Error?.Code,
        SafeMessage = failure.Error?.Message
    };

    private static BasePolicyExplainConstraintSummary? ConstraintSummaryFromFailure(OperationResult<BasePolicyEvaluation> failure) =>
        failure.Error?.Policy?.Obligations is { Length: > 0 } obligations
            ? new BasePolicyExplainConstraintSummary
            {
                Obligations = obligations.Select(static obligation => new BasePolicyExplainObligationSummary
                {
                    Kind = obligation,
                    Code = obligation,
                    Enforcement = ObligationEnforcement.Required
                }).ToArray()
            }
            : null;

    private static string? DeniedByWriteMask(FieldMask mask, string[] fields) =>
        fields.Length == 0
            ? null
            : mask.Mode switch
            {
                FieldMaskMode.Unspecified or FieldMaskMode.AllowAll => null,
                FieldMaskMode.DenyAll => fields[0],
                FieldMaskMode.IncludeOnly => fields.FirstOrDefault(field => !(mask.Include ?? []).Contains(field, StringComparer.Ordinal)),
                FieldMaskMode.Exclude => fields.FirstOrDefault(field => (mask.Exclude ?? []).Contains(field, StringComparer.Ordinal)),
                _ => fields[0]
            };

    private static bool StoreAllows(
        RecordReadCapability read,
        RecordMutationCapability mutation,
        BaseOperationKind operation) =>
        operation switch
        {
            BaseOperationKind.List => read.List,
            BaseOperationKind.Get => read.Get,
            BaseOperationKind.Create => mutation.Create,
            BaseOperationKind.Patch => mutation.Patch,
            BaseOperationKind.Replace => mutation.Replace,
            BaseOperationKind.Delete => mutation.Delete,
            _ => false
        };

    private static bool CollectionAllows(CollectionDefinition collection, BaseOperationKind operation) =>
        collection.Operations is null
        || operation switch
        {
            BaseOperationKind.List => collection.Operations.List,
            BaseOperationKind.Get => collection.Operations.Get,
            BaseOperationKind.Create => collection.Operations.Create,
            BaseOperationKind.Patch => collection.Operations.Patch,
            BaseOperationKind.Replace => collection.Operations.Replace,
            BaseOperationKind.Delete => collection.Operations.Delete,
            _ => true
        };

    private static OperationResult<BasePolicyExplainResponse> MissingPayload(string property) =>
        OperationResults.ValidationFailed<BasePolicyExplainResponse>(new BaseError
        {
            Code = "base.policyExplain.payload.required",
            Message = "The explain request payload is required for this operation.",
            Category = ErrorCategory.Validation,
            Target = property
        });

    private static BaseError UnsupportedOperationError(BasePolicyExplainOperation operation) => new()
    {
        Code = "base.policyExplain.operation.unsupported",
        Message = "The explain operation is not supported.",
        Category = ErrorCategory.Unsupported,
        Target = operation.ToString()
    };

    private static OperationResult<BasePolicyExplainResponse> Failure<TValue>(OperationResult<TValue> result) =>
        new()
        {
            Status = result.Status,
            Error = SanitizeError(result.Error),
            Warnings = result.Warnings,
            Diagnostics = result.Diagnostics,
            Revision = result.Revision,
            Events = result.Events
        };

    private static BaseError? SanitizeError(BaseError? error)
    {
        if (error is null)
        {
            return null;
        }

        return error with
        {
            Validation = error.Validation?.Select(static issue => issue with { RejectedValue = null }).ToArray(),
            Store = null,
            Detail = null,
            Hint = null,
            TraceId = null
        };
    }
}
