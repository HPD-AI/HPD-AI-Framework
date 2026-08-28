using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

internal sealed class DefaultBaseModuleMutationRuntime(
    IRecordStoreRegistry stores,
    BaseCollectionRegistry collections,
    BaseModuleMutationRegistry registry,
    IBaseSchemaValidator schemaValidator,
    IBasePolicyOrchestrator policy,
    IBaseResultNormalizer normalizer,
    BaseSubjectContractRegistry subjects,
    TimeProvider timeProvider,
    BaseSubjectLifecycleRegistry? lifecycleRegistry = null,
    BaseSubjectRetirementRegistry? retirementRegistry = null,
    BaseSemanticActivationRegistry? semanticRegistry = null,
    BaseActivationRegistry? activationRegistry = null,
    BaseActivationAcceptedTimeAuthority? acceptedTimeAuthority = null,
    BaseSemanticRecoveryAuthorityRegistry? semanticRecoveryRegistry = null,
    BaseSemanticActivationMigrationRegistry? semanticMigrationRegistry = null) : IBaseModuleMutationRuntime
{
    private readonly BaseSubjectLifecycleRegistry lifecycleConsumers = lifecycleRegistry ?? new([], subjects);
    private readonly BaseSubjectRetirementRegistry retirement = retirementRegistry ?? new([], [], lifecycleRegistry ?? new([], subjects));
    private readonly BaseActivationAcceptedTimeAuthority acceptedTimes = acceptedTimeAuthority ?? new(timeProvider);
    private readonly BaseSemanticActivationMigrationRegistry semanticMigrations = semanticMigrationRegistry ?? new([]);
    public ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        TRequest request,
        BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(session, definition, generatedIdentity, request, null, identity, options, null, cancellationToken);

    internal ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteWireAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        TRequest request,
        ReadOnlyMemory<byte> requestJson,
        BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(session, definition, generatedIdentity, request, requestJson, identity, options, null, cancellationToken);

    internal ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteTransactionalAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        TRequest request,
        BaseMutationRequestIdentity identity,
        BaseTransactionalActivationCandidate activation,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(session, definition, generatedIdentity, request, null, identity, null, activation, cancellationToken);

    internal ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteWireTransactionalAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        TRequest request,
        ReadOnlyMemory<byte> requestJson,
        BaseMutationRequestIdentity identity,
        BaseTransactionalActivationCandidate activation,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(session, definition, generatedIdentity, request, requestJson, identity, null, activation, cancellationToken);

    private async ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteCoreAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        TRequest request,
        ReadOnlyMemory<byte>? wireRequestJson,
        BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options,
        BaseTransactionalActivationCandidate? transactionalActivation,
        CancellationToken cancellationToken)
    {
        if (!AudienceAllowed(session, definition)
            || options?.MaximumWait is { } wait && (wait <= TimeSpan.Zero || wait > definition.Limits.Deadlines.CommitObservationTimeout))
            return Failure<TResult>(OperationStatus.PolicyDenied, BaseModuleMutationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        OperationContext moduleOperation = session.Operation(BaseOperationKind.ModuleMutation, definition.Id);
        CollectionDefinition policyResource = new()
            {
                Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, System = true,
                SystemOwnerModuleId = definition.OwningModuleId,
            };
        OperationResult<BasePolicyEvaluation> operationPolicy = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = session.Principal, Operation = moduleOperation, Collection = policyResource,
            ResourceKind = PolicyResourceKind.ModuleMutation,
        }, cancellationToken).ConfigureAwait(false);
        if (!operationPolicy.IsSuccess() || operationPolicy.Value?.Authority is null
            || !BaseSystemCollectionGate.HasExactModuleGrant(operationPolicy, definition.GrantId,
                definition.OwningModuleId, session.Principal, moduleOperation))
            return Failure<TResult>(OperationStatus.PolicyDenied, BaseModuleMutationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        if (!await AuthorizeDeclaredAuthorityAsync(
                session, definition, moduleOperation,
                cancellationToken).ConfigureAwait(false))
            return Failure<TResult>(OperationStatus.PolicyDenied, BaseModuleMutationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        byte[] requestBytes;
        JsonElement requestElement;
        try
        {
            requestBytes = wireRequestJson is { } wire
                ? CanonicalRequest(wire.Span, definition.Limits.MaximumRequestBytes)
                : CanonicalRequest(JsonSerializer.SerializeToUtf8Bytes(request, generatedIdentity.RequestTypeInfo), definition.Limits.MaximumRequestBytes);
            using JsonDocument requestDocument = JsonDocument.Parse(requestBytes);
            requestElement = requestDocument.RootElement.Clone();
            BaseModuleProgramEvaluator<TRequest, TResult>.ValidateDto(requestBytes, generatedIdentity.RequestBindings, providerInfluenced: false);
        }
        catch { return Failure<TResult>(OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation); }
        if (requestBytes.LongLength > definition.Limits.MaximumRequestBytes)
            return Failure<TResult>(OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.LimitExceeded, ErrorCategory.Validation);
        BaseSemanticActivationKeyDefinition? semanticDefinition;
        try { semanticDefinition = ResolveSemanticDefinition(definition, options, semanticRegistry); }
        catch { return Failure<TResult>(OperationStatus.ValidationFailed, "base.semanticActivation.contractInvalid", ErrorCategory.Validation); }
        if (semanticDefinition is not null && !await AuthorizeSemanticAsync(
                session, semanticDefinition, options!.SemanticActivation!, cancellationToken).ConfigureAwait(false))
            return Failure<TResult>(OperationStatus.PolicyDenied, BaseModuleMutationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        IReadOnlyDictionary<string, CollectionDefinition> installed = collections.Collections;
        var requestEvaluator = new BaseModuleProgramEvaluator<TRequest, TResult>(
            definition, generatedIdentity, requestElement, null, installed, definition.Limits);
        try
        {
            IReadOnlyDictionary<string, BaseModuleGuard> guards = definition.Template.Guards
                .ToDictionary(static value => value.Id, StringComparer.Ordinal);
            HashSet<string> reachableGuards = ReachableGuardIds(definition.Template, guards);
            foreach (string guardId in reachableGuards.Order(StringComparer.Ordinal))
                if (BaseModuleMutationContractValidator.IsRequestOnlyGuard(guardId, guards))
                    _ = requestEvaluator.Guard(guardId);
            foreach (BaseModulePrecondition precondition in definition.Template.Preconditions)
                if (!requestEvaluator.Guard(precondition.GuardId))
                    return Failure<TResult>(OperationStatus.ValidationFailed,
                        "base.moduleMutation.requirementFailed", ErrorCategory.Validation);
        }
        catch (BaseModuleScalarContractException)
        {
            return Failure<TResult>(OperationStatus.ValidationFailed,
                BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation);
        }
        catch (BaseModuleRequestLimitException)
        {
            return Failure<TResult>(OperationStatus.ValidationFailed,
                BaseModuleMutationErrorCodes.LimitExceeded, ErrorCategory.Validation);
        }
        catch (OverflowException)
        {
            return Failure<TResult>(OperationStatus.ValidationFailed,
                BaseModuleMutationErrorCodes.LimitExceeded, ErrorCategory.Validation);
        }
        catch
        {
            return Failure<TResult>(OperationStatus.ValidationFailed,
                BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation);
        }
        BaseModuleMutationCaptureExtension extension;
        CollectionDefinition[] authorityCollections;
        try
        {
            extension = BuildCaptureExtension(definition, requestEvaluator, session, registry, installed, requestBytes, out int disabledCaptures);
            if (disabledCaptures > definition.Limits.MaximumDisabledCaptures)
                return Failure<TResult>(OperationStatus.ValidationFailed,
                    BaseModuleMutationErrorCodes.LimitExceeded, ErrorCategory.Validation);
            authorityCollections = extension.Records.Select(static value => value.Collection)
                .Concat(extension.RelationTargets.Select(static value => value.TargetCollection))
                .Concat(definition.SystemCollectionIds.Select(id => installed[id]))
                .DistinctBy(static value => value.Id, StringComparer.Ordinal)
                .OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        }
        catch (BaseModuleRequestLimitException)
        {
            return Failure<TResult>(OperationStatus.ValidationFailed,
                BaseModuleMutationErrorCodes.LimitExceeded, ErrorCategory.Validation);
        }
        catch { return Failure<TResult>(OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation); }

        RecordStoreRegistration? storeRegistration = ResolveOneRegistration(authorityCollections);
        IAtomicRecordStore? atomicStore = storeRegistration?.AtomicExecutionStore
            ?? storeRegistration?.Store as IAtomicRecordStore;
        if (atomicStore is null || !BaseModuleMutationCapabilityContract.Supports(definition.Limits, atomicStore.Capabilities.ModuleMutation))
            return Failure<TResult>(OperationStatus.Unsupported, BaseModuleMutationErrorCodes.CapabilityMissing, ErrorCategory.Unsupported);
        if (semanticDefinition is not null && options?.SemanticActivation is BaseSemanticActivationGuardedRetireRequest
            && ExternalRecoverySelected(storeRegistration!.StoreId))
        {
            var replay = new BaseModuleMutationReceiptResolver<TResult>(
                definition, generatedIdentity.ResultTypeInfo, generatedIdentity.ResultBindings,
                session.Principal, moduleOperation, policy);
            try
            {
                RecordMutationExecutionResult existing = await atomicStore.ResolveAtomicReceiptAsync(
                    replay, identity, definition.Limits.Deadlines.ReceiptResolutionTimeout, cancellationToken).ConfigureAwait(false);
                if (existing.Outcome == RecordMutationExecutionOutcome.Committed && replay.Result is not null)
                {
                    if (!await FinalizeStoredSemanticRecoveryAsync(storeRegistration.StoreId, replay.SemanticReceipt,
                            existing.ReceiptAuthority, identity, cancellationToken).ConfigureAwait(false))
                        return Failure<TResult>(OperationStatus.StoreError, BaseSemanticActivationErrorCodes.ExternalPublicationPending, ErrorCategory.Store);
                    return new BaseSuccess<BaseModuleMutationExecutionResult<TResult>>(replay.Result,
                        OperationStatus.Ok, null, null, null, null);
                }
                if (existing.ReceiptResolution != BaseAtomicReceiptResolutionDisposition.ConfirmedMissing)
                    return Failure<TResult>(OperationStatus.StoreError,
                        existing.ReceiptResolution == BaseAtomicReceiptResolutionDisposition.Indeterminate
                            ? BaseModuleMutationErrorCodes.CommitIndeterminate
                            : BaseModuleMutationErrorCodes.ReceiptUnavailable,
                        ErrorCategory.Store);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { return Failure<TResult>(OperationStatus.StoreError, BaseModuleMutationErrorCodes.Cancelled, ErrorCategory.Store); }
            catch { return Failure<TResult>(OperationStatus.StoreError, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.Store); }
        }
        BaseAtomicMutationExecutionLimits limits = ResolveExecutionLimits(definition.Limits);
        OperationResult<BaseAtomicMutationAuthorityRequirement> authority = await atomicStore
            .CaptureAtomicMutationAuthorityRequirementAsync(session.ApplicationId, [.. authorityCollections], limits, cancellationToken)
            .ConfigureAwait(false);
        if (!authority.IsSuccess() || authority.Value is null)
            return Failure<TResult>(authority.Status, authority.Error ?? Error(BaseModuleMutationErrorCodes.AuthorityChanged, ErrorCategory.Conflict));
        BaseAtomicSemanticActivationExtension? semantic;
        byte[] localStructuralDigest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.moduleMutation.receipt.v1\0{definition.Id}\0{definition.Version}\0{Convert.ToHexString(definition.Checksum.ToArray())}\0{Convert.ToHexString(requestBytes)}"));
        try { semantic = CreateSemanticExtension(definition, options, semanticRegistry, acceptedTimes, authority.Value, storeRegistration!.StoreId, requestElement, generatedIdentity); }
        catch { return Failure<TResult>(OperationStatus.ValidationFailed, "base.semanticActivation.contractInvalid", ErrorCategory.Validation); }
        SemanticRecoveryExecution? recovery = null;
        if (semantic?.Operation is BaseSemanticActivationRetireIntent)
        {
            BaseResult<SemanticRecoveryExecution?> recoveryResult = await PrepareSemanticRecoveryAsync(
                atomicStore, semantic, identity, localStructuralDigest, semanticDefinition!, cancellationToken).ConfigureAwait(false);
            if (recoveryResult is not BaseSuccess<SemanticRecoveryExecution?> recoverySuccess)
                return Failure<TResult>(recoveryResult.Status, ((BaseFailure<SemanticRecoveryExecution?>)recoveryResult).Error);
            recovery = recoverySuccess.Value;
            if (recovery is not null)
                semantic = semantic with { Capture = semantic.Capture with
                {
                    RecoveryPreflight = recovery.Preflight,
                    RecoveryPending = recovery.PendingAuthority,
                }, StructuralDigest = Hash("base.semanticActivation.extension.v1\0",
                    semantic.Capture.Definition.Checksum.ToArray(), semantic.Capture.CanonicalKey.ToArray(),
                    semantic.Capture.ProposedScopeBindingId.ToArray(), [(byte)BaseSemanticActivationOperationKind.Retire],
                    recovery.PendingAuthority.Checksum.ToArray()).ToImmutableArray() };
        }

        string intentDigest = Digest("base.moduleMutation.intent.v1", extension.RequestDigest, authority.Value.ApplicationId);
        var intent = new BaseAtomicMutationIntent
        {
            IntentDigest = intentDigest,
            Authority = authority.Value,
            Items = [],
        };
        var processor = new BaseModuleMutationProcessor<TRequest, TResult>(
            definition, generatedIdentity, requestElement, intent, extension, options?.ActivationGuard,
            options?.ActivationCreation, semantic,
            (semanticDefinition?.Compaction as BaseSemanticActivationSubjectRetirementCompaction)?.SubjectReferenceRequestPropertyId,
            limits, installed,
            session.Principal, moduleOperation, operationPolicy.Value, requestEvaluator.EstablishedRequestGuards,
            schemaValidator, policy, normalizer, subjects, lifecycleConsumers, retirement, semanticMigrations, transactionalActivation);
        var executionRequest = new RecordMutationExecutionRequest
        {
            AcquisitionTimeout = definition.Limits.Deadlines.AcquisitionTimeout,
            TransactionTimeout = definition.Limits.Deadlines.TransactionTimeout,
            CommitCompletionTimeout = options?.MaximumWait ?? definition.Limits.Deadlines.CommitObservationTimeout,
            AtomicRequest = new BaseAtomicMutationExecutionRequest
            {
                Identity = identity,
                StructuralDigest = localStructuralDigest,
                ExpiresAt = timeProvider.GetUtcNow().Add(definition.ReceiptPolicy.Lifetime),
                MaxReceiptBytes = checked((int)Math.Min(definition.Limits.MaximumReceiptBytes, int.MaxValue)),
            },
        };
        RecordMutationExecutionResult execution;
        try { execution = await atomicStore.ExecuteAtomicAsync(processor, executionRequest, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return recovery is null
                ? Failure<TResult>(OperationStatus.StoreError, BaseModuleMutationErrorCodes.Cancelled, ErrorCategory.Store)
                : Failure<TResult>(OperationStatus.StoreError, BaseModuleMutationErrorCodes.CommitIndeterminate, ErrorCategory.Store);
        }
        catch
        {
            return recovery is null
                ? Failure<TResult>(OperationStatus.StoreError, BaseModuleMutationErrorCodes.StoreError, ErrorCategory.Store)
                : Failure<TResult>(OperationStatus.StoreError, BaseModuleMutationErrorCodes.CommitIndeterminate, ErrorCategory.Store);
        }
        if (execution.Outcome == RecordMutationExecutionOutcome.Indeterminate)
            return Failure<TResult>(OperationStatus.StoreError, BaseModuleMutationErrorCodes.CommitIndeterminate, ErrorCategory.Store);
        if (execution.Outcome != RecordMutationExecutionOutcome.Committed || processor.Result is null)
        {
            if (recovery is not null)
            {
                BaseSemanticRecoveryCancellationDisposition? cancelled = await CancelSemanticRecoveryAsync(recovery, identity, execution,
                    executionRequest.AtomicRequest!, cancellationToken).ConfigureAwait(false);
                if (cancelled is BaseSemanticRecoveryCancellationDisposition.AlreadyFinalized
                    or BaseSemanticRecoveryCancellationDisposition.CommitBoundPending)
                {
                    var resolver = new BaseModuleMutationReceiptResolver<TResult>(definition,
                        generatedIdentity.ResultTypeInfo, generatedIdentity.ResultBindings,
                        session.Principal, moduleOperation, policy);
                    RecordMutationExecutionResult resolved;
                    try
                    {
                        resolved = await atomicStore.ResolveAtomicReceiptAsync(resolver, identity,
                            definition.Limits.Deadlines.ReceiptResolutionTimeout, cancellationToken).ConfigureAwait(false);
                    }
                    catch { return Failure<TResult>(OperationStatus.StoreError, BaseSemanticActivationErrorCodes.ExternalPublicationPending, ErrorCategory.Store); }
                    if (resolved.ReceiptResolution == BaseAtomicReceiptResolutionDisposition.Found && resolver.Result is not null
                        && await FinalizeStoredSemanticRecoveryAsync(storeRegistration!.StoreId, resolver.SemanticReceipt,
                            resolved.ReceiptAuthority, identity, cancellationToken).ConfigureAwait(false))
                        return new BaseSuccess<BaseModuleMutationExecutionResult<TResult>>(resolver.Result,
                            OperationStatus.Ok, null, null, null, null);
                    return Failure<TResult>(OperationStatus.StoreError, BaseSemanticActivationErrorCodes.ExternalPublicationPending, ErrorCategory.Store);
                }
                if (cancelled is not (BaseSemanticRecoveryCancellationDisposition.Cancelled
                    or BaseSemanticRecoveryCancellationDisposition.AlreadyCancelled))
                    return Failure<TResult>(OperationStatus.StoreError, BaseSemanticActivationErrorCodes.ExternalPublicationPending, ErrorCategory.Store);
            }
            BaseError failure = execution.Processing?.Error is { } processingError
                ? processingError.Code == BaseMutationRequestErrorCodes.FingerprintConflict
                    ? Error(BaseMutationRequestErrorCodes.FingerprintConflict, ErrorCategory.Conflict)
                    : processingError
                : NormalizeProviderExecutionError(execution.Error);
            return Failure<TResult>(failure.Category == ErrorCategory.Store
                ? OperationStatus.StoreError : OperationStatus.Conflict, failure);
        }
        if (recovery is not null && !await FinalizeSemanticRecoveryAsync(
                recovery, processor.SemanticReceipt, execution.ReceiptAuthority, identity, cancellationToken).ConfigureAwait(false))
            return Failure<TResult>(OperationStatus.StoreError, BaseSemanticActivationErrorCodes.ExternalPublicationPending, ErrorCategory.Store);
        return new BaseSuccess<BaseModuleMutationExecutionResult<TResult>>(
            processor.Result with
            {
                Disposition = execution.RequestDisposition,
                Outcome = execution.RequestDisposition == BaseMutationRequestDisposition.Duplicate
                    ? BaseModuleMutationOutcome.Duplicate : BaseModuleMutationOutcome.Committed,
            },
            OperationStatus.Updated, null, null, null, null);
    }

    private bool ExternalRecoverySelected(string logicalStoreId) => semanticRecoveryRegistry is not null
        && semanticRecoveryRegistry.Selections.TryGetValue(logicalStoreId, out BaseSemanticActivationRestoreSelection? selection)
        && selection.EnabledRestoreMode == BaseActivationRestoreMode.NewDisasterDomain;

    private async ValueTask<bool> FinalizeStoredSemanticRecoveryAsync(string logicalStoreId,
        BaseSemanticActivationReceiptEvidence? receipt, BaseCommittedAtomicReceiptAuthority? receiptAuthority,
        BaseMutationRequestIdentity localIdentity, CancellationToken cancellationToken)
    {
        BaseSemanticRecoveryLocalReceiptAuthority? local = receipt?.RecoveryPublication;
        var owned = semanticRecoveryRegistry?.Find(logicalStoreId);
        if (local is null || owned is null || receiptAuthority is null) return false;
        BaseSemanticRecoveryPendingCommitAuthority pending = local.PendingAuthority;
        BaseSemanticRecoveryAuthorityDefinition definition = owned.Value.Definition;
        if (pending.AuthorityId != definition.Id || pending.AuthorityVersion != definition.Version
            || !CryptographicOperations.FixedTimeEquals(pending.AuthorityChecksum.AsSpan(), definition.ContractChecksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticRecoveryAuthorityContract.PendingCommitChecksum(pending).AsSpan(), pending.Checksum.AsSpan())
            || !BaseSemanticRecoveryAuthorityContract.PendingCommitIsValid(definition, pending.Intent, pending.Pending)
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticRecoveryAuthorityContract.RecoveryEntryChecksum(local.FinalEntry).AsSpan(), local.FinalEntry.Checksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticRecoveryAuthorityContract.LocalReceiptAuthorityChecksum(local).AsSpan(), local.Checksum.AsSpan()))
            return false;
        BaseSemanticRecoveryOperationLimits limits = BaseSemanticRecoveryAuthorityContract.OperationLimits(definition);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(limits.PublicationDeadline);
            var request = new BaseSemanticRecoveryFinalizeRequest
            {
                ApplicationId = pending.ApplicationId, LogicalStoreId = pending.LogicalStoreId,
                Pending = pending.Pending, FinalEntry = local.FinalEntry,
                LocalReceipt = CreateLocalReceiptEnvelope(localIdentity, receiptAuthority, receipt!.CommitEvidenceChecksum),
                CommitObservationChecksum = receipt.CommitEvidenceChecksum,
                Identity = RecoveryIdentity(localIdentity, "finalize", local.FinalEntry.Checksum.AsSpan()), Limits = limits,
            };
            BaseResult<BaseSemanticRecoveryFinalizationResult> result = await semanticRecoveryRegistry!.InvokeAsync(logicalStoreId,
                limits.PublicationDeadline, request, HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryFinalizeRequest,
                HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryFinalizationResult,
                static (instance, value, token) => instance.FinalizeAsync(value, token), deadline.Token).ConfigureAwait(false);
            return result is BaseSuccess<BaseSemanticRecoveryFinalizationResult> success
                && FinalizationMatches(definition, request, success.Value);
        }
        catch { return false; }
    }

    private async ValueTask<BaseResult<SemanticRecoveryExecution?>> PrepareSemanticRecoveryAsync(
        IAtomicRecordStore atomicStore,
        BaseAtomicSemanticActivationExtension semantic,
        BaseMutationRequestIdentity localIdentity,
        byte[] localStructuralDigest,
        BaseSemanticActivationKeyDefinition installedDefinition,
        CancellationToken cancellationToken)
    {
        if (semanticRecoveryRegistry is null || semantic.Operation is not BaseSemanticActivationRetireIntent retire)
            return new BaseSuccess<SemanticRecoveryExecution?>(null, OperationStatus.Ok, null, null, null, null);
        if (!semanticRecoveryRegistry.Selections.TryGetValue(semantic.Capture.StoreAuthority.LogicalStoreId, out BaseSemanticActivationRestoreSelection? selection)
            || selection.EnabledRestoreMode != BaseActivationRestoreMode.NewDisasterDomain)
            return new BaseSuccess<SemanticRecoveryExecution?>(null, OperationStatus.Ok, null, null, null, null);
        var owned = semanticRecoveryRegistry.Find(semantic.Capture.StoreAuthority.LogicalStoreId);
        if (owned is null || atomicStore is not IBaseSemanticActivationPreflightStore preflightStore)
            return RecoveryFailure(BaseSemanticActivationErrorCodes.ExternalAuthorityUnavailable, OperationStatus.Unsupported);
        BaseSemanticRecoveryAuthorityDefinition authorityDefinition = owned.Value.Definition;
        BaseSemanticRecoveryOperationLimits externalLimits = BaseSemanticRecoveryAuthorityContract.OperationLimits(authorityDefinition);
        TimeSpan preflightDeadline = Min(installedDefinition.Limits.Deadlines.AcquisitionTimeout, externalLimits.AcquisitionDeadline);
        BaseSemanticActivationSubjectLifetimeBinding? lifetime = retire.SubjectLifetime;
        var preflightRequest = new BaseSemanticRecoveryPreflightRequest
        {
            Definition = retire.Definition, CanonicalKey = retire.CanonicalKey,
            KeyPreimageChecksum = semantic.Capture.KeyPreimageChecksum, Scope = retire.Scope,
            SubjectLifetime = lifetime is null ? null : new BaseSemanticRecoverySubjectLifetimePreimage
            {
                ContractId = lifetime.ContractId, ContractVersion = lifetime.ContractVersion,
                ContractChecksum = lifetime.ContractChecksum, SubjectId = lifetime.SubjectId,
                AuthorityEpoch = lifetime.AuthorityEpoch, Incarnation = lifetime.Incarnation,
            },
            MaximumCanonicalKeyBytes = installedDefinition.Limits.MaximumCanonicalKeyBytes,
            StoreAuthority = semantic.Capture.StoreAuthority, Limits = semantic.Capture.Limits,
            Deadline = preflightDeadline,
        };
        OperationResult<BaseSemanticRecoveryPreflightEvidence> preflight;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(preflightDeadline);
            preflight = await preflightStore.PreflightSemanticRecoveryAsync(preflightRequest, deadline.Token).ConfigureAwait(false);
        }
        catch { return RecoveryFailure(BaseSemanticActivationErrorCodes.ExternalAuthorityUnavailable, OperationStatus.StoreError); }
        if (!preflight.IsSuccess() || preflight.Value is null
            || !BaseSemanticActivationEvidenceContract.RecoveryPreflightIsValid(preflightRequest, preflight.Value))
            return RecoveryFailure(preflight.Error?.Code ?? BaseSemanticActivationErrorCodes.ActivationNotTerminal,
                preflight.IsSuccess() ? OperationStatus.StoreError : preflight.Status);
        var boundary = new BaseSemanticActivationRecoveryBoundary
        {
            DefinitionId = retire.Definition.Id, ScopeBindingId = preflight.Value.ScopeBinding.BindingId,
            Key = BaseSemanticActivationKeyDigest.Create(preflight.Value.Key.ToArray()),
        };
        byte[] operationFingerprint = BaseSemanticRecoveryAuthorityContract
            .RetirementOperationFingerprint(retire.CompletionOperation).ToArray();
        var intent = new BaseSemanticRecoveryPendingTerminalIntent
        {
            Boundary = boundary, RetirementOperationFingerprint = operationFingerprint.ToImmutableArray(),
            SubjectLifetime = preflight.Value.Live.SubjectLifetime, Checksum = [],
        };
        intent = intent with { Checksum = BaseSemanticRecoveryAuthorityContract.PendingIntentChecksum(intent) };
        BaseMutationRequestIdentity beginIdentity = RecoveryIdentity(localIdentity, "begin", intent.Checksum.AsSpan());
        BaseSemanticRecoveryPendingPublication? resolvedPending = null;
        BaseSemanticRecoveryPendingResolutionDisposition disposition;
        try
        {
            var resolveRequest = new BaseSemanticRecoveryResolvePendingRequest
            {
                ApplicationId = semantic.Capture.StoreAuthority.ApplicationId,
                LogicalStoreId = semantic.Capture.StoreAuthority.LogicalStoreId,
                Intent = intent, BeginIdentity = beginIdentity, Limits = externalLimits,
            };
            BaseResult<BaseSemanticRecoveryPendingResolution> resolved = await ResolvePendingBoundedAsync(
                semantic.Capture.StoreAuthority.LogicalStoreId, resolveRequest, externalLimits.ResolutionDeadline, cancellationToken).ConfigureAwait(false);
            if (resolved is not BaseSuccess<BaseSemanticRecoveryPendingResolution> resolution
                || !BaseSemanticRecoveryAuthorityContract.PendingResolutionIsValid(
                    authorityDefinition, resolveRequest, resolution.Value, timeProvider.GetUtcNow()))
                return RecoveryFailure(BaseSemanticActivationErrorCodes.ExternalAuthorityUnavailable, OperationStatus.StoreError);
            disposition = resolution.Value.Disposition;
            resolvedPending = resolution.Value.Pending;
            if (disposition == BaseSemanticRecoveryPendingResolutionDisposition.Missing)
            {
                BaseResult<BaseSemanticRecoveryPendingPublication>? begun = null;
                try
                {
                    using var beginDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    beginDeadline.CancelAfter(externalLimits.AcquisitionDeadline);
                    var beginRequest = new BaseSemanticRecoveryBeginRequest
                    {
                        ApplicationId = semantic.Capture.StoreAuthority.ApplicationId,
                        LogicalStoreId = semantic.Capture.StoreAuthority.LogicalStoreId,
                        Intent = intent, Identity = beginIdentity, Limits = externalLimits,
                    };
                    begun = await semanticRecoveryRegistry!.InvokeAsync(semantic.Capture.StoreAuthority.LogicalStoreId,
                        externalLimits.AcquisitionDeadline, beginRequest,
                        HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryBeginRequest,
                        HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryPendingPublication,
                        static (instance, value, token) => instance.BeginAsync(value, token), beginDeadline.Token).ConfigureAwait(false);
                }
                catch when (!cancellationToken.IsCancellationRequested) { }
                if (begun is BaseSuccess<BaseSemanticRecoveryPendingPublication> success)
                    resolvedPending = success.Value;
                else
                {
                    BaseResult<BaseSemanticRecoveryPendingResolution> afterLoss = await ResolvePendingBoundedAsync(
                        semantic.Capture.StoreAuthority.LogicalStoreId, resolveRequest, externalLimits.ResolutionDeadline, cancellationToken).ConfigureAwait(false);
                    if (afterLoss is not BaseSuccess<BaseSemanticRecoveryPendingResolution> recovered
                        || recovered.Value.Disposition != BaseSemanticRecoveryPendingResolutionDisposition.Pending
                        || !BaseSemanticRecoveryAuthorityContract.PendingResolutionIsValid(
                            authorityDefinition, resolveRequest, recovered.Value, timeProvider.GetUtcNow()))
                        return RecoveryFailure(BaseSemanticActivationErrorCodes.ExternalAuthorityUnavailable, OperationStatus.StoreError);
                    resolvedPending = recovered.Value.Pending;
                }
            }
            else if (disposition is BaseSemanticRecoveryPendingResolutionDisposition.Cancelled
                or BaseSemanticRecoveryPendingResolutionDisposition.Finalized)
                return RecoveryFailure(BaseSemanticActivationErrorCodes.ExternalPublicationPending, OperationStatus.Conflict);
        }
        catch { return RecoveryFailure(BaseSemanticActivationErrorCodes.ExternalAuthorityUnavailable, OperationStatus.StoreError); }
        if (resolvedPending is not { } pendingValue
            || !BaseSemanticRecoveryAuthorityContract.PendingIsValid(
                authorityDefinition, intent, pendingValue, timeProvider.GetUtcNow()))
            return RecoveryFailure(BaseSemanticActivationErrorCodes.ExternalAuthorityUnavailable, OperationStatus.StoreError);
        var pendingAuthority = new BaseSemanticRecoveryPendingCommitAuthority
        {
            ApplicationId = semantic.Capture.StoreAuthority.ApplicationId,
            LogicalStoreId = semantic.Capture.StoreAuthority.LogicalStoreId,
            LocalScope = localIdentity.Scope, LocalOperation = localIdentity.Operation,
            LocalIdempotencyKey = localIdentity.IdempotencyKey,
            LocalFingerprint = localIdentity.Fingerprint.ToArray().ToImmutableArray(),
            LocalStructuralDigest = localStructuralDigest.ToImmutableArray(),
            AuthorityId = authorityDefinition.Id, AuthorityVersion = authorityDefinition.Version,
            AuthorityChecksum = authorityDefinition.ContractChecksum, Intent = intent,
            Pending = pendingValue, Checksum = [],
        };
        pendingAuthority = pendingAuthority with { Checksum = BaseSemanticRecoveryAuthorityContract.PendingCommitChecksum(pendingAuthority) };
        return new BaseSuccess<SemanticRecoveryExecution?>(new(
            authorityDefinition, semantic.Capture.StoreAuthority.LogicalStoreId, externalLimits,
            preflight.Value, intent, pendingValue, pendingAuthority),
            OperationStatus.Ok, null, null, null, null);
    }

    private async ValueTask<BaseResult<BaseSemanticRecoveryPendingResolution>> ResolvePendingBoundedAsync(
        string logicalStoreId, BaseSemanticRecoveryResolvePendingRequest request,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        return await semanticRecoveryRegistry!.InvokeAsync(logicalStoreId, timeout, request,
            HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryResolvePendingRequest,
            HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryPendingResolution,
            static (authority, value, token) => authority.ResolvePendingAsync(value, token), deadline.Token).ConfigureAwait(false);
    }

    private async ValueTask<BaseSemanticRecoveryCancellationDisposition?> CancelSemanticRecoveryAsync(SemanticRecoveryExecution recovery,
        BaseMutationRequestIdentity localIdentity, RecordMutationExecutionResult execution,
        BaseAtomicMutationExecutionRequest atomicRequest, CancellationToken cancellationToken)
    {
        if (execution.Outcome == RecordMutationExecutionOutcome.Indeterminate) return null;
        ImmutableArray<byte> rollback = execution.ConfirmedRollbackProofChecksum;
        if (rollback.Length != 32 || !CryptographicOperations.FixedTimeEquals(rollback.AsSpan(),
                BaseSemanticRecoveryAuthorityContract.RollbackProofChecksum(recovery.PendingAuthority, atomicRequest, execution.Outcome).AsSpan()))
            return null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(recovery.Limits.PublicationDeadline);
            var request = new BaseSemanticRecoveryCancelRequest
            {
                Pending = recovery.Pending, ConfirmedRollbackProofChecksum = rollback,
                Identity = RecoveryIdentity(localIdentity, "cancel", rollback.AsSpan()), Limits = recovery.Limits,
            };
            BaseResult<BaseSemanticRecoveryCancellationResult> result = await semanticRecoveryRegistry!.InvokeAsync(recovery.LogicalStoreId,
                recovery.Limits.PublicationDeadline, request, HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryCancelRequest,
                HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryCancellationResult,
                static (instance, value, token) => instance.CancelAsync(value, token), deadline.Token).ConfigureAwait(false);
            return result is BaseSuccess<BaseSemanticRecoveryCancellationResult> success
                && BaseSemanticRecoveryAuthorityContract.CancellationIsValid(recovery.Definition, request, success.Value)
                    ? success.Value.Disposition : null;
        }
        catch { return null; }
    }

    private async ValueTask<bool> FinalizeSemanticRecoveryAsync(SemanticRecoveryExecution recovery,
        BaseSemanticActivationReceiptEvidence? receipt, BaseCommittedAtomicReceiptAuthority? receiptAuthority,
        BaseMutationRequestIdentity localIdentity,
        CancellationToken cancellationToken)
    {
        BaseSemanticRecoveryLocalReceiptAuthority? local = receipt?.RecoveryPublication;
        if (local is null || receiptAuthority is null
            || !CryptographicOperations.FixedTimeEquals(local.PendingAuthority.Checksum.AsSpan(), recovery.PendingAuthority.Checksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(BaseSemanticRecoveryAuthorityContract.LocalReceiptAuthorityChecksum(local).AsSpan(), local.Checksum.AsSpan()))
            return false;
        BaseSemanticActivationRecoveryEntry entry = local.FinalEntry;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(recovery.Limits.PublicationDeadline);
            var request = new BaseSemanticRecoveryFinalizeRequest
            {
                ApplicationId = recovery.PendingAuthority.ApplicationId,
                LogicalStoreId = recovery.PendingAuthority.LogicalStoreId,
                Pending = recovery.Pending, FinalEntry = entry,
                LocalReceipt = CreateLocalReceiptEnvelope(localIdentity, receiptAuthority, receipt!.CommitEvidenceChecksum),
                CommitObservationChecksum = receipt!.CommitEvidenceChecksum,
                Identity = RecoveryIdentity(localIdentity, "finalize", entry.Checksum.AsSpan()), Limits = recovery.Limits,
            };
            BaseResult<BaseSemanticRecoveryFinalizationResult> result = await semanticRecoveryRegistry!.InvokeAsync(recovery.LogicalStoreId,
                recovery.Limits.PublicationDeadline, request, HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryFinalizeRequest,
                HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryFinalizationResult,
                static (instance, value, token) => instance.FinalizeAsync(value, token), deadline.Token).ConfigureAwait(false);
            return result is BaseSuccess<BaseSemanticRecoveryFinalizationResult> success
                && success.Value.Head.ApplicationId == recovery.Preflight.Live.StoreAuthority.Requirement.ApplicationId
                && FinalizationMatches(recovery.Definition, request, success.Value);
        }
        catch { return false; }
    }

    private static BaseMutationRequestIdentity RecoveryIdentity(BaseMutationRequestIdentity local, string operation, ReadOnlySpan<byte> authority)
    {
        byte[] fingerprint = Hash("base.semanticRecovery.identity.v1\0", local.Fingerprint.ToArray(), Encoding.UTF8.GetBytes(operation), authority.ToArray());
        return BaseMutationRequestIdentity.Create(local.Scope, $"semantic-recovery-{operation}",
            Convert.ToHexStringLower(Hash("base.semanticRecovery.idempotency.v1\0", local.Fingerprint.ToArray(), authority.ToArray())),
            BaseMutationRequestFingerprint.Create(fingerprint));
    }

    private static BaseSemanticRecoveryLocalReceiptEnvelope CreateLocalReceiptEnvelope(
        BaseMutationRequestIdentity identity,
        BaseCommittedAtomicReceiptAuthority authority,
        ImmutableArray<byte> commitObservationChecksum)
    {
        var value = new BaseSemanticRecoveryLocalReceiptEnvelope
        {
            Identity = identity,
            StructuralDigest = authority.StructuralDigest,
            ReceiptBytes = authority.ReceiptBytes,
            ReceiptChecksum = authority.ReceiptChecksum,
            ReceiptFormatVersion = authority.FormatVersion,
            SchemaGeneration = authority.SchemaGeneration,
            StoreInstanceId = authority.StoreInstanceId,
            CommittedAt = authority.CommittedAt,
            ExpiresAt = authority.ExpiresAt,
            CommitObservationChecksum = commitObservationChecksum,
            Checksum = [],
        };
        return value with { Checksum = BaseSemanticRecoveryAuthorityContract.LocalReceiptEnvelopeChecksum(value) };
    }

    private static bool FinalizationMatches(BaseSemanticRecoveryAuthorityDefinition definition,
        BaseSemanticRecoveryFinalizeRequest request, BaseSemanticRecoveryFinalizationResult value) =>
        BaseSemanticRecoveryAuthorityContract.FinalizationIsValid(definition, request, value);

    private static BaseFailure<SemanticRecoveryExecution?> RecoveryFailure(string code, OperationStatus status) =>
        new(status, Error(code, status == OperationStatus.Unsupported ? ErrorCategory.Unsupported : ErrorCategory.Store), null, null);

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;

    private sealed record SemanticRecoveryExecution(
        BaseSemanticRecoveryAuthorityDefinition Definition,
        string LogicalStoreId,
        BaseSemanticRecoveryOperationLimits Limits,
        BaseSemanticRecoveryPreflightEvidence Preflight,
        BaseSemanticRecoveryPendingTerminalIntent Intent,
        BaseSemanticRecoveryPendingPublication Pending,
        BaseSemanticRecoveryPendingCommitAuthority PendingAuthority);

    private BaseAtomicSemanticActivationExtension? CreateSemanticExtension<TRequest, TResult>(
        BaseRegisteredModuleMutationDefinition operation,
        BaseModuleMutationExecutionOptions? options,
        BaseSemanticActivationRegistry? registry,
        BaseActivationAcceptedTimeAuthority acceptedTime,
        BaseAtomicMutationAuthorityRequirement authority,
        string logicalStoreId,
        JsonElement request,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> requestIdentity)
    {
        BaseSemanticActivationGuardedRequest? requested = options?.SemanticActivation;
        if (requested is null) return null;
        if (registry is null || options!.ActivationGuard is null || options.ActivationCreation is not null)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseSemanticActivationKeyDefinition definition = registry.Find(requested.Key.DefinitionId, requested.Key.DefinitionVersion)
            ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        byte[] suppliedChecksum = requested.Key.CopyDefinitionChecksum();
        if (!CryptographicOperations.FixedTimeEquals(suppliedChecksum, definition.Checksum.AsSpan())
            || !string.Equals(requested.Key.ApplicationId, definition.OwningApplicationId, StringComparison.Ordinal)
            || !string.Equals(requested.Key.ModuleId, definition.OwningModuleId, StringComparison.Ordinal)
            || requested.Key.OwnerGeneration != registry.OwnerGeneration
            || requested.Scope.Kind != definition.ScopeKind)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseSemanticActivationModuleOperationIdentity expected = requested is BaseSemanticActivationGuardedEnsureRequest
            ? definition.EnsureOperation : definition.RetirementOperation;
        if (!string.Equals(operation.Id, expected.OperationId, StringComparison.Ordinal)
            || operation.Version != expected.OperationVersion
            || !string.Equals(Convert.ToHexStringLower(operation.Checksum.ToArray()), expected.OperationChecksum, StringComparison.Ordinal))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        byte[] canonicalKey = requested.Key.CopyCanonicalKey();
        if (canonicalKey.Length is < 1 || canonicalKey.Length > definition.Limits.MaximumCanonicalKeyBytes)
            throw new InvalidOperationException("base.semanticActivation.keyInvalid");
        byte[] proposedScopeBinding = RandomNumberGenerator.GetBytes(32);
        BaseSemanticActivationSubjectLifetimeBinding? subjectLifetime = ExtractSubjectLifetime(
            definition, request, requestIdentity, proposedScopeBinding);
        BaseSemanticActivationKeyDigest key = BaseSemanticActivationKeyDigest.Create(Hash(
            "base.semanticActivation.key.v1\0", Encoding.UTF8.GetBytes(definition.Id), proposedScopeBinding, canonicalKey));
        var identity = new BaseSemanticActivationDefinitionIdentity
        {
            Id = new string(definition.Id.AsSpan()), Version = definition.Version,
            Checksum = definition.Checksum.ToArray().ToImmutableArray(), OwnerGeneration = registry.OwnerGeneration,
            OwningModuleId = new string(definition.OwningModuleId.AsSpan()),
            RetirementOperation = definition.RetirementOperation with { },
        };
        BaseSemanticActivationOperation semanticOperation = requested switch
        {
            BaseSemanticActivationGuardedEnsureRequest ensure => CreateEnsure(ensure, identity, key, canonicalKey, proposedScopeBinding, subjectLifetime,
                definition.OwningApplicationId, logicalStoreId, definition.OwningModuleId,
                activationRegistry ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid")),
            BaseSemanticActivationGuardedRetireRequest => new BaseSemanticActivationRetireIntent
            {
                Definition = identity, Key = key, CanonicalKey = canonicalKey.ToImmutableArray(),
                Scope = requested.Scope with { Value = requested.Scope.Value is null ? null : new string(requested.Scope.Value.AsSpan()) },
                SubjectLifetime = subjectLifetime,
                CompletionOperation = definition.RetirementOperation with { },
            },
            _ => throw new InvalidOperationException("base.semanticActivation.contractInvalid"),
        };
        byte[] structural = Hash("base.semanticActivation.extension.v1\0", definition.Checksum.ToArray(), canonicalKey,
            proposedScopeBinding, [(byte)(requested is BaseSemanticActivationGuardedEnsureRequest ? 1 : 2)]);
        return new BaseAtomicSemanticActivationExtension
        {
            Capture = new BaseSemanticActivationCaptureRequest
            {
                Definition = identity,
                CanonicalKey = canonicalKey.ToImmutableArray(),
                KeyPreimageChecksum = requested.Key.CopyPreimageChecksum().ToImmutableArray(),
                Scope = requested.Scope with { Value = requested.Scope.Value is null ? null : new string(requested.Scope.Value.AsSpan()) },
                ProposedScopeBindingId = proposedScopeBinding.ToImmutableArray(),
                Operation = requested is BaseSemanticActivationGuardedEnsureRequest
                    ? BaseSemanticActivationOperationKind.Ensure : BaseSemanticActivationOperationKind.Retire,
                StoreAuthority = ResolveSemanticStoreAuthority(authority, registry, logicalStoreId),
                Limits = definition.Limits.Execution with { },
                AcceptedTime = acceptedTime.Capture(definition.OwningApplicationId),
            },
            Operation = semanticOperation,
            StructuralDigest = structural.ToImmutableArray(),
        };
    }

    private static BaseSemanticActivationStoreAuthorityRequirement ResolveSemanticStoreAuthority(
        BaseAtomicMutationAuthorityRequirement authority, BaseSemanticActivationRegistry registry, string logicalStoreId)
    {
        BaseSemanticActivationStoreAuthorityRequirement value = authority.SemanticActivation
            ?? throw new InvalidOperationException("base.semanticActivation.capabilityMissing");
        if (!string.Equals(value.ApplicationId, authority.ApplicationId, StringComparison.Ordinal)
            || !string.Equals(value.LogicalStoreId, logicalStoreId, StringComparison.Ordinal)
            || !string.Equals(value.StoreInstanceId, authority.StoreInstanceId, StringComparison.Ordinal)
            || value.RestoreEpoch != authority.RestoreEpoch || value.SchemaGeneration != authority.SchemaGeneration
            || value.SemanticAuthorityGeneration <= 0 || value.DefinitionSetChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(value.DefinitionSetChecksum.AsSpan(), registry.DefinitionSetChecksum.AsSpan()))
            throw new InvalidOperationException("base.semanticActivation.authorityChanged");
        return value with
        {
            ApplicationId = new string(value.ApplicationId.AsSpan()), LogicalStoreId = new string(value.LogicalStoreId.AsSpan()),
            StoreInstanceId = new string(value.StoreInstanceId.AsSpan()),
            DefinitionSetChecksum = value.DefinitionSetChecksum.ToArray().ToImmutableArray(),
        };
    }

    private static BaseSemanticActivationKeyDefinition? ResolveSemanticDefinition(
        BaseRegisteredModuleMutationDefinition operation,
        BaseModuleMutationExecutionOptions? options,
        BaseSemanticActivationRegistry? registry)
    {
        BaseSemanticActivationGuardedRequest? requested = options?.SemanticActivation;
        if (requested is null) return null;
        if (registry is null || options!.ActivationGuard is null || options.ActivationCreation is not null)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseSemanticActivationKeyDefinition definition = registry.Find(requested.Key.DefinitionId, requested.Key.DefinitionVersion)
            ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseSemanticActivationModuleOperationIdentity expected = requested is BaseSemanticActivationGuardedEnsureRequest
            ? definition.EnsureOperation : definition.RetirementOperation;
        if (!string.Equals(operation.Id, expected.OperationId, StringComparison.Ordinal)
            || operation.Version != expected.OperationVersion
            || !string.Equals(Convert.ToHexStringLower(operation.Checksum.ToArray()), expected.OperationChecksum, StringComparison.Ordinal))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        return definition;
    }

    private async ValueTask<bool> AuthorizeSemanticAsync(
        BaseSession session,
        BaseSemanticActivationKeyDefinition definition,
        BaseSemanticActivationGuardedRequest request,
        CancellationToken cancellationToken)
    {
        string grant = request is BaseSemanticActivationGuardedEnsureRequest
            ? definition.EnsureGrantId : definition.RetirementGrantId;
        OperationContext operation = session.Operation(BaseOperationKind.ModuleMutation, definition.Id) with
        {
            CollectionId = definition.Id,
            Mode = OperationMode.System,
        };
        OperationResult<BasePolicyEvaluation> evaluation = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = session.Principal,
            Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = definition.Id, Name = "Semantic activation authority", Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                System = true, SystemOwnerModuleId = definition.OwningModuleId,
            },
            ResourceKind = PolicyResourceKind.ModuleMutation,
        }, cancellationToken).ConfigureAwait(false);
        return BaseSystemCollectionGate.HasExactModuleGrant(
            evaluation, grant, definition.OwningModuleId, session.Principal, operation);
    }

    private static BaseSemanticActivationEnsureIntent CreateEnsure(
        BaseSemanticActivationGuardedEnsureRequest request,
        BaseSemanticActivationDefinitionIdentity definition,
        BaseSemanticActivationKeyDigest key,
        byte[] canonicalKey,
        byte[] scopeBinding,
        BaseSemanticActivationSubjectLifetimeBinding? subjectLifetime,
        string applicationId,
        string logicalStoreId,
        string owningModuleId,
        BaseActivationRegistry installedActivations)
    {
        long due = request.DueAt?.ToUnixTimeMilliseconds() ?? 0;
        var dueAuthority = new BaseSemanticActivationDueAuthority
        {
            Mode = request.DueAt is null ? BaseSemanticActivationDueMode.AcceptedCurrentTime : BaseSemanticActivationDueMode.ExplicitUtcInstant,
            CanonicalUnixMilliseconds = due,
        };
        Span<byte> digest = stackalloc byte[32]; key.CopyTo(digest);
        byte[] activationIdBytes = SemanticActivationId(applicationId, logicalStoreId, owningModuleId,
            definition.Id, scopeBinding, canonicalKey);
        byte[] creationChecksum = Hash("base.semanticActivation.creation.v1\0", definition.Checksum.ToArray(), digest.ToArray(), scopeBinding, activationIdBytes);
        BaseActivationDefinition installed = installedActivations.Find(request.Activation.Id, request.Activation.Version)
            ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        if (!CryptographicOperations.FixedTimeEquals(installed.Checksum.AsSpan(), request.Activation.Checksum.AsSpan()))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        return new BaseSemanticActivationEnsureIntent
        {
            Definition = definition, Key = key, CanonicalKey = canonicalKey.ToImmutableArray(),
            Scope = request.Scope with { Value = request.Scope.Value is null ? null : new string(request.Scope.Value.AsSpan()) },
            SubjectLifetime = subjectLifetime,
            Due = dueAuthority,
            Activation = new BaseSemanticActivationCreateIntent
            {
                Definition = request.Activation with { Checksum = request.Activation.Checksum.ToArray().ToImmutableArray() },
                CanonicalInput = request.CanonicalInput.ToArray().ToImmutableArray(), InputChecksum = request.InputChecksum.ToArray().ToImmutableArray(),
                Scope = request.Scope with { Value = request.Scope.Value is null ? null : new string(request.Scope.Value.AsSpan()) }, Due = dueAuthority,
                Priority = 0, InitiallyEligible = true,
                Identity = new BaseSemanticActivationCreationIdentity
                {
                    SemanticDefinition = definition, Key = key, ScopeBindingId = scopeBinding.ToImmutableArray(),
                    DerivedActivationIdBytes = activationIdBytes.ToImmutableArray(), Checksum = creationChecksum.ToImmutableArray(),
                },
                Limits = installed.Limits with
                {
                    Provider = installed.Limits.Provider with { },
                    AtomicCreation = installed.Limits.AtomicCreation with { Deadlines = installed.Limits.AtomicCreation.Deadlines with { } },
                },
            },
        };
    }

    private static byte[] SemanticActivationId(string applicationId, string logicalStoreId, string owningModuleId,
        string definitionId, byte[] scopeBinding, byte[] canonicalKey) => Hash(
        "base.semanticActivation.activation.v1\0", Encoding.UTF8.GetBytes(applicationId), Encoding.UTF8.GetBytes(logicalStoreId),
        Encoding.UTF8.GetBytes(owningModuleId), Encoding.UTF8.GetBytes(definitionId), scopeBinding, canonicalKey);

    private BaseSemanticActivationSubjectLifetimeBinding? ExtractSubjectLifetime<TRequest, TResult>(
        BaseSemanticActivationKeyDefinition definition,
        JsonElement request,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity,
        byte[] proposedScopeBinding)
    {
        if (definition.Compaction is BaseSemanticActivationNoCompaction) return null;
        if (definition.Compaction is not BaseSemanticActivationSubjectRetirementCompaction compaction)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseGeneratedSubjectRegistration subject = subjects.Find(
            compaction.SubjectContract.ContractId, compaction.SubjectContract.ContractVersion)
            ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseModuleDtoPropertyBinding[] matches = identity.RequestBindings.Values
            .Where(value => value.StablePropertyId == compaction.SubjectReferenceRequestPropertyId).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        JsonElement current = request;
        for (int index = 0; index < matches[0].WirePropertyPath.Count; index++)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(matches[0].WirePropertyPath[index], out current))
                throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        }
        var decoded = BaseSubjectReferenceEncoding.DecodeElement(current, subject.Definition.SubjectIdKind,
            subject.Definition.MaximumSubjectIdUtf8Bytes);
        var value = new BaseSemanticActivationSubjectLifetimeBinding
        {
            ContractId = subject.Definition.Id, ContractVersion = subject.Definition.Version,
            ContractChecksum = Convert.FromHexString(subject.Checksum).ToImmutableArray(),
            SubjectId = decoded.SubjectId,
            AuthorityEpoch = decoded.AuthorityEpoch,
            Incarnation = decoded.Incarnation,
            ScopeBindingId = proposedScopeBinding.ToImmutableArray(), Checksum = [],
        };
        return value with { Checksum = SemanticLifetimeChecksum(value).ToImmutableArray() };
    }

    private static byte[] SemanticLifetimeChecksum(BaseSemanticActivationSubjectLifetimeBinding value) => Hash(
        "base.semanticActivation.subjectLifetime.v1\0", Encoding.UTF8.GetBytes(value.ContractId),
        BitConverter.GetBytes(value.ContractVersion).Reverse().ToArray(), value.ContractChecksum.ToArray(),
        value.SubjectId.ToUtf8Bytes(), Encoding.UTF8.GetBytes(value.AuthorityEpoch.ToBase64Url()),
        Encoding.UTF8.GetBytes(value.Incarnation.ToBase64Url()), value.ScopeBindingId.ToArray());

    private static byte[] Hash(string purpose, params byte[][] fields)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(purpose));
        byte[] length = new byte[4];
        foreach (byte[] field in fields)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, field.Length);
            hash.AppendData(length); hash.AppendData(field);
        }
        return hash.GetHashAndReset();
    }

    public async ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ResolveAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(generatedIdentity);
        ArgumentNullException.ThrowIfNull(identity);
        OperationResult<BasePolicyEvaluation> disclosure = await policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = session.Principal,
            Operation = session.Operation(BaseOperationKind.ModuleMutation, definition.Id),
            Collection = PolicyResource(definition),
            ResourceKind = PolicyResourceKind.ModuleMutation,
        }, cancellationToken).ConfigureAwait(false);
        if (!disclosure.IsSuccess() || disclosure.Value?.Authority is null)
            return Failure<TResult>(OperationStatus.NotFound, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.NotFound);
        CollectionDefinition[] authorityCollections;
        try
        {
            authorityCollections = definition.SystemCollectionIds
                .Select(id => collections.Collections.Values.Single(value => string.Equals(value.Id, id, StringComparison.Ordinal)))
                .ToArray();
        }
        catch { return Failure<TResult>(OperationStatus.NotFound, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.NotFound); }
        RecordStoreRegistration? receiptRegistration = ResolveOneRegistration(authorityCollections);
        IAtomicRecordStore? store = receiptRegistration?.AtomicExecutionStore ?? receiptRegistration?.Store as IAtomicRecordStore;
        if (store is null || !BaseModuleMutationCapabilityContract.Supports(definition.Limits, store.Capabilities.ModuleMutation))
            return Failure<TResult>(OperationStatus.NotFound, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.NotFound);
        var resolver = new BaseModuleMutationReceiptResolver<TResult>(
            definition, generatedIdentity.ResultTypeInfo, generatedIdentity.ResultBindings,
            session.Principal, session.Operation(BaseOperationKind.ModuleMutation, definition.Id), policy);
        RecordMutationExecutionResult resolution;
        try
        {
            resolution = await store.ResolveAtomicReceiptAsync(
                resolver, identity, definition.Limits.Deadlines.ReceiptResolutionTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch { return Failure<TResult>(OperationStatus.NotFound, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.NotFound); }
        if (resolution.Outcome != RecordMutationExecutionOutcome.Committed || resolver.Result is null)
            return Failure<TResult>(OperationStatus.NotFound, BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.NotFound);
        if (resolver.SemanticReceipt?.RecoveryPublication is not null
            && !await FinalizeStoredSemanticRecoveryAsync(receiptRegistration!.StoreId, resolver.SemanticReceipt,
                resolution.ReceiptAuthority, identity, cancellationToken).ConfigureAwait(false))
            return Failure<TResult>(OperationStatus.StoreError, BaseSemanticActivationErrorCodes.ExternalPublicationPending, ErrorCategory.Store);
        return new BaseSuccess<BaseModuleMutationExecutionResult<TResult>>(resolver.Result, OperationStatus.Ok, null, null, null, null);
    }

    private IAtomicRecordStore? ResolveOneStore(CollectionDefinition[] authorityCollections)
    {
        RecordStoreRegistration? registration = ResolveOneRegistration(authorityCollections);
        return registration?.AtomicExecutionStore ?? registration?.Store as IAtomicRecordStore;
    }

    private RecordStoreRegistration? ResolveOneRegistration(CollectionDefinition[] authorityCollections)
    {
        RecordStoreRegistration[] registrations = authorityCollections.Length == 0
            ? stores.GetRegistrations()
            : authorityCollections.Select(value => stores.GetRegistrationForCollection(value.Id)).Where(static value => value is not null).Cast<RecordStoreRegistration>().DistinctBy(static value => value.StoreId).ToArray();
        return registrations.Length == 1 ? registrations[0] : null;
    }

    private async ValueTask<bool> AuthorizeDeclaredAuthorityAsync(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        OperationContext moduleOperation,
        CancellationToken cancellationToken)
    {
        foreach (BaseModuleSystemSourceGrant sourceGrant in definition.SystemSourceGrants)
        {
            string collectionId = sourceGrant.CollectionId;
            if (!collections.Collections.TryGetValue(collectionId, out CollectionDefinition? collection)) return false;
            OperationContext sourceOperation = moduleOperation with { CollectionId = collection.Id };
            OperationResult<BasePolicyEvaluation> source = await policy.EvaluateWriteAsync(new BasePolicyRequest
            {
                Principal = session.Principal,
                Operation = sourceOperation,
                Collection = collection,
                ResourceKind = PolicyResourceKind.ModuleMutation,
            }, cancellationToken).ConfigureAwait(false);
            if (!BaseSystemCollectionGate.HasExactModuleSourceGrant(source, sourceGrant.GrantId,
                    definition.OwningModuleId, session.Principal, sourceOperation, collection.Id)) return false;
        }

        foreach (string contractId in definition.ImportedSubjectContractIds)
        {
            BaseGeneratedSubjectRegistration[] matches = subjects.All
                .Where(value => string.Equals(value.Definition.Id, contractId, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1) return false;
            BaseGeneratedSubjectRegistration registration = matches[0];
            OperationResult<BasePolicyEvaluation> imported = await policy.EvaluateWriteAsync(new BasePolicyRequest
            {
                Principal = session.Principal,
                Operation = moduleOperation with
                {
                    Operation = BaseOperationKind.SubjectValidate,
                    CollectionId = registration.Definition.Id,
                    RecordId = null,
                    Mode = OperationMode.System,
                },
                Collection = new CollectionDefinition
                {
                    Id = registration.Definition.Id,
                    Name = "Exported logical subject contract",
                    Kind = BaseCollectionKinds.Custom,
                    SchemaMode = SchemaMode.Strict,
                    UnknownFields = UnknownFieldPolicy.Reject,
                    System = true,
                    SystemOwnerModuleId = registration.Definition.OwningModuleId,
                },
                ResourceKind = PolicyResourceKind.SubjectContract,
                SubjectContractId = registration.Definition.Id,
                SubjectContractVersion = registration.Definition.Version,
            }, cancellationToken).ConfigureAwait(false);
            if (!BaseSystemCollectionGate.HasExactGrant(imported, registration.Definition.ValidationGrantId)) return false;
        }
        return true;
    }

    private CollectionDefinition PolicyResource(BaseRegisteredModuleMutationDefinition definition) =>
        definition.SystemCollectionIds.Length > 0
        && collections.Collections.TryGetValue(definition.SystemCollectionIds[0], out CollectionDefinition? installed)
            ? installed
            : new CollectionDefinition
            {
                Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, System = true,
                SystemOwnerModuleId = definition.OwningModuleId,
            };

    private static BaseModuleMutationCaptureExtension BuildCaptureExtension<TRequest, TResult>(
        BaseRegisteredModuleMutationDefinition definition,
        BaseModuleProgramEvaluator<TRequest, TResult> evaluator,
        BaseSession session,
        BaseModuleMutationRegistry registry,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        byte[] requestBytes,
        out int disabledCaptures)
    {
        var records = ImmutableArray.CreateBuilder<BaseModuleRecordCaptureRequest>();
        var generations = ImmutableArray.CreateBuilder<BaseModuleGenerationCaptureRequest>();
        var relations = ImmutableArray.CreateBuilder<BaseModuleRelationTargetCaptureRequest>();
        disabledCaptures = 0;
        foreach (BaseModuleCapture capture in definition.Template.Captures.OrderBy(static value => value.Id, StringComparer.Ordinal))
        {
            if (capture.EnableGuardId is not null && !evaluator.Guard(capture.EnableGuardId))
            {
                disabledCaptures = checked(disabledCaptures + 1);
                continue;
            }
            if (capture is BaseModuleRecordCapture record)
            {
                BaseModuleProgramValue id = evaluator.Evaluate(record.RecordId);
                records.Add(new BaseModuleRecordCaptureRequest
                {
                    Ordinal = records.Count, CaptureId = record.Id, Collection = collections[record.CollectionId],
                    RecordId = RecordId.Create(id.Value.GetString() ?? throw new InvalidOperationException()), Presence = record.Presence,
                });
            }
            else if (capture is BaseModuleGenerationCapture generation)
            {
                BaseModuleGenerationCellDefinition cell = registry.FindCell(generation.CellId) ?? throw new InvalidOperationException();
                BaseModuleProgramValue key = generation.Key is null
                    ? BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.HostConstant)
                    : evaluator.Evaluate(generation.Key);
                OperationContext operation = session.Operation(BaseOperationKind.ModuleMutation, definition.Id);
                generations.Add(new BaseModuleGenerationCaptureRequest
                {
                    Ordinal = generations.Count, CaptureId = generation.Id, Cell = cell,
                    Scope = new BaseModuleGenerationScopeAuthority
                    {
                        Kind = cell.Scope,
                        Tenant = cell.Scope is BaseModuleGenerationScope.Tenant or BaseModuleGenerationScope.TenantAndKey ? operation.TenantId : null,
                        Project = cell.Scope is BaseModuleGenerationScope.Project or BaseModuleGenerationScope.ProjectAndKey ? operation.ProjectId : null,
                    },
                    KeyUtf8 = key.Present ? Encoding.UTF8.GetBytes(key.Value.GetString() ?? throw new InvalidOperationException()).ToImmutableArray() : [],
                    Absence = generation.Absence,
                });
            }
        }
        foreach (BaseModuleStatement statement in EnumerateActiveStatements(definition.Template.Body, evaluator))
        {
            string? collectionId = statement switch
            {
                BaseModuleCreateStatement value => value.CollectionId,
                BaseModulePatchStatement value => value.CollectionId,
                BaseModuleReplaceStatement value => value.CollectionId,
                BaseModuleUpsertStatement value => value.CollectionId,
                _ => null,
            };
            if (collectionId is null || !collections.TryGetValue(collectionId, out CollectionDefinition? collection)) continue;
            IEnumerable<BaseModuleObjectExpression> payloads = statement switch
            {
                BaseModuleCreateStatement value => [value.Payload],
                BaseModulePatchStatement value => [value.Patch],
                BaseModuleReplaceStatement value => [value.Payload],
                BaseModuleUpsertStatement value => [value.Create, value.Update],
                _ => [],
            };
            foreach (BaseModuleObjectExpression payload in payloads)
            foreach (BaseModuleObjectPropertyExpression property in payload.Properties)
            {
                FieldDefinition? field = collection.Fields?.SingleOrDefault(value => string.Equals(value.Id, property.StablePropertyId, StringComparison.Ordinal));
                if (field?.Relation is not { OwningSide: BaseRelationOwningSide.Source } relation) continue;
                BaseModuleProgramValue target = evaluator.Evaluate(property.Value);
                if (target.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                IEnumerable<string> ids = target.Value.ValueKind == JsonValueKind.Array
                    ? target.Value.EnumerateArray().Select(static value => value.GetString() ?? throw new InvalidOperationException()).ToArray()
                    : [target.Value.GetString() ?? throw new InvalidOperationException()];
                foreach (string id in ids)
                {
                    if (relations.Any(value => string.Equals(value.SourceStatementId, statement.Id, StringComparison.Ordinal)
                        && string.Equals(value.SourceFieldId, field.Id, StringComparison.Ordinal)
                        && string.Equals(value.TargetCollection.Id, relation.TargetCollectionId, StringComparison.Ordinal)
                        && value.TargetRecordId == RecordId.Create(id))) continue;
                    relations.Add(new BaseModuleRelationTargetCaptureRequest
                    {
                        Ordinal = relations.Count, SourceStatementId = statement.Id, SourceFieldId = field.Id,
                        TargetCollection = collections[relation.TargetCollectionId], TargetRecordId = RecordId.Create(id),
                    });
                }
            }
        }
        return new BaseModuleMutationCaptureExtension
        {
            OperationId = definition.Id, OperationVersion = definition.Version,
            OperationChecksum = Convert.ToHexString(definition.Checksum.ToArray()).ToLowerInvariant(),
            RequestDigest = Convert.ToHexString(SHA256.HashData(requestBytes)).ToLowerInvariant(),
            Records = records.ToImmutable(), RelationTargets = relations.ToImmutable(), Generations = generations.ToImmutable(),
        };
    }

    private static IEnumerable<BaseModuleStatement> EnumerateStatements(BaseModuleMutationBlock block)
    {
        foreach (BaseModuleStatement statement in block.Statements)
        {
            yield return statement;
            if (statement is not BaseModuleIfStatement branch) continue;
            foreach (BaseModuleStatement nested in EnumerateStatements(branch.WhenTrue)) yield return nested;
            foreach (BaseModuleStatement nested in EnumerateStatements(branch.WhenFalse)) yield return nested;
        }
    }

    private static IEnumerable<BaseModuleStatement> EnumerateActiveStatements<TRequest, TResult>(
        BaseModuleMutationBlock block,
        BaseModuleProgramEvaluator<TRequest, TResult> evaluator)
    {
        foreach (BaseModuleStatement statement in block.Statements)
        {
            if (statement is BaseModuleIfStatement branch)
            {
                BaseModuleMutationBlock selected = evaluator.Guard(branch.GuardId)
                    ? branch.WhenTrue
                    : branch.WhenFalse;
                foreach (BaseModuleStatement child in EnumerateActiveStatements(selected, evaluator))
                    yield return child;
                continue;
            }

            yield return statement;
        }
    }

    private static HashSet<string> ReachableGuardIds(
        BaseModuleMutationTemplate template,
        IReadOnlyDictionary<string, BaseModuleGuard> guards)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (BaseModuleCapture capture in template.Captures)
        {
            Add(capture.EnableGuardId);
            foreach (BaseModuleValueExpression expression in (IEnumerable<BaseModuleValueExpression>)(capture switch
            {
                BaseModuleRecordCapture record => [record.RecordId],
                BaseModuleGenerationCapture { Key: { } key } => [key],
                _ => [],
            }))
                AddExpression(expression);
        }
        foreach (BaseModulePrecondition precondition in template.Preconditions) Add(precondition.GuardId);
        AddBlock(template.Body);
        AddExpression(template.Result.Value);
        return reachable;

        void Add(string? id)
        {
            if (id is null || !reachable.Add(id) || !guards.TryGetValue(id, out BaseModuleGuard? guard)) return;
            switch (guard)
            {
                case BaseModuleLogicalGuard logical:
                    foreach (string child in logical.ChildGuardIds) Add(child);
                    break;
                case BaseModuleSetGuard set:
                    AddSet(set.Left);
                    if (set.Right is { } right) AddSet(right);
                    break;
            }
            foreach (BaseModuleValueExpression expression in (IEnumerable<BaseModuleValueExpression>)(guard switch
            {
                BaseModuleRevisionEqualsGuard value => [value.Expected],
                BaseModuleFieldEqualsGuard value => [value.Expected],
                BaseModuleFieldComparisonGuard value => [value.Expected],
                BaseModuleGenerationGuard { Expected: { } expected } => [expected],
                BaseModuleValueEqualsGuard value => [value.Left, value.Right],
                BaseModuleValueComparisonGuard value => [value.Left, value.Right],
                BaseModuleValuePresenceGuard value => [value.Value],
                _ => [],
            }))
                AddExpression(expression);
        }

        void AddSet(BaseModuleStaticSet set)
        {
            foreach (BaseModuleStaticSetMember member in set.Members)
            {
                Add(member.EnableGuardId);
                AddExpression(member.Value);
            }
        }

        void AddBlock(BaseModuleMutationBlock block)
        {
            foreach (BaseModuleStatement statement in block.Statements)
            {
                switch (statement)
                {
                    case BaseModuleIfStatement branch:
                        Add(branch.GuardId); AddBlock(branch.WhenTrue); AddBlock(branch.WhenFalse); break;
                    case BaseModuleRequireStatement require: Add(require.GuardId); break;
                    case BaseModuleCreateStatement create: AddExpression(create.RecordId); AddExpression(create.Payload); break;
                    case BaseModulePatchStatement patch:
                        AddExpression(patch.RecordId); AddExpression(patch.Patch); AddExpression(patch.ExpectedRevision); break;
                    case BaseModuleReplaceStatement replace:
                        AddExpression(replace.RecordId); AddExpression(replace.Payload); AddExpression(replace.ExpectedRevision); break;
                    case BaseModuleDeleteStatement delete:
                        AddExpression(delete.RecordId); AddExpression(delete.ExpectedRevision); break;
                    case BaseModuleUpsertStatement upsert:
                        AddExpression(upsert.RecordId); AddExpression(upsert.Create); AddExpression(upsert.Update);
                        AddExpression(upsert.ExpectedRevision); break;
                }
            }
        }

        void AddExpression(BaseModuleValueExpression? expression)
        {
            if (expression is null) return;
            switch (expression)
            {
                case BaseModuleConditionalExpression conditional:
                    Add(conditional.GuardId); AddExpression(conditional.WhenTrue); AddExpression(conditional.WhenFalse); break;
                case BaseModuleCoalesceExpression coalesce:
                    foreach (BaseModuleValueExpression value in coalesce.Values) AddExpression(value); break;
                case BaseModuleBinaryNumericExpression numeric:
                    AddExpression(numeric.Left); AddExpression(numeric.Right); break;
                case BaseModuleRecordIdConversionExpression conversion: AddExpression(conversion.Source); break;
                case BaseModuleGenerationKeyFromGuidExpression conversion: AddExpression(conversion.Source); break;
                case BaseModulePresenceLiftExpression lift: AddExpression(lift.Source); break;
                case BaseModuleIncarnationBytesExpression conversion: AddExpression(conversion.Source); break;
                case BaseModuleSha256HexStringIdentityExpression identity: AddExpression(identity.Source); break;
                case BaseModuleObjectExpression value:
                    foreach (BaseModuleObjectPropertyExpression property in value.Properties) AddExpression(property.Value); break;
            }
        }
    }

    internal static BaseAtomicMutationExecutionLimits ResolveExecutionLimits(BaseModuleMutationLimits value) => new()
    {
        MaximumItems = value.MaximumRecordMutations, MaximumQueryNodes = 0, MaximumQueryDepth = 0,
        MaximumLiteralValues = 0, MaximumSelectedRecords = 0, MaximumProducedMutations = value.MaximumRecordMutations,
        MaximumQueryExecutions = 0, MaximumPreviousStateRequirements = 0, MaximumRecordCaptures = value.MaximumRecordCaptures,
        MaximumRelationTargetCaptures = value.MaximumRelationTargetCaptures, MaximumGenerationReads = value.MaximumGenerationReads,
        MaximumGenerationComparisons = value.MaximumGenerationComparisons, MaximumGenerationIncrements = value.MaximumGenerationIncrements,
        MaximumGuardNodes = value.MaximumGuardNodes, MaximumGuardDepth = value.MaximumGuardDepth,
        MaximumStatements = value.MaximumStatements, MaximumBranches = value.MaximumBranches,
        MaximumExpressionNodes = value.MaximumExpressionNodes, MaximumSelectedBytes = value.MaximumSelectedBytes,
        MaximumEvidenceBytes = value.MaximumEvidenceBytes, MaximumTransientBytes = value.MaximumTransientBytes,
        MaximumReadIntervals = value.MaximumReadIntervals, MaximumSubjectValidations = value.MaximumSubjectValidations,
        MaximumAuthorityReads = value.MaximumAuthorityReads, MaximumRelationChecks = value.MaximumRelationChecks,
        MaximumUniqueConstraintChecks = value.MaximumUniqueConstraintChecks, MaximumRequestBytes = value.MaximumRequestBytes,
        MaximumRetirementProjections = value.MaximumRecordMutations, MaximumRetirementBarrierReads = value.MaximumRecordMutations,
        MaximumRetirementAcknowledgementReads = 1, MaximumRetirementPublications = value.MaximumRecordMutations,
        MaximumGenerationBytes = value.MaximumGenerationBytes, MaximumWrittenBytes = value.MaximumWrittenBytes,
        MaximumFactBytes = value.MaximumFactBytes, MaximumJournalBytes = value.MaximumJournalBytes,
        MaximumReceiptBytes = value.MaximumReceiptBytes, MaximumResultBytes = value.MaximumResultBytes,
        MaximumRetirementEvidenceBytes = value.MaximumEvidenceBytes, MaximumRetirementPublicationBytes = value.MaximumFactBytes,
        Deadlines = value.Deadlines with { },
    };

    private static bool AudienceAllowed(BaseSession session, BaseRegisteredModuleMutationDefinition definition) =>
        definition.Audience switch
        {
            BaseModuleMutationAudience.Service => session.Principal.AuthenticationState is PrincipalAuthenticationState.Service or PrincipalAuthenticationState.System,
            BaseModuleMutationAudience.System => session.Principal.AuthenticationState == PrincipalAuthenticationState.System,
            _ => false,
        };

    internal static byte[] CanonicalRequest(ReadOnlySpan<byte> json, long maximumBytes)
    {
        int maximum = checked((int)Math.Min(maximumBytes, int.MaxValue));
        return BaseCanonicalJson.Canonicalize(json, new BaseCanonicalJsonLimits
        {
            MaximumCanonicalBytes = maximum,
            MaximumDepth = 64,
            MaximumTotalNodes = 65_536,
            MaximumTotalStringUtf8Bytes = maximum,
            MaximumTotalNameUtf8Bytes = maximum,
            MaximumArrayItemsPerContainer = 16_384,
            MaximumObjectPropertiesPerContainer = 16_384,
        });
    }

    private static BaseFailure<BaseModuleMutationExecutionResult<TResult>> Failure<TResult>(OperationStatus status, string code, ErrorCategory category) =>
        Failure<TResult>(status, Error(code, category));
    private static BaseFailure<BaseModuleMutationExecutionResult<TResult>> Failure<TResult>(OperationStatus status, BaseError error) =>
        new(status, error, null, null);
    private static BaseError Error(string code, ErrorCategory category) => new() { Code = code,
        Message = code == BaseModuleMutationErrorCodes.ProviderContractInvalid
            ? "The module mutation provider returned invalid evidence."
            : "The registered module mutation could not be completed.", Category = category };
    private static BaseError NormalizeProviderExecutionError(BaseError? error) => error?.Code switch
    {
        BaseMutationErrorCodes.TransactionTimeout => Error(BaseMutationErrorCodes.TransactionTimeout, ErrorCategory.Store),
        BaseMutationErrorCodes.TransactionConflict or BaseMutationErrorCodes.RevisionConflict =>
            Error(BaseModuleMutationErrorCodes.GenerationConflict, ErrorCategory.Conflict),
        BaseMutationRequestErrorCodes.FingerprintConflict =>
            Error(BaseMutationRequestErrorCodes.FingerprintConflict, ErrorCategory.Conflict),
        BaseModuleMutationErrorCodes.ProviderContractInvalid =>
            Error(BaseModuleMutationErrorCodes.ProviderContractInvalid, ErrorCategory.Store),
        _ => Error(BaseModuleMutationErrorCodes.StoreError, ErrorCategory.Store),
    };
    private static string Digest(params string[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', values)))).ToLowerInvariant();
}

internal sealed class BaseModuleMutationReceiptResolver<TResult>(
    BaseRegisteredModuleMutationDefinition definition,
    System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultTypeInfo,
    IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> resultBindings,
    PrincipalContext principal,
    OperationContext operation,
    IBasePolicyOrchestrator policy) : IAtomicMutationProcessor
{
    internal BaseModuleMutationExecutionResult<TResult>? Result { get; private set; }
    internal BaseSemanticActivationReceiptEvidence? SemanticReceipt { get; private set; }
    internal ImmutableArray<byte> OuterReceiptChecksum { get; private set; }

    public ValueTask<AtomicMutationProcessingResult> ProcessAsync(IAtomicRecordSession session, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Failed());

    public async ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseAtomicReceiptResult committedResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BaseModuleMutationReceiptResult? module = committedResult.ModuleMutation;
        if (committedResult.Kind != BaseAtomicReceiptResultKind.ModuleMutation || module is null
            || !string.Equals(module.OperationId, definition.Id, StringComparison.Ordinal)
            || module.OperationVersion != definition.Version)
            return Failed();
        if (!await BaseModuleReceiptDisclosure.AuthorizeAsync(
                committedResult, definition, resultBindings, principal, operation, policy, cancellationToken).ConfigureAwait(false))
            return Failed();
        try
        {
            BaseModuleProgramEvaluator<object, TResult>.ValidateDto(
                module.CanonicalResultBytes.AsSpan(), resultBindings, providerInfluenced: true);
            TResult? typed = JsonSerializer.Deserialize(module.CanonicalResultBytes.AsSpan(), resultTypeInfo);
            if (typed is null) return Failed();
            Result = new BaseModuleMutationExecutionResult<TResult>
            {
                Disposition = BaseMutationRequestDisposition.Duplicate,
                Outcome = BaseModuleMutationOutcome.Duplicate,
                Result = typed,
            };
            SemanticReceipt = module.SemanticActivation;
            OuterReceiptChecksum = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                BaseAtomicReceiptWire.From(committedResult), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire)).ToImmutableArray();
            return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, committedResult);
        }
        catch (BaseModuleScalarContractException) { return Failed(BaseModuleMutationErrorCodes.ProviderContractInvalid, ErrorCategory.Store); }
        catch { return Failed(); }
    }

    private static AtomicMutationProcessingResult Failed() => Failed(BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization);
    private static AtomicMutationProcessingResult Failed(string code, ErrorCategory category) => new(
        AtomicMutationProcessingOutcome.Failed,
        [],
        new BaseError
        {
            Code = code,
            Message = code == BaseModuleMutationErrorCodes.ProviderContractInvalid
                ? "The module mutation provider returned invalid evidence."
                : "The stored module mutation receipt cannot be resolved.",
            Category = category,
        });
}

internal static class BaseModuleReceiptDisclosure
{
    internal static async ValueTask<bool> AuthorizeAsync(
        BaseAtomicReceiptResult committedResult,
        BaseRegisteredModuleMutationDefinition definition,
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> resultBindings,
        PrincipalContext principal,
        OperationContext operation,
        IBasePolicyOrchestrator policy,
        CancellationToken cancellationToken)
    {
        foreach (BaseOwnedMutationFact owned in committedResult.Mutations)
        {
            BaseRecordMutationFact fact;
            try { fact = owned.MaterializeOwned(); }
            catch { return false; }
            RecordEnvelope? resource = fact.After ?? fact.Before;
            if (resource is null || !definition.SystemCollectionIds.Contains(fact.Collection.Id, StringComparer.Ordinal)) return false;
            OperationResult<BasePolicyEvaluation> disclosure = await policy.EvaluateReadAsync(new BasePolicyRequest
            {
                Principal = principal,
                Operation = operation with { CollectionId = fact.Collection.Id, RecordId = resource.Id.Value },
                Collection = fact.Collection,
                ResourceKind = PolicyResourceKind.ModuleMutation,
                ExistingRecord = resource,
                RecordId = resource.Id,
            }, cancellationToken).ConfigureAwait(false);
            BaseModuleSystemSourceGrant? sourceGrant = definition.SystemSourceGrants
                .SingleOrDefault(value => string.Equals(value.CollectionId, fact.Collection.Id, StringComparison.Ordinal));
            OperationContext sourceOperation = operation with { CollectionId = fact.Collection.Id, RecordId = resource.Id.Value };
            if (!disclosure.IsSuccess() || disclosure.Value is null || sourceGrant is null
                || !BaseSystemCollectionGate.HasExactModuleSourceGrant(disclosure, sourceGrant.GrantId,
                    definition.OwningModuleId, principal, sourceOperation, fact.Collection.Id)
                || !BaseRecordFilterMatcher.Matches(resource, disclosure.Value.EffectiveRecordFilter)) return false;
        }

        OperationResult<BasePolicyEvaluation> result = await policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                System = true, SystemOwnerModuleId = definition.OwningModuleId,
            },
            ResourceKind = PolicyResourceKind.ModuleMutation,
        }, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess() && result.Value is not null
            && BaseSystemCollectionGate.HasExactModuleGrant(result, definition.GrantId,
                definition.OwningModuleId, principal, operation)
            && ResultDisclosureAllows(result.Value.EffectiveReadMask, resultBindings.Values);
    }

    private static bool ResultDisclosureAllows(FieldMask? mask, IEnumerable<BaseModuleDtoPropertyBinding> bindings)
    {
        BaseModuleDtoPropertyBinding[] declared = bindings.ToArray();
        if (declared.Any(binding => binding.RecordDisclosure != BaseRecordDisclosure.Include))
            return false;
        string[] values = declared.Select(static binding => binding.StablePropertyId).ToArray();
        return mask?.Mode switch
        {
            null or FieldMaskMode.Unspecified or FieldMaskMode.AllowAll => true,
            FieldMaskMode.DenyAll => values.Length == 0,
            FieldMaskMode.IncludeOnly => values.All(value => (mask.Include ?? []).Contains(value, StringComparer.Ordinal)),
            FieldMaskMode.Exclude => values.All(value => !(mask.Exclude ?? []).Contains(value, StringComparer.Ordinal)),
            _ => false,
        };
    }
}
