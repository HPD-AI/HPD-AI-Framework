using System.Text.Json;

namespace HPD.Base;

internal interface IBaseSubjectLifecycleExporterRuntime
{
    ValueTask<BaseResult<BaseSubjectTombstoneResult<TSubject>>> TombstoneAsync<TSubject>(BaseSession session, BaseGeneratedSubjectRegistration registration, BaseSubjectTombstoneRequest<TSubject> request, CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectFinalRetirementResult<TSubject>>> FinalizeRetirementAsync<TSubject>(BaseSession session, BaseGeneratedSubjectRegistration registration, BaseSubjectFinalRetirementRequest<TSubject> request, BaseSubjectFinalRetirementExecutionOptions? options, CancellationToken cancellationToken);
}

internal sealed class DefaultBaseSubjectLifecycleExporterRuntime(
    IBaseMutationCoordinator mutations,
    IBasePolicyOrchestrator policy,
    BaseCollectionRegistry collections) : IBaseSubjectLifecycleExporterRuntime
{
    private const string TombstoneGrant = "base.subjectLifecycle.tombstone";
    private const string FinalizeGrant = "base.subjectLifecycle.finalizeRetirement";

    public async ValueTask<BaseResult<BaseSubjectTombstoneResult<TSubject>>> TombstoneAsync<TSubject>(BaseSession session, BaseGeneratedSubjectRegistration registration, BaseSubjectTombstoneRequest<TSubject> request, CancellationToken cancellationToken)
    {
        OperationContext operation = session.Operation(BaseOperationKind.SubjectLifecycleTombstone, registration.Definition.Id, RecordId.Create(request.Subject.SubjectId.Value));
        if (!await AuthorizedAsync(session, registration, operation, TombstoneGrant, cancellationToken).ConfigureAwait(false))
            return Failure<BaseSubjectTombstoneResult<TSubject>>(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        if (!TryCollection(registration, out CollectionDefinition? collection, out FieldDefinition? tombstone, out FieldDefinition? active))
            return Failure<BaseSubjectTombstoneResult<TSubject>>(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleContractInvalid, ErrorCategory.Capability);
        CollectionDefinition selectedCollection = collection!;
        FieldDefinition selectedTombstone = tombstone!;
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [selectedTombstone.WireName] = BooleanElement(true) };
        if (active is not null) fields[active.WireName] = BooleanElement(false);
        OperationResult<BaseRecordBatchResult> execution = await ExecuteAsync(session, operation, request.Identity, new BaseRecordBatchItem
        {
            ItemId = "subject-lifecycle-tombstone", CollectionId = selectedCollection.Id, Kind = BaseRecordMutationKind.Patch,
            RecordId = RecordId.Create(request.Subject.SubjectId.Value),
            Patch = new RecordPatchRequest { Patch = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields }, RemovedFieldIds = [], ExpectedRevision = request.ExpectedPrivateRevision },
            OperationOverride = BaseOperationKind.SubjectLifecycleTombstone,
            SubjectLifecycleTransition = Transition(request.Subject, null, BaseSubjectLifecycleState.Tombstoned),
        }, TombstoneReceipt(registration, request.Subject, operation.Now), cancellationToken).ConfigureAwait(false);
        if (!execution.IsSuccess() || execution.Value is null) return Failure<BaseSubjectTombstoneResult<TSubject>>(execution.Status, execution.Error?.Code ?? BaseSubjectErrorCodes.LifecycleProviderContractInvalid, execution.Error?.Category ?? ErrorCategory.Store);
        BaseRecordBatchItemResult item = execution.Value.Items.Single();
        if (execution.Value.Outcome != BaseRecordBatchOutcome.Committed || item.Disposition != BaseRecordBatchItemDisposition.Committed)
            return Failure<BaseSubjectTombstoneResult<TSubject>>(item.Status, item.Error?.Code ?? execution.Value.Error?.Code ?? BaseSubjectErrorCodes.LifecycleProviderContractInvalid, item.Error?.Category ?? execution.Value.Error?.Category ?? ErrorCategory.Store);
        if (item.SubjectLifecycle is not { } evidence || item.Record?.Metadata.Revision is not { } revision
            || item.Record.Metadata.UpdatedAt is not { } tombstonedAt)
            return Failure<BaseSubjectTombstoneResult<TSubject>>(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Capability);
        bool duplicate = execution.Value.RequestDisposition == BaseMutationRequestDisposition.Duplicate;
        return Success(new BaseSubjectTombstoneResult<TSubject>
        {
            Fact = new BaseSubjectLifecycleFact<TSubject> { Subject = request.Subject, Fact = Fact(evidence) },
            PrivateRevision = revision,
            TombstonedAt = tombstonedAt,
            Duplicate = duplicate,
        }, duplicate ? OperationStatus.Ok : OperationStatus.Updated);
    }

    public async ValueTask<BaseResult<BaseSubjectFinalRetirementResult<TSubject>>> FinalizeRetirementAsync<TSubject>(BaseSession session, BaseGeneratedSubjectRegistration registration, BaseSubjectFinalRetirementRequest<TSubject> request, BaseSubjectFinalRetirementExecutionOptions? options, CancellationToken cancellationToken)
    {
        OperationContext operation = session.Operation(BaseOperationKind.SubjectLifecycleFinalizeRetirement, registration.Definition.Id, RecordId.Create(request.Subject.SubjectId.Value));
        BaseActivationGuard? guard = options?.ActivationGuard;
        if (!ValidFinalExecutionAuthority(session, registration.Definition.FinalRetirementExecutionMode, guard))
            return Failure<BaseSubjectFinalRetirementResult<TSubject>>(OperationStatus.Conflict,
                guard is null ? "base.activation.guardRequired" : "base.activation.guardInvalid", ErrorCategory.Conflict);
        if (!session.ActivationDeclaresSourceGrants(FinalizeGrant))
            return Failure<BaseSubjectFinalRetirementResult<TSubject>>(OperationStatus.PolicyDenied,
                BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        if (!await AuthorizedAsync(session, registration, operation, FinalizeGrant, cancellationToken).ConfigureAwait(false))
            return Failure<BaseSubjectFinalRetirementResult<TSubject>>(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        if (!TryCollection(registration, out CollectionDefinition? collection, out _, out _))
            return Failure<BaseSubjectFinalRetirementResult<TSubject>>(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleContractInvalid, ErrorCategory.Capability);
        CollectionDefinition selectedCollection = collection!;
        OperationResult<BaseRecordBatchResult> execution = await ExecuteAsync(session, operation, request.Identity, new BaseRecordBatchItem
        {
            ItemId = "subject-lifecycle-finalize", CollectionId = selectedCollection.Id, Kind = BaseRecordMutationKind.Delete,
            RecordId = RecordId.Create(request.Subject.SubjectId.Value), Delete = new RecordDeleteRequest { ExpectedRevision = request.ExpectedPrivateRevision, ReturnPrevious = false },
            OperationOverride = BaseOperationKind.SubjectLifecycleFinalizeRetirement,
            SubjectLifecycleTransition = Transition(request.Subject, request.ExpectedTombstoneSequence, BaseSubjectLifecycleState.Retired),
        }, guard, cancellationToken).ConfigureAwait(false);
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

    private ValueTask<OperationResult<BaseRecordBatchResult>> ExecuteAsync(BaseSession session, OperationContext operation,
        BaseMutationRequestIdentity identity, BaseRecordBatchItem item, BaseActivationGuard? activationGuard,
        CancellationToken cancellationToken) =>
        mutations.ExecuteBatchAsync(new BaseRecordBatchRequest
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            Operations = [item],
            RequestIdentity = identity,
            ActivationGuard = activationGuard,
        }, session.Principal, operation, cancellationToken);

    private ValueTask<OperationResult<BaseRecordBatchResult>> ExecuteAsync(BaseSession session, OperationContext operation,
        BaseMutationRequestIdentity identity, BaseRecordBatchItem item, BaseAtomicReceiptProjection? receiptProjection,
        CancellationToken cancellationToken) =>
        mutations.ExecuteBatchAsync(new BaseRecordBatchRequest
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            Operations = [item],
            RequestIdentity = identity,
            ReceiptProjection = receiptProjection,
        }, session.Principal, operation, cancellationToken);

    private static BaseAtomicReceiptProjection TombstoneReceipt<TSubject>(BaseGeneratedSubjectRegistration registration,
        BaseSubjectReference<TSubject> subject, DateTimeOffset acceptedAt) => new()
    {
        Kind = BaseAtomicReceiptResultKind.SubjectTombstone,
        Create = facts => CreateTombstoneReceipt(registration, subject, acceptedAt, facts),
        ValidateStored = receipt => ValidTombstoneReceipt(registration, subject, receipt),
    };

    private static bool ValidFinalExecutionAuthority(BaseSession session, BaseSubjectFinalExecutionMode mode,
        BaseActivationGuard? guard)
    {
        BaseActivationSessionProvenance? provenance = session.ActivationProvenance;
        if (provenance is null)
            return guard is null && mode == BaseSubjectFinalExecutionMode.OrdinaryOrActivationGuarded;
        return guard is not null && provenance.Matches(guard.Claim);
    }

    private static BaseAtomicReceiptResult CreateTombstoneReceipt<TSubject>(BaseGeneratedSubjectRegistration registration,
        BaseSubjectReference<TSubject> subject, DateTimeOffset acceptedAt, BaseRecordMutationFact[] facts)
    {
        if (facts is not [BaseRecordMutationFact { SubjectLifecycle: { } evidence, After.Metadata.Revision: { } revision } mutation]
            || evidence.ResultingState != BaseSubjectLifecycleState.Tombstoned
            || !evidence.AuthorityEpoch.Equals(subject.AuthorityEpoch)
            || !evidence.Incarnation.Equals(subject.Incarnation))
            throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        return new BaseAtomicReceiptResult
        {
            Kind = BaseAtomicReceiptResultKind.SubjectTombstone,
            Mutations = [BaseOwnedMutationFact.Freeze(mutation, 1)],
            SubjectTombstone = new BaseAtomicSubjectTombstoneReceiptResult
            {
                SubjectContractId = registration.Definition.Id,
                SubjectContractVersion = registration.Definition.Version,
                Fact = OwnedFact(registration, evidence),
                PrivateRevision = revision,
                TombstonedAt = acceptedAt,
            },
        };
    }

    private static bool ValidTombstoneReceipt<TSubject>(BaseGeneratedSubjectRegistration registration,
        BaseSubjectReference<TSubject> subject, BaseAtomicReceiptResult receipt)
    {
        if (receipt.SubjectTombstone is not { } tombstone || receipt.Mutations.Length != 1)
            return false;
        BaseOwnedSubjectLifecycleFact fact = tombstone.Fact;
        BaseRecordMutationFact mutation;
        try { mutation = receipt.Mutations[0].MaterializeOwned(); }
        catch { return false; }
        return string.Equals(tombstone.SubjectContractId, registration.Definition.Id, StringComparison.Ordinal)
            && tombstone.SubjectContractVersion == registration.Definition.Version
            && string.Equals(fact.ContractChecksum, registration.Checksum, StringComparison.Ordinal)
            && fact.SubjectId.Equals(subject.SubjectId)
            && fact.AuthorityEpoch.Equals(subject.AuthorityEpoch)
            && fact.Incarnation.Equals(subject.Incarnation)
            && mutation.SubjectLifecycle is { } evidence
            && string.Equals(evidence.ContractId, fact.ContractId, StringComparison.Ordinal)
            && evidence.ContractVersion == fact.ContractVersion
            && string.Equals(evidence.SubjectId, fact.SubjectId.Value, StringComparison.Ordinal)
            && evidence.AuthorityEpoch.Equals(fact.AuthorityEpoch)
            && evidence.Incarnation.Equals(fact.Incarnation)
            && evidence.SubjectSequence == fact.SubjectSequence
            && evidence.ContractStateGeneration == fact.ContractStateGeneration
            && evidence.DeliveryEpoch == fact.DeliveryEpoch
            && evidence.PreviousState == fact.PreviousState
            && evidence.ResultingState == fact.CurrentState
            && evidence.CommitPosition.Equals(fact.CommitPosition)
            && mutation.After?.Metadata.Revision == tombstone.PrivateRevision
            && mutation.After.Metadata.UpdatedAt == tombstone.TombstonedAt;
    }

    private static BaseOwnedSubjectLifecycleFact OwnedFact(BaseGeneratedSubjectRegistration registration,
        BaseSubjectLifecycleCommitEvidence value) => new()
    {
        CommitPosition = value.CommitPosition,
        ContractId = value.ContractId,
        ContractVersion = value.ContractVersion,
        ContractChecksum = registration.Checksum,
        Scope = value.Scope with { Value = value.Scope.Value is null ? null : new string(value.Scope.Value.AsSpan()) },
        SubjectId = BaseSubjectId.Create(value.SubjectId, registration.Definition.SubjectIdKind,
            registration.Definition.MaximumSubjectIdUtf8Bytes),
        AuthorityEpoch = value.AuthorityEpoch,
        Incarnation = value.Incarnation,
        SubjectSequence = value.SubjectSequence,
        ContractStateGeneration = value.ContractStateGeneration,
        DeliveryEpoch = value.DeliveryEpoch,
        Kind = BaseSubjectLifecycleFactKind.Transitioned,
        PreviousState = value.PreviousState,
        CurrentState = value.ResultingState,
    };

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
