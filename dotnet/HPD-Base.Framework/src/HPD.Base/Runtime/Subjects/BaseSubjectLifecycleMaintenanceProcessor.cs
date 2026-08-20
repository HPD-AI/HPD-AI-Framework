using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed class BaseSubjectLifecycleMaintenanceProcessor : IBaseSubjectLifecycleMaintenanceProcessor
{
    internal BaseSubjectLifecycleMaintenanceResult? Result { get; private set; }

    public async ValueTask<RecordMutationExecutionResult> ExecuteAsync(
        IBaseSubjectLifecycleMaintenanceSession session,
        BaseSubjectLifecycleMaintenanceExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(request);
        if (!Valid(request)) return Failure(BaseSubjectErrorCodes.LifecycleContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation);
        OperationResult<BaseSubjectLifecycleMaintenanceResult> execution = await session.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (!execution.IsSuccess() || execution.Value is null)
            return Failure(execution.Error?.Code ?? BaseSubjectErrorCodes.LifecycleProviderContractInvalid, execution.Status, execution.Error?.Category ?? ErrorCategory.Store);
        BaseSubjectLifecycleMaintenanceResult value = execution.Value;
        if (value.Kind != request.Kind || value.ExaminedCount < value.ChangedCount || value.ChangedCount < 0 || value.CanonicalBytes < 0
            || value.DeliveryEpoch < request.ExpectedDeliveryEpoch || value.ProjectionGeneration is < 1
            || value.RollingChecksum.Length != 64 || !value.RollingChecksum.All(Uri.IsHexDigit))
            return Failure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        Result = value with { };
        return new RecordMutationExecutionResult(RecordMutationExecutionOutcome.Committed,
            new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, []));
    }

    private static bool Valid(BaseSubjectLifecycleMaintenanceExecutionRequest request)
    {
        if (request.Identity is null) return false;
        try
        {
            BaseMutationRequestIdentity normalized = BaseMutationRequestIdentity.Create(
                request.Identity.Scope, request.Identity.Operation, request.Identity.IdempotencyKey, request.Identity.Fingerprint);
            if (!string.Equals(normalized.Scope, request.Identity.Scope, StringComparison.Ordinal)
                || !string.Equals(normalized.Operation, request.Identity.Operation, StringComparison.Ordinal)
                || !string.Equals(normalized.IdempotencyKey, request.Identity.IdempotencyKey, StringComparison.Ordinal)) return false;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return false; }
        if (request.FormatVersion != 1 || !Enum.IsDefined(request.Kind) || request.PlanChecksum is not { Length: 32 } || request.ExpectedStoreGeneration < 0 || request.ExpectedSchemaGeneration < 0 || request.ExpectedRestoreEpoch < 0
            || request.ExpectedDeliveryEpoch < 1 || request.ExpectedScopeProtectionGeneration < 1 || string.IsNullOrWhiteSpace(request.ExpectedScopeProtectionKeyId)
            || request.PageSize is < 1 or > 256 || request.OperationTimeout < TimeSpan.FromMilliseconds(100) || request.OperationTimeout > TimeSpan.FromMinutes(30)
            || request.CommitCompletionTimeout < TimeSpan.FromMilliseconds(100) || request.CommitCompletionTimeout > TimeSpan.FromMinutes(5)) return false;
        bool consumerKind = request.Kind is BaseSubjectLifecycleMaintenanceKind.MarkCheckpointOvertaken or BaseSubjectLifecycleMaintenanceKind.RemoveConsumer or BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection;
        if (consumerKind != (request.ConsumerId is not null) || consumerKind != request.ConsumerVersion.HasValue || request.ConsumerVersion is < 1) return false;
        if (consumerKind != request.ExpectedProjectionGeneration.HasValue || request.ExpectedProjectionGeneration is < 1) return false;
        bool contractKind = request.Kind is not (BaseSubjectLifecycleMaintenanceKind.RestoreTransform or BaseSubjectLifecycleMaintenanceKind.RecoverPublication or BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection);
        if (contractKind != (request.ContractId is not null) || contractKind != request.ContractVersion.HasValue || request.ContractVersion is < 1) return false;
        if (request.Kind == BaseSubjectLifecycleMaintenanceKind.MarkCheckpointOvertaken && (request.Scope is null || request.RetainedFrom is null)) return false;
        if (request.Kind == BaseSubjectLifecycleMaintenanceKind.Prune && request.RetainedFrom is null) return false;
        if ((request.Kind == BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection) != (request.ReplacementScopeProtectionKeyId is not null)) return false;
        return CryptographicOperations.FixedTimeEquals(request.PlanChecksum, PlanChecksum(request));
    }

    internal static byte[] PlanChecksum(BaseSubjectLifecycleMaintenanceExecutionRequest request)
    {
        string framed = $"base.subjectLifecycle.maintenance.v1\0{request.FormatVersion}\0{request.Identity.Scope}\0{request.Identity.Operation}\0{request.Identity.IdempotencyKey}\0{Convert.ToHexStringLower(request.Identity.Fingerprint.ToArray())}\0{(int)request.Kind}\0{request.ContractId}\0{request.ContractVersion}\0{request.ConsumerId}\0{request.ConsumerVersion}\0{(int?)request.Scope?.Kind}\0{request.Scope?.Value}\0{request.RetainedFrom?.CommitPosition.Value}\0{request.RetainedFrom?.SubjectId.Value}\0{request.RetainedFrom?.AuthorityEpoch.ToBase64Url()}\0{request.RetainedFrom?.Incarnation.ToBase64Url()}\0{request.RetainedFrom?.SubjectSequence}\0{request.ExpectedStoreGeneration}\0{request.ExpectedSchemaGeneration}\0{request.ExpectedRestoreEpoch}\0{request.ExpectedDeliveryEpoch}\0{request.ExpectedProjectionGeneration}\0{request.ExpectedScopeProtectionGeneration}\0{request.ExpectedScopeProtectionKeyId}\0{request.ReplacementScopeProtectionKeyId}\0{(request.LastCanonicalKey is null ? null : Convert.ToHexStringLower(request.LastCanonicalKey))}\0{request.PageSize}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(framed));
    }

    private static RecordMutationExecutionResult Failure(string code, OperationStatus status, ErrorCategory category)
    {
        BaseError error = code.StartsWith("base.subjectLifecycle.", StringComparison.Ordinal)
            ? BaseSubjectFailureContract.Error(code)
            : new BaseError { Code = code, Category = category, Message = "The identified request conflicts with stored evidence." };
        return new RecordMutationExecutionResult(RecordMutationExecutionOutcome.RollbackConfirmed,
            new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.Failed, [], error), error);
    }
}
