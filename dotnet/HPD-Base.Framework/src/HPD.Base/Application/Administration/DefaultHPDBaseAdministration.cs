using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

internal sealed class DefaultHPDBaseAdministration(
    IServiceProvider services,
    IRecordStoreRegistry stores,
    IBasePolicyOrchestrator policy,
    BaseSubjectContractRegistry subjects,
    BaseSubjectLifecycleInspectionAuthorityRegistry lifecycleInspectionAuthorities,
    BaseActivationRegistry activations,
    BaseActivationMigrationRegistry activationMigrations,
    BaseScheduleRecoveryKeyRegistry scheduleRecoveryKeys,
    BaseActivationAcceptedTimeAuthority activationTime,
    BaseActivationProviderExecutionGate activationProviderGate,
    BaseSemanticRecoveryAuthorityRegistry semanticRecovery,
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
        BaseResult<BaseRestoreResult> result = await RouteAsync(request.StoreId, request.Principal, BaseOperationKind.AdminRestore,
            async administration =>
            {
                BaseRestoreRequest authorized = request with
                {
                    RecoveryApplicationId = features.LogicalSchema.ApplicationId,
                    RecoveryVerificationKeys = scheduleRecoveryKeys.Keys,
                    RecoveryAcceptedNow = timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                    SemanticRecoveryAuthority = null,
                };
                if (!semanticRecovery.Selections.TryGetValue(request.StoreId, out BaseSemanticActivationRestoreSelection? selection)
                    || selection.EnabledRestoreMode != BaseActivationRestoreMode.NewDisasterDomain)
                    return await administration.RestoreAsync(source, authorized, cancellationToken).ConfigureAwait(false);

                Stream effective = source;
                string? temporaryPath = null;
                try
                {
                    if (!source.CanSeek)
                    {
                        temporaryPath = Path.Combine(Path.GetTempPath(), $"hpd-base-semantic-restore-{Guid.NewGuid():N}.tmp");
                        var temporary = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite,
                            FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        effective = temporary;
                        byte[] buffer = new byte[131072]; long total = 0;
                        while (true)
                        {
                            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                            if (read == 0) break;
                            total = checked(total + read);
                            if (total > administration.AdministrationCapability.MaxArtifactBytes)
                                return RestoreFailure(BaseAdministrationErrorCodes.ArtifactTooLarge, "The backup artifact exceeds the configured bound.");
                            await temporary.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        }
                        await temporary.FlushAsync(cancellationToken).ConfigureAwait(false);
                        temporary.Position = 0;
                    }
                    long start = effective.Position;
                    OperationResult<BaseBackupManifest> validation = await administration.ValidateBackupAsync(effective,
                        new BaseBackupValidationRequest
                        {
                            StoreId = request.StoreId, Principal = request.Principal,
                            ExpectedArtifactStoreIdentityDigest = request.ExpectedArtifactStoreIdentityDigest,
                        }, cancellationToken).ConfigureAwait(false);
                    if (!validation.IsSuccess() || validation.Value is null)
                        return new OperationResult<BaseRestoreResult> { Status = validation.Status, Error = validation.Error,
                            Warnings = validation.Warnings, Diagnostics = validation.Diagnostics };
                    effective.Position = start;
                    BaseResult<BaseSemanticRecoveryRestoreAuthority> recovery = await ReadSemanticRestoreAuthorityAsync(
                        semanticRecovery, features.LogicalSchema.ApplicationId, request.StoreId,
                        validation.Value, authorized.RecoveryAcceptedNow, cancellationToken).ConfigureAwait(false);
                    if (recovery is not BaseSuccess<BaseSemanticRecoveryRestoreAuthority> success)
                    {
                        BaseFailure<BaseSemanticRecoveryRestoreAuthority> failure = (BaseFailure<BaseSemanticRecoveryRestoreAuthority>)recovery;
                        return new OperationResult<BaseRestoreResult> { Status = failure.Status, Error = failure.Error };
                    }
                    return await administration.RestoreAsync(effective,
                        authorized with { SemanticRecoveryAuthority = success.Value }, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (!ReferenceEquals(effective, source)) await effective.DisposeAsync().ConfigureAwait(false);
                    if (temporaryPath is not null) try { File.Delete(temporaryPath); } catch { }
                }
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseSuccess<BaseRestoreResult>)
        {
            try { await services.GetRequiredService<BaseSubjectControlDispatcher>().ReconcileAsync(cancellationToken).ConfigureAwait(false); }
            catch when (!cancellationToken.IsCancellationRequested) { }
        }
        return result;
    }

    internal static async ValueTask<BaseResult<BaseSemanticRecoveryRestoreAuthority>> ReadSemanticRestoreAuthorityAsync(
        BaseSemanticRecoveryAuthorityRegistry semanticRecovery, string applicationId,
        string logicalStoreId, BaseBackupManifest artifact, long acceptedNow, CancellationToken cancellationToken)
    {
        var owned = semanticRecovery.Find(logicalStoreId);
        if (owned is null) return SemanticRecoveryFailure();
        BaseSemanticRecoveryAuthorityDefinition definition = owned.Value.Definition;
        BaseSemanticRecoveryOperationLimits limits = definition.Limits;
        byte[] artifactChecksum;
        try { artifactChecksum = Convert.FromHexString(artifact.ProviderPayloadSha256); }
        catch (FormatException) { return SemanticRecoveryFailure(); }
        var headRequest = new BaseSemanticRecoveryHeadRequest
        {
            ApplicationId = applicationId, LogicalStoreId = logicalStoreId,
            ArtifactId = artifact.ProviderPayloadSha256, ArtifactChecksum = artifactChecksum.ToImmutableArray(), Limits = limits,
        };
        try
        {
            BaseResult<BaseSemanticRecoveryPublishedHead> headResult = await semanticRecovery.InvokeAsync(logicalStoreId,
                limits.ResolutionDeadline, headRequest, HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryHeadRequest,
                HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryPublishedHead,
                static (instance, value, token) => instance.ReadHeadAsync(value, token), cancellationToken).ConfigureAwait(false);
            if (headResult is not BaseSuccess<BaseSemanticRecoveryPublishedHead> headSuccess
                || !BaseSemanticRecoveryAuthorityContract.PublishedHeadIsValid(definition,
                    headRequest.ApplicationId, headRequest.LogicalStoreId,
                    BaseSemanticRecoveryAuthorityContract.HeadRequestChecksum(headRequest), headSuccess.Value)
                || headSuccess.Value.HasPendingSuccessor || headSuccess.Value.PublishedSequence < artifact.SemanticTerminalPublicationSequence)
                return SemanticRecoveryFailure();
            var entries = ImmutableArray.CreateBuilder<BaseSemanticRecoveryPublicationEntry>();
            long after = artifact.SemanticTerminalPublicationSequence; int pageCount = 0; long canonicalBytes = 0;
            while (after < headSuccess.Value.PublishedSequence)
            {
                pageCount = checked(pageCount + 1);
                if (pageCount > limits.MaximumPages) return SemanticRecoveryFailure();
                int take = (int)Math.Min(limits.MaximumPageEntries, headSuccess.Value.PublishedSequence - after);
                var pageRequest = new BaseSemanticRecoveryPageRequest
                { Head = headSuccess.Value, AfterSequence = after, Take = take, Limits = limits };
                BaseResult<BaseSemanticRecoveryPublicationPage> pageResult = await semanticRecovery.InvokeAsync(logicalStoreId,
                    limits.ResolutionDeadline, pageRequest, HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryPageRequest,
                    HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryPublicationPage,
                    static (instance, value, token) => instance.ReadPageAsync(value, token), cancellationToken).ConfigureAwait(false);
                if (pageResult is not BaseSuccess<BaseSemanticRecoveryPublicationPage> pageSuccess
                    || !BaseSemanticRecoveryAuthorityContract.PublicationPageIsValid(pageRequest, pageSuccess.Value))
                    return SemanticRecoveryFailure();
                foreach (BaseSemanticRecoveryPublicationEntry entry in pageSuccess.Value.Entries)
                    canonicalBytes = checked(canonicalBytes + JsonSerializer.SerializeToUtf8Bytes(
                        entry, HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryPublicationEntry).LongLength);
                if (canonicalBytes > limits.MaximumTransientBytes) return SemanticRecoveryFailure();
                entries.AddRange(pageSuccess.Value.Entries);
                after = pageSuccess.Value.NextAfterSequence ?? pageSuccess.Value.HeadSequence;
                if (entries.Count > limits.MaximumEntries) return SemanticRecoveryFailure();
            }
            var authority = new BaseSemanticRecoveryRestoreAuthority
            {
                Definition = definition,
                AcceptedNow = acceptedNow, PageCount = pageCount, CanonicalBytes = canonicalBytes,
                TransientBytes = canonicalBytes, Limits = limits,
                ArtifactSequence = artifact.SemanticTerminalPublicationSequence,
                ArtifactOrderedChecksum = artifact.SemanticTerminalPublicationChecksum,
                HeadRequest = headRequest, Head = headSuccess.Value,
                Publications = entries.ToImmutable(), Checksum = [],
            };
            authority = authority with { Checksum = BaseSemanticRecoveryAuthorityContract.RestoreAuthorityChecksum(authority) };
            return BaseSemanticRecoveryAuthorityContract.RestoreAuthorityIsValid(definition, authority)
                ? new BaseSuccess<BaseSemanticRecoveryRestoreAuthority>(authority, OperationStatus.Ok, null, null, null, null)
                : SemanticRecoveryFailure();
        }
        catch when (!cancellationToken.IsCancellationRequested) { return SemanticRecoveryFailure(); }
    }

    private static BaseFailure<BaseSemanticRecoveryRestoreAuthority> SemanticRecoveryFailure() => new(
        OperationStatus.StoreError, new BaseError { Code = BaseSemanticActivationErrorCodes.RecoveryProofInvalid,
            Message = "Semantic activation recovery proof is invalid.", Category = ErrorCategory.Store }, null, null);

    private static OperationResult<BaseRestoreResult> RestoreFailure(string code, string message) => new()
    { Status = OperationStatus.ValidationFailed, Error = new BaseError { Code = code, Message = message, Category = ErrorCategory.Validation } };

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

    public async ValueTask<BaseResult<BaseSemanticRecoveryQuarantineRecoveryResult>> RecoverSemanticRecoveryQuarantineAsync(
        BaseSemanticRecoveryQuarantineRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.Principal);
        cancellationToken.ThrowIfCancellationRequested();
        var installed = semanticRecovery.Find(request.LogicalStoreId);
        if (installed is null)
            return new BaseFailure<BaseSemanticRecoveryQuarantineRecoveryResult>(OperationStatus.NotFound,
                new BaseError { Code = BaseSemanticActivationErrorCodes.ExternalPublicationUnavailable,
                    Message = "Semantic recovery authority is unavailable.", Category = ErrorCategory.NotFound }, null, null);
        BaseSemanticRecoveryAuthorityDefinition definition = installed.Value.Definition;
        var operation = new OperationContext
        {
            ApplicationId = features.LogicalSchema.ApplicationId, Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.SemanticRecoveryMaintenance, CollectionId = definition.Id,
            TenantId = request.Principal.CurrentTenantId, Mode = OperationMode.System,
        };
        var resource = new CollectionDefinition
        {
            Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
            SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
            System = true, SystemOwnerModuleId = definition.OwningModuleId,
        };
        OperationResult<BasePolicyEvaluation> authorized = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = request.Principal, Operation = operation, Collection = resource,
            ResourceKind = PolicyResourceKind.ModuleMutation,
        }, cancellationToken).ConfigureAwait(false);
        if (!authorized.IsSuccess() || authorized.Value?.Authority is null
            || !BaseSystemCollectionGate.HasExactModuleGrant(authorized, definition.RecoveryGrantId,
                definition.OwningModuleId, request.Principal, operation))
            return new BaseFailure<BaseSemanticRecoveryQuarantineRecoveryResult>(OperationStatus.PolicyDenied,
                new BaseError { Code = "base.semanticActivation.unauthorized", Message = "Semantic recovery authority is unavailable.", Category = ErrorCategory.Authorization }, null, null);
        return semanticRecovery.RecoverQuarantine(request);
    }

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

    public async ValueTask<BaseResult<BaseActivationMigrationResult>> MigrateActivationAsync(
        BaseActivationAdministrationMigrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        IBaseActivationMigrationRegistration? migration = activationMigrations.Find(request.MigrationId, request.MigrationVersion);
        BaseActivationDefinition? source = migration is null ? null : activations.Find(
            migration.Definition.Source.Id, migration.Definition.Source.Version);
        BaseActivationDefinition? target = migration is null ? null : activations.Find(
            migration.Definition.Target.Id, migration.Definition.Target.Version);
        if (migration is null || source is null || target is null || request.ExpectedGeneration is < 1 or long.MaxValue
            || stores.GetRegistration(request.StoreId)?.Store is not IBaseActivationProvider provider
            || !BaseActivationCertificationReceiptContract.Validate(provider.Descriptor))
            return ActivationPageFailure<BaseActivationMigrationResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        var operation = new OperationContext
        {
            ApplicationId = features.LogicalSchema.ApplicationId, Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.ActivationTransition, CollectionId = source.Id,
            TenantId = request.Principal.CurrentTenantId, Mode = OperationMode.System, Now = timeProvider.GetUtcNow(),
        };
        OperationResult<BasePolicyEvaluation> authorized = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = request.Principal, Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = source.Id, Name = source.Id, Kind = BaseCollectionKinds.Custom, SchemaMode = SchemaMode.Strict,
                UnknownFields = UnknownFieldPolicy.Reject, System = true, SystemOwnerModuleId = source.OwningModuleId,
                Store = new StoreAnnotation { StoreId = request.StoreId },
            },
            ResourceKind = PolicyResourceKind.ActivationDefinition,
        }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactActivationGrant(
            authorized, source.Grants.Migrate, source.OwningModuleId, request.Principal, operation)
            || migration.Definition.GrantId != source.Grants.Migrate)
            return ActivationPageFailure<BaseActivationMigrationResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        BaseAcceptedTimeReceipt accepted = activationTime.Capture(features.LogicalSchema.ApplicationId);
        var scope = new BaseOwnedScopeSeekAuthority
        {
            Kind = request.Scope.Kind,
            ProtectedIndexDigest = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"base.activation.scope.v2\0{(int)request.Scope.Kind}\n{request.Scope.Value ?? string.Empty}")).ToImmutableArray(),
        };
        BaseActivationProviderCallResult<OperationResult<BaseActivationMigrationCandidate>> candidateCall =
            await activationProviderGate.ExecuteAsync(token => provider.ReadMigrationCandidateAsync(new BaseActivationMigrationCandidateRequest
            {
                ApplicationId = features.LogicalSchema.ApplicationId, Scope = scope,
                SourceDefinition = migration.Definition.Source, ActivationId = request.ActivationId,
                ExpectedGeneration = request.ExpectedGeneration, AcceptedTime = accepted, Limits = source.Limits.Provider,
            }, token), source.Limits.Provider.AcquisitionTimeout, source.Limits.Provider.TransactionTimeout, cancellationToken).ConfigureAwait(false);
        if (candidateCall.Outcome != BaseActivationProviderCallOutcome.Completed || candidateCall.Value?.Value is not { } candidate)
            return ActivationPageFailure<BaseActivationMigrationResult>(
                candidateCall.Outcome is BaseActivationProviderCallOutcome.TimedOut or BaseActivationProviderCallOutcome.Capacity
                    ? OperationStatus.CapabilityUnavailable : candidateCall.Value?.Status ?? OperationStatus.StoreError,
                candidateCall.Value?.Error?.Code ?? "base.activation.migrationConflict",
                candidateCall.Value?.Error?.Category ?? ErrorCategory.Store);
        if (candidate.ActivationId != request.ActivationId || candidate.Generation != request.ExpectedGeneration
            || candidate.InputChecksum.Length != 32 || candidate.ControlChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(candidate.CanonicalInput.AsSpan()), candidate.InputChecksum.AsSpan())
            || candidate.CanonicalInput.Length > source.Limits.MaximumInputBytes)
            return ActivationPageFailure<BaseActivationMigrationResult>(OperationStatus.StoreError, "base.activation.providerContractInvalid", ErrorCategory.Store);
        ImmutableArray<byte> replacementInput;
        try { replacementInput = migration.Project(candidate.CanonicalInput.AsSpan()); }
        catch (JsonException)
        { return ActivationPageFailure<BaseActivationMigrationResult>(OperationStatus.ValidationFailed, "base.activation.migrationInvalid", ErrorCategory.Validation); }
        if (replacementInput.Length > target.Limits.MaximumInputBytes)
            return ActivationPageFailure<BaseActivationMigrationResult>(OperationStatus.ValidationFailed, "base.activation.budgetExceeded", ErrorCategory.Validation);
        string replacementId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.migration.id.v1\0{Convert.ToHexString(migration.Definition.Checksum.AsSpan())}\n{request.ActivationId}\n{request.ExpectedGeneration}\n{Convert.ToHexString(request.Identity.Fingerprint.ToArray())}")));
        var intent = new BaseActivationCreateIntent
        {
            Ordinal = 0, Definition = migration.Definition.Target,
            CanonicalInput = replacementInput, InputChecksum = SHA256.HashData(replacementInput.AsSpan()).ToImmutableArray(),
            Scope = request.Scope with { }, RequestedDueAt = request.DueAt?.ToUnixTimeMilliseconds() ?? accepted.CapturedUtc,
            EffectiveDueAt = request.DueAt?.ToUnixTimeMilliseconds() ?? accepted.CapturedUtc,
            Priority = 0, OverlapKey = [], OverlapPolicy = BaseScheduleOverlapPolicy.Allow,
            InitiallyEligible = true, Identity = request.Identity,
        };
        BaseActivationProviderCallResult<OperationResult<BaseActivationMigrationResult>> migrated =
            await activationProviderGate.ExecuteAsync(token => provider.MigrateAsync(new BaseActivationMigrationRequest
            {
                ApplicationId = features.LogicalSchema.ApplicationId, Scope = scope,
                SourceDefinition = migration.Definition.Source, SourceActivationId = request.ActivationId,
                ExpectedSourceGeneration = request.ExpectedGeneration, ExpectedSourceInputChecksum = candidate.InputChecksum,
                ReplacementActivationId = replacementId, Replacement = intent,
                MigrationId = migration.Definition.Id, MigrationVersion = migration.Definition.Version,
                MigrationChecksum = migration.Definition.Checksum, AcceptedTime = accepted,
                Identity = request.Identity, Limits = source.Limits.Provider,
            }, token), source.Limits.Provider.AcquisitionTimeout, source.Limits.Provider.TransactionTimeout, cancellationToken).ConfigureAwait(false);
        if (migrated.Outcome != BaseActivationProviderCallOutcome.Completed || migrated.Value is null)
            return ActivationPageFailure<BaseActivationMigrationResult>(OperationStatus.StoreError, "base.activation.storeError", ErrorCategory.Store);
        if (migrated.Value.IsSuccess() && migrated.Value.Value is { } committed
            && (committed.SourceActivationId != request.ActivationId
                || committed.SourceGeneration != request.ExpectedGeneration + 1
                || committed.SourceControlChecksum.Length != 32
                || committed.ReplacementActivationId != replacementId || committed.ReplacementGeneration != 1
                || committed.ReplacementControlChecksum.Length != 32
                || !AccountingValid(committed.Accounting, 1, source.Limits.Provider)))
            return ActivationPageFailure<BaseActivationMigrationResult>(OperationStatus.StoreError, "base.activation.providerContractInvalid", ErrorCategory.Store);
        return BaseResultMapper.Map<BaseActivationMigrationResult, BaseActivationMigrationResult>(migrated.Value, static value => value);
    }

    public async ValueTask<BaseResult<BaseActivationQuarantinePage>> ExecuteActivationRepairAsync(
        BaseActivationAdministrationRepairRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        BaseActivationDefinition? definition = activations.Find(request.DefinitionId, request.DefinitionVersion);
        if (definition is null || request.Kind != BaseActivationRepairKind.InspectQuarantine
            || request.Take is < 1 or > 256 || request.AfterSequence is < 1
            || stores.GetRegistration(request.StoreId)?.Store is not IBaseActivationProvider provider
            || !BaseActivationCertificationReceiptContract.Validate(provider.Descriptor))
            return ActivationPageFailure<BaseActivationQuarantinePage>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        var operation = new OperationContext
        {
            ApplicationId = features.LogicalSchema.ApplicationId, Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.AdminInspect, CollectionId = definition.Id,
            TenantId = request.Principal.CurrentTenantId, Mode = OperationMode.System, Now = timeProvider.GetUtcNow(),
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
            }, ResourceKind = PolicyResourceKind.ActivationDefinition,
        }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactActivationGrant(
            authorized, definition.Grants.Repair, definition.OwningModuleId, request.Principal, operation))
            return ActivationPageFailure<BaseActivationQuarantinePage>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        BaseActivationProviderCallResult<OperationResult<BaseActivationQuarantinePage>> call = await activationProviderGate.ExecuteAsync(
            token => provider.ReadQuarantineAsync(new BaseActivationQuarantineRequest
            { AfterSequence = request.AfterSequence, Take = request.Take }, token),
            definition.Limits.Provider.AcquisitionTimeout,
            definition.Limits.Provider.TransactionTimeout,
            cancellationToken).ConfigureAwait(false);
        if (call.Outcome != BaseActivationProviderCallOutcome.Completed || call.Value is null)
            return ActivationPageFailure<BaseActivationQuarantinePage>(OperationStatus.StoreError, "base.activation.storeError", ErrorCategory.Store);
        if (!call.Value.IsSuccess() || call.Value.Value is null)
            return BaseResultMapper.Map<BaseActivationQuarantinePage, BaseActivationQuarantinePage>(call.Value, static value => value);
        BaseActivationQuarantinePage page = call.Value.Value;
        bool valid = page.Items.Length <= request.Take && page.Items.All(static item =>
                item.Sequence > 0 && !string.IsNullOrWhiteSpace(item.Operation))
            && page.Items.Select(static item => item.Sequence).SequenceEqual(
                page.Items.Select(static item => item.Sequence).Order())
            && page.Items.Select(static item => item.Sequence).Distinct().Count() == page.Items.Length
            && page.Items.All(item => request.AfterSequence is null || item.Sequence > request.AfterSequence)
            && (page.NextSequence is null || page.Items.Length != 0 && page.NextSequence == page.Items[^1].Sequence);
        return valid
            ? BaseResultMapper.Map<BaseActivationQuarantinePage, BaseActivationQuarantinePage>(call.Value, static value => value)
            : ActivationPageFailure<BaseActivationQuarantinePage>(OperationStatus.StoreError, "base.activation.providerContractInvalid", ErrorCategory.Store);
    }

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
        int candidates = checked(page.Items.Length + (page.Completed ? 0 : 1));
        long evidenceBytes = 0;
        foreach (BaseActivationPruneEvidence item in page.Items)
            evidenceBytes = checked(evidenceBytes + BaseActivationPruneEvidenceContract.MeasureCanonicalBytes(item));
        if (page.Items.Length > take || candidates > limits.MaximumCandidates
            || !AccountingValid(page.Accounting, candidates, limits)
            || page.Accounting.EvidenceBytes != evidenceBytes
            || page.Accounting.IndexOperations != checked(1 + page.Items.Length * 2)) return false;
        for (int index = 0; index < page.Items.Length; index++)
            if (!BaseActivationPruneEvidenceContract.IsValid(page.Items[index])
                || index != 0 && string.CompareOrdinal(page.Items[index - 1].ActivationId, page.Items[index].ActivationId) >= 0)
                return false;
        return page.Completed
            ? page.NextActivationId is null
            : page.Items.Length != 0 && page.NextActivationId == page.Items[^1].ActivationId;
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
        BaseActivationTransitionRequest transition = create(
            definition, activationTime.Capture(features.LogicalSchema.ApplicationId), request);
        BaseActivationProviderCallResult<OperationResult<BaseActivationTransitionResult>> call = await activationProviderGate.ExecuteAsync(
            async token => transition is BaseActivationReconcileEffectRequest reconciliation
                ? (await provider.ResolveIndeterminateAsync(
                    new BaseActivationIndeterminateRequest { Reconciliation = reconciliation }, token).ConfigureAwait(false)) switch
                    {
                        { Status: var status, Value: { } value } => new OperationResult<BaseActivationTransitionResult>
                            { Status = status, Value = value.Transition },
                        { } failed => new OperationResult<BaseActivationTransitionResult>
                            { Status = failed.Status, Error = failed.Error, Warnings = failed.Warnings, Diagnostics = failed.Diagnostics },
                    }
                : await provider.TransitionAsync(transition, token).ConfigureAwait(false),
            definition.Limits.Provider.AcquisitionTimeout,
            definition.Limits.Provider.TransactionTimeout,
            cancellationToken).ConfigureAwait(false);
        if (call.Outcome == BaseActivationProviderCallOutcome.Cancelled && cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);
        if (call.Outcome is BaseActivationProviderCallOutcome.TimedOut or BaseActivationProviderCallOutcome.Capacity)
            return ActivationFailure(OperationStatus.CapabilityUnavailable, "base.activation.capacityUnavailable", ErrorCategory.Capability);
        if (call.Outcome != BaseActivationProviderCallOutcome.Completed || call.Value is null)
            return ActivationFailure(OperationStatus.StoreError, "base.activation.storeError", ErrorCategory.Store);
        return BaseResultMapper.Map<BaseActivationTransitionResult, BaseActivationTransitionResult>(call.Value, static value => value);
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
