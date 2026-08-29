using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    BaseSemanticActivationRegistry semanticActivations,
    BaseSemanticActivationRemovalRegistry semanticRemovals,
    BaseModuleMutationRegistry moduleMutations,
    BaseSemanticActivationInspectionTokenCodec semanticInspectionTokens,
    BaseSemanticActivationControlTokenCodec semanticControlTokens,
    BaseSubjectControlOperationalState subjectControlState,
    HPDBaseInstalledFeatures features,
    TimeProvider timeProvider,
    IEnumerable<IBaseCommittedRestoreObserver> restoreObservers,
    IOptions<HPDBaseRuntimeOptions> runtimeOptions) : IHPDBaseAdministration
{
    private readonly IBaseCommittedRestoreObserver[] _restoreObservers = restoreObservers.ToArray();
    private readonly TimeSpan _postCommitWorkTimeout = runtimeOptions.Value.Events.PostCommitWorkTimeout;

    public BaseAdministrationCapability Capability =>
        stores.GetRegistrations().Select(static registration => registration.Store).OfType<IRecordStoreAdministration>().ToArray() is [{ } administration]
            ? administration.AdministrationCapability
            : UnavailableCapability;

    public async ValueTask<BaseResult<BaseSemanticActivationInspectionPage>> InspectSemanticActivationsAsync(
        string storeId, PrincipalContext principal, BaseSemanticActivationInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        BaseSemanticActivationKeyDefinition? installed = semanticActivations.Find(request.Definition.Id, request.Definition.Version);
        if (installed is null || !CryptographicOperations.FixedTimeEquals(installed.Checksum.AsSpan(), request.Definition.Checksum.AsSpan())
            || !await AuthorizeSemanticAdministrationAsync(storeId, principal, installed, BaseOperationKind.AdminInspect, cancellationToken).ConfigureAwait(false))
            return SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.PolicyDenied,
                BaseSemanticActivationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        if (request.Take is < 1 or > 256 || request.State is { } state && !Enum.IsDefined(state)
            || !SemanticExecutionLimitsValid(request.Limits))
            return SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
        RecordStoreRegistration? registration = stores.GetRegistration(storeId);
        IAtomicRecordStore? atomic = registration?.AtomicExecutionStore ?? registration?.Store as IAtomicRecordStore;
        if (registration?.Store is not IBaseSemanticActivationAdministration provider || atomic is null)
            return SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.CapabilityUnavailable,
                BaseSemanticActivationErrorCodes.CapabilityUnavailable, ErrorCategory.Capability);
        if (!BaseSemanticActivationCapabilityContract.IsValid(provider.SemanticActivationCapability)
            || !provider.SemanticActivationCapability.MaintenanceSupported)
            return SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.CapabilityUnavailable,
                BaseSemanticActivationErrorCodes.CapabilityUnavailable, ErrorCategory.Capability);
        BaseSemanticActivationExecutionLimits effectiveInspectionLimits = EffectiveSemanticInspectionLimits(
            request.Limits, installed.Limits.Execution, provider.SemanticActivationCapability);
        BaseRegisteredModuleMutationDefinition? operation = moduleMutations.Find(installed.EnsureOperation.OperationId,
            installed.EnsureOperation.OperationVersion);
        if (operation is null) return SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.CapabilityUnavailable,
            BaseSemanticActivationErrorCodes.NotInstalled, ErrorCategory.Capability);
        OperationResult<BaseAtomicMutationAuthorityRequirement> captured = await atomic.CaptureAtomicMutationAuthorityRequirementAsync(
            features.LogicalSchema.ApplicationId, [], DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(operation.Limits), cancellationToken).ConfigureAwait(false);
        BaseSemanticActivationStoreAuthorityRequirement? authority = captured.Value?.SemanticActivation;
        if (!captured.IsSuccess() || authority is null || authority.LogicalStoreId != storeId)
            return SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.Conflict,
                BaseSemanticActivationErrorCodes.GraphChanged, ErrorCategory.Conflict);
        BaseSemanticActivationProviderInspectionBoundary? after = null;
        if (request.After is not null)
        {
            if (!semanticInspectionTokens.TryRead(request.After, features.LogicalSchema.ApplicationId, storeId,
                    request.Definition, request.State, request.Take, out BaseSemanticActivationInspectionTokenPayload? payload)
                || payload is null || payload.RestoreEpoch != authority.RestoreEpoch
                || payload.Boundary.CapturedAuthorityGeneration != authority.SemanticAuthorityGeneration)
                return SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.ValidationFailed,
                    BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
            after = payload.Boundary;
        }
        var providerRequest = new BaseSemanticActivationProviderInspectionRequest
        {
            ApplicationId = features.LogicalSchema.ApplicationId, LogicalStoreId = storeId, RestoreEpoch = authority.RestoreEpoch,
            Definition = request.Definition, State = request.State, After = after, Take = request.Take,
            Limits = effectiveInspectionLimits, RuntimeRequestAuthorityChecksum = [],
        };
        providerRequest = providerRequest with { RuntimeRequestAuthorityChecksum = BaseSemanticActivationInspectionContract.RequestChecksum(providerRequest) };
        BaseActivationProviderCallResult<BaseResult<BaseSemanticActivationProviderInspectionPage>> inspected =
            await activationProviderGate.ExecuteAsync(token => provider.InspectAsync(providerRequest, token),
                installed.Limits.Deadlines.AcquisitionTimeout, installed.Limits.Deadlines.MaintenanceTimeout,
                cancellationToken).ConfigureAwait(false);
        if (inspected.Outcome != BaseActivationProviderCallOutcome.Completed || inspected.Value is null)
            return SemanticProviderCallFailure<BaseSemanticActivationInspectionPage>(inspected.Outcome);
        BaseResult<BaseSemanticActivationProviderInspectionPage> result = inspected.Value;
        if (result is BaseFailure<BaseSemanticActivationProviderInspectionPage> providerFailure)
            return NormalizeSemanticInspectionProviderFailure(providerFailure);
        if (result is not BaseSuccess<BaseSemanticActivationProviderInspectionPage> success
            || !SemanticInspectionPageValid(providerRequest, success.Value, authority, installed))
            return SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.ProviderContractInvalid,
                ErrorCategory.Store);
        BaseSemanticActivationProviderInspectionPage page = success.Value;
        DateTimeOffset expiry = timeProvider.GetUtcNow().AddMinutes(15);
        ImmutableArray<BaseSemanticActivationInspectionItem> items = page.Items.Select(item => new BaseSemanticActivationInspectionItem
        {
            State = item.State, SlotGeneration = item.SlotGeneration, RetirementPosition = item.RetirementPosition,
            ItemToken = semanticInspectionTokens.Protect(new(features.LogicalSchema.ApplicationId, storeId, authority.RestoreEpoch,
                request.Definition, request.State, request.Take, item.Boundary, expiry)),
            SanitizedChecksum = SHA256.HashData(item.StateChecksum.AsSpan()).ToImmutableArray(),
        }).ToImmutableArray();
        BaseSemanticActivationInspectionToken? next = page.Next is null ? null : semanticInspectionTokens.Protect(new(
            features.LogicalSchema.ApplicationId, storeId, authority.RestoreEpoch, request.Definition, request.State,
            request.Take, page.Next, expiry));
        byte[] sanitized = SHA256.HashData(page.Checksum.AsSpan());
        return new BaseSuccess<BaseSemanticActivationInspectionPage>(new()
        {
            Items = items, Next = next, CapturedAuthorityGeneration = page.CapturedAuthorityGeneration,
            ReadIntervals = page.ReadIntervals.Select(static value => value with
            {
                CanonicalLowerBound = value.CanonicalLowerBound.ToArray().ToImmutableArray(),
                CanonicalUpperBound = value.CanonicalUpperBound.ToArray().ToImmutableArray(),
            }).ToImmutableArray(), Accounting = page.Accounting with { }, Checksum = sanitized.ToImmutableArray(),
        }, OperationStatus.Ok, null, null, null, null);
    }

    private async ValueTask<BaseResult<BaseSemanticActivationMaintenanceResult>> ExecuteSemanticActivationMaintenanceAsync(
        string storeId, PrincipalContext principal, BaseSemanticActivationMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        BaseSemanticActivationKeyDefinition? installed = request is BaseSemanticActivationRemoveRequest removeRequest
            ? semanticRemovals.Find(removeRequest.Definition)?.From
            : semanticActivations.Find(request.Definition.Id, request.Definition.Version);
        if (installed is null || !CryptographicOperations.FixedTimeEquals(installed.Checksum.AsSpan(), request.Definition.Checksum.AsSpan())
            || !await AuthorizeSemanticAdministrationAsync(storeId, principal, installed,
                BaseOperationKind.SemanticRecoveryMaintenance, cancellationToken).ConfigureAwait(false))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.PolicyDenied,
                BaseSemanticActivationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        if (!SemanticMaintenanceRequestValid(request))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
        if (request is BaseSemanticActivationRemoveRequest remove
            && (semanticRemovals.Find(remove.Definition) is not { } authority
                || !CryptographicOperations.FixedTimeEquals(authority.Checksum.AsSpan(), remove.RemovalAuthority.Checksum.AsSpan())
                || !CryptographicOperations.FixedTimeEquals(authority.ResultingDefinitionSetChecksum.AsSpan(), semanticActivations.DefinitionSetChecksum.AsSpan())))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                BaseSemanticActivationErrorCodes.RemovalBlocked, ErrorCategory.Conflict);
        if (stores.GetRegistration(storeId)?.Store is not IBaseSemanticActivationAdministration provider)
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.CapabilityUnavailable,
                BaseSemanticActivationErrorCodes.CapabilityUnavailable, ErrorCategory.Capability);
        if (semanticRecovery.Selections.TryGetValue(storeId, out BaseSemanticActivationRestoreSelection? selection)
            && selection.EnabledRestoreMode is not null
            && request is BaseSemanticActivationMigrateRequest or BaseSemanticActivationRemoveRequest)
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                request is BaseSemanticActivationMigrateRequest
                    ? BaseSemanticActivationErrorCodes.MigrationBlocked
                    : BaseSemanticActivationErrorCodes.RemovalBlocked,
                ErrorCategory.Conflict);
        if (request is BaseSemanticActivationRemoveRequest
            && (semanticRecovery.HasExternalAuthority(storeId) || semanticRecovery.HasOperationalDependency(storeId)))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                BaseSemanticActivationErrorCodes.RemovalBlocked, ErrorCategory.Conflict);
        if (!BaseSemanticActivationCapabilityContract.IsValid(provider.SemanticActivationCapability)
            || !provider.SemanticActivationCapability.MaintenanceSupported)
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.CapabilityUnavailable,
                BaseSemanticActivationErrorCodes.CapabilityUnavailable, ErrorCategory.Capability);
        request = WithEffectiveSemanticMaintenanceLimits(request, installed, semanticActivations.Definitions,
            semanticRemovals.Authorities,
            provider.SemanticActivationCapability);
        BaseActivationProviderCallResult<BaseResult<BaseSemanticActivationMaintenanceResult>> executed =
            await activationProviderGate.ExecuteAsync(token => provider.ExecuteAsync(request, token),
                installed.Limits.Deadlines.AcquisitionTimeout,
                request.Limits.Deadline < installed.Limits.Deadlines.MaintenanceTimeout
                    ? request.Limits.Deadline : installed.Limits.Deadlines.MaintenanceTimeout,
                cancellationToken).ConfigureAwait(false);
        if (executed.Outcome != BaseActivationProviderCallOutcome.Completed || executed.Value is null)
            return SemanticProviderCallFailure<BaseSemanticActivationMaintenanceResult>(executed.Outcome);
        BaseResult<BaseSemanticActivationMaintenanceResult> result = executed.Value;
        if (result is not BaseSuccess<BaseSemanticActivationMaintenanceResult> success
            || !BaseSemanticActivationMaintenanceContract.IsValid(request, success.Value))
            return result is BaseFailure<BaseSemanticActivationMaintenanceResult> failure ? NormalizeSemanticProviderFailure(failure)
                : SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                    BaseSemanticActivationErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
        return new BaseSuccess<BaseSemanticActivationMaintenanceResult>(CloneSemanticMaintenance(success.Value),
            success.Status, success.Warnings, success.Revision, success.Events, success.Diagnostics);
    }

    public async ValueTask<BaseResult<BaseSemanticActivationControlDescriptor>> ReadSemanticActivationControlAsync(
        string storeId, PrincipalContext principal, BaseSemanticActivationDefinitionKey definition,
        CancellationToken cancellationToken = default)
    {
        BaseSemanticActivationKeyDefinition? installed = semanticActivations.Find(definition.Id, definition.Version)
            ?? semanticRemovals.Find(definition)?.From;
        if (installed is null || !CryptographicOperations.FixedTimeEquals(installed.Checksum.AsSpan(), definition.Checksum.AsSpan())
            || !await AuthorizeSemanticAdministrationAsync(storeId, principal, installed,
                BaseOperationKind.SemanticRecoveryMaintenance, cancellationToken).ConfigureAwait(false))
            return SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.PolicyDenied,
                BaseSemanticActivationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        RecordStoreRegistration? registration = stores.GetRegistration(storeId);
        IAtomicRecordStore? atomic = registration?.AtomicExecutionStore ?? registration?.Store as IAtomicRecordStore;
        if (registration?.Store is not IBaseSemanticActivationAdministration provider || atomic is null
            || !BaseSemanticActivationCapabilityContract.IsValid(provider.SemanticActivationCapability)
            || !provider.SemanticActivationCapability.MaintenanceSupported)
            return SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.CapabilityUnavailable,
                BaseSemanticActivationErrorCodes.CapabilityUnavailable, ErrorCategory.Capability);
        BaseSemanticActivationOperationalStatus operational;
        try { operational = provider.SemanticActivationOperationalStatus; }
        catch { return SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.StoreError,
            BaseSemanticActivationErrorCodes.ProviderContractInvalid, ErrorCategory.Store); }
        if (!SemanticOperationalStatusValid(operational))
            return SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
        bool quarantined = operational.Quarantined || activationProviderGate.IsQuarantined;
        if (!operational.Ready || quarantined)
            return new BaseSuccess<BaseSemanticActivationControlDescriptor>(new()
            {
                DefinitionId = definition.Id, DefinitionVersion = definition.Version,
                AuthorityGeneration = null, LiveCount = null, RetiredCount = null, AbsenceCount = null,
                Ready = false, Quarantined = quarantined, Compact = null, Remove = null,
            }, OperationStatus.Ok, null, null, null, null);
        BaseRegisteredModuleMutationDefinition? operation = moduleMutations.Find(installed.EnsureOperation.OperationId,
            installed.EnsureOperation.OperationVersion);
        if (operation is null) return SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.CapabilityUnavailable,
            BaseSemanticActivationErrorCodes.NotInstalled, ErrorCategory.Capability);
        OperationResult<BaseAtomicMutationAuthorityRequirement> captured = await atomic.CaptureAtomicMutationAuthorityRequirementAsync(
            features.LogicalSchema.ApplicationId, [], DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(operation.Limits), cancellationToken).ConfigureAwait(false);
        BaseSemanticActivationStoreAuthorityRequirement? storeAuthority = captured.Value?.SemanticActivation;
        if (!captured.IsSuccess() || storeAuthority is null || storeAuthority.LogicalStoreId != storeId)
            return SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.Conflict,
                BaseSemanticActivationErrorCodes.GraphChanged, ErrorCategory.Conflict);
        long maximumRows; long providerMaximumRows;
        try
        {
            maximumRows = checked(installed.Limits.MaximumLiveSlots + installed.Limits.MaximumRetiredSlots + installed.Limits.MaximumAbsenceMarkers);
            providerMaximumRows = checked(provider.SemanticActivationCapability.MaximumLiveSlots
                + provider.SemanticActivationCapability.MaximumRetiredSlots + provider.SemanticActivationCapability.MaximumAbsenceMarkers);
        }
        catch (OverflowException) { return SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.ValidationFailed, BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation); }
        var authorityRequest = new BaseSemanticActivationMaintenanceAuthorityRequest
        {
            ApplicationId = features.LogicalSchema.ApplicationId, LogicalStoreId = storeId,
            RestoreEpoch = storeAuthority.RestoreEpoch, Definition = definition,
            SemanticAuthorityGeneration = storeAuthority.SemanticAuthorityGeneration,
            MaximumRows = Math.Min(maximumRows, providerMaximumRows),
            MaximumBytes = Math.Min(installed.Limits.Execution.MaximumTransientBytes, provider.SemanticActivationCapability.MaximumTransientBytes),
            RuntimeRequestChecksum = [],
        };
        authorityRequest = authorityRequest with { RuntimeRequestChecksum = BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(authorityRequest) };
        BaseActivationProviderCallResult<BaseResult<BaseSemanticActivationMaintenanceAuthority>> inspected = await activationProviderGate.ExecuteAsync(
            token => provider.InspectMaintenanceAuthorityAsync(authorityRequest, token), installed.Limits.Deadlines.AcquisitionTimeout,
            installed.Limits.Deadlines.MaintenanceTimeout, cancellationToken).ConfigureAwait(false);
        if (inspected.Outcome != BaseActivationProviderCallOutcome.Completed || inspected.Value is null)
            return SemanticProviderCallFailure<BaseSemanticActivationControlDescriptor>(inspected.Outcome);
        if (inspected.Value is not BaseSuccess<BaseSemanticActivationMaintenanceAuthority> success)
            return inspected.Value is BaseFailure<BaseSemanticActivationMaintenanceAuthority> failure
                ? SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(failure.Status, failure.Error.Code, failure.Error.Category)
                : SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.StoreError, BaseSemanticActivationErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
        BaseSemanticActivationMaintenanceAuthority authority = success.Value;
        if (!SemanticControlAuthorityValid(authorityRequest, authority))
            return SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
        try { operational = provider.SemanticActivationOperationalStatus; }
        catch { return SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.StoreError,
            BaseSemanticActivationErrorCodes.ProviderContractInvalid, ErrorCategory.Store); }
        if (!SemanticOperationalStatusValid(operational))
            return SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
        if (!operational.Ready || operational.Quarantined || activationProviderGate.IsQuarantined)
            return new BaseSuccess<BaseSemanticActivationControlDescriptor>(new()
            {
                DefinitionId = definition.Id, DefinitionVersion = definition.Version,
                AuthorityGeneration = null, LiveCount = null, RetiredCount = null, AbsenceCount = null,
                Ready = false, Quarantined = operational.Quarantined || activationProviderGate.IsQuarantined,
                Compact = null, Remove = null,
            }, OperationStatus.Ok, null, null, null, null);
        int pageSize = Math.Min(256, provider.SemanticActivationCapability.MaximumMaintenancePageSize);
        long authorityPages = authority.ExaminedRows / pageSize + (authority.ExaminedRows % pageSize == 0 ? 0 : 1);
        // Compaction first stages the selected authority and then rebinds the complete
        // surviving authority graph. Both passes consume the installed page budget.
        long requiredPages = checked(authorityPages * 2);
        long requiredRows = checked(authority.ExaminedRows * 2);
        if (requiredPages > int.MaxValue)
            return SemanticAdminFailure<BaseSemanticActivationControlDescriptor>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation);
        var limits = new BaseSemanticActivationMaintenanceLimits
        {
            PageSize = pageSize,
            MaximumPages = checked((int)Math.Max(1, requiredPages)),
            MaximumRows = requiredRows,
            MaximumBytes = Math.Min(provider.SemanticActivationCapability.MaximumTransientBytes,
                Math.Max(authority.CanonicalBytes, installed.Limits.Execution.MaximumTransientBytes)),
            Deadline = installed.Limits.Deadlines.MaintenanceTimeout,
        };
        DateTimeOffset expiry = timeProvider.GetUtcNow().AddMinutes(15);
        BaseSemanticActivationControlTokenPayload Payload(BaseSemanticActivationControlTokenKind kind) => new(kind,
            features.LogicalSchema.ApplicationId, storeId, storeAuthority.RestoreEpoch, definition,
            semanticActivations.DefinitionSetChecksum, authority.SemanticAuthorityGeneration, authority.LiveCount,
            authority.RetiredCount, authority.AbsenceCount, authority.RetiredAuthorityChecksum,
            authority.DefinitionStateChecksum, authority.AbsenceAuthorityChecksum, limits, null, expiry);
        bool compactEligible = installed.Compaction is not BaseSemanticActivationNoCompaction && authority.RetiredCount > 0;
        bool removeEligible = semanticRemovals.Find(definition) is not null && authority.LiveCount == 0 && authority.RetiredCount == 0;
        return new BaseSuccess<BaseSemanticActivationControlDescriptor>(new()
        {
            DefinitionId = definition.Id, DefinitionVersion = definition.Version,
            AuthorityGeneration = authority.SemanticAuthorityGeneration, LiveCount = authority.LiveCount,
            RetiredCount = authority.RetiredCount, AbsenceCount = authority.AbsenceCount,
            Ready = operational.Ready,
            Quarantined = operational.Quarantined,
            Compact = compactEligible ? semanticControlTokens.Protect(Payload(BaseSemanticActivationControlTokenKind.Compact)) : null,
            Remove = removeEligible ? semanticControlTokens.Protect(Payload(BaseSemanticActivationControlTokenKind.Remove)) : null,
        }, OperationStatus.Ok, null, null, null, null);
    }

    public async ValueTask<BaseResult<BaseSemanticActivationControlResult>> ExecuteSemanticActivationControlAsync(
        string storeId, PrincipalContext principal, BaseSemanticActivationControlCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        string normalizedIdempotencyKey;
        try { normalizedIdempotencyKey = BaseMutationRequestIdentity.NormalizeIdempotencyKey(command.IdempotencyKey); }
        catch (ArgumentException)
        { return SemanticAdminFailure<BaseSemanticActivationControlResult>(OperationStatus.ValidationFailed, BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation); }
        if (!semanticControlTokens.TryRead(command.Token, features.LogicalSchema.ApplicationId, storeId, out BaseSemanticActivationControlTokenPayload? payload)
            || payload is null || payload.Kind is not (BaseSemanticActivationControlTokenKind.Compact or BaseSemanticActivationControlTokenKind.Remove
                or BaseSemanticActivationControlTokenKind.ResumeCompact or BaseSemanticActivationControlTokenKind.ResumeRemove)
            || payload.IdempotencyKey is not null && !string.Equals(payload.IdempotencyKey, normalizedIdempotencyKey, StringComparison.Ordinal)
            || command.Confirmation != Confirmation(payload.Kind))
            return SemanticAdminFailure<BaseSemanticActivationControlResult>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
        if (!await AuthorizeSemanticControlPayloadAsync(storeId, principal, payload, cancellationToken).ConfigureAwait(false))
            return SemanticAdminFailure<BaseSemanticActivationControlResult>(OperationStatus.PolicyDenied,
                BaseSemanticActivationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        if (!await SemanticControlTokenCurrentAsync(payload, allowGenerationAdvance: true, cancellationToken).ConfigureAwait(false))
            return SemanticAdminFailure<BaseSemanticActivationControlResult>(OperationStatus.Conflict,
                BaseSemanticActivationErrorCodes.RestoreConflict, ErrorCategory.Conflict);
        BaseSemanticActivationMaintenanceRequest request;
        try { request = ControlRequest(payload, normalizedIdempotencyKey); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return SemanticAdminFailure<BaseSemanticActivationControlResult>(OperationStatus.ValidationFailed, BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation); }
        BaseResult<BaseSemanticActivationMaintenanceResult> result = await ExecuteSemanticActivationMaintenanceAsync(
            storeId, principal, request, cancellationToken).ConfigureAwait(false);
        return ControlResult(payload, normalizedIdempotencyKey, request, result);
    }

    public async ValueTask<BaseResult<BaseSemanticActivationControlResult>> ResolveSemanticActivationControlAsync(
        string storeId, PrincipalContext principal, BaseSemanticActivationControlResolution resolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (!semanticControlTokens.TryRead(resolution.Token, features.LogicalSchema.ApplicationId, storeId, out BaseSemanticActivationControlTokenPayload? payload)
            || payload is null || payload.Kind is not (BaseSemanticActivationControlTokenKind.ResolveCompact or BaseSemanticActivationControlTokenKind.ResolveRemove)
            || string.IsNullOrWhiteSpace(payload.IdempotencyKey))
            return SemanticAdminFailure<BaseSemanticActivationControlResult>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
        if (!await AuthorizeSemanticControlPayloadAsync(storeId, principal, payload, cancellationToken).ConfigureAwait(false))
            return SemanticAdminFailure<BaseSemanticActivationControlResult>(OperationStatus.PolicyDenied,
                BaseSemanticActivationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        if (!await SemanticControlTokenCurrentAsync(payload, allowGenerationAdvance: true, cancellationToken).ConfigureAwait(false))
            return SemanticAdminFailure<BaseSemanticActivationControlResult>(OperationStatus.Conflict,
                BaseSemanticActivationErrorCodes.RestoreConflict, ErrorCategory.Conflict);
        BaseSemanticActivationMaintenanceRequest original = ControlRequest(payload, payload.IdempotencyKey);
        ImmutableArray<byte> fingerprint = BaseSemanticActivationMaintenanceContract.RequestFingerprint(original);
        var request = new BaseSemanticActivationMaintenanceResolutionRequest
        {
            Definition = payload.Definition, Identity = original.Identity,
            MaintenanceId = Convert.ToHexStringLower(fingerprint.AsSpan()), RequestFingerprint = fingerprint,
            Deadline = payload.Limits.Deadline,
        };
        BaseResult<BaseSemanticActivationMaintenanceResult> result = await ResolveSemanticActivationMaintenanceAsync(
            storeId, principal, request, cancellationToken).ConfigureAwait(false);
        return ControlResult(payload, payload.IdempotencyKey, original, result);
    }

    private async ValueTask<BaseResult<BaseSemanticActivationMaintenanceResult>> ResolveSemanticActivationMaintenanceAsync(
        string storeId, PrincipalContext principal, BaseSemanticActivationMaintenanceResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        BaseSemanticActivationKeyDefinition? installed = semanticActivations.Find(request.Definition.Id, request.Definition.Version)
            ?? semanticRemovals.Find(request.Definition)?.From;
        if (installed is null || !CryptographicOperations.FixedTimeEquals(installed.Checksum.AsSpan(), request.Definition.Checksum.AsSpan())
            || !await AuthorizeSemanticAdministrationAsync(storeId, principal, installed, BaseOperationKind.SemanticRecoveryMaintenance, cancellationToken).ConfigureAwait(false))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.PolicyDenied,
                BaseSemanticActivationErrorCodes.Unauthorized, ErrorCategory.Authorization);
        if (request.Identity is null || string.IsNullOrWhiteSpace(request.MaintenanceId)
            || request.RequestFingerprint.Length != 32 || request.Deadline <= TimeSpan.Zero)
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
        if (stores.GetRegistration(storeId)?.Store is not IBaseSemanticActivationAdministration provider)
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.CapabilityUnavailable,
                BaseSemanticActivationErrorCodes.CapabilityUnavailable, ErrorCategory.Capability);
        BaseActivationProviderCallResult<BaseResult<BaseSemanticActivationMaintenanceResult>> resolved =
            await activationProviderGate.ExecuteAsync(token => provider.ResolveAsync(request, token),
                installed.Limits.Deadlines.AcquisitionTimeout,
                request.Deadline < installed.Limits.Deadlines.ReceiptResolutionTimeout
                    ? request.Deadline : installed.Limits.Deadlines.ReceiptResolutionTimeout,
                cancellationToken).ConfigureAwait(false);
        if (resolved.Outcome != BaseActivationProviderCallOutcome.Completed || resolved.Value is null)
            return SemanticProviderCallFailure<BaseSemanticActivationMaintenanceResult>(resolved.Outcome);
        BaseResult<BaseSemanticActivationMaintenanceResult> result = resolved.Value;
        if (result is not BaseSuccess<BaseSemanticActivationMaintenanceResult> success || !ResolvedSemanticMaintenanceValid(request, success.Value))
            return result is BaseFailure<BaseSemanticActivationMaintenanceResult> failure ? NormalizeSemanticProviderFailure(failure)
                : SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                    BaseSemanticActivationErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
        return new BaseSuccess<BaseSemanticActivationMaintenanceResult>(CloneSemanticMaintenance(success.Value),
            success.Status, success.Warnings, success.Revision, success.Events, success.Diagnostics);
    }

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
        if (result is BaseSuccess<BaseRestoreResult> restored)
        {
            bool observerFailed = await NotifyRestoreObserversAsync(restored.Value).ConfigureAwait(false);
            if (observerFailed)
            {
                OperationWarning[] warnings =
                [
                    .. restored.Warnings ?? [],
                    new OperationWarning
                    {
                        Code = "base.runtime.restoreObserverFailed",
                        Message = "A committed restore observer failed.",
                    },
                ];
                result = new BaseSuccess<BaseRestoreResult>(
                    restored.Value, restored.Status, warnings,
                    restored.Revision, restored.Events, restored.Diagnostics);
            }
            try { await services.GetRequiredService<BaseSubjectControlDispatcher>().ReconcileAsync(cancellationToken).ConfigureAwait(false); }
            catch when (!cancellationToken.IsCancellationRequested) { }
        }
        return result;
    }

    private async ValueTask<bool> NotifyRestoreObserversAsync(BaseRestoreResult restore)
    {
        bool failed = false;
        foreach (IBaseCommittedRestoreObserver observer in _restoreObservers)
        {
            using var lifetime = new CancellationTokenSource(_postCommitWorkTimeout);
            try
            {
                await observer.ObserveAsync(Clone(restore), lifetime.Token)
                    .AsTask()
                    .WaitAsync(lifetime.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                failed = true;
            }
        }
        return failed;
    }

    private static BaseRestoreResult Clone(BaseRestoreResult value) => new()
    {
        StoreId = new string(value.StoreId.AsSpan()),
        Status = value.Status,
        InstalledStoreIdentityDigest = new string(value.InstalledStoreIdentityDigest.AsSpan()),
        RestoreEpoch = value.RestoreEpoch,
        RecoveryImageRetained = value.RecoveryImageRetained,
    };

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
                new BaseError { Code = BaseSemanticActivationErrorCodes.ExternalAuthorityUnavailable,
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
                && CryptographicOperations.FixedTimeEquals(item.Definition.Checksum.AsSpan(), definition.Checksum.AsSpan())
                && item.MaximumYields == definition.Limits.MaximumYields
                && BaseActivationControlChecksumContract.Matches(item.ControlChecksum.AsSpan(),
                    item.ActivationId, item.Generation, item.State, item.EffectiveDueAt,
                    item.YieldCount, item.MaximumYields, item.ExecutionSliceOrdinal,
                    item.AttemptStartedAt, item.SliceStartedAt,
                    item.TerminalYieldDisposition, item.TerminalYieldFailureCode))
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

    public ValueTask<BaseResult<BaseActivationReceiptCompactionResult>> CompactActivationReceiptsAsync(
        BaseActivationAdministrationReceiptCompactionRequest request,
        CancellationToken cancellationToken = default) =>
        RouteActivationPageAsync(request, async (provider, definition, scope, accepted, value, token) =>
        {
            OperationResult<BaseActivationReceiptCompactionAuthority> authority =
                await provider.CaptureReceiptCompactionAuthorityAsync(new BaseActivationReceiptCompactionAuthorityRequest
                {
                    ApplicationId = accepted.ApplicationId,
                    Definition = new BaseActivationDefinitionKey
                    {
                        Id = definition.Id, Version = definition.Version, Checksum = definition.Checksum,
                    },
                    ReceiptRetention = definition.ReceiptRetention,
                    Scope = scope,
                    Limits = definition.Limits.Provider,
                }, token).ConfigureAwait(false);
            if (!authority.IsSuccess() || authority.Value is null)
            {
                bool known = authority.Error is { } error
                    && (authority.Status == OperationStatus.Conflict
                        && error.Code is "base.activation.removalBlocked" or "base.activation.maintenanceConflict"
                        && error.Category == ErrorCategory.Conflict
                    || authority.Status == OperationStatus.CapabilityUnavailable
                        && error.Code == "base.activation.capabilityUnavailable"
                        && error.Category == ErrorCategory.Capability);
                if (!known)
                {
                    activationProviderGate.QuarantineContractViolation();
                    return BaseActivationFailureContract.ProviderContractInvalid<BaseActivationReceiptCompactionResult>();
                }
                return new OperationResult<BaseActivationReceiptCompactionResult>
                {
                    Status = authority.Status,
                    Error = authority.Error,
                    Warnings = authority.Warnings,
                    Diagnostics = authority.Diagnostics,
                };
            }
            if (!ReceiptCompactionAuthorityValid(
                authority.Value, definition.ReceiptRetention, accepted.ApplicationId, value.StoreId))
            {
                activationProviderGate.QuarantineContractViolation();
                return BaseActivationFailureContract.ProviderContractInvalid<BaseActivationReceiptCompactionResult>();
            }
            return await provider.CompactActivationReceiptsAsync(new BaseActivationReceiptCompactionRequest
            {
                ApplicationId = accepted.ApplicationId,
                Definition = new BaseActivationDefinitionKey
                {
                    Id = definition.Id, Version = definition.Version, Checksum = definition.Checksum,
                },
                ReceiptRetention = definition.ReceiptRetention,
                Scope = scope,
                AcceptedTime = accepted,
                After = value.AfterActivationId is null ? null : new BaseActivationReceiptCompactionCursor
                {
                    ActivationId = value.AfterActivationId,
                    ReceiptSequence = value.AfterReceiptSequence!.Value,
                },
                Take = value.Take,
                BackupFloor = authority.Value.BackupFloor,
                ExpectedReservation = authority.Value.Reservation,
                Limits = definition.Limits.Provider,
                Identity = value.Identity,
            }, token).ConfigureAwait(false);
        }, static definition => definition.Grants.Remove, cancellationToken);

    private static bool ReceiptCompactionAuthorityValid(
        BaseActivationReceiptCompactionAuthority authority,
        BaseActivationReceiptRetentionPolicy retention,
        string applicationId,
        string logicalStoreId)
    {
        if (!BaseActivationYieldReservationContract.IsValid(authority.Reservation)
            || !Enum.IsDefined(authority.BackupFloor.Kind)) return false;
        return retention.ProtectedBackupCoverage switch
        {
            BaseActivationProtectedBackupCoverage.NotRequired =>
                authority.BackupFloor.Kind == BaseActivationReceiptBackupFloorKind.NotApplicable
                && authority.BackupFloor.Checkpoint is null,
            BaseActivationProtectedBackupCoverage.Required =>
                authority.BackupFloor.Kind == BaseActivationReceiptBackupFloorKind.Checkpoint
                && BaseActivationBackupCoverageCheckpointContract.IsValid(authority.BackupFloor.Checkpoint)
                && authority.BackupFloor.Checkpoint!.ApplicationId == applicationId
                && authority.BackupFloor.Checkpoint.LogicalStoreId == logicalStoreId,
            _ => false,
        };
    }

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
        if (candidateCall.Outcome != BaseActivationProviderCallOutcome.Completed)
            return ActivationPageFailure<BaseActivationMigrationResult>(
                candidateCall.Outcome is BaseActivationProviderCallOutcome.TimedOut or BaseActivationProviderCallOutcome.Capacity
                    ? OperationStatus.CapabilityUnavailable : OperationStatus.StoreError,
                "base.activation.migrationConflict", ErrorCategory.Store);
        if (candidateCall.Value is null || candidateCall.Value.IsSuccess() && candidateCall.Value.Value is null)
            return ActivationProviderContractInvalid<BaseActivationMigrationResult>();
        if (!candidateCall.Value.IsSuccess())
            return NormalizeMigrationProviderFailure<BaseActivationMigrationResult, BaseActivationMigrationCandidate>(candidateCall.Value,
                candidatePhase: true);
        BaseActivationMigrationCandidate candidate = candidateCall.Value.Value!;
        if (candidate.ActivationId != request.ActivationId || candidate.Generation != request.ExpectedGeneration
            || candidate.SourceDefinition.Id != migration.Definition.Source.Id
            || candidate.SourceDefinition.Version != migration.Definition.Source.Version
            || !CryptographicOperations.FixedTimeEquals(
                candidate.SourceDefinition.Checksum.AsSpan(), migration.Definition.Source.Checksum.AsSpan())
            || candidate.State is not (BaseActivationState.Pending or BaseActivationState.RetryPending
                or BaseActivationState.Exhausted or BaseActivationState.Cancelled)
            || candidate.InputChecksum.Length != 32
            || !BaseActivationControlChecksumContract.Matches(
                candidate.ControlChecksum.AsSpan(), candidate.ActivationId, candidate.Generation, candidate.State,
                candidate.EffectiveDueAt, candidate.YieldCount, candidate.MaximumYields,
                candidate.ExecutionSliceOrdinal, candidate.AttemptStartedAt, candidate.SliceStartedAt,
                candidate.TerminalYieldDisposition, candidate.TerminalYieldFailureCode)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(candidate.CanonicalInput.AsSpan()), candidate.InputChecksum.AsSpan())
            || candidate.CanonicalInput.Length > source.Limits.MaximumInputBytes
            || candidate.Accounting.EvidenceBytes != checked(
                candidate.CanonicalInput.Length + candidate.InputChecksum.Length + candidate.ControlChecksum.Length)
            || !AccountingValid(candidate.Accounting, 1, source.Limits.Provider))
            return ActivationProviderContractInvalid<BaseActivationMigrationResult>();
        ImmutableArray<byte> replacementInput;
        try { replacementInput = migration.Project(candidate.CanonicalInput.AsSpan()); }
        catch (BaseActivationDtoContractException exception) when (exception.Code == "base.activation.providerContractInvalid")
        {
            return ActivationProviderContractInvalid<BaseActivationMigrationResult>();
        }
        catch (JsonException)
        { return ActivationPageFailure<BaseActivationMigrationResult>(OperationStatus.ValidationFailed, "base.activation.migrationInvalid", ErrorCategory.Validation); }
        if (replacementInput.Length > target.Limits.MaximumInputBytes)
            return ActivationPageFailure<BaseActivationMigrationResult>(OperationStatus.ValidationFailed, "base.activation.budgetExceeded", ErrorCategory.Validation);
        string replacementId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.migration.id.v1\0{Convert.ToHexString(migration.Definition.Checksum.AsSpan())}\n{request.ActivationId}\n{request.ExpectedGeneration}\n{Convert.ToHexString(request.Identity.Fingerprint.ToArray())}")));
        var intent = new BaseActivationCreateIntent
        {
            Ordinal = 0, Definition = migration.Definition.Target, MaximumYields = target.Limits.MaximumYields,
            ReceiptRetention = target.ReceiptRetention with { },
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
        if (migrated.Outcome != BaseActivationProviderCallOutcome.Completed)
            return ActivationPageFailure<BaseActivationMigrationResult>(OperationStatus.StoreError, "base.activation.storeError", ErrorCategory.Store);
        if (migrated.Value is null || migrated.Value.IsSuccess() && migrated.Value.Value is null)
            return ActivationProviderContractInvalid<BaseActivationMigrationResult>();
        if (migrated.Value.IsSuccess() && migrated.Value.Value is { } committed
            && (committed.SourceActivationId != request.ActivationId
                || committed.SourceDefinition.Id != migration.Definition.Source.Id
                || committed.SourceDefinition.Version != migration.Definition.Source.Version
                || !CryptographicOperations.FixedTimeEquals(
                    committed.SourceDefinition.Checksum.AsSpan(), migration.Definition.Source.Checksum.AsSpan())
                || committed.SourceGeneration != request.ExpectedGeneration + 1
                || !BaseActivationControlChecksumContract.Matches(
                    committed.SourceControlChecksum.AsSpan(), request.ActivationId,
                    request.ExpectedGeneration + 1, BaseActivationState.Migrated,
                    candidate.EffectiveDueAt, candidate.YieldCount, candidate.MaximumYields,
                    candidate.ExecutionSliceOrdinal, candidate.AttemptStartedAt, candidate.SliceStartedAt,
                    null, null)
                || committed.ReplacementActivationId != replacementId
                || committed.ReplacementDefinition.Id != migration.Definition.Target.Id
                || committed.ReplacementDefinition.Version != migration.Definition.Target.Version
                || !CryptographicOperations.FixedTimeEquals(
                    committed.ReplacementDefinition.Checksum.AsSpan(), migration.Definition.Target.Checksum.AsSpan())
                || committed.ReplacementGeneration != 1
                || !BaseActivationControlChecksumContract.Matches(
                    committed.ReplacementControlChecksum.AsSpan(), replacementId, 1, BaseActivationState.Pending,
                    intent.EffectiveDueAt ?? intent.RequestedDueAt, 0, intent.MaximumYields,
                    0, null, null, null, null)
                || committed.MigrationId != migration.Definition.Id
                || committed.MigrationVersion != migration.Definition.Version
                || !CryptographicOperations.FixedTimeEquals(
                    committed.MigrationChecksum.AsSpan(), migration.Definition.Checksum.AsSpan())
                || committed.Disposition is not (BaseMutationRequestDisposition.Committed or BaseMutationRequestDisposition.Duplicate)
                || !AccountingValid(committed.Accounting, 1, source.Limits.Provider)))
            return ActivationProviderContractInvalid<BaseActivationMigrationResult>();
        if (!migrated.Value.IsSuccess())
            return NormalizeMigrationProviderFailure<BaseActivationMigrationResult, BaseActivationMigrationResult>(migrated.Value,
                candidatePhase: false);
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
            || request is BaseActivationAdministrationReceiptCompactionRequest compaction
                && (compaction.AfterActivationId is null) != (compaction.AfterReceiptSequence is null)
            || request is BaseActivationAdministrationReceiptCompactionRequest { AfterReceiptSequence: < 1 }
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
        {
            if (request is BaseActivationAdministrationReceiptCompactionRequest
                && !KnownReceiptCompactionFailure(result))
                return ActivationProviderContractInvalid<TResult>();
            return BaseResultMapper.Map<TResult, TResult>(result, static value => value);
        }
        bool valid = result.Value switch
        {
            BaseActivationMaintenancePage page => ValidateMaintenancePage(page, request.Take, definition.Limits.Provider),
            BaseActivationPrunePage page => ValidatePrunePage(page, request.Take, definition.Limits.Provider),
            BaseActivationReceiptCompactionResult page => ValidateReceiptCompactionPage(page, request.Take, definition.Limits.Provider),
            _ => false,
        };
        if (valid) return BaseResultMapper.Map<TResult, TResult>(result, static value => value);
        return request is BaseActivationAdministrationReceiptCompactionRequest
            ? ActivationProviderContractInvalid<TResult>()
            : ActivationPageFailure<TResult>(OperationStatus.StoreError,
                "base.activation.providerContractInvalid", ErrorCategory.Store);
    }

    private static bool KnownReceiptCompactionFailure<TResult>(OperationResult<TResult> result) =>
        result.Error is { } error
        && (result.Status == OperationStatus.Conflict
                && error.Code is "base.activation.maintenanceConflict" or "base.activation.removalBlocked"
                && error.Category == ErrorCategory.Conflict
            || result.Status == OperationStatus.ValidationFailed
                && error.Code == "base.activation.budgetExceeded"
                && error.Category == ErrorCategory.Validation
            || result.Status == OperationStatus.StoreError
                && error.Code == "base.activation.receiptCorrupt"
                && error.Category == ErrorCategory.Store);

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
        int candidates = page.Items.Length;
        int boundaryProbe = page.Completed ? 0 : 1;
        long evidenceBytes = 0;
        foreach (BaseActivationPruneEvidence item in page.Items)
            evidenceBytes = checked(evidenceBytes + BaseActivationPruneEvidenceContract.MeasureCanonicalBytes(item));
        if (page.Items.Length > take || candidates > limits.MaximumCandidates
            || page.DeletedReceiptCount < 0
            || page.DeletedYieldReceiptCount < 0
            || page.DeletedYieldReceiptCount > page.DeletedReceiptCount
            || !BaseActivationInstanceReceiptChainContract.IsValid(page.PriorChain)
            || !BaseActivationInstanceReceiptChainContract.IsValid(page.ResultingChain)
            || !BaseActivationYieldReservationContract.IsValid(page.PriorReservation)
            || !BaseActivationYieldReservationContract.IsValid(page.ResultingReservation)
            || page.PriorChain.CurrentSequence != page.ResultingChain.CurrentSequence
            || !CryptographicOperations.FixedTimeEquals(
                page.PriorChain.OrderedChecksum.AsSpan(), page.ResultingChain.OrderedChecksum.AsSpan())
            || page.ResultingChain.Generation != page.PriorChain.Generation
                + (page.DeletedReceiptCount == 0 ? 0 : 1)
            || page.ResultingReservation.MaximumSlots != page.PriorReservation.MaximumSlots
            || page.ResultingReservation.ReservedUnusedSlots != page.PriorReservation.ReservedUnusedSlots
            || page.PriorReservation.RetainedUsedSlots < page.DeletedYieldReceiptCount
            || page.ResultingReservation.RetainedUsedSlots
                != page.PriorReservation.RetainedUsedSlots - page.DeletedYieldReceiptCount
            || page.ResultingReservation.Generation != page.PriorReservation.Generation
                + (page.DeletedYieldReceiptCount == 0 ? 0 : 1)
            || !AccountingValid(page.Accounting, candidates, limits)
            || page.Accounting.EvidenceBytes != evidenceBytes
            || page.Accounting.ReadIntervals != 1 + boundaryProbe
            || page.Accounting.IndexOperations != checked(
                1 + boundaryProbe + page.Items.Length * 2 + page.DeletedReceiptCount * 2)) return false;
        for (int index = 0; index < page.Items.Length; index++)
            if (!BaseActivationPruneEvidenceContract.IsValid(page.Items[index])
                || index != 0 && string.CompareOrdinal(page.Items[index - 1].ActivationId, page.Items[index].ActivationId) >= 0)
                return false;
        return page.Completed
            ? page.NextActivationId is null
            : page.Items.Length != 0 && page.NextActivationId == page.Items[^1].ActivationId;
    }

    private static bool ValidateReceiptCompactionPage(
        BaseActivationReceiptCompactionResult page,
        int take,
        BaseActivationExecutionLimits limits)
    {
        if (page.ExaminedCount < 0 || page.ExaminedCount > take
            || page.DeletedCount < 0 || page.DeletedCount > page.ExaminedCount
            || page.DeletedYieldReceiptCount < 0
            || page.DeletedYieldReceiptCount > page.DeletedCount
            || page.DeletedAuthorityOrderedDigest.Length != 32
            || !BaseActivationInstanceReceiptChainContract.IsValid(page.PriorChain)
            || !BaseActivationInstanceReceiptChainContract.IsValid(page.ResultingChain)
            || !BaseActivationYieldReservationContract.IsValid(page.PriorReservation)
            || !BaseActivationYieldReservationContract.IsValid(page.ResultingReservation)
            || page.PriorChain.CurrentSequence != page.ResultingChain.CurrentSequence
            || !CryptographicOperations.FixedTimeEquals(
                page.PriorChain.OrderedChecksum.AsSpan(), page.ResultingChain.OrderedChecksum.AsSpan())
            || page.ResultingChain.Generation < page.PriorChain.Generation
            || page.ResultingChain.Generation > page.PriorChain.Generation + 1
            || page.ResultingReservation.MaximumSlots != page.PriorReservation.MaximumSlots
            || page.ResultingReservation.ReservedUnusedSlots != page.PriorReservation.ReservedUnusedSlots
            || page.PriorReservation.RetainedUsedSlots < page.DeletedYieldReceiptCount
            || page.ResultingReservation.RetainedUsedSlots
                != page.PriorReservation.RetainedUsedSlots - page.DeletedYieldReceiptCount
            || !AccountingValid(page.Accounting, page.ExaminedCount, limits)
            || page.Completed != (page.Next is null)
            || page.Next is { ReceiptSequence: < 1 }
            || !Enum.IsDefined(page.Disposition)
            || page.Disposition is not (BaseMutationRequestDisposition.Committed or BaseMutationRequestDisposition.Duplicate))
            return false;
        return page.ResultingChain.Generation == page.PriorChain.Generation
                + (page.DeletedCount == 0 ? 0 : 1)
            && page.ResultingReservation.Generation == page.PriorReservation.Generation
                + (page.DeletedYieldReceiptCount == 0 ? 0 : 1);
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

    private BaseFailure<TResult> ActivationProviderContractInvalid<TResult>()
    {
        activationProviderGate.QuarantineContractViolation();
        OperationResult<TResult> result = BaseActivationFailureContract.ProviderContractInvalid<TResult>();
        return new(result.Status, result.Error!, null, null);
    }

    private BaseFailure<TResult> NormalizeMigrationProviderFailure<TResult, TProviderResult>(
        OperationResult<TProviderResult> result,
        bool candidatePhase)
    {
        BaseError? error = result.Error;
        bool accepted = error is not null && (
            result.Status == OperationStatus.Conflict
                && error.Category == ErrorCategory.Conflict
                && error.Code == "base.activation.migrationConflict"
            || candidatePhase
                && result.Status == OperationStatus.ValidationFailed
                && error.Category == ErrorCategory.Validation
                && error.Code == "base.activation.budgetExceeded"
            || !candidatePhase
                && result.Status == OperationStatus.CapabilityUnavailable
                && error.Category == ErrorCategory.Capability
                && error.Code == "base.activation.capacityUnavailable");
        return accepted
            ? ActivationPageFailure<TResult>(result.Status, error!.Code, error.Category)
            : ActivationProviderContractInvalid<TResult>();
    }

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

    private async ValueTask<bool> AuthorizeSemanticAdministrationAsync(string storeId, PrincipalContext principal,
        BaseSemanticActivationKeyDefinition definition, BaseOperationKind operationKind, CancellationToken cancellationToken)
    {
        var operation = new OperationContext
        {
            Operation = operationKind, CollectionId = definition.Id, Mode = OperationMode.System, Now = timeProvider.GetUtcNow(),
        };
        OperationResult<BasePolicyEvaluation> evaluation = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal, Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = definition.Id, Name = "Semantic activation authority", Kind = BaseCollectionKinds.Custom,
                System = true, SystemOwnerModuleId = definition.OwningModuleId, SchemaMode = SchemaMode.Strict,
                UnknownFields = UnknownFieldPolicy.Reject, Store = new StoreAnnotation { StoreId = storeId },
            },
            ResourceKind = PolicyResourceKind.AdminMetadata,
        }, cancellationToken).ConfigureAwait(false);
        return BaseSystemCollectionGate.HasExactModuleGrant(evaluation, definition.MaintenanceGrantId,
            definition.OwningModuleId, principal, operation);
    }

    private static bool SemanticInspectionPageValid(BaseSemanticActivationProviderInspectionRequest request,
        BaseSemanticActivationProviderInspectionPage page, BaseSemanticActivationStoreAuthorityRequirement current,
        BaseSemanticActivationKeyDefinition installed)
    {
        if (page.CapturedAuthorityGeneration != current.SemanticAuthorityGeneration || page.Items.Length > request.Take
            || page.ReadIntervals.Length != 1 || page.Accounting.SlotReads != page.Items.Length
            || page.Accounting.ReadIntervals != 1 || page.Accounting.IndexOperations != 1
            || page.Accounting.KeyBytes != checked(page.Items.Length * 32L)
            || page.Accounting.EvidenceBytes < 0 || page.Accounting.TransientBytes < page.Accounting.EvidenceBytes
            || page.Accounting.SlotReads > request.Limits.MaximumSlotReads
            || page.Accounting.EvidenceBytes > request.Limits.MaximumEvidenceBytes
            || page.Accounting.TransientBytes > request.Limits.MaximumTransientBytes
            || !CryptographicOperations.FixedTimeEquals(page.Checksum.AsSpan(),
                BaseSemanticActivationInspectionContract.PageChecksum(request, page).AsSpan())) return false;
        BaseSemanticActivationProviderInspectionBoundary? prior = request.After;
        long exactEvidenceBytes = 0;
        foreach (BaseSemanticActivationProviderInspectionItem item in page.Items)
        {
            if (!Enum.IsDefined(item.State) || request.State is not null && item.State != request.State.Value
                || item.SlotGeneration <= 0 || item.StateChecksum.Length != 32 || item.CanonicalStateAuthority.IsDefaultOrEmpty
                || item.Boundary.DefinitionId != request.Definition.Id
                || item.Boundary.CapturedAuthorityGeneration != current.SemanticAuthorityGeneration
                || item.Boundary.ScopeBindingId.Length != 32 || item.Boundary.RuntimeBoundaryChecksum.Length != 32
                || !CryptographicOperations.FixedTimeEquals(item.Boundary.RuntimeBoundaryChecksum.AsSpan(),
                    BaseSemanticActivationInspectionContract.BoundaryChecksum(request, item.Boundary.ScopeBindingId.AsSpan(),
                        item.Boundary.Key, current.SemanticAuthorityGeneration).AsSpan())
                || prior is not null && CompareBoundary(prior, item.Boundary) >= 0
                || !InspectionAuthorityValid(item, current, installed)) return false;
            exactEvidenceBytes = checked(exactEvidenceBytes + item.Boundary.ScopeBindingId.Length
                + BaseSemanticActivationKeyDigest.Length + item.CanonicalStateAuthority.Length + item.StateChecksum.Length + 20);
            prior = item.Boundary;
        }
        if (page.Accounting.EvidenceBytes != exactEvidenceBytes || page.Accounting.TransientBytes != exactEvidenceBytes) return false;
        if (page.Next is null ? page.Items.Length == request.Take : page.Items.Length != request.Take
            || page.Next is not null && (page.Items.IsDefaultOrEmpty || CompareBoundary(page.Items[^1].Boundary, page.Next) != 0)) return false;
        BaseAtomicReadIntervalEvidence interval = page.ReadIntervals[0];
        byte[] expectedLower = request.After is null ? Encoding.UTF8.GetBytes(request.Definition.Id) : request.After.RuntimeBoundaryChecksum.ToArray();
        byte[] expectedUpper = page.Items.IsDefaultOrEmpty ? expectedLower : page.Items[^1].Boundary.RuntimeBoundaryChecksum.ToArray();
        return interval.LogicalAccessPathId == "base.semanticActivation.inspection" && !interval.LowerInclusive && interval.UpperInclusive
            && interval.CanonicalLowerBound.AsSpan().SequenceEqual(expectedLower)
            && interval.CanonicalUpperBound.AsSpan().SequenceEqual(expectedUpper);
    }

    private static bool InspectionAuthorityValid(BaseSemanticActivationProviderInspectionItem item,
        BaseSemanticActivationStoreAuthorityRequirement current, BaseSemanticActivationKeyDefinition installed)
    {
        try
        {
            BaseSemanticActivationStoreAuthority store; BaseSemanticActivationDefinitionKey definition;
            BaseSemanticActivationKeyDigest key; long generation; ImmutableArray<byte> checksum;
            if (item.State == BaseSemanticActivationSlotState.Live)
            {
                BaseSemanticActivationLiveAuthority value = JsonSerializer.Deserialize(item.CanonicalStateAuthority.AsSpan(),
                    HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)!;
                store = value.StoreAuthority; definition = new() { Id = value.Definition.Id, Version = value.Definition.Version, Checksum = value.Definition.Checksum };
                key = value.KeyDigest; generation = value.SlotGeneration; checksum = value.Checksum;
                if (!value.ScopeBinding.BindingId.AsSpan().SequenceEqual(item.Boundary.ScopeBindingId.AsSpan())
                    || !CryptographicOperations.FixedTimeEquals(checksum.AsSpan(), BaseSemanticActivationEvidenceContract.LiveChecksum(value).AsSpan())) return false;
            }
            else if (item.State == BaseSemanticActivationSlotState.Retired)
            {
                BaseSemanticActivationRetirementAuthority value = JsonSerializer.Deserialize(item.CanonicalStateAuthority.AsSpan(),
                    HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)!;
                store = value.StoreAuthority; definition = value.Definition; key = value.KeyDigest; generation = value.SlotGeneration; checksum = value.Checksum;
                if (!CryptographicOperations.FixedTimeEquals(checksum.AsSpan(), BaseSemanticActivationEvidenceContract.RetirementChecksum(value).AsSpan())) return false;
            }
            else
            {
                BaseSemanticActivationAbsenceAuthority value = JsonSerializer.Deserialize(item.CanonicalStateAuthority.AsSpan(),
                    HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)!;
                store = value.StoreAuthority; definition = new() { Id = value.Definition.Id, Version = value.Definition.Version, Checksum = value.Definition.Checksum };
                key = value.Key; generation = value.FinalSlotGeneration; checksum = value.Checksum;
                if (!value.ScopeBindingId.AsSpan().SequenceEqual(item.Boundary.ScopeBindingId.AsSpan())
                    || !CryptographicOperations.FixedTimeEquals(checksum.AsSpan(), BaseSemanticActivationEvidenceContract.AbsenceChecksum(value).AsSpan())) return false;
            }
            Span<byte> itemKey = stackalloc byte[32]; Span<byte> boundaryKey = stackalloc byte[32]; key.CopyTo(itemKey); item.Boundary.Key.CopyTo(boundaryKey);
            return definition.Id == installed.Id && definition.Version == installed.Version
                && CryptographicOperations.FixedTimeEquals(definition.Checksum.AsSpan(), installed.Checksum.AsSpan())
                && generation == item.SlotGeneration && CryptographicOperations.FixedTimeEquals(itemKey, boundaryKey)
                && CryptographicOperations.FixedTimeEquals(checksum.AsSpan(), item.StateChecksum.AsSpan())
                && StoreAuthorityMatches(store.Requirement, current)
                && CryptographicOperations.FixedTimeEquals(store.Checksum.AsSpan(), BaseSemanticActivationEvidenceContract.StoreAuthorityChecksum(current).AsSpan());
        }
        catch { return false; }
    }

    private static bool StoreAuthorityMatches(BaseSemanticActivationStoreAuthorityRequirement left,
        BaseSemanticActivationStoreAuthorityRequirement right) =>
        left.ApplicationId == right.ApplicationId
        && left.LogicalStoreId == right.LogicalStoreId
        && left.StoreInstanceId == right.StoreInstanceId
        && left.RestoreEpoch == right.RestoreEpoch
        && left.SchemaGeneration == right.SchemaGeneration
        && left.SemanticAuthorityGeneration == right.SemanticAuthorityGeneration
        && CryptographicOperations.FixedTimeEquals(left.DefinitionSetChecksum.AsSpan(), right.DefinitionSetChecksum.AsSpan());

    private static int CompareBoundary(BaseSemanticActivationProviderInspectionBoundary left,
        BaseSemanticActivationProviderInspectionBoundary right)
    {
        int binding = left.ScopeBindingId.AsSpan().SequenceCompareTo(right.ScopeBindingId.AsSpan());
        if (binding != 0) return binding;
        Span<byte> a = stackalloc byte[32]; Span<byte> b = stackalloc byte[32]; left.Key.CopyTo(a); right.Key.CopyTo(b);
        return a.SequenceCompareTo(b);
    }

    private static bool ResolvedSemanticMaintenanceValid(BaseSemanticActivationMaintenanceResolutionRequest request,
        BaseSemanticActivationMaintenanceResult result)
    {
        if (!Enum.IsDefined(result.Disposition) || result.PreviousAuthorityGeneration <= 0
            || result.ResultingAuthorityGeneration < result.PreviousAuthorityGeneration || result.ExaminedRows < 0
            || result.ChangedRows < 0 || result.ChangedRows > result.ExaminedRows || result.CanonicalBytes < 0
            || !BaseSemanticActivationMaintenanceContract.ReceiptDispositionIsValid(result.Disposition, result.ReceiptDisposition)) return false;
        if (result.Disposition == BaseSemanticActivationMaintenanceDisposition.InProgress)
        {
            BaseSemanticActivationMaintenanceCheckpoint? checkpoint = result.Checkpoint;
            return checkpoint is not null && checkpoint.MaintenanceId == request.MaintenanceId
                && DefinitionAuthorityMatches(checkpoint.Definition, request.Definition)
                && CryptographicOperations.FixedTimeEquals(checkpoint.RequestFingerprint.AsSpan(), request.RequestFingerprint.AsSpan())
                && CryptographicOperations.FixedTimeEquals(checkpoint.Checksum.AsSpan(),
                    BaseSemanticActivationMaintenanceContract.CheckpointChecksum(checkpoint).AsSpan());
        }
        return result.Checkpoint is null && result.AuthorityChecksum.Length == 32 && result.ResultChecksum.Length == 32
            && result.CommitObservationChecksum.Length == 32
            && CryptographicOperations.FixedTimeEquals(result.ResultChecksum.AsSpan(),
                BaseSemanticActivationMaintenanceContract.ResultChecksum(result, result.AuthorityChecksum.AsSpan()).AsSpan())
            && CryptographicOperations.FixedTimeEquals(result.CommitObservationChecksum.AsSpan(),
                BaseSemanticActivationMaintenanceContract.CommitObservationChecksum(result.ResultChecksum.AsSpan()).AsSpan());
    }

    private static BaseSemanticActivationMaintenanceResult CloneSemanticMaintenance(BaseSemanticActivationMaintenanceResult value) => value with
    {
        AuthorityChecksum = value.AuthorityChecksum.ToArray().ToImmutableArray(),
        ResultChecksum = value.ResultChecksum.ToArray().ToImmutableArray(),
        CommitObservationChecksum = value.CommitObservationChecksum.ToArray().ToImmutableArray(),
        Checkpoint = value.Checkpoint is null ? null : value.Checkpoint with
        {
            Definition = value.Checkpoint.Definition with { Checksum = value.Checkpoint.Definition.Checksum.ToArray().ToImmutableArray() },
            After = value.Checkpoint.After is null ? null : value.Checkpoint.After with
            {
                ScopeBindingId = value.Checkpoint.After.ScopeBindingId.ToArray().ToImmutableArray(),
            },
            RollingChecksum = value.Checkpoint.RollingChecksum.ToArray().ToImmutableArray(),
            RequestFingerprint = value.Checkpoint.RequestFingerprint.ToArray().ToImmutableArray(),
            Checksum = value.Checkpoint.Checksum.ToArray().ToImmutableArray(),
        },
    };

    private static bool DefinitionAuthorityMatches(BaseSemanticActivationDefinitionKey left,
        BaseSemanticActivationDefinitionKey right) => left.Id == right.Id && left.Version == right.Version
        && CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static BaseFailure<T> SemanticAdminFailure<T>(OperationStatus status, string code, ErrorCategory category) => new(
        status, new BaseError { Code = code, Message = "The semantic activation administration request could not be completed.", Category = category }, null, null);

    private static BaseFailure<T> SemanticProviderCallFailure<T>(BaseActivationProviderCallOutcome outcome) => outcome switch
    {
        BaseActivationProviderCallOutcome.TimedOut => SemanticAdminFailure<T>(OperationStatus.StoreError,
            BaseSemanticActivationErrorCodes.MaintenanceTimeout, ErrorCategory.Store),
        BaseActivationProviderCallOutcome.Cancelled => SemanticAdminFailure<T>(OperationStatus.StoreError,
            BaseSemanticActivationErrorCodes.MaintenanceTimeout, ErrorCategory.Unexpected),
        BaseActivationProviderCallOutcome.Capacity => SemanticAdminFailure<T>(OperationStatus.CapabilityUnavailable,
            BaseSemanticActivationErrorCodes.CapacityUnavailable, ErrorCategory.Capability),
        _ => SemanticAdminFailure<T>(OperationStatus.StoreError,
            BaseSemanticActivationErrorCodes.ProviderContractInvalid, ErrorCategory.Store),
    };

    private static BaseSemanticActivationMaintenanceRequest WithEffectiveSemanticMaintenanceLimits(
        BaseSemanticActivationMaintenanceRequest request, BaseSemanticActivationKeyDefinition definition,
        IReadOnlyList<BaseSemanticActivationKeyDefinition> definitions,
        IReadOnlyList<BaseSemanticActivationRemovalAuthority> removals, BaseSemanticActivationCapability capability)
    {
        BaseSemanticActivationKeyDefinition[] completeDefinitions = definitions.Concat(removals.Select(static value => value.From))
            .GroupBy(static value => (value.Id, value.Version, Checksum: Convert.ToHexString(value.Checksum.AsSpan())))
            .Select(static value => value.First()).ToArray();
        long installedRows = completeDefinitions.Aggregate(0L, static (sum, item) => checked(sum
            + item.Limits.MaximumLiveSlots + item.Limits.MaximumRetiredSlots + item.Limits.MaximumAbsenceMarkers));
        long installedBytes = completeDefinitions.Aggregate(0L, static (sum, item) => checked(sum + item.Limits.Execution.MaximumTransientBytes));
        var limits = request.Limits with
        {
            PageSize = Math.Min(request.Limits.PageSize, Math.Min(256, capability.MaximumMaintenancePageSize)),
            MaximumRows = Math.Min(request.Limits.MaximumRows, Math.Min(installedRows,
                checked(capability.MaximumLiveSlots + capability.MaximumRetiredSlots + capability.MaximumAbsenceMarkers))),
            MaximumBytes = Math.Min(request.Limits.MaximumBytes, Math.Min(installedBytes, capability.MaximumTransientBytes)),
            Deadline = request.Limits.Deadline < definition.Limits.Deadlines.MaintenanceTimeout
                ? request.Limits.Deadline : definition.Limits.Deadlines.MaintenanceTimeout,
        };
        return request switch
        {
            BaseSemanticActivationCompactRequest value => value with { Limits = limits },
            BaseSemanticActivationMigrateRequest value => value with { Limits = limits },
            BaseSemanticActivationRemoveRequest value => value with { Limits = limits },
            _ => throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid),
        };
    }

    private static bool SemanticControlAuthorityValid(BaseSemanticActivationMaintenanceAuthorityRequest request,
        BaseSemanticActivationMaintenanceAuthority value)
    {
        try
        {
            return value.SemanticAuthorityGeneration == request.SemanticAuthorityGeneration && value.LiveCount >= 0
                && value.RetiredCount >= 0 && value.AbsenceCount >= 0 && value.ExaminedRows >= 0 && value.CanonicalBytes >= 0
                && value.ExaminedRows == checked(value.LiveCount + value.RetiredCount + value.AbsenceCount)
                && value.ExaminedRows <= request.MaximumRows && value.CanonicalBytes <= request.MaximumBytes
                && value.RetiredAuthorityChecksum.Length == 32 && value.DefinitionStateChecksum.Length == 32
                && value.AbsenceAuthorityChecksum.Length == 32 && value.Checksum.Length == 32
                && CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(),
                    BaseSemanticActivationMaintenanceAuthorityContract.Checksum(request, value).AsSpan());
        }
        catch (OverflowException) { return false; }
    }

    private static bool SemanticOperationalStatusValid(BaseSemanticActivationOperationalStatus value) =>
        value.ActiveOperations >= 0 && value.RetainedOperations >= 0 && value.MaximumRetainedOperations > 0
        && value.RetainedOperations <= value.MaximumRetainedOperations
        && !(value.Ready && value.Quarantined);

    private async ValueTask<bool> SemanticControlTokenCurrentAsync(BaseSemanticActivationControlTokenPayload payload,
        bool allowGenerationAdvance, CancellationToken cancellationToken)
    {
        if (!CryptographicOperations.FixedTimeEquals(payload.DefinitionSetChecksum.AsSpan(), semanticActivations.DefinitionSetChecksum.AsSpan()))
            return false;
        BaseSemanticActivationKeyDefinition? installed = semanticActivations.Find(payload.Definition.Id, payload.Definition.Version)
            ?? semanticRemovals.Find(payload.Definition)?.From;
        if (installed is null || !CryptographicOperations.FixedTimeEquals(installed.Checksum.AsSpan(), payload.Definition.Checksum.AsSpan())) return false;
        RecordStoreRegistration? registration = stores.GetRegistration(payload.LogicalStoreId);
        IAtomicRecordStore? atomic = registration?.AtomicExecutionStore ?? registration?.Store as IAtomicRecordStore;
        BaseRegisteredModuleMutationDefinition? operation = moduleMutations.Find(installed.EnsureOperation.OperationId,
            installed.EnsureOperation.OperationVersion);
        if (atomic is null || operation is null) return false;
        OperationResult<BaseAtomicMutationAuthorityRequirement> captured = await atomic.CaptureAtomicMutationAuthorityRequirementAsync(
            features.LogicalSchema.ApplicationId, [], DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(operation.Limits), cancellationToken).ConfigureAwait(false);
        return captured.IsSuccess() && captured.Value?.SemanticActivation is { } authority
            && authority.LogicalStoreId == payload.LogicalStoreId && authority.RestoreEpoch == payload.RestoreEpoch
            && (allowGenerationAdvance ? authority.SemanticAuthorityGeneration >= payload.SemanticAuthorityGeneration
                : authority.SemanticAuthorityGeneration == payload.SemanticAuthorityGeneration);
    }

    private async ValueTask<bool> AuthorizeSemanticControlPayloadAsync(string storeId, PrincipalContext principal,
        BaseSemanticActivationControlTokenPayload payload, CancellationToken cancellationToken)
    {
        BaseSemanticActivationKeyDefinition? installed = semanticActivations.Find(payload.Definition.Id, payload.Definition.Version)
            ?? semanticRemovals.Find(payload.Definition)?.From;
        return installed is not null && storeId == payload.LogicalStoreId
            && CryptographicOperations.FixedTimeEquals(installed.Checksum.AsSpan(), payload.Definition.Checksum.AsSpan())
            && await AuthorizeSemanticAdministrationAsync(storeId, principal, installed,
                BaseOperationKind.SemanticRecoveryMaintenance, cancellationToken).ConfigureAwait(false);
    }

    private static string Confirmation(BaseSemanticActivationControlTokenKind kind) => kind switch
    {
        BaseSemanticActivationControlTokenKind.Compact => "compact-retired-semantic-authority",
        BaseSemanticActivationControlTokenKind.Remove => "remove-semantic-definition",
        BaseSemanticActivationControlTokenKind.ResumeCompact or BaseSemanticActivationControlTokenKind.ResumeRemove => "resume-semantic-maintenance",
        _ => string.Empty,
    };

    private BaseSemanticActivationMaintenanceRequest ControlRequest(BaseSemanticActivationControlTokenPayload payload,
        string idempotencyKey)
    {
        bool compact = IsCompactControl(payload.Kind);
        BaseMutationRequestIdentity identity = SemanticControlIdentity(payload, idempotencyKey);
        if (compact)
            return new BaseSemanticActivationCompactRequest
            {
                Identity = identity, Definition = payload.Definition,
                ExpectedSemanticAuthorityGeneration = payload.SemanticAuthorityGeneration, Limits = payload.Limits,
                ExpectedRetiredCount = payload.RetiredCount, ExpectedRetiredChecksum = payload.RetiredAuthorityChecksum,
            };
        BaseSemanticActivationRemovalAuthority removal = semanticRemovals.Find(payload.Definition)
            ?? throw new InvalidOperationException(BaseSemanticActivationErrorCodes.RemovalBlocked);
        if (!CryptographicOperations.FixedTimeEquals(removal.ResultingDefinitionSetChecksum.AsSpan(), payload.DefinitionSetChecksum.AsSpan()))
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.GraphChanged);
        return new BaseSemanticActivationRemoveRequest
        {
            Identity = identity, Definition = payload.Definition,
            ExpectedSemanticAuthorityGeneration = payload.SemanticAuthorityGeneration, Limits = payload.Limits,
            RemovalAuthority = removal, ExpectedLiveCount = payload.LiveCount, ExpectedRetiredCount = payload.RetiredCount,
            ExpectedAbsenceCount = payload.AbsenceCount, ExpectedDefinitionStateChecksum = payload.DefinitionStateChecksum,
            ExpectedAbsenceAuthorityChecksum = payload.AbsenceAuthorityChecksum,
        };
    }

    internal static bool IsCompactControl(BaseSemanticActivationControlTokenKind kind) => kind is
        BaseSemanticActivationControlTokenKind.Compact or BaseSemanticActivationControlTokenKind.ResumeCompact
        or BaseSemanticActivationControlTokenKind.ResolveCompact;

    internal static BaseMutationRequestIdentity SemanticControlIdentity(
        BaseSemanticActivationControlTokenPayload payload, string idempotencyKey)
    {
        string normalizedIdempotencyKey = BaseMutationRequestIdentity.NormalizeIdempotencyKey(idempotencyKey);
        bool compact = IsCompactControl(payload.Kind); int semanticKind = compact ? 1 : 2;
        byte[] semantic = SHA256.HashData(Encoding.UTF8.GetBytes($"base.semanticActivation.control.v1\0{semanticKind}\0{payload.Definition.Id}\0{payload.Definition.Version}\0{payload.SemanticAuthorityGeneration}\0{normalizedIdempotencyKey}"));
        return BaseMutationRequestIdentity.Create($"semantic-activation:{payload.Definition.Id}",
            compact ? "semanticActivation.compact" : "semanticActivation.remove", normalizedIdempotencyKey,
            BaseMutationRequestFingerprint.Create(semantic));
    }

    private BaseResult<BaseSemanticActivationControlResult> ControlResult(
        BaseSemanticActivationControlTokenPayload payload, string idempotencyKey,
        BaseSemanticActivationMaintenanceRequest request, BaseResult<BaseSemanticActivationMaintenanceResult> result)
    {
        if (result is BaseFailure<BaseSemanticActivationMaintenanceResult> failure)
        {
            if (failure.Error.Code != BaseSemanticActivationErrorCodes.MaintenanceIndeterminate)
                return new BaseFailure<BaseSemanticActivationControlResult>(failure.Status, failure.Error, failure.Warnings, failure.Diagnostics);
            BaseSemanticActivationControlTokenKind kind = payload.Kind is BaseSemanticActivationControlTokenKind.Remove or BaseSemanticActivationControlTokenKind.ResumeRemove
                ? BaseSemanticActivationControlTokenKind.ResolveRemove : BaseSemanticActivationControlTokenKind.ResolveCompact;
            BaseSemanticActivationControlToken resolution = semanticControlTokens.Protect(payload with
            { Kind = kind, IdempotencyKey = idempotencyKey, ExpiresAtUtc = timeProvider.GetUtcNow().AddMinutes(15) });
            return new BaseSuccess<BaseSemanticActivationControlResult>(new()
            {
                Disposition = BaseSemanticActivationMaintenanceDisposition.Indeterminate,
                AuthorityGeneration = payload.SemanticAuthorityGeneration, ExaminedRows = 0, ChangedRows = 0,
                CanonicalBytes = 0, ReceiptDisposition = null, Resume = null, Resolution = resolution,
                SanitizedChecksum = SHA256.HashData(BaseSemanticActivationMaintenanceContract.RequestFingerprint(request).AsSpan()).ToImmutableArray(),
            }, OperationStatus.Ok, null, null, null, null);
        }
        BaseSuccess<BaseSemanticActivationMaintenanceResult> success = (BaseSuccess<BaseSemanticActivationMaintenanceResult>)result;
        BaseSemanticActivationMaintenanceResult value = success.Value;
        BaseSemanticActivationControlToken? resume = value.Disposition == BaseSemanticActivationMaintenanceDisposition.InProgress
            ? semanticControlTokens.Protect(payload with
            {
                Kind = payload.Kind is BaseSemanticActivationControlTokenKind.Remove or BaseSemanticActivationControlTokenKind.ResumeRemove
                    ? BaseSemanticActivationControlTokenKind.ResumeRemove : BaseSemanticActivationControlTokenKind.ResumeCompact,
                IdempotencyKey = idempotencyKey, ExpiresAtUtc = timeProvider.GetUtcNow().AddMinutes(15),
            }) : null;
        return new BaseSuccess<BaseSemanticActivationControlResult>(new()
        {
            Disposition = value.Disposition, AuthorityGeneration = value.ResultingAuthorityGeneration,
            ExaminedRows = value.ExaminedRows, ChangedRows = value.ChangedRows, CanonicalBytes = value.CanonicalBytes,
            ReceiptDisposition = value.ReceiptDisposition, Resume = resume, Resolution = null,
            SanitizedChecksum = SHA256.HashData(value.ResultChecksum.AsSpan()).ToImmutableArray(),
        }, success.Status, success.Warnings, success.Revision, success.Events, success.Diagnostics);
    }

    private static BaseSemanticActivationExecutionLimits EffectiveSemanticInspectionLimits(
        BaseSemanticActivationExecutionLimits requested, BaseSemanticActivationExecutionLimits installed,
        BaseSemanticActivationCapability provider) => new()
    {
        MaximumOperations = 1,
        MaximumScopeDirectoryReads = Math.Min(requested.MaximumScopeDirectoryReads, Math.Min(installed.MaximumScopeDirectoryReads, provider.MaximumScopeDirectoryReads)),
        MaximumSlotReads = Math.Min(requested.MaximumSlotReads, provider.MaximumInspectionSlotReads),
        MaximumActivationReads = Math.Min(requested.MaximumActivationReads, Math.Min(installed.MaximumActivationReads, provider.MaximumActivationReads)),
        MaximumReadIntervals = Math.Min(requested.MaximumReadIntervals, Math.Min(installed.MaximumReadIntervals, provider.MaximumReadIntervals)),
        MaximumIndexOperations = Math.Min(requested.MaximumIndexOperations, Math.Min(installed.MaximumIndexOperations, provider.MaximumIndexOperations)),
        MaximumActivationBytes = Math.Min(requested.MaximumActivationBytes, Math.Min(installed.MaximumActivationBytes, provider.MaximumActivationBytes)),
        MaximumScopeDirectoryBytes = Math.Min(requested.MaximumScopeDirectoryBytes, Math.Min(installed.MaximumScopeDirectoryBytes, provider.MaximumScopeDirectoryBytes)),
        MaximumEvidenceBytes = Math.Min(requested.MaximumEvidenceBytes, Math.Min(installed.MaximumEvidenceBytes, provider.MaximumEvidenceBytes)),
        MaximumReceiptBytes = Math.Min(requested.MaximumReceiptBytes, Math.Min(installed.MaximumReceiptBytes, provider.MaximumReceiptBytes)),
        MaximumTransientBytes = Math.Min(requested.MaximumTransientBytes, Math.Min(installed.MaximumTransientBytes, provider.MaximumTransientBytes)),
    };

    private static BaseFailure<BaseSemanticActivationMaintenanceResult> NormalizeSemanticProviderFailure(
        BaseFailure<BaseSemanticActivationMaintenanceResult> failure) => failure.Error.Code switch
    {
        BaseSemanticActivationErrorCodes.Invalid => SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed, BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation),
        BaseSemanticActivationErrorCodes.BudgetExceeded => SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed, BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation),
        BaseSemanticActivationErrorCodes.GraphChanged or BaseSemanticActivationErrorCodes.FingerprintConflict
            or BaseSemanticActivationErrorCodes.CompactionBlocked or BaseSemanticActivationErrorCodes.MigrationBlocked
            or BaseSemanticActivationErrorCodes.RemovalBlocked => SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict, failure.Error.Code, ErrorCategory.Conflict),
        BaseSemanticActivationErrorCodes.CapabilityUnavailable or BaseSemanticActivationErrorCodes.CapacityUnavailable =>
            SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.CapabilityUnavailable, failure.Error.Code, ErrorCategory.Capability),
        BaseSemanticActivationErrorCodes.MaintenanceTimeout or BaseSemanticActivationErrorCodes.MaintenanceIndeterminate
            or BaseSemanticActivationErrorCodes.Corrupt => SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError, failure.Error.Code, ErrorCategory.Store),
        _ => SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
            BaseSemanticActivationErrorCodes.ProviderContractInvalid, ErrorCategory.Store),
    };

    private static BaseFailure<BaseSemanticActivationInspectionPage> NormalizeSemanticInspectionProviderFailure(
        BaseFailure<BaseSemanticActivationProviderInspectionPage> failure) => failure.Error.Code switch
    {
        BaseSemanticActivationErrorCodes.Invalid => SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.ValidationFailed, BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation),
        BaseSemanticActivationErrorCodes.BudgetExceeded =>
            SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.ValidationFailed, failure.Error.Code, ErrorCategory.Validation),
        BaseSemanticActivationErrorCodes.GraphChanged =>
            SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.Conflict, failure.Error.Code, ErrorCategory.Conflict),
        BaseSemanticActivationErrorCodes.CapabilityUnavailable or BaseSemanticActivationErrorCodes.CapacityUnavailable =>
            SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.CapabilityUnavailable, failure.Error.Code, ErrorCategory.Capability),
        BaseSemanticActivationErrorCodes.MaintenanceTimeout or BaseSemanticActivationErrorCodes.Corrupt =>
            SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.StoreError, failure.Error.Code, ErrorCategory.Store),
        _ => SemanticAdminFailure<BaseSemanticActivationInspectionPage>(OperationStatus.StoreError,
            BaseSemanticActivationErrorCodes.ProviderContractInvalid, ErrorCategory.Store),
    };

    private static bool SemanticMaintenanceRequestValid(BaseSemanticActivationMaintenanceRequest request) =>
        request.Identity is not null && request.ExpectedSemanticAuthorityGeneration > 0
        && request.Limits.PageSize is >= 1 and <= 256 && request.Limits.MaximumPages > 0
        && request.Limits.MaximumRows > 0 && request.Limits.MaximumBytes > 0 && request.Limits.Deadline > TimeSpan.Zero
        && request switch
        {
            BaseSemanticActivationCompactRequest value => value.ExpectedRetiredCount >= 0 && value.ExpectedRetiredChecksum.Length == 32,
            BaseSemanticActivationMigrateRequest value => value.Migration is not null && value.Migration.Checksum.Length == 32,
            BaseSemanticActivationRemoveRequest value => value.ExpectedLiveCount >= 0 && value.ExpectedRetiredCount >= 0
                && value.ExpectedAbsenceCount >= 0 && value.ExpectedDefinitionStateChecksum.Length == 32
                && value.ExpectedAbsenceAuthorityChecksum.Length == 32
                && value.RemovalAuthority is not null && value.RemovalAuthority.Checksum.Length == 32,
            _ => false,
        };

    private static bool SemanticExecutionLimitsValid(BaseSemanticActivationExecutionLimits value) =>
        value.MaximumOperations == 1 && value.MaximumScopeDirectoryReads > 0 && value.MaximumSlotReads > 0
        && value.MaximumActivationReads > 0 && value.MaximumReadIntervals > 0 && value.MaximumIndexOperations > 0
        && value.MaximumActivationBytes > 0 && value.MaximumScopeDirectoryBytes > 0 && value.MaximumEvidenceBytes > 0
        && value.MaximumReceiptBytes > 0 && value.MaximumTransientBytes > 0;

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
