using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed class DefaultBaseSubjectLifecycleRuntime(
    IRecordStoreRegistry stores,
    BaseSubjectContractRegistry contracts,
    IBasePolicyOrchestrator policy,
    BaseOpaqueTokenProtector tokens,
    TimeProvider timeProvider,
    Microsoft.Extensions.Options.IOptions<HPDBaseSubjectLifecycleOptions> options,
    BaseSubjectLifecycleRuntimeLimits runtimeLimits,
    BaseSubjectLifecycleOperationalState operationalState) : IBaseSubjectLifecycleRuntime, IAsyncDisposable, IDisposable
{
    private const string FeedReadGrant = "base.subjectLifecycle.feed.read";
    private const string FeedCheckpointGrant = "base.subjectLifecycle.feed.checkpoint";
    private const string ReconciliationReadGrant = "base.subjectLifecycle.reconcile.read";
    private readonly BaseSubjectLifecycleTokenCodec _tokenCodec = new(tokens, timeProvider);
    private readonly BaseSubjectScopeProtector _scopes = new(tokens);
    private readonly TimeSpan _cursorLifetime = options.Value.CursorLifetime;
    private readonly TimeSpan _shutdownDrainTimeout = runtimeLimits.ShutdownDrainTimeout;
    private readonly SemaphoreSlim _providerReadSlots = new(
        runtimeLimits.MaximumActiveAndQuarantinedReads,
        runtimeLimits.MaximumActiveAndQuarantinedReads);

    public async ValueTask<bool> AuthorizeGenerationAsync(BaseSession session, BaseInstalledSubjectLifecycleConsumer installed, CancellationToken cancellationToken)
    {
        BaseSubjectLifecycleConsumerDefinition consumer = installed.Definition;
        BaseGeneratedSubjectRegistration? contract = contracts.Find(consumer.ContractId, consumer.ContractVersion);
        if (contract is null || !AudienceAllows(consumer.Audience, session.Principal.AuthenticationState)) return false;
        OperationContext operation = session.Operation(BaseOperationKind.SubjectLifecycleRead, consumer.Id);
        if (contract.Definition.Scope == BaseSubjectScopeKind.Tenant && string.IsNullOrEmpty(operation.TenantId)
            || contract.Definition.Scope == BaseSubjectScopeKind.Project && string.IsNullOrEmpty(operation.ProjectId)) return false;
        var resource = new CollectionDefinition { Id = consumer.Id, Name = consumer.Id, Kind = BaseCollectionKinds.Custom, SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, System = true, SystemOwnerModuleId = consumer.OwningModuleId };
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = session.Principal, Operation = operation, Collection = resource, ResourceKind = PolicyResourceKind.SubjectLifecycle }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(authorization, consumer.DeliveryGrantId, consumer.OwningModuleId,
            consumer.Id, consumer.ContractId, consumer.ContractVersion, session.Principal, operation)) return false;
        OperationContext fixedOperation = session.Operation(BaseOperationKind.SubjectLifecycleRead, FeedReadGrant);
        OperationResult<BasePolicyEvaluation> fixedAuthorization = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = session.Principal, Operation = fixedOperation, Collection = resource, ResourceKind = PolicyResourceKind.SubjectLifecycle }, cancellationToken).ConfigureAwait(false);
        return BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(fixedAuthorization, FeedReadGrant, consumer.OwningModuleId,
            FeedReadGrant, consumer.ContractId, consumer.ContractVersion, session.Principal, fixedOperation);
    }

    public async ValueTask<bool> AuthorizeReconciliationGenerationAsync(BaseSession session, BaseInstalledSubjectLifecycleConsumer installed, CancellationToken cancellationToken)
    {
        BaseSubjectLifecycleConsumerDefinition consumer = installed.Definition;
        if (consumer.ReconciliationGrantId is null) return false;
        BaseGeneratedSubjectRegistration? contract = contracts.Find(consumer.ContractId, consumer.ContractVersion);
        if (contract is null || !AudienceAllows(consumer.Audience, session.Principal.AuthenticationState)) return false;
        OperationContext operation = session.Operation(BaseOperationKind.SubjectLifecycleReconcile, consumer.Id);
        if (contract.Definition.Scope == BaseSubjectScopeKind.Tenant && string.IsNullOrEmpty(operation.TenantId)
            || contract.Definition.Scope == BaseSubjectScopeKind.Project && string.IsNullOrEmpty(operation.ProjectId)) return false;
        var resource = new CollectionDefinition { Id=consumer.Id,Name=consumer.Id,Kind=BaseCollectionKinds.Custom,SchemaMode=SchemaMode.Strict,UnknownFields=UnknownFieldPolicy.Reject,System=true,SystemOwnerModuleId=consumer.OwningModuleId };
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal=session.Principal,Operation=operation,Collection=resource,ResourceKind=PolicyResourceKind.SubjectLifecycle }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(authorization,consumer.ReconciliationGrantId,consumer.OwningModuleId,consumer.Id,consumer.ContractId,consumer.ContractVersion,session.Principal,operation)) return false;
        OperationContext fixedOperation=session.Operation(BaseOperationKind.SubjectLifecycleReconcile,ReconciliationReadGrant);
        OperationResult<BasePolicyEvaluation> fixedAuthorization=await policy.EvaluateReadAsync(new BasePolicyRequest{Principal=session.Principal,Operation=fixedOperation,Collection=resource,ResourceKind=PolicyResourceKind.SubjectLifecycle},cancellationToken).ConfigureAwait(false);
        return BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(fixedAuthorization,ReconciliationReadGrant,consumer.OwningModuleId,ReconciliationReadGrant,consumer.ContractId,consumer.ContractVersion,session.Principal,fixedOperation);
    }

    public async ValueTask<BaseResult<BaseSubjectLifecycleCheckpoint>> CreateHintCheckpointAsync(
        BaseSession session,
        BaseInstalledSubjectLifecycleConsumer installed,
        BaseSubjectLifecycleCommitEvidence evidence,
        CancellationToken cancellationToken)
    {
        BaseSubjectLifecycleConsumerDefinition consumer = installed.Definition;
        BaseGeneratedSubjectRegistration? contract = contracts.Find(consumer.ContractId, consumer.ContractVersion);
        if (contract is null || !AudienceAllows(consumer.Audience, session.Principal.AuthenticationState)
            || evidence.ContractId != consumer.ContractId || evidence.ContractVersion != consumer.ContractVersion
            || !consumer.ObservedStates.Contains(evidence.ResultingState))
            return HintFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        OperationContext operation = session.Operation(BaseOperationKind.SubjectLifecycleRead, consumer.Id);
        BaseOwnedSubjectScopeEvidence expectedScope = new() { Kind = contract.Definition.Scope, Value = contract.Definition.Scope switch { BaseSubjectScopeKind.Global => null, BaseSubjectScopeKind.Tenant => operation.TenantId, BaseSubjectScopeKind.Project => operation.ProjectId, _ => null } };
        if (expectedScope.Kind != evidence.Scope.Kind || expectedScope.Value != evidence.Scope.Value)
            return HintFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        var resource = new CollectionDefinition { Id = consumer.Id, Name = consumer.Id, Kind = BaseCollectionKinds.Custom, SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, System = true, SystemOwnerModuleId = consumer.OwningModuleId };
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = session.Principal, Operation = operation, Collection = resource, ResourceKind = PolicyResourceKind.SubjectLifecycle }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(authorization, consumer.DeliveryGrantId, consumer.OwningModuleId,
            consumer.Id, consumer.ContractId, consumer.ContractVersion, session.Principal, operation))
            return HintFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        OperationContext fixedOperation = session.Operation(BaseOperationKind.SubjectLifecycleRead, FeedReadGrant);
        OperationResult<BasePolicyEvaluation> fixedAuthorization = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = session.Principal, Operation = fixedOperation, Collection = resource, ResourceKind = PolicyResourceKind.SubjectLifecycle }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(fixedAuthorization, FeedReadGrant, consumer.OwningModuleId,
            FeedReadGrant, consumer.ContractId, consumer.ContractVersion, session.Principal, fixedOperation))
            return HintFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        if (stores.GetStoreForCollection(contract.Definition.ValidationPlan.PrivateCollectionId) is not IBaseSubjectLifecycleStore store)
            return HintFailure(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Capability);
        OperationResult<BaseSubjectLifecycleProviderInspection> inspection;
        try { inspection = await InvokeProviderReadAsync(token => store.InspectAsync(new BaseSubjectLifecycleProviderInspectionRequest
        {
            ContractId = consumer.ContractId, ContractVersion = consumer.ContractVersion, ConsumerId = consumer.Id,
            ScopeAuthority = new BaseSubjectScopeQueryAuthority { Mode = BaseSubjectScopeQueryMode.ExactScope, ExactScope = expectedScope, InstalledAuthorityDigest = installed.Checksum },
            IncludeTerminalReceipt = false, MaximumResultBytes = 4096,
            DeadlineUtc = timeProvider.GetUtcNow().Add(consumer.Limits.ReadTimeout),
        }, token), consumer.Limits.ReadTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return HintFailure(OperationStatus.StoreError, BaseSubjectErrorCodes.Timeout, ErrorCategory.Store); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return HintFailure(OperationStatus.StoreError, BaseSubjectErrorCodes.Timeout, ErrorCategory.Store); }
        catch (Exception) { return HintFailure(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Capability); }
        BaseSubjectLifecycleConsumerInspection? projection = inspection.Value?.Consumers.SingleOrDefault(value => value.ConsumerId == consumer.Id && value.ConsumerVersion == consumer.Version);
        if (!inspection.IsSuccess() || inspection.Value is null || projection is null || projection.Overtaken
            || projection.ProjectionGeneration < 1 || inspection.Value.DeliveryEpoch < evidence.DeliveryEpoch)
            return HintFailure(inspection.Status, inspection.Error?.Code ?? BaseSubjectErrorCodes.LifecycleProviderContractInvalid, inspection.Error?.Category ?? ErrorCategory.Store);
        DateTimeOffset issued = timeProvider.GetUtcNow(); DateTimeOffset expires = checked(issued + _cursorLifetime);
        var boundary = new BaseSubjectLifecycleOrderingBoundary
        {
            CommitPosition = evidence.CommitPosition,
            SubjectId = BaseSubjectId.Create(evidence.SubjectId, contract.Definition.SubjectIdKind, contract.Definition.MaximumSubjectIdUtf8Bytes),
            AuthorityEpoch = evidence.AuthorityEpoch, Incarnation = evidence.Incarnation, SubjectSequence = evidence.SubjectSequence,
        };
        byte[] binding = BaseSubjectLifecycleTokenCodec.Binding(session.ApplicationId, consumer, installed.Checksum, contract.Checksum, expectedScope);
        BaseSubjectLifecycleCheckpoint checkpoint = _tokenCodec.ProtectCheckpoint(new(
            inspection.Value.StoreInstanceId, inspection.Value.RestoreEpoch, inspection.Value.DeliveryEpoch,
            projection.ProjectionGeneration, projection.CheckpointGeneration, boundary, issued, expires), binding);
        return new BaseSuccess<BaseSubjectLifecycleCheckpoint>(checkpoint, OperationStatus.Ok, null, null, null, null);
    }

    public async ValueTask<BaseResult<BaseSubjectLifecycleCheckpointResult>> AdvanceAsync<TSubject>(BaseSession session, BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> identity, BaseInstalledSubjectLifecycleConsumer installed, BaseSubjectLifecycleCheckpoint checkpoint, BaseMutationRequestIdentity requestIdentity, CancellationToken cancellationToken)
        => await AdvanceUntypedAsync(session, installed, checkpoint, requestIdentity, cancellationToken).ConfigureAwait(false);

    public async ValueTask<BaseResult<BaseSubjectLifecycleCheckpointResult>> AdvanceUntypedAsync(BaseSession session, BaseInstalledSubjectLifecycleConsumer installed, BaseSubjectLifecycleCheckpoint checkpoint, BaseMutationRequestIdentity requestIdentity, CancellationToken cancellationToken)
    {
        BaseSubjectLifecycleConsumerDefinition consumer = installed.Definition; BaseGeneratedSubjectRegistration? contract = contracts.Find(consumer.ContractId, consumer.ContractVersion);
        if (contract is null || !AudienceAllows(consumer.Audience, session.Principal.AuthenticationState)) return CheckpointFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        OperationContext operation = session.Operation(BaseOperationKind.SubjectLifecycleCheckpoint, consumer.Id);
        var resource = new CollectionDefinition { Id = consumer.Id, Name = consumer.Id, Kind = BaseCollectionKinds.Custom, SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, System = true, SystemOwnerModuleId = consumer.OwningModuleId };
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateWriteAsync(new BasePolicyRequest { Principal = session.Principal, Operation = operation, Collection = resource, ResourceKind = PolicyResourceKind.SubjectLifecycle }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(authorization, consumer.DeliveryGrantId, consumer.OwningModuleId,
            consumer.Id, consumer.ContractId, consumer.ContractVersion, session.Principal, operation))
            return CheckpointFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        OperationContext fixedOperation = session.Operation(BaseOperationKind.SubjectLifecycleCheckpoint, FeedCheckpointGrant);
        OperationResult<BasePolicyEvaluation> fixedAuthorization = await policy.EvaluateWriteAsync(new BasePolicyRequest { Principal = session.Principal, Operation = fixedOperation, Collection = resource, ResourceKind = PolicyResourceKind.SubjectLifecycle }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(fixedAuthorization, FeedCheckpointGrant, consumer.OwningModuleId,
            FeedCheckpointGrant, consumer.ContractId, consumer.ContractVersion, session.Principal, fixedOperation))
            return CheckpointFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        BaseOwnedSubjectScopeEvidence scope = new() { Kind = contract.Definition.Scope, Value = contract.Definition.Scope switch { BaseSubjectScopeKind.Global => null, BaseSubjectScopeKind.Tenant => operation.TenantId, BaseSubjectScopeKind.Project => operation.ProjectId, _ => null } };
        byte[] binding = BaseSubjectLifecycleTokenCodec.Binding(session.ApplicationId, consumer, installed.Checksum, contract.Checksum, scope);
        BaseSubjectLifecycleTokenReadStatus checkpointStatus = _tokenCodec.ReadCheckpoint(checkpoint, binding, contract.Definition.SubjectIdKind, out BaseSubjectLifecycleTokenPayload? token);
        if (checkpointStatus != BaseSubjectLifecycleTokenReadStatus.Valid || token is null)
            return CheckpointFailure(checkpointStatus == BaseSubjectLifecycleTokenReadStatus.Expired ? OperationStatus.Conflict : OperationStatus.ValidationFailed,
                checkpointStatus == BaseSubjectLifecycleTokenReadStatus.Expired ? BaseSubjectErrorCodes.CursorExpired : BaseSubjectErrorCodes.CursorInvalid,
                checkpointStatus == BaseSubjectLifecycleTokenReadStatus.Expired ? ErrorCategory.Conflict : ErrorCategory.Validation);
        if (token.Boundary is null || !ExactAdvanceIdentity(requestIdentity, consumer, installed.Checksum, token.Boundary))
            return CheckpointFailure(OperationStatus.ValidationFailed, BaseSubjectErrorCodes.LifecycleContractInvalid, ErrorCategory.Validation);
        if (stores.GetStoreForCollection(contract.Definition.ValidationPlan.PrivateCollectionId) is not IBaseSubjectLifecycleStore store) return CheckpointFailure(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Capability);
        var checkpointRequest = new BaseSubjectLifecycleProviderCheckpointRequest { ApplicationId = session.ApplicationId, ContractId = consumer.ContractId, ContractVersion = consumer.ContractVersion, ConsumerId = consumer.Id, ConsumerVersion = consumer.Version, ConsumerChecksum = installed.Checksum, ProjectionGeneration = token.ProjectionGeneration, Scope = scope, Through = token.Boundary, ExpectedCheckpointGeneration = token.CheckpointGeneration, Identity = requestIdentity, DeadlineUtc = timeProvider.GetUtcNow().Add(consumer.Limits.ReadTimeout) };
        var processor = new BaseSubjectLifecycleCheckpointProcessor(checkpointRequest);
        RecordMutationExecutionResult execution = await store.AdvanceCheckpointAsync(processor, new RecordMutationExecutionRequest
        {
            AcquisitionTimeout = consumer.Limits.ReadTimeout,
            TransactionTimeout = consumer.Limits.ReadTimeout,
            CommitCompletionTimeout = consumer.Limits.ReadTimeout,
            AtomicRequest = new BaseAtomicMutationExecutionRequest
            {
                Identity = requestIdentity,
                StructuralDigest = CheckpointStructuralDigest(checkpointRequest),
                ExpiresAt = timeProvider.GetUtcNow().AddDays(30),
                MaxReceiptBytes = 4096,
            },
        }, cancellationToken).ConfigureAwait(false);
        if (execution.Outcome == RecordMutationExecutionOutcome.Committed && processor.Result is not null)
            return new BaseSuccess<BaseSubjectLifecycleCheckpointResult>(processor.Result,
                processor.Result.Duplicate ? OperationStatus.Ok : OperationStatus.Updated, null, null, null, null);
        BaseError? inherited = execution.Error ?? execution.Processing?.Error;
        if (inherited is not null && !inherited.Code.StartsWith("base.subjectLifecycle.", StringComparison.Ordinal))
            return new BaseFailure<BaseSubjectLifecycleCheckpointResult>(
                execution.Outcome == RecordMutationExecutionOutcome.Indeterminate ? OperationStatus.StoreError : OperationStatus.Conflict,
                inherited with { }, null, null);
        return CheckpointFailure(execution.Outcome == RecordMutationExecutionOutcome.Indeterminate ? OperationStatus.StoreError : OperationStatus.Conflict,
            inherited?.Code ?? (execution.Outcome == RecordMutationExecutionOutcome.Indeterminate ? BaseSubjectErrorCodes.LifecycleCommitIndeterminate : BaseSubjectErrorCodes.LifecycleProviderContractInvalid),
            inherited?.Category ?? ErrorCategory.Store);
    }

    public async ValueTask<BaseResult<BaseSubjectLifecyclePage<TSubject>>> ReadAsync<TSubject>(BaseSession session, BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> identity, BaseInstalledSubjectLifecycleConsumer installed, BaseSubjectLifecycleCursor? after, int? take, CancellationToken cancellationToken)
    {
        BaseResult<BaseUntypedSubjectLifecyclePage> result = await ReadUntypedAsync(session, installed, after, take, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseUntypedSubjectLifecyclePage> failure)
            return Failure<TSubject>(failure.Status, failure.Error.Code, failure.Error.Category);
        BaseUntypedSubjectLifecyclePage page = ((BaseSuccess<BaseUntypedSubjectLifecyclePage>)result).Value;
        ImmutableArray<BaseSubjectLifecycleFact<TSubject>> typed = [.. page.Facts.Select(value => new BaseSubjectLifecycleFact<TSubject>
        {
            Subject = new(value.SubjectId, value.AuthorityEpoch, value.Incarnation),
            Fact = value with { },
        })];
        return new BaseSuccess<BaseSubjectLifecyclePage<TSubject>>(new() { Facts = typed, Next = page.Next, Through = page.Through }, OperationStatus.Ok, null, null, null, null);
    }

    public async ValueTask<BaseResult<BaseUntypedSubjectLifecyclePage>> ReadUntypedAsync(BaseSession session, BaseInstalledSubjectLifecycleConsumer installed, BaseSubjectLifecycleCursor? after, int? take, CancellationToken cancellationToken)
    {
        BaseSubjectLifecycleConsumerDefinition consumer = installed.Definition;
        BaseGeneratedSubjectRegistration? contract = contracts.Find(consumer.ContractId, consumer.ContractVersion);
        if (contract is null || !AudienceAllows(consumer.Audience, session.Principal.AuthenticationState)) return UntypedFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        OperationContext operation = session.Operation(BaseOperationKind.SubjectLifecycleRead, consumer.Id);
        var resource = new CollectionDefinition { Id = consumer.Id, Name = consumer.Id, Kind = BaseCollectionKinds.Custom, SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, System = true, SystemOwnerModuleId = consumer.OwningModuleId };
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = session.Principal, Operation = operation, Collection = resource, ResourceKind = PolicyResourceKind.SubjectLifecycle }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(authorization, consumer.DeliveryGrantId, consumer.OwningModuleId,
            consumer.Id, consumer.ContractId, consumer.ContractVersion, session.Principal, operation))
            return UntypedFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        OperationContext fixedOperation = session.Operation(BaseOperationKind.SubjectLifecycleRead, FeedReadGrant);
        OperationResult<BasePolicyEvaluation> fixedAuthorization = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = session.Principal, Operation = fixedOperation, Collection = resource, ResourceKind = PolicyResourceKind.SubjectLifecycle }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(fixedAuthorization, FeedReadGrant, consumer.OwningModuleId,
            FeedReadGrant, consumer.ContractId, consumer.ContractVersion, session.Principal, fixedOperation))
            return UntypedFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        BaseOwnedSubjectScopeEvidence scope = new() { Kind = contract.Definition.Scope, Value = contract.Definition.Scope switch { BaseSubjectScopeKind.Global => null, BaseSubjectScopeKind.Tenant => operation.TenantId, BaseSubjectScopeKind.Project => operation.ProjectId, _ => null } };
        if (scope.Kind != BaseSubjectScopeKind.Global && string.IsNullOrEmpty(scope.Value)) return UntypedFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        byte[] binding = BaseSubjectLifecycleTokenCodec.Binding(session.ApplicationId, consumer, installed.Checksum, contract.Checksum, scope);
        BaseSubjectLifecycleTokenPayload? decoded = null;
        if (after is not null)
        {
            BaseSubjectLifecycleTokenReadStatus cursorStatus = _tokenCodec.ReadCursor(after, binding, contract.Definition.SubjectIdKind, out decoded);
            if (cursorStatus != BaseSubjectLifecycleTokenReadStatus.Valid)
                return UntypedFailure(cursorStatus == BaseSubjectLifecycleTokenReadStatus.Expired ? OperationStatus.Conflict : OperationStatus.ValidationFailed,
                    cursorStatus == BaseSubjectLifecycleTokenReadStatus.Expired ? BaseSubjectErrorCodes.CursorExpired : BaseSubjectErrorCodes.CursorInvalid,
                    cursorStatus == BaseSubjectLifecycleTokenReadStatus.Expired ? ErrorCategory.Conflict : ErrorCategory.Validation);
        }
        IRecordStore? recordStore = stores.GetStoreForCollection(contract.Definition.ValidationPlan.PrivateCollectionId);
        if (recordStore is not IBaseSubjectLifecycleStore lifecycleStore) return UntypedFailure(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Capability);
        OperationResult<BaseSubjectLifecycleProviderInspection> inspection;
        try { inspection = await InvokeProviderReadAsync(token => lifecycleStore.InspectAsync(new BaseSubjectLifecycleProviderInspectionRequest
        {
            ContractId = consumer.ContractId,
            ContractVersion = consumer.ContractVersion,
            ConsumerId = consumer.Id,
            ScopeAuthority = new BaseSubjectScopeQueryAuthority
            {
                Mode = BaseSubjectScopeQueryMode.ExactScope,
                ExactScope = scope,
                InstalledAuthorityDigest = installed.Checksum,
            },
            IncludeTerminalReceipt = false,
            MaximumResultBytes = 4096,
            DeadlineUtc = timeProvider.GetUtcNow().Add(consumer.Limits.ReadTimeout),
        }, token), consumer.Limits.ReadTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return UntypedFailure(OperationStatus.StoreError, BaseSubjectErrorCodes.Timeout, ErrorCategory.Store); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return UntypedFailure(OperationStatus.StoreError, BaseSubjectErrorCodes.Timeout, ErrorCategory.Store); }
        catch (Exception) { return UntypedFailure(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Capability); }
        BaseSubjectLifecycleConsumerInspection? installedProjection = inspection.IsSuccess() && inspection.Value is not null
            ? inspection.Value.Consumers.SingleOrDefault(value => value.ConsumerId == consumer.Id && value.ConsumerVersion == consumer.Version)
            : null;
        if (installedProjection is null || installedProjection.ProjectionGeneration < 1)
            return UntypedFailure(inspection.IsSuccess() ? OperationStatus.CapabilityUnavailable : inspection.Status,
                inspection.Error?.Code ?? BaseSubjectErrorCodes.LifecycleProviderContractInvalid,
                inspection.Error?.Category ?? ErrorCategory.Capability);
        int pageSize = Math.Min(take ?? consumer.Limits.MaximumFactsPerPage, consumer.Limits.MaximumFactsPerPage);
        if (pageSize < 1) return UntypedFailure(OperationStatus.ValidationFailed, BaseSubjectErrorCodes.LifecycleContractInvalid, ErrorCategory.Validation);
        var providerRequest = new BaseSubjectLifecycleProviderReadRequest { ApplicationId = session.ApplicationId, ContractId = consumer.ContractId, ContractVersion = consumer.ContractVersion, ContractChecksum = contract.Checksum, ConsumerId = consumer.Id, ConsumerVersion = consumer.Version, ConsumerChecksum = installed.Checksum, ProjectionGeneration = installedProjection.ProjectionGeneration, Scope = scope, After = decoded?.Boundary, Take = pageSize, MaximumResultBytes = consumer.Limits.MaximumResultBytes, DeadlineUtc = timeProvider.GetUtcNow().Add(consumer.Limits.ReadTimeout) };
        OperationResult<BaseSubjectLifecycleProviderPage> read;
        try { read = await InvokeProviderReadAsync(token => lifecycleStore.ReadAsync(providerRequest, token), consumer.Limits.ReadTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return UntypedFailure(OperationStatus.StoreError, BaseSubjectErrorCodes.Timeout, ErrorCategory.Store); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return UntypedFailure(OperationStatus.StoreError, BaseSubjectErrorCodes.Timeout, ErrorCategory.Store); }
        catch (Exception) { return UntypedFailure(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Capability); }
        if (!read.IsSuccess() || read.Value is null) return UntypedFailure(read.Status, read.Error?.Code ?? BaseSubjectErrorCodes.LifecycleProviderContractInvalid, read.Error?.Category ?? ErrorCategory.Store);
        BaseSubjectLifecycleProviderPage page = read.Value;
        if (decoded is not null && (!string.Equals(decoded.StoreInstanceId, page.StoreInstanceId, StringComparison.Ordinal) || decoded.RestoreEpoch != page.RestoreEpoch || decoded.DeliveryEpoch != page.DeliveryEpoch || decoded.ProjectionGeneration != page.ProjectionGeneration))
            return UntypedFailure(OperationStatus.Conflict, BaseSubjectErrorCodes.CursorOvertaken, ErrorCategory.Conflict);
        if (!Validate(page, providerRequest, consumer, installed.Checksum, contract, scope, decoded?.Boundary, pageSize)) return UntypedFailure(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Capability);
        DateTimeOffset issued = timeProvider.GetUtcNow(); DateTimeOffset expires = checked(issued + _cursorLifetime);
        BaseSubjectLifecycleCheckpoint checkpoint = _tokenCodec.ProtectCheckpoint(new(page.StoreInstanceId, page.RestoreEpoch, page.DeliveryEpoch, page.ProjectionGeneration, page.CheckpointGeneration, page.Through, issued, expires), binding);
        BaseSubjectLifecycleCursor? next = page.Through is null || page.HighWater is not null && Compare(page.Through, page.HighWater) >= 0
            ? null
            : _tokenCodec.ProtectCursor(new(page.StoreInstanceId, page.RestoreEpoch, page.DeliveryEpoch, page.ProjectionGeneration, page.CheckpointGeneration, page.Through, issued, expires), binding);
        ImmutableArray<BaseSubjectLifecycleFact> facts = [.. page.Facts.Select(static value => value.Fact with { })];
        return new BaseSuccess<BaseUntypedSubjectLifecyclePage>(new() { Facts = facts, Next = next, Through = checkpoint }, OperationStatus.Ok, null, null, null, null);
    }

    public async ValueTask<BaseResult<BaseSubjectLifecycleReconciliationPage<TSubject>>> ReconcileAsync<TSubject>(BaseSession session, BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> identity, BaseInstalledSubjectLifecycleConsumer installed, BaseSubjectId? afterSubjectId, int? take, CancellationToken cancellationToken)
    {
        BaseResult<BaseSubjectLifecycleProviderReconciliationPage> result = await ReconcileUntypedAsync(session, installed, afterSubjectId, take, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseSubjectLifecycleProviderReconciliationPage> failure)
            return ReconciliationFailure<TSubject>(failure.Status, failure.Error.Code, failure.Error.Category);
        BaseSubjectLifecycleProviderReconciliationPage page = ((BaseSuccess<BaseSubjectLifecycleProviderReconciliationPage>)result).Value;
        return new BaseSuccess<BaseSubjectLifecycleReconciliationPage<TSubject>>(new()
        {
            Subjects = [.. page.Subjects.Select(value => new BaseCurrentSubjectLifecycle<TSubject>
            {
                SubjectId=value.SubjectId,AuthorityEpoch=value.AuthorityEpoch,Incarnation=value.Incarnation,State=value.State,SubjectSequence=value.SubjectSequence,
            })],
            NextSubjectId = page.NextSubjectId,
            CapturedHighWater = page.CapturedHighWater,
        }, OperationStatus.Ok, null, null, null, null);
    }

    public async ValueTask<BaseResult<BaseSubjectLifecycleProviderReconciliationPage>> ReconcileUntypedAsync(BaseSession session, BaseInstalledSubjectLifecycleConsumer installed, BaseSubjectId? afterSubjectId, int? take, CancellationToken cancellationToken)
    {
        BaseSubjectLifecycleConsumerDefinition consumer = installed.Definition;
        if (consumer.ReconciliationGrantId is null) return ReconciliationFailure(OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleReconciliationUnavailable, ErrorCategory.Capability);
        BaseGeneratedSubjectRegistration? contract = contracts.Find(consumer.ContractId, consumer.ContractVersion);
        if (contract is null || !AudienceAllows(consumer.Audience, session.Principal.AuthenticationState)) return ReconciliationFailure(OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized, ErrorCategory.Authorization);
        OperationContext operation = session.Operation(BaseOperationKind.SubjectLifecycleReconcile, consumer.Id);
        var resource = new CollectionDefinition { Id=consumer.Id,Name=consumer.Id,Kind=BaseCollectionKinds.Custom,SchemaMode=SchemaMode.Strict,UnknownFields=UnknownFieldPolicy.Reject,System=true,SystemOwnerModuleId=consumer.OwningModuleId };
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal=session.Principal,Operation=operation,Collection=resource,ResourceKind=PolicyResourceKind.SubjectLifecycle }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(authorization,consumer.ReconciliationGrantId,consumer.OwningModuleId,consumer.Id,consumer.ContractId,consumer.ContractVersion,session.Principal,operation)) return ReconciliationFailure(OperationStatus.PolicyDenied,BaseSubjectErrorCodes.LifecycleUnauthorized,ErrorCategory.Authorization);
        OperationContext fixedOperation=session.Operation(BaseOperationKind.SubjectLifecycleReconcile,ReconciliationReadGrant);
        OperationResult<BasePolicyEvaluation> fixedAuthorization=await policy.EvaluateReadAsync(new BasePolicyRequest{Principal=session.Principal,Operation=fixedOperation,Collection=resource,ResourceKind=PolicyResourceKind.SubjectLifecycle},cancellationToken).ConfigureAwait(false);
        if(!BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(fixedAuthorization,ReconciliationReadGrant,consumer.OwningModuleId,ReconciliationReadGrant,consumer.ContractId,consumer.ContractVersion,session.Principal,fixedOperation))return ReconciliationFailure(OperationStatus.PolicyDenied,BaseSubjectErrorCodes.LifecycleUnauthorized,ErrorCategory.Authorization);
        BaseOwnedSubjectScopeEvidence scope=new(){Kind=contract.Definition.Scope,Value=contract.Definition.Scope switch{BaseSubjectScopeKind.Global=>null,BaseSubjectScopeKind.Tenant=>operation.TenantId,BaseSubjectScopeKind.Project=>operation.ProjectId,_=>null}};
        if(scope.Kind!=BaseSubjectScopeKind.Global&&string.IsNullOrEmpty(scope.Value))return ReconciliationFailure(OperationStatus.PolicyDenied,BaseSubjectErrorCodes.LifecycleUnauthorized,ErrorCategory.Authorization);
        if(stores.GetStoreForCollection(contract.Definition.ValidationPlan.PrivateCollectionId) is not IBaseSubjectLifecycleStore store)return ReconciliationFailure(OperationStatus.CapabilityUnavailable,BaseSubjectErrorCodes.LifecycleProviderContractInvalid,ErrorCategory.Capability);
        OperationResult<BaseSubjectLifecycleProviderInspection> inspection;
        try{inspection=await InvokeProviderReadAsync(token=>store.InspectAsync(new(){ContractId=consumer.ContractId,ContractVersion=consumer.ContractVersion,ConsumerId=consumer.Id,ScopeAuthority=new(){Mode=BaseSubjectScopeQueryMode.ExactScope,ExactScope=scope,InstalledAuthorityDigest=installed.Checksum},IncludeTerminalReceipt=false,MaximumResultBytes=4096,DeadlineUtc=timeProvider.GetUtcNow().Add(consumer.Limits.ReadTimeout)},token),consumer.Limits.ReadTimeout,cancellationToken).ConfigureAwait(false);}catch(TimeoutException){return ReconciliationFailure(OperationStatus.StoreError,BaseSubjectErrorCodes.Timeout,ErrorCategory.Store);}catch(Exception){return ReconciliationFailure(OperationStatus.CapabilityUnavailable,BaseSubjectErrorCodes.LifecycleProviderContractInvalid,ErrorCategory.Capability);}
        BaseSubjectLifecycleConsumerInspection? projection=inspection.IsSuccess()&&inspection.Value is not null?inspection.Value.Consumers.SingleOrDefault(value=>value.ConsumerId==consumer.Id&&value.ConsumerVersion==consumer.Version):null;
        if(projection is null)return ReconciliationFailure(OperationStatus.CapabilityUnavailable,BaseSubjectErrorCodes.LifecycleProviderContractInvalid,ErrorCategory.Capability);
        int pageSize=Math.Min(take??consumer.Limits.MaximumFactsPerPage,consumer.Limits.MaximumFactsPerPage);if(pageSize<1)return ReconciliationFailure(OperationStatus.ValidationFailed,BaseSubjectErrorCodes.LifecycleContractInvalid,ErrorCategory.Validation);
        var request=new BaseSubjectLifecycleProviderReconciliationRequest{ApplicationId=session.ApplicationId,ContractId=consumer.ContractId,ContractVersion=consumer.ContractVersion,ContractChecksum=contract.Checksum,ConsumerId=consumer.Id,ConsumerVersion=consumer.Version,ConsumerChecksum=installed.Checksum,ProjectionGeneration=projection.ProjectionGeneration,Scope=scope,AfterSubjectId=afterSubjectId,Take=pageSize,MaximumResultBytes=consumer.Limits.MaximumResultBytes,DeadlineUtc=timeProvider.GetUtcNow().Add(consumer.Limits.ReadTimeout)};
        OperationResult<BaseSubjectLifecycleProviderReconciliationPage> read;try{read=await InvokeProviderReadAsync(token=>store.ReconcileAsync(request,token),consumer.Limits.ReadTimeout,cancellationToken).ConfigureAwait(false);}catch(TimeoutException){return ReconciliationFailure(OperationStatus.StoreError,BaseSubjectErrorCodes.Timeout,ErrorCategory.Store);}catch(Exception){return ReconciliationFailure(OperationStatus.CapabilityUnavailable,BaseSubjectErrorCodes.LifecycleProviderContractInvalid,ErrorCategory.Capability);}
        if(!read.IsSuccess()||read.Value is null)return ReconciliationFailure(read.Status,read.Error?.Code??BaseSubjectErrorCodes.LifecycleProviderContractInvalid,read.Error?.Category??ErrorCategory.Store);
        BaseSubjectLifecycleProviderReconciliationPage page=read.Value;long bytes=0;BaseSubjectId? previous=null;try{foreach(BaseCurrentSubjectLifecycle value in page.Subjects){bytes=checked(bytes+96L+Encoding.UTF8.GetByteCount(value.SubjectId.Value));if(value.SubjectSequence<1||previous is not null&&string.CompareOrdinal(previous.Value.Value,value.SubjectId.Value)>=0||afterSubjectId is not null&&string.CompareOrdinal(afterSubjectId.Value.Value,value.SubjectId.Value)>=0)return ReconciliationFailure(OperationStatus.CapabilityUnavailable,BaseSubjectErrorCodes.LifecycleProviderContractInvalid,ErrorCategory.Capability);previous=value.SubjectId;}}catch(OverflowException){return ReconciliationFailure(OperationStatus.CapabilityUnavailable,BaseSubjectErrorCodes.LifecycleProviderContractInvalid,ErrorCategory.Capability);}
        if(page.Subjects.IsDefault||page.Subjects.Length>pageSize||page.ProjectionGeneration!=projection.ProjectionGeneration||!_scopes.Matches(page.Scope,scope)||page.Accounting.RowsHydrated!=page.Subjects.Length||page.Accounting.RowsSought<page.Accounting.RowsHydrated||page.Accounting.ResultBytes!=bytes||page.Accounting.ResultBytes>consumer.Limits.MaximumResultBytes||page.NextSubjectId is not null&&(page.Subjects.Length==0||!page.NextSubjectId.Value.Equals(page.Subjects[^1].SubjectId)))return ReconciliationFailure(OperationStatus.CapabilityUnavailable,BaseSubjectErrorCodes.LifecycleProviderContractInvalid,ErrorCategory.Capability);
        return new BaseSuccess<BaseSubjectLifecycleProviderReconciliationPage>(page,OperationStatus.Ok,null,null,null,null);
    }

    private bool Validate(
        BaseSubjectLifecycleProviderPage page,
        BaseSubjectLifecycleProviderReadRequest request,
        BaseSubjectLifecycleConsumerDefinition consumer,
        string checksum,
        BaseGeneratedSubjectRegistration contract,
        BaseOwnedSubjectScopeEvidence scope,
        BaseSubjectLifecycleOrderingBoundary? after,
        int take)
    {
        long expectedResultBytes;
        long expectedTransientBytes;
        try
        {
            expectedResultBytes = BaseSubjectCanonicalRetainedWork.MeasureLifecycleProviderFacts(page.Facts);
            expectedTransientBytes = checked(expectedResultBytes + BaseSubjectCanonicalRetainedWork.MeasureLifecycleIntervals(page.Intervals));
        }
        catch (OverflowException) { return false; }
        if (string.IsNullOrWhiteSpace(page.StoreInstanceId) || page.RestoreEpoch < 0 || page.DeliveryEpoch < 1
            || page.CheckpointGeneration < 0 || page.ProjectionGeneration < 1 || page.Facts.IsDefault
            || page.Facts.Length > take || !_scopes.Matches(page.Scope, scope)
            || page.Accounting.RowsSought < page.Accounting.RowsHydrated || page.Accounting.RowsHydrated != page.Facts.Length
            || page.Accounting.ResultBytes != expectedResultBytes || page.Accounting.ResultBytes > consumer.Limits.MaximumResultBytes
            || page.Accounting.TransientBytes != expectedTransientBytes
            || !BaseSubjectLifecycleReadIntervals.Matches(page.Intervals, request, page.Scope, page.Through)
            || page.EarliestRetained is not null && page.HighWater is not null && Compare(page.EarliestRetained, page.HighWater) > 0)
            return false;
        BaseSubjectLifecycleOrderingBoundary? previous = null;
        foreach (BaseSubjectLifecycleProviderFact item in page.Facts)
        {
            BaseSubjectLifecycleState? resultingState = item.Fact.Kind switch
            {
                BaseSubjectLifecycleFactKind.Created when item.Fact.Created is not null && item.Fact.Transitioned is null && item.Fact.Retired is null
                    && item.Fact.Created.CurrentState == BaseSubjectLifecycleState.Active => BaseSubjectLifecycleState.Active,
                BaseSubjectLifecycleFactKind.Transitioned when item.Fact.Created is null && item.Fact.Transitioned is not null && item.Fact.Retired is null
                    && ValidTransition(item.Fact.Transitioned.PreviousState, item.Fact.Transitioned.CurrentState) => item.Fact.Transitioned.CurrentState,
                BaseSubjectLifecycleFactKind.Retired when item.Fact.Created is null && item.Fact.Transitioned is null && item.Fact.Retired?.PreviousState == BaseSubjectLifecycleState.Tombstoned
                    => BaseSubjectLifecycleState.Retired,
                _ => null,
            };
            if (item.ConsumerId != consumer.Id || item.ConsumerVersion != consumer.Version || item.ConsumerChecksum != checksum
                || item.ProjectionGeneration != page.ProjectionGeneration || resultingState != item.MatchedObservedState
                || !consumer.ObservedStates.Contains(item.MatchedObservedState) || !_scopes.Matches(item.Scope, scope)
                || item.Fact.ContractId != contract.Definition.Id || item.Fact.ContractVersion != contract.Definition.Version
                || item.Fact.CommitPosition.Value < 1 || item.Fact.SubjectSequence < 1 || item.Fact.ContractStateGeneration < 1
                || item.Fact.DeliveryEpoch < 1 || item.Fact.DeliveryEpoch > page.DeliveryEpoch
                || !item.Boundary.CommitPosition.Equals(item.Fact.CommitPosition)
                || !item.Boundary.SubjectId.Equals(item.Fact.SubjectId) || !item.Boundary.AuthorityEpoch.Equals(item.Fact.AuthorityEpoch)
                || !item.Boundary.Incarnation.Equals(item.Fact.Incarnation) || item.Boundary.SubjectSequence != item.Fact.SubjectSequence
                || after is not null && Compare(after, item.Boundary) >= 0 || previous is not null && Compare(previous, item.Boundary) >= 0)
                return false;
            previous = item.Boundary;
        }
        return (page.Facts.Length == 0 && page.Through is null || page.Facts.Length != 0 && Equals(page.Through, page.Facts[^1].Boundary))
            && (page.Through is null || page.HighWater is not null && Compare(page.Through, page.HighWater) <= 0);
    }

    private static bool ValidTransition(BaseSubjectLifecycleState previous, BaseSubjectLifecycleState current) =>
        previous == BaseSubjectLifecycleState.Active && current is BaseSubjectLifecycleState.Inactive or BaseSubjectLifecycleState.Tombstoned
        || previous == BaseSubjectLifecycleState.Inactive && current is BaseSubjectLifecycleState.Active or BaseSubjectLifecycleState.Tombstoned;
    private static bool AudienceAllows(BaseSubjectLifecycleConsumerAudience audience, PrincipalAuthenticationState principal) =>
        audience == BaseSubjectLifecycleConsumerAudience.System
            ? principal == PrincipalAuthenticationState.System
            : principal is PrincipalAuthenticationState.Service or PrincipalAuthenticationState.System;

    private static int Compare(BaseSubjectLifecycleOrderingBoundary a, BaseSubjectLifecycleOrderingBoundary b)
    { int c = a.CommitPosition.Value.CompareTo(b.CommitPosition.Value); if (c != 0) return c; c = string.CompareOrdinal(a.SubjectId.Value, b.SubjectId.Value); if (c != 0) return c; c = a.AuthorityEpoch.ToArray().AsSpan().SequenceCompareTo(b.AuthorityEpoch.ToArray()); if (c != 0) return c; c = a.Incarnation.ToArray().AsSpan().SequenceCompareTo(b.Incarnation.ToArray()); return c != 0 ? c : a.SubjectSequence.CompareTo(b.SubjectSequence); }

    private async ValueTask<T> InvokeProviderReadAsync<T>(
        Func<CancellationToken, ValueTask<T>> invoke,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!await _providerReadSlots.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            throw new TimeoutException();
        var deadline = new CancellationTokenSource(timeout);
        bool release = true;
        Task<T>? work = null;
        operationalState.Enter();
        try
        {
            work = invoke(deadline.Token).AsTask();
            return await work.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch when (work is { IsCompleted: false })
        {
            release = false;
            operationalState.Quarantine();
            _ = ReleaseQuarantinedReadAsync(work, deadline);
            throw;
        }
        finally
        {
            if (release)
            {
                deadline.Dispose();
                operationalState.Complete();
                _providerReadSlots.Release();
            }
        }
    }

    private async Task ReleaseQuarantinedReadAsync<T>(Task<T> work, CancellationTokenSource deadline)
    {
        try { _ = await work.ConfigureAwait(false); }
        catch { }
        finally
        {
            deadline.Dispose();
            operationalState.ReleaseQuarantine();
            _providerReadSlots.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var drain = new CancellationTokenSource(_shutdownDrainTimeout);
        try
        {
            while (operationalState.Active + operationalState.Quarantined != 0)
                await Task.Delay(TimeSpan.FromMilliseconds(10), drain.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (drain.IsCancellationRequested) { }
        if (operationalState.Active + operationalState.Quarantined == 0)
            _providerReadSlots.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    private static byte[] CheckpointStructuralDigest(BaseSubjectLifecycleProviderCheckpointRequest request) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"base.subjectLifecycle.checkpoint.v1\0{request.ApplicationId}\0{request.ContractId}\0{request.ContractVersion}\0{request.ConsumerId}\0{request.ConsumerVersion}\0{request.ConsumerChecksum}\0{request.ProjectionGeneration}\0{(int)request.Scope.Kind}\0{request.Scope.Value}\0{request.Through?.CommitPosition.Value}\0{request.Through?.SubjectId.Value}\0{request.Through?.AuthorityEpoch.ToBase64Url()}\0{request.Through?.Incarnation.ToBase64Url()}\0{request.Through?.SubjectSequence}\0{request.ExpectedCheckpointGeneration}"));
    private static bool ExactAdvanceIdentity(BaseMutationRequestIdentity identity, BaseSubjectLifecycleConsumerDefinition consumer, string checksum, BaseSubjectLifecycleOrderingBoundary boundary)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"base.subjectLifecycle.delivery.advance.v1\0{checksum}\0{boundary.CommitPosition.Value}\0{boundary.SubjectId.Value}\0{boundary.AuthorityEpoch.ToBase64Url()}\0{boundary.Incarnation.ToBase64Url()}\0{boundary.SubjectSequence}"));
        return string.Equals(identity.Scope, $"subject-lifecycle:{consumer.Id}", StringComparison.Ordinal)
            && string.Equals(identity.Operation, "subjectLifecycle.advance", StringComparison.Ordinal)
            && string.Equals(identity.IdempotencyKey, Convert.ToHexStringLower(digest), StringComparison.Ordinal)
            && CryptographicOperations.FixedTimeEquals(identity.Fingerprint.ToArray(), digest);
    }
    private static BaseFailure<BaseSubjectLifecyclePage<TSubject>> Failure<TSubject>(OperationStatus status, string code, ErrorCategory category) => new(status, BaseSubjectFailureContract.Error(code), null, null);
    private static BaseFailure<BaseUntypedSubjectLifecyclePage> UntypedFailure(OperationStatus status, string code, ErrorCategory category) => new(status, BaseSubjectFailureContract.Error(code), null, null);
    private static BaseFailure<BaseSubjectLifecycleCheckpointResult> CheckpointFailure(OperationStatus status, string code, ErrorCategory category) => new(status, BaseSubjectFailureContract.Error(code), null, null);
    private static BaseFailure<BaseSubjectLifecycleProviderReconciliationPage> ReconciliationFailure(OperationStatus status,string code,ErrorCategory category)=>new(status,BaseSubjectFailureContract.Error(code),null,null);
    private static BaseFailure<BaseSubjectLifecycleReconciliationPage<TSubject>> ReconciliationFailure<TSubject>(OperationStatus status,string code,ErrorCategory category)=>new(status,BaseSubjectFailureContract.Error(code),null,null);
    private static BaseFailure<BaseSubjectLifecycleCheckpoint> HintFailure(OperationStatus status, string code, ErrorCategory category) => new(status, BaseSubjectFailureContract.Error(code), null, null);
}

internal sealed record BaseSubjectLifecycleRuntimeLimits(
    int MaximumActiveAndQuarantinedReads,
    TimeSpan ShutdownDrainTimeout)
{
    internal static BaseSubjectLifecycleRuntimeLimits Default { get; } = new(8, TimeSpan.FromSeconds(5));
}
