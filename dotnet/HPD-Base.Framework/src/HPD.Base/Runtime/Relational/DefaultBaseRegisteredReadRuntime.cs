using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultBaseRegisteredReadRuntime(
    BaseReadRegistry registry,
    BaseCollectionRegistry collections,
    IRecordStoreRegistry stores,
    IBasePolicyOrchestrator policy,
    IServiceProvider services,
    IOptions<HPDBaseRelationalOptions> options) : IBaseRegisteredReadRuntime
{
    private readonly HPDBaseRelationalOptions _options = options.Value;

    /// <summary>Executes the execute async operation.</summary>
    public async ValueTask<OperationResult<BaseRegisteredReadEvaluation<TRow>>> ExecuteAsync<TParameters, TRow>(
        BaseReadDefinition<TParameters, TRow> definition,
        TParameters parameters,
        BaseReadPageRequest? page,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using HPDBaseRelationalTelemetry.Scope telemetry = HPDBaseRelationalTelemetry.StartRelational(
            HPDBaseTelemetrySpans.RelationalRead, "read", definition.Plan.Sources.Length, definition.Plan.Joins.Length);
        IHPDBaseApplication? application = services.GetService<IHPDBaseApplication>();
        BaseApplicationReadiness? readiness = application?.CurrentReadiness;
        if (readiness is not null && (readiness.State != BaseApplicationReadinessState.Ready || readiness.SchemaGeneration is null))
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.application.notReady", "HPD.BASE is not ready.");
        long generation = readiness?.SchemaGeneration ?? definition.Plan.SchemaGeneration;
        if (!registry.Registrations.TryGetValue(definition.Id, out IBaseReadRegistration? registered) ||
            !ReferenceEquals(registered, definition))
            return Failure<TRow>(OperationStatus.NotFound, "base.relational.read.notFound", "The registered read handle is not installed.");
        if (page is { } requested && (requested.Page < 1 || requested.PerPage < 1 || requested.PerPage > _options.MaxPageSize))
            return Failure<TRow>(OperationStatus.ValidationFailed, "base.relational.read.invalid", "The registered read page is invalid.");

        IRecordStore? selected = null;
        foreach (BaseRelationalReadSource source in definition.Plan.Sources)
        {
            IRecordStore? store = stores.GetStoreForCollection(source.CollectionId);
            if (store is null)
                return Failure<TRow>(OperationStatus.NotFound, "base.relational.read.notFound", "A registered read source is unavailable.");
            if (selected is not null && !ReferenceEquals(selected, store))
                return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.multipleStores", "Registered reads require one store instance.");
            selected = store;
        }

        if (selected is not IRelationalReadStore relational || !relational.RelationalReads.Supported)
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.unsupported", "The selected store cannot execute this registered read.");
        if (!relational.RelationalReads.SnapshotConsistency)
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.snapshotUnavailable", "The selected store cannot provide the required snapshot.");
        if (!relational.RelationalReads.CompleteDependencyEvidence)
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.unsupported", "The selected store cannot provide complete dependency evidence.");
        if (!Supports(definition.Plan, relational.RelationalReads, _options))
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.unsupported", "The selected store cannot execute this registered read.");

        var sourcePolicies = new List<BaseRelationalReadSourcePolicy>(definition.Plan.Sources.Length);
        foreach (BaseRelationalReadSource source in definition.Plan.Sources)
        {
            OperationContext sourceOperation = operation with { CollectionId = source.CollectionId };
            if (!collections.Collections.TryGetValue(source.CollectionId, out CollectionDefinition? collection))
                return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.schemaNotReady", "A registered read source schema is not ready.");
            OperationResult<BasePolicyEvaluation> policyResult;
            try
            {
                policyResult = await policy.EvaluateReadAsync(new BasePolicyRequest
                {
                    Principal = principal,
                    Operation = sourceOperation,
                    Collection = collection,
                    ResourceKind = PolicyResourceKind.Query,
                }, cancellationToken).ConfigureAwait(false);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                return Failure<TRow>(OperationStatus.PolicyDenied, "base.relational.read.policyUnsupported", "Registered read policy evaluation failed.");
            }
            if (!policyResult.IsSuccess() || policyResult.Value is null)
                return Failure<TRow>(OperationStatus.PolicyDenied, "base.relational.read.policyUnsupported", "Registered read policy evaluation failed.");
            sourcePolicies.Add(new BaseRelationalReadSourcePolicy
            {
                SourceId = source.Id,
                CollectionId = source.CollectionId,
                Filter = policyResult.Value.EffectiveRecordFilter,
                ReadMask = policyResult.Value.EffectiveReadMask,
            });
        }
        if (!InfluenceAllowed(definition.Plan, sourcePolicies))
            return Failure<TRow>(OperationStatus.PolicyDenied, "base.relational.read.policyUnsupported", "Policy denied the registered read.");

        BaseRelationalParameterValue[] encoded;
        try { encoded = definition.ParameterCodec.Encode(parameters); }
        catch { return Failure<TRow>(OperationStatus.ValidationFailed, "base.relational.read.invalid", "Registered read parameters are invalid."); }
        if (!ValidateParameters(definition.Plan.Parameters, encoded, _options, relational.RelationalReads))
            return Failure<TRow>(OperationStatus.ValidationFailed, "base.relational.read.invalid", "Registered read parameters are invalid.");
        if (!SemanticTypesValid(definition.Plan, encoded, collections.Collections))
            return Failure<TRow>(OperationStatus.ValidationFailed, "base.relational.read.invalid", "Registered read parameter types are incompatible with the registered plan.");

        var plan = definition.Plan with { Page = page, SchemaGeneration = generation };
        var request = new BaseRelationalReadExecutionRequest
        {
            Plan = plan,
            ParameterValues = encoded,
            SourcePolicies = sourcePolicies.ToArray(),
            Operation = operation,
            AcquisitionTimeout = _options.SnapshotAcquisitionTimeout,
            ExecutionTimeout = _options.MaxExecutionDuration,
            MaxResultRows = Math.Min(_options.MaxResultRows, relational.RelationalReads.MaxResultRows),
            MaxResultBytes = Math.Min(_options.MaxResultBytes, relational.RelationalReads.MaxResultBytes),
        };

        OperationResult<BaseRelationalReadExecutionResult> result;
        try
        {
            result = await relational.ExecuteReadAsync(request, cancellationToken)
                .AsTask().WaitAsync(_options.MaxExecutionDuration, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return Failure<TRow>(OperationStatus.StoreError, "base.relational.read.timeout", "Registered read execution timed out.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure<TRow>(OperationStatus.StoreError, "base.relational.read.timeout", "Registered read execution timed out.");
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return Failure<TRow>(OperationStatus.StoreError, "base.relational.read.resultInvalid", "Registered read execution failed.");
        }

        if (!result.IsSuccess() || result.Value is null)
        {
            string code = result.Error?.Code switch
            {
                "base.relational.read.limitExceeded" => "base.relational.read.limitExceeded",
                "base.relational.read.timeout" => "base.relational.read.timeout",
                "base.relational.read.schemaNotReady" => "base.relational.read.schemaNotReady",
                "base.relational.read.snapshotUnavailable" => "base.relational.read.snapshotUnavailable",
                "base.relational.read.unsupported" => "base.relational.read.unsupported",
                _ when result.Status == OperationStatus.CapabilityUnavailable => "base.relational.read.unsupported",
                _ => "base.relational.read.resultInvalid",
            };
            return Failure<TRow>(result.Status, code, "Registered read execution failed.");
        }
        BaseRelationalReadExecutionResult execution = result.Value;
        BaseApplicationReadiness? completedReadiness = application?.CurrentReadiness;
        if (execution.Result.SchemaGeneration != generation ||
            (completedReadiness is not null &&
                (completedReadiness.State != BaseApplicationReadinessState.Ready || completedReadiness.SchemaGeneration != generation)))
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.schemaNotReady", "The registered read schema generation is not ready.");
        ResultValidation resultValidation = ValidateResult(plan, execution.Result, request, relational.RelationalReads, _options);
        if (resultValidation != ResultValidation.Valid)
            return Failure<TRow>(OperationStatus.StoreError,
                resultValidation == ResultValidation.LimitExceeded ? "base.relational.read.limitExceeded" : "base.relational.read.resultInvalid",
                "The provider returned an invalid registered read result.");

        TRow[] rows;
        try { rows = execution.Result.Rows.Select(definition.RowCodec.Decode).ToArray(); }
        catch { return Failure<TRow>(OperationStatus.StoreError, "base.relational.read.resultInvalid", "The provider returned an invalid registered read result."); }

        if (!ValidateEvidence(plan, execution.DependencyEvidence, request))
            return Failure<TRow>(OperationStatus.StoreError, "base.relational.dependencies.invalid", "The provider returned invalid dependency evidence.");

        BaseDependencyReference[] protectedDependencies;
        try
        {
            protectedDependencies = ProtectDependencies(execution.DependencyEvidence, services.GetService<IBaseDependencyReferenceFactory>(), operation.TenantId);
        }
        catch
        {
            return Failure<TRow>(OperationStatus.StoreError, "base.relational.dependencies.invalid", "The provider returned invalid dependency evidence.");
        }

        var completed = new OperationResult<BaseRegisteredReadEvaluation<TRow>>
        {
            Status = result.Status,
            Value = new BaseRegisteredReadEvaluation<TRow>
            {
                Page = new BasePage<TRow> { Items = rows, Page = execution.Result.Page, Count = execution.Result.Count is { } count ? new CountInfo { Mode = QueryCountMode.Exact, Total = count, IsExact = true } : null },
                Dependencies = new BaseDependencySet { References = protectedDependencies },
            },
            Warnings = result.Warnings,
            Diagnostics = result.Diagnostics,
        };
        telemetry.SetOutcome(completed.Status);
        return completed;
    }

    private static ResultValidation ValidateResult(
        BaseRelationalReadPlan plan,
        BaseRelationalReadResult result,
        BaseRelationalReadExecutionRequest request,
        RelationalReadCapability capability,
        HPDBaseRelationalOptions options)
    {
        string[] expected = plan.Projection.Select(static projection => projection.FieldId).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        if (result.Rows.Length > request.MaxResultRows) return ResultValidation.LimitExceeded;
        if (result.Count < 0 || result.Page.Limit is < 0 || result.Page.Offset is < 0 || result.Page.Page is < 0 || result.Page.PerPage is < 0)
            return ResultValidation.Invalid;
        long bytes = 0;
        foreach (BaseRelationalRow row in result.Rows)
        {
            if (!row.Fields.Select(static field => field.FieldId).OrderBy(static id => id, StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal))
                return ResultValidation.Invalid;
            foreach (BaseRelationalFieldValue field in row.Fields)
            {
                if (!ValidValue(field.Value, options, capability, allowArray: true)) return ResultValidation.Invalid;
                bytes += field.FieldId.Length * 2L + ValueBytes(field.Value);
                if (bytes > request.MaxResultBytes) return ResultValidation.LimitExceeded;
            }
        }
        return ResultValidation.Valid;
    }

    private enum ResultValidation { Valid, Invalid, LimitExceeded }

    private static bool ValidateEvidence(
        BaseRelationalReadPlan plan,
        BaseReadDependencyEvidence[] evidence,
        BaseRelationalReadExecutionRequest request)
    {
        if (evidence is null) return false;
        HashSet<string> allowed = plan.Sources.Select(static source => source.CollectionId).ToHashSet(StringComparer.Ordinal);
        if (evidence.Length < allowed.Count || evidence.Length > request.MaxResultRows + allowed.Count) return false;
        var contributing = new HashSet<string>(StringComparer.Ordinal);
        var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (BaseReadDependencyEvidence item in evidence)
        {
            if (string.IsNullOrWhiteSpace(item.CollectionId) || item.CollectionId.Length > 128 ||
                !allowed.Contains(item.CollectionId) || item.RecordId is { Length: > 512 } ||
                !entries.Add(item.CollectionId + "\0" + item.RecordId))
                return false;
            contributing.Add(item.CollectionId);
        }
        return contributing.SetEquals(allowed);
    }

    private static long ValueBytes(QueryValue value) => value.Kind switch
    {
        QueryValueKind.String => (value.String?.Length ?? 0) * 2L,
        QueryValueKind.Id => (value.Id?.Length ?? 0) * 2L,
        QueryValueKind.Decimal => (value.Decimal?.Length ?? 0) * 2L,
        QueryValueKind.Array => (value.Array ?? []).Sum(ValueBytes),
        _ => 16,
    };

    private static BaseDependencyReference[] ProtectDependencies(
        BaseReadDependencyEvidence[] evidence,
        IBaseDependencyReferenceFactory? factory,
        string? tenantId)
    {
        if (evidence is null || evidence.Any(static item => string.IsNullOrWhiteSpace(item.CollectionId) || item.CollectionId.Length > 128 || item.RecordId is { Length: > 512 }))
            throw new InvalidOperationException();
        if (factory is null) return [];
        return evidence.Select(item => item.RecordId is null
                ? factory.Create(BaseDependencyIds.Collection,
                    new BaseDependencyParameter("tenant", tenantId),
                    new BaseDependencyParameter("collection", item.CollectionId))
                : factory.Create(BaseDependencyIds.Record,
                    new BaseDependencyParameter("tenant", tenantId),
                    new BaseDependencyParameter("collection", item.CollectionId),
                    new BaseDependencyParameter("record", item.RecordId)))
            .DistinctBy(static reference => (reference.TemplateId, reference.Value))
            .ToArray();
    }

    private static bool Supports(
        BaseRelationalReadPlan plan,
        RelationalReadCapability capability,
        HPDBaseRelationalOptions options)
    {
        if (plan.Sources.Length == 0 || plan.Sources.Length > Math.Min(options.MaxSources, capability.MaxSources) ||
            plan.Joins.Length > Math.Min(options.MaxJoins, capability.MaxJoins) ||
            plan.GroupKeys.Length > Math.Min(options.MaxGroupKeys, capability.MaxGroupKeys) ||
            plan.Aggregates.Length > Math.Min(options.MaxAggregates, capability.MaxAggregates) ||
            plan.Projection.Length == 0 || plan.Projection.Length > Math.Min(options.MaxProjectionFields, capability.MaxProjectionFields) ||
            plan.Sort.Length > Math.Min(options.MaxSortFields, capability.MaxSortFields) ||
            plan.Budgets.MaxResultRows > Math.Min(options.MaxResultRows, capability.MaxResultRows) ||
            plan.Budgets.MaxResultBytes > Math.Min(options.MaxResultBytes, capability.MaxResultBytes) ||
            plan.Joins.Any(join => !capability.JoinKinds.Contains(join.Kind)) ||
            plan.Aggregates.Any(aggregate => !capability.AggregateKinds.Contains(aggregate.Kind)))
            return false;

        int predicateNodes = CountPredicate(plan.Predicate) + CountPredicate(plan.Having);
        if (predicateNodes > Math.Min(options.MaxPredicateNodes, capability.MaxPredicateNodes))
            return false;
        return PredicatesSupported(plan.Predicate, capability) && PredicatesSupported(plan.Having, capability);
    }

    private static bool InfluenceAllowed(
        BaseRelationalReadPlan plan,
        IReadOnlyList<BaseRelationalReadSourcePolicy> policies)
    {
        var bySource = policies.ToDictionary(static policy => policy.SourceId, StringComparer.Ordinal);
        foreach (BaseRelationalOperand operand in Operands(plan))
        {
            if (operand.Kind is not (BaseRelationalOperandKind.SourceField or BaseRelationalOperandKind.RecordId) ||
                operand.SourceId is null || !bySource.TryGetValue(operand.SourceId, out BaseRelationalReadSourcePolicy? policy))
                continue;
            FieldMask? mask = policy.ReadMask;
            bool system = operand.Kind == BaseRelationalOperandKind.RecordId;
            if (system && mask?.AppliesToSystemFields != true) continue;
            bool allowed = mask?.Mode switch
            {
                null or FieldMaskMode.Unspecified or FieldMaskMode.AllowAll => true,
                FieldMaskMode.DenyAll => false,
                FieldMaskMode.IncludeOnly => (mask.Include ?? []).Contains(operand.FieldId!, StringComparer.Ordinal),
                FieldMaskMode.Exclude => !(mask.Exclude ?? []).Contains(operand.FieldId!, StringComparer.Ordinal),
                _ => false,
            };
            if (!allowed) return false;
        }
        return true;
    }

    private static IEnumerable<BaseRelationalOperand> Operands(BaseRelationalReadPlan plan)
    {
        foreach (BaseRelationalReadJoin join in plan.Joins) { yield return join.Left; yield return join.Right; }
        foreach (BaseRelationalOperand operand in PredicateOperands(plan.Predicate)) yield return operand;
        foreach (BaseRelationalOperand operand in plan.GroupKeys) yield return operand;
        foreach (BaseRelationalReadAggregate aggregate in plan.Aggregates) if (aggregate.Operand is not null) yield return aggregate.Operand;
        foreach (BaseRelationalOperand operand in PredicateOperands(plan.Having)) yield return operand;
        foreach (BaseRelationalReadProjection projection in plan.Projection) yield return projection.Operand;
        foreach (BaseRelationalReadSort sort in plan.Sort) yield return sort.Operand;
    }

    private static IEnumerable<BaseRelationalOperand> PredicateOperands(BaseRelationalPredicate? predicate)
    {
        if (predicate?.Left is not null) yield return predicate.Left;
        if (predicate?.Right is not null) yield return predicate.Right;
        foreach (BaseRelationalPredicate child in predicate?.Children ?? [])
            foreach (BaseRelationalOperand operand in PredicateOperands(child)) yield return operand;
    }

    private static int CountPredicate(BaseRelationalPredicate? predicate) => predicate is null
        ? 0
        : 1 + (predicate.Children?.Sum(CountPredicate) ?? 0);

    private static bool PredicatesSupported(BaseRelationalPredicate? predicate, RelationalReadCapability capability)
    {
        if (predicate is null) return true;
        if (predicate.Kind == FilterNodeKind.Compare && !capability.ComparisonOperators.Contains(predicate.Operator))
            return false;
        if (predicate.Left?.Literal is { } left && !capability.ValueKinds.Contains(left.Kind)) return false;
        if (predicate.Right?.Literal is { } right && !capability.ValueKinds.Contains(right.Kind)) return false;
        return predicate.Children?.All(child => PredicatesSupported(child, capability)) ?? true;
    }

    private static bool ValidateParameters(
        BaseRelationalReadParameter[] definitions,
        BaseRelationalParameterValue[] values,
        HPDBaseRelationalOptions options,
        RelationalReadCapability capability)
    {
        if (values.Length != definitions.Length || values.Length > options.MaxParameters)
            return false;
        string[] expectedIds = definitions.Select(static parameter => parameter.Id).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        string[] actualIds = values.Select(static value => value.ParameterId)
            .OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        if (!actualIds.SequenceEqual(expectedIds.OrderBy(static id => id, StringComparer.Ordinal), StringComparer.Ordinal))
            return false;
        if (!values.All(value => ValidValue(value.Value, options, capability, allowArray: true))) return false;
        IReadOnlyDictionary<string, BaseRelationalReadParameter> byId = definitions.ToDictionary(static parameter => parameter.Id, StringComparer.Ordinal);
        return values.All(value => Matches(byId[value.ParameterId], value.Value));

        static bool Matches(BaseRelationalReadParameter definition, QueryValue value)
        {
            if (value.Kind == QueryValueKind.Null) return definition.Nullable;
            if (value.Kind != definition.Kind) return false;
            if (value.Kind == QueryValueKind.Array)
                return value.Array is { } items && definition.MaxItems is { } maxItems && items.Length <= maxItems &&
                    items.All(item => item.Kind == definition.ElementKind && WithinLength(definition, item));
            return WithinLength(definition, value);
        }

        static bool WithinLength(BaseRelationalReadParameter definition, QueryValue value) => definition.MaxLength is not { } maximum ||
            (value.Kind == QueryValueKind.String ? value.String?.Length : value.Id?.Length) is { } length && length <= maximum;
    }

    private static bool SemanticTypesValid(
        BaseRelationalReadPlan plan,
        BaseRelationalParameterValue[] parameters,
        IReadOnlyDictionary<string, CollectionDefinition> collections)
    {
        var parameterKinds = parameters.ToDictionary(static value => value.ParameterId, static value => value.Value, StringComparer.Ordinal);
        var sources = plan.Sources.ToDictionary(static source => source.Id, StringComparer.Ordinal);
        var aggregates = plan.Aggregates.ToDictionary(static aggregate => aggregate.Id, StringComparer.Ordinal);
        QueryValueKind? Kind(BaseRelationalOperand operand) => operand.Kind switch
        {
            BaseRelationalOperandKind.RecordId => QueryValueKind.Id,
            BaseRelationalOperandKind.SourceField => FieldKind((collections[sources[operand.SourceId!].CollectionId].Fields ?? [])
                .Single(field => field.Id == operand.FieldId)),
            BaseRelationalOperandKind.Parameter => parameterKinds[operand.ParameterId!].Kind,
            BaseRelationalOperandKind.Literal => operand.Literal!.Kind,
            BaseRelationalOperandKind.Aggregate => AggregateKind(aggregates[operand.AggregateId!]),
            _ => null,
        };
        QueryValueKind? AggregateKind(BaseRelationalReadAggregate aggregate) => aggregate.Kind switch
        {
            BaseAggregateKind.Count or BaseAggregateKind.CountDistinct => QueryValueKind.Integer,
            BaseAggregateKind.Any or BaseAggregateKind.All => QueryValueKind.Boolean,
            BaseAggregateKind.Average => Kind(aggregate.Operand!) == QueryValueKind.Number ? QueryValueKind.Number : QueryValueKind.Decimal,
            BaseAggregateKind.Sum => Kind(aggregate.Operand!) switch
            {
                QueryValueKind.Integer => QueryValueKind.Integer,
                QueryValueKind.Number => QueryValueKind.Number,
                _ => QueryValueKind.Decimal,
            },
            _ => Kind(aggregate.Operand!),
        };
        bool Predicate(BaseRelationalPredicate? predicate)
        {
            if (predicate is null) return true;
            if (!(predicate.Children ?? []).All(Predicate)) return false;
            if (predicate.Kind is not (FilterNodeKind.Compare or FilterNodeKind.In or FilterNodeKind.Between)) return true;
            QueryValueKind? left = Kind(predicate.Left!);
            QueryValueKind? right = Kind(predicate.Right!);
            if (predicate.Kind == FilterNodeKind.Compare)
                return Compatible(left, right) && (predicate.Operator is FilterOperator.Equal or FilterOperator.NotEqual || Ordered(left) && Ordered(right));
            QueryValue array = predicate.Right!.Kind switch
            {
                BaseRelationalOperandKind.Parameter => parameterKinds[predicate.Right.ParameterId!],
                BaseRelationalOperandKind.Literal => predicate.Right.Literal!,
                _ => null!,
            };
            if (array?.Kind != QueryValueKind.Array || array.Array is null ||
                predicate.Kind == FilterNodeKind.Between && array.Array.Length != 2 ||
                array.Array.Any(item => !Compatible(left, item.Kind))) return false;
            return predicate.Kind != FilterNodeKind.Between || Ordered(left) && array.Array.All(item => Ordered(item.Kind));
        }
        return plan.Joins.All(join => Compatible(Kind(join.Left), Kind(join.Right))) &&
            Predicate(plan.Predicate) && Predicate(plan.Having);
    }

    private static QueryValueKind? FieldKind(FieldDefinition field) => field.Format == "date-time"
        ? QueryValueKind.DateTime
        : field.Type switch
        {
            "string" => QueryValueKind.String,
            "boolean" => QueryValueKind.Boolean,
            "integer" => QueryValueKind.Integer,
            "number" => QueryValueKind.Number,
            "decimal" => QueryValueKind.Decimal,
            "id" => QueryValueKind.Id,
            _ => null,
        };

    private static bool Numeric(QueryValueKind? kind) => kind is QueryValueKind.Integer or QueryValueKind.Number or QueryValueKind.Decimal;
    private static bool Ordered(QueryValueKind? kind) => kind is QueryValueKind.String or QueryValueKind.Integer or QueryValueKind.Number or QueryValueKind.Decimal or QueryValueKind.DateTime or QueryValueKind.Id;
    private static bool Compatible(QueryValueKind? left, QueryValueKind? right) => left is not null && right is not null && (left == right || Numeric(left) && Numeric(right) || left == QueryValueKind.Null || right == QueryValueKind.Null);

    private static bool ValidValue(QueryValue? value, HPDBaseRelationalOptions options, RelationalReadCapability capability, bool allowArray)
    {
        if (value is null) return false;
        if (!capability.ValueKinds.Contains(value.Kind)) return false;
        int branches = (value.String is null ? 0 : 1) + (value.Boolean is null ? 0 : 1) +
            (value.Integer is null ? 0 : 1) + (value.Number is null ? 0 : 1) +
            (value.Decimal is null ? 0 : 1) + (value.DateTime is null ? 0 : 1) +
            (value.Id is null ? 0 : 1) + (value.Array is null ? 0 : 1);
        return value.Kind switch
        {
            QueryValueKind.Null => branches == 0,
            QueryValueKind.String => branches == 1 && value.String is string text && text.Length <= options.MaxParameterStringLength,
            QueryValueKind.Boolean => branches == 1 && value.Boolean is not null,
            QueryValueKind.Integer => branches == 1 && value.Integer is not null,
            QueryValueKind.Number => branches == 1 && value.Number is { } number && double.IsFinite(number),
            QueryValueKind.Decimal => branches == 1 && value.Decimal is { Length: > 0 } decimalText && decimal.TryParse(decimalText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _),
            QueryValueKind.DateTime => branches == 1 && value.DateTime is not null,
            QueryValueKind.Id => branches == 1 && value.Id is { Length: > 0 } id && id.Length <= options.MaxParameterStringLength,
            QueryValueKind.Array => branches == 1 && allowArray && value.Array is { } array &&
                array.Length <= options.MaxParameterArrayItems &&
                array.All(item => item.Kind != QueryValueKind.Array && ValidValue(item, options, capability, allowArray: false)),
            _ => false,
        };
    }

    private static OperationResult<BaseRegisteredReadEvaluation<TRow>> Failure<TRow>(OperationStatus status, string code, string message) => new()
    {
        Status = status,
        Error = new BaseError
        {
            Code = code,
            Message = message,
            Category = status switch
            {
                OperationStatus.ValidationFailed => ErrorCategory.Validation,
                OperationStatus.NotFound => ErrorCategory.NotFound,
                OperationStatus.CapabilityUnavailable => ErrorCategory.Capability,
                OperationStatus.PolicyDenied or OperationStatus.Unauthorized => ErrorCategory.Authorization,
                _ => ErrorCategory.Store,
            },
        },
    };

    private static OperationResult<BaseRegisteredReadEvaluation<TRow>> CopyFailure<TRow, TSource>(OperationResult<TSource> result) => new()
    {
        Status = result.Status,
        Error = result.Error,
        Warnings = result.Warnings,
        Diagnostics = result.Diagnostics,
    };
}
