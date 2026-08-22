using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

internal sealed class DefaultHPDBaseAdministration(
    IServiceProvider services,
    IRecordStoreRegistry stores,
    IBasePolicyOrchestrator policy,
    BaseSubjectContractRegistry subjects,
    BaseSubjectLifecycleInspectionAuthorityRegistry lifecycleInspectionAuthorities,
    BaseActivationRegistry activations,
    BaseScheduleRecoveryKeyRegistry scheduleRecoveryKeys,
    BaseActivationAcceptedTimeAuthority activationTime,
    BaseActivationProviderExecutionGate activationProviderGate,
    BaseSubjectControlOperationalState subjectControlState,
    HPDBaseInstalledFeatures features,
    TimeProvider timeProvider) : IHPDBaseAdministration
{
    public BaseAdministrationCapability Capability =>
        stores.GetRegistrations().Select(static registration => registration.Store).OfType<IRecordStoreAdministration>().ToArray() is [{ } administration]
            ? administration.AdministrationCapability
            : UnavailableCapability;

    public async ValueTask<BaseResult<BaseBackupManifest>> CreateBackupAsync(Stream destination, BaseBackupRequest request, CancellationToken cancellationToken = default) =>
        await RouteAsync(request.StoreId, request.Principal, BaseOperationKind.AdminBackup,
            administration => administration.CreateBackupAsync(destination, request, cancellationToken), cancellationToken).ConfigureAwait(false);

    public async ValueTask<BaseResult<BaseBackupManifest>> ValidateBackupAsync(Stream source, BaseBackupValidationRequest request, CancellationToken cancellationToken = default) =>
        await RouteAsync(request.StoreId, request.Principal, BaseOperationKind.AdminBackup,
            administration => administration.ValidateBackupAsync(source, request, cancellationToken), cancellationToken).ConfigureAwait(false);

    public async ValueTask<BaseResult<BaseRestoreResult>> RestoreAsync(Stream source, BaseRestoreRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        BaseRestoreRequest authorized = request with
        {
            RecoveryApplicationId = features.LogicalSchema.ApplicationId,
            RecoveryVerificationKeys = scheduleRecoveryKeys.Keys,
            RecoveryAcceptedNow = timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
        };
        BaseResult<BaseRestoreResult> result = await RouteAsync(request.StoreId, request.Principal, BaseOperationKind.AdminRestore,
            administration => administration.RestoreAsync(source, authorized, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (result is BaseSuccess<BaseRestoreResult>)
        {
            try { await services.GetRequiredService<BaseSubjectControlDispatcher>().ReconcileAsync(cancellationToken).ConfigureAwait(false); }
            catch when (!cancellationToken.IsCancellationRequested) { }
        }
        return result;
    }

    public async ValueTask<BaseResult<BasePurgeResult>> PurgeAsync(BasePurgeRequest request, CancellationToken cancellationToken = default) =>
        BaseResultMapper.Map<BasePurgeResult, BasePurgeResult>(
            await services.GetRequiredService<IBaseMutationCoordinator>().ExecutePurgeAsync(request, cancellationToken).ConfigureAwait(false),
            static value => value);

    public async ValueTask<BaseResult<BaseVectorRebuildResult>> RebuildVectorIndexAsync(BaseVectorRebuildRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        IBaseVectorRebuildService? vector = services.GetService<IBaseVectorRebuildService>();
        if (vector is null) return await Unsupported<BaseVectorRebuildResult>(cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map<BaseVectorRebuildResult, BaseVectorRebuildResult>(await vector.RebuildAsync(request, cancellationToken).ConfigureAwait(false), static value => value);
    }

    public async ValueTask<BaseResult<BaseSubjectEpochRotationResult>> RotateSubjectEpochAsync(
        string storeId,
        PrincipalContext principal,
        BaseSubjectEpochRotationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);
        if (!subjectControlState.AdmitsRotation)
            return new BaseFailure<BaseSubjectEpochRotationResult>(
                OperationStatus.CapabilityUnavailable,
                BaseSubjectFailureContract.Error(BaseSubjectErrorCodes.ValidationUnavailable),
                null,
                null);
        BaseResult<BaseSubjectEpochRotationResult> result = await RouteSubjectAsync(
            storeId,
            principal,
            request,
            administration => administration.RotateEpochAsync(request, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (result is BaseSuccess<BaseSubjectEpochRotationResult>)
        {
            try { await services.GetRequiredService<BaseSubjectControlDispatcher>().ReconcileAsync(cancellationToken).ConfigureAwait(false); }
            catch when (!cancellationToken.IsCancellationRequested) { }
        }
        return result;
    }

    public async ValueTask<BaseResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteSubjectAuthorityMaintenanceAsync(
        string storeId,
        PrincipalContext principal,
        BaseSubjectAuthorityMaintenanceExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(storeId) || stores.GetRegistration(storeId)?.Store is not IBaseSubjectAuthorityMaintenanceStore store)
            return await Unsupported<BaseSubjectLifecycleMaintenanceResult>(cancellationToken).ConfigureAwait(false);

        const string rotationGrant = "base.subjectLifecycle.scope.rotate";
        BaseGeneratedSubjectRegistration? target = request.Lifecycle.ContractId is null || request.Lifecycle.ContractVersion is null
            ? null
            : subjects.Find(request.Lifecycle.ContractId, request.Lifecycle.ContractVersion.Value);
        if (request.Lifecycle.Kind != BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection && target is null)
            return new BaseFailure<BaseSubjectLifecycleMaintenanceResult>(OperationStatus.ValidationFailed,
                new BaseError { Code = BaseSubjectErrorCodes.LifecycleContractInvalid, Message = "The subject lifecycle contract is invalid.", Category = ErrorCategory.Validation }, null, null);

        string action = request.Lifecycle.Kind == BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection
            ? rotationGrant
            : target!.Definition.AdministrationGrantId;
        string owner = request.Lifecycle.Kind == BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection
            ? "base"
            : target!.Definition.OwningModuleId;
        var operation = new OperationContext
        {
            ApplicationId = features.LogicalSchema.ApplicationId,
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.SubjectLifecycleMaintenance,
            CollectionId = action,
            TenantId = principal.CurrentTenantId,
            Mode = OperationMode.System,
            Now = timeProvider.GetUtcNow(),
        };
        var resource = new CollectionDefinition
        {
            Id = action,
            Name = "Subject lifecycle maintenance",
            Kind = BaseCollectionKinds.Custom,
            Exposed = false,
            System = true,
            SystemOwnerModuleId = owner,
            SchemaMode = SchemaMode.Strict,
            UnknownFields = UnknownFieldPolicy.Reject,
            Store = new StoreAnnotation { StoreId = storeId },
        };
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = operation,
            Collection = resource,
            ResourceKind = target is null ? PolicyResourceKind.AdminMetadata : PolicyResourceKind.SubjectLifecycle,
            SubjectContractId = target?.Definition.Id,
            SubjectContractVersion = target?.Definition.Version,
        }, cancellationToken).ConfigureAwait(false);
        bool admitted = target is null
            ? BaseSystemCollectionGate.HasExactModuleGrant(authorization, rotationGrant, "base", principal, operation)
            : BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(authorization, action, owner, action,
                target.Definition.Id, target.Definition.Version, principal, operation);
        if (!admitted)
            return new BaseFailure<BaseSubjectLifecycleMaintenanceResult>(OperationStatus.PolicyDenied,
                new BaseError { Code = BaseSubjectErrorCodes.LifecycleUnauthorized, Message = "The subject lifecycle operation is not authorized.", Category = ErrorCategory.Authorization }, null, null);

        var normalized = request with { CombinedPlanChecksum = BaseSubjectAuthorityMaintenanceProcessor.PlanChecksum(request with { CombinedPlanChecksum = new byte[32] }) };
        var processor = new BaseSubjectAuthorityMaintenanceProcessor();
        RecordMutationExecutionResult execution = await store.ExecuteMaintenanceAsync(processor, normalized, cancellationToken).ConfigureAwait(false);
        if (execution.Outcome == RecordMutationExecutionOutcome.Committed && processor.LifecycleResult is not null)
            return new BaseSuccess<BaseSubjectLifecycleMaintenanceResult>(processor.LifecycleResult, processor.LifecycleResult.Duplicate ? OperationStatus.Ok : OperationStatus.Updated, null, null, null, null);
        BaseError error = execution.Error ?? execution.Processing?.Error ?? new BaseError
        {
            Code = execution.Outcome == RecordMutationExecutionOutcome.Indeterminate ? BaseSubjectErrorCodes.LifecycleCommitIndeterminate : BaseSubjectErrorCodes.LifecycleProviderContractInvalid,
            Message = "The subject lifecycle maintenance operation failed.",
            Category = ErrorCategory.Store,
        };
        return new BaseFailure<BaseSubjectLifecycleMaintenanceResult>(execution.Outcome == RecordMutationExecutionOutcome.Indeterminate ? OperationStatus.StoreError : OperationStatus.Conflict, error, null, null);
    }

    public async ValueTask<BaseResult<BaseSubjectLifecycleInspectionResult>> InspectSubjectLifecycleAsync(
        string storeId,
        PrincipalContext principal,
        BaseSubjectLifecycleInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(storeId) || stores.GetRegistration(storeId)?.Store is not IBaseSubjectLifecycleStore store)
            return await Unsupported<BaseSubjectLifecycleInspectionResult>(cancellationToken).ConfigureAwait(false);
        BaseGeneratedSubjectRegistration? target = subjects.Find(request.ContractId, request.ContractVersion);
        if (target is null || !Enum.IsDefined(request.ScopeMode) || request.MaximumResultBytes is < 1 or > 1_048_576
            || request.Timeout < TimeSpan.FromMilliseconds(100) || request.Timeout > TimeSpan.FromMinutes(2)
            || request.ScopeMode == BaseSubjectScopeQueryMode.ExactScope != (request.ExactScope is not null)
            || request.ScopeMode == BaseSubjectScopeQueryMode.AllAuthorizedScopes && (request.IncludeTerminalReceipt || request.SubjectId is not null))
            return new BaseFailure<BaseSubjectLifecycleInspectionResult>(OperationStatus.ValidationFailed,
                new BaseError { Code = BaseSubjectErrorCodes.LifecycleContractInvalid, Message = "The subject lifecycle inspection request is invalid.", Category = ErrorCategory.Validation }, null, null);

        var operation = new OperationContext
        {
            ApplicationId = features.LogicalSchema.ApplicationId,
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.SubjectLifecycleMaintenance,
            CollectionId = target.Definition.AdministrationGrantId,
            TenantId = principal.CurrentTenantId,
            Mode = OperationMode.System,
            Now = timeProvider.GetUtcNow(),
        };
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = target.Definition.AdministrationGrantId,
                Name = "Subject lifecycle inspection",
                Kind = BaseCollectionKinds.Custom,
                Exposed = false,
                System = true,
                SystemOwnerModuleId = target.Definition.OwningModuleId,
                SchemaMode = SchemaMode.Strict,
                UnknownFields = UnknownFieldPolicy.Reject,
                Store = new StoreAnnotation { StoreId = storeId },
            },
            ResourceKind = PolicyResourceKind.SubjectLifecycle,
            SubjectContractId = target.Definition.Id,
            SubjectContractVersion = target.Definition.Version,
        }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactSubjectLifecycleGrant(authorization, target.Definition.AdministrationGrantId,
                target.Definition.OwningModuleId, target.Definition.AdministrationGrantId, target.Definition.Id,
                target.Definition.Version, principal, operation))
            return new BaseFailure<BaseSubjectLifecycleInspectionResult>(OperationStatus.PolicyDenied,
                new BaseError { Code = BaseSubjectErrorCodes.LifecycleUnauthorized, Message = "The subject lifecycle operation is not authorized.", Category = ErrorCategory.Authorization }, null, null);

        string authorityDigest;
        if (request.ScopeMode == BaseSubjectScopeQueryMode.AllAuthorizedScopes)
        {
            BaseSubjectLifecycleInspectionAuthority? authority = lifecycleInspectionAuthorities.Find(request.ContractId, request.ContractVersion);
            if (authority is null)
                return new BaseFailure<BaseSubjectLifecycleInspectionResult>(OperationStatus.PolicyDenied,
                    new BaseError { Code = BaseSubjectErrorCodes.LifecycleUnauthorized, Message = "The subject lifecycle operation is not authorized.", Category = ErrorCategory.Authorization }, null, null);
            authorityDigest = authority.Digest;
        }
        else authorityDigest = target.Checksum;

        OperationResult<BaseSubjectLifecycleProviderInspection> inspected = await store.InspectAsync(new BaseSubjectLifecycleProviderInspectionRequest
        {
            ContractId = request.ContractId,
            ContractVersion = request.ContractVersion,
            ConsumerId = request.ConsumerId,
            ScopeAuthority = new BaseSubjectScopeQueryAuthority { Mode = request.ScopeMode, ExactScope = request.ExactScope, InstalledAuthorityDigest = authorityDigest },
            SubjectId = request.SubjectId,
            IncludeTerminalReceipt = request.IncludeTerminalReceipt,
            MaximumResultBytes = request.MaximumResultBytes,
            DeadlineUtc = timeProvider.GetUtcNow().Add(request.Timeout),
        }, cancellationToken).ConfigureAwait(false);
        if (!inspected.IsSuccess() || inspected.Value is null)
            return new BaseFailure<BaseSubjectLifecycleInspectionResult>(inspected.Status,
                BaseSubjectFailureContract.NormalizeProviderError(inspected.Status, inspected.Error), null, null);
        BaseSubjectTerminalLifetimeReceipt? terminal = inspected.Value.TerminalReceipt;
        return new BaseSuccess<BaseSubjectLifecycleInspectionResult>(new BaseSubjectLifecycleInspectionResult
        {
            DeliveryEpoch = inspected.Value.DeliveryEpoch,
            EarliestRetained = inspected.Value.EarliestRetained,
            HighWater = inspected.Value.HighWater,
            Consumers = inspected.Value.Consumers.ToArray(),
            TerminalReceipt = terminal is null ? null : new BaseSubjectTerminalLifetimeInspection
            {
                ContractId = terminal.ContractId, ContractVersion = terminal.ContractVersion, SubjectId = terminal.SubjectId,
                RetiredAuthorityEpoch = terminal.RetiredAuthorityEpoch, RetiredIncarnation = terminal.RetiredIncarnation,
                RetiredLifetimeGeneration = terminal.RetiredLifetimeGeneration, RetiredSubjectSequence = terminal.RetiredSubjectSequence,
                RetiredPosition = terminal.RetiredPosition, ContractStateGeneration = terminal.ContractStateGeneration,
                RestoreEpoch = terminal.RestoreEpoch, ReceiptChecksum = terminal.ReceiptChecksum,
            },
        }, OperationStatus.Ok, null, null, null, null);
    }

    public ValueTask<BaseResult<BaseActivationTransitionResult>> CancelActivationAsync(
        BaseActivationAdministrationCancelRequest request,
        CancellationToken cancellationToken = default) =>
        RouteActivationAsync(request, static (definition, accepted, value) => new BaseActivationCancelRequest
        {
            ActivationId = value.ActivationId,
            ExpectedGeneration = value.ExpectedGeneration,
            Propagation = value.Propagation,
            Identity = value.Identity,
            AcceptedTime = accepted,
            Limits = definition.Limits.Provider,
        }, static definition => definition.Grants.Cancel, cancellationToken);

    public ValueTask<BaseResult<BaseActivationTransitionResult>> RetryActivationAsync(
        BaseActivationAdministrationRetryRequest request,
        CancellationToken cancellationToken = default) =>
        RouteActivationAsync(request, static (definition, accepted, value) => new BaseActivationOperatorRetryRequest
        {
            ActivationId = value.ActivationId,
            ExpectedGeneration = value.ExpectedGeneration,
            RetryDueAt = value.DueAt?.ToUnixTimeMilliseconds() ?? accepted.CapturedUtc,
            Identity = value.Identity,
            AcceptedTime = accepted,
            Limits = definition.Limits.Provider,
        }, static definition => definition.Grants.Retry, cancellationToken);

    public ValueTask<BaseResult<BaseActivationTransitionResult>> ReconcileActivationAsync(
        BaseActivationAdministrationReconcileRequest request,
        CancellationToken cancellationToken = default) =>
        RouteActivationAsync(request, static (definition, accepted, value) => new BaseActivationReconcileEffectRequest
        {
            ActivationId = value.ActivationId,
            ExpectedGeneration = value.ExpectedGeneration,
            ExpectedEffectStartGeneration = value.ExpectedEffectStartGeneration,
            ExpectedEffectChecksum = value.ExpectedEffectChecksum,
            Disposition = value.Disposition,
            VerificationEvidence = value.VerificationEvidence,
            VerificationChecksum = value.VerificationChecksum,
            Identity = value.Identity,
            AcceptedTime = accepted,
            Limits = definition.Limits.Provider,
        }, static definition => definition.Grants.Reconcile, cancellationToken);

    public ValueTask<BaseResult<BaseActivationTransitionResult>> DisposeActivationAsync(
        BaseActivationAdministrationDisposeRequest request,
        CancellationToken cancellationToken = default) =>
        RouteActivationAsync(request, static (definition, accepted, value) => new BaseActivationDisposeRequest
        {
            ActivationId = value.ActivationId,
            ExpectedGeneration = value.ExpectedGeneration,
            Identity = value.Identity,
            AcceptedTime = accepted,
            Limits = definition.Limits.Provider,
        }, static definition => definition.Grants.Dispose, cancellationToken);

    public async ValueTask<BaseResult<BaseActivationAdministrationPage>> ReadActivationsAsync(
        BaseActivationAdministrationReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        BaseActivationDefinition? definition = activations.Find(request.DefinitionId, request.DefinitionVersion);
        if (definition is null || stores.GetRegistration(request.StoreId)?.Store is not IBaseActivationProvider provider
            || !BaseActivationCertificationReceiptContract.Validate(provider.Descriptor)
            || request.Take is < 1 or > 256 || !Enum.IsDefined(request.States))
            return ActivationReadFailure(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        var operation = new OperationContext
        {
            ApplicationId = features.LogicalSchema.ApplicationId,
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.AdminInspect,
            CollectionId = definition.Id,
            TenantId = request.Principal.CurrentTenantId,
            Mode = OperationMode.System,
            Now = timeProvider.GetUtcNow(),
        };
        OperationResult<BasePolicyEvaluation> authorized = await policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = request.Principal, Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                System = true, SystemOwnerModuleId = definition.OwningModuleId,
                Store = new StoreAnnotation { StoreId = request.StoreId },
            },
            ResourceKind = PolicyResourceKind.ActivationDefinition,
        }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactActivationGrant(
            authorized, definition.Grants.Inspect, definition.OwningModuleId, request.Principal, operation))
            return ActivationReadFailure(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        BaseOwnedScopeSeekAuthority scope = new()
        {
            Kind = request.Scope.Kind,
            ProtectedIndexDigest = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"base.activation.scope.v2\0{(int)request.Scope.Kind}\n{request.Scope.Value ?? string.Empty}")).ToImmutableArray(),
        };
        BaseActivationAdministrationQueryRequest providerRequest = new()
        {
            ApplicationId = features.LogicalSchema.ApplicationId,
            Scope = scope,
            Definition = new BaseActivationDefinitionKey
            {
                Id = definition.Id, Version = definition.Version,
                Checksum = definition.Checksum.ToArray().ToImmutableArray(),
            },
            States = request.States, After = request.After, Take = request.Take,
            AcceptedTime = activationTime.Capture(features.LogicalSchema.ApplicationId),
            Limits = definition.Limits.Provider,
        };
        BaseActivationProviderCallResult<OperationResult<BaseActivationAdministrationPage>> call =
            await activationProviderGate.ExecuteAsync(
                token => provider.ReadAdministrationAsync(providerRequest, token),
                definition.Limits.Provider.AcquisitionTimeout,
                definition.Limits.Provider.TransactionTimeout,
                cancellationToken).ConfigureAwait(false);
        if (call.Outcome == BaseActivationProviderCallOutcome.Cancelled && cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);
        if (call.Outcome is BaseActivationProviderCallOutcome.TimedOut or BaseActivationProviderCallOutcome.Capacity)
            return ActivationReadFailure(OperationStatus.CapabilityUnavailable, "base.activation.capacityUnavailable", ErrorCategory.Capability);
        if (call.Outcome != BaseActivationProviderCallOutcome.Completed || call.Value is null)
            return ActivationReadFailure(OperationStatus.StoreError, "base.activation.storeError", ErrorCategory.Store);
        OperationResult<BaseActivationAdministrationPage> result = call.Value;
        if (!result.IsSuccess() || result.Value is null)
            return BaseResultMapper.Map<BaseActivationAdministrationPage, BaseActivationAdministrationPage>(result, static value => value);
        BaseActivationAdministrationPage page = result.Value;
        bool valid = page.Items.Length <= request.Take
            && page.Items.Length <= definition.Limits.Provider.MaximumCandidates
            && page.Intervals.Length is 1
            && page.Intervals[0].LogicalAccessPathId == "base.activation.administration.byScopeDefinitionStateDue.v1"
            && page.Accounting.Candidates == page.Items.Length
            && page.Accounting.ReadIntervals == 1
            && page.Accounting.EvidenceBytes <= definition.Limits.Provider.MaximumEvidenceBytes
            && page.Accounting.TransientBytes <= definition.Limits.Provider.MaximumTransientBytes
            && page.Items.All(item => item.Definition.Id == definition.Id
                && item.Definition.Version == definition.Version
                && CryptographicOperations.FixedTimeEquals(item.Definition.Checksum.AsSpan(), definition.Checksum.AsSpan()))
            && CanonicallyOrdered(page.Items)
            && (page.Next is null || page.Items.Length != 0 && BoundaryEquals(page.Next, page.Items[^1]));
        if (!valid)
            return ActivationReadFailure(OperationStatus.StoreError, "base.activation.providerContractInvalid", ErrorCategory.Store);
        return new BaseSuccess<BaseActivationAdministrationPage>(page, OperationStatus.Ok, null, null, null, null);
    }

    public ValueTask<BaseResult<BaseActivationMaintenancePage>> AdvanceActivationMaintenanceAsync(
        BaseActivationAdministrationMaintenanceRequest request, CancellationToken cancellationToken = default) =>
        RouteActivationPageAsync(request, static (provider, definition, scope, accepted, value, token) =>
            provider.AdvanceMaintenanceAsync(new BaseActivationMaintenanceRequest
            {
                ApplicationId = accepted.ApplicationId, Scope = scope,
                Definition = new BaseActivationDefinitionKey { Id = definition.Id, Version = definition.Version, Checksum = definition.Checksum },
                Kind = value.Kind, AfterActivationId = value.AfterActivationId, Take = value.Take,
                AcceptedTime = accepted, Identity = value.Identity, Limits = definition.Limits.Provider,
            }, token), static definition => definition.Grants.Repair, cancellationToken);

    public ValueTask<BaseResult<BaseActivationPrunePage>> PruneActivationsAsync(
        BaseActivationAdministrationPruneRequest request, CancellationToken cancellationToken = default) =>
        RouteActivationPageAsync(request, static (provider, definition, scope, accepted, value, token) =>
            provider.PruneAsync(new BaseActivationPruneRequest
            {
                ApplicationId = accepted.ApplicationId, Scope = scope,
                Definition = new BaseActivationDefinitionKey { Id = definition.Id, Version = definition.Version, Checksum = definition.Checksum },
                AfterActivationId = value.AfterActivationId, Take = value.Take,
                AcceptedTime = accepted, Identity = value.Identity, Limits = definition.Limits.Provider,
            }, token), static definition => definition.Grants.Remove, cancellationToken);

    private async ValueTask<BaseResult<TResult>> RouteActivationPageAsync<TRequest, TResult>(
        TRequest request,
        Func<IBaseActivationProvider, BaseActivationDefinition, BaseOwnedScopeSeekAuthority, BaseAcceptedTimeReceipt, TRequest, CancellationToken, ValueTask<OperationResult<TResult>>> invoke,
        Func<BaseActivationDefinition, string> requiredGrant,
        CancellationToken cancellationToken)
        where TRequest : BaseActivationAdministrationPageRequest
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        BaseActivationDefinition? definition = activations.Find(request.DefinitionId, request.DefinitionVersion);
        if (definition is null || request.Take is < 1 or > 256
            || stores.GetRegistration(request.StoreId)?.Store is not IBaseActivationProvider provider
            || !BaseActivationCertificationReceiptContract.Validate(provider.Descriptor))
            return ActivationPageFailure<TResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        var operation = new OperationContext
        {
            ApplicationId = features.LogicalSchema.ApplicationId, Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.ActivationTransition, CollectionId = definition.Id,
            TenantId = request.Principal.CurrentTenantId, Mode = OperationMode.System, Now = timeProvider.GetUtcNow(),
        };
        OperationResult<BasePolicyEvaluation> authorized = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = request.Principal, Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                System = true, SystemOwnerModuleId = definition.OwningModuleId,
                Store = new StoreAnnotation { StoreId = request.StoreId },
            },
            ResourceKind = PolicyResourceKind.ActivationDefinition,
        }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactActivationGrant(
            authorized, requiredGrant(definition), definition.OwningModuleId, request.Principal, operation))
            return ActivationPageFailure<TResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        var scope = new BaseOwnedScopeSeekAuthority
        {
            Kind = request.Scope.Kind,
            ProtectedIndexDigest = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"base.activation.scope.v2\0{(int)request.Scope.Kind}\n{request.Scope.Value ?? string.Empty}")).ToImmutableArray(),
        };
        BaseActivationProviderCallResult<OperationResult<TResult>> call = await activationProviderGate.ExecuteAsync(
            token => invoke(provider, definition, scope, activationTime.Capture(features.LogicalSchema.ApplicationId), request, token),
            definition.Limits.Provider.AcquisitionTimeout, definition.Limits.Provider.TransactionTimeout, cancellationToken).ConfigureAwait(false);
        if (call.Outcome == BaseActivationProviderCallOutcome.Cancelled && cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);
        if (call.Outcome is BaseActivationProviderCallOutcome.TimedOut or BaseActivationProviderCallOutcome.Capacity)
            return ActivationPageFailure<TResult>(OperationStatus.CapabilityUnavailable, "base.activation.capacityUnavailable", ErrorCategory.Capability);
        if (call.Outcome != BaseActivationProviderCallOutcome.Completed || call.Value is null)
            return ActivationPageFailure<TResult>(OperationStatus.StoreError, "base.activation.storeError", ErrorCategory.Store);
        OperationResult<TResult> result = call.Value;
        if (!result.IsSuccess() || result.Value is null)
            return BaseResultMapper.Map<TResult, TResult>(result, static value => value);
        bool valid = result.Value switch
        {
            BaseActivationMaintenancePage page => ValidateMaintenancePage(page, request.Take, definition.Limits.Provider),
            BaseActivationPrunePage page => ValidatePrunePage(page, request.Take, definition.Limits.Provider),
            _ => false,
        };
        return valid
            ? BaseResultMapper.Map<TResult, TResult>(result, static value => value)
            : ActivationPageFailure<TResult>(OperationStatus.StoreError, "base.activation.providerContractInvalid", ErrorCategory.Store);
    }

    private static bool ValidateMaintenancePage(
        BaseActivationMaintenancePage page, int take, BaseActivationExecutionLimits limits)
    {
        if (page.Items.Length > take || page.Items.Length > limits.MaximumCandidates
            || !AccountingValid(page.Accounting, page.Items.Length, limits)) return false;
        for (int index = 0; index < page.Items.Length; index++)
        {
            BaseActivationMaintenanceItem item = page.Items[index];
            if (string.IsNullOrWhiteSpace(item.ActivationId) || item.PreviousGeneration < 1
                || item.PreviousGeneration == long.MaxValue || item.ResultingGeneration != item.PreviousGeneration + 1
                || item.ControlChecksum.Length != 32
                || index != 0 && string.CompareOrdinal(page.Items[index - 1].ActivationId, item.ActivationId) >= 0)
                return false;
        }
        return page.Completed
            ? page.NextActivationId is null
            : page.Items.Length != 0 && page.NextActivationId == page.Items[^1].ActivationId;
    }

    private static bool ValidatePrunePage(BaseActivationPrunePage page, int take, BaseActivationExecutionLimits limits)
    {
        if (page.ActivationIds.Length > take || page.ActivationIds.Length > limits.MaximumCandidates
            || !AccountingValid(page.Accounting, page.ActivationIds.Length, limits)) return false;
        for (int index = 0; index < page.ActivationIds.Length; index++)
            if (string.IsNullOrWhiteSpace(page.ActivationIds[index])
                || index != 0 && string.CompareOrdinal(page.ActivationIds[index - 1], page.ActivationIds[index]) >= 0)
                return false;
        return page.Completed
            ? page.NextActivationId is null
            : page.ActivationIds.Length != 0 && page.NextActivationId == page.ActivationIds[^1];
    }

    private static bool AccountingValid(BaseActivationAccounting accounting, int candidates, BaseActivationExecutionLimits limits) =>
        accounting.Candidates == candidates && accounting.Comparisons >= 0
        && accounting.IndexOperations >= 0 && accounting.IndexOperations <= limits.MaximumIndexOperations
        && accounting.ReadIntervals >= 0 && accounting.ReadIntervals <= limits.MaximumReadIntervals
        && accounting.EvidenceBytes >= 0 && accounting.EvidenceBytes <= limits.MaximumEvidenceBytes
        && accounting.TransientBytes >= accounting.EvidenceBytes
        && accounting.TransientBytes <= limits.MaximumTransientBytes;

    private static BaseFailure<TResult> ActivationPageFailure<TResult>(OperationStatus status, string code, ErrorCategory category) =>
        new(status, new BaseError { Code = code, Message = "The activation administration request could not be completed.", Category = category }, null, null);

    private static bool CanonicallyOrdered(ImmutableArray<BaseActivationAdministrationItem> items)
    {
        for (int index = 1; index < items.Length; index++)
        {
            BaseActivationAdministrationItem left = items[index - 1];
            BaseActivationAdministrationItem right = items[index];
            int comparison = string.CompareOrdinal(left.Definition.Id, right.Definition.Id);
            if (comparison > 0 || comparison == 0 && (left.Definition.Version > right.Definition.Version
                || left.Definition.Version == right.Definition.Version && (left.EffectiveDueAt > right.EffectiveDueAt
                || left.EffectiveDueAt == right.EffectiveDueAt
                && string.CompareOrdinal(left.ActivationId, right.ActivationId) >= 0))) return false;
        }
        return true;
    }

    private static bool BoundaryEquals(
        BaseActivationAdministrationBoundary boundary,
        BaseActivationAdministrationItem item) =>
        boundary.DefinitionId == item.Definition.Id && boundary.DefinitionVersion == item.Definition.Version
        && boundary.EffectiveDueAt == item.EffectiveDueAt && boundary.ActivationId == item.ActivationId;

    private static BaseFailure<BaseActivationAdministrationPage> ActivationReadFailure(
        OperationStatus status, string code, ErrorCategory category) => new(status, new BaseError
        {
            Code = code, Message = "The activation administration request could not be completed.", Category = category,
        }, null, null);

    private async ValueTask<BaseResult<BaseActivationTransitionResult>> RouteActivationAsync<TRequest>(
        TRequest request,
        Func<BaseActivationDefinition, BaseAcceptedTimeReceipt, TRequest, BaseActivationTransitionRequest> create,
        Func<BaseActivationDefinition, string> requiredGrant,
        CancellationToken cancellationToken)
        where TRequest : BaseActivationAdministrationRequest
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        BaseActivationDefinition? definition = activations.Find(request.DefinitionId, request.DefinitionVersion);
        if (definition is null || stores.GetRegistration(request.StoreId)?.Store is not IBaseActivationProvider provider
            || !BaseActivationCertificationReceiptContract.Validate(provider.Descriptor))
            return ActivationFailure(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        var operation = new OperationContext
        {
            ApplicationId = features.LogicalSchema.ApplicationId,
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.ActivationTransition,
            CollectionId = definition.Id,
            TenantId = request.Principal.CurrentTenantId,
            Mode = OperationMode.System,
            Now = timeProvider.GetUtcNow(),
        };
        OperationResult<BasePolicyEvaluation> authorized = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = request.Principal,
            Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                System = true, SystemOwnerModuleId = definition.OwningModuleId,
                Store = new StoreAnnotation { StoreId = request.StoreId },
            },
            ResourceKind = PolicyResourceKind.ActivationDefinition,
        }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactActivationGrant(
            authorized, requiredGrant(definition), definition.OwningModuleId, request.Principal, operation))
            return ActivationFailure(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        OperationResult<BaseActivationTransitionResult> result;
        try
        {
            result = await provider.TransitionAsync(
                create(definition, activationTime.Capture(features.LogicalSchema.ApplicationId), request), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return ActivationFailure(OperationStatus.StoreError, "base.activation.storeError", ErrorCategory.Store); }
        return BaseResultMapper.Map<BaseActivationTransitionResult, BaseActivationTransitionResult>(result, static value => value);
    }

    private static BaseFailure<BaseActivationTransitionResult> ActivationFailure(
        OperationStatus status, string code, ErrorCategory category) => new(status, new BaseError
        {
            Code = code,
            Message = "The activation administration request could not be completed.",
            Category = category,
        }, null, null);

    private async ValueTask<BaseResult<T>> RouteSubjectAsync<T>(
        string storeId,
        PrincipalContext principal,
        BaseSubjectEpochRotationRequest request,
        Func<IBaseSubjectAdministration, ValueTask<OperationResult<T>>> invoke,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(storeId))
            return await Unsupported<T>(cancellationToken).ConfigureAwait(false);
        BaseGeneratedSubjectRegistration? target = subjects.Find(request.ContractId, request.ContractVersion);
        if (target is null)
            return new BaseFailure<T>(OperationStatus.ValidationFailed, new BaseError
            {
                Code = BaseSubjectErrorCodes.ContractInvalid,
                Message = "The subject contract is invalid.",
                Category = ErrorCategory.Validation,
            }, null, null);
        var operation = new OperationContext
        {
            ApplicationId = features.LogicalSchema.ApplicationId,
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.SubjectEpochRotate,
            CollectionId = target.Definition.Id,
            TenantId = principal.CurrentTenantId,
            Mode = OperationMode.System,
            Now = DateTimeOffset.UtcNow,
        };
        OperationResult<BasePolicyEvaluation> authorized = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = operation,
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
                Store = new StoreAnnotation { StoreId = storeId },
            },
            ResourceKind = PolicyResourceKind.SubjectContract,
            SubjectContractId = target.Definition.Id,
            SubjectContractVersion = target.Definition.Version,
        }, cancellationToken).ConfigureAwait(false);
        if (!authorized.IsSuccess() || !BaseSystemCollectionGate.HasExactGrant(authorized, target.Definition.AdministrationGrantId))
            return new BaseFailure<T>(OperationStatus.PolicyDenied, new BaseError
            {
                Code = BaseAdministrationErrorCodes.Unauthorized,
                Message = "The administration request is not authorized.",
                Category = ErrorCategory.Authorization,
            }, null, null);
        if (stores.GetRegistration(storeId)?.Store is not IBaseSubjectAdministration administration)
            return await Unsupported<T>(cancellationToken).ConfigureAwait(false);
        OperationResult<T> providerResult = await invoke(administration).ConfigureAwait(false);
        if (!providerResult.IsSuccess())
        {
            BaseError error = BaseSubjectFailureContract.NormalizeProviderError(providerResult.Status, providerResult.Error);
            return new BaseFailure<T>(
                BaseSubjectFailureContract.NormalizeProviderStatus(providerResult.Status, providerResult.Error),
                error,
                null,
                null);
        }
        return BaseResultMapper.Map<T, T>(providerResult, static value => value);
    }

    private static ValueTask<BaseResult<T>> Unsupported<T>(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<BaseResult<T>>(new BaseFailure<T>(
            OperationStatus.CapabilityUnavailable,
            new BaseError
            {
                Code = BaseAdministrationErrorCodes.CapabilityUnavailable,
                Message = "The selected BASE provider does not support administration.",
                Category = ErrorCategory.Capability,
            },
            warnings: null,
            diagnostics: null));
    }

    private async ValueTask<BaseResult<T>> RouteAsync<T>(
        string storeId,
        PrincipalContext principal,
        BaseOperationKind operationKind,
        Func<IRecordStoreAdministration, ValueTask<OperationResult<T>>> invoke,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(storeId) || stores.GetRegistration(storeId)?.Store is not IRecordStoreAdministration administration)
            return await Unsupported<T>(cancellationToken).ConfigureAwait(false);
        var operation = new OperationContext
        {
            Operation = operationKind,
            CollectionId = "base-administration",
            Mode = OperationMode.System,
            Now = DateTimeOffset.UtcNow,
        };
        OperationResult<BasePolicyEvaluation> authorized = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal,
            Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = "base-administration",
                Name = "BASE administration",
                Kind = "system",
                Exposed = false,
                SchemaMode = SchemaMode.Strict,
                UnknownFields = UnknownFieldPolicy.Reject,
                Store = new StoreAnnotation { StoreId = storeId },
            },
            ResourceKind = PolicyResourceKind.AdminMetadata,
        }, cancellationToken).ConfigureAwait(false);
        if (!authorized.IsSuccess())
            return new BaseFailure<T>(OperationStatus.PolicyDenied, new BaseError
            {
                Code = BaseAdministrationErrorCodes.Unauthorized,
                Message = "The administration request is not authorized.",
                Category = ErrorCategory.Authorization,
            }, null, null);
        return BaseResultMapper.Map<T, T>(await invoke(administration).ConfigureAwait(false), static value => value);
    }

    private static BaseAdministrationCapability UnavailableCapability { get; } = new()
    {
        Backup = false, Validate = false, Restore = false, AdministrativePurge = true,
        VectorRebuild = false,
        OnlineBackup = false, WritersBlockedDuringBackup = false, ReadersBlockedDuringBackup = false,
        RestoreRequiresExclusiveMaintenance = false, Durable = false, MaxArtifactBytes = 0,
    };
}
