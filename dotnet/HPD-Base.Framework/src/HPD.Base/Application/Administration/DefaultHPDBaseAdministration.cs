using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

internal sealed class DefaultHPDBaseAdministration(
    IServiceProvider services,
    IRecordStoreRegistry stores,
    IBasePolicyOrchestrator policy,
    BaseSubjectContractRegistry subjects,
    BaseSubjectControlOperationalState subjectControlState,
    HPDBaseInstalledFeatures features) : IHPDBaseAdministration
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
