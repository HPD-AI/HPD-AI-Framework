using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultBaseRecordRuntime(
    IBaseSchemaProvider schema,
    BaseCollectionRegistry collections,
    IRecordStoreResolver storeResolver,
    IBaseQueryValidator queryValidator,
    IBasePolicyOrchestrator policy,
    IBaseRecordRedactor recordRedactor,
    IBaseResultNormalizer normalizer,
    IBaseOperationalFailureMapper failureMapper,
    IBaseMutationCoordinator mutations,
    IServiceProvider services,
    IOptions<HPDBaseRelationalOptions> relationalOptions,
    ILogger<DefaultBaseRecordRuntime> logger) : IBaseRecordRuntime
{
    private readonly HPDBaseRelationalOptions _relationalOptions = relationalOptions.Value;
    /// <summary>Executes the list async operation.</summary>
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

        OperationResult<RecordPage> result;
        if (composedQuery.Include is { Length: > 0 })
        {
            result = await ExecuteIncludesAsync(
                collection, composedQuery, principal, context, storeResult.Value,
                policyResult.Value, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var providerQuery = BaseQueryFieldResolver.ToStoredNames(collection, composedQuery);
            result = await InvokeStoreAsync(
                () => storeResult.Value.ListAsync(collection, providerQuery, context, cancellationToken),
                context).ConfigureAwait(false);
        }
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

    private async ValueTask<OperationResult<RecordPage>> ExecuteIncludesAsync(
        CollectionDefinition root,
        RecordQuery query,
        PrincipalContext principal,
        OperationContext operation,
        IRecordStore store,
        BasePolicyEvaluation? rootPolicy,
        CancellationToken cancellationToken)
    {
        using HPDBaseRelationalTelemetry.Scope telemetry = HPDBaseRelationalTelemetry.StartRelational(
            HPDBaseTelemetrySpans.RelationInclude, "include", 1, query.Include?.Length ?? 0);
        if (store is not IConsistentRecordIncludeStore includes || !includes.Includes.Supported || !includes.Includes.SnapshotConsistency)
            return OperationResults.Unsupported<RecordPage>(new BaseError { Code = "base.include.unsupported", Message = "The selected store cannot execute snapshot-consistent includes.", Category = ErrorCategory.Unsupported });
        var policies = new Dictionary<string, RecordIncludeSourcePolicy>(StringComparer.Ordinal)
        {
            [root.Id] = new RecordIncludeSourcePolicy
            {
                CollectionId = root.Id,
                Filter = rootPolicy?.EffectiveRecordFilter,
                ReadMask = rootPolicy?.EffectiveReadMask,
                VisibleFieldIds = (root.Fields ?? []).Select(static field => field.Id).ToArray(),
            },
        };
        OperationResult<RecordPage>? validation = await ResolveIncludePoliciesAsync(root, query.Include!, principal, operation, store, policies, 1, [0], cancellationToken).ConfigureAwait(false);
        if (validation is not null) return validation;
        IHPDBaseApplication? application = services.GetService<IHPDBaseApplication>();
        long generation = application?.CurrentReadiness.SchemaGeneration ?? 0;
        OperationResult<RecordIncludeExecutionResult> executed;
        try
        {
            executed = await includes.ExecuteIncludeAsync(new RecordIncludeExecutionRequest
            {
                RootCollection = root,
                RootQuery = query,
                IncludePlan = query.Include!,
                SourcePolicies = policies.Values.ToArray(),
                Operation = operation,
                AcquisitionTimeout = _relationalOptions.SnapshotAcquisitionTimeout,
                ExecutionTimeout = _relationalOptions.MaxExecutionDuration,
                MaxResultRows = Math.Min(_relationalOptions.MaxIncludedRecords, includes.Includes.MaxRecords),
                MaxResultBytes = _relationalOptions.MaxResultBytes,
            }, cancellationToken).AsTask().WaitAsync(_relationalOptions.MaxExecutionDuration, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        { return OperationResults.StoreError<RecordPage>(new BaseError { Code = "base.include.limitExceeded", Message = "Include execution exceeded its bounded lifetime.", Category = ErrorCategory.Store }); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return OperationResults.StoreError<RecordPage>(new BaseError { Code = "base.include.limitExceeded", Message = "Include execution exceeded its bounded lifetime.", Category = ErrorCategory.Store }); }
        catch when (!cancellationToken.IsCancellationRequested)
        { return OperationResults.StoreError<RecordPage>(new BaseError { Code = "base.include.invalid", Message = "Include execution failed.", Category = ErrorCategory.Store }); }
        if (!executed.IsSuccess() || executed.Value is null)
        {
            string code = executed.Error?.Code switch
            {
                "base.include.limitExceeded" => "base.include.limitExceeded",
                "base.include.snapshotUnsupported" => "base.include.snapshotUnsupported",
                "base.include.unsupported" => "base.include.unsupported",
                _ => "base.include.invalid",
            };
            var error = new BaseError
            {
                Code = code,
                Message = "Include execution failed.",
                Category = code is "base.include.unsupported" or "base.include.snapshotUnsupported"
                    ? ErrorCategory.Unsupported
                    : ErrorCategory.Store,
            };
            return code == "base.include.unsupported"
                ? OperationResults.Unsupported<RecordPage>(error)
                : code == "base.include.snapshotUnsupported"
                    ? OperationResults.CapabilityUnavailable<RecordPage>(error)
                    : OperationResults.StoreError<RecordPage>(error);
        }
        IncludeResultValidation resultValidation = ValidateIncludeResult(
            executed.Value.Page,
            root,
            query.Include!,
            policies,
            Math.Min(_relationalOptions.MaxIncludedRecords, includes.Includes.MaxRecords),
            _relationalOptions.MaxResultBytes);
        if (resultValidation != IncludeResultValidation.Valid)
            return OperationResults.StoreError<RecordPage>(new BaseError
            {
                Code = resultValidation == IncludeResultValidation.LimitExceeded ? "base.include.limitExceeded" : "base.include.invalid",
                Message = "The include provider returned an invalid result.",
                Category = ErrorCategory.Store,
            });
        if (generation != 0 && (executed.Value.SchemaGeneration != generation || application?.CurrentReadiness.SchemaGeneration != generation))
            return OperationResults.CapabilityUnavailable<RecordPage>(new BaseError { Code = "base.include.snapshotUnsupported", Message = "The include schema generation is not ready.", Category = ErrorCategory.Capability });
        if (!ValidIncludeEvidence(executed.Value.DependencyEvidence, policies.Keys, Math.Min(_relationalOptions.MaxIncludedRecords, includes.Includes.MaxRecords)))
            return OperationResults.StoreError<RecordPage>(new BaseError { Code = "base.relational.dependencies.invalid", Message = "The include dependency evidence is invalid.", Category = ErrorCategory.Store });
        OperationResult<RecordPage> completed = OperationResults.Ok(executed.Value.Page);
        telemetry.SetOutcome(completed.Status);
        return completed;
    }

    private IncludeResultValidation ValidateIncludeResult(
        RecordPage page,
        CollectionDefinition rootCollection,
        RecordInclude[] plan,
        IReadOnlyDictionary<string, RecordIncludeSourcePolicy> policies,
        int maxRecords,
        int maxBytes)
    {
        if (page.Items is null || page.Page.Limit is < 0 || page.Page.Offset is < 0 || page.Page.Page is < 0 || page.Page.PerPage is < 0 || page.Count?.Total < 0)
            return IncludeResultValidation.Invalid;
        int records = 0;
        long bytes = 0;
        IncludeResultValidation Visit(RecordEnvelope record, CollectionDefinition collection, RecordInclude[] expected, bool included)
        {
            if (included && ++records > maxRecords) return IncludeResultValidation.LimitExceeded;
            foreach ((string key, System.Text.Json.JsonElement value) in record.Payload.Fields ?? [])
            {
                bytes += key.Length * 2L + value.GetRawText().Length * 2L;
                if (bytes > maxBytes) return IncludeResultValidation.LimitExceeded;
            }
            RecordIncludeResult[] actual = record.Includes ?? [];
            if (actual.Length != expected.Length) return IncludeResultValidation.Invalid;
            for (int index = 0; index < expected.Length; index++)
            {
                RecordInclude requested = expected[index];
                RecordIncludeResult result = actual[index];
                if (!string.Equals(result.NavigationId, requested.NavigationId, StringComparison.Ordinal)) return IncludeResultValidation.Invalid;
                IncludeTargetResolution resolved = IncludeTarget(collection, requested.NavigationId);
                CollectionDefinition? target = resolved.Target;
                if (target is null || !policies.TryGetValue(target.Id, out RecordIncludeSourcePolicy? targetPolicy))
                    return IncludeResultValidation.Invalid;
                RecordEnvelope[] children = result.Kind switch
                {
                    RecordIncludeKind.None when result.Record is null && result.Records is null => [],
                    RecordIncludeKind.One when result.Record is not null && result.Records is null => [result.Record],
                    RecordIncludeKind.Many when result.Record is null && result.Records is not null => result.Records,
                    _ => null!,
                };
                if (children is null || requested.Limit is { } limit && children.Length > limit) return IncludeResultValidation.Invalid;
                if (resolved.Many && result.Kind != RecordIncludeKind.Many || !resolved.Many && result.Kind is not (RecordIncludeKind.None or RecordIncludeKind.One))
                    return IncludeResultValidation.Invalid;
                if (targetPolicy.Denied && (children.Length != 0 || result.Kind != (resolved.Many ? RecordIncludeKind.Many : RecordIncludeKind.None)))
                    return IncludeResultValidation.Invalid;
                HashSet<string> allowedNames = AllowedIncludePayloadNames(target, requested, targetPolicy);
                foreach (RecordEnvelope child in children)
                {
                    if (!string.Equals(child.CollectionId, target.Id, StringComparison.Ordinal))
                        return IncludeResultValidation.Invalid;
                    if ((child.Payload.Fields ?? []).Keys.Any(key => !allowedNames.Contains(key)))
                        return IncludeResultValidation.Invalid;
                    IncludeResultValidation childValidation = Visit(child, target, requested.Includes ?? [], included: true);
                    if (childValidation != IncludeResultValidation.Valid) return childValidation;
                }
            }
            return IncludeResultValidation.Valid;
        }
        foreach (RecordEnvelope rootRecord in page.Items)
        {
            IncludeResultValidation rootValidation = Visit(rootRecord, rootCollection, plan, included: false);
            if (rootValidation != IncludeResultValidation.Valid) return rootValidation;
        }
        return IncludeResultValidation.Valid;
    }

    private IncludeTargetResolution IncludeTarget(CollectionDefinition parent, string navigationId)
    {
        RelationDefinition? relation = (parent.Fields ?? [])
            .Select(static field => field.Relation)
            .FirstOrDefault(candidate => candidate is not null &&
                (candidate.Id == navigationId || candidate.SourceFieldId == navigationId));
        string? targetId = relation?.TargetCollectionId;
        bool many = relation?.LocalMultiplicity == BaseRelationMultiplicity.Many;
        if (relation is null)
        {
            relation = collections.Collections.Values
                .SelectMany(static collection => collection.Fields ?? [])
                .Select(static field => field.Relation)
                .FirstOrDefault(candidate => candidate is not null &&
                    candidate.TargetCollectionId == parent.Id &&
                    candidate.InverseNavigationId == navigationId);
            targetId = relation?.SourceCollectionId;
            many = relation?.InverseMultiplicity == BaseRelationMultiplicity.Many;
        }
        return targetId is not null && collections.Collections.TryGetValue(targetId, out CollectionDefinition? target)
            ? new IncludeTargetResolution(target, many)
            : default;
    }

    private readonly record struct IncludeTargetResolution(CollectionDefinition? Target, bool Many);

    private static HashSet<string> AllowedIncludePayloadNames(
        CollectionDefinition collection,
        RecordInclude include,
        RecordIncludeSourcePolicy policy)
    {
        HashSet<string> visible = policy.VisibleFieldIds.ToHashSet(StringComparer.Ordinal);
        IEnumerable<FieldDefinition> fields = (collection.Fields ?? []).Where(field => visible.Contains(field.Id));
        fields = policy.ReadMask?.Mode switch
        {
            FieldMaskMode.DenyAll => [],
            FieldMaskMode.IncludeOnly => fields.Where(field => (policy.ReadMask.Include ?? []).Contains(field.Id)),
            FieldMaskMode.Exclude => fields.Where(field => !(policy.ReadMask.Exclude ?? []).Contains(field.Id)),
            _ => fields,
        };
        if (include.SelectFieldIds is { } selected)
            fields = fields.Where(field => selected.Contains(field.Id));
        return fields.Select(static field => field.WireName).ToHashSet(StringComparer.Ordinal);
    }

    private static bool ValidIncludeEvidence(BaseReadDependencyEvidence[]? evidence, IEnumerable<string> requiredCollections, int maxRecords)
    {
        if (evidence is null) return false;
        HashSet<string> required = requiredCollections.ToHashSet(StringComparer.Ordinal);
        if (evidence.Length < required.Count || evidence.Length > maxRecords + required.Count) return false;
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (BaseReadDependencyEvidence item in evidence)
        {
            if (string.IsNullOrWhiteSpace(item.CollectionId) || item.CollectionId.Length > 128 ||
                !required.Contains(item.CollectionId) || item.RecordId is { Length: > 512 } ||
                !entries.Add(item.CollectionId + "\0" + item.RecordId))
                return false;
            observed.Add(item.CollectionId);
        }
        return observed.SetEquals(required);
    }

    private enum IncludeResultValidation { Valid, Invalid, LimitExceeded }

    private async ValueTask<OperationResult<RecordPage>?> ResolveIncludePoliciesAsync(
        CollectionDefinition parent,
        RecordInclude[] includes,
        PrincipalContext principal,
        OperationContext operation,
        IRecordStore expectedStore,
        Dictionary<string, RecordIncludeSourcePolicy> policies,
        int depth,
        int[] includeCount,
        CancellationToken cancellationToken)
    {
        if (depth > _relationalOptions.MaxIncludeDepth) return IncludeValidation("base.include.limitExceeded", "Include depth exceeds the configured limit.");
        foreach (RecordInclude include in includes)
        {
            if (++includeCount[0] > _relationalOptions.MaxIncludes) return IncludeValidation("base.include.limitExceeded", "Include count exceeds the configured limit.");
            RelationDefinition? relation = (parent.Fields ?? []).Select(static field => field.Relation).FirstOrDefault(candidate => candidate is not null && (candidate.Id == include.NavigationId || candidate.SourceFieldId == include.NavigationId));
            bool inverse = false;
            if (relation is null)
            {
                relation = collections.Collections.Values.SelectMany(static collection => collection.Fields ?? []).Select(static field => field.Relation)
                    .FirstOrDefault(candidate => candidate is not null && candidate.TargetCollectionId == parent.Id && candidate.InverseNavigationId == include.NavigationId);
                inverse = relation is not null;
            }
            if (relation is null || relation.Include?.Allowed != true) return IncludeValidation("base.include.invalid", "The requested relation cannot be included.");
            if (relation.Include.MaxDepth is { } relationDepth && depth > relationDepth) return IncludeValidation("base.include.limitExceeded", "Include depth exceeds the relation limit.");
            if (include.Filter is not null && relation.Include.FilterAllowed != true) return IncludeValidation("base.include.unsupported", "The relation does not permit include filtering.");
            if (include.Sort is { Length: > 0 } && relation.Include.SortAllowed != true) return IncludeValidation("base.include.unsupported", "The relation does not permit include sorting.");
            if (include.Limit is <= 0 || include.Limit > _relationalOptions.MaxIncludedRecordsPerParent) return IncludeValidation("base.include.limitExceeded", "The per-parent include limit is invalid.");
            string targetId = inverse ? relation.SourceCollectionId : relation.TargetCollectionId;
            OperationContext targetOperation = operation with { CollectionId = targetId };
            OperationResult<CollectionDefinition> targetResult = await schema.GetCollectionAsync(targetId, principal, targetOperation, BasePolicyRuntimeSimulation.ViewFor(principal, targetOperation), cancellationToken).ConfigureAwait(false);
            if (!targetResult.IsSuccess() || targetResult.Value is null) return Failure<RecordPage, CollectionDefinition>(targetResult);
            if (inverse && !(targetResult.Value.Fields ?? []).Any(field =>
                    field.Relation is { } visibleRelation &&
                    visibleRelation.Id == relation.Id &&
                    visibleRelation.InverseNavigationId == include.NavigationId))
                return IncludeValidation("base.include.invalid", "The requested relation cannot be included.");
            HashSet<string> targetFields = (targetResult.Value.Fields ?? []).Select(static field => field.Id).ToHashSet(StringComparer.Ordinal);
            if ((include.SelectFieldIds ?? []).Any(field => !targetFields.Contains(field)) ||
                (include.Sort ?? []).Any(sort => !targetFields.Contains(sort.Field)) ||
                !IncludeFilterFieldsValid(include.Filter, targetFields))
                return IncludeValidation("base.include.invalid", "The include references an unknown target field.");
            OperationResult<IRecordStore> targetStore = storeResolver.Resolve(targetResult.Value, targetOperation);
            if (!targetStore.IsSuccess() || !ReferenceEquals(targetStore.Value, expectedStore)) return IncludeValidation("base.include.snapshotUnsupported", "Includes require one store instance.");
            OperationResult<BasePolicyEvaluation> targetPolicy = await EvaluateReadPolicyAsync(new BasePolicyRequest { Principal = principal, Operation = targetOperation, Collection = targetResult.Value, ResourceKind = PolicyResourceKind.Query }, cancellationToken).ConfigureAwait(false);
            if (targetPolicy.Status == OperationStatus.PolicyDenied)
            {
                policies[targetId] = new RecordIncludeSourcePolicy
                {
                    CollectionId = targetId,
                    VisibleFieldIds = targetFields.ToArray(),
                    Denied = true,
                };
            }
            else if (!targetPolicy.IsSuccess() || targetPolicy.Value is null)
                return OperationResults.Unsupported<RecordPage>(new BaseError
                {
                    Code = "base.include.policyUnsupported",
                    Message = "Include policy evaluation could not be enforced.",
                    Category = ErrorCategory.Unsupported,
                });
            else
            {
                policies[targetId] = new RecordIncludeSourcePolicy
                {
                    CollectionId = targetId,
                    Filter = targetPolicy.Value.EffectiveRecordFilter,
                    ReadMask = targetPolicy.Value.EffectiveReadMask,
                    VisibleFieldIds = targetFields.ToArray(),
                };
            }
            if (include.Includes is { Length: > 0 } && await ResolveIncludePoliciesAsync(targetResult.Value, include.Includes, principal, targetOperation, expectedStore, policies, depth + 1, includeCount, cancellationToken).ConfigureAwait(false) is { } failure) return failure;
        }
        return null;
    }

    private static OperationResult<RecordPage> IncludeValidation(string code, string message) => OperationResults.ValidationFailed<RecordPage>(new BaseError { Code = code, Message = message, Category = ErrorCategory.Validation });
    private static bool IncludeFilterFieldsValid(FilterExpression? filter, HashSet<string> fields) => filter is null ||
        (filter.Field is null || fields.Contains(filter.Field)) && (filter.Children ?? []).All(child => IncludeFilterFieldsValid(child, fields));

    /// <summary>Executes the get async operation.</summary>
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

    /// <summary>Executes the create async operation.</summary>
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

    /// <summary>Executes the patch async operation.</summary>
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

    /// <summary>Executes the replace async operation.</summary>
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

    /// <summary>Executes the delete async operation.</summary>
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

    /// <summary>Executes the upsert async operation.</summary>
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

    /// <summary>Executes the batch async operation.</summary>
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
        if (!result.Value.Enabled || !result.Value.Exposed && !result.Value.System)
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
        /// <summary>Executes the as operation.</summary>
        public OperationResult<T> As<T>() => OperationResults.Unsupported<T>(Error);
    }
}
