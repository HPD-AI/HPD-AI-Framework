using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed class DefaultBaseActivationWorkerRuntime(
    IRecordStoreRegistry stores,
    IBasePolicyOrchestrator policy,
    BaseActivationAcceptedTimeAuthority acceptedTime) : IBaseActivationWorkerRuntime
{
    public async ValueTask<OperationResult<BaseActivationDueObservation>> ObserveAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        CancellationToken cancellationToken)
    {
        OperationResult<BaseActivationWorkerAuthority> authority = await AuthorizeAsync(session, definition, cancellationToken).ConfigureAwait(false);
        if (!authority.IsSuccess() || authority.Value is null)
            return CopyFailure<BaseActivationDueObservation, BaseActivationWorkerAuthority>(authority);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseActivationDueObservation>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        OperationResult<BaseActivationDueObservation> result = await provider.ObserveDueAsync(new BaseActivationDueObservationRequest
        {
            ApplicationId = session.ApplicationId,
            WorkerModuleId = definition.OwningModuleId,
            Definitions = authority.Value.Definitions,
            Scope = authority.Value.Scope,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            MaximumCandidates = Math.Min(1, definition.Limits.Provider.MaximumCandidates),
            Limits = definition.Limits.Provider,
        }, cancellationToken).ConfigureAwait(false);
        return ValidateObservation(result, definition.Limits.Provider);
    }

    public async ValueTask<OperationResult<BaseActivationClaimResult>> ClaimAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseDueObservationToken observation,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(identity);
        OperationResult<BaseActivationWorkerAuthority> authority = await AuthorizeAsync(session, definition, cancellationToken).ConfigureAwait(false);
        if (!authority.IsSuccess() || authority.Value is null)
            return CopyFailure<BaseActivationClaimResult, BaseActivationWorkerAuthority>(authority);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseActivationClaimResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        OperationResult<BaseActivationClaimResult> result = await provider.TryClaimNextAsync(new BaseActivationClaimRequest
        {
            Observation = observation,
            Worker = authority.Value,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            LeaseMilliseconds = checked((long)definition.Limits.LeaseDuration.TotalMilliseconds),
            Identity = identity,
            Limits = definition.Limits.Provider,
        }, cancellationToken).ConfigureAwait(false);
        return ValidateClaim(result, definition, authority.Value);
    }

    public async ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationClaimAuthority claim,
        BaseActivationLeaseObservation lease,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(session, definition, cancellationToken).ConfigureAwait(false))
            return Failure<BaseActivationRenewResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseActivationRenewResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        OperationResult<BaseActivationRenewResult> result = await provider.RenewAsync(new BaseActivationRenewRequest
        {
            Claim = claim,
            ExpectedLeaseRevision = lease.LeaseRevision,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            ExtensionMilliseconds = checked((long)definition.Limits.LeaseDuration.TotalMilliseconds),
            Identity = identity,
            Limits = definition.Limits.Provider,
        }, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null) return result;
        return ClaimsEqual(result.Value.Claim, claim) && result.Value.Lease.LeaseRevision == checked(lease.LeaseRevision + 1)
            ? result
            : Failure<BaseActivationRenewResult>(OperationStatus.StoreError, "base.activation.providerContractInvalid", ErrorCategory.Store);
    }

    public ValueTask<OperationResult<BaseActivationTransitionResult>> CompleteAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationClaimAuthority claim,
        ImmutableArray<byte> result,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken) =>
        TransitionAsync(session, definition, new BaseActivationCompleteRequest
        {
            ActivationId = claim.ActivationId,
            Claim = claim,
            CanonicalResult = result,
            ResultChecksum = SHA256.HashData(result.AsSpan()).ToImmutableArray(),
            Identity = identity,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            Limits = definition.Limits.Provider,
        }, cancellationToken);

    public ValueTask<OperationResult<BaseActivationTransitionResult>> FailAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationClaimAuthority claim,
        string failureCode,
        bool retry,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken) =>
        FailCoreAsync(session, definition, claim, failureCode, retry, identity, cancellationToken);

    private ValueTask<OperationResult<BaseActivationTransitionResult>> FailCoreAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationClaimAuthority claim,
        string failureCode,
        bool retry,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        BaseAcceptedTimeReceipt now = acceptedTime.Capture(session.ApplicationId);
        bool retryAllowed = retry && claim.AttemptNumber < definition.Retry.MaximumAttempts &&
            definition.Retry.RetryableFailureCodes.Contains(failureCode, StringComparer.Ordinal);
        long? retryDueAt = retryAllowed
            ? checked(now.CapturedUtc + RetryDelay(definition.Retry, claim.ActivationId, claim.AttemptNumber))
            : null;
        return TransitionAsync(session, definition, new BaseActivationFailRequest
        {
            ActivationId = claim.ActivationId,
            Claim = claim,
            StableFailureCode = failureCode,
            Disposition = retryAllowed ? BaseActivationFailureDisposition.Retry : BaseActivationFailureDisposition.Exhaust,
            RetryDueAt = retryDueAt,
            Identity = identity,
            AcceptedTime = now,
            Limits = definition.Limits.Provider,
        }, cancellationToken);
    }

    private async ValueTask<OperationResult<BaseActivationTransitionResult>> TransitionAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationTransitionRequest request,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(session, definition, cancellationToken).ConfigureAwait(false))
            return Failure<BaseActivationTransitionResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        IBaseActivationProvider? provider = ResolveProvider();
        return provider is null
            ? Failure<BaseActivationTransitionResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported)
            : await provider.TransitionAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OperationResult<BaseActivationWorkerAuthority>> AuthorizeAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(session, definition, cancellationToken).ConfigureAwait(false))
            return Failure<BaseActivationWorkerAuthority>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        BaseOwnedScopeSeekAuthority scope = Scope(session.ActivationScope);
        BaseActivationDefinitionKey key = new()
        { Id = definition.Id, Version = definition.Version, Checksum = definition.Checksum.ToArray().ToImmutableArray() };
        string worker = session.Principal.SubjectId ?? string.Empty;
        byte[] checksum = Hash($"base.activation.worker.v2\0{session.ApplicationId}\n{definition.OwningModuleId}\n{worker}\n{definition.Id}\n{definition.Version}\n{Convert.ToHexString(definition.Checksum.AsSpan())}\n{Convert.ToHexString(scope.ProtectedIndexDigest.AsSpan())}");
        return OperationResults.Ok(new BaseActivationWorkerAuthority
        {
            ApplicationId = session.ApplicationId,
            ModuleId = definition.OwningModuleId,
            WorkerIdentity = worker,
            Definitions = [key],
            Scope = scope,
            Checksum = checksum.ToImmutableArray(),
        });
    }

    private async ValueTask<bool> IsAuthorizedAsync(BaseSession session, BaseActivationDefinition definition, CancellationToken cancellationToken)
    {
        if (!BaseSystemCollectionGate.Allows(session.Principal))
            return false;
        OperationContext operation = session.Operation(BaseOperationKind.ActivationClaim, definition.Id);
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = session.Principal,
            Operation = operation,
            Collection = PolicyResource(definition),
            ResourceKind = PolicyResourceKind.ActivationDefinition,
        }, cancellationToken).ConfigureAwait(false);
        return BaseSystemCollectionGate.HasExactActivationGrant(
            authorization, definition.ExecuteGrantId, definition.OwningModuleId, session.Principal, operation);
    }

    private IBaseActivationProvider? ResolveProvider()
    {
        IBaseActivationProvider[] values = stores.GetRegistrations().Select(static item => item.Store)
            .OfType<IBaseActivationProvider>().Distinct().ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static OperationResult<BaseActivationDueObservation> ValidateObservation(
        OperationResult<BaseActivationDueObservation> result,
        BaseActivationExecutionLimits limits)
    {
        if (!result.IsSuccess() || result.Value is null) return result;
        BaseActivationDueObservation value = result.Value;
        bool valid = value.Token.Value is { Length: > 0 } && value.Intervals.Length is > 0 &&
            value.Intervals.Length <= limits.MaximumReadIntervals && value.Accounting.Candidates <= limits.MaximumCandidates &&
            value.Accounting.EvidenceBytes <= limits.MaximumEvidenceBytes && value.Accounting.TransientBytes <= limits.MaximumTransientBytes;
        return valid ? result : Failure<BaseActivationDueObservation>(OperationStatus.StoreError, "base.activation.providerContractInvalid", ErrorCategory.Store);
    }

    private static OperationResult<BaseActivationClaimResult> ValidateClaim(
        OperationResult<BaseActivationClaimResult> result,
        BaseActivationDefinition definition,
        BaseActivationWorkerAuthority worker)
    {
        if (!result.IsSuccess() || result.Value is null) return result;
        if (result.Value is not BaseActivationClaimedResult claimed) return result;
        bool valid = claimed.Payload.Definition.Id == definition.Id && claimed.Payload.Definition.Version == definition.Version &&
            CryptographicOperations.FixedTimeEquals(claimed.Payload.Definition.Checksum.AsSpan(), definition.Checksum.AsSpan()) &&
            claimed.Payload.CanonicalInput.Length <= definition.Limits.MaximumInputBytes &&
            claimed.Claim.ActivationId == claimed.Payload.ActivationId && claimed.Claim.AttemptNumber == claimed.Attempt.AttemptNumber &&
            claimed.Claim.FencingToken.Length == 32 && claimed.Lease.LeaseRevision > 0 && claimed.Lease.LeaseExpiresAt > claimed.Attempt.StartedAt &&
            claimed.Intervals.Length > 0 && claimed.Accounting.EvidenceBytes <= definition.Limits.Provider.MaximumEvidenceBytes &&
            worker.Definitions.Length == 1;
        return valid ? result : Failure<BaseActivationClaimResult>(OperationStatus.StoreError, "base.activation.providerContractInvalid", ErrorCategory.Store);
    }

    private static BaseOwnedScopeSeekAuthority Scope(BaseOwnedSubjectScopeEvidence scope) => new()
    {
        Kind = scope.Kind,
        ProtectedIndexDigest = Hash($"base.activation.scope.v2\0{(int)scope.Kind}\n{scope.Value ?? string.Empty}").ToImmutableArray(),
    };

    private static CollectionDefinition PolicyResource(BaseActivationDefinition definition) => new()
    {
        Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
        SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
        System = true, SystemOwnerModuleId = definition.OwningModuleId,
    };

    private static bool ClaimsEqual(BaseActivationClaimAuthority left, BaseActivationClaimAuthority right) =>
        left.ActivationId == right.ActivationId &&
        left.AttemptNumber == right.AttemptNumber && left.ClaimEpoch == right.ClaimEpoch &&
        left.CancellationGeneration == right.CancellationGeneration && left.StoreInstanceId == right.StoreInstanceId &&
        left.RestoreEpoch == right.RestoreEpoch && CryptographicOperations.FixedTimeEquals(left.FencingToken.AsSpan(), right.FencingToken.AsSpan());

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static long RetryDelay(BaseActivationRetryProfile profile, string activationId, int attemptNumber)
    {
        long delay = profile.InitialDelayMilliseconds;
        for (int index = 1; index < attemptNumber; index++)
        {
            long numerator = checked(delay * profile.MultiplierNumerator);
            delay = checked((numerator + profile.MultiplierDenominator - 1) / profile.MultiplierDenominator);
            delay = Math.Min(delay, profile.MaximumDelayMilliseconds);
        }
        long maximumJitter = checked((delay * profile.JitterBasisPoints + 9_999) / 10_000);
        if (maximumJitter == 0) return delay;
        byte[] digest = Hash($"base.activation.retry.jitter.v2\0{activationId}\n{attemptNumber}");
        ulong sample = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(digest);
        return checked(Math.Min(profile.MaximumDelayMilliseconds, delay + (long)(sample % checked((ulong)maximumJitter + 1))));
    }
    private static OperationResult<T> Failure<T>(OperationStatus status, string code, ErrorCategory category) => new()
    { Status = status, Error = new BaseError { Code = code, Message = "The activation operation could not be completed.", Category = category } };
    private static OperationResult<T> CopyFailure<T, TSource>(OperationResult<TSource> source) => new()
    { Status = source.Status, Error = source.Error, Warnings = source.Warnings, Diagnostics = source.Diagnostics };
}
