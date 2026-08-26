using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace HPD.Base;

internal sealed class DefaultBaseRegisteredReadRuntime(
    BaseReadRegistry registry,
    BaseCollectionRegistry collections,
    IRecordStoreRegistry stores,
    IBasePolicyOrchestrator policy,
    IServiceProvider services,
    IOptions<HPDBaseRelationalOptions> options,
    BaseSubjectContractRegistry subjects,
    HPDBaseInstalledFeatures? installed = null) : IBaseRegisteredReadRuntime
{
    private readonly HPDBaseRelationalOptions _options = options.Value;

    /// <summary>Executes the execute async operation.</summary>
    public async ValueTask<OperationResult<BaseRegisteredReadEvaluation<TRow>>> ExecuteAsync<TParameters, TRow>(
        BaseReadDefinition<TParameters, TRow> definition,
        TParameters parameters,
        BaseRegisteredReadWindow? window,
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
        if (!Authorized(definition.Authorization, principal.AuthenticationState))
            return Failure<TRow>(OperationStatus.PolicyDenied, "base.relational.read.denied", "Policy denied the registered read.");
        if (definition.SourceAuthority == BaseRegisteredReadSourceAuthority.System && !BaseSystemCollectionGate.Allows(principal))
            return Failure<TRow>(OperationStatus.NotFound, "base.systemCollection.accessForbidden", "The registered read was not found.");
        if (!ValidWindow(definition.Plan, window, _options.MaxPageSize))
            return Failure<TRow>(OperationStatus.ValidationFailed, "base.relational.read.invalid", "The registered read page is invalid.");
        if (definition.Plan.Topology == BaseRelationalReadTopology.CompoundCount)
        {
            int branches = definition.Plan.CompoundCountBranches.Length;
            if (window is { } compoundPage && (compoundPage.Kind != BaseRegisteredReadWindowKind.Page || compoundPage.Page != 1 || compoundPage.PerPage != branches))
                return Failure<TRow>(OperationStatus.ValidationFailed, "base.relational.read.invalid", "The registered read page is invalid.");
            window ??= PageWindow(1, branches);
        }

        BaseSubjectAcquisitionDefinition? acquisition = SubjectAcquisition(definition.Plan, definition.Id, definition.Audience, subjects);
        TimeSpan acquisitionTimeout = _options.SnapshotAcquisitionTimeout;
        if (definition.Plan.Projection.Any(static projection => projection.Operand.Kind == BaseRelationalOperandKind.SubjectReference))
        {
            if (acquisition is null || WindowLimit(window) > acquisition.MaximumResults)
                return Failure<TRow>(OperationStatus.NotFound, "base.systemCollection.accessForbidden", "The registered read was not found.");
            window ??= PageWindow(1, acquisition.MaximumResults);
            BaseGeneratedSubjectRegistration target = subjects.Find(acquisition.ContractId, acquisition.ContractVersion)!;
            acquisitionTimeout = target.Definition.ValidationPlan.Limits.AcquisitionTimeout < acquisitionTimeout
                ? target.Definition.ValidationPlan.Limits.AcquisitionTimeout
                : acquisitionTimeout;
            OperationResult<BasePolicyEvaluation> acquisitionGrant = await policy.EvaluateReadAsync(new BasePolicyRequest
            {
                Principal = principal,
                Operation = operation with
                {
                    Operation = BaseOperationKind.SubjectAcquire,
                    CollectionId = target.Definition.Id,
                    RecordId = null,
                    Audience = acquisition.Audience,
                    Mode = OperationMode.System,
                },
                Collection = new CollectionDefinition
                {
                    Id = target.Definition.Id,
                    Name = "Exported logical subject contract",
                    Kind = "system",
                    Exposed = false,
                    System = true,
                    SystemOwnerModuleId = target.Definition.OwningModuleId,
                    SchemaMode = SchemaMode.Strict,
                    UnknownFields = UnknownFieldPolicy.Reject,
                    Store = collections.Collections[definition.Plan.Sources[0].CollectionId].Store,
                },
                ResourceKind = PolicyResourceKind.SubjectContract,
                SubjectContractId = target.Definition.Id,
                SubjectContractVersion = target.Definition.Version,
            }, cancellationToken).ConfigureAwait(false);
            if (!acquisitionGrant.IsSuccess() || !BaseSystemCollectionGate.HasExactGrant(acquisitionGrant, acquisition.RequiredGrantId))
                return Failure<TRow>(OperationStatus.NotFound, "base.systemCollection.accessForbidden", "The registered read was not found.");
        }

        var sourcePolicies = new List<BaseRelationalReadSourcePolicy>(definition.Plan.Sources.Length);
        bool projectionGrantMatched = false;
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
            bool exactGrantMatched = BaseSystemCollectionGate.HasExactGrant(policyResult, definition.RequiredGrantId);
            if (!BaseSystemCollectionGate.AllowsSource(collection, policyResult, definition.RequiredGrantId))
                return Failure<TRow>(OperationStatus.NotFound, "base.systemCollection.accessForbidden", "The registered read was not found.");
            projectionGrantMatched |= exactGrantMatched;
            sourcePolicies.Add(new BaseRelationalReadSourcePolicy
            {
                SourceId = source.Id,
                CollectionId = source.CollectionId,
                Filter = policyResult.Value.EffectiveRecordFilter,
                ReadMask = policyResult.Value.EffectiveReadMask,
            });
        }
        if (definition.Disclosure is not BaseRegisteredReadDisclosure.Ordinary || definition.SourceAuthority == BaseRegisteredReadSourceAuthority.System)
        {
            if (!projectionGrantMatched)
                return Failure<TRow>(OperationStatus.NotFound, "base.systemCollection.accessForbidden", "The registered read was not found.");
        }
        if (!InfluenceAllowed(definition.Plan, sourcePolicies))
            return Failure<TRow>(OperationStatus.PolicyDenied, "base.relational.read.policyUnsupported", "Policy denied the registered read.");

        // Resolve provider authority only after every source has passed policy and, for
        // system collections, its exact grant. Provider discovery is itself influence.
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

        if (selected is not IRelationalReadStore relational)
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.unsupported", "The selected store cannot execute this registered read.");
        RelationalReadCapability capability;
        byte[] capabilityChecksum;
        try
        {
            capability = BaseRelationalReadCapabilityContract.Clone(relational.RelationalReads);
            if (!BaseRelationalReadCapabilityContract.IsValid(capability))
                return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.unsupported", "The selected store cannot execute this registered read.");
            capabilityChecksum = BaseRelationalReadCapabilityContract.Checksum(capability).ToArray();
        }
        catch
        {
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.unsupported", "The selected store cannot execute this registered read.");
        }
        if (!capability.Supported)
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.unsupported", "The selected store cannot execute this registered read.");
        if (installed is not null && !CryptographicOperations.FixedTimeEquals(
                capabilityChecksum.AsSpan(), installed.StoreProvider.RelationalReadCapabilityChecksum.AsSpan()))
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.unsupported", "The selected store cannot execute this registered read.");
        if (!capability.SnapshotConsistency)
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.snapshotUnavailable", "The selected store cannot provide the required snapshot.");
        if (!capability.CompleteDependencyEvidence)
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.unsupported", "The selected store cannot provide complete dependency evidence.");
        if (!Supports(definition.Plan, capability, _options))
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.unsupported", "The selected store cannot execute this registered read.");

        BaseRelationalParameterValue[] encoded;
        try
        {
            encoded = definition.ParameterCodec.Encode(parameters).Select(static value => new BaseRelationalParameterValue
            {
                ParameterId = new string(value.ParameterId.AsSpan()),
                Value = OwnValue(value.Value),
            }).ToArray();
        }
        catch { return Failure<TRow>(OperationStatus.ValidationFailed, "base.relational.read.invalid", "Registered read parameters are invalid."); }
        if (!ValidateParameters(definition.Plan.Parameters, encoded, _options, capability))
            return Failure<TRow>(OperationStatus.ValidationFailed, "base.relational.read.invalid", "Registered read parameters are invalid.");
        if (!SemanticTypesValid(definition.Plan, encoded, collections.Collections))
            return Failure<TRow>(OperationStatus.ValidationFailed, "base.relational.read.invalid", "Registered read parameter types are incompatible with the registered plan.");

        var plan = definition.Plan with { Window = window is null ? null : window with { }, SchemaGeneration = generation };
        var request = new BaseRelationalReadExecutionRequest
        {
            Plan = plan,
            ParameterValues = encoded,
            SourcePolicies = sourcePolicies.ToArray(),
            Operation = operation,
            AcquisitionTimeout = acquisitionTimeout,
            ExecutionTimeout = TimeSpan.FromMilliseconds(definition.Plan.Budgets.MaxExecutionMilliseconds),
            MaxResultRows = Math.Min(_options.MaxResultRows, capability.MaxResultRows),
            MaxResultBytes = Math.Min(definition.Plan.Budgets.MaxResultBytes,
                Math.Min(_options.MaxRegisteredReadResultBytes, capability.MaxResultBytes)),
        };

        OperationResult<BaseRelationalReadExecutionResult> result;
        try
        {
            result = await relational.ExecuteReadAsync(request, cancellationToken)
                .AsTask().WaitAsync(request.ExecutionTimeout, cancellationToken).ConfigureAwait(false);
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
        BaseRelationalReadExecutionResult execution;
        try { execution = OwnExecution(result.Value); }
        catch { return Failure<TRow>(OperationStatus.StoreError, "base.relational.read.resultInvalid", "The provider returned an invalid registered read result."); }
        BaseApplicationReadiness? completedReadiness = application?.CurrentReadiness;
        if (execution.Result.SchemaGeneration != generation ||
            (completedReadiness is not null &&
                (completedReadiness.State != BaseApplicationReadinessState.Ready || completedReadiness.SchemaGeneration != generation)))
            return Failure<TRow>(OperationStatus.CapabilityUnavailable, "base.relational.read.schemaNotReady", "The registered read schema generation is not ready.");
        long evidenceBytes = 0;
        if (plan.Topology == BaseRelationalReadTopology.CompoundCount
            && (!BaseRelationalReadEvidenceAccounting.TryMeasure(execution.DependencyEvidence, execution.CompoundBranches, out evidenceBytes)
                || evidenceBytes > request.MaxResultBytes))
            return Failure<TRow>(OperationStatus.StoreError, "base.relational.read.limitExceeded", "The provider returned an over-limit registered read result.");
        ResultValidation resultValidation = ValidateResult(plan, execution.Result,
            request with { MaxResultBytes = checked((int)(request.MaxResultBytes - evidenceBytes)) }, capability, _options);
        if (resultValidation != ResultValidation.Valid)
            return Failure<TRow>(OperationStatus.StoreError,
                resultValidation == ResultValidation.LimitExceeded ? "base.relational.read.limitExceeded" : "base.relational.read.resultInvalid",
                "The provider returned an invalid registered read result.");

        TRow[] rows;
        try { rows = execution.Result.Rows.Select(definition.RowCodec.Decode).ToArray(); }
        catch { return Failure<TRow>(OperationStatus.StoreError, "base.relational.read.resultInvalid", "The provider returned an invalid registered read result."); }

        if (!ValidateEvidence(plan, execution.DependencyEvidence, request, acquisition))
            return Failure<TRow>(OperationStatus.StoreError, "base.relational.dependencies.invalid", "The provider returned invalid dependency evidence.");
        if (!ValidateCompoundEvidence(plan, execution.CompoundBranches, generation))
            return Failure<TRow>(OperationStatus.StoreError, "base.relational.read.resultInvalid", "The provider returned an invalid registered read result.");

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

    private static bool Authorized(BaseReadAuthorization authorization, PrincipalAuthenticationState state) =>
        authorization switch
        {
            BaseReadAuthorization.Authenticated => state is PrincipalAuthenticationState.Authenticated or
                PrincipalAuthenticationState.Service or PrincipalAuthenticationState.Admin or PrincipalAuthenticationState.System,
            BaseReadAuthorization.Admin => state is PrincipalAuthenticationState.Admin or PrincipalAuthenticationState.System,
            BaseReadAuthorization.System => state == PrincipalAuthenticationState.System,
            _ => false,
        };

    private static BaseSubjectAcquisitionDefinition? SubjectAcquisition(
        BaseRelationalReadPlan plan,
        string readId,
        HPDBaseEndpointAudience audience,
        BaseSubjectContractRegistry subjects)
    {
        BaseRelationalOperand[] operands = plan.Projection
            .Where(static projection => projection.Operand.Kind == BaseRelationalOperandKind.SubjectReference)
            .Select(static projection => projection.Operand).ToArray();
        if (operands.Length == 0) return null;
        if (plan.Projection.Length != 1 || operands.Length != 1
            || Operands(plan).Count(static operand => operand.Kind == BaseRelationalOperandKind.SubjectReference) != 1)
            return null;
        BaseRelationalOperand projected = operands[0];
        BaseSubjectAcquisitionDefinition? acquisition = subjects.Acquisitions.SingleOrDefault(value =>
            string.Equals(value.RegisteredReadId, readId, StringComparison.Ordinal)
            && value.Audience == audience
            && string.Equals(value.ContractId, projected.SubjectContractId, StringComparison.Ordinal)
            && value.ContractVersion == projected.SubjectContractVersion);
        return acquisition is not null && subjects.Find(acquisition.ContractId, acquisition.ContractVersion) is not null ? acquisition : null;
    }

    private static ResultValidation ValidateResult(
        BaseRelationalReadPlan plan,
        BaseRelationalReadResult result,
        BaseRelationalReadExecutionRequest request,
        RelationalReadCapability capability,
        HPDBaseRelationalOptions options)
    {
        if (plan.Topology == BaseRelationalReadTopology.CompoundCount)
            return ValidateCompoundResult(plan, result, request, capability, options);
        string[] expected = plan.Projection.Select(static projection => projection.FieldId).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        if (result.Rows.Length > request.MaxResultRows) return ResultValidation.LimitExceeded;
        if (result.Count < 0 || result.Page.Limit is < 0 || result.Page.Offset is < 0 || result.Page.Page is < 0 || result.Page.PerPage is < 0)
            return ResultValidation.Invalid;
        if (!ValidResultWindow(plan.Window, result)) return ResultValidation.Invalid;
        long bytes = 0;
        foreach (BaseRelationalRow row in result.Rows)
        {
            if (!row.Fields.Select(static field => field.FieldId).OrderBy(static id => id, StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal))
                return ResultValidation.Invalid;
            foreach (BaseRelationalFieldValue field in row.Fields)
            {
                if (!ValidValue(field.Value, options, capability, allowArray: true)) return ResultValidation.Invalid;
                BaseRelationalReadProjection projection = plan.Projection.Single(item =>
                    string.Equals(item.FieldId, field.FieldId, StringComparison.Ordinal));
                if (field.Value.Kind == QueryValueKind.CanonicalJson &&
                    (projection.CanonicalJsonAuthority is not { } authority || !CanonicalJsonValue(field.Value, authority)))
                    return ResultValidation.Invalid;
                bytes += field.FieldId.Length * 2L + ValueBytes(field.Value);
                if (bytes > request.MaxResultBytes) return ResultValidation.LimitExceeded;
            }
        }
        return ResultValidation.Valid;
    }

    private enum ResultValidation { Valid, Invalid, LimitExceeded }

    private static bool ValidResultWindow(BaseRegisteredReadWindow? window, BaseRelationalReadResult result)
    {
        if (window is null) return true;
        if (result.Page.Cursor is not null || result.Page.NextCursor is not null) return false;
        if (result.Count is not { } total) return false;
        if (window.Kind == BaseRegisteredReadWindowKind.Offset)
        {
            int offset = window.Offset!.Value;
            int limit = window.Limit!.Value;
            long remaining = total <= offset ? 0 : total - offset;
            int expectedRows = checked((int)Math.Min(limit, remaining));
            bool hasMore = remaining > result.Rows.Length;
            return result.Rows.Length == expectedRows && result.Page.Offset == offset && result.Page.Limit == limit
                && result.Page.Page is null && result.Page.PerPage is null && result.Page.HasMore == hasMore;
        }
        int page = window.Page!.Value;
        int perPage = window.PerPage!.Value;
        long offsetForPage;
        try { offsetForPage = checked(((long)page - 1) * perPage); }
        catch (OverflowException) { return false; }
        long pageRemaining = total <= offsetForPage ? 0 : total - offsetForPage;
        int expectedPageRows = checked((int)Math.Min(perPage, pageRemaining));
        return result.Rows.Length == expectedPageRows && result.Page.Page == page && result.Page.PerPage == perPage
            && result.Page.Offset is null && result.Page.Limit is null && result.Page.HasMore == (pageRemaining > result.Rows.Length);
    }

    private static bool ValidWindow(BaseRelationalReadPlan plan, BaseRegisteredReadWindow? window, int maximumPageSize)
    {
        if (window is null) return true;
        return window.Kind switch
        {
            BaseRegisteredReadWindowKind.Page => window.Page is >= 1 && window.PerPage is >= 1
                && window.PerPage <= maximumPageSize && window.Offset is null && window.Limit is null,
            BaseRegisteredReadWindowKind.Offset => plan.Topology != BaseRelationalReadTopology.CompoundCount
                && plan.Pagination.Mode == BaseRegisteredReadPaginationMode.PageAndOffset
                && window.Offset is >= 0 && window.Offset <= plan.Pagination.MaximumOffset
                && window.Limit is >= 1 && window.Limit <= maximumPageSize
                && window.Limit <= plan.Budgets.MaxResultRows && window.Page is null && window.PerPage is null,
            _ => false,
        };
    }

    private static int? WindowLimit(BaseRegisteredReadWindow? window) => window?.Kind switch
    {
        BaseRegisteredReadWindowKind.Page => window.PerPage,
        BaseRegisteredReadWindowKind.Offset => window.Limit,
        _ => null,
    };

    private static BaseRegisteredReadWindow PageWindow(int page, int perPage) => new()
    {
        Kind = BaseRegisteredReadWindowKind.Page,
        Page = page,
        PerPage = perPage,
    };

    private static ResultValidation ValidateCompoundResult(
        BaseRelationalReadPlan plan, BaseRelationalReadResult result, BaseRelationalReadExecutionRequest request,
        RelationalReadCapability capability, HPDBaseRelationalOptions options)
    {
        if (result.Rows.Length != plan.CompoundCountBranches.Length || result.Count != result.Rows.Length
            || result.Page.Page != 1 || result.Page.PerPage != result.Rows.Length || result.Page.Limit != result.Rows.Length
            || result.Page.Offset is not (null or 0) || result.Page.HasMore || result.Page.NextCursor is not null) return ResultValidation.Invalid;
        long bytes = 0;
        for (int index = 0; index < result.Rows.Length; index++)
        {
            BaseRelationalCompoundCountBranch branch = plan.CompoundCountBranches[index];
            BaseRelationalFieldValue[] fields = result.Rows[index].Fields;
            if (fields.Length != 2) return ResultValidation.Invalid;
            BaseRelationalFieldValue? discriminator = fields.SingleOrDefault(field => string.Equals(field.FieldId, branch.DiscriminatorOutputFieldId, StringComparison.Ordinal));
            BaseRelationalFieldValue? count = fields.SingleOrDefault(field => string.Equals(field.FieldId, branch.CountOutputFieldId, StringComparison.Ordinal));
            if (discriminator?.Value.Kind != QueryValueKind.String || !string.Equals(discriminator.Value.String, branch.Discriminator, StringComparison.Ordinal)
                || count?.Value.Kind != QueryValueKind.Integer || count.Value.Integer is null or < 0) return ResultValidation.Invalid;
            foreach (BaseRelationalFieldValue field in fields)
            {
                if (!ValidValue(field.Value, options, capability, allowArray: false)) return ResultValidation.Invalid;
                bytes += field.FieldId.Length * 2L + ValueBytes(field.Value);
                if (bytes > request.MaxResultBytes) return ResultValidation.LimitExceeded;
            }
        }
        return ResultValidation.Valid;
    }

    private static bool ValidateCompoundEvidence(
        BaseRelationalReadPlan plan, BaseRelationalCompoundBranchEvidence[] evidence, long generation)
    {
        if (plan.Topology == BaseRelationalReadTopology.Ordinary) return evidence.Length == 0;
        if (evidence.Length != plan.CompoundCountBranches.Length) return false;
        for (int index = 0; index < evidence.Length; index++)
        {
            BaseRelationalCompoundCountBranch branch = plan.CompoundCountBranches[index];
            BaseRelationalCompoundBranchEvidence item = evidence[index];
            if (item.RowOrdinal != index || item.SchemaGeneration != generation
                || !string.Equals(item.BranchId, branch.Id, StringComparison.Ordinal)
                || item.BranchChecksum != branch.BranchChecksum) return false;
        }
        return true;
    }

    private static bool ValidateEvidence(
        BaseRelationalReadPlan plan,
        BaseReadDependencyEvidence[] evidence,
        BaseRelationalReadExecutionRequest request,
        BaseSubjectAcquisitionDefinition? acquisition)
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
            bool hasSubject = item.SubjectContractId is not null || item.SubjectContractVersion is not null || item.SubjectStateGeneration is not null;
            if (hasSubject && (acquisition is null
                || !string.Equals(item.SubjectContractId, acquisition.ContractId, StringComparison.Ordinal)
                || item.SubjectContractVersion != acquisition.ContractVersion
                || item.SubjectStateGeneration is not > 0))
                return false;
            contributing.Add(item.CollectionId);
        }
        int subjectEvidence = evidence.Count(static item => item.SubjectContractId is not null);
        return contributing.SetEquals(allowed) && (acquisition is null ? subjectEvidence == 0 : subjectEvidence == 1);
    }

    private static long ValueBytes(QueryValue value) => value.Kind switch
    {
        QueryValueKind.String => (value.String?.Length ?? 0) * 2L,
        QueryValueKind.Id => (value.Id?.Length ?? 0) * 2L,
        QueryValueKind.Decimal => (value.Decimal?.Length ?? 0) * 2L,
        QueryValueKind.Array => (value.Array ?? []).Sum(ValueBytes),
        QueryValueKind.SubjectReference => System.Text.Encoding.UTF8.GetByteCount(value.SubjectId ?? string.Empty) + 32,
        QueryValueKind.CanonicalJson => value.CanonicalJsonUtf8.IsDefault ? 0 : value.CanonicalJsonUtf8.Length,
        _ => 16,
    };

    private static BaseRelationalReadExecutionResult OwnExecution(BaseRelationalReadExecutionResult value) => new()
    {
        Result = new BaseRelationalReadResult
        {
            Rows = value.Result.Rows.Select(static row => new BaseRelationalRow
            {
                Fields = row.Fields.Select(static field => new BaseRelationalFieldValue
                {
                    FieldId = new string(field.FieldId.AsSpan()),
                    Value = OwnValue(field.Value),
                }).ToArray(),
            }).ToArray(),
            Count = value.Result.Count,
            Page = value.Result.Page with { },
            SchemaGeneration = value.Result.SchemaGeneration,
        },
        DependencyEvidence = value.DependencyEvidence.Select(static evidence => evidence with
        {
            CollectionId = new string(evidence.CollectionId.AsSpan()),
            RecordId = evidence.RecordId is null ? null : new string(evidence.RecordId.AsSpan()),
            SubjectContractId = evidence.SubjectContractId is null ? null : new string(evidence.SubjectContractId.AsSpan()),
        }).ToArray(),
        CompoundBranches = value.CompoundBranches.Select(static evidence => evidence with
        {
            BranchId = new string(evidence.BranchId.AsSpan()),
            BranchChecksum = BaseSchemaAuthorityChecksum.Create(evidence.BranchChecksum.ToArray()),
        }).ToArray(),
    };

    private static QueryValue OwnValue(QueryValue value) => value with
    {
        String = value.String is null ? null : new string(value.String.AsSpan()),
        Decimal = value.Decimal is null ? null : new string(value.Decimal.AsSpan()),
        Id = value.Id is null ? null : new string(value.Id.AsSpan()),
        Array = value.Array?.Select(OwnValue).ToArray(),
        SubjectId = value.SubjectId is null ? null : new string(value.SubjectId.AsSpan()),
        SubjectAuthorityEpoch = value.SubjectAuthorityEpoch is null ? null : new string(value.SubjectAuthorityEpoch.AsSpan()),
        SubjectIncarnation = value.SubjectIncarnation is null ? null : new string(value.SubjectIncarnation.AsSpan()),
        CanonicalJsonUtf8 = value.CanonicalJsonUtf8.IsDefault
            ? default
            : System.Collections.Immutable.ImmutableArray.Create(value.CanonicalJsonUtf8.AsSpan().ToArray()),
    };

    private static BaseDependencyReference[] ProtectDependencies(
        BaseReadDependencyEvidence[] evidence,
        IBaseDependencyReferenceFactory? factory,
        string? tenantId)
    {
        if (evidence is null || evidence.Any(static item => string.IsNullOrWhiteSpace(item.CollectionId) || item.CollectionId.Length > 128 || item.RecordId is { Length: > 512 }))
            throw new InvalidOperationException();
        if (factory is null) return [];
        return evidence.SelectMany(item => new[] { item.RecordId is null
                ? factory.Create(BaseDependencyIds.Collection,
                    new BaseDependencyParameter("tenant", tenantId),
                    new BaseDependencyParameter("collection", item.CollectionId))
                : factory.Create(BaseDependencyIds.Record,
                    new BaseDependencyParameter("tenant", tenantId),
                    new BaseDependencyParameter("collection", item.CollectionId),
                    new BaseDependencyParameter("record", item.RecordId)),
                item.SubjectContractId is null ? null : factory.Create(BaseDependencyIds.SubjectContract,
                    new BaseDependencyParameter("contract", item.SubjectContractId),
                    new BaseDependencyParameter("version", item.SubjectContractVersion!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new BaseDependencyParameter("generation", item.SubjectStateGeneration!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))) })
            .Where(static reference => reference is not null)
            .Select(static reference => reference!)
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
            (plan.Topology == BaseRelationalReadTopology.Ordinary && plan.Projection.Length == 0) ||
            plan.Projection.Length > Math.Min(options.MaxProjectionFields, capability.MaxProjectionFields) ||
            plan.Sort.Length > Math.Min(options.MaxSortFields, capability.MaxSortFields) ||
            plan.Budgets.MaxResultRows > Math.Min(options.MaxResultRows, capability.MaxResultRows) ||
            plan.Budgets.MaxResultBytes > Math.Min(options.MaxRegisteredReadResultBytes, capability.MaxResultBytes) ||
            plan.Budgets.MaxExecutionMilliseconds < 1 ||
            plan.Budgets.MaxExecutionMilliseconds > options.MaxExecutionDuration.TotalMilliseconds ||
            plan.Joins.Any(join => !capability.JoinKinds.Contains(join.Kind)) ||
            plan.Aggregates.Any(aggregate => !capability.AggregateKinds.Contains(aggregate.Kind)))
            return false;
        if (plan.Topology == BaseRelationalReadTopology.CompoundCount &&
            (!capability.IndependentAggregateBranches || !capability.SingleSnapshotCompoundReads
                || plan.CompoundCountBranches.Length > Math.Min(options.MaxCompoundReadBranches, capability.MaxCompoundBranches)
                || plan.CompoundCountBranches.Length > Math.Min(options.MaxAggregates, capability.MaxAggregates)
                || plan.Budgets.MaxCompoundOperations > Math.Min(options.MaxCompoundReadOperations, capability.MaxCompoundOperations))) return false;
        if ((plan.Parameters.Any(static parameter => parameter.Kind == QueryValueKind.CanonicalJson)
            || plan.Projection.Any(static projection => projection.CanonicalJsonAuthority is not null))
            && !capability.CanonicalJsonValues) return false;

        int predicateNodes = CountPredicate(plan.Predicate) + CountPredicate(plan.Having)
            + plan.CompoundCountBranches.Sum(static branch => CountPredicate(branch.Predicate));
        if (predicateNodes > Math.Min(options.MaxPredicateNodes, capability.MaxPredicateNodes))
            return false;
        return PredicatesSupported(plan.Predicate, capability) && PredicatesSupported(plan.Having, capability)
            && plan.CompoundCountBranches.All(branch => PredicatesSupported(branch.Predicate, capability));
    }

    private static bool InfluenceAllowed(
        BaseRelationalReadPlan plan,
        IReadOnlyList<BaseRelationalReadSourcePolicy> policies)
    {
        var bySource = policies.ToDictionary(static policy => policy.SourceId, StringComparer.Ordinal);
        foreach (BaseRelationalOperand operand in Operands(plan))
        {
            if (operand.Kind is not (BaseRelationalOperandKind.SourceField or BaseRelationalOperandKind.StoredSubjectReference or BaseRelationalOperandKind.RecordId or BaseRelationalOperandKind.RecordRevision) ||
                operand.SourceId is null || !bySource.TryGetValue(operand.SourceId, out BaseRelationalReadSourcePolicy? policy))
                continue;
            FieldMask? mask = policy.ReadMask;
            bool system = operand.Kind is BaseRelationalOperandKind.RecordId or BaseRelationalOperandKind.RecordRevision;
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
        foreach (BaseRelationalCompoundCountBranch branch in plan.CompoundCountBranches)
            foreach (BaseRelationalOperand operand in PredicateOperands(branch.Predicate)) yield return operand;
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
            return WithinLength(definition, value) && (value.Kind != QueryValueKind.CanonicalJson
                || definition.CanonicalJsonAuthority is { } authority && CanonicalJsonValue(value, authority));
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
            BaseRelationalOperandKind.RecordRevision => QueryValueKind.String,
            BaseRelationalOperandKind.SourceField => FieldKind((collections[sources[operand.SourceId!].CollectionId].Fields ?? [])
                .Single(field => field.Id == operand.FieldId)),
            BaseRelationalOperandKind.Parameter => parameterKinds[operand.ParameterId!].Kind,
            BaseRelationalOperandKind.Literal => operand.Literal!.Kind,
            BaseRelationalOperandKind.Aggregate => AggregateKind(aggregates[operand.AggregateId!]),
            BaseRelationalOperandKind.StoredSubjectReference => QueryValueKind.SubjectReference,
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
            Predicate(plan.Predicate) && Predicate(plan.Having)
            && plan.CompoundCountBranches.All(branch => Predicate(branch.Predicate));
    }

    private static QueryValueKind? FieldKind(FieldDefinition field) => field.ScalarKind is BaseScalarKind.Guid or BaseScalarKind.RecordId
        ? QueryValueKind.Id
        : field.Format == "date-time"
        ? QueryValueKind.DateTime
        : field.Type switch
        {
            "string" => QueryValueKind.String,
            "boolean" => QueryValueKind.Boolean,
            "integer" => QueryValueKind.Integer,
            "number" => QueryValueKind.Number,
            "decimal" => QueryValueKind.Decimal,
            "id" => QueryValueKind.Id,
            _ when field.ScalarKind == BaseScalarKind.CanonicalJson => QueryValueKind.CanonicalJson,
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
            (value.Id is null ? 0 : 1) + (value.Array is null ? 0 : 1) +
            (value.CanonicalJsonUtf8.IsDefault ? 0 : 1) +
            (value.SubjectId is null && value.SubjectAuthorityEpoch is null && value.SubjectIncarnation is null
                && value.SubjectIdKind is null && value.SubjectIdMaximumUtf8Bytes is null ? 0 : 1);
        return value.Kind switch
        {
            QueryValueKind.Null => branches == 0,
            QueryValueKind.String => branches == 1 && value.String is string text && text.Length <= options.MaxParameterStringLength,
            QueryValueKind.Boolean => branches == 1 && value.Boolean is not null,
            QueryValueKind.Integer => branches == 1 && value.Integer is not null,
            QueryValueKind.Number => branches == 1 && value.Number is { } number && double.IsFinite(number),
            QueryValueKind.Decimal => branches == 1 && value.Decimal is { Length: > 0 } decimalText && decimal.TryParse(decimalText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _),
            QueryValueKind.DateTime => branches == 1 && value.DateTime is not null,
            QueryValueKind.Id => branches == 1 && value.Id is { } id && id.Length <= options.MaxParameterStringLength && RecordId.TryParse(id, out _),
            QueryValueKind.Array => branches == 1 && allowArray && value.Array is { } array &&
                array.Length <= options.MaxParameterArrayItems &&
                array.All(item => item.Kind != QueryValueKind.Array && ValidValue(item, options, capability, allowArray: false)),
            QueryValueKind.SubjectReference => branches == 1 && value.SubjectId is not null
                && value.SubjectIdKind is { } subjectKind && Enum.IsDefined(subjectKind)
                && value.SubjectIdMaximumUtf8Bytes is >= 1 and <= 256
                && CanonicalSubjectValue(value, subjectKind),
            QueryValueKind.CanonicalJson => branches == 1 && capability.CanonicalJsonValues
                && CanonicalJsonValue(value),
            _ => false,
        };
    }

    private static bool CanonicalJsonValue(QueryValue value)
    {
        if (value.CanonicalJsonUtf8.IsDefaultOrEmpty) return false;
        try
        {
            _ = BaseCanonicalJson.ParseAndValidate(value.CanonicalJsonUtf8.AsSpan(), new BaseCanonicalJsonLimits
            {
                MaximumCanonicalBytes = 1_048_576,
                MaximumDepth = 64,
                MaximumArrayItemsPerContainer = 16_384,
                MaximumObjectPropertiesPerContainer = 16_384,
                MaximumTotalNodes = 65_536,
                MaximumTotalStringUtf8Bytes = 1_048_576,
                MaximumTotalNameUtf8Bytes = 1_048_576,
            });
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static bool CanonicalJsonValue(QueryValue value, BaseReadCanonicalJsonAuthority authority)
    {
        if (!BaseReadCanonicalJsonAuthorityContract.Valid(authority) || value.CanonicalJsonUtf8.IsDefaultOrEmpty) return false;
        try
        {
            _ = BaseCanonicalJson.ParseAndValidate(value.CanonicalJsonUtf8.AsSpan(), BaseReadCanonicalJsonAuthorityContract.Limits(authority));
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(value.CanonicalJsonUtf8.AsMemory());
            return authority.JsonShape switch
            {
                BaseJsonShape.Object => document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object,
                BaseJsonShape.Array => document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array,
                BaseJsonShape.ObjectOrArray => document.RootElement.ValueKind is System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array,
                _ => false,
            };
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static bool CanonicalSubjectValue(QueryValue value, BaseSubjectIdKind kind)
    {
        try
        {
            _ = BaseSubjectId.Create(value.SubjectId!, kind, value.SubjectIdMaximumUtf8Bytes!.Value);
            _ = BaseSubjectAuthorityEpoch.Parse(value.SubjectAuthorityEpoch!);
            _ = BaseSubjectIncarnation.Parse(value.SubjectIncarnation!);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            return false;
        }
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
