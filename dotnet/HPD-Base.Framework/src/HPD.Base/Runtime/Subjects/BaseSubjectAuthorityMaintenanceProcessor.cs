using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed class BaseSubjectAuthorityMaintenanceProcessor : IBaseSubjectAuthorityMaintenanceProcessor
{
    internal BaseSubjectLifecycleMaintenanceResult? LifecycleResult { get; private set; }
    internal BaseSubjectRetirementMaintenanceResult? RetirementResult { get; private set; }

    public async ValueTask<RecordMutationExecutionResult> ExecuteAsync(
        IBaseSubjectAuthorityMaintenanceSession session,
        BaseSubjectAuthorityMaintenanceExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        if (!Valid(request)) return Failure(BaseSubjectErrorCodes.LifecycleContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation);

        BaseSubjectAuthorityMaintenancePageRequest page = new()
        {
            FormatVersion = 1,
            LifecycleKind = request.Lifecycle.Kind,
            RetirementKind = request.Retirement?.Kind,
            PageOrdinal = 1,
            CombinedPlanChecksum = request.CombinedPlanChecksum.ToArray(),
            LastCanonicalKey = null,
            PageSize = request.PageSize,
        };
        BaseSubjectAuthorityMaintenancePageResult? final = null;
        do
        {
            OperationResult<BaseSubjectAuthorityMaintenancePageResult> execution = await session.ExecutePageAsync(page, cancellationToken).ConfigureAwait(false);
            if (!execution.IsSuccess() || execution.Value is null)
                return Failure(execution.Error?.Code ?? BaseSubjectErrorCodes.LifecycleProviderContractInvalid, execution.Status, execution.Error?.Category ?? ErrorCategory.Store);
            BaseSubjectAuthorityMaintenancePageResult value = execution.Value;
            if (value.PageOrdinal != page.PageOrdinal || value.LifecycleExaminedCount < value.LifecycleChangedCount
                || value.RetirementExaminedCount < value.RetirementChangedCount || value.CanonicalBytes < 0
                || value.RollingChecksum.Length != 64 || !value.RollingChecksum.All(Uri.IsHexDigit)
                || value.HasMore != (value.NextCanonicalKey is not null) || value.NextCanonicalKey is { Length: > 4096 })
                return Failure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            if(value.HasMore?(value.LifecycleResult is not null||value.RetirementResult is not null)
                : value.LifecycleResult is null||(request.Retirement is null&&value.RetirementResult is not null)||(request.Retirement is not null&&value.RetirementResult is null))
                return Failure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid,OperationStatus.CapabilityUnavailable,ErrorCategory.Capability);
            final = value;
            if (value.HasMore)
            {
                page = page with { PageOrdinal = checked(page.PageOrdinal + 1), LastCanonicalKey = value.NextCanonicalKey!.ToArray() };
            }
        } while (final.HasMore);

        LifecycleResult=final.LifecycleResult;
        RetirementResult=final.RetirementResult;
        return new RecordMutationExecutionResult(RecordMutationExecutionOutcome.Committed,
            new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, []));
    }

    private static bool Valid(BaseSubjectAuthorityMaintenanceExecutionRequest request)
    {
        if (request.Identity is null || request.Lifecycle.PlanChecksum is not { Length: 32 }
            || request.CombinedPlanChecksum is not { Length: 32 } || request.ExpectedStoreGeneration < 0
            || request.ExpectedSchemaGeneration < 0 || request.ExpectedRestoreEpoch < 0
            || request.ExpectedScopeProtectionGeneration < 1 || string.IsNullOrWhiteSpace(request.ExpectedScopeProtectionKeyId)
            || request.PageSize is < 1 or > 256 || request.OperationTimeout < TimeSpan.FromMilliseconds(100)
            || request.OperationTimeout > TimeSpan.FromMinutes(30) || request.CommitCompletionTimeout < TimeSpan.FromMilliseconds(100)
            || request.CommitCompletionTimeout > TimeSpan.FromMinutes(5)) return false;
        bool semanticAbsent = request.ExpectedSemanticActivationAuthorityGeneration is null
            && request.ExpectedSemanticActivationDefinitionSetChecksum.IsDefaultOrEmpty;
        bool semanticPresent = request.ExpectedSemanticActivationAuthorityGeneration is > 0
            && request.ExpectedSemanticActivationDefinitionSetChecksum.Length == 32;
        if (!semanticAbsent && !semanticPresent) return false;
        if (request.Retirement is not null && request.PageSize != 256) return false;
        return CryptographicOperations.FixedTimeEquals(request.CombinedPlanChecksum, PlanChecksum(request));
    }

    internal static byte[] PlanChecksum(BaseSubjectAuthorityMaintenanceExecutionRequest request)
    {
        BaseSubjectLifecycleMaintenancePlan plan = request.Lifecycle;
        string framed = $"base.subjectAuthority.maintenance.v2\0{request.Identity.Scope}\0{request.Identity.Operation}\0{request.Identity.IdempotencyKey}\0{Convert.ToHexStringLower(request.Identity.Fingerprint.ToArray())}\0{(int)plan.Kind}\0{plan.ContractId}\0{plan.ContractVersion}\0{plan.ConsumerId}\0{plan.ConsumerVersion}\0{(int?)plan.Scope?.Kind}\0{plan.Scope?.Value}\0{plan.RetainedFrom?.CommitPosition.Value}\0{plan.RetainedFrom?.SubjectId.Value}\0{plan.RetainedFrom?.AuthorityEpoch.ToBase64Url()}\0{plan.RetainedFrom?.Incarnation.ToBase64Url()}\0{plan.RetainedFrom?.SubjectSequence}\0{Convert.ToHexStringLower(plan.PlanChecksum)}\0{request.ExpectedStoreGeneration}\0{request.ExpectedSchemaGeneration}\0{request.ExpectedRestoreEpoch}\0{plan.ExpectedDeliveryEpoch}\0{plan.ExpectedProjectionGeneration}\0{request.ExpectedScopeProtectionGeneration}\0{request.ExpectedScopeProtectionKeyId}\0{request.ReplacementScopeProtectionKeyId}\0{request.ExpectedSemanticActivationAuthorityGeneration}\0{Convert.ToHexStringLower(request.ExpectedSemanticActivationDefinitionSetChecksum.AsSpan())}\0{request.PageSize}\0{(int?)request.Retirement?.Kind}\0{request.Retirement?.ExpectedGraphGeneration}\0{request.Retirement?.ExpectedBarrierControlGeneration}\0{(request.Retirement is null?string.Empty:Convert.ToHexStringLower(request.Retirement.PlanChecksum))}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(framed));
    }

    private static RecordMutationExecutionResult Failure(string code, OperationStatus status, ErrorCategory category)
    {
        BaseError error = code.StartsWith("base.subject", StringComparison.Ordinal)
            ? BaseSubjectFailureContract.Error(code)
            : new BaseError { Code = code, Category = category, Message = "The identified request conflicts with stored evidence." };
        return new RecordMutationExecutionResult(RecordMutationExecutionOutcome.RollbackConfirmed,
            new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.Failed, [], error), error);
    }
}
