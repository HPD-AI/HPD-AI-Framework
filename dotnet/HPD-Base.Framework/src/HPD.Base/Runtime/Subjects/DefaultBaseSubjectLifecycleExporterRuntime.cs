using System.Text.Json;

namespace HPD.Base;

internal interface IBaseSubjectLifecycleExporterRuntime
{
    ValueTask<BaseResult<BaseSubjectLifecycleFact<TSubject>>> TombstoneAsync<TSubject>(BaseSession session, BaseGeneratedSubjectRegistration registration, BaseSubjectTombstoneRequest<TSubject> request, CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectFinalRetirementResult<TSubject>>> FinalizeRetirementAsync<TSubject>(BaseSession session, BaseGeneratedSubjectRegistration registration, BaseSubjectFinalRetirementRequest<TSubject> request, CancellationToken cancellationToken);
}

internal sealed class DefaultBaseSubjectLifecycleExporterRuntime(
    IBaseMutationCoordinator mutations,
    IBasePolicyOrchestrator policy,
    BaseCollectionRegistry collections) : IBaseSubjectLifecycleExporterRuntime
{
    private const string TombstoneGrant = "base.subjectLifecycle.tombstone";
    private const string FinalizeGrant = "base.subjectLifecycle.finalizeRetirement";

    public async ValueTask<BaseResult<BaseSubjectLifecycleFact<TSubject>>> TombstoneAsync<TSubject>(BaseSession session, BaseGeneratedSubjectRegistration registration, BaseSubjectTombstoneRequest<TSubject> request, CancellationToken cancellationToken)
    {
        OperationContext operation = session.Operation(BaseOperationKind.SubjectLifecycleTombstone, registration.Definition.Id, new RecordId(request.Subject.SubjectId.Value));
        if (!await AuthorizedAsync(session, registration, operation, TombstoneGrant, cancellationToken).ConfigureAwait(false))
            return Failure<BaseSubjectLifecycleFact<TSubject>>(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        if (!TryCollection(registration, out CollectionDefinition? collection, out FieldDefinition? tombstone, out FieldDefinition? active))
            return Failure<BaseSubjectLifecycleFact<TSubject>>(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleContractInvalid, ErrorCategory.Capability);
        CollectionDefinition selectedCollection = collection!;
        FieldDefinition selectedTombstone = tombstone!;
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [selectedTombstone.WireName] = BooleanElement(true) };
        if (active is not null) fields[active.WireName] = BooleanElement(false);
        OperationResult<BaseRecordBatchResult> execution = await ExecuteAsync(session, operation, request.Identity, new BaseRecordBatchItem
        {
            ItemId = "subject-lifecycle-tombstone", CollectionId = selectedCollection.Id, Kind = BaseRecordMutationKind.Patch,
            RecordId = new RecordId(request.Subject.SubjectId.Value),
            Patch = new RecordPatchRequest { Patch = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields }, ExpectedRevision = request.ExpectedPrivateRevision },
            OperationOverride = BaseOperationKind.SubjectLifecycleTombstone,
            SubjectLifecycleTransition = Transition(request.Subject, null, BaseSubjectLifecycleState.Tombstoned),
        }, cancellationToken).ConfigureAwait(false);
        if (!execution.IsSuccess() || execution.Value is null) return Failure<BaseSubjectLifecycleFact<TSubject>>(execution.Status, execution.Error?.Code ?? BaseSubjectErrorCodes.LifecycleProviderContractInvalid, execution.Error?.Category ?? ErrorCategory.Store);
        BaseRecordBatchItemResult item = execution.Value.Items.Single();
        if (execution.Value.Outcome != BaseRecordBatchOutcome.Committed || item.Disposition != BaseRecordBatchItemDisposition.Committed)
            return Failure<BaseSubjectLifecycleFact<TSubject>>(item.Status, item.Error?.Code ?? execution.Value.Error?.Code ?? BaseSubjectErrorCodes.LifecycleProviderContractInvalid, item.Error?.Category ?? execution.Value.Error?.Category ?? ErrorCategory.Store);
        if (item.SubjectLifecycle is not { } evidence) return Failure<BaseSubjectLifecycleFact<TSubject>>(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Capability);
        return Success(new BaseSubjectLifecycleFact<TSubject> { Subject = request.Subject, Fact = Fact(evidence) }, execution.Value.RequestDisposition == BaseMutationRequestDisposition.Duplicate ? OperationStatus.Ok : OperationStatus.Updated);
    }

    public async ValueTask<BaseResult<BaseSubjectFinalRetirementResult<TSubject>>> FinalizeRetirementAsync<TSubject>(BaseSession session, BaseGeneratedSubjectRegistration registration, BaseSubjectFinalRetirementRequest<TSubject> request, CancellationToken cancellationToken)
    {
        OperationContext operation = session.Operation(BaseOperationKind.SubjectLifecycleFinalizeRetirement, registration.Definition.Id, new RecordId(request.Subject.SubjectId.Value));
        if (!await AuthorizedAsync(session, registration, operation, FinalizeGrant, cancellationToken).ConfigureAwait(false))
            return Failure<BaseSubjectFinalRetirementResult<TSubject>>(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        if (!TryCollection(registration, out CollectionDefinition? collection, out _, out _))
            return Failure<BaseSubjectFinalRetirementResult<TSubject>>(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleContractInvalid, ErrorCategory.Capability);
        CollectionDefinition selectedCollection = collection!;
        OperationResult<BaseRecordBatchResult> execution = await ExecuteAsync(session, operation, request.Identity, new BaseRecordBatchItem
        {
            ItemId = "subject-lifecycle-finalize", CollectionId = selectedCollection.Id, Kind = BaseRecordMutationKind.Delete,
            RecordId = new RecordId(request.Subject.SubjectId.Value), Delete = new RecordDeleteRequest { ExpectedRevision = request.ExpectedPrivateRevision, ReturnPrevious = false },
            OperationOverride = BaseOperationKind.SubjectLifecycleFinalizeRetirement,
            SubjectLifecycleTransition = Transition(request.Subject, request.ExpectedTombstoneSequence, BaseSubjectLifecycleState.Retired),
        }, cancellationToken).ConfigureAwait(false);
        if (!execution.IsSuccess() || execution.Value is null) return Failure<BaseSubjectFinalRetirementResult<TSubject>>(execution.Status, execution.Error?.Code ?? BaseSubjectErrorCodes.LifecycleProviderContractInvalid, execution.Error?.Category ?? ErrorCategory.Store);
        BaseRecordBatchItemResult item = execution.Value.Items.Single();
        if (execution.Value.Outcome != BaseRecordBatchOutcome.Committed || item.Disposition != BaseRecordBatchItemDisposition.Committed)
            return Failure<BaseSubjectFinalRetirementResult<TSubject>>(item.Status, item.Error?.Code ?? execution.Value.Error?.Code ?? BaseSubjectErrorCodes.LifecycleProviderContractInvalid, item.Error?.Category ?? execution.Value.Error?.Category ?? ErrorCategory.Store);
        BaseSubjectLifecycleCommitEvidence? evidence = item.SubjectLifecycle;
        if (evidence is null) return Failure<BaseSubjectFinalRetirementResult<TSubject>>(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Capability);
        return Success(new BaseSubjectFinalRetirementResult<TSubject> { Subject = request.Subject, RetiredSubjectSequence = evidence.SubjectSequence, RetiredPosition = evidence.CommitPosition, Duplicate = execution.Value.RequestDisposition == BaseMutationRequestDisposition.Duplicate }, OperationStatus.Deleted);
    }

    private async ValueTask<bool> AuthorizedAsync(BaseSession session, BaseGeneratedSubjectRegistration registration, OperationContext operation, string grantId, CancellationToken cancellationToken)
    {
        var resource = new CollectionDefinition { Id = registration.Definition.Id, Name = registration.Definition.Id, Kind = BaseCollectionKinds.Custom, SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, System = true, SystemOwnerModuleId = registration.Definition.OwningModuleId };
        OperationResult<BasePolicyEvaluation> result = await policy.EvaluateWriteAsync(new BasePolicyRequest { Principal = session.Principal, Operation = operation, Collection = resource, ResourceKind = PolicyResourceKind.SubjectLifecycle, SubjectContractId = registration.Definition.Id, SubjectContractVersion = registration.Definition.Version }, cancellationToken).ConfigureAwait(false);
        return BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(
            result, grantId, registration.Definition.OwningModuleId, grantId,
            registration.Definition.Id, registration.Definition.Version,
            session.Principal, operation);
    }

    private bool TryCollection(BaseGeneratedSubjectRegistration registration, out CollectionDefinition? collection, out FieldDefinition? tombstone, out FieldDefinition? active)
    {
        collection = collections.Collections.GetValueOrDefault(registration.Definition.ValidationPlan.PrivateCollectionId);
        tombstone = collection?.Fields?.SingleOrDefault(field => field.Id == registration.Definition.TombstoneFieldId);
        active = registration.Definition.ValidationPlan.Active.FieldId is { } activeId ? collection?.Fields?.SingleOrDefault(field => field.Id == activeId) : null;
        return collection is not null && tombstone is not null;
    }

    private ValueTask<OperationResult<BaseRecordBatchResult>> ExecuteAsync(BaseSession session, OperationContext operation, BaseMutationRequestIdentity identity, BaseRecordBatchItem item, CancellationToken cancellationToken) =>
        mutations.ExecuteBatchAsync(new BaseRecordBatchRequest { Mode = BaseRecordBatchExecutionMode.Atomic, Operations = [item], RequestIdentity = identity }, session.Principal, operation, cancellationToken);

    private static BaseSubjectLifecycleTransitionPrecondition Transition<TSubject>(BaseSubjectReference<TSubject> subject, long? sequence, BaseSubjectLifecycleState state) => new() { Subject = new BaseOwnedSubjectReference(subject.SubjectId, subject.AuthorityEpoch, subject.Incarnation), ExpectedSubjectSequence = sequence, ResultingState = state };
    private static JsonElement BooleanElement(bool value)
    {
        using JsonDocument document = JsonDocument.Parse(value ? "true" : "false");
        return document.RootElement.Clone();
    }
    private static BaseSubjectLifecycleFact Fact(BaseSubjectLifecycleCommitEvidence value) => new() { CommitPosition = value.CommitPosition, ContractId = value.ContractId, ContractVersion = value.ContractVersion, SubjectId = BaseSubjectId.Create(value.SubjectId, BaseSubjectIdKind.OrdinalString, 256), AuthorityEpoch = value.AuthorityEpoch, Incarnation = value.Incarnation, SubjectSequence = value.SubjectSequence, ContractStateGeneration = value.ContractStateGeneration, DeliveryEpoch = value.DeliveryEpoch, Kind = BaseSubjectLifecycleFactKind.Transitioned, Transitioned = new() { PreviousState = value.PreviousState!.Value, CurrentState = value.ResultingState } };
    private static BaseSuccess<T> Success<T>(T value, OperationStatus status) => new(value, status, null, null, null, null);
    private static BaseFailure<T> Failure<T>(OperationStatus status, string code, ErrorCategory category) => new(status, BaseSubjectFailureContract.Error(code), null, null);
}
