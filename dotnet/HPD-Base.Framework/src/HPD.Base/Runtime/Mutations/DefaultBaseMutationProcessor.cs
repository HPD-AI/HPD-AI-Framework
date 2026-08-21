using System.Diagnostics;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed class DefaultBaseMutationProcessor(
    BaseMutationCommand[] commands,
    PrincipalContext principal,
    IBasePolicyOrchestrator policy,
    IBaseResultNormalizer normalizer,
    CollectionDefinition[] collections,
    BaseAtomicMutationExecutionLimits executionLimits,
    BaseAtomicMutationAuthorityRequirement authority,
    BaseSubjectContractRegistry subjects) : IAtomicMutationProcessor
{
    private readonly List<BaseMutationAttempt> _attempts = [];
    private readonly List<BaseFinalizedRelationPolicy> _relationPolicies = [];
    private readonly IReadOnlyDictionary<string, CollectionDefinition> _collections = collections.ToDictionary(static value => value.Id, StringComparer.Ordinal);
    private readonly Dictionary<int, BasePolicyEvaluation> _finalizedPolicies = [];
    private long _deadline;
    private IReadOnlyDictionary<int, BaseCapturedMutationItem> _captured = new Dictionary<int, BaseCapturedMutationItem>();

    /// <summary>Gets the attempts.</summary>
    public IReadOnlyList<BaseMutationAttempt> Attempts => _attempts;

    public async ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseRecordMutationFact[] committedMutations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(committedMutations);
        if (_attempts.Count != 0 || committedMutations.Length != commands.Length)
            return Failed(Error(BaseMutationRequestErrorCodes.ReceiptUnavailable, "The stored receipt is unavailable.", ErrorCategory.Authorization));

        var projectedMutations = new BaseRecordMutationFact[committedMutations.Length];
        for (var index = 0; index < committedMutations.Length; index++)
        {
            BaseMutationCommand command = commands[index];
            BaseRecordMutationFact mutation = committedMutations[index];
            if (!string.Equals(mutation.Collection.Id, command.Collection.Id, StringComparison.Ordinal)
                || mutation.RequestedOperation != command.Kind)
                return Failed(Error(BaseMutationRequestErrorCodes.ReceiptUnavailable, "The stored receipt is unavailable.", ErrorCategory.Authorization));
            if (!TryProjectReceiptMutation(mutation, command.Collection, out mutation))
                return Failed(Error(BaseMutationRequestErrorCodes.ReceiptUnavailable, "The stored receipt is unavailable.", ErrorCategory.Authorization));
            projectedMutations[index] = mutation;

            RecordEnvelope? resource = mutation.After ?? mutation.Before;
            OperationResult<BasePolicyEvaluation> policyResult = await policy.EvaluateReadAsync(new BasePolicyRequest
            {
                Principal = principal,
                Operation = command.Context,
                Collection = command.Collection,
                ResourceKind = PolicyResourceKind.Record,
                RecordId = resource?.Id,
                ExistingRecord = resource,
            }, cancellationToken).ConfigureAwait(false);
            if (!policyResult.IsSuccess())
                return Failed(policyResult.Error ?? Error("base.runtime.policy.denied", "Policy denied the operation.", ErrorCategory.Authorization));
            if (policyResult.Value?.Decision.Effect != PolicyEffect.Allow
                || resource is not null && !BaseRecordFilterMatcher.Matches(resource, policyResult.Value.EffectiveRecordFilter))
                return Failed(Error("base.runtime.policy.denied", "Policy denied the operation.", ErrorCategory.Authorization));

            _attempts.Add(new BaseMutationAttempt
            {
                Command = command,
                Mutation = mutation with { ItemId = command.ItemId },
                Policy = policyResult.Value,
                Status = mutation.CommittedOperation switch
                {
                    BaseCommittedRecordMutationKind.Create => OperationStatus.Created,
                    BaseCommittedRecordMutationKind.Delete => OperationStatus.Deleted,
                    _ => OperationStatus.Updated,
                },
                Revision = mutation.After?.Metadata.Revision is { } revision
                    ? new RevisionInfo
                    {
                        Revision = revision.Value,
                        ETag = mutation.After.Metadata.ETag,
                        LastModified = mutation.After.Metadata.UpdatedAt,
                        Guarantee = RevisionGuarantee.Store,
                    }
                    : null,
            });
        }

        return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, projectedMutations);
    }

    internal static bool TryProjectReceiptMutation(
        BaseRecordMutationFact stored,
        CollectionDefinition currentCollection,
        out BaseRecordMutationFact projected)
    {
        Dictionary<string, FieldDefinition> storedByName = (stored.Collection.Fields ?? [])
            .ToDictionary(static field => field.WireName, StringComparer.Ordinal);
        Dictionary<string, FieldDefinition> currentById = (currentCollection.Fields ?? [])
            .ToDictionary(static field => field.Id, StringComparer.Ordinal);

        bool ProjectEnvelope(RecordEnvelope? source, out RecordEnvelope? target)
        {
            target = source;
            if (source is null) return true;
            Dictionary<string, JsonElement>? sourceFields = source.Payload.Kind switch
            {
                RecordPayloadKind.FieldMap => source.Payload.Fields,
                RecordPayloadKind.Json when source.Payload.Json.ValueKind == JsonValueKind.Object =>
                    source.Payload.Json.EnumerateObject().ToDictionary(
                        static property => property.Name,
                        static property => property.Value.Clone(),
                        StringComparer.Ordinal),
                _ => null,
            };
            if (sourceFields is null) return false;
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach ((string name, JsonElement value) in sourceFields)
            {
                if (storedByName.TryGetValue(name, out FieldDefinition? oldField))
                {
                    if (!currentById.TryGetValue(oldField.Id, out FieldDefinition? currentField)
                        || !string.Equals(oldField.Type, currentField.Type, StringComparison.Ordinal)
                        || !string.Equals(oldField.Format, currentField.Format, StringComparison.Ordinal)
                        || !fields.TryAdd(currentField.WireName, value.Clone()))
                        return false;
                }
                else
                {
                    if (stored.Collection.UnknownFields != UnknownFieldPolicy.Preserve
                        || currentCollection.UnknownFields != UnknownFieldPolicy.Preserve
                        || !fields.TryAdd(name, value.Clone()))
                        return false;
                }
            }

            target = source with
            {
                Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields },
            };
            return true;
        }

        if (!ProjectEnvelope(stored.Before, out RecordEnvelope? before)
            || !ProjectEnvelope(stored.After, out RecordEnvelope? after)
            || !ProjectEnvelope(stored.Delete?.Previous, out RecordEnvelope? deletedPrevious))
        {
            projected = stored;
            return false;
        }

        string[]? changedFields = stored.ChangedFields;
        if (changedFields is not null)
        {
            var mapped = new string[changedFields.Length];
            for (var index = 0; index < changedFields.Length; index++)
            {
                string name = changedFields[index];
                if (storedByName.TryGetValue(name, out FieldDefinition? oldField))
                {
                    if (!currentById.TryGetValue(oldField.Id, out FieldDefinition? currentField))
                    {
                        projected = stored;
                        return false;
                    }
                    mapped[index] = currentField.WireName;
                }
                else if (stored.Collection.UnknownFields == UnknownFieldPolicy.Preserve
                    && currentCollection.UnknownFields == UnknownFieldPolicy.Preserve)
                {
                    mapped[index] = name;
                }
                else
                {
                    projected = stored;
                    return false;
                }
            }
            changedFields = mapped;
        }

        projected = stored with
        {
            Collection = currentCollection,
            Before = before,
            After = after,
            Delete = stored.Delete is null ? null : stored.Delete with { Previous = deletedPrevious },
            ChangedFields = changedFields,
        };
        return true;
    }

    /// <summary>Executes the process async operation.</summary>
    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
        IAtomicRecordSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_attempts.Count != 0)
            return Failed(Error("base.runtime.batch.invalid", "A mutation processor can only be invoked once.", ErrorCategory.Unexpected));
        _deadline = Stopwatch.GetTimestamp() + (long)(executionLimits.Deadlines.TransactionTimeout.TotalSeconds * Stopwatch.Frequency);

        BaseAtomicMutationIntent intent = CreateIntent(commands, authority, _collections);
        var captureRequest = new BaseAtomicExecutionRequest
        {
            Kind = BaseAtomicMutationExecutionKind.RecordMutations,
            Intent = intent,
            Limits = executionLimits,
        };
        OperationResult<BaseCapturedAtomicExecution> capture = await session.CaptureAtomicExecutionAsync(captureRequest, cancellationToken).ConfigureAwait(false);
        if (!capture.IsSuccess() || capture.Value is null)
            return HasPotentialSubjectWork()
                ? FailedProvider(capture.Status, capture.Error)
                : Failed(capture.Error ?? Error("base.runtime.store.error", "The provider authority capture failed.", ErrorCategory.Store));
        BaseCapturedAtomicExecution capturedEvidence;
        try { capturedEvidence = FreezeCapturedAuthority(capture.Value); }
        catch
        {
            return Failed(Error(BaseSubjectErrorCodes.ProviderContractInvalid, "The provider authority capture was invalid.", ErrorCategory.Store));
        }
        bool capturedValid = ValidateCaptured(intent, capturedEvidence, executionLimits);
        if (!capturedValid)
            return Failed(Error(BaseSubjectErrorCodes.ProviderContractInvalid, "The provider authority capture was invalid.", ErrorCategory.Store));
        OperationResult<BaseFinalizedRecordMutationPlan> finalizedPlan = await FinalizeCapturedCommandsAsync(
            capturedEvidence.Items.ToDictionary(static item => item.Ordinal), cancellationToken).ConfigureAwait(false);
        if (!finalizedPlan.IsSuccess() || finalizedPlan.Value is null)
            return Failed(finalizedPlan.Error ?? Error("base.runtime.batch.itemInvalid", "A batch item failed.", ErrorCategory.Unexpected));
        if (!BaseAtomicPolicyAuthority.IsAdmissible(finalizedPlan.Value.PolicyEvaluations))
            return Failed(Error(BasePolicyAuthorityErrorCodes.Invalid, "The mutation policy authority is invalid.", ErrorCategory.Authorization));
        ImmutableArray<BaseAtomicMutationPlanItem> finalizedItems = finalizedPlan.Value.Items;
        BaseAtomicPolicyAuthorityDigest policyDigest = BaseAtomicPolicyAuthority.Compute(
            authority.ApplicationId, "base.l30.recordMutations", finalizedPlan.Value.PolicyEvaluations);
        var plan = new BaseFinalizedAtomicExecutionPlan
        {
            Kind = BaseAtomicMutationExecutionKind.RecordMutations,
            IntentDigest = intent.IntentDigest,
            CaptureDigest = capturedEvidence.CaptureDigest,
            PolicyAuthorityDigest = policyDigest,
            Authority = authority with { },
            Items = finalizedItems,
            SubjectValidations = finalizedPlan.Value.SubjectValidations,
            Limits = executionLimits with { },
            PlanDigest = BaseAtomicPolicyAuthority.BindPlanDigest(
                ComputePlanDigest(intent.IntentDigest, capturedEvidence.CaptureDigest, finalizedItems, finalizedPlan.Value.SubjectValidations), policyDigest),
        };
        BaseFinalizedAtomicExecutionPlan retainedPlan = BaseAtomicMutationOwnership.FreezePlan(plan);
        BaseFinalizedAtomicExecutionPlan providerPlan = BaseAtomicMutationOwnership.FreezePlan(retainedPlan);
        OperationResult<BasePreparedAtomicExecution> prepared = await session.PrepareAtomicExecutionAsync(
            capture.Value,
            providerPlan,
            cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess() || prepared.Value is null)
            return HasSubjectWork(retainedPlan)
                ? FailedProvider(prepared.Status, prepared.Error)
                : Failed(prepared.Error ?? Error("base.runtime.store.error", "The mutation preparation failed.", ErrorCategory.Store));
        if (!ValidatePrepared(retainedPlan, capturedEvidence, prepared.Value))
            return Failed(Error(BaseSubjectErrorCodes.ProviderContractInvalid, "The mutation preparation was invalid.", ErrorCategory.Store));
        if (prepared.Value.SubjectValidations.Any(static validation => validation.State == BaseSubjectValidationState.Invalid))
            return Failed(Error(BaseSubjectErrorCodes.ReferenceInvalid, "The subject reference is invalid.", ErrorCategory.Validation));

        OperationResult<BaseProvisionalAtomicExecution> applied = await session.ApplyPreparedAtomicExecutionAsync(
            prepared.Value,
            cancellationToken).ConfigureAwait(false);
        if (!applied.IsSuccess() || applied.Value is null || !ValidateApplied(retainedPlan, prepared.Value, applied.Value))
            return !applied.IsSuccess() || applied.Value is null
                ? HasSubjectWork(retainedPlan)
                    ? FailedProvider(applied.Status, applied.Error)
                    : Failed(applied.Error ?? Error("base.runtime.store.error", "The mutation application failed.", ErrorCategory.Store))
                : Failed(Error(BaseSubjectErrorCodes.ProviderContractInvalid, "The applied mutation evidence was invalid.", ErrorCategory.Store));

        BaseRecordMutationFact[] mutations;
        try { mutations = applied.Value.Facts.Select(static fact => fact.MaterializeOwned()).ToArray(); }
        catch { return Failed(Error(BaseSubjectErrorCodes.ProviderContractInvalid, "The applied mutation evidence was invalid.", ErrorCategory.Store)); }
        for (int index = 0; index < mutations.Length; index++)
        {
            BaseRecordMutationFact mutation = mutations[index];
            BaseMutationCommand command = commands[index];
            _attempts[index] = new BaseMutationAttempt
            {
                Command = command,
                Mutation = mutation,
                Policy = _finalizedPolicies[index],
                Status = mutation.CommittedOperation switch
                {
                    BaseCommittedRecordMutationKind.Create => OperationStatus.Created,
                    BaseCommittedRecordMutationKind.Delete => OperationStatus.Deleted,
                    _ => OperationStatus.Updated,
                },
                Revision = mutation.After?.Metadata.Revision is { } revision
                    ? new RevisionInfo { Revision = revision.Value, ETag = mutation.After.Metadata.ETag, LastModified = mutation.After.Metadata.UpdatedAt, Guarantee = RevisionGuarantee.Store }
                    : null,
            };
        }

        return new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.ReadyToCommit,
            mutations);
    }

    private static BaseCapturedAtomicExecution FreezeCapturedAuthority(
        BaseCapturedAtomicExecution value) => new()
    {
        Kind = value.Kind,
        IntentDigest = new string(value.IntentDigest.AsSpan()),
        CaptureDigest = new string(value.CaptureDigest.AsSpan()),
        Authority = value.Authority with
        {
            ApplicationId = new string(value.Authority.ApplicationId.AsSpan()),
            StoreInstanceId = new string(value.Authority.StoreInstanceId.AsSpan()),
            Collections = value.Authority.Collections.Select(static collection => collection with
            {
                CollectionId = new string(collection.CollectionId.AsSpan()),
            }).ToImmutableArray(),
            TransactionEvidenceToken = value.Authority.TransactionEvidenceToken.ToArray().ToImmutableArray(),
        },
        Items = value.Items.Select(static item => item with
        {
            CollectionId = new string(item.CollectionId.AsSpan()),
            Current = item.Current is null ? null : RecordCloneHelpers.CloneEnvelope(item.Current),
            RelationTargets = item.RelationTargets.Select(static relation => relation with
            {
                SourceFieldId = new string(relation.SourceFieldId.AsSpan()),
                TargetCollectionId = new string(relation.TargetCollectionId.AsSpan()),
                Current = relation.Current is null ? null : RecordCloneHelpers.CloneEnvelope(relation.Current),
            }).ToImmutableArray(),
        }).ToImmutableArray(),
        ModuleRecords = value.ModuleRecords.Select(static record => record with
        {
            CaptureId = new string(record.CaptureId.AsSpan()),
            CollectionId = new string(record.CollectionId.AsSpan()),
            Current = record.Current is null ? null : RecordCloneHelpers.CloneEnvelope(record.Current),
        }).ToImmutableArray(),
        ModuleRelationTargets = value.ModuleRelationTargets.Select(static target => target with
        {
            SourceStatementId = new string(target.SourceStatementId.AsSpan()),
            SourceFieldId = new string(target.SourceFieldId.AsSpan()),
            TargetCollectionId = new string(target.TargetCollectionId.AsSpan()),
            Current = target.Current is null ? null : RecordCloneHelpers.CloneEnvelope(target.Current),
        }).ToImmutableArray(),
        Generations = value.Generations.Select(static generation => generation with
        {
            CaptureId = new string(generation.CaptureId.AsSpan()),
            CellId = new string(generation.CellId.AsSpan()),
            CanonicalKeyDigest = new string(generation.CanonicalKeyDigest.AsSpan()),
        }).ToImmutableArray(),
        ReadIntervals = value.ReadIntervals.Select(static interval => interval with
        {
            LogicalAccessPathId = new string(interval.LogicalAccessPathId.AsSpan()),
            CanonicalLowerBound = interval.CanonicalLowerBound.ToArray().ToImmutableArray(),
            CanonicalUpperBound = interval.CanonicalUpperBound.ToArray().ToImmutableArray(),
        }).ToImmutableArray(),
        Accounting = value.Accounting with { },
    };

    internal async ValueTask<OperationResult<BaseFinalizedRecordMutationPlan>> FinalizeCapturedCommandsAsync(
        IReadOnlyDictionary<int, BaseCapturedMutationItem> capturedItems,
        CancellationToken cancellationToken)
    {
        _deadline = Stopwatch.GetTimestamp() + (long)(executionLimits.Deadlines.TransactionTimeout.TotalSeconds * Stopwatch.Frequency);
        _captured = capturedItems;
        var planItems = ImmutableArray.CreateBuilder<BaseAtomicMutationPlanItem>(commands.Length);
        var finalRecords = new Dictionary<(string Collection, string Record), RecordEnvelope?>();
        for (int ordinal = 0; ordinal < commands.Length; ordinal++)
        {
            BaseMutationCommand command = commands[ordinal];
            cancellationToken.ThrowIfCancellationRequested();
            if (!_captured.TryGetValue(ordinal, out BaseCapturedMutationItem? capturedItem))
                return OperationResults.ValidationFailed<BaseFinalizedRecordMutationPlan>(
                    Error(BaseSubjectErrorCodes.ProviderContractInvalid, "The provider authority capture was invalid.", ErrorCategory.Store));
            var key = (command.CollectionId, TargetId(command).Value);
            RecordEnvelope? current = finalRecords.TryGetValue(key, out RecordEnvelope? overlayCurrent)
                ? overlayCurrent
                : capturedItem.Current;
            OperationResult<BaseAtomicMutationPlanItem> finalized = await FinalizeCommandAsync(
                command, ordinal, current, planItems.ToImmutable(), cancellationToken).ConfigureAwait(false);
            if (!finalized.IsSuccess() || finalized.Value is null)
            {
                _attempts.Add(Failure(command, finalized.Status,
                    finalized.Error ?? Error("base.runtime.batch.itemInvalid", "A batch item failed.", ErrorCategory.Unexpected)));
                return new OperationResult<BaseFinalizedRecordMutationPlan> { Status = finalized.Status, Error = finalized.Error };
            }
            planItems.Add(finalized.Value);
            _attempts.Add(new BaseMutationAttempt
            {
                Command = command,
                Status = finalized.Value.Kind switch
                {
                    BaseCommittedRecordMutationKind.Create => OperationStatus.Created,
                    BaseCommittedRecordMutationKind.Delete => OperationStatus.Deleted,
                    _ => OperationStatus.Updated,
                },
                Policy = _finalizedPolicies[ordinal],
            });
            finalRecords[key] = finalized.Value.Kind == BaseCommittedRecordMutationKind.Delete
                ? null
                : new RecordEnvelope
                {
                    CollectionId = command.CollectionId,
                    Id = finalized.Value.RecordId,
                    Payload = RecordCloneHelpers.ClonePayload(finalized.Value.ProposedPayload!),
                    Metadata = current?.Metadata ?? new RecordMetadata(),
                };
        }
        ImmutableArray<BaseAtomicMutationPlanItem> items = planItems.MoveToImmutable();
        OperationResult<(ImmutableArray<BaseAtomicMutationPlanItem> Items, ImmutableArray<BaseSubjectReferenceValidationPlanItem> Validations)> subjectsResult = BuildSubjectPlan(items);
        if (!subjectsResult.IsSuccess() || subjectsResult.Value == default)
            return new OperationResult<BaseFinalizedRecordMutationPlan> { Status = subjectsResult.Status, Error = subjectsResult.Error };
        if (!ValidateSubjectContractLimits(subjectsResult.Value.Items, subjectsResult.Value.Validations))
            return OperationResults.ValidationFailed<BaseFinalizedRecordMutationPlan>(
                Error(BaseSubjectErrorCodes.BudgetExceeded, "The subject validation budget was exceeded.", ErrorCategory.Validation));
        return OperationResults.Ok(new BaseFinalizedRecordMutationPlan(
            subjectsResult.Value.Items,
            subjectsResult.Value.Validations,
            [.. Enumerable.Range(0, commands.Length).Select(index => _finalizedPolicies[index])],
            [.. _relationPolicies]));
    }

    private async ValueTask<OperationResult<BaseAtomicMutationPlanItem>> FinalizeCommandAsync(
        BaseMutationCommand command,
        int ordinal,
        RecordEnvelope? current,
        ImmutableArray<BaseAtomicMutationPlanItem> earlierItems,
        CancellationToken cancellationToken)
    {
        BaseCommittedRecordMutationKind committed;
        RecordPayload? proposed;
        ImmutableArray<string> changed;
        PolicyResourceKind resourceKind;
        RecordPayload? changedPayload;

        switch (command.Kind)
        {
            case BaseRecordMutationKind.Create:
                if (current is not null)
                    return OperationResults.Conflict<BaseAtomicMutationPlanItem>(Error("base.runtime.record.conflict", "The record already exists.", ErrorCategory.Conflict));
                committed = BaseCommittedRecordMutationKind.Create;
                proposed = command.CreatePayload!.Payload;
                changed = command.CreatePayload.ChangedFields.ToImmutableArray();
                resourceKind = PolicyResourceKind.CreatePayload;
                changedPayload = proposed;
                break;
            case BaseRecordMutationKind.Patch:
                if (current is null)
                    return OperationResults.NotFound<BaseAtomicMutationPlanItem>(Error("base.runtime.record.notFound", "The record was not found.", ErrorCategory.NotFound));
                if (!RevisionMatches(current, command.Patch!.ExpectedRevision))
                    return OperationResults.Conflict<BaseAtomicMutationPlanItem>(Error("base.runtime.revision.conflict", "The revision precondition was not satisfied.", ErrorCategory.Conflict));
                committed = BaseCommittedRecordMutationKind.Patch;
                changedPayload = command.UpdatePayload!.Payload;
                proposed = BasePolicyRuntimeSimulation.MergePatchPayload(current.Payload, changedPayload);
                changed = command.UpdatePayload.ChangedFields.ToImmutableArray();
                resourceKind = PolicyResourceKind.UpdatePayload;
                break;
            case BaseRecordMutationKind.Replace:
                if (current is null)
                    return OperationResults.NotFound<BaseAtomicMutationPlanItem>(Error("base.runtime.record.notFound", "The record was not found.", ErrorCategory.NotFound));
                if (!RevisionMatches(current, command.Replace!.ExpectedRevision))
                    return OperationResults.Conflict<BaseAtomicMutationPlanItem>(Error("base.runtime.revision.conflict", "The revision precondition was not satisfied.", ErrorCategory.Conflict));
                committed = BaseCommittedRecordMutationKind.Replace;
                proposed = command.UpdatePayload!.Payload;
                changedPayload = proposed;
                changed = command.UpdatePayload.ChangedFields.ToImmutableArray();
                resourceKind = PolicyResourceKind.UpdatePayload;
                break;
            case BaseRecordMutationKind.Delete:
                if (current is null)
                    return OperationResults.NotFound<BaseAtomicMutationPlanItem>(Error("base.runtime.record.notFound", "The record was not found.", ErrorCategory.NotFound));
                if (!RevisionMatches(current, command.Delete!.ExpectedRevision))
                    return OperationResults.Conflict<BaseAtomicMutationPlanItem>(Error("base.runtime.revision.conflict", "The revision precondition was not satisfied.", ErrorCategory.Conflict));
                committed = BaseCommittedRecordMutationKind.Delete;
                proposed = null;
                changedPayload = null;
                changed = [];
                resourceKind = PolicyResourceKind.DeleteCandidate;
                break;
            case BaseRecordMutationKind.Upsert:
                RecordUpsertRequest upsert = command.Upsert!;
                if (current is null)
                {
                    committed = BaseCommittedRecordMutationKind.Create;
                    proposed = command.CreatePayload!.Payload;
                    changedPayload = proposed;
                    changed = command.CreatePayload.ChangedFields.ToImmutableArray();
                    resourceKind = PolicyResourceKind.CreatePayload;
                }
                else
                {
                    committed = upsert.UpdateMode == RecordUpsertUpdateMode.Patch
                        ? BaseCommittedRecordMutationKind.Patch : BaseCommittedRecordMutationKind.Replace;
                    changedPayload = command.UpdatePayload!.Payload;
                    proposed = upsert.UpdateMode == RecordUpsertUpdateMode.Patch
                        ? BasePolicyRuntimeSimulation.MergePatchPayload(current.Payload, changedPayload)
                        : changedPayload;
                    changed = command.UpdatePayload.ChangedFields.ToImmutableArray();
                    resourceKind = PolicyResourceKind.UpdatePayload;
                }
                break;
            default:
                return OperationResults.ValidationFailed<BaseAtomicMutationPlanItem>(Error("base.runtime.batch.itemInvalid", "The mutation kind is invalid.", ErrorCategory.Validation));
        }

        RecordEnvelope? proposedRecord = current is null || proposed is null ? null : current with { Payload = proposed };
        OperationResult<BasePolicyEvaluation> evaluated = await EvaluateAsync(
            command, resourceKind, proposed, current, proposedRecord, cancellationToken).ConfigureAwait(false);
        if (!evaluated.IsSuccess() || evaluated.Value is null)
        {
            if (command.Kind == BaseRecordMutationKind.Upsert
                && (evaluated.Status is OperationStatus.PolicyDenied or OperationStatus.Unauthorized
                    || evaluated.Error?.Category is ErrorCategory.Authorization or ErrorCategory.Authentication))
            {
                return OperationResults.PolicyDenied<BaseAtomicMutationPlanItem>(Error(
                    "base.runtime.policy.denied",
                    "Policy denied the operation.",
                    ErrorCategory.Authorization));
            }
            return new OperationResult<BaseAtomicMutationPlanItem> { Status = evaluated.Status, Error = evaluated.Error };
        }
        RecordPayload predicate = proposed ?? current!.Payload;
        if (EnforceWritePolicy<RecordEnvelope>(predicate, changedPayload, evaluated.Value) is { } denied)
            return new OperationResult<BaseAtomicMutationPlanItem> { Status = denied.Status, Error = denied.Error };
        if (proposed is not null && await EnforceCapturedRelationsAsync(
                command, proposed, _captured[ordinal].RelationTargets, earlierItems, cancellationToken).ConfigureAwait(false) is { } relationError)
            return new OperationResult<BaseAtomicMutationPlanItem>
            {
                Status = RelationStatus(relationError),
                Error = relationError,
            };
        if (command.Kind == BaseRecordMutationKind.Upsert)
        {
            if (current is null && command.Upsert!.ExpectedRevision is not null)
                return OperationResults.Conflict<BaseAtomicMutationPlanItem>(Error("base.runtime.revision.conflict", "The revision precondition was not satisfied.", ErrorCategory.Conflict));
            if (current is null && command.Upsert!.Condition == RecordUpsertExistenceCondition.UpdateOnly)
                return OperationResults.NotFound<BaseAtomicMutationPlanItem>(Error("base.runtime.upsert.preconditionFailed", "The upsert precondition was not satisfied.", ErrorCategory.NotFound));
            if (current is not null && command.Upsert!.Condition == RecordUpsertExistenceCondition.CreateOnly)
                return OperationResults.Conflict<BaseAtomicMutationPlanItem>(Error("base.runtime.upsert.preconditionFailed", "The upsert precondition was not satisfied.", ErrorCategory.Conflict));
            if (current is not null && !RevisionMatches(current, command.Upsert!.ExpectedRevision))
                return OperationResults.Conflict<BaseAtomicMutationPlanItem>(Error("base.runtime.revision.conflict", "The revision precondition was not satisfied.", ErrorCategory.Conflict));
        }
        _finalizedPolicies[ordinal] = evaluated.Value;
        return OperationResults.Ok(new BaseAtomicMutationPlanItem
        {
            Ordinal = ordinal,
            ItemId = command.ItemId,
            EventId = command.EventId,
            Collection = command.Collection with { Fields = command.Collection.Fields?.Select(static field => field with { }).ToArray() },
            Kind = committed,
            RequestedKind = command.Kind,
            RecordId = command.RecordId ?? command.Upsert?.Id ?? command.Create?.RequestedId ?? throw new InvalidOperationException(),
            RuntimeAssignedRecordId = command.RuntimeAssignedRecordId,
            ProposedPayload = proposed is null ? null : RecordCloneHelpers.ClonePayload(proposed),
            Delete = committed == BaseCommittedRecordMutationKind.Delete ? command.Delete! with { } : null,
            Current = current is null ? null : RecordCloneHelpers.CloneEnvelope(current),
            ChangedFields = changed,
            SubjectLifecycle = null,
            Operation = command.Context with { },
        });
    }

    private static bool RevisionMatches(RecordEnvelope current, RevisionToken? expected) =>
        expected is null || current.Metadata.Revision == expected.Value;

    private async ValueTask<BaseError?> EnforceCapturedRelationsAsync(
        BaseMutationCommand command,
        RecordPayload payload,
        ImmutableArray<BaseCapturedRelationTarget> capturedTargets,
        ImmutableArray<BaseAtomicMutationPlanItem> earlierItems,
        CancellationToken cancellationToken)
    {
        foreach (FieldDefinition field in command.Collection.Fields ?? [])
        {
            if (field.Relation is not { OwningSide: BaseRelationOwningSide.Source } relation) continue;
            if (relation.DeleteBehavior is not BaseRelationDeleteBehavior.Restrict || relation.ExistenceEnforcement is not EnforcementOwner.Runtime)
                return RelationError("base.relation.enforcementUnsupported", "The declared relation enforcement mode is unavailable.", ErrorCategory.Unsupported);
            if (!_collections.TryGetValue(relation.TargetCollectionId, out CollectionDefinition? targetCollection))
                return RelationError("base.relation.invalid", "The declared relation is invalid.", ErrorCategory.Validation);
            if (!TryRelationIds(payload, field.WireName, relation, out RecordId[] ids, out string? code))
                return RelationError(code!, "The relation value has an invalid shape or cardinality.", ErrorCategory.Validation);
            foreach (RecordId id in ids)
            {
                BaseAtomicMutationPlanItem? earlier = earlierItems.LastOrDefault(value =>
                    string.Equals(value.Collection.Id, targetCollection.Id, StringComparison.Ordinal) && value.RecordId == id);
                RecordEnvelope? target = earlier is null
                    ? capturedTargets.SingleOrDefault(value =>
                    string.Equals(value.SourceFieldId, field.Id, StringComparison.Ordinal) &&
                    string.Equals(value.TargetCollectionId, targetCollection.Id, StringComparison.Ordinal) && value.TargetRecordId == id)?.Current
                    : earlier.Kind == BaseCommittedRecordMutationKind.Delete ? null : new RecordEnvelope
                    {
                        CollectionId = targetCollection.Id,
                        Id = id,
                        Payload = RecordCloneHelpers.ClonePayload(earlier.ProposedPayload!),
                        Metadata = earlier.Current?.Metadata ?? new RecordMetadata(),
                    };
                if (target is null || !string.Equals(target.CollectionId, targetCollection.Id, StringComparison.Ordinal) || target.Id != id)
                    return RelationError("base.relation.targetUnavailable", "A relation target is unavailable.", ErrorCategory.Authorization);
                OperationResult<BasePolicyEvaluation> targetPolicy;
                try
                {
                    Task<OperationResult<BasePolicyEvaluation>> task = policy.EvaluateReadAsync(new BasePolicyRequest
                    {
                        Principal = principal, Operation = command.Context, Collection = targetCollection,
                        ResourceKind = PolicyResourceKind.RelationTarget, RecordId = id, ExistingRecord = target,
                    }, cancellationToken).AsTask();
                    TimeSpan remaining = TimeSpan.FromSeconds(Math.Max(0, _deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);
                    if (remaining <= TimeSpan.Zero) return RelationError("base.relation.policyTimeout", "Relation policy evaluation exceeded its bounded lifetime.", ErrorCategory.Store);
                    TimeSpan publicationMargin = TimeSpan.FromMilliseconds(Math.Min(10, Math.Max(1, remaining.TotalMilliseconds / 10)));
                    remaining -= publicationMargin;
                    if (remaining <= TimeSpan.Zero) return RelationError("base.relation.policyTimeout", "Relation policy evaluation exceeded its bounded lifetime.", ErrorCategory.Store);
                    targetPolicy = await task.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException) { return RelationError("base.relation.policyTimeout", "Relation policy evaluation exceeded its bounded lifetime.", ErrorCategory.Store); }
                catch (OperationCanceledException) { throw; }
                catch { return RelationError("base.relation.targetUnavailable", "A relation target is unavailable.", ErrorCategory.Authorization); }
                if (!targetPolicy.IsSuccess() || targetPolicy.Value?.Decision.Effect != PolicyEffect.Allow ||
                    !BaseRecordFilterMatcher.Matches(target, targetPolicy.Value.EffectiveRecordFilter))
                    return RelationError("base.relation.targetUnavailable", "A relation target is unavailable.", ErrorCategory.Authorization);
                if (targetPolicy.Value.Authority is null)
                    return RelationError("base.relation.targetUnavailable", "A relation target is unavailable.", ErrorCategory.Authorization);
                _relationPolicies.Add(new BaseFinalizedRelationPolicy(
                    command.ItemId, field.Id, targetCollection.Id, id, targetPolicy.Value));
            }
        }
        return null;
    }

    private OperationResult<(ImmutableArray<BaseAtomicMutationPlanItem>, ImmutableArray<BaseSubjectReferenceValidationPlanItem>)> BuildSubjectPlan(
        ImmutableArray<BaseAtomicMutationPlanItem> items)
    {
        var finalized = ImmutableArray.CreateBuilder<BaseAtomicMutationPlanItem>(items.Length);
        var validations = ImmutableArray.CreateBuilder<BaseSubjectReferenceValidationPlanItem>();
        foreach (BaseAtomicMutationPlanItem item in items)
        {
            BaseGeneratedSubjectRegistration[] lifecycleContracts = subjects.All
                .Where(subject => string.Equals(subject.Definition.ValidationPlan.PrivateCollectionId, item.Collection.Id, StringComparison.Ordinal))
                .ToArray();
            if (lifecycleContracts.Length > 1)
                return SubjectPlanFailure(BaseSubjectErrorCodes.ContractInvalid);
            BaseSubjectLifecyclePlanItem? lifecycle = null;
            if (lifecycleContracts.Length == 1)
            {
                BaseGeneratedSubjectRegistration subject = lifecycleContracts[0];
                BaseSubjectId subjectId;
                try { subjectId = BaseSubjectId.Create(item.RecordId.Value, subject.Definition.SubjectIdKind, subject.Definition.MaximumSubjectIdUtf8Bytes); }
                catch { return SubjectPlanFailure(BaseSubjectErrorCodes.ContractInvalid); }
                lifecycle = new BaseSubjectLifecyclePlanItem
                {
                    ContractId = subject.Definition.Id,
                    ContractVersion = subject.Definition.Version,
                    ContractChecksum = subject.Checksum,
                    SubjectId = subjectId,
                    Kind = item.Kind switch
                    {
                        BaseCommittedRecordMutationKind.Create => BaseSubjectLifecycleMutationKind.Create,
                        BaseCommittedRecordMutationKind.Delete => BaseSubjectLifecycleMutationKind.Retire,
                        _ => BaseSubjectLifecycleMutationKind.Preserve,
                    },
                };
                if (item.Kind != BaseCommittedRecordMutationKind.Delete &&
                    !HasValidSubjectLogicalState(item, subject.Definition))
                    return SubjectPlanFailure(BaseSubjectErrorCodes.ContractInvalid);
            }

            if (item.Kind != BaseCommittedRecordMutationKind.Delete && item.ProposedPayload?.Fields is { } fields)
            {
                IEnumerable<FieldDefinition> referenceFields = (item.Collection.Fields ?? []).Where(static field => field.SubjectReference is not null);
                if (item.Kind == BaseCommittedRecordMutationKind.Patch)
                    referenceFields = referenceFields.Where(field => item.ChangedFields.Contains(field.WireName, StringComparer.Ordinal));
                foreach (FieldDefinition field in referenceFields)
                {
                    if (!fields.TryGetValue(field.WireName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
                        continue;
                    BaseSubjectReferenceDefinition reference = field.SubjectReference!;
                    BaseGeneratedSubjectRegistration? target = subjects.Find(reference.ContractId, reference.ContractVersion);
                    // The exact subject-contract grant is evaluated by the coordinator
                    // before provider resolution. Transaction-bound policy here owns
                    // only the captured source record and must not impersonate that
                    // separate L38 authority decision.
                    if (target is null)
                        return SubjectPlanFailure(BaseSubjectErrorCodes.ReferenceInvalid, OperationStatus.PolicyDenied, ErrorCategory.Authorization);
                    if (!TryParseReference(value, target.Definition, out BaseOwnedSubjectReference? owned))
                        return SubjectPlanFailure(BaseSubjectErrorCodes.ReferenceInvalid);
                    string? scope = target.Definition.Scope switch
                    {
                        BaseSubjectScopeKind.Global => null,
                        BaseSubjectScopeKind.Tenant => principal.CurrentTenantId,
                        BaseSubjectScopeKind.Project => item.Operation.ProjectId,
                        _ => null,
                    };
                    if (target.Definition.Scope != BaseSubjectScopeKind.Global && string.IsNullOrWhiteSpace(scope))
                        return SubjectPlanFailure(BaseSubjectErrorCodes.ReferenceInvalid);
                    validations.Add(new BaseSubjectReferenceValidationPlanItem
                    {
                        MutationOrdinal = item.Ordinal,
                        SourceFieldId = field.Id,
                        ValidationPlanId = target.Definition.ValidationPlan.Id,
                        ValidationPlanVersion = target.Definition.ValidationPlan.Version,
                        Requirement = reference.Requirement,
                        Reference = owned!,
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
            FieldDefinition? active = definitions.SingleOrDefault(field =>
                string.Equals(field.Id, plan.Active.FieldId, StringComparison.Ordinal));
            if (active is null || !fields.TryGetValue(active.WireName, out JsonElement value) ||
                value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
        }
        if (plan.Scope.Kind != BaseSubjectScopeBindingKind.Global)
        {
            FieldDefinition? scope = definitions.SingleOrDefault(field =>
                string.Equals(field.Id, plan.Scope.FieldId, StringComparison.Ordinal));
            if (scope is null || !fields.TryGetValue(scope.WireName, out JsonElement value) ||
                value.ValueKind != JsonValueKind.String || value.GetString() is not { } text)
                return false;
            try { _ = BaseSubjectId.Create(text, BaseSubjectIdKind.OrdinalString, 256); }
            catch { return false; }
        }
        return true;
    }

    private static OperationResult<(ImmutableArray<BaseAtomicMutationPlanItem>, ImmutableArray<BaseSubjectReferenceValidationPlanItem>)> SubjectPlanFailure(
        string code,
        OperationStatus status = OperationStatus.ValidationFailed,
        ErrorCategory category = ErrorCategory.Validation) => new()
    {
        Status = status,
        Error = Error(code, code == BaseSubjectErrorCodes.ReferenceInvalid ? "The subject reference is invalid." : "The subject contract is invalid.", category),
    };

    private static bool TryParseReference(
        JsonElement value,
        BaseExportedSubjectDefinition definition,
        out BaseOwnedSubjectReference? reference)
    {
        reference = null;
        if (value.ValueKind != JsonValueKind.Object) return false;
        string? subjectId = null, epoch = null, incarnation = null;
        int count = 0;
        foreach (JsonProperty property in value.EnumerateObject())
        {
            count++;
            if (property.Value.ValueKind != JsonValueKind.String) return false;
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

    internal static string ComputePlanDigest(
        string intentDigest,
        string captureDigest,
        ImmutableArray<BaseAtomicMutationPlanItem> items,
        ImmutableArray<BaseSubjectReferenceValidationPlanItem> validations)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"hpd.base.atomic-mutation-plan.v1\0{intentDigest}\0{captureDigest}\0"));
        foreach (BaseAtomicMutationPlanItem item in items)
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"{item.Ordinal}\0{item.Collection.Id}\0{item.RecordId.Value}\0{(int)item.Kind}\0{(int)item.RequestedKind}\0{item.EventId}\0"));
            if (item.ProposedPayload is not null)
                hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(item.ProposedPayload, HPDBaseJsonSerializerContext.Default.RecordPayload));
            if (item.SubjectLifecycle is { } lifecycle)
                hash.AppendData(Encoding.UTF8.GetBytes($"\0subject-lifecycle\0{lifecycle.ContractId}\0{lifecycle.ContractVersion}\0{lifecycle.ContractChecksum}\0{(int)lifecycle.Kind}\0{lifecycle.SubjectId.Value}\0"));
        }
        foreach (BaseSubjectReferenceValidationPlanItem validation in validations)
            hash.AppendData(Encoding.UTF8.GetBytes($"\0subject-validation\0{validation.MutationOrdinal}\0{validation.SourceFieldId}\0{validation.ValidationPlanId}\0{validation.ValidationPlanVersion}\0{(int)validation.Requirement}\0{validation.Reference.SubjectId.Value}\0{validation.Reference.AuthorityEpoch.ToBase64Url()}\0{validation.Reference.Incarnation.ToBase64Url()}\0{(int)validation.Scope.Kind}\0{validation.Scope.Value}\0"));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static bool ValidateCaptured(
        BaseAtomicMutationIntent intent,
        BaseCapturedAtomicExecution captured,
        BaseAtomicMutationExecutionLimits limits)
    {
        if (!string.Equals(captured.IntentDigest, intent.IntentDigest, StringComparison.Ordinal)
            || captured.CaptureDigest is not { Length: 64 }
            || captured.Items.Length != intent.Items.Length
            || captured.Authority.ApplicationId != intent.Authority.ApplicationId
            || captured.Authority.StoreInstanceId != intent.Authority.StoreInstanceId
            || captured.Authority.RestoreEpoch != intent.Authority.RestoreEpoch
            || captured.Authority.SchemaGeneration != intent.Authority.SchemaGeneration
            || !captured.Authority.Collections.SequenceEqual(intent.Authority.Collections)
            || !Enum.IsDefined(captured.Authority.Isolation)
            || captured.Authority.TransactionEvidenceToken.IsDefaultOrEmpty)
            return false;

        long selectedBytes = 0;
        var expectedIntervals = new List<(string Path, byte[] Key)>();
        var transactionRecords = new Dictionary<string, RecordEnvelope?>(StringComparer.Ordinal);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(Encoding.UTF8.GetBytes(intent.IntentDigest));
        for (int index = 0; index < intent.Items.Length; index++)
        {
            BaseAtomicMutationIntentItem expected = intent.Items[index];
            BaseCapturedMutationItem actual = captured.Items[index];
            string itemKey = CaptureRecordKey(expected.Collection.Id, expected.RecordId);
            bool hasPriorState = transactionRecords.TryGetValue(itemKey, out RecordEnvelope? priorState);
            BaseCapturedMutationDisposition disposition = expected.RequestedKind switch
            {
                BaseRecordMutationKind.Create => actual.Current is null
                    ? BaseCapturedMutationDisposition.Create
                    : BaseCapturedMutationDisposition.Update,
                BaseRecordMutationKind.Upsert => actual.Current is null ? BaseCapturedMutationDisposition.Create : BaseCapturedMutationDisposition.Update,
                BaseRecordMutationKind.Delete => BaseCapturedMutationDisposition.Delete,
                _ => BaseCapturedMutationDisposition.Update,
            };
            if (actual.Ordinal != index || actual.CollectionId != expected.Collection.Id || actual.RecordId != expected.RecordId
                || actual.RuntimeAssignedRecordId != expected.RuntimeAssignedRecordId || actual.Disposition != disposition
                || disposition == BaseCapturedMutationDisposition.Create && actual.Current is not null
                || actual.Current is not null && (actual.Current.CollectionId != expected.Collection.Id || actual.Current.Id != expected.RecordId)
                || hasPriorState && !CapturedRecordsEqual(actual.Current, priorState)
                || actual.RelationTargets.Length != expected.RelationTargets.Length)
                return false;
            if (!AppendRecord(actual.Current, digest, ref selectedBytes)) return false;
            byte[] recordKey = Encoding.UTF8.GetBytes(expected.RecordId.Value);
            digest.AppendData(recordKey);
            for (int relationIndex = 0; relationIndex < expected.RelationTargets.Length; relationIndex++)
            {
                BaseAtomicRelationTargetIntent expectedRelation = expected.RelationTargets[relationIndex];
                BaseCapturedRelationTarget actualRelation = actual.RelationTargets[relationIndex];
                string relationKey = CaptureRecordKey(expectedRelation.TargetCollection.Id, expectedRelation.TargetRecordId);
                bool hasPriorTarget = transactionRecords.TryGetValue(relationKey, out RecordEnvelope? priorTarget);
                if (actualRelation.SourceFieldId != expectedRelation.SourceFieldId
                    || actualRelation.TargetCollectionId != expectedRelation.TargetCollection.Id
                    || actualRelation.TargetRecordId != expectedRelation.TargetRecordId
                    || actualRelation.Current is not null && (actualRelation.Current.CollectionId != expectedRelation.TargetCollection.Id
                        || actualRelation.Current.Id != expectedRelation.TargetRecordId)
                    || hasPriorTarget && !CapturedRecordsEqual(actualRelation.Current, priorTarget)
                    || !AppendRecord(actualRelation.Current, digest, ref selectedBytes))
                    return false;
                expectedIntervals.Add(($"collection:{expectedRelation.TargetCollection.Id}:record", Encoding.UTF8.GetBytes(expectedRelation.TargetRecordId.Value)));
            }
            expectedIntervals.Add(($"collection:{expected.Collection.Id}:record", recordKey));
            transactionRecords[itemKey] = SimulateCapturedIntent(expected, actual.Current);
        }
        if (!string.Equals(captured.CaptureDigest, Convert.ToHexStringLower(digest.GetHashAndReset()), StringComparison.Ordinal)
            || captured.ReadIntervals.Length != expectedIntervals.Count
            || captured.Accounting.Records != intent.Items.Length + intent.Items.Sum(static item => item.RelationTargets.Length)
            || captured.Accounting.ReadIntervals != captured.ReadIntervals.Length
            || captured.Accounting.SelectedBytes != selectedBytes)
            return false;
        for (int index = 0; index < expectedIntervals.Count; index++)
        {
            BaseAtomicReadIntervalEvidence interval = captured.ReadIntervals[index];
            (string path, byte[] key) = expectedIntervals[index];
            if (interval.LogicalAccessPathId != path || !interval.LowerInclusive || !interval.UpperInclusive
                || !interval.CanonicalLowerBound.AsSpan().SequenceEqual(key)
                || !interval.CanonicalUpperBound.AsSpan().SequenceEqual(key))
                return false;
        }
        long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(captured.ReadIntervals);
        return captured.Accounting.EvidenceBytes == evidenceBytes
            && captured.Accounting.TransientBytes >= selectedBytes + evidenceBytes
            && captured.Accounting.SelectedBytes <= limits.MaximumSelectedBytes
            && captured.Accounting.EvidenceBytes <= limits.MaximumEvidenceBytes
            && captured.Accounting.TransientBytes <= limits.MaximumTransientBytes
            && captured.Accounting.ReadIntervals <= limits.MaximumReadIntervals;

        static bool AppendRecord(RecordEnvelope? record, IncrementalHash digest, ref long selectedBytes)
        {
            if (record is null) return true;
            try
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                selectedBytes = checked(selectedBytes + bytes.LongLength);
                digest.AppendData(bytes);
                return true;
            }
            catch { return false; }
        }
    }

    private static string CaptureRecordKey(string collectionId, RecordId recordId) =>
        collectionId + "\n" + recordId.Value;

    private static bool CapturedRecordsEqual(RecordEnvelope? left, RecordEnvelope? right)
    {
        if (left is null || right is null) return left is null && right is null;
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(left, HPDBaseJsonSerializerContext.Default.RecordEnvelope)
                .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, HPDBaseJsonSerializerContext.Default.RecordEnvelope));
        }
        catch { return false; }
    }

    private static RecordEnvelope? SimulateCapturedIntent(
        BaseAtomicMutationIntentItem item,
        RecordEnvelope? current)
    {
        if (item.RequestedKind == BaseRecordMutationKind.Delete) return null;
        RecordPayload? payload = item.RequestedKind switch
        {
            BaseRecordMutationKind.Create => item.Create?.Payload,
            BaseRecordMutationKind.Replace => item.Replace?.Payload,
            BaseRecordMutationKind.Patch when current is not null && item.Patch is not null =>
                BasePolicyRuntimeSimulation.MergePatchPayload(current.Payload, item.Patch.Patch),
            BaseRecordMutationKind.Upsert when current is null => item.Upsert?.CreatePayload,
            BaseRecordMutationKind.Upsert when item.Upsert?.UpdateMode == RecordUpsertUpdateMode.Replace => item.Upsert.UpdatePayload,
            BaseRecordMutationKind.Upsert when current is not null && item.Upsert is not null =>
                BasePolicyRuntimeSimulation.MergePatchPayload(current.Payload, item.Upsert.UpdatePayload),
            _ => null,
        };
        if (payload is null) return current;
        return new RecordEnvelope
        {
            CollectionId = item.Collection.Id,
            Id = item.RecordId,
            Payload = payload,
            Metadata = current?.Metadata ?? new RecordMetadata(),
        };
    }

    private bool ValidatePrepared(
        BaseFinalizedAtomicExecutionPlan plan,
        BaseCapturedAtomicExecution captured,
        BasePreparedAtomicExecution prepared) =>
        string.Equals(prepared.PlanDigest, plan.PlanDigest, StringComparison.Ordinal)
        && prepared.Dispositions.Length == plan.Items.Length
        && (!HasSubjectWork(plan) || prepared.Dispositions.Select((disposition, index) => Enum.IsDefined(disposition) && disposition == (plan.Items[index].Kind switch
        {
            BaseCommittedRecordMutationKind.Create => BaseCapturedMutationDisposition.Create,
            BaseCommittedRecordMutationKind.Delete => BaseCapturedMutationDisposition.Delete,
            _ => BaseCapturedMutationDisposition.Update,
        })).All(static valid => valid))
        && prepared.SubjectValidations.Length == plan.SubjectValidations.Length
        && prepared.ReadIntervals.Length >= captured.ReadIntervals.Length
        && (!HasSubjectWork(plan) || captured.ReadIntervals.All(expected => prepared.ReadIntervals.Any(actual => IntervalEquals(expected, actual))))
        && prepared.Accounting.AuthorityReads >= 0
        && prepared.Accounting.ReadIntervals == prepared.ReadIntervals.Length
        && prepared.Accounting.SelectedBytes >= captured.Accounting.SelectedBytes
        && prepared.Accounting.EvidenceBytes == BaseSubjectCanonicalRetainedWork.MeasureIntervals(prepared.ReadIntervals)
        && prepared.Accounting.TransientBytes >= prepared.Accounting.SelectedBytes + prepared.Accounting.EvidenceBytes
        && prepared.Accounting.AuthorityReads <= plan.Limits.MaximumAuthorityReads
        && prepared.Accounting.ReadIntervals <= plan.Limits.MaximumReadIntervals
        && prepared.Accounting.SelectedBytes <= plan.Limits.MaximumSelectedBytes
        && prepared.Accounting.EvidenceBytes <= plan.Limits.MaximumEvidenceBytes
        && prepared.Accounting.TransientBytes <= plan.Limits.MaximumTransientBytes
        && ValidateSubjectAuthorityEvidence(plan, prepared)
        && prepared.SubjectValidations.Select((validation, index) =>
            validation.Ordinal == index
            && validation.MutationOrdinal == plan.SubjectValidations[index].MutationOrdinal
            && string.Equals(validation.SourceFieldId, plan.SubjectValidations[index].SourceFieldId, StringComparison.Ordinal)
            && Enum.IsDefined(validation.State)).All(static valid => valid)
        && string.Equals(prepared.Authority.StoreInstanceId, captured.Authority.StoreInstanceId, StringComparison.Ordinal)
        && prepared.Authority.RestoreEpoch == captured.Authority.RestoreEpoch
        && prepared.Authority.SchemaGeneration == captured.Authority.SchemaGeneration
        && prepared.Authority.Collections.SequenceEqual(captured.Authority.Collections)
        && (!HasSubjectWork(plan) || prepared.Authority.Isolation == captured.Authority.Isolation
            && prepared.Authority.TransactionEvidenceToken.AsSpan().SequenceEqual(captured.Authority.TransactionEvidenceToken.AsSpan()));

    private static bool IntervalEquals(BaseAtomicReadIntervalEvidence left, BaseAtomicReadIntervalEvidence right) =>
        left.LogicalAccessPathId == right.LogicalAccessPathId
        && left.LowerInclusive == right.LowerInclusive && left.UpperInclusive == right.UpperInclusive
        && left.CanonicalLowerBound.AsSpan().SequenceEqual(right.CanonicalLowerBound.AsSpan())
        && left.CanonicalUpperBound.AsSpan().SequenceEqual(right.CanonicalUpperBound.AsSpan());

    private bool ValidateSubjectAuthorityEvidence(BaseFinalizedAtomicExecutionPlan plan, BasePreparedAtomicExecution prepared)
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
                string.Equals(candidate.Definition.ValidationPlan.Id, validation.ValidationPlanId, StringComparison.Ordinal)
                && candidate.Definition.ValidationPlan.Version == validation.ValidationPlanVersion);
            if (registration is null) return false;
            expected[(registration.Definition.Id, registration.Definition.Version)] = registration;
        }
        if (prepared.SubjectAuthorities.Length != expected.Count) return false;
        var seen = new HashSet<(string Id, int Version)>();
        foreach (BaseSubjectTransactionAuthorityEvidence evidence in prepared.SubjectAuthorities)
        {
            var key = (evidence.ContractId, evidence.ContractVersion);
            if (!seen.Add(key) || !expected.TryGetValue(key, out BaseGeneratedSubjectRegistration? registration)
                || !string.Equals(evidence.ContractChecksum, registration.Checksum, StringComparison.Ordinal)
                || !string.Equals(evidence.StoreInstanceId, prepared.Authority.StoreInstanceId, StringComparison.Ordinal)
                || evidence.RestoreEpoch != prepared.Authority.RestoreEpoch
                || evidence.SchemaGeneration != prepared.Authority.SchemaGeneration
                || evidence.StateGeneration < 1)
                return false;
        }
        var expectedOverlayKeys = plan.Items
            .Where(static item => item.SubjectLifecycle is not null)
            .Select(static item => item.SubjectLifecycle!)
            .Select(static lifecycle => (lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId.Value))
            .Concat(plan.SubjectValidations.Select(validation =>
            {
                BaseGeneratedSubjectRegistration registration = expected.Values.Single(candidate =>
                    candidate.Definition.ValidationPlan.Id == validation.ValidationPlanId
                    && candidate.Definition.ValidationPlan.Version == validation.ValidationPlanVersion);
                return (registration.Definition.Id, registration.Definition.Version, validation.Reference.SubjectId.Value);
            }))
            .ToHashSet();
        var actualOverlayKeys = prepared.SubjectOverlay
            .Select(static value => (value.ContractId, value.ContractVersion, value.SubjectId.Value))
            .ToArray();
        if (actualOverlayKeys.Length != expectedOverlayKeys.Count
            || actualOverlayKeys.Distinct().Count() != actualOverlayKeys.Length
            || actualOverlayKeys.Any(key => !expectedOverlayKeys.Contains(key)))
            return false;
        foreach (IGrouping<(string ContractId, int ContractVersion, string SubjectId), BaseSubjectLifecyclePlanItem> group in plan.Items
            .Where(static item => item.SubjectLifecycle is not null)
            .Select(static item => item.SubjectLifecycle!)
            .GroupBy(static value => (value.ContractId, value.ContractVersion, value.SubjectId.Value)))
        {
            BaseSubjectLifecyclePlanItem final = group.Last();
            BasePreparedSubjectOverlayEvidence? overlay = prepared.SubjectOverlay.SingleOrDefault(value =>
                value.ContractId == final.ContractId && value.ContractVersion == final.ContractVersion
                && value.SubjectId.Equals(final.SubjectId));
            if (overlay is null || (final.Kind == BaseSubjectLifecycleMutationKind.Retire
                    ? overlay.Exists || overlay.Incarnation is not null
                    : !overlay.Exists || overlay.Incarnation is null)
                || !HasExactInterval(prepared.ReadIntervals, $"subject:{final.ContractId}:contract", Encoding.UTF8.GetBytes($"{final.ContractId}\n{final.ContractVersion}"))
                || !HasExactInterval(prepared.ReadIntervals, $"subject:{final.ContractId}:lifetime", Encoding.UTF8.GetBytes($"{final.ContractId}\n{final.ContractVersion}\n{final.SubjectId.Value}"))
                || !HasExactInterval(prepared.ReadIntervals, $"subject:{final.ContractId}:record", Encoding.UTF8.GetBytes(final.SubjectId.Value)))
                return false;
        }
        for (int index = 0; index < plan.SubjectValidations.Length; index++)
        {
            BaseSubjectReferenceValidationPlanItem validation = plan.SubjectValidations[index];
            BaseGeneratedSubjectRegistration registration = expected.Values.Single(candidate =>
                candidate.Definition.ValidationPlan.Id == validation.ValidationPlanId
                && candidate.Definition.ValidationPlan.Version == validation.ValidationPlanVersion);
            BaseSubjectTransactionAuthorityEvidence authority = prepared.SubjectAuthorities.Single(value =>
                value.ContractId == registration.Definition.Id && value.ContractVersion == registration.Definition.Version);
            BasePreparedSubjectOverlayEvidence? overlay = prepared.SubjectOverlay.SingleOrDefault(value =>
                value.ContractId == registration.Definition.Id && value.ContractVersion == registration.Definition.Version
                && value.SubjectId.Equals(validation.Reference.SubjectId));
            if (overlay is null) return false;
            bool structurallyValid = overlay.Exists && overlay.Incarnation is { } incarnation
                && incarnation.Equals(validation.Reference.Incarnation)
                && authority.AuthorityEpoch.Equals(validation.Reference.AuthorityEpoch)
                && (registration.Definition.Scope == BaseSubjectScopeKind.Global
                    || string.Equals(overlay.Scope, validation.Scope.Value, StringComparison.Ordinal))
                && (validation.Requirement != BaseSubjectReferenceRequirement.Active
                    || overlay.Active == registration.Definition.ValidationPlan.Active.ActiveValue);
            if ((prepared.SubjectValidations[index].State == BaseSubjectValidationState.Valid) != structurallyValid)
                return false;
            byte[] contractKey = Encoding.UTF8.GetBytes($"{registration.Definition.Id}\n{registration.Definition.Version}");
            byte[] subjectKey = Encoding.UTF8.GetBytes($"{registration.Definition.Id}\n{registration.Definition.Version}\n{validation.Reference.SubjectId.Value}");
            byte[] recordKey = Encoding.UTF8.GetBytes(validation.Reference.SubjectId.Value);
            if (!HasExactInterval(prepared.ReadIntervals, $"subject:{registration.Definition.Id}:contract", contractKey)
                || !HasExactInterval(prepared.ReadIntervals, $"subject:{registration.Definition.Id}:lifetime", subjectKey)
                || !HasExactInterval(prepared.ReadIntervals, $"subject:{registration.Definition.Id}:record", recordKey))
                return false;
        }
        return true;
    }

    private static bool HasExactInterval(ImmutableArray<BaseAtomicReadIntervalEvidence> intervals, string path, byte[] key) =>
        intervals.Any(interval => string.Equals(interval.LogicalAccessPathId, path, StringComparison.Ordinal)
            && interval.LowerInclusive && interval.UpperInclusive
            && interval.CanonicalLowerBound.AsSpan().SequenceEqual(key)
            && interval.CanonicalUpperBound.AsSpan().SequenceEqual(key));

    private static bool ValidateApplied(
        BaseFinalizedAtomicExecutionPlan plan,
        BasePreparedAtomicExecution prepared,
        BaseProvisionalAtomicExecution applied)
    {
        bool strict = plan.SubjectValidations.Length != 0 || plan.Items.Any(static item => item.SubjectLifecycle is not null);
        if (!string.Equals(applied.PlanDigest, plan.PlanDigest, StringComparison.Ordinal)
            || applied.Facts.Length != plan.Items.Length)
            return false;
        for (int index = 0; index < plan.Items.Length; index++)
        {
            BaseRecordMutationFact fact;
            try { fact = applied.Facts[index].MaterializeOwned(); }
            catch { return false; }
            BaseAtomicMutationPlanItem item = plan.Items[index];
            if (!string.Equals(fact.Collection.Id, item.Collection.Id, StringComparison.Ordinal)
                || strict && (fact.ItemId != item.ItemId || fact.Event.EventId != item.EventId)
                || fact.RequestedOperation != item.RequestedKind
                || fact.CommittedOperation != item.Kind
                || (fact.After ?? fact.Before)?.Id != item.RecordId
                || !ValidCommittedLifecycle(item.SubjectLifecycle, prepared.SubjectOverlay, fact.SubjectLifecycle)
                || item.Kind == BaseCommittedRecordMutationKind.Delete && (fact.Before is null || fact.After is not null)
                || item.Kind != BaseCommittedRecordMutationKind.Delete && fact.After is null
                || strict && item.Current is not null && !RecordEquals(fact.Before, item.Current)
                || strict && item.Current is null && fact.Before is not null
                || strict && item.ProposedPayload is not null && !PayloadEquals(fact.After?.Payload, item.ProposedPayload)
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
            return JsonSerializer.SerializeToUtf8Bytes(left, HPDBaseJsonSerializerContext.Default.RecordEnvelope)
                .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, HPDBaseJsonSerializerContext.Default.RecordEnvelope));
        }
        catch { return false; }
    }

    private static bool PayloadEquals(RecordPayload? left, RecordPayload right)
    {
        if (left is null) return false;
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(left, HPDBaseJsonSerializerContext.Default.RecordPayload)
                .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, HPDBaseJsonSerializerContext.Default.RecordPayload));
        }
        catch { return false; }
    }

    private async ValueTask<BaseMutationAttempt> ProcessCommandAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken) =>
        command.Kind switch
        {
            BaseRecordMutationKind.Create => await CreateAsync(session, command, cancellationToken).ConfigureAwait(false),
            BaseRecordMutationKind.Patch => await PatchAsync(session, command, cancellationToken, knownExisting: _captured[command.Index].Current).ConfigureAwait(false),
            BaseRecordMutationKind.Replace => await ReplaceAsync(session, command, cancellationToken, knownExisting: _captured[command.Index].Current).ConfigureAwait(false),
            BaseRecordMutationKind.Delete => await DeleteAsync(session, command, cancellationToken, _captured[command.Index].Current).ConfigureAwait(false),
            BaseRecordMutationKind.Upsert => await UpsertAsync(session, command, cancellationToken, _captured[command.Index].Current).ConfigureAwait(false),
            _ => Failure(command, OperationStatus.ValidationFailed, Error(
                "base.runtime.batch.itemInvalid", "The mutation kind is invalid.", ErrorCategory.Validation))
        };

    private async ValueTask<BaseMutationAttempt> CreateAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken,
        BaseRecordMutationKind requested = BaseRecordMutationKind.Create,
        RecordUpsertOutcome? upsertOutcome = null)
    {
        var validated = command.CreatePayload!;
        var policyResult = await EvaluateAsync(command, PolicyResourceKind.CreatePayload, validated.Payload, null, null, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess() || policyResult.Value is null)
            return FromFailure(command, policyResult);
        if (EnforceWritePolicy<RecordEnvelope>(validated.Payload, validated.Payload, policyResult.Value) is { } gate)
            return FromFailure(command, gate);
        if (await EnforceRelationsAsync(session, command, validated.Payload, cancellationToken).ConfigureAwait(false) is { } relationError)
            return Failure(command, RelationStatus(relationError), relationError);

        return await WriteCreateAsync(
            session,
            command,
            policyResult.Value,
            requested,
            upsertOutcome,
            cancellationToken).ConfigureAwait(false);
    }

    private BaseMutationAttempt NormalizeCreateFailure(
        BaseMutationCommand command,
        OperationResult<BasePolicyEvaluation> policyResult) =>
        FromFailure(command, policyResult);

    private async ValueTask<BaseMutationAttempt> WriteCreateAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        BasePolicyEvaluation policyResult,
        BaseRecordMutationKind requested,
        RecordUpsertOutcome? upsertOutcome,
        CancellationToken cancellationToken)
    {
        var validated = command.CreatePayload!;
        var request = command.Create! with { Payload = validated.Payload };
        var result = await session.CreateAsync(
            command.Collection,
            request,
            SessionContext(command, requested, validated.ChangedFields),
            cancellationToken).ConfigureAwait(false);
        return NormalizeSession(command, result, policyResult, upsertOutcome);
    }

    private async ValueTask<BaseMutationAttempt> PatchAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken,
        BaseRecordMutationKind requested = BaseRecordMutationKind.Patch,
        RecordUpsertOutcome? upsertOutcome = null,
        RecordEnvelope? knownExisting = null)
    {
        var existingResult = knownExisting is null
            ? normalizer.NormalizeStoreResult(
                await session.GetAsync(command.Collection, command.RecordId!.Value, command.Context, cancellationToken).ConfigureAwait(false),
                command.Context)
            : OperationResults.Ok(knownExisting);
        if (!existingResult.IsSuccess() || existingResult.Value is null)
            return FromFailure(command, existingResult, providerError: true);

        var validated = command.UpdatePayload!;
        var proposedPayload = BasePolicyRuntimeSimulation.MergePatchPayload(existingResult.Value.Payload, validated.Payload);
        var proposed = existingResult.Value with { Payload = proposedPayload };
        var policyResult = await EvaluateAsync(
            command, PolicyResourceKind.UpdatePayload, proposedPayload,
            existingResult.Value, proposed, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess() || policyResult.Value is null)
            return FromFailure(command, policyResult);
        if (EnforceWritePolicy<RecordEnvelope>(proposedPayload, validated.Payload, policyResult.Value) is { } gate)
            return FromFailure(command, gate);
        if (await EnforceRelationsAsync(session, command, proposedPayload, cancellationToken).ConfigureAwait(false) is { } relationError)
            return Failure(command, RelationStatus(relationError), relationError);

        return await WritePatchAsync(
            session,
            command,
            existingResult.Value,
            policyResult.Value,
            requested,
            upsertOutcome,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<BaseMutationAttempt> WritePatchAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        RecordEnvelope existing,
        BasePolicyEvaluation policyResult,
        BaseRecordMutationKind requested,
        RecordUpsertOutcome? upsertOutcome,
        CancellationToken cancellationToken)
    {
        var validated = command.UpdatePayload!;
        var request = command.Patch! with { Patch = validated.Payload };
        var result = await session.PatchAsync(
            command.Collection,
            command.RecordId.GetValueOrDefault(),
            request,
            SessionContext(command, requested, validated.ChangedFields),
            cancellationToken).ConfigureAwait(false);
        return NormalizeSession(command, result, policyResult, upsertOutcome);
    }

    private async ValueTask<BaseMutationAttempt> ReplaceAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken,
        BaseRecordMutationKind requested = BaseRecordMutationKind.Replace,
        RecordUpsertOutcome? upsertOutcome = null,
        RecordEnvelope? knownExisting = null)
    {
        var existingResult = knownExisting is null
            ? normalizer.NormalizeStoreResult(
                await session.GetAsync(command.Collection, command.RecordId!.Value, command.Context, cancellationToken).ConfigureAwait(false),
                command.Context)
            : OperationResults.Ok(knownExisting);
        if (!existingResult.IsSuccess() || existingResult.Value is null)
            return FromFailure(command, existingResult, providerError: true);

        var validated = command.UpdatePayload!;
        var proposed = existingResult.Value with { Payload = validated.Payload };
        var policyResult = await EvaluateAsync(
            command, PolicyResourceKind.UpdatePayload, validated.Payload,
            existingResult.Value, proposed, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess() || policyResult.Value is null)
            return FromFailure(command, policyResult);
        if (EnforceWritePolicy<RecordEnvelope>(validated.Payload, validated.Payload, policyResult.Value) is { } gate)
            return FromFailure(command, gate);
        if (await EnforceRelationsAsync(session, command, validated.Payload, cancellationToken).ConfigureAwait(false) is { } relationError)
            return Failure(command, RelationStatus(relationError), relationError);

        return await WriteReplaceAsync(
            session,
            command,
            existingResult.Value,
            policyResult.Value,
            requested,
            upsertOutcome,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<BaseMutationAttempt> WriteReplaceAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        RecordEnvelope existing,
        BasePolicyEvaluation policyResult,
        BaseRecordMutationKind requested,
        RecordUpsertOutcome? upsertOutcome,
        CancellationToken cancellationToken)
    {
        var validated = command.UpdatePayload!;
        var request = command.Replace! with { Payload = validated.Payload };
        var result = await session.ReplaceAsync(
            command.Collection,
            command.RecordId.GetValueOrDefault(),
            request,
            SessionContext(command, requested, validated.ChangedFields),
            cancellationToken).ConfigureAwait(false);
        return NormalizeSession(command, result, policyResult, upsertOutcome);
    }

    private async ValueTask<BaseMutationAttempt> DeleteAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken,
        RecordEnvelope? capturedExisting)
    {
        OperationResult<RecordEnvelope> existing = capturedExisting is null
            ? OperationResults.NotFound<RecordEnvelope>(Error("base.runtime.record.notFound", "The record was not found.", ErrorCategory.NotFound))
            : OperationResults.Ok(capturedExisting);
        if (!existing.IsSuccess() || existing.Value is null)
            return FromFailure(command, existing, providerError: true);

        var policyResult = await EvaluateAsync(
            command, PolicyResourceKind.DeleteCandidate, null,
            existing.Value, null, cancellationToken).ConfigureAwait(false);
        if (!policyResult.IsSuccess())
            return FromFailure(command, policyResult);
        if (EnforceWritePolicy<DeleteResult>(existing.Value.Payload, null, policyResult.Value) is { } gate)
            return FromFailure(command, gate);

        var result = await session.DeleteAsync(
            command.Collection,
            command.RecordId!.Value,
            command.Delete!,
            SessionContext(command, BaseRecordMutationKind.Delete),
            cancellationToken).ConfigureAwait(false);
        return NormalizeSession(command, result, policyResult.Value, null);
    }

    private async ValueTask<BaseMutationAttempt> UpsertAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        CancellationToken cancellationToken,
        RecordEnvelope? capturedExisting)
    {
        var request = command.Upsert!;
        OperationResult<RecordEnvelope> existing = capturedExisting is null
            ? OperationResults.NotFound<RecordEnvelope>(Error("base.runtime.record.notFound", "The record was not found.", ErrorCategory.NotFound))
            : OperationResults.Ok(capturedExisting);
        if (existing.Status == OperationStatus.NotFound)
        {
            var validated = command.CreatePayload!;
            var policyResult = await EvaluateAsync(
                command,
                PolicyResourceKind.CreatePayload,
                validated.Payload,
                null,
                null,
                cancellationToken).ConfigureAwait(false);
            if (!policyResult.IsSuccess() || policyResult.Value is null)
                return NormalizeCreateFailure(command, policyResult);
            if (EnforceWritePolicy<RecordEnvelope>(
                    validated.Payload,
                    validated.Payload,
                    policyResult.Value) is { } gate)
            {
                return FromFailure(command, gate);
            }
            if (await EnforceRelationsAsync(session, command, validated.Payload, cancellationToken).ConfigureAwait(false) is { } createRelationError)
                return Failure(command, RelationStatus(createRelationError), createRelationError);

            if (request.Condition == RecordUpsertExistenceCondition.UpdateOnly || request.ExpectedRevision is not null)
                return Failure(command,
                    request.ExpectedRevision is null
                        ? OperationStatus.NotFound
                        : OperationStatus.Conflict,
                    Error(
                    request.ExpectedRevision is null ? "base.runtime.upsert.preconditionFailed" : "base.runtime.revision.conflict",
                    "The upsert precondition was not satisfied.",
                    request.ExpectedRevision is null ? ErrorCategory.NotFound : ErrorCategory.Conflict));

            return await WriteCreateAsync(
                session,
                command,
                policyResult.Value,
                BaseRecordMutationKind.Upsert,
                RecordUpsertOutcome.Created,
                cancellationToken).ConfigureAwait(false);
        }

        if (!existing.IsSuccess() || existing.Value is null)
            return FromFailure(command, existing, providerError: true);

        var validatedUpdate = command.UpdatePayload!;
        var proposedPayload = request.UpdateMode == RecordUpsertUpdateMode.Patch
            ? BasePolicyRuntimeSimulation.MergePatchPayload(
                existing.Value.Payload,
                validatedUpdate.Payload)
            : validatedUpdate.Payload;
        var proposed = existing.Value with { Payload = proposedPayload };
        var updatePolicy = await EvaluateAsync(
            command,
            PolicyResourceKind.UpdatePayload,
            proposedPayload,
            existing.Value,
            proposed,
            cancellationToken).ConfigureAwait(false);
        if (!updatePolicy.IsSuccess() || updatePolicy.Value is null)
            return FromFailure(command, updatePolicy);
        if (EnforceWritePolicy<RecordEnvelope>(
                proposedPayload,
                validatedUpdate.Payload,
                updatePolicy.Value) is { } updateGate)
        {
            return FromFailure(command, updateGate);
        }
        if (await EnforceRelationsAsync(session, command, proposedPayload, cancellationToken).ConfigureAwait(false) is { } relationError)
            return Failure(command, RelationStatus(relationError), relationError);

        if (request.Condition == RecordUpsertExistenceCondition.CreateOnly)
            return Failure(command, OperationStatus.Conflict, Error(
                "base.runtime.upsert.preconditionFailed",
                "The upsert precondition was not satisfied.",
                ErrorCategory.Conflict));

        return request.UpdateMode == RecordUpsertUpdateMode.Patch
            ? await WritePatchAsync(
                session,
                command,
                existing.Value,
                updatePolicy.Value,
                BaseRecordMutationKind.Upsert,
                RecordUpsertOutcome.Updated,
                cancellationToken).ConfigureAwait(false)
            : await WriteReplaceAsync(
                session,
                command,
                existing.Value,
                updatePolicy.Value,
                BaseRecordMutationKind.Upsert,
                RecordUpsertOutcome.Updated,
                cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateAsync(
        BaseMutationCommand command,
        PolicyResourceKind resourceKind,
        RecordPayload? proposedPayload,
        RecordEnvelope? existing,
        RecordEnvelope? proposed,
        CancellationToken cancellationToken)
    {
        using var activity = HPDBaseRuntimeTelemetry.StartPolicyEvaluation(
            command.Context,
            resourceKind.ToString());
        var startedAt = Stopwatch.GetTimestamp();
        var result = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = command.Context,
            Collection = command.Collection,
            ResourceKind = resourceKind,
            RecordId = command.RecordId ?? command.Upsert?.Id,
            ExistingRecord = existing,
            ProposedPayload = proposedPayload,
            ProposedRecord = proposed
        }, cancellationToken).ConfigureAwait(false);
        return HPDBaseRuntimeTelemetry.FinishPolicyEvaluation(
            activity,
            result,
            command.Context,
            startedAt);
    }

    private async ValueTask<BaseError?> EnforceRelationsAsync(
        IAtomicRecordSession session,
        BaseMutationCommand command,
        RecordPayload payload,
        CancellationToken cancellationToken)
    {
        foreach (FieldDefinition field in command.Collection.Fields ?? [])
        {
            if (field.Relation is not { OwningSide: BaseRelationOwningSide.Source } relation)
                continue;
            if (relation.DeleteBehavior is not BaseRelationDeleteBehavior.Restrict || relation.ExistenceEnforcement is not EnforcementOwner.Runtime)
                return RelationError("base.relation.enforcementUnsupported", "The declared relation enforcement mode is unavailable.", ErrorCategory.Unsupported);
            if (!_collections.TryGetValue(relation.TargetCollectionId, out CollectionDefinition? targetCollection))
                return RelationError("base.relation.invalid", "The declared relation is invalid.", ErrorCategory.Validation);
            if (!TryRelationIds(payload, field.WireName, relation, out RecordId[] ids, out string? code))
                return RelationError(code!, "The relation value has an invalid shape or cardinality.", ErrorCategory.Validation);

            foreach (RecordId id in ids)
            {
                OperationResult<RecordEnvelope> target = normalizer.NormalizeStoreResult(
                    await session.GetAsync(targetCollection, id, command.Context, cancellationToken).ConfigureAwait(false),
                    command.Context);
                if (!target.IsSuccess() || target.Value is null)
                    return RelationError("base.relation.targetUnavailable", "A relation target is unavailable.", ErrorCategory.Authorization);

                OperationResult<BasePolicyEvaluation> targetPolicy;
                try
                {
                    Task<OperationResult<BasePolicyEvaluation>> policyTask = policy.EvaluateReadAsync(new BasePolicyRequest
                    {
                        Principal = principal,
                        Operation = command.Context,
                        Collection = targetCollection,
                        ResourceKind = PolicyResourceKind.RelationTarget,
                        RecordId = id,
                        ExistingRecord = target.Value
                    }, cancellationToken).AsTask();
                    TimeSpan remaining = TimeSpan.FromSeconds(Math.Max(0, _deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);
                    if (remaining <= TimeSpan.Zero) return RelationError("base.relation.policyTimeout", "Relation policy evaluation exceeded its bounded lifetime.", ErrorCategory.Store);
                    TimeSpan publicationMargin = TimeSpan.FromMilliseconds(Math.Min(10, Math.Max(1, remaining.TotalMilliseconds / 10)));
                    remaining -= publicationMargin;
                    if (remaining <= TimeSpan.Zero) return RelationError("base.relation.policyTimeout", "Relation policy evaluation exceeded its bounded lifetime.", ErrorCategory.Store);
                    try { targetPolicy = await policyTask.WaitAsync(remaining, cancellationToken).ConfigureAwait(false); }
                    catch { Observe(policyTask); throw; }
                }
                catch (TimeoutException)
                {
                    return RelationError("base.relation.policyTimeout", "Relation policy evaluation exceeded its bounded lifetime.", ErrorCategory.Store);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    return RelationError("base.relation.targetUnavailable", "A relation target is unavailable.", ErrorCategory.Authorization);
                }
                if (!targetPolicy.IsSuccess() || targetPolicy.Value?.Decision.Effect != PolicyEffect.Allow ||
                    !BaseRecordFilterMatcher.Matches(target.Value, targetPolicy.Value.EffectiveRecordFilter))
                    return RelationError("base.relation.targetUnavailable", "A relation target is unavailable.", ErrorCategory.Authorization);
            }
        }
        return null;
    }

    private static bool TryRelationIds(RecordPayload payload, string name, RelationDefinition relation, out RecordId[] ids, out string? code)
    {
        ids = []; code = null;
        if (payload.Fields is null || !payload.Fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            if (relation.Required || relation.LocalMultiplicity == BaseRelationMultiplicity.ExactlyOne) { code = "base.relation.cardinalityInvalid"; return false; }
            return true;
        }
        if (relation.LocalMultiplicity == BaseRelationMultiplicity.Many)
        {
            if (value.ValueKind != JsonValueKind.Array) { code = "base.relation.invalid"; return false; }
            var values = new List<RecordId>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())) { code = "base.relation.invalid"; return false; }
                string id = item.GetString()!;
                if (!unique.Add(id)) { code = "base.relation.cardinalityInvalid"; return false; }
                values.Add(new RecordId(id));
            }
            if (relation.MinimumCount is int minimum && values.Count < minimum ||
                relation.MaximumCount is int maximum && values.Count > maximum)
            {
                code = "base.relation.cardinalityInvalid";
                return false;
            }
            ids = values.ToArray(); return true;
        }
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) { code = "base.relation.invalid"; return false; }
        ids = [new RecordId(value.GetString()!)]; return true;
    }

    private static OperationStatus RelationStatus(BaseError error) => error.Category switch
    {
        ErrorCategory.Validation => OperationStatus.ValidationFailed,
        ErrorCategory.Unsupported => OperationStatus.Unsupported,
        ErrorCategory.Store => OperationStatus.StoreError,
        _ => OperationStatus.PolicyDenied
    };

    private static BaseError RelationError(string code, string message, ErrorCategory category) => new() { Code = code, Message = message, Category = category };

    private static void Observe(Task task) => _ = task.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private BaseMutationAttempt NormalizeSession(
        BaseMutationCommand command,
        OperationResult<RecordMutationSessionResult> result,
        BasePolicyEvaluation? policyResult,
        RecordUpsertOutcome? upsertOutcome)
    {
        var normalized = normalizer.NormalizeStoreResult(result, command.Context);
        if (!normalized.IsSuccess() || normalized.Value is null)
            return FromFailure(command, normalized, providerError: true);

        var mutation = normalized.Value.Mutation;
        if (!ValidMutationFact(command, normalized.Value))
        {
            return Failure(command, OperationStatus.StoreError, Error(
                "base.runtime.store.malformedMutationFact",
                "The store returned an inconsistent mutation fact.",
                ErrorCategory.Store));
        }

        if (mutation.UpsertOutcome != upsertOutcome)
        {
            return Failure(command, OperationStatus.StoreError, Error(
                "base.runtime.store.malformedMutationFact",
                "The store returned an inconsistent mutation fact.",
                ErrorCategory.Store));
        }

        return new BaseMutationAttempt
        {
            Command = command,
            Status = normalized.Status,
            Mutation = mutation,
            Policy = policyResult,
            Revision = normalized.Revision
        };
    }

    private static bool ValidMutationFact(
        BaseMutationCommand command,
        RecordMutationSessionResult sessionResult)
    {
        var mutation = sessionResult.Mutation;
        if (mutation.Event is null
            || !string.Equals(mutation.Event.EventId, command.EventId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(mutation.Event.Type)
            || !Enum.IsDefined(mutation.Event.Guarantee)
            || !string.Equals(mutation.ItemId, command.ItemId, StringComparison.Ordinal)
            || !string.Equals(mutation.Collection.Id, command.Collection.Id, StringComparison.Ordinal)
            || mutation.RequestedOperation != command.Kind)
        {
            return false;
        }

        var expectedCommitted = command.Kind switch
        {
            BaseRecordMutationKind.Create => BaseCommittedRecordMutationKind.Create,
            BaseRecordMutationKind.Patch => BaseCommittedRecordMutationKind.Patch,
            BaseRecordMutationKind.Replace => BaseCommittedRecordMutationKind.Replace,
            BaseRecordMutationKind.Delete => BaseCommittedRecordMutationKind.Delete,
            BaseRecordMutationKind.Upsert when mutation.UpsertOutcome == RecordUpsertOutcome.Created
                => BaseCommittedRecordMutationKind.Create,
            BaseRecordMutationKind.Upsert when command.Upsert?.UpdateMode == RecordUpsertUpdateMode.Patch
                && mutation.UpsertOutcome == RecordUpsertOutcome.Updated
                => BaseCommittedRecordMutationKind.Patch,
            BaseRecordMutationKind.Upsert when mutation.UpsertOutcome == RecordUpsertOutcome.Updated
                => BaseCommittedRecordMutationKind.Replace,
            _ => (BaseCommittedRecordMutationKind)(-1)
        };
        if (mutation.CommittedOperation != expectedCommitted
            || !string.Equals(mutation.Event.Type, EventType(expectedCommitted), StringComparison.Ordinal))
        {
            return false;
        }

        if (command.Kind != BaseRecordMutationKind.Upsert && mutation.UpsertOutcome is not null)
            return false;

        var targetId = command.RecordId ?? command.Upsert?.Id ?? command.Create?.RequestedId;
        if (!ValidRecord(mutation.Before, command.CollectionId, targetId)
            || !ValidRecord(mutation.After, command.CollectionId, targetId))
        {
            return false;
        }

        return expectedCommitted switch
        {
            BaseCommittedRecordMutationKind.Create =>
                mutation.Before is null
                && mutation.After is not null
                && mutation.Delete is null
                && sessionResult.Record == mutation.After
                && sessionResult.Delete is null,
            BaseCommittedRecordMutationKind.Patch or BaseCommittedRecordMutationKind.Replace =>
                mutation.Before is not null
                && mutation.After is not null
                && mutation.Delete is null
                && sessionResult.Record == mutation.After
                && sessionResult.Delete is null,
            BaseCommittedRecordMutationKind.Delete =>
                mutation.Before is not null
                && mutation.After is null
                && mutation.Delete is { Deleted: true }
                && sessionResult.Record is null
                && sessionResult.Delete == mutation.Delete
                && mutation.Delete.Id == mutation.Before.Id,
            _ => false
        };
    }

    private static bool ValidRecord(
        RecordEnvelope? record,
        string collectionId,
        RecordId? targetId) =>
        record is null
        || string.Equals(record.CollectionId, collectionId, StringComparison.Ordinal)
        && (targetId is null || record.Id == targetId.Value);

    private static string EventType(BaseCommittedRecordMutationKind operation) =>
        operation switch
        {
            BaseCommittedRecordMutationKind.Create => BaseEventTypes.RecordCreated,
            BaseCommittedRecordMutationKind.Patch => BaseEventTypes.RecordPatched,
            BaseCommittedRecordMutationKind.Replace => BaseEventTypes.RecordUpdated,
            BaseCommittedRecordMutationKind.Delete => BaseEventTypes.RecordDeleted,
            _ => string.Empty
        };

    private static RecordMutationSessionContext SessionContext(
        BaseMutationCommand command,
        BaseRecordMutationKind requested,
        string[]? changedFields = null) => new()
    {
        ItemId = command.ItemId,
        RequestedOperation = requested,
        EventId = command.EventId,
        Operation = command.Context,
        ChangedFields = changedFields
    };

    private static BaseAtomicMutationIntent CreateIntent(
        BaseMutationCommand[] source,
        BaseAtomicMutationAuthorityRequirement authority,
        IReadOnlyDictionary<string, CollectionDefinition> collections)
    {
        var items = ImmutableArray.CreateBuilder<BaseAtomicMutationIntentItem>(source.Length);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(Encoding.UTF8.GetBytes("hpd.base.atomic-mutation-intent.v1"));
        for (int index = 0; index < source.Length; index++)
        {
            BaseMutationCommand command = source[index];
            RecordId id = command.RecordId ?? command.Create?.RequestedId ?? command.Upsert?.Id
                ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
            RecordCreateRequest? create = command.Create is null ? null : command.Create with { Payload = RecordCloneHelpers.ClonePayload(command.Create.Payload) };
            RecordPatchRequest? patch = command.Patch is null ? null : command.Patch with { Patch = RecordCloneHelpers.ClonePayload(command.Patch.Patch) };
            RecordReplaceRequest? replace = command.Replace is null ? null : command.Replace with { Payload = RecordCloneHelpers.ClonePayload(command.Replace.Payload) };
            RecordUpsertRequest? upsert = command.Upsert is null ? null : command.Upsert with
            {
                CreatePayload = RecordCloneHelpers.ClonePayload(command.Upsert.CreatePayload),
                UpdatePayload = RecordCloneHelpers.ClonePayload(command.Upsert.UpdatePayload),
            };
            items.Add(new BaseAtomicMutationIntentItem
            {
                Ordinal = index, Collection = command.Collection with { Fields = command.Collection.Fields?.Select(static field => field with { }).ToArray() },
                RequestedKind = command.Kind, RecordId = id, RuntimeAssignedRecordId = command.RuntimeAssignedRecordId,
                Create = create, Patch = patch, Replace = replace,
                Upsert = upsert, Delete = command.Delete is null ? null : command.Delete with { },
                RelationTargets = RelationTargetIntents(command, collections), Operation = command.Context with { },
            });
            digest.AppendData(Encoding.UTF8.GetBytes($"{index}\0{command.CollectionId}\0{(int)command.Kind}\0{id.Value}\0{command.ItemId}\0"));
            RecordPayload? payload = command.CreatePayload?.Payload ?? command.UpdatePayload?.Payload;
            if (payload is not null) digest.AppendData(JsonSerializer.SerializeToUtf8Bytes(payload, HPDBaseJsonSerializerContext.Default.RecordPayload));
        }
        return new BaseAtomicMutationIntent
        {
            IntentDigest = Convert.ToHexStringLower(digest.GetHashAndReset()), Authority = authority with { }, Items = items.MoveToImmutable(),
        };
    }

    internal static BaseAtomicMutationExecutionLimits CreateExecutionLimits(
        BaseMutationCommand[] source,
        TimeSpan requestedTimeout,
        BaseSubjectContractRegistry subjects)
    {
        BaseSubjectValidationLimits[] participating = source
            .SelectMany(command => (command.Collection.Fields ?? [])
                .Where(static field => field.SubjectReference is not null)
                .Select(field => subjects.Find(field.SubjectReference!.ContractId, field.SubjectReference.ContractVersion)))
            .Concat(source.SelectMany(command => subjects.All.Where(subject =>
                string.Equals(subject.Definition.ValidationPlan.PrivateCollectionId, command.CollectionId, StringComparison.Ordinal))))
            .Where(static registration => registration is not null)
            .Select(static registration => registration!.Definition.ValidationPlan.Limits)
            .Distinct()
            .ToArray();

        int MinInt(Func<BaseSubjectValidationLimits, int> select, int fallback) =>
            participating.Length == 0 ? fallback : participating.Min(select);
        long MinLong(Func<BaseSubjectValidationLimits, long> select, long fallback) =>
            participating.Length == 0 ? fallback : participating.Min(select);
        TimeSpan timeout = participating.Length == 0
            ? requestedTimeout
            : participating.Select(static value => value.ExecutionTimeout).Append(requestedTimeout).Min();

        return new BaseAtomicMutationExecutionLimits
        {
            MaximumItems = 1_024,
            MaximumQueryNodes = 1,
            MaximumQueryDepth = 1,
            MaximumLiteralValues = 1,
            MaximumSelectedRecords = 1_024,
            MaximumProducedMutations = 1_024,
            MaximumQueryExecutions = 1,
            MaximumPreviousStateRequirements = 1_024,
            MaximumRecordCaptures = 1_024,
            MaximumRelationTargetCaptures = 1_024,
            MaximumGenerationReads = 1,
            MaximumGenerationComparisons = 1,
            MaximumGenerationIncrements = 1,
            MaximumGuardNodes = 1,
            MaximumGuardDepth = 1,
            MaximumStatements = 1_024,
            MaximumBranches = 1,
            MaximumExpressionNodes = 1,
            MaximumSelectedBytes = MinLong(static value => value.MaximumSelectedBytes, 8_388_608),
            MaximumEvidenceBytes = MinLong(static value => value.MaximumEvidenceBytes, 8_388_608),
            MaximumTransientBytes = MinLong(static value => value.MaximumTransientBytes, 67_108_864),
            MaximumReadIntervals = MinInt(static value => value.MaximumReadIntervals, 1_024),
            MaximumSubjectValidations = MinInt(static value => value.MaximumReferencesPerMutation, 1_024),
            MaximumAuthorityReads = MinInt(static value => value.MaximumAuthorityReads, 1_024),
            MaximumRelationChecks = 1_024,
            MaximumUniqueConstraintChecks = 1_024,
            MaximumRequestBytes = 8_388_608,
            MaximumGenerationBytes = 1,
            MaximumWrittenBytes = 67_108_864,
            MaximumFactBytes = 67_108_864,
            MaximumJournalBytes = 67_108_864,
            MaximumReceiptBytes = 8_388_608,
            MaximumResultBytes = 8_388_608,
            Deadlines = new BaseAtomicMutationDeadlines
            {
                AcquisitionTimeout = timeout,
                TransactionTimeout = timeout,
                CommitObservationTimeout = timeout,
                ReceiptResolutionTimeout = timeout,
            },
        };
    }

    private bool ValidateSubjectContractLimits(
        ImmutableArray<BaseAtomicMutationPlanItem> items,
        ImmutableArray<BaseSubjectReferenceValidationPlanItem> validations)
    {
        BaseGeneratedSubjectRegistration[] participating = validations
            .Select(validation => subjects.All.SingleOrDefault(candidate =>
                candidate.Definition.ValidationPlan.Id == validation.ValidationPlanId
                && candidate.Definition.ValidationPlan.Version == validation.ValidationPlanVersion))
            .Concat(items.Where(static item => item.SubjectLifecycle is not null).Select(item =>
                subjects.Find(item.SubjectLifecycle!.ContractId, item.SubjectLifecycle.ContractVersion)))
            .Where(static registration => registration is not null)
            .Select(static registration => registration!)
            .DistinctBy(static registration => (registration.Definition.Id, registration.Definition.Version))
            .ToArray();
        if (participating.Length == 0)
            return validations.Length == 0;
        if (participating.Any(registration =>
                validations.Length > registration.Definition.ValidationPlan.Limits.MaximumReferencesPerMutation
                || validations.Select(static validation => (validation.ValidationPlanId, validation.ValidationPlanVersion)).Distinct().Count()
                    > registration.Definition.ValidationPlan.Limits.MaximumValidationPlansPerMutation))
            return false;
        foreach (IGrouping<int, BaseSubjectReferenceValidationPlanItem> record in validations.GroupBy(static validation => validation.MutationOrdinal))
        {
            BaseGeneratedSubjectRegistration[] targets = record.Select(validation => subjects.All.Single(candidate =>
                    candidate.Definition.ValidationPlan.Id == validation.ValidationPlanId
                    && candidate.Definition.ValidationPlan.Version == validation.ValidationPlanVersion))
                .DistinctBy(static registration => (registration.Definition.Id, registration.Definition.Version))
                .ToArray();
            if (targets.Any(target => record.Count() > target.Definition.ValidationPlan.Limits.MaximumReferencesPerRecord))
                return false;
        }
        return validations.Length <= participating.Min(static registration => registration.Definition.ValidationPlan.Limits.MaximumReferencesPerMutation);
    }

    internal static ImmutableArray<BaseAtomicRelationTargetIntent> RelationTargetIntents(
        BaseMutationCommand command,
        IReadOnlyDictionary<string, CollectionDefinition> collections)
    {
        var result = new Dictionary<(string Field, string Collection, string Record), BaseAtomicRelationTargetIntent>();
        IEnumerable<RecordPayload> payloads = command.Kind switch
        {
            BaseRecordMutationKind.Create => command.CreatePayload is null ? [] : [command.CreatePayload.Payload],
            BaseRecordMutationKind.Patch or BaseRecordMutationKind.Replace => command.UpdatePayload is null ? [] : [command.UpdatePayload.Payload],
            BaseRecordMutationKind.Upsert => new[] { command.CreatePayload?.Payload, command.UpdatePayload?.Payload }.OfType<RecordPayload>(),
            _ => [],
        };
        foreach (FieldDefinition field in command.Collection.Fields ?? [])
        {
            if (field.Relation is not { OwningSide: BaseRelationOwningSide.Source } relation ||
                !collections.TryGetValue(relation.TargetCollectionId, out CollectionDefinition? targetCollection))
                continue;
            foreach (RecordPayload payload in payloads)
            {
                if (!TryRelationIds(payload, field.WireName, relation, out RecordId[] ids, out _)) continue;
                foreach (RecordId id in ids)
                    result.TryAdd((field.Id, targetCollection.Id, id.Value), new BaseAtomicRelationTargetIntent
                    {
                        SourceFieldId = field.Id,
                        TargetCollection = targetCollection with { Fields = targetCollection.Fields?.Select(static value => value with { }).ToArray() },
                        TargetRecordId = id,
                    });
            }
        }
        return result.Values.OrderBy(static value => value.SourceFieldId, StringComparer.Ordinal)
            .ThenBy(static value => value.TargetCollection.Id, StringComparer.Ordinal)
            .ThenBy(static value => value.TargetRecordId.Value, StringComparer.Ordinal).ToImmutableArray();
    }

    private static RecordId TargetId(BaseMutationCommand command) =>
        command.RecordId ?? command.Create?.RequestedId ?? command.Upsert?.Id
        ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);

    private bool HasPotentialSubjectWork() => commands.Any(command =>
        (command.Collection.Fields ?? []).Any(static field => field.SubjectReference is not null)
        || subjects.All.Any(subject => string.Equals(
            subject.Definition.ValidationPlan.PrivateCollectionId,
            command.Collection.Id,
            StringComparison.Ordinal)));

    private static bool HasSubjectWork(BaseFinalizedAtomicExecutionPlan plan) =>
        plan.SubjectValidations.Length != 0 || plan.Items.Any(static item => item.SubjectLifecycle is not null);

    private AtomicMutationProcessingResult Failed(BaseError error) =>
        new(AtomicMutationProcessingOutcome.Failed, _attempts
            .Where(static attempt => attempt.Mutation is not null)
            .Select(static attempt => attempt.Mutation!)
            .ToArray(), error);

    private AtomicMutationProcessingResult FailedProvider(OperationStatus status, BaseError? error) =>
        Failed(BaseSubjectFailureContract.NormalizeProviderError(status, error));

    private static BaseMutationAttempt FromFailure<T>(
        BaseMutationCommand command,
        OperationResult<T> result,
        bool providerError = false)
    {
        if (command.Kind == BaseRecordMutationKind.Upsert
            && (result.Status is OperationStatus.PolicyDenied or OperationStatus.Unauthorized
                || result.Error?.Category is ErrorCategory.Authorization or ErrorCategory.Authentication))
        {
            return Failure(
                command,
                OperationStatus.PolicyDenied,
                Error(
                    "base.runtime.policy.denied",
                    "Policy denied the operation.",
                    ErrorCategory.Authorization),
                providerError: false);
        }

        return Failure(
            command,
            result.Status,
            result.Error ?? Error(
                "base.runtime.batch.itemInvalid",
                "A batch item failed.",
                ErrorCategory.Unexpected),
            providerError);
    }

    private static BaseMutationAttempt Failure(
        BaseMutationCommand command,
        OperationStatus status,
        BaseError error,
        bool providerError = false) => new()
    {
        Command = command,
        Status = status,
        Error = error,
        ProviderError = providerError
    };

    private static OperationResult<T>? EnforceWritePolicy<T>(
        RecordPayload? predicatePayload,
        RecordPayload? changedPayload,
        BasePolicyEvaluation? evaluation)
    {
        if (evaluation?.Decision.Constraints?.WriteCheck is { } writeCheck)
        {
            var outcome = BasePolicyWriteConstraintEvaluator.Evaluate(predicatePayload, writeCheck);
            if (outcome == BasePolicyWriteCheckEvaluation.Unsupported)
                return OperationResults.Unsupported<T>(Error(
                    "base.runtime.policy.writeCheck.unsupported",
                    "Policy write check is not safely evaluable by this runtime.",
                    ErrorCategory.Unsupported));
            if (outcome == BasePolicyWriteCheckEvaluation.Denied)
                return OperationResults.PolicyDenied<T>(Error(
                    "base.runtime.policy.writeCheck.denied",
                    "Policy write check denied the operation.",
                    ErrorCategory.Authorization));
        }

        if (evaluation?.EffectiveWriteMask is not { } mask)
            return null;
        var fields = changedPayload is null ? [] : BasePolicyRuntimeSimulation.PayloadFields(changedPayload);
        var denied = mask.Mode switch
        {
            FieldMaskMode.Unspecified or FieldMaskMode.AllowAll => null,
            FieldMaskMode.DenyAll => fields.FirstOrDefault(),
            FieldMaskMode.IncludeOnly => fields.FirstOrDefault(field => !(mask.Include ?? []).Contains(field, StringComparer.Ordinal)),
            FieldMaskMode.Exclude => fields.FirstOrDefault(field => (mask.Exclude ?? []).Contains(field, StringComparer.Ordinal)),
            _ => fields.FirstOrDefault()
        };
        return denied is null ? null : OperationResults.PolicyDenied<T>(Error(
            "base.runtime.policy.writeMask.denied",
            "Policy write mask does not allow this field to be written.",
            ErrorCategory.Authorization));
    }

    private static BaseError Error(string code, string message, ErrorCategory category) => new()
    {
        Code = code,
        Message = message,
        Category = category
    };
}
