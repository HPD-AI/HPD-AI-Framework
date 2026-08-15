using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace HPD.Base;

internal interface IBaseSelectionMutationRuntime
{
    ValueTask<BaseResult<BaseSelectionMutationResult>> ExecuteAsync(
        BaseSession session,
        CollectionDefinition collection,
        BaseSelectionOperationProfile profile,
        RecordQuery query,
        RecordPatchRequest? patch,
        BasePreviousStateRequirement previousState,
        BaseMutationRequestIdentity? identity,
        BaseSelectionMutationExecutionOptions? options,
        CancellationToken cancellationToken);
}

internal sealed class DefaultBaseSelectionMutationRuntime(
    IBaseStoreExecutionResolver stores,
    IBasePolicyOrchestrator policy,
    IBaseMutationPostCommitDispatcher postCommit,
    TimeProvider timeProvider,
    BaseSubjectContractRegistry subjects) : IBaseSelectionMutationRuntime
{
    public async ValueTask<BaseResult<BaseSelectionMutationResult>> ExecuteAsync(
        BaseSession session,
        CollectionDefinition collection,
        BaseSelectionOperationProfile profile,
        RecordQuery query,
        RecordPatchRequest? patch,
        BasePreviousStateRequirement previousState,
        BaseMutationRequestIdentity? identity,
        BaseSelectionMutationExecutionOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(previousState);
        BasePreviousStateRequirement? normalizedPreviousState = NormalizePreviousState(collection, previousState, profile.Limits.MaximumPreviousStateRequirements);
        if (query.Page?.Mode != QueryPaginationMode.Offset || query.Page.Offset is not (null or 0)
            || query.Page.Limit is not { } take || take < 1 || take > profile.Limits.MaximumSelectedRecords
            || query.Sort is not { Length: > 0 }
            || !IsTotalOrder(query.Sort)
            || query.Page.Cursor is not null
            || options?.CallerWaitTimeout is { } wait && (wait <= TimeSpan.Zero || wait > profile.Limits.CallerCommitObservationTimeout)
            || profile.MutationKind == BaseSelectionMutationKind.MergePatch && patch is null
            || profile.MutationKind == BaseSelectionMutationKind.Delete && patch is not null
            || normalizedPreviousState is null)
            return Failure(OperationStatus.ValidationFailed, BaseSelectionErrorCodes.ContractInvalid, ErrorCategory.Validation);

        OperationContext operation = session.Operation(BaseOperationKind.SelectionMutation, collection.Id);
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = session.Principal,
            Operation = operation,
            Collection = collection,
            ResourceKind = PolicyResourceKind.SelectionMutation,
            Query = query,
            ProposedPayload = patch?.Patch,
        }, cancellationToken).ConfigureAwait(false);
        if (!authorization.IsSuccess() || authorization.Value is null
            || !BaseSystemCollectionGate.HasExactGrant(authorization, profile.RequiredGrantId))
            return Failure(OperationStatus.PolicyDenied, BaseSelectionErrorCodes.PolicyUnsupported, ErrorCategory.Authorization);
        RecordQuery constrained = BasePolicyRuntimeSimulation.ComposePolicyFilter(query, authorization.Value.EffectiveRecordFilter);
        if (!WithinQueryLimits(constrained.Filter, profile.Limits))
            return Failure(OperationStatus.ValidationFailed, BaseSelectionErrorCodes.LimitExceeded, ErrorCategory.Validation);
        BaseResult<bool> subjectAuthorization = await AuthorizeSubjectValidationsAsync(
            session, collection, operation, patch, cancellationToken).ConfigureAwait(false);
        if (subjectAuthorization is BaseFailure<bool> subjectFailure)
            return new BaseFailure<BaseSelectionMutationResult>(subjectFailure.Status, subjectFailure.Error, subjectFailure.Warnings, subjectFailure.Diagnostics);
        BaseRecordMutationKind mutationKind = profile.MutationKind == BaseSelectionMutationKind.MergePatch
            ? BaseRecordMutationKind.Patch : BaseRecordMutationKind.Delete;
        OperationResult<BaseResolvedMutationStore> resolved = stores.Resolve(collection, mutationKind, operation);
        if (!resolved.IsSuccess() || resolved.Value?.AtomicStore is null
            || resolved.Value.Store.Capabilities.SelectionMutation is not { IsSupported: true } capability
            || !CapabilitySupports(profile, capability))
            return Failure(OperationStatus.Unsupported, BaseSelectionErrorCodes.CapabilityMissing, ErrorCategory.Unsupported);

        OperationResult<BaseAuthoritySnapshotRequirement> authority = await resolved.Value.AtomicStore
            .CaptureSelectionAuthorityAsync(profile.ApplicationId, collection, cancellationToken).ConfigureAwait(false);
        if (!authority.IsSuccess() || authority.Value is null
            || string.IsNullOrWhiteSpace(authority.Value.StoreInstanceId))
            return Failure(OperationStatus.Conflict, BaseSelectionErrorCodes.SchemaGenerationChanged, ErrorCategory.Conflict);

        RecordQuery providerQuery = BaseQueryFieldResolver.ToStoredNames(collection, constrained);
        var processor = new BaseSelectionMutationProcessor(
            session.Principal, operation, collection, profile, providerQuery, patch, normalizedPreviousState, policy,
            authorization.Value, resolved.Value, authority.Value, subjects);
        var executionRequest = new RecordMutationExecutionRequest
        {
            AcquisitionTimeout = profile.Limits.AcquisitionTimeout,
            TransactionTimeout = profile.Limits.ExecutionTimeout,
            CommitCompletionTimeout = options?.CallerWaitTimeout ?? profile.Limits.CallerCommitObservationTimeout,
            AtomicRequest = identity is null ? null : new BaseAtomicMutationExecutionRequest
            {
                Identity = identity,
                StructuralDigest = StructuralDigest(profile, constrained, patch, normalizedPreviousState, take),
                ExpiresAt = timeProvider.GetUtcNow().AddDays(30),
                MaxReceiptBytes = checked((int)Math.Min(profile.Limits.MaximumReceiptBytes, int.MaxValue)),
            },
        };
        RecordMutationExecutionResult execution;
        try
        {
            execution = await resolved.Value.AtomicStore.ExecuteAtomicAsync(processor, executionRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(OperationStatus.StoreError, BaseSelectionErrorCodes.Cancelled, ErrorCategory.Store);
        }
        catch (Exception)
        {
            return Failure(OperationStatus.StoreError, "base.runtime.store.error", ErrorCategory.Store);
        }
        if (execution.Outcome == RecordMutationExecutionOutcome.Indeterminate)
            return Failure(OperationStatus.StoreError, BaseSelectionErrorCodes.CommitIndeterminate, ErrorCategory.Store);
        BaseSelectionMutationResult? completed = processor.Result;
        if (execution.RequestDisposition == BaseMutationRequestDisposition.Duplicate
            && execution.Processing?.Receipt.SelectionMutation is { } stored)
            completed = new BaseSelectionMutationResult
            {
                SelectedCount = stored.SelectedCount,
                MutatedCount = stored.MutatedCount,
                Outcome = stored.Outcome,
                RequestDisposition = BaseMutationRequestDisposition.Duplicate,
            };
        if (execution.Outcome != RecordMutationExecutionOutcome.Committed || completed is null)
        {
            BaseError error = execution.Processing?.Error ?? execution.Error
                ?? new BaseError { Code = BaseSelectionErrorCodes.TransactionConflict, Message = "The selection mutation rolled back.", Category = ErrorCategory.Conflict };
            return new BaseFailure<BaseSelectionMutationResult>(
                error.Category == ErrorCategory.Conflict ? OperationStatus.Conflict : OperationStatus.StoreError,
                error, null, null);
        }
        if (execution.RequestDisposition != BaseMutationRequestDisposition.Duplicate)
            foreach (BaseMutationAttempt attempt in processor.Attempts)
                _ = await postCommit.DispatchAsync(attempt, session.Principal).ConfigureAwait(false);
        return new BaseSuccess<BaseSelectionMutationResult>(completed with
        {
            RequestDisposition = execution.RequestDisposition,
        }, OperationStatus.Ok, null, null, null, null);
    }

    private async ValueTask<BaseResult<bool>> AuthorizeSubjectValidationsAsync(
        BaseSession session,
        CollectionDefinition collection,
        OperationContext sourceOperation,
        RecordPatchRequest? patch,
        CancellationToken cancellationToken)
    {
        if (patch?.Patch.Fields is not { } fields)
            return new BaseSuccess<bool>(true, OperationStatus.Ok, null, null, null, null);
        BaseSubjectReferenceDefinition[] references = (collection.Fields ?? [])
            .Where(field => field.SubjectReference is not null
                && fields.TryGetValue(field.WireName, out System.Text.Json.JsonElement value)
                && value.ValueKind != System.Text.Json.JsonValueKind.Null)
            .Select(static field => field.SubjectReference!)
            .DistinctBy(static reference => (reference.ContractId, reference.ContractVersion))
            .ToArray();
        foreach (BaseSubjectReferenceDefinition reference in references)
        {
            BaseGeneratedSubjectRegistration? target = subjects.Find(reference.ContractId, reference.ContractVersion);
            if (target is null)
                return Failure<bool>(OperationStatus.ValidationFailed, BaseSubjectErrorCodes.ContractInvalid, ErrorCategory.Validation);
            OperationResult<BasePolicyEvaluation> result = await policy.EvaluateWriteAsync(new BasePolicyRequest
            {
                Principal = session.Principal,
                Operation = sourceOperation with
                {
                    Operation = BaseOperationKind.SubjectValidate,
                    CollectionId = target.Definition.Id,
                    RecordId = null,
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
                    Store = collection.Store,
                },
                ResourceKind = PolicyResourceKind.SubjectContract,
                SubjectContractId = target.Definition.Id,
                SubjectContractVersion = target.Definition.Version,
            }, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || !BaseSystemCollectionGate.HasExactGrant(result, target.Definition.ValidationGrantId))
                return Failure<bool>(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.ReferenceInvalid, ErrorCategory.Authorization);
        }
        return new BaseSuccess<bool>(true, OperationStatus.Ok, null, null, null, null);
    }

    private static BaseFailure<BaseSelectionMutationResult> Failure(OperationStatus status, string code, ErrorCategory category) =>
        new(status, new BaseError { Code = code, Message = "The selection mutation could not be completed.", Category = category }, null, null);

    private static BaseFailure<T> Failure<T>(OperationStatus status, string code, ErrorCategory category) =>
        new(status, new BaseError { Code = code, Message = "The selection mutation could not be completed.", Category = category }, null, null);

    private static bool CapabilitySupports(BaseSelectionOperationProfile profile, BaseSelectionMutationCapability capability)
    {
        BaseSelectionOperationLimits required = profile.Limits;
        BaseSelectionOperationLimits maximum = capability.CertifiedMaxima;
        return capability.ReceiptEnvelopeFormatVersions.Contains(2)
            && capability.CanonicalCodecVersions.Contains(1)
            && capability.SupportsReceiptOnlyCommit
            && capability.SuppliesReadIntervalEvidence
            && capability.SupportsReadYourWrites
            && capability.SupportsBoundedCancellation
            && capability.SupportsBoundedCommitObservation
            && required.MaximumQueryNodes <= maximum.MaximumQueryNodes
            && required.MaximumQueryDepth <= maximum.MaximumQueryDepth
            && required.MaximumLiteralValues <= maximum.MaximumLiteralValues
            && required.MaximumSelectedRecords <= maximum.MaximumSelectedRecords
            && required.MaximumSelectedBytes <= maximum.MaximumSelectedBytes
            && required.MaximumProducedMutations <= maximum.MaximumProducedMutations
            && required.MaximumQueryExecutions <= maximum.MaximumQueryExecutions
            && required.MaximumReadIntervals <= maximum.MaximumReadIntervals
            && required.MaximumWrittenBytes <= maximum.MaximumWrittenBytes
            && required.MaximumFactBytes <= maximum.MaximumFactBytes
            && required.MaximumJournalBytes <= maximum.MaximumJournalBytes
            && required.MaximumReceiptBytes <= maximum.MaximumReceiptBytes
            && required.MaximumRelationChecks <= maximum.MaximumRelationChecks
            && required.MaximumUniqueConstraintChecks <= maximum.MaximumUniqueConstraintChecks
            && required.MaximumPreviousStateRequirements <= maximum.MaximumPreviousStateRequirements
            && required.MaximumTransientBytes <= maximum.MaximumTransientBytes
            && required.MaximumResultBytes <= maximum.MaximumResultBytes
            && required.AcquisitionTimeout <= maximum.AcquisitionTimeout
            && required.ExecutionTimeout <= maximum.ExecutionTimeout
            && required.CallerCommitObservationTimeout <= maximum.CallerCommitObservationTimeout;
    }

    private static byte[] StructuralDigest(BaseSelectionOperationProfile profile, RecordQuery query,
        RecordPatchRequest? patch, BasePreviousStateRequirement previousState, int take)
    {
        byte[] bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new BaseSelectionDigestInput
        {
            ProfileId = profile.Id,
            ProfileVersion = profile.Version,
            ApplicationId = profile.ApplicationId,
            CollectionId = profile.CollectionId,
            MutationKind = profile.MutationKind,
            Query = query,
            Patch = patch,
            PreviousState = previousState,
            Take = take,
        }, BaseSelectionJsonSerializerContext.Default.BaseSelectionDigestInput);
        return System.Security.Cryptography.SHA256.HashData(bytes);
    }

    private static BasePreviousStateRequirement? NormalizePreviousState(
        CollectionDefinition collection,
        BasePreviousStateRequirement requirement,
        int maximumFields)
    {
        if (!Enum.IsDefined(requirement.Revision.Kind)
            || requirement.Revision.Kind == BaseRevisionRequirementKind.Exact != (requirement.Revision.ExactRevision is not null)
            || requirement.Fields.IsDefault
            || requirement.Fields.Length > maximumFields)
            return null;
        Dictionary<string, FieldDefinition> fields = (collection.Fields ?? [])
            .ToDictionary(static field => field.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = ImmutableArray.CreateBuilder<BasePreviousFieldRequirement>(requirement.Fields.Length);
        foreach (BasePreviousFieldRequirement item in requirement.Fields.OrderBy(static item => item.FieldId, StringComparer.Ordinal))
        {
            if (!Enum.IsDefined(item.Kind) || !seen.Add(item.FieldId)
                || !fields.TryGetValue(item.FieldId, out FieldDefinition? field)
                || item.Kind == BasePreviousFieldRequirementKind.Equal != (item.Value is not null))
                return null;
            normalized.Add(new BasePreviousFieldRequirement
            {
                FieldId = new string(field.WireName.AsSpan()),
                Kind = item.Kind,
                Value = item.Value is null ? null : CloneQueryValue(item.Value),
            });
        }
        return new BasePreviousStateRequirement
        {
            Revision = requirement.Revision with { },
            Fields = normalized.MoveToImmutable(),
        };
    }

    private static QueryValue CloneQueryValue(QueryValue value) => value with
    {
        String = value.String is null ? null : new string(value.String.AsSpan()),
        Decimal = value.Decimal is null ? null : new string(value.Decimal.AsSpan()),
        Id = value.Id is null ? null : new string(value.Id.AsSpan()),
        Array = value.Array?.Select(CloneQueryValue).ToArray(),
    };

    private static bool WithinQueryLimits(FilterExpression? filter, BaseSelectionOperationLimits limits)
    {
        int nodes = 0, literals = 0;
        bool Visit(FilterExpression node, int depth)
        {
            if (depth > limits.MaximumQueryDepth || ++nodes > limits.MaximumQueryNodes) return false;
            try
            {
                literals = checked(literals + (node.Value is null ? 0 : 1) + (node.Values?.Length ?? 0) + (node.Arguments?.Length ?? 0));
            }
            catch (OverflowException) { return false; }
            if (literals > limits.MaximumLiteralValues) return false;
            foreach (FilterExpression child in node.Children ?? []) if (!Visit(child, depth + 1)) return false;
            return true;
        }
        return filter is null || Visit(filter, 1);
    }

    internal static bool WithinSubjectLimits(
        ImmutableArray<BaseSubjectReferenceValidationPlanItem> validations,
        BaseGeneratedSubjectRegistration[] participating)
    {
        if (participating.Length == 0)
            return validations.Length == 0;
        int distinctPlans = validations
            .Select(static validation => (validation.ValidationPlanId, validation.ValidationPlanVersion))
            .Distinct().Count();
        if (participating.Any(registration =>
                validations.Length > registration.Definition.ValidationPlan.Limits.MaximumReferencesPerMutation
                || distinctPlans > registration.Definition.ValidationPlan.Limits.MaximumValidationPlansPerMutation))
            return false;
        foreach (IGrouping<int, BaseSubjectReferenceValidationPlanItem> record in validations.GroupBy(static validation => validation.MutationOrdinal))
        {
            BaseGeneratedSubjectRegistration[] targets = record.Select(validation => participating.Single(candidate =>
                    candidate.Definition.ValidationPlan.Id == validation.ValidationPlanId
                    && candidate.Definition.ValidationPlan.Version == validation.ValidationPlanVersion))
                .DistinctBy(static registration => (registration.Definition.Id, registration.Definition.Version))
                .ToArray();
            if (targets.Any(target => record.Count() > target.Definition.ValidationPlan.Limits.MaximumReferencesPerRecord))
                return false;
        }
        return true;
    }

    private static bool IsTotalOrder(QuerySort[] sort) =>
        string.Equals(sort[^1].Field, "id", StringComparison.Ordinal)
        && sort.Count(static item => string.Equals(item.Field, "id", StringComparison.Ordinal)) == 1
        && sort[^1].Nulls == QueryNullOrder.Unspecified;
}

internal sealed record BaseSelectionDigestInput
{
    public required string ProfileId { get; init; }
    public required int ProfileVersion { get; init; }
    public required string ApplicationId { get; init; }
    public required string CollectionId { get; init; }
    public required BaseSelectionMutationKind MutationKind { get; init; }
    public required RecordQuery Query { get; init; }
    public RecordPatchRequest? Patch { get; init; }
    public required BasePreviousStateRequirement PreviousState { get; init; }
    public required int Take { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(BaseSelectionDigestInput))]
internal partial class BaseSelectionJsonSerializerContext : JsonSerializerContext;

internal sealed class BaseSelectionMutationProcessor(
    PrincipalContext principal,
    OperationContext operation,
    CollectionDefinition collection,
    BaseSelectionOperationProfile profile,
    RecordQuery query,
    RecordPatchRequest? patch,
    BasePreviousStateRequirement previousState,
    IBasePolicyOrchestrator policy,
    BasePolicyEvaluation operationPolicy,
    BaseResolvedMutationStore store,
    BaseAuthoritySnapshotRequirement authority,
    BaseSubjectContractRegistry subjects) : IAtomicMutationProcessor
{
    internal BaseSelectionMutationResult? Result { get; private set; }
    internal IReadOnlyList<BaseMutationAttempt> Attempts => _attempts;
    private readonly List<BaseMutationAttempt> _attempts = [];

    public async ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseAtomicReceiptResult committedResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (committedResult.Kind != BaseAtomicReceiptResultKind.SelectionMutation
            || committedResult.SelectionMutation is not { } stored
            || !string.Equals(stored.ApplicationId, profile.ApplicationId, StringComparison.Ordinal)
            || !string.Equals(stored.CollectionId, collection.Id, StringComparison.Ordinal)
            || !string.Equals(stored.OperationProfileId, profile.Id, StringComparison.Ordinal)
            || stored.OperationProfileVersion != profile.Version
            || !string.Equals(stored.ReceiptScope, principal.CurrentTenantId ?? string.Empty, StringComparison.Ordinal))
            return Failed(BaseMutationRequestErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization);
        OperationResult<BasePolicyEvaluation> disclosure = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = operation,
            Collection = collection,
            ResourceKind = PolicyResourceKind.SelectionMutation,
        }, cancellationToken).ConfigureAwait(false);
        if (!disclosure.IsSuccess() || disclosure.Value is null
            || !BaseSystemCollectionGate.HasExactGrant(disclosure, profile.RequiredGrantId))
            return Failed(BaseMutationRequestErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization);
        foreach (BaseOwnedMutationFact owned in committedResult.Mutations)
        {
            BaseRecordMutationFact fact;
            try { fact = owned.MaterializeOwned(); }
            catch { return Failed(BaseMutationRequestErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization); }
            RecordEnvelope? resource = fact.After ?? fact.Before;
            if (!string.Equals(fact.Collection.Id, collection.Id, StringComparison.Ordinal) || resource is null)
                return Failed(BaseMutationRequestErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization);
            OperationResult<BasePolicyEvaluation> item = await policy.EvaluateWriteAsync(new BasePolicyRequest
            {
                Principal = principal,
                Operation = operation with { RecordId = resource.Id.Value },
                Collection = collection,
                ResourceKind = PolicyResourceKind.SelectionMutation,
                ExistingRecord = resource,
                RecordId = resource.Id,
            }, cancellationToken).ConfigureAwait(false);
            if (!item.IsSuccess() || item.Value is null || !BaseSystemCollectionGate.HasExactGrant(item, profile.RequiredGrantId))
                return Failed(BaseMutationRequestErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization);
        }
        Result = new BaseSelectionMutationResult
        {
            SelectedCount = stored.SelectedCount,
            MutatedCount = stored.MutatedCount,
            Outcome = stored.Outcome,
            RequestDisposition = BaseMutationRequestDisposition.Duplicate,
        };
        return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, committedResult);
    }

    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
        IAtomicRecordSession session,
        CancellationToken cancellationToken = default)
    {
        OperationResult<BaseAtomicSelectionResult> selected = await session.SelectAsync(new BaseAtomicSelectionRequest
        {
            Collection = collection,
            Query = query,
            Limits = new BaseAtomicSelectionLimits
            {
                MaximumRecords = profile.Limits.MaximumSelectedRecords,
                MaximumSelectedBytes = profile.Limits.MaximumSelectedBytes,
                MaximumReadIntervals = profile.Limits.MaximumReadIntervals,
                MaximumTransientBytes = profile.Limits.MaximumTransientBytes,
                MaximumUniqueConstraintChecks = profile.Limits.MaximumUniqueConstraintChecks,
            },
            Authority = authority,
            CanonicalRecordCodecVersion = 1,
        }, cancellationToken).ConfigureAwait(false);
        if (!selected.IsSuccess() || selected.Value is null)
            return Failed(MapProviderFailure(selected));
        if (!ValidateSelection(selected.Value))
            return Failed("base.runtime.store.error", ErrorCategory.Store);
        BaseCapturedAtomicMutationAuthority captured = selected.Value.MutationCapture;
        if (!SelectionCaptureMatches(selected.Value, captured))
            return Failed(BaseSubjectErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
        var planItems = ImmutableArray.CreateBuilder<BaseAtomicMutationPlanItem>(selected.Value.Records.Length);
        var policies = new List<BasePolicyEvaluation>(selected.Value.Records.Length + 1) { operationPolicy };
        foreach (BaseOwnedSelectedRecord owned in selected.Value.Records)
        {
            RecordEnvelope record = owned.MaterializeOwned();
            if (!PreviousStateMatches(record, previousState))
                return Failed(BaseSelectionErrorCodes.TransactionConflict, ErrorCategory.Conflict);
            OperationResult<BasePolicyEvaluation> authorized = await policy.EvaluateWriteAsync(new BasePolicyRequest
            {
                Principal = principal,
                Operation = operation with { RecordId = record.Id.Value },
                Collection = collection,
                ResourceKind = profile.MutationKind == BaseSelectionMutationKind.MergePatch
                    ? PolicyResourceKind.UpdatePayload : PolicyResourceKind.DeleteCandidate,
                ExistingRecord = record,
                ProposedPayload = patch?.Patch,
                RecordId = record.Id,
            }, cancellationToken).ConfigureAwait(false);
            if (!authorized.IsSuccess() || authorized.Value is null)
                return Failed(BaseSelectionErrorCodes.PolicyUnsupported, ErrorCategory.Authorization);
            policies.Add(authorized.Value);
            RecordPayload? proposed = profile.MutationKind == BaseSelectionMutationKind.MergePatch
                ? BasePolicyRuntimeSimulation.MergePatchPayload(record.Payload, patch!.Patch)
                : null;
            planItems.Add(new BaseAtomicMutationPlanItem
            {
                Ordinal = planItems.Count,
                ItemId = $"selection:{planItems.Count}",
                EventId = Guid.NewGuid().ToString("N"),
                Collection = collection with { Fields = collection.Fields?.Select(static field => field with { }).ToArray() },
                Kind = profile.MutationKind == BaseSelectionMutationKind.MergePatch
                    ? BaseCommittedRecordMutationKind.Patch : BaseCommittedRecordMutationKind.Delete,
                RequestedKind = profile.MutationKind == BaseSelectionMutationKind.MergePatch
                    ? BaseRecordMutationKind.Patch : BaseRecordMutationKind.Delete,
                RecordId = record.Id,
                ProposedPayload = proposed,
                Delete = profile.MutationKind == BaseSelectionMutationKind.Delete
                    ? new RecordDeleteRequest { ReturnPrevious = true, ExpectedRevision = record.Metadata.Revision }
                    : null,
                Current = record,
                ChangedFields = profile.MutationKind == BaseSelectionMutationKind.MergePatch
                    ? (patch!.Patch.Fields ?? []).Keys.Order(StringComparer.Ordinal).ToImmutableArray()
                    : [],
                Operation = operation with { RecordId = record.Id.Value },
            });
        }
        ImmutableArray<BaseAtomicMutationPlanItem> finalized = planItems.MoveToImmutable();
        OperationResult<(ImmutableArray<BaseAtomicMutationPlanItem> Items, ImmutableArray<BaseSubjectReferenceValidationPlanItem> Validations)> subjectPlan =
            BuildSelectionSubjectPlan(finalized, policies);
        if (!subjectPlan.IsSuccess() || subjectPlan.Value == default)
            return Failed(subjectPlan.Error ?? Error(BaseSubjectErrorCodes.ContractInvalid, ErrorCategory.Validation));
        finalized = subjectPlan.Value.Items;
        BaseGeneratedSubjectRegistration[] participatingSubjects = subjectPlan.Value.Validations
            .Select(validation => subjects.All.SingleOrDefault(candidate =>
                candidate.Definition.ValidationPlan.Id == validation.ValidationPlanId
                && candidate.Definition.ValidationPlan.Version == validation.ValidationPlanVersion))
            .Concat(finalized.Where(static item => item.SubjectLifecycle is not null).Select(item =>
                subjects.Find(item.SubjectLifecycle!.ContractId, item.SubjectLifecycle.ContractVersion)))
            .Where(static registration => registration is not null)
            .Select(static registration => registration!)
            .DistinctBy(static registration => (registration.Definition.Id, registration.Definition.Version))
            .ToArray();
        if (!DefaultBaseSelectionMutationRuntime.WithinSubjectLimits(subjectPlan.Value.Validations, participatingSubjects))
            return Failed(BaseSelectionErrorCodes.LimitExceeded, ErrorCategory.Validation);
        int subjectValidationLimit = participatingSubjects.Length == 0
            ? profile.Limits.MaximumProducedMutations
            : Math.Min(profile.Limits.MaximumProducedMutations,
                participatingSubjects.Min(static registration => registration.Definition.ValidationPlan.Limits.MaximumReferencesPerMutation));
        int authorityReadLimit = participatingSubjects.Length == 0
            ? profile.Limits.MaximumReadIntervals
            : Math.Min(profile.Limits.MaximumReadIntervals,
                participatingSubjects.Min(static registration => registration.Definition.ValidationPlan.Limits.MaximumAuthorityReads));
        int intervalLimit = participatingSubjects.Length == 0
            ? profile.Limits.MaximumReadIntervals
            : Math.Min(profile.Limits.MaximumReadIntervals,
                participatingSubjects.Min(static registration => registration.Definition.ValidationPlan.Limits.MaximumReadIntervals));
        long selectedByteLimit = participatingSubjects.Length == 0
            ? profile.Limits.MaximumSelectedBytes
            : Math.Min(profile.Limits.MaximumSelectedBytes,
                participatingSubjects.Min(static registration => registration.Definition.ValidationPlan.Limits.MaximumSelectedBytes));
        long evidenceByteLimit = participatingSubjects.Length == 0
            ? profile.Limits.MaximumTransientBytes
            : Math.Min(profile.Limits.MaximumTransientBytes,
                participatingSubjects.Min(static registration => registration.Definition.ValidationPlan.Limits.MaximumEvidenceBytes));
        long transientByteLimit = participatingSubjects.Length == 0
            ? profile.Limits.MaximumTransientBytes
            : Math.Min(profile.Limits.MaximumTransientBytes,
                participatingSubjects.Min(static registration => registration.Definition.ValidationPlan.Limits.MaximumTransientBytes));
        TimeSpan executionTimeout = participatingSubjects.Length == 0
            ? profile.Limits.ExecutionTimeout
            : participatingSubjects.Select(static registration => registration.Definition.ValidationPlan.Limits.ExecutionTimeout)
                .Append(profile.Limits.ExecutionTimeout).Min();
        var limits = new BaseAtomicMutationExecutionLimits
        {
            MaximumItems = profile.Limits.MaximumProducedMutations,
            MaximumQueryNodes = profile.Limits.MaximumQueryNodes,
            MaximumQueryDepth = profile.Limits.MaximumQueryDepth,
            MaximumLiteralValues = profile.Limits.MaximumLiteralValues,
            MaximumSelectedRecords = profile.Limits.MaximumSelectedRecords,
            MaximumProducedMutations = profile.Limits.MaximumProducedMutations,
            MaximumQueryExecutions = profile.Limits.MaximumQueryExecutions,
            MaximumPreviousStateRequirements = profile.Limits.MaximumPreviousStateRequirements,
            MaximumRecordCaptures = 1,
            MaximumRelationTargetCaptures = profile.Limits.MaximumProducedMutations,
            MaximumGenerationReads = 1,
            MaximumGenerationComparisons = 1,
            MaximumGenerationIncrements = 1,
            MaximumGuardNodes = 1,
            MaximumGuardDepth = 1,
            MaximumStatements = profile.Limits.MaximumProducedMutations,
            MaximumBranches = 1,
            MaximumExpressionNodes = 1,
            MaximumSelectedBytes = selectedByteLimit,
            MaximumEvidenceBytes = evidenceByteLimit,
            MaximumTransientBytes = transientByteLimit,
            MaximumReadIntervals = intervalLimit,
            MaximumSubjectValidations = subjectValidationLimit,
            MaximumAuthorityReads = authorityReadLimit,
            MaximumRelationChecks = profile.Limits.MaximumProducedMutations,
            MaximumUniqueConstraintChecks = profile.Limits.MaximumUniqueConstraintChecks,
            MaximumRequestBytes = profile.Limits.MaximumTransientBytes,
            MaximumGenerationBytes = 1,
            MaximumWrittenBytes = profile.Limits.MaximumTransientBytes,
            MaximumFactBytes = profile.Limits.MaximumTransientBytes,
            MaximumJournalBytes = profile.Limits.MaximumTransientBytes,
            MaximumReceiptBytes = profile.Limits.MaximumTransientBytes,
            MaximumResultBytes = profile.Limits.MaximumTransientBytes,
            Deadlines = new BaseAtomicMutationDeadlines
            {
                AcquisitionTimeout = executionTimeout,
                TransactionTimeout = executionTimeout,
                CommitObservationTimeout = executionTimeout,
                ReceiptResolutionTimeout = executionTimeout,
            },
        };
        if (!BaseAtomicPolicyAuthority.IsAdmissible(policies))
            return Failed(new BaseError
            {
                Code = BasePolicyAuthorityErrorCodes.Invalid,
                Message = "The mutation policy authority is invalid.",
                Category = ErrorCategory.Authorization,
            });
        BaseAtomicPolicyAuthorityDigest policyDigest = BaseAtomicPolicyAuthority.Compute(
            authority.ApplicationId, $"{profile.Id}:{profile.Version}", policies);
        var mutationPlan = new BaseAtomicMutationPlan
        {
            Kind = BaseAtomicMutationExecutionKind.SelectionMutation,
            IntentDigest = captured.IntentDigest,
            CaptureDigest = captured.CaptureDigest,
            PolicyAuthorityDigest = policyDigest,
            Authority = new BaseAtomicMutationAuthorityRequirement
            {
                ApplicationId = authority.ApplicationId,
                StoreInstanceId = authority.StoreInstanceId,
                RestoreEpoch = authority.RestoreEpoch,
                SchemaGeneration = authority.SchemaGeneration,
                Collections = [new BaseCollectionGenerationRequirement
                {
                    CollectionId = collection.Id,
                    CollectionGeneration = authority.CollectionGeneration,
                }],
            },
            Items = finalized,
            SubjectValidations = subjectPlan.Value.Validations,
            Limits = limits,
            PlanDigest = BaseAtomicPolicyAuthority.BindPlanDigest(
                SelectionPlanDigest(captured, finalized, subjectPlan.Value.Validations), policyDigest),
        };
        BaseAtomicMutationPlan retainedPlan = BaseAtomicMutationOwnership.FreezePlan(mutationPlan);
        BaseAtomicMutationPlan providerPlan = BaseAtomicMutationOwnership.FreezePlan(retainedPlan);
        OperationResult<BasePreparedAtomicMutation> prepared = await session.PrepareAtomicMutationAsync(captured, providerPlan, cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess() || prepared.Value is null || !PreparedMatches(retainedPlan, captured, prepared.Value))
            return !prepared.IsSuccess() || prepared.Value is null
                ? HasSubjectWork(retainedPlan)
                    ? Failed(BaseSubjectFailureContract.NormalizeProviderError(prepared.Status, prepared.Error))
                    : Failed(prepared.Error ?? Error("base.runtime.store.error", ErrorCategory.Store))
                : Failed(BaseSubjectFailureContract.Error(BaseSubjectErrorCodes.ProviderContractInvalid));
        if (prepared.Value.SubjectValidations.Any(static validation => validation.State == BaseSubjectValidationState.Invalid))
            return Failed(BaseSubjectErrorCodes.ReferenceInvalid, ErrorCategory.Validation);
        OperationResult<BaseProvisionalAppliedAtomicMutation> applied = await session.ApplyPreparedAtomicMutationAsync(prepared.Value, cancellationToken).ConfigureAwait(false);
        if (!applied.IsSuccess() || applied.Value is null || !AppliedMatches(retainedPlan, prepared.Value, applied.Value))
            return !applied.IsSuccess() || applied.Value is null
                ? HasSubjectWork(retainedPlan)
                    ? Failed(BaseSubjectFailureContract.NormalizeProviderError(applied.Status, applied.Error))
                    : Failed(applied.Error ?? Error("base.runtime.store.error", ErrorCategory.Store))
                : Failed(BaseSubjectFailureContract.Error(BaseSubjectErrorCodes.ProviderContractInvalid));
        BaseRecordMutationFact[] facts;
        try { facts = applied.Value.Facts.Select(static fact => fact.MaterializeOwned()).ToArray(); }
        catch { return Failed(BaseSubjectErrorCodes.ProviderContractInvalid, ErrorCategory.Store); }
        for (int index = 0; index < facts.Length; index++)
        {
            BaseRecordMutationFact mutation = facts[index];
            BaseAtomicMutationPlanItem item = finalized[index];
            _attempts.Add(new BaseMutationAttempt
            {
                Command = new BaseMutationCommand
                {
                    Index = index, ItemId = item.ItemId ?? $"selection:{index}", CollectionId = collection.Id,
                    Kind = item.RequestedKind, Collection = collection, Context = item.Operation,
                    EventId = item.EventId, Store = store, RecordId = item.RecordId,
                    Patch = patch,
                    Delete = item.Delete,
                },
                Status = mutation.CommittedOperation == BaseCommittedRecordMutationKind.Delete ? OperationStatus.Deleted : OperationStatus.Updated,
                Mutation = mutation,
                Policy = policies[index],
                Revision = mutation.After?.Metadata.Revision is { } revision
                    ? new RevisionInfo { Revision = revision.Value, Guarantee = RevisionGuarantee.Store }
                    : null,
            });
        }
        Result = new BaseSelectionMutationResult
        {
            SelectedCount = selected.Value.Records.Length,
            MutatedCount = facts.Length,
            Outcome = BaseRecordBatchOutcome.Committed,
        };
        var receipt = new BaseAtomicReceiptResult
            {
                Kind = BaseAtomicReceiptResultKind.SelectionMutation,
                Mutations = applied.Value.Facts,
                SelectionMutation = new BaseSelectionMutationReceiptResult
                {
                    ApplicationId = profile.ApplicationId,
                    CollectionId = collection.Id,
                    OperationProfileId = profile.Id,
                    OperationProfileVersion = profile.Version,
                    ReceiptScope = principal.CurrentTenantId ?? string.Empty,
                    SelectedCount = selected.Value.Records.Length,
                    MutatedCount = facts.Length,
                    Outcome = BaseRecordBatchOutcome.Committed,
                },
            };
        OperationResult<BaseSelectionMutationCommitAccounting> measured = await session.MeasureSelectionMutationAsync(receipt, Result, cancellationToken).ConfigureAwait(false);
        if (!measured.IsSuccess() || measured.Value is null || !WithinAccounting(measured.Value, profile.Limits, facts.Length))
            return Failed(BaseSelectionErrorCodes.LimitExceeded, ErrorCategory.Validation);
        return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, receipt);
    }

    private bool SelectionCaptureMatches(BaseAtomicSelectionResult selection, BaseCapturedAtomicMutationAuthority captured)
    {
        if (captured.Items.Length != selection.Records.Length
            || captured.ReadIntervals.Length != selection.ReadIntervals.Length
            || !string.Equals(captured.Authority.StoreInstanceId, selection.Authority.StoreInstanceId, StringComparison.Ordinal)
            || captured.Authority.RestoreEpoch != selection.Authority.RestoreEpoch
            || captured.Authority.SchemaGeneration != selection.Authority.SchemaGeneration
            || captured.Authority.Collections.Length != 1
            || captured.Authority.Collections[0].CollectionGeneration != selection.Authority.CollectionGeneration)
            return false;
        for (int index = 0; index < selection.Records.Length; index++)
        {
            BaseCapturedMutationItem item = captured.Items[index];
            BaseOwnedSelectedRecord selected = selection.Records[index];
            if (item.Ordinal != index || item.Current is null
                || !string.Equals(item.CollectionId, collection.Id, StringComparison.Ordinal)
                || !string.Equals(item.RecordId.Value, selected.RecordId, StringComparison.Ordinal)
                || item.Current.CollectionId != collection.Id || item.Current.Id.Value != selected.RecordId
                || item.Current.Metadata.Revision != selected.Revision
                || item.RelationTargets.Length != 0)
                return false;
        }
        return true;
    }

    private OperationResult<(ImmutableArray<BaseAtomicMutationPlanItem> Items, ImmutableArray<BaseSubjectReferenceValidationPlanItem> Validations)> BuildSelectionSubjectPlan(
        ImmutableArray<BaseAtomicMutationPlanItem> items,
        IReadOnlyList<BasePolicyEvaluation> policies)
    {
        var finalized = ImmutableArray.CreateBuilder<BaseAtomicMutationPlanItem>(items.Length);
        var validations = ImmutableArray.CreateBuilder<BaseSubjectReferenceValidationPlanItem>();
        foreach (BaseAtomicMutationPlanItem item in items)
        {
            BaseGeneratedSubjectRegistration[] lifecycleContracts = subjects.All.Where(subject =>
                string.Equals(subject.Definition.ValidationPlan.PrivateCollectionId, item.Collection.Id, StringComparison.Ordinal)).ToArray();
            if (lifecycleContracts.Length > 1)
                return SubjectPlanFailure();
            BaseSubjectLifecyclePlanItem? lifecycle = null;
            if (lifecycleContracts.Length == 1)
            {
                BaseGeneratedSubjectRegistration subject = lifecycleContracts[0];
                BaseSubjectId id;
                try { id = BaseSubjectId.Create(item.RecordId.Value, subject.Definition.SubjectIdKind, subject.Definition.MaximumSubjectIdUtf8Bytes); }
                catch { return SubjectPlanFailure(); }
                lifecycle = new BaseSubjectLifecyclePlanItem
                {
                    ContractId = subject.Definition.Id,
                    ContractVersion = subject.Definition.Version,
                    ContractChecksum = subject.Checksum,
                    SubjectId = id,
                    Kind = item.Kind == BaseCommittedRecordMutationKind.Delete
                        ? BaseSubjectLifecycleMutationKind.Retire
                        : BaseSubjectLifecycleMutationKind.Preserve,
                };
                if (item.Kind != BaseCommittedRecordMutationKind.Delete &&
                    !HasValidSubjectLogicalState(item, subject.Definition))
                    return SubjectPlanFailure();
            }
            if (item.Kind == BaseCommittedRecordMutationKind.Patch && item.ProposedPayload?.Fields is { } fields)
            {
                foreach (FieldDefinition field in (item.Collection.Fields ?? []).Where(field =>
                    field.SubjectReference is not null && item.ChangedFields.Contains(field.WireName, StringComparer.Ordinal)))
                {
                    if (!fields.TryGetValue(field.WireName, out System.Text.Json.JsonElement value)
                        || value.ValueKind == System.Text.Json.JsonValueKind.Null)
                        continue;
                    BaseSubjectReferenceDefinition reference = field.SubjectReference!;
                    BaseGeneratedSubjectRegistration? target = subjects.Find(reference.ContractId, reference.ContractVersion);
                    if (target is null || !TryParseSubjectReference(value, target.Definition, out BaseOwnedSubjectReference? parsed))
                        return SubjectPlanFailure(BaseSubjectErrorCodes.ReferenceInvalid, ErrorCategory.Authorization);
                    string? scope = target.Definition.Scope switch
                    {
                        BaseSubjectScopeKind.Global => null,
                        BaseSubjectScopeKind.Tenant => principal.CurrentTenantId,
                        BaseSubjectScopeKind.Project => item.Operation.ProjectId,
                        _ => null,
                    };
                    if (target.Definition.Scope != BaseSubjectScopeKind.Global && string.IsNullOrWhiteSpace(scope))
                        return SubjectPlanFailure(BaseSubjectErrorCodes.ReferenceInvalid, ErrorCategory.Validation);
                    validations.Add(new BaseSubjectReferenceValidationPlanItem
                    {
                        MutationOrdinal = item.Ordinal,
                        SourceFieldId = field.Id,
                        ValidationPlanId = target.Definition.ValidationPlan.Id,
                        ValidationPlanVersion = target.Definition.ValidationPlan.Version,
                        Requirement = reference.Requirement,
                        Reference = parsed!,
                        Scope = new BaseOwnedSubjectScopeEvidence { Kind = target.Definition.Scope, Value = scope },
                    });
                }
            }
            finalized.Add(item with { SubjectLifecycle = lifecycle });
        }
        return OperationResults.Ok((finalized.MoveToImmutable(), validations.ToImmutable()));
    }

    private static bool HasValidSubjectLogicalState(BaseAtomicMutationPlanItem item, BaseExportedSubjectDefinition definition)
    {
        if (item.ProposedPayload?.Fields is not { } fields)
            return false;
        BaseSubjectValidationPlanDefinition plan = definition.ValidationPlan;
        FieldDefinition[] definitions = item.Collection.Fields ?? [];
        if (plan.Active.Kind == BaseSubjectActiveBindingKind.RequiredBooleanField)
        {
            FieldDefinition? active = definitions.SingleOrDefault(field => field.Id == plan.Active.FieldId);
            if (active is null || !fields.TryGetValue(active.WireName, out System.Text.Json.JsonElement value) ||
                value.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                return false;
        }
        if (plan.Scope.Kind != BaseSubjectScopeBindingKind.Global)
        {
            FieldDefinition? scope = definitions.SingleOrDefault(field => field.Id == plan.Scope.FieldId);
            if (scope is null || !fields.TryGetValue(scope.WireName, out System.Text.Json.JsonElement value) ||
                value.ValueKind != System.Text.Json.JsonValueKind.String || value.GetString() is not { } text)
                return false;
            try { _ = BaseSubjectId.Create(text, BaseSubjectIdKind.OrdinalString, 256); }
            catch { return false; }
        }
        return true;
    }

    private static OperationResult<(ImmutableArray<BaseAtomicMutationPlanItem>, ImmutableArray<BaseSubjectReferenceValidationPlanItem>)> SubjectPlanFailure(
        string code = BaseSubjectErrorCodes.ContractInvalid,
        ErrorCategory category = ErrorCategory.Validation) => new()
    {
        Status = category == ErrorCategory.Authorization ? OperationStatus.PolicyDenied : OperationStatus.ValidationFailed,
        Error = Error(code, category),
    };

    private static bool TryParseSubjectReference(
        System.Text.Json.JsonElement value,
        BaseExportedSubjectDefinition definition,
        out BaseOwnedSubjectReference? reference)
    {
        reference = null;
        if (value.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
        string? subjectId = null, epoch = null, incarnation = null;
        int count = 0;
        foreach (System.Text.Json.JsonProperty property in value.EnumerateObject())
        {
            count++;
            if (property.Value.ValueKind != System.Text.Json.JsonValueKind.String) return false;
            switch (property.Name)
            {
                case "subjectId" when subjectId is null: subjectId = property.Value.GetString(); break;
                case "authorityEpoch" when epoch is null: epoch = property.Value.GetString(); break;
                case "incarnation" when incarnation is null: incarnation = property.Value.GetString(); break;
                default: return false;
            }
        }
        if (count != 3 || subjectId is null || epoch is null || incarnation is null) return false;
        try
        {
            reference = new BaseOwnedSubjectReference(
                BaseSubjectId.Create(subjectId, definition.SubjectIdKind, definition.MaximumSubjectIdUtf8Bytes),
                BaseSubjectAuthorityEpoch.Parse(epoch),
                BaseSubjectIncarnation.Parse(incarnation));
            return true;
        }
        catch { return false; }
    }

    private static string SelectionPlanDigest(
        BaseCapturedAtomicMutationAuthority captured,
        ImmutableArray<BaseAtomicMutationPlanItem> items,
        ImmutableArray<BaseSubjectReferenceValidationPlanItem> validations)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"hpd.base.selection-mutation-plan.v1\0{captured.IntentDigest}\0{captured.CaptureDigest}\0"));
        foreach (BaseAtomicMutationPlanItem item in items)
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"{item.Ordinal}\0{item.Collection.Id}\0{item.RecordId.Value}\0{(int)item.Kind}\0{item.EventId}\0"));
            if (item.ProposedPayload is not null)
                hash.AppendData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(item.ProposedPayload, HPDBaseJsonSerializerContext.Default.RecordPayload));
            if (item.SubjectLifecycle is { } lifecycle)
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"\0subject-lifecycle\0{lifecycle.ContractId}\0{lifecycle.ContractVersion}\0{lifecycle.ContractChecksum}\0{(int)lifecycle.Kind}\0{lifecycle.SubjectId.Value}\0"));
        }
        foreach (BaseSubjectReferenceValidationPlanItem validation in validations)
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"\0subject-validation\0{validation.MutationOrdinal}\0{validation.SourceFieldId}\0{validation.ValidationPlanId}\0{validation.ValidationPlanVersion}\0{(int)validation.Requirement}\0{validation.Reference.SubjectId.Value}\0{validation.Reference.AuthorityEpoch.ToBase64Url()}\0{validation.Reference.Incarnation.ToBase64Url()}\0{(int)validation.Scope.Kind}\0{validation.Scope.Value}\0"));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private bool PreparedMatches(BaseAtomicMutationPlan plan, BaseCapturedAtomicMutationAuthority captured, BasePreparedAtomicMutation prepared) =>
        string.Equals(plan.PlanDigest, prepared.PlanDigest, StringComparison.Ordinal)
        && prepared.Dispositions.Length == plan.Items.Length
        && (!HasSubjectWork(plan) || prepared.Dispositions.Select((disposition, index) => Enum.IsDefined(disposition) && disposition == (plan.Items[index].Kind switch
        {
            BaseCommittedRecordMutationKind.Create => BaseCapturedMutationDisposition.Create,
            BaseCommittedRecordMutationKind.Delete => BaseCapturedMutationDisposition.Delete,
            _ => BaseCapturedMutationDisposition.Update,
        })).All(static valid => valid))
        && prepared.SubjectValidations.Length == plan.SubjectValidations.Length
        && prepared.ReadIntervals.Length >= 1
        && prepared.Accounting.ReadIntervals == prepared.ReadIntervals.Length
        && prepared.Accounting.SelectedBytes <= plan.Limits.MaximumSelectedBytes
        && prepared.Accounting.EvidenceBytes <= plan.Limits.MaximumEvidenceBytes
        && prepared.Accounting.TransientBytes <= plan.Limits.MaximumTransientBytes
        && prepared.Accounting.AuthorityReads <= plan.Limits.MaximumAuthorityReads
        && prepared.Authority.StoreInstanceId == captured.Authority.StoreInstanceId
        && prepared.Authority.RestoreEpoch == captured.Authority.RestoreEpoch
        && prepared.Authority.SchemaGeneration == captured.Authority.SchemaGeneration
        && prepared.Authority.Collections.SequenceEqual(captured.Authority.Collections)
        && (!HasSubjectWork(plan) || prepared.Authority.Isolation == captured.Authority.Isolation
            && prepared.Authority.TransactionEvidenceToken.AsSpan().SequenceEqual(captured.Authority.TransactionEvidenceToken.AsSpan())
            && captured.ReadIntervals.All(expected => prepared.ReadIntervals.Any(actual => IntervalEquals(expected, actual))))
        && PreparedSubjectEvidenceMatches(plan, prepared);

    private static bool IntervalEquals(BaseAtomicReadIntervalEvidence left, BaseAtomicReadIntervalEvidence right) =>
        left.LogicalAccessPathId == right.LogicalAccessPathId
        && left.LowerInclusive == right.LowerInclusive && left.UpperInclusive == right.UpperInclusive
        && left.CanonicalLowerBound.AsSpan().SequenceEqual(right.CanonicalLowerBound.AsSpan())
        && left.CanonicalUpperBound.AsSpan().SequenceEqual(right.CanonicalUpperBound.AsSpan());

    private bool PreparedSubjectEvidenceMatches(BaseAtomicMutationPlan plan, BasePreparedAtomicMutation prepared)
    {
        var expected = new Dictionary<(string Id, int Version), BaseGeneratedSubjectRegistration>();
        foreach (BaseAtomicMutationPlanItem item in plan.Items)
            if (item.SubjectLifecycle is { } lifecycle)
            {
                BaseGeneratedSubjectRegistration? registration = subjects.Find(lifecycle.ContractId, lifecycle.ContractVersion);
                if (registration is null) return false;
                expected[(lifecycle.ContractId, lifecycle.ContractVersion)] = registration;
            }
        foreach (BaseSubjectReferenceValidationPlanItem validation in plan.SubjectValidations)
        {
            BaseGeneratedSubjectRegistration? registration = subjects.All.SingleOrDefault(candidate =>
                candidate.Definition.ValidationPlan.Id == validation.ValidationPlanId
                && candidate.Definition.ValidationPlan.Version == validation.ValidationPlanVersion);
            if (registration is null) return false;
            expected[(registration.Definition.Id, registration.Definition.Version)] = registration;
        }
        var expectedOverlayKeys = plan.Items.Where(static item => item.SubjectLifecycle is not null)
            .Select(static item => item.SubjectLifecycle!)
            .Select(static lifecycle => (lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId.Value))
            .Concat(plan.SubjectValidations.Select(validation =>
            {
                BaseGeneratedSubjectRegistration registration = expected.Values.Single(candidate =>
                    candidate.Definition.ValidationPlan.Id == validation.ValidationPlanId
                    && candidate.Definition.ValidationPlan.Version == validation.ValidationPlanVersion);
                return (registration.Definition.Id, registration.Definition.Version, validation.Reference.SubjectId.Value);
            })).ToHashSet();
        var actualOverlayKeys = prepared.SubjectOverlay
            .Select(static value => (value.ContractId, value.ContractVersion, value.SubjectId.Value)).ToArray();
        if (prepared.SubjectAuthorities.Length != expected.Count
            || prepared.SubjectValidations.Length != plan.SubjectValidations.Length
            || prepared.SubjectAuthorities.GroupBy(static value => (value.ContractId, value.ContractVersion)).Any(static group => group.Count() != 1)
            || actualOverlayKeys.Length != expectedOverlayKeys.Count
            || actualOverlayKeys.Distinct().Count() != actualOverlayKeys.Length
            || actualOverlayKeys.Any(key => !expectedOverlayKeys.Contains(key)))
            return false;
        foreach (BaseSubjectTransactionAuthorityEvidence authority in prepared.SubjectAuthorities)
        {
            if (!expected.TryGetValue((authority.ContractId, authority.ContractVersion), out BaseGeneratedSubjectRegistration? registration)
                || authority.ContractChecksum != registration.Checksum
                || authority.StoreInstanceId != prepared.Authority.StoreInstanceId
                || authority.RestoreEpoch != prepared.Authority.RestoreEpoch
                || authority.SchemaGeneration != prepared.Authority.SchemaGeneration
                || authority.StateGeneration < 1)
                return false;
        }
        foreach (IGrouping<(string ContractId, int ContractVersion, string SubjectId), BaseSubjectLifecyclePlanItem> group in plan.Items
            .Where(static item => item.SubjectLifecycle is not null).Select(static item => item.SubjectLifecycle!)
            .GroupBy(static value => (value.ContractId, value.ContractVersion, value.SubjectId.Value)))
        {
            BaseSubjectLifecyclePlanItem final = group.Last();
            BasePreparedSubjectOverlayEvidence? overlay = prepared.SubjectOverlay.SingleOrDefault(value =>
                value.ContractId == final.ContractId && value.ContractVersion == final.ContractVersion
                && value.SubjectId.Equals(final.SubjectId));
            if (overlay is null || (final.Kind == BaseSubjectLifecycleMutationKind.Retire
                    ? overlay.Exists || overlay.Incarnation is not null
                    : !overlay.Exists || overlay.Incarnation is null)
                || !HasSubjectInterval(prepared.ReadIntervals, $"subject:{final.ContractId}:contract", System.Text.Encoding.UTF8.GetBytes($"{final.ContractId}\n{final.ContractVersion}"))
                || !HasSubjectInterval(prepared.ReadIntervals, $"subject:{final.ContractId}:lifetime", System.Text.Encoding.UTF8.GetBytes($"{final.ContractId}\n{final.ContractVersion}\n{final.SubjectId.Value}"))
                || !HasSubjectInterval(prepared.ReadIntervals, $"subject:{final.ContractId}:record", System.Text.Encoding.UTF8.GetBytes(final.SubjectId.Value))) return false;
        }
        for (int index = 0; index < plan.SubjectValidations.Length; index++)
        {
            BaseSubjectReferenceValidationPlanItem validation = plan.SubjectValidations[index];
            BasePreparedSubjectValidationEvidence result = prepared.SubjectValidations[index];
            if (result.Ordinal != index || result.MutationOrdinal != validation.MutationOrdinal
                || result.SourceFieldId != validation.SourceFieldId || !Enum.IsDefined(result.State)) return false;
            BaseGeneratedSubjectRegistration registration = expected.Values.Single(candidate =>
                candidate.Definition.ValidationPlan.Id == validation.ValidationPlanId
                && candidate.Definition.ValidationPlan.Version == validation.ValidationPlanVersion);
            BaseSubjectTransactionAuthorityEvidence authority = prepared.SubjectAuthorities.Single(value =>
                value.ContractId == registration.Definition.Id && value.ContractVersion == registration.Definition.Version);
            BasePreparedSubjectOverlayEvidence? overlay = prepared.SubjectOverlay.SingleOrDefault(value =>
                value.ContractId == registration.Definition.Id && value.ContractVersion == registration.Definition.Version
                && value.SubjectId.Equals(validation.Reference.SubjectId));
            if (overlay is null) return false;
            bool valid = overlay.Exists && overlay.Incarnation is { } incarnation
                && incarnation.Equals(validation.Reference.Incarnation)
                && authority.AuthorityEpoch.Equals(validation.Reference.AuthorityEpoch)
                && (registration.Definition.Scope == BaseSubjectScopeKind.Global || overlay.Scope == validation.Scope.Value)
                && (validation.Requirement != BaseSubjectReferenceRequirement.Active
                    || overlay.Active == registration.Definition.ValidationPlan.Active.ActiveValue);
            if ((result.State == BaseSubjectValidationState.Valid) != valid) return false;
            byte[] contractKey = System.Text.Encoding.UTF8.GetBytes($"{registration.Definition.Id}\n{registration.Definition.Version}");
            byte[] subjectKey = System.Text.Encoding.UTF8.GetBytes($"{registration.Definition.Id}\n{registration.Definition.Version}\n{validation.Reference.SubjectId.Value}");
            byte[] recordKey = System.Text.Encoding.UTF8.GetBytes(validation.Reference.SubjectId.Value);
            if (!HasSubjectInterval(prepared.ReadIntervals, $"subject:{registration.Definition.Id}:contract", contractKey)
                || !HasSubjectInterval(prepared.ReadIntervals, $"subject:{registration.Definition.Id}:lifetime", subjectKey)
                || !HasSubjectInterval(prepared.ReadIntervals, $"subject:{registration.Definition.Id}:record", recordKey)) return false;
        }
        return true;
    }

    private static bool HasSubjectInterval(ImmutableArray<BaseAtomicReadIntervalEvidence> intervals, string path, byte[] key) =>
        intervals.Any(interval => interval.LogicalAccessPathId == path && interval.LowerInclusive && interval.UpperInclusive
            && interval.CanonicalLowerBound.AsSpan().SequenceEqual(key)
            && interval.CanonicalUpperBound.AsSpan().SequenceEqual(key));

    private static bool AppliedMatches(
        BaseAtomicMutationPlan plan,
        BasePreparedAtomicMutation prepared,
        BaseProvisionalAppliedAtomicMutation applied)
    {
        bool strict = HasSubjectWork(plan);
        if (!string.Equals(plan.PlanDigest, applied.PlanDigest, StringComparison.Ordinal) || applied.Facts.Length != plan.Items.Length)
            return false;
        for (int index = 0; index < plan.Items.Length; index++)
        {
            BaseRecordMutationFact fact;
            try { fact = applied.Facts[index].MaterializeOwned(); }
            catch { return false; }
            BaseAtomicMutationPlanItem item = plan.Items[index];
            if (!string.Equals(fact.Collection.Id, item.Collection.Id, StringComparison.Ordinal)
                || strict && (fact.ItemId != item.ItemId || fact.Event.EventId != item.EventId
                    || fact.RequestedOperation != item.RequestedKind)
                || fact.CommittedOperation != item.Kind
                || (fact.After ?? fact.Before)?.Id != item.RecordId
                || !ValidCommittedLifecycle(item.SubjectLifecycle, prepared.SubjectOverlay, fact.SubjectLifecycle)
                || strict && item.Current is not null && !RecordEquals(fact.Before, item.Current)
                || strict && item.ProposedPayload is not null && !PayloadEquals(fact.After?.Payload, item.ProposedPayload)
                || item.Kind == BaseCommittedRecordMutationKind.Delete && (fact.Before is null || fact.After is not null)
                || item.Kind != BaseCommittedRecordMutationKind.Delete && fact.After is null
                || strict && !(fact.ChangedFields ?? []).SequenceEqual(item.ChangedFields, StringComparer.Ordinal))
                return false;
        }
        return true;
    }

    private static bool ValidCommittedLifecycle(
        BaseSubjectLifecyclePlanItem? expected,
        ImmutableArray<BasePreparedSubjectOverlayEvidence> overlays,
        BaseSubjectLifecycleCommitEvidence? actual) => expected is null
        ? actual is null
        : actual is not null
            && string.Equals(actual.ContractId, expected.ContractId, StringComparison.Ordinal)
            && actual.ContractVersion == expected.ContractVersion
            && string.Equals(actual.SubjectId, expected.SubjectId.Value, StringComparison.Ordinal)
            && actual.Kind == expected.Kind
            && overlays.SingleOrDefault(value => value.ContractId == expected.ContractId
                && value.ContractVersion == expected.ContractVersion
                && value.SubjectId.Equals(expected.SubjectId)) is { } overlay
            && (expected.Kind == BaseSubjectLifecycleMutationKind.Retire
                ? actual.Incarnation is null && !overlay.Exists && overlay.Incarnation is null
                : actual.Incarnation is { Length: 22 } && IsCanonicalIncarnation(actual.Incarnation)
                    && overlay.Exists && overlay.Incarnation is { } incarnation
                    && string.Equals(actual.Incarnation, incarnation.ToBase64Url(), StringComparison.Ordinal));

    private static bool IsCanonicalIncarnation(string value)
    {
        try { return BaseSubjectIncarnation.Parse(value).ToBase64Url() == value; }
        catch { return false; }
    }

    private static bool RecordEquals(RecordEnvelope? left, RecordEnvelope right)
    {
        if (left is null) return false;
        try
        {
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(left, HPDBaseJsonSerializerContext.Default.RecordEnvelope)
                .AsSpan().SequenceEqual(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(right, HPDBaseJsonSerializerContext.Default.RecordEnvelope));
        }
        catch { return false; }
    }

    private static bool PayloadEquals(RecordPayload? left, RecordPayload right)
    {
        if (left is null) return false;
        try
        {
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(left, HPDBaseJsonSerializerContext.Default.RecordPayload)
                .AsSpan().SequenceEqual(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(right, HPDBaseJsonSerializerContext.Default.RecordPayload));
        }
        catch { return false; }
    }

    private static bool PreviousStateMatches(RecordEnvelope record, BasePreviousStateRequirement requirement)
    {
        if (requirement.Revision.Kind == BaseRevisionRequirementKind.Exact
            && requirement.Revision.ExactRevision != record.Metadata.Revision) return false;
        foreach (BasePreviousFieldRequirement field in requirement.Fields)
        {
            System.Text.Json.JsonElement value = default;
            bool present = record.Payload.Fields?.TryGetValue(field.FieldId, out value) == true;
            if (field.Kind == BasePreviousFieldRequirementKind.IsMissing && present) return false;
            if (field.Kind == BasePreviousFieldRequirementKind.IsDefined && !present) return false;
            if (field.Kind == BasePreviousFieldRequirementKind.IsNull && (!present || value.ValueKind != System.Text.Json.JsonValueKind.Null)) return false;
            if (field.Kind == BasePreviousFieldRequirementKind.Equal
                && (!present || field.Value is null || !QueryValueEquals(value, field.Value))) return false;
        }
        return true;
    }

    private static bool WithinAccounting(BaseSelectionMutationCommitAccounting value, BaseSelectionOperationLimits limits, int mutations) =>
        mutations <= limits.MaximumProducedMutations
        && value.WrittenBytes is >= 0 && value.WrittenBytes <= limits.MaximumWrittenBytes
        && value.FactBytes is >= 0 && value.FactBytes <= limits.MaximumFactBytes
        && value.JournalBytes is >= 0 && value.JournalBytes <= limits.MaximumJournalBytes
        && value.ReceiptBytes is >= 0 && value.ReceiptBytes <= limits.MaximumReceiptBytes
        && value.RelationChecks is >= 0 && value.RelationChecks <= limits.MaximumRelationChecks
        && value.UniqueConstraintChecks is >= 0 && value.UniqueConstraintChecks <= limits.MaximumUniqueConstraintChecks
        && value.ResultBytes is >= 0 && value.ResultBytes <= limits.MaximumResultBytes
        && value.TransientBytes is >= 0 && value.TransientBytes <= limits.MaximumTransientBytes;

    private bool ValidateSelection(BaseAtomicSelectionResult selected) =>
        ValidateSelectionEvidence(selected, profile, authority, collection, query);

    internal static bool ValidateSelectionEvidence(
        BaseAtomicSelectionResult selected,
        BaseSelectionOperationProfile profile,
        BaseAuthoritySnapshotRequirement authority,
        CollectionDefinition collection,
        RecordQuery query)
    {
        if (selected.MutationCapture is null
            || !string.Equals(selected.Authority.ApplicationId, profile.ApplicationId, StringComparison.Ordinal)
            || !string.Equals(selected.Authority.StoreInstanceId, authority.StoreInstanceId, StringComparison.Ordinal)
            || selected.Authority.RestoreEpoch != authority.RestoreEpoch
            || selected.Authority.SchemaGeneration != authority.SchemaGeneration
            || selected.Authority.CollectionGeneration != authority.CollectionGeneration
            || selected.Records.Length > profile.Limits.MaximumSelectedRecords
            || selected.Accounting.SelectedRecords != selected.Records.Length
            || selected.Accounting.SelectedBytes < 0
            || selected.Accounting.SelectedBytes > profile.Limits.MaximumSelectedBytes
            || selected.Accounting.ReadIntervals != selected.ReadIntervals.Length
            || selected.ReadIntervals.Length == 0
            || selected.ReadIntervals.Length > profile.Limits.MaximumReadIntervals)
            return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long canonicalBytes = 0;
        RecordEnvelope? previous = null;
        for (int index = 0; index < selected.Records.Length; index++)
        {
            BaseOwnedSelectedRecord record = selected.Records[index];
            if (record.SelectionOrdinal != index || record.CodecVersion != 1 || record.CanonicalBytes <= 0)
                return false;
            try
            {
                RecordEnvelope materialized = record.MaterializeOwned();
                if (!string.Equals(materialized.Id.Value, record.RecordId, StringComparison.Ordinal)
                    || !string.Equals(materialized.CollectionId, collection.Id, StringComparison.Ordinal)
                    || materialized.Metadata.Revision != record.Revision
                    || !ids.Add(materialized.Id.Value)
                    || !BaseRecordFilterMatcher.Matches(materialized, query.Filter)
                    || previous is not null && CompareSelected(previous, materialized, query.Sort!) >= 0)
                    return false;
                canonicalBytes = checked(canonicalBytes + record.CopyCanonicalBytes().LongLength);
                previous = materialized;
            }
            catch { return false; }
        }
        byte[] boundary = selected.Records.Length == 0 ? [] : BaseSelectionOrderTuple.Encode(selected.Records[^1].MaterializeOwned(), query.Sort!);
        if (canonicalBytes != selected.Accounting.SelectedBytes
            || !selected.CanonicalOrderBoundary.AsSpan().SequenceEqual(boundary)
            || selected.Accounting.EvidenceBytes != selected.ReadIntervals.Sum(static interval =>
                checked((long)interval.CanonicalLowerBound.Length + interval.CanonicalUpperBound.Length))) return false;
        string path = $"collection:{collection.Id}";
        ReadOnlySpan<byte> priorUpper = default;
        bool boundaryCovered = false;
        for (int index = 0; index < selected.ReadIntervals.Length; index++)
        {
            BaseAtomicReadIntervalEvidence interval = selected.ReadIntervals[index];
            if (!string.Equals(interval.LogicalAccessPathId, path, StringComparison.Ordinal)
                || interval.CanonicalLowerBound.IsDefault || interval.CanonicalUpperBound.IsDefault
                || CompareBytes(interval.CanonicalLowerBound.AsSpan(), interval.CanonicalUpperBound.AsSpan()) > 0
                || index > 0 && CompareBytes(priorUpper, interval.CanonicalLowerBound.AsSpan()) >= 0) return false;
            int lowerToBoundary = CompareBytes(interval.CanonicalLowerBound.AsSpan(), boundary);
            int boundaryToUpper = CompareBytes(boundary, interval.CanonicalUpperBound.AsSpan());
            if ((lowerToBoundary < 0 || lowerToBoundary == 0 && interval.LowerInclusive)
                && (boundaryToUpper < 0 || boundaryToUpper == 0 && interval.UpperInclusive))
                boundaryCovered = true;
            priorUpper = interval.CanonicalUpperBound.AsSpan();
        }
        return boundaryCovered;
    }

    private static int CompareSelected(RecordEnvelope left, RecordEnvelope right, QuerySort[] sort)
    {
        foreach (QuerySort item in sort)
        {
            if (string.Equals(item.Field, "id", StringComparison.Ordinal))
            {
                int id = string.Compare(left.Id.Value, right.Id.Value, StringComparison.Ordinal);
                if (id != 0) return item.Direction == QuerySortDirection.Desc ? -id : id;
                continue;
            }
            System.Text.Json.JsonElement leftValue = default, rightValue = default;
            bool leftPresent = left.Payload.Fields?.TryGetValue(item.Field, out leftValue) == true;
            bool rightPresent = right.Payload.Fields?.TryGetValue(item.Field, out rightValue) == true;
            int comparison = CompareSortValue(leftPresent, leftValue, rightPresent, rightValue, item.Nulls);
            if (comparison != 0) return item.Direction == QuerySortDirection.Desc ? -comparison : comparison;
        }
        return 0;
    }

    private static int CompareSortValue(bool leftPresent, System.Text.Json.JsonElement left, bool rightPresent, System.Text.Json.JsonElement right, QueryNullOrder nulls)
    {
        bool leftNull = !leftPresent || left.ValueKind == System.Text.Json.JsonValueKind.Null;
        bool rightNull = !rightPresent || right.ValueKind == System.Text.Json.JsonValueKind.Null;
        if (leftNull || rightNull)
        {
            if (leftNull == rightNull) return 0;
            bool first = nulls != QueryNullOrder.Last;
            return leftNull == first ? -1 : 1;
        }
        if (left.ValueKind == System.Text.Json.JsonValueKind.Number && right.ValueKind == System.Text.Json.JsonValueKind.Number
            && left.TryGetDecimal(out decimal a) && right.TryGetDecimal(out decimal b)) return a.CompareTo(b);
        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    private static int CompareBytes(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left.SequenceCompareTo(right);

    private static bool QueryValueEquals(System.Text.Json.JsonElement value, QueryValue expected) => expected.Kind switch
    {
        QueryValueKind.Null => value.ValueKind == System.Text.Json.JsonValueKind.Null,
        QueryValueKind.String => value.ValueKind == System.Text.Json.JsonValueKind.String && string.Equals(value.GetString(), expected.String, StringComparison.Ordinal),
        QueryValueKind.Id => value.ValueKind == System.Text.Json.JsonValueKind.String && string.Equals(value.GetString(), expected.Id, StringComparison.Ordinal),
        QueryValueKind.Boolean => value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False && value.GetBoolean() == expected.Boolean,
        QueryValueKind.Integer => value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt64(out long integer) && integer == expected.Integer,
        QueryValueKind.Number => value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetDouble(out double number) && number.Equals(expected.Number),
        QueryValueKind.Decimal => value.ValueKind == System.Text.Json.JsonValueKind.Number && string.Equals(value.GetRawText(), expected.Decimal, StringComparison.Ordinal),
        QueryValueKind.DateTime => value.ValueKind == System.Text.Json.JsonValueKind.String && value.TryGetDateTimeOffset(out DateTimeOffset date) && date.Equals(expected.DateTime),
        QueryValueKind.Array => value.ValueKind == System.Text.Json.JsonValueKind.Array && expected.Array is { } items
            && value.GetArrayLength() == items.Length
            && value.EnumerateArray().Zip(items).All(static pair => QueryValueEquals(pair.First, pair.Second)),
        _ => false,
    };

    private static BaseError MapProviderFailure(OperationResult<BaseAtomicSelectionResult> failure) => (failure.Status, failure.Error?.Code, failure.Error?.Category) switch
    {
        (OperationStatus.ValidationFailed, "base.provider.selection.limitExceeded", ErrorCategory.Validation) => Error(BaseSelectionErrorCodes.LimitExceeded, ErrorCategory.Validation),
        (OperationStatus.Conflict, "base.provider.selection.authorityChanged", ErrorCategory.Conflict) => Error(BaseSelectionErrorCodes.SchemaGenerationChanged, ErrorCategory.Conflict),
        (OperationStatus.Unsupported, "base.provider.selection.queryUnsupported", ErrorCategory.Unsupported) => Error(BaseSelectionErrorCodes.CapabilityMissing, ErrorCategory.Unsupported),
        (OperationStatus.Conflict, "base.provider.selection.transactionConflict", ErrorCategory.Conflict) => Error(BaseSelectionErrorCodes.TransactionConflict, ErrorCategory.Conflict),
        (OperationStatus.StoreError, "base.provider.selection.timeout", ErrorCategory.Store) => Error(BaseSelectionErrorCodes.Timeout, ErrorCategory.Store),
        (OperationStatus.StoreError, "base.provider.selection.cancelled", ErrorCategory.Store) => Error(BaseSelectionErrorCodes.Cancelled, ErrorCategory.Store),
        (OperationStatus.ValidationFailed, "base.provider.selection.authorityInvalid", ErrorCategory.Validation) => Error(BaseSelectionErrorCodes.ContractInvalid, ErrorCategory.Validation),
        _ => Error("base.runtime.store.error", ErrorCategory.Store),
    };
    private static bool HasSubjectWork(BaseAtomicMutationPlan plan) =>
        plan.SubjectValidations.Length != 0 || plan.Items.Any(static item => item.SubjectLifecycle is not null);
    private static AtomicMutationProcessingResult Failed(string code, ErrorCategory category) =>
        new(AtomicMutationProcessingOutcome.Failed, [], Error(code, category));
    private static AtomicMutationProcessingResult Failed(BaseError error) =>
        new(AtomicMutationProcessingOutcome.Failed, [], error);
    private static BaseError Error(string code, ErrorCategory category) =>
        new() { Code = code, Message = "The selection mutation failed.", Category = category };
}
