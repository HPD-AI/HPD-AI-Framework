using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

internal sealed class DefaultHPDBaseAdministration(
    IServiceProvider services,
    IRecordStoreRegistry stores,
    IBasePolicyOrchestrator policy,
    BaseSubjectContractRegistry subjects,
    BaseSubjectLifecycleInspectionAuthorityRegistry lifecycleInspectionAuthorities,
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
        BaseResult<BaseRestoreResult> result = await RouteAsync(request.StoreId, request.Principal, BaseOperationKind.AdminRestore,
            administration => administration.RestoreAsync(source, request, cancellationToken), cancellationToken).ConfigureAwait(false);
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

    public async ValueTask<BaseResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteSubjectLifecycleMaintenanceAsync(
        string storeId,
        PrincipalContext principal,
        BaseSubjectLifecycleMaintenanceExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(storeId) || stores.GetRegistration(storeId)?.Store is not IBaseSubjectLifecycleStore store)
            return await Unsupported<BaseSubjectLifecycleMaintenanceResult>(cancellationToken).ConfigureAwait(false);

        const string rotationGrant = "base.subjectLifecycle.scope.rotate";
        BaseGeneratedSubjectRegistration? target = request.ContractId is null || request.ContractVersion is null
            ? null
            : subjects.Find(request.ContractId, request.ContractVersion.Value);
        if (request.Kind != BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection && target is null)
            return new BaseFailure<BaseSubjectLifecycleMaintenanceResult>(OperationStatus.ValidationFailed,
                new BaseError { Code = BaseSubjectErrorCodes.LifecycleContractInvalid, Message = "The subject lifecycle contract is invalid.", Category = ErrorCategory.Validation }, null, null);

        string action = request.Kind == BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection
            ? rotationGrant
            : target!.Definition.AdministrationGrantId;
        string owner = request.Kind == BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection
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

        var normalized = request with { PlanChecksum = BaseSubjectLifecycleMaintenanceProcessor.PlanChecksum(request with { PlanChecksum = new byte[32] }) };
        var processor = new BaseSubjectLifecycleMaintenanceProcessor();
        RecordMutationExecutionResult execution = await store.ExecuteMaintenanceAsync(processor, normalized, cancellationToken).ConfigureAwait(false);
        if (execution.Outcome == RecordMutationExecutionOutcome.Committed && processor.Result is not null)
            return new BaseSuccess<BaseSubjectLifecycleMaintenanceResult>(processor.Result, processor.Result.Duplicate ? OperationStatus.Ok : OperationStatus.Updated, null, null, null, null);
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
