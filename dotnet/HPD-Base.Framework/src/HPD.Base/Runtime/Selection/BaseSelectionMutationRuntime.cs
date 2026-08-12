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
    TimeProvider timeProvider) : IBaseSelectionMutationRuntime
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
            resolved.Value, authority.Value);
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

    private static BaseFailure<BaseSelectionMutationResult> Failure(OperationStatus status, string code, ErrorCategory category) =>
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
                FieldId = new string(field.Name.AsSpan()),
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
    BaseResolvedMutationStore store,
    BaseAuthoritySnapshotRequirement authority) : IAtomicMutationProcessor
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
        var facts = new List<BaseRecordMutationFact>(selected.Value.Records.Length);
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
            RecordMutationSessionContext context = new()
            {
                RequestedOperation = profile.MutationKind == BaseSelectionMutationKind.MergePatch
                    ? BaseRecordMutationKind.Patch : BaseRecordMutationKind.Delete,
                EventId = Guid.NewGuid().ToString("N"),
                Operation = operation with { RecordId = record.Id.Value },
            };
            OperationResult<RecordMutationSessionResult> mutation = profile.MutationKind == BaseSelectionMutationKind.MergePatch
                ? await session.PatchAsync(collection, record.Id, patch! with { ExpectedRevision = null }, context, cancellationToken).ConfigureAwait(false)
                : await session.DeleteAsync(collection, record.Id, new RecordDeleteRequest { ReturnPrevious = true }, context, cancellationToken).ConfigureAwait(false);
            if (!mutation.IsSuccess() || mutation.Value is null)
                return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.Failed, [], mutation.Error
                    ?? Error(BaseSelectionErrorCodes.TransactionConflict, ErrorCategory.Conflict));
            facts.Add(mutation.Value.Mutation);
            _attempts.Add(new BaseMutationAttempt
            {
                Command = new BaseMutationCommand
                {
                    Index = _attempts.Count,
                    ItemId = $"selection:{_attempts.Count}",
                    CollectionId = collection.Id,
                    Kind = context.RequestedOperation,
                    Collection = collection,
                    Context = context.Operation,
                    EventId = context.EventId,
                    Store = store,
                    RecordId = record.Id,
                    Patch = patch,
                    Delete = profile.MutationKind == BaseSelectionMutationKind.Delete ? new RecordDeleteRequest { ReturnPrevious = false } : null,
                },
                Status = mutation.Value.Mutation.CommittedOperation == BaseCommittedRecordMutationKind.Delete ? OperationStatus.Deleted : OperationStatus.Updated,
                Mutation = mutation.Value.Mutation,
                Policy = authorized.Value,
                Revision = mutation.Value.Record?.Metadata.Revision is { } revision ? new RevisionInfo { Revision = revision.Value, Guarantee = RevisionGuarantee.Store } : null,
            });
        }
        Result = new BaseSelectionMutationResult
        {
            SelectedCount = selected.Value.Records.Length,
            MutatedCount = facts.Count,
            Outcome = BaseRecordBatchOutcome.Committed,
        };
        OperationResult projections = await session.ApplyMutationProjectionsAsync(
            BaseAtomicMutationProjectionFactory.Create([.. facts]), cancellationToken).ConfigureAwait(false);
        if (!projections.IsSuccess())
            return Failed(projections.Error ?? Error("base.runtime.mutationProjectionFailed", ErrorCategory.Store));
        var receipt = new BaseAtomicReceiptResult
            {
                Kind = BaseAtomicReceiptResultKind.SelectionMutation,
                Mutations = facts.Select(static fact => BaseOwnedMutationFact.Freeze(fact, 1)).ToImmutableArray(),
                SelectionMutation = new BaseSelectionMutationReceiptResult
                {
                    ApplicationId = profile.ApplicationId,
                    CollectionId = collection.Id,
                    OperationProfileId = profile.Id,
                    OperationProfileVersion = profile.Version,
                    ReceiptScope = principal.CurrentTenantId ?? string.Empty,
                    SelectedCount = selected.Value.Records.Length,
                    MutatedCount = facts.Count,
                    Outcome = BaseRecordBatchOutcome.Committed,
                },
            };
        OperationResult<BaseSelectionMutationCommitAccounting> measured = await session.MeasureSelectionMutationAsync(receipt, Result, cancellationToken).ConfigureAwait(false);
        if (!measured.IsSuccess() || measured.Value is null || !WithinAccounting(measured.Value, profile.Limits, facts.Count))
            return Failed(BaseSelectionErrorCodes.LimitExceeded, ErrorCategory.Validation);
        return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, receipt);
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
        if (!string.Equals(selected.Authority.ApplicationId, profile.ApplicationId, StringComparison.Ordinal)
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
    private static AtomicMutationProcessingResult Failed(string code, ErrorCategory category) =>
        new(AtomicMutationProcessingOutcome.Failed, [], Error(code, category));
    private static AtomicMutationProcessingResult Failed(BaseError error) =>
        new(AtomicMutationProcessingOutcome.Failed, [], error);
    private static BaseError Error(string code, ErrorCategory category) =>
        new() { Code = code, Message = "The selection mutation failed.", Category = category };
}
