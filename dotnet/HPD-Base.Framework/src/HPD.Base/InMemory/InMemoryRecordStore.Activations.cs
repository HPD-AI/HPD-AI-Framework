using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore
{
    private static readonly BaseActivationAccounting EmptyActivationAccounting = new()
    {
        Candidates = 0,
        Comparisons = 0,
        IndexOperations = 1,
        ReadIntervals = 1,
        EvidenceBytes = 0,
        TransientBytes = 0,
    };

    /// <inheritdoc />
    public BaseActivationProviderDescriptor Descriptor { get; } = new()
    {
        ProviderId = "hpd.base.inMemory.activations",
        ProviderVersion = "1",
        ProtocolVersion = 2,
        Capability = new BaseActivationProviderCapability
        {
            AtomicCreationSupported = true,
            SelectionTargetSupported = true,
            ModuleTargetSupported = true,
            GuardedChildrenSupported = true,
            RestoreFencingSupported = true,
            DueInvalidation = BaseDueInvalidationClass.Native,
            ScheduleKinds = [BaseScheduleKind.Once, BaseScheduleKind.Interval, BaseScheduleKind.Cron, BaseScheduleKind.Calendar],
            ExecutionClasses = [BaseActivationExecutionClass.TransactionalOperation, BaseActivationExecutionClass.AtLeastOnceWorker, BaseActivationExecutionClass.AtMostOnceEffect],
            MaximumActivationsPerTransaction = 256,
            MaximumDueCandidates = 256,
            MaximumInputBytes = 4L * 1024 * 1024,
            MaximumResultBytes = 4L * 1024 * 1024,
            MaximumEvidenceBytes = 16L * 1024 * 1024,
            MaximumTransientBytes = 16L * 1024 * 1024,
            MaximumReceiptBytes = 16L * 1024 * 1024,
            MaximumPendingRows = 1_000_000,
            MaximumClaimedRows = 1_000_000,
            MaximumTerminalRows = 1_000_000,
            MaximumAttempts = 1024,
            MaximumRenewalsPerAttempt = 4096,
            MaximumChildrenPerAttempt = 4096,
            MaximumLineageDepth = 256,
            MaximumOccurrencePage = 256,
            MaximumTimeZoneBytes = 64L * 1024 * 1024,
            AcquisitionDeadline = TimeSpan.FromSeconds(5),
            TransactionDeadline = TimeSpan.FromSeconds(30),
            ObservationWaitDeadline = TimeSpan.FromMinutes(5),
            RenewalDeadline = TimeSpan.FromSeconds(5),
            CommitObservationDeadline = TimeSpan.FromSeconds(30),
            ReceiptResolutionDeadline = TimeSpan.FromSeconds(30),
            MaintenanceDeadline = TimeSpan.FromMinutes(5),
            ShutdownDrainDeadline = TimeSpan.FromSeconds(60),
            ProviderQuarantineSlots = 32,
            HandlerQuarantineSlots = 32,
            CanonicalChecksum = ImmutableArray.CreateRange(SHA256.HashData("hpd.base.inMemory.activations.v2"u8)),
        },
    };

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationDueObservation>> ObserveDueAsync(
        BaseActivationDueObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationDueObservation>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (!ValidateLimits(request.Limits) || request.MaximumCandidates < 1 ||
            request.MaximumCandidates > Math.Min(256, request.Limits.MaximumCandidates))
            return ActivationFailure<BaseActivationDueObservation>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            List<InMemoryActivationRow> eligible = EligibleRows(
                state, request.Definitions, request.Scope, request.AcceptedTime.CapturedUtc, request.After);
            int inspected = Math.Min(eligible.Count, request.MaximumCandidates);
            InMemoryActivationRow? first = eligible.FirstOrDefault();
            BaseActivationDueBoundary? boundary = first is null ? null : Boundary(first, request.AcceptedTime.CapturedUtc);
            byte[] token = DueToken(
                state.ActivationIndexGeneration,
                request.AcceptedTime.CapturedUtc,
                request.Scope.ProtectedIndexDigest.AsSpan(),
                request.Definitions,
                boundary);
            BaseAtomicReadIntervalEvidence interval = DueInterval(request.Scope, request.AcceptedTime.CapturedUtc, request.After, boundary);
            long evidenceBytes = checked(token.Length + IntervalBytes(interval));
            if (evidenceBytes > request.Limits.MaximumEvidenceBytes)
                return ActivationFailure<BaseActivationDueObservation>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);

            return OperationResults.Ok(new BaseActivationDueObservation
            {
                Earliest = boundary,
                Token = new BaseDueObservationToken { Value = token.ToImmutableArray() },
                Intervals = [interval],
                Accounting = new BaseActivationAccounting
                {
                    Candidates = inspected,
                    Comparisons = inspected,
                    IndexOperations = 1,
                    ReadIntervals = 1,
                    EvidenceBytes = evidenceBytes,
                    TransientBytes = evidenceBytes,
                },
            });
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<BaseDueWaitResult> WaitForDueChangeAsync(
        BaseDueObservationToken token,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long observedGeneration = DecodeDueGeneration(token.Value.AsSpan());
            if (observedGeneration < 0)
                return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.TokenInvalid };
            if (Volatile.Read(ref _publishedState).ActivationIndexGeneration != observedGeneration)
                return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.Changed };
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25), cancellationToken)
                .ConfigureAwait(false);
        }
        return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.Deadline };
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationClaimResult>> TryClaimNextAsync(
        BaseActivationClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationClaimResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (!ValidateLimits(request.Limits) || request.LeaseMilliseconds <= 0)
            return ActivationFailure<BaseActivationClaimResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadActivationReceipt(current, request.Identity, "activation-claimed",
                HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult, static value => value,
                out OperationResult<BaseActivationClaimResult>? replay))
                return ResolveClaimReplay(current, replay, request.AcceptedTime.CapturedUtc);
            long tokenGeneration = DecodeDueGeneration(request.Observation.Value.AsSpan());
            if (tokenGeneration != current.ActivationIndexGeneration)
                return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationObservationChangedResult(
                    new BaseDueObservationToken { Value = CurrentWorkerToken(current, request).ToImmutableArray() }));

            List<InMemoryActivationRow> eligible = EligibleRows(
                current, request.Worker.Definitions, request.Worker.Scope, request.AcceptedTime.CapturedUtc, null);
            if (eligible.Count == 0)
                return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimEmptyResult(
                    new BaseDueObservationToken { Value = CurrentWorkerToken(current, request).ToImmutableArray() }));

            InMemoryActivationRow row = eligible[0];
            var next = current.Clone();
            InMemoryActivationRow mutable = next.Activations[row.Payload.ActivationId];
            if (mutable.State == BaseActivationState.Claimed && mutable.Lease is not null &&
                mutable.Lease.LeaseExpiresAt <= request.AcceptedTime.CapturedUtc)
            {
                long recoveredGeneration = checked(mutable.Generation + 1);
                next.Activations[row.Payload.ActivationId] = mutable with
                {
                    State = BaseActivationState.RetryPending,
                    Generation = recoveredGeneration,
                    Claim = null,
                    Lease = null,
                    EffectiveDueAt = request.AcceptedTime.CapturedUtc,
                    ControlChecksum = ControlChecksum(row.Payload.ActivationId, recoveredGeneration, BaseActivationState.RetryPending),
                };
                next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
                BaseActivationClaimResult recoveredResult = new BaseActivationRecoveredClaimResult(
                    row.Payload.ActivationId, recoveredGeneration);
                WriteActivationReceipt(next, request.Identity, "activation-claimed", recoveredResult,
                    HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult);
                Volatile.Write(ref _publishedState, next);
                return OperationResults.Ok(recoveredResult);
            }

            int attemptNumber = checked(mutable.AttemptNumber + 1);
            long claimEpoch = checked(mutable.ClaimEpoch + 1);
            long generation = checked(mutable.Generation + 1);
            byte[] fence = Hash($"base.activation.claim.v2\0{mutable.Payload.ActivationId}\n{attemptNumber}\n{claimEpoch}\n{request.Worker.WorkerIdentity}");
            var claim = new BaseActivationClaimAuthority
            {
                ActivationId = mutable.Payload.ActivationId,
                AttemptNumber = attemptNumber,
                ClaimEpoch = claimEpoch,
                FencingToken = fence.ToImmutableArray(),
                WorkerIdentity = request.Worker.WorkerIdentity,
                CancellationGeneration = 0,
                StoreInstanceId = _options.StoreId,
                RestoreEpoch = 0,
                DefinitionChecksum = mutable.Payload.Definition.Checksum.ToArray().ToImmutableArray(),
            };
            long expiresAt = checked(request.AcceptedTime.CapturedUtc + request.LeaseMilliseconds);
            var lease = new BaseActivationLeaseObservation
            {
                LeaseRevision = 1,
                LeaseExpiresAt = expiresAt,
                Checksum = Hash($"base.activation.lease.v2\0{mutable.Payload.ActivationId}\n1\n{expiresAt}").ToImmutableArray(),
            };
            byte[] controlChecksum = ControlChecksum(mutable.Payload.ActivationId, generation, BaseActivationState.Claimed);
            next.Activations[mutable.Payload.ActivationId] = mutable with
            {
                State = BaseActivationState.Claimed,
                Generation = generation,
                AttemptNumber = attemptNumber,
                ClaimEpoch = claimEpoch,
                Claim = claim,
                Lease = lease,
                ControlChecksum = controlChecksum,
            };
            next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
            var attempt = new BaseActivationAttemptEvidence
            {
                AttemptId = $"{mutable.Payload.ActivationId}:{attemptNumber}",
                AttemptNumber = attemptNumber,
                StartedAt = request.AcceptedTime.CapturedUtc,
                Checksum = Hash($"base.activation.attempt.v2\0{mutable.Payload.ActivationId}\n{attemptNumber}").ToImmutableArray(),
            };
            BaseActivationClaimResult claimedResult = new BaseActivationClaimedResult(
                mutable.Payload.DeepClone(),
                claim,
                lease,
                attempt,
                [DueInterval(request.Worker.Scope, request.AcceptedTime.CapturedUtc, null, Boundary(mutable, request.AcceptedTime.CapturedUtc))],
                EmptyActivationAccounting with { Candidates = 1, Comparisons = 1 });
            WriteActivationReceipt(next, request.Identity, "activation-claimed", claimedResult,
                HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(claimedResult);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(
        BaseActivationRenewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationRenewResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadActivationReceipt(current, request.Identity, "activation-renewed",
                HPDBaseJsonSerializerContext.Default.BaseActivationRenewResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseActivationRenewResult>? replay))
                return replay;
            if (!current.Activations.TryGetValue(request.Claim.ActivationId, out InMemoryActivationRow? row) ||
                !ClaimMatches(row, request.Claim) || row.Lease?.LeaseRevision != request.ExpectedLeaseRevision ||
                row.Lease.LeaseExpiresAt <= request.AcceptedTime.CapturedUtc)
                return ActivationFailure<BaseActivationRenewResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            long revision = checked(request.ExpectedLeaseRevision + 1);
            long expiresAt = checked(request.AcceptedTime.CapturedUtc + request.ExtensionMilliseconds);
            var lease = new BaseActivationLeaseObservation
            {
                LeaseRevision = revision,
                LeaseExpiresAt = expiresAt,
                Checksum = Hash($"base.activation.lease.v2\0{row.Payload.ActivationId}\n{revision}\n{expiresAt}").ToImmutableArray(),
            };
            var next = current.Clone();
            next.Activations[row.Payload.ActivationId] = next.Activations[row.Payload.ActivationId] with { Lease = lease };
            var result = new BaseActivationRenewResult
            {
                Claim = request.Claim,
                Lease = lease,
                Accounting = EmptyActivationAccounting with { ReadIntervals = 0, IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteActivationReceipt(next, request.Identity, "activation-renewed", result, HPDBaseJsonSerializerContext.Default.BaseActivationRenewResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationTransitionResult>> TransitionAsync(
        BaseActivationTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            string receiptKind = ActivationTransitionReceiptKind(request);
            if (TryReadActivationReceipt(current, request.Identity, receiptKind,
                HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseActivationTransitionResult>? replay))
                return replay;
            if (!current.Activations.TryGetValue(request.ActivationId, out InMemoryActivationRow? row))
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.notFound", OperationStatus.NotFound, ErrorCategory.NotFound);

            if (request is BaseActivationEffectHeartbeatRequest effectHeartbeat)
            {
                if (row.State != BaseActivationState.EffectStarted || row.Effect is null ||
                    !EffectMatches(row.Effect, effectHeartbeat.Effect) ||
                    row.Effect.HeartbeatRevision != effectHeartbeat.ExpectedHeartbeatRevision || effectHeartbeat.ExtensionMilliseconds <= 0 ||
                    !CurrentExecutorAllows(current, row.Effect.Executor, request.AcceptedTime.CapturedUtc))
                    return ActivationFailure<BaseActivationTransitionResult>("base.activation.effectLost", OperationStatus.Conflict, ErrorCategory.Conflict);
                BaseEffectExecutionAuthority replacement = Effect(row.Effect.Claim, row.Effect.Executor,
                    row.Effect.EffectStartGeneration, checked(row.Effect.HeartbeatRevision + 1),
                    checked(request.AcceptedTime.CapturedUtc + effectHeartbeat.ExtensionMilliseconds));
                var heartbeatState = current.Clone();
                heartbeatState.Activations[row.Payload.ActivationId] = heartbeatState.Activations[row.Payload.ActivationId] with { Effect = replacement };
                var heartbeatResult = new BaseActivationTransitionResult
                {
                    State = row.State, Generation = row.Generation, ControlChecksum = row.ControlChecksum.ToImmutableArray(),
                    Accounting = EmptyActivationAccounting with { IndexOperations = 1 }, Disposition = BaseMutationRequestDisposition.Committed,
                    Effect = replacement,
                };
                WriteActivationReceipt(heartbeatState, request.Identity, receiptKind, heartbeatResult,
                    HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult);
                Volatile.Write(ref _publishedState, heartbeatState);
                return OperationResults.Ok(heartbeatResult);
            }

            BaseActivationState resultingState;
            byte[]? result = null;
            BaseEffectExecutionAuthority? resultingEffect = null;
            switch (request)
            {
                case BaseActivationCompleteRequest complete when ClaimMatches(row, complete.Claim):
                    if (!CryptographicOperations.FixedTimeEquals(
                        SHA256.HashData(complete.CanonicalResult.AsSpan()), complete.ResultChecksum.AsSpan()))
                        return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                    resultingState = BaseActivationState.Succeeded;
                    result = complete.CanonicalResult.ToArray();
                    break;
                case BaseActivationFailRequest failed when ClaimMatches(row, failed.Claim):
                    if ((failed.Disposition == BaseActivationFailureDisposition.Retry) != failed.RetryDueAt.HasValue ||
                        failed.RetryDueAt is < 0)
                        return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                    resultingState = failed.Disposition == BaseActivationFailureDisposition.Retry
                        ? BaseActivationState.RetryPending
                        : BaseActivationState.Exhausted;
                    break;
                case BaseActivationCancelRequest cancel when row.Generation == cancel.ExpectedGeneration:
                    resultingState = row.State == BaseActivationState.EffectStarted
                        ? BaseActivationState.EffectStarted
                        : BaseActivationState.Cancelled;
                    resultingEffect = row.State == BaseActivationState.EffectStarted ? row.Effect : null;
                    break;
                case BaseActivationBeginEffectRequest begin when ClaimMatches(row, begin.Claim) && begin.HeartbeatMilliseconds > 0 &&
                    CurrentExecutorAllows(current, begin.Executor, request.AcceptedTime.CapturedUtc) &&
                    HeartbeatsEqual(current.Executors[ExecutorKey(begin.Executor.ApplicationId, begin.Executor.HostId, begin.Executor.ProcessIncarnationId)].Heartbeat, begin.ExecutorHeartbeat):
                    resultingState = BaseActivationState.EffectStarted;
                    resultingEffect = Effect(begin.Claim, begin.Executor, checked(row.Generation + 1), 1,
                        checked(request.AcceptedTime.CapturedUtc + begin.HeartbeatMilliseconds));
                    break;
                case BaseActivationCompleteEffectRequest completeEffect when row.State == BaseActivationState.EffectStarted && row.Effect is not null &&
                    EffectMatches(row.Effect, completeEffect.Effect):
                    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(completeEffect.CanonicalResult.AsSpan()), completeEffect.ResultChecksum.AsSpan()))
                        return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                    resultingState = BaseActivationState.Succeeded;
                    result = completeEffect.CanonicalResult.ToArray();
                    break;
                case BaseActivationRecoverEffectRequest recover when row.State == BaseActivationState.EffectStarted && row.Effect is not null &&
                    EffectMatches(row.Effect, recover.Effect) && row.Effect.HeartbeatExpiresAt <= request.AcceptedTime.CapturedUtc &&
                    !CurrentExecutorAllows(current, row.Effect.Executor, request.AcceptedTime.CapturedUtc):
                    resultingState = BaseActivationState.OutcomeUnknown;
                    resultingEffect = row.Effect;
                    break;
                case BaseActivationReconcileEffectRequest reconcile when row.State == BaseActivationState.OutcomeUnknown && row.Effect is not null &&
                    row.Generation == reconcile.ExpectedGeneration &&
                    row.Effect.EffectStartGeneration == reconcile.ExpectedEffectStartGeneration &&
                    reconcile.ExpectedEffectChecksum.Length == 32 &&
                    CryptographicOperations.FixedTimeEquals(row.Effect.Checksum.AsSpan(), reconcile.ExpectedEffectChecksum.AsSpan()):
                    if (reconcile.VerificationEvidence.IsDefaultOrEmpty || reconcile.VerificationChecksum.Length != 32 ||
                        !Enum.IsDefined(reconcile.Disposition) ||
                        !CryptographicOperations.FixedTimeEquals(
                            SHA256.HashData(reconcile.VerificationEvidence.AsSpan()), reconcile.VerificationChecksum.AsSpan()))
                        return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                    resultingState = reconcile.Disposition switch
                    {
                        BaseEffectReconciliationDisposition.Succeeded => BaseActivationState.Succeeded,
                        BaseEffectReconciliationDisposition.Exhausted => BaseActivationState.Exhausted,
                        BaseEffectReconciliationDisposition.Disposed => BaseActivationState.Disposed,
                        _ => BaseActivationState.OutcomeUnknown,
                    };
                    result = reconcile.Disposition == BaseEffectReconciliationDisposition.Succeeded
                        ? reconcile.VerificationEvidence.ToArray()
                        : null;
                    break;
                case BaseActivationOperatorRetryRequest retry when row.State == BaseActivationState.Exhausted &&
                    row.Generation == retry.ExpectedGeneration && retry.RetryDueAt >= request.AcceptedTime.CapturedUtc:
                    resultingState = BaseActivationState.RetryPending;
                    break;
                case BaseActivationDisposeRequest dispose when row.Generation == dispose.ExpectedGeneration &&
                    row.State is BaseActivationState.Succeeded or BaseActivationState.Exhausted or
                        BaseActivationState.Cancelled or BaseActivationState.Migrated:
                    resultingState = BaseActivationState.Disposed;
                    break;
                default:
                    return ActivationFailure<BaseActivationTransitionResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            }

            long generation = checked(row.Generation + 1);
            byte[] checksum = ControlChecksum(row.Payload.ActivationId, generation, resultingState);
            var next = current.Clone();
            next.Activations[row.Payload.ActivationId] = next.Activations[row.Payload.ActivationId] with
            {
                State = resultingState,
                Generation = generation,
                Claim = null,
                Lease = null,
                CanonicalResult = result,
                Effect = resultingState is BaseActivationState.EffectStarted or BaseActivationState.OutcomeUnknown
                    ? resultingEffect
                    : null,
                EffectiveDueAt = resultingState == BaseActivationState.RetryPending
                    ? request switch
                    {
                        BaseActivationFailRequest failed => failed.RetryDueAt!.Value,
                        BaseActivationOperatorRetryRequest retry => retry.RetryDueAt,
                        _ => row.EffectiveDueAt,
                    }
                    : row.EffectiveDueAt,
                ControlChecksum = checksum,
            };
            next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
            var transitionResult = new BaseActivationTransitionResult
            {
                State = resultingState,
                Generation = generation,
                ControlChecksum = checksum.ToImmutableArray(),
                Accounting = EmptyActivationAccounting with { ReadIntervals = 0, IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
                Effect = resultingEffect,
            };
            WriteActivationReceipt(next, request.Identity, receiptKind, transitionResult,
                HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(transitionResult);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorRegistrationResult>> RegisterExecutorAsync(
        BaseExecutorRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (string.IsNullOrWhiteSpace(request.ApplicationId) || string.IsNullOrWhiteSpace(request.HostId) ||
            string.IsNullOrWhiteSpace(request.ProcessIncarnationId) || request.WorkerDefinitionSetChecksum.Length != 32 ||
            request.RequestedHeartbeatMilliseconds <= 0 || request.AcceptedTime.ApplicationId != request.ApplicationId)
            return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadActivationReceipt(current, request.Identity, "executor-registered",
                HPDBaseJsonSerializerContext.Default.BaseExecutorRegistrationResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseExecutorRegistrationResult>? replay))
                return replay;
            string key = ExecutorKey(request.ApplicationId, request.HostId, request.ProcessIncarnationId);
            if (current.Executors.TryGetValue(key, out InMemoryExecutorRow? existing) && !existing.Retired)
                return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.executorConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            var next = current.Clone();
            long generation = checked(next.NextExecutorGeneration + 1);
            next.NextExecutorGeneration = generation;
            byte[] authorityChecksum = Hash($"base.activation.executor.v2\0{request.ApplicationId}\n{request.HostId}\n{request.ProcessIncarnationId}\n{generation}\n{_options.StoreId}\n0\n{Convert.ToHexString(request.WorkerDefinitionSetChecksum.AsSpan())}");
            var authority = new BaseExecutorIncarnationAuthority
            {
                ApplicationId = new string(request.ApplicationId.AsSpan()), HostId = new string(request.HostId.AsSpan()),
                ProcessIncarnationId = new string(request.ProcessIncarnationId.AsSpan()), ExecutorGeneration = generation,
                StoreInstanceId = _options.StoreId, RestoreEpoch = 0,
                WorkerDefinitionSetChecksum = request.WorkerDefinitionSetChecksum.ToArray().ToImmutableArray(),
                Checksum = authorityChecksum.ToImmutableArray(),
            };
            var heartbeat = Heartbeat(authority, 1, checked(request.AcceptedTime.CapturedUtc + request.RequestedHeartbeatMilliseconds));
            next.Executors[key] = new InMemoryExecutorRow(authority, heartbeat, false);
            var result = new BaseExecutorRegistrationResult
            {
                Executor = authority, Heartbeat = heartbeat, Accounting = EmptyActivationAccounting with { IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteActivationReceipt(next, request.Identity, "executor-registered", result, HPDBaseJsonSerializerContext.Default.BaseExecutorRegistrationResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorHeartbeatResult>> HeartbeatExecutorAsync(
        BaseExecutorHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseExecutorHeartbeatResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (request.ExpectedHeartbeatRevision <= 0 || request.ExtensionMilliseconds <= 0)
            return ActivationFailure<BaseExecutorHeartbeatResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadActivationReceipt(current, request.Identity, "executor-heartbeat",
                HPDBaseJsonSerializerContext.Default.BaseExecutorHeartbeatResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseExecutorHeartbeatResult>? replay))
                return replay;
            string key = ExecutorKey(request.Executor.ApplicationId, request.Executor.HostId, request.Executor.ProcessIncarnationId);
            if (!current.Executors.TryGetValue(key, out InMemoryExecutorRow? row) || row.Retired ||
                !ExecutorMatches(row.Authority, request.Executor) || row.Heartbeat.HeartbeatRevision != request.ExpectedHeartbeatRevision ||
                row.Heartbeat.HeartbeatExpiresAt < request.AcceptedTime.CapturedUtc)
                return ActivationFailure<BaseExecutorHeartbeatResult>("base.activation.executorLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            var next = current.Clone();
            long revision = checked(row.Heartbeat.HeartbeatRevision + 1);
            BaseExecutorHeartbeatObservation heartbeat = Heartbeat(row.Authority, revision,
                checked(request.AcceptedTime.CapturedUtc + request.ExtensionMilliseconds));
            next.Executors[key] = next.Executors[key] with { Heartbeat = heartbeat };
            var result = new BaseExecutorHeartbeatResult
            {
                Executor = row.Authority, Heartbeat = heartbeat, Accounting = EmptyActivationAccounting with { IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteActivationReceipt(next, request.Identity, "executor-heartbeat", result, HPDBaseJsonSerializerContext.Default.BaseExecutorHeartbeatResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorRetirementResult>> RetireExecutorAsync(
        BaseExecutorRetirementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseExecutorRetirementResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadActivationReceipt(current, request.Identity, "executor-retired",
                HPDBaseJsonSerializerContext.Default.BaseExecutorRetirementResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseExecutorRetirementResult>? replay))
                return replay;
            string key = ExecutorKey(request.Executor.ApplicationId, request.Executor.HostId, request.Executor.ProcessIncarnationId);
            if (!current.Executors.TryGetValue(key, out InMemoryExecutorRow? row) || row.Retired ||
                !ExecutorMatches(row.Authority, request.Executor) || row.Heartbeat.HeartbeatRevision != request.ExpectedHeartbeatRevision)
                return ActivationFailure<BaseExecutorRetirementResult>("base.activation.executorLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            var next = current.Clone();
            next.Executors[key] = next.Executors[key] with { Retired = true };
            byte[] checksum = Hash($"base.activation.executor.retired.v2\0{Convert.ToHexString(row.Authority.Checksum.AsSpan())}\n{row.Heartbeat.HeartbeatRevision}");
            var result = new BaseExecutorRetirementResult
            {
                Executor = row.Authority, HeartbeatRevision = row.Heartbeat.HeartbeatRevision,
                RetirementChecksum = checksum.ToImmutableArray(), Accounting = EmptyActivationAccounting with { IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteActivationReceipt(next, request.Identity, "executor-retired", result, HPDBaseJsonSerializerContext.Default.BaseExecutorRetirementResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<BaseScheduleAuthority>> ReadScheduleAsync(
        string scheduleId, int scheduleVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InMemoryStoreState state = Volatile.Read(ref _publishedState);
        return ValueTask.FromResult(state.Schedules.TryGetValue(ScheduleKey(scheduleId, scheduleVersion), out BaseScheduleAuthority? value)
            ? OperationResults.Ok(value with { Definition = BaseScheduleDefinitionBuilder.Create(value.Definition), Checksum = value.Checksum.ToArray().ToImmutableArray() })
            : ActivationFailure<BaseScheduleAuthority>("base.activation.scheduleNotFound", OperationStatus.NotFound, ErrorCategory.NotFound));
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleMutationResult>> MutateScheduleAsync(
        BaseScheduleMutationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseScheduleMutationResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        BaseScheduleDefinition definition;
        try { definition = BaseScheduleDefinitionBuilder.Create(request.Definition); }
        catch { return ActivationFailure<BaseScheduleMutationResult>("base.activation.scheduleInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation); }
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState); var next = current.Clone();
            if (TryReadActivationReceipt(current, request.Identity, "schedule-mutated",
                HPDBaseJsonSerializerContext.Default.BaseScheduleMutationResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseScheduleMutationResult>? replay))
                return replay;
            string key = ScheduleKey(definition.Id, definition.Version);
            current.Schedules.TryGetValue(key, out BaseScheduleAuthority? existing);
            if (request.Kind == BaseScheduleMutationKind.Create && existing is not null ||
                request.Kind != BaseScheduleMutationKind.Create && (existing is null || existing.DefinitionGeneration != request.ExpectedDefinitionGeneration))
                return ActivationFailure<BaseScheduleMutationResult>("base.activation.scheduleConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            if (request.Kind == BaseScheduleMutationKind.Remove)
            {
                next.Schedules.Remove(key);
                var removed = new BaseScheduleMutationResult { Authority = null, Accounting = EmptyActivationAccounting with { IndexOperations = 1 }, Disposition = BaseMutationRequestDisposition.Committed };
                WriteActivationReceipt(next, request.Identity, "schedule-mutated", removed, HPDBaseJsonSerializerContext.Default.BaseScheduleMutationResult);
                Volatile.Write(ref _publishedState, next);
                return OperationResults.Ok(removed);
            }
            long generation = existing is null ? 1 : checked(existing.DefinitionGeneration + 1);
            long epoch = existing is null ? 1 : request.Kind == BaseScheduleMutationKind.Update ? checked(existing.ScheduleEpoch + 1) : existing.ScheduleEpoch;
            bool enabled = request.Kind switch { BaseScheduleMutationKind.Disable => false, BaseScheduleMutationKind.Enable => true, _ => existing?.Enabled ?? true };
            long? last = request.Kind == BaseScheduleMutationKind.Update ? null : existing?.LastConsideredNominal;
            long? following = request.Kind == BaseScheduleMutationKind.Update || existing is null
                ? request.InitialNextNominal
                : existing.NextNominal;
            BaseScheduleAuthority authority = ScheduleAuthority(definition, generation, enabled, epoch, last, following);
            next.Schedules[key] = authority;
            var result = new BaseScheduleMutationResult { Authority = authority, Accounting = EmptyActivationAccounting with { IndexOperations = 1 }, Disposition = BaseMutationRequestDisposition.Committed };
            WriteActivationReceipt(next, request.Identity, "schedule-mutated", result, HPDBaseJsonSerializerContext.Default.BaseScheduleMutationResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleMaintenancePage>> AdvanceSchedulesAsync(
        BaseScheduleMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (request.Occurrences.Length is < 1 or > 256)
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState); var next = current.Clone();
            if (TryReadActivationReceipt(current, request.Identity, "occurrence-page",
                HPDBaseJsonSerializerContext.Default.BaseScheduleMaintenancePage,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseScheduleMaintenancePage>? replay))
                return replay;
            string key = ScheduleKey(request.ScheduleId, request.ScheduleVersion);
            if (!current.Schedules.TryGetValue(key, out BaseScheduleAuthority? authority) || !authority.Enabled ||
                !CryptographicOperations.FixedTimeEquals(authority.Checksum.AsSpan(), request.ExpectedAuthorityChecksum.AsSpan()))
                return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.scheduleConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            long previous = authority.LastConsideredNominal ?? -1;
            var committedFacts = ImmutableArray.CreateBuilder<BaseScheduleOccurrenceFact>(request.Occurrences.Length);
            var cancellations = ImmutableArray.CreateBuilder<BaseScheduleCancellationAuthority>();
            foreach (BaseScheduleOccurrenceProposal proposal in request.Occurrences)
            {
                BaseScheduleOccurrenceProposal effectiveProposal;
                try { effectiveProposal = ResolveOverlap(next, proposal); }
                catch (InvalidOperationException)
                { return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation); }
                BaseScheduleOccurrenceFact fact = effectiveProposal.Fact;
                if (fact.ScheduleId != authority.Definition.Id || fact.ScheduleEpoch != authority.ScheduleEpoch || fact.NominalAt <= previous ||
                    next.ScheduleOccurrences.ContainsKey(fact.OccurrenceId) || !OccurrenceShapeValid(effectiveProposal) ||
                    !CryptographicOperations.FixedTimeEquals(fact.Checksum.AsSpan(), OccurrenceChecksum(fact)))
                    return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.occurrenceInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                previous = fact.NominalAt;
                next.ScheduleOccurrences.Add(fact.OccurrenceId, fact);
                committedFacts.Add(fact);
                if (effectiveProposal.Activation is { } activation)
                {
                    string activationId = ((BaseOccurrenceMaterialized)fact.Disposition).ActivationId;
                    InMemoryActivationRow[] cancellationBlockers = activation.OverlapPolicy == BaseScheduleOverlapPolicy.CancelPrevious
                        ? ActiveOverlapRows(next, activation.OverlapKey).ToArray()
                        : [];
                    byte[] fingerprint = ScheduleActivationFingerprint(activation, fact.OccurrenceId);
                    if (next.Activations.TryGetValue(activationId, out InMemoryActivationRow? existing) && !CryptographicOperations.FixedTimeEquals(existing.Fingerprint, fingerprint))
                        return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
                    if (existing is null)
                    {
                        byte[] payloadChecksum = SHA256.HashData(activation.CanonicalInput.AsSpan());
                        var payload = new BaseActivationPayload { ActivationId = activationId, Definition = activation.Definition,
                            CanonicalInput = activation.CanonicalInput, InputChecksum = activation.InputChecksum, Scope = activation.Scope,
                            Checksum = payloadChecksum.ToImmutableArray() };
                        next.Activations.Add(activationId, new InMemoryActivationRow(payload, BaseActivationState.Pending, 1,
                            activation.RequestedDueAt, activation.EffectiveDueAt ?? activation.RequestedDueAt, fingerprint,
                            ControlChecksum(activationId, 1, BaseActivationState.Pending), activation.OccurrenceId,
                            activation.Priority, activation.OverlapKey.IsDefaultOrEmpty ? null : activation.OverlapKey.ToArray(),
                            activation.OverlapPolicy, cancellationBlockers.Length == 0));
                        next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
                    }
                    if (cancellationBlockers.Length != 0)
                    {
                        InMemoryActivationRow high = cancellationBlockers[^1];
                        string maintenanceId = Convert.ToHexStringLower(Hash(
                            $"base.activation.schedule.cancelPrevious.v2\0{fact.OccurrenceId}\n{activationId}"));
                        next.ScheduleCancellations[maintenanceId] = new InMemoryScheduleCancellationRow(
                            maintenanceId, activationId, activation.OverlapKey.ToArray(),
                            new BaseScheduleCancellationBoundary
                            { EffectiveDueAt = high.EffectiveDueAt, ActivationId = high.Payload.ActivationId }, null, false);
                        cancellations.Add(new BaseScheduleCancellationAuthority
                        {
                            MaintenanceId = maintenanceId, ReplacementActivationId = activationId,
                            OverlapKey = activation.OverlapKey.ToArray().ToImmutableArray(),
                            HighWater = new BaseScheduleCancellationBoundary
                            { EffectiveDueAt = high.EffectiveDueAt, ActivationId = high.Payload.ActivationId },
                        });
                    }
                }
            }
            if (previous != request.ResultingLastConsideredNominal)
                return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.occurrenceInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            BaseScheduleAuthority replacement = ScheduleAuthority(authority.Definition, authority.DefinitionGeneration, authority.Enabled,
                authority.ScheduleEpoch, request.ResultingLastConsideredNominal, request.ResultingNextNominal);
            next.Schedules[key] = replacement;
            var result = new BaseScheduleMaintenancePage
            {
                Authority = replacement, Occurrences = committedFacts.MoveToImmutable(), Cancellations = cancellations.ToImmutable(),
                Accounting = EmptyActivationAccounting with { Candidates = request.Occurrences.Length, IndexOperations = request.Occurrences.Length * 2 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteActivationReceipt(next, request.Identity, "occurrence-page", result, HPDBaseJsonSerializerContext.Default.BaseScheduleMaintenancePage);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    private static BaseScheduleOccurrenceProposal ResolveOverlap(InMemoryStoreState state, BaseScheduleOccurrenceProposal proposal)
    {
        if (proposal.Activation is not { } activation || activation.OverlapKey.IsDefaultOrEmpty ||
            activation.OverlapPolicy is BaseScheduleOverlapPolicy.Allow or BaseScheduleOverlapPolicy.Queue)
            return proposal;
        InMemoryActivationRow[] blockers = ActiveOverlapRows(state, activation.OverlapKey).ToArray();
        if (activation.OverlapPolicy == BaseScheduleOverlapPolicy.SkipWhileActive && blockers.Length != 0)
        {
            BaseScheduleOccurrenceFact skipped = proposal.Fact with
            { Disposition = new BaseOccurrenceSkippedOverlap(blockers[0].Payload.ActivationId), Checksum = [] };
            skipped = skipped with { Checksum = OccurrenceChecksum(skipped).ToImmutableArray() };
            return new BaseScheduleOccurrenceProposal { Fact = skipped };
        }
        return proposal;
    }

    private static IOrderedEnumerable<InMemoryActivationRow> ActiveOverlapRows(
        InMemoryStoreState state, ImmutableArray<byte> overlapKey) => state.Activations.Values.Where(row =>
            row.OverlapKey is not null && CryptographicOperations.FixedTimeEquals(row.OverlapKey, overlapKey.AsSpan()) &&
            row.State is BaseActivationState.Pending or BaseActivationState.RetryPending or BaseActivationState.Claimed or BaseActivationState.EffectStarted)
        .OrderBy(static row => row.EffectiveDueAt).ThenBy(static row => row.Payload.ActivationId, StringComparer.Ordinal);

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleCancellationMaintenancePage>> AdvanceScheduleCancellationAsync(
        BaseScheduleCancellationMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime) || request.OverlapKey.Length != 32 ||
            request.HighWater.ActivationId.Length == 0 || request.Limits.MaximumCandidates is < 1 or > 256)
            return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadActivationReceipt(current, request.Identity, "cancellation-maintenance",
                HPDBaseJsonSerializerContext.Default.BaseScheduleCancellationMaintenancePage,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseScheduleCancellationMaintenancePage>? replay))
                return replay;
            if (!current.ScheduleCancellations.TryGetValue(request.MaintenanceId, out InMemoryScheduleCancellationRow? stored) ||
                stored.ReplacementActivationId != request.ReplacementActivationId || stored.Completed ||
                !CryptographicOperations.FixedTimeEquals(stored.OverlapKey, request.OverlapKey.AsSpan()) ||
                !BoundaryEquals(stored.HighWater, request.HighWater) || !BoundaryEquals(stored.After, request.After))
                return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            var next = current.Clone();
            InMemoryActivationRow[] page = ActiveOverlapRows(next, request.OverlapKey)
                .Where(row => row.Payload.ActivationId != request.ReplacementActivationId &&
                    BoundaryAfter(row, request.After) && BoundaryAtOrBefore(row, request.HighWater))
                .Take(Math.Min(256, request.Limits.MaximumCandidates)).ToArray();
            foreach (InMemoryActivationRow blocker in page)
            {
                long generation = checked(blocker.Generation + 1);
                next.Activations[blocker.Payload.ActivationId] = blocker with
                {
                    State = BaseActivationState.Cancelled, Generation = generation, Claim = null, Lease = null, Eligible = false,
                    ControlChecksum = ControlChecksum(blocker.Payload.ActivationId, generation, BaseActivationState.Cancelled),
                };
            }
            BaseScheduleCancellationBoundary? boundary = page.Length == 0 ? request.After : new()
            { EffectiveDueAt = page[^1].EffectiveDueAt, ActivationId = page[^1].Payload.ActivationId };
            bool completed = !ActiveOverlapRows(next, request.OverlapKey).Any(row =>
                row.Payload.ActivationId != request.ReplacementActivationId && BoundaryAfter(row, boundary) && BoundaryAtOrBefore(row, request.HighWater));
            if (completed)
            {
                if (!next.Activations.TryGetValue(request.ReplacementActivationId, out InMemoryActivationRow? replacement) || replacement.State != BaseActivationState.Pending)
                    return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
                next.Activations[request.ReplacementActivationId] = replacement with { Eligible = true };
                next.ScheduleCancellations[request.MaintenanceId] = stored with { After = boundary, Completed = true };
            }
            else next.ScheduleCancellations[request.MaintenanceId] = stored with { After = boundary };
            next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
            var result = new BaseScheduleCancellationMaintenancePage
            {
                MaintenanceId = request.MaintenanceId, CancelledCount = page.Length, Next = completed ? null : boundary,
                Completed = completed, Accounting = EmptyActivationAccounting with
                { Candidates = page.Length, Comparisons = page.Length, IndexOperations = page.Length + 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteActivationReceipt(next, request.Identity, "cancellation-maintenance", result,
                HPDBaseJsonSerializerContext.Default.BaseScheduleCancellationMaintenancePage);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    private static bool BoundaryEquals(BaseScheduleCancellationBoundary? left, BaseScheduleCancellationBoundary? right) =>
        left is null ? right is null : right is not null && left.EffectiveDueAt == right.EffectiveDueAt && left.ActivationId == right.ActivationId;
    private static bool BoundaryAfter(InMemoryActivationRow row, BaseScheduleCancellationBoundary? boundary) => boundary is null ||
        row.EffectiveDueAt > boundary.EffectiveDueAt || row.EffectiveDueAt == boundary.EffectiveDueAt &&
        string.CompareOrdinal(row.Payload.ActivationId, boundary.ActivationId) > 0;
    private static bool BoundaryAtOrBefore(InMemoryActivationRow row, BaseScheduleCancellationBoundary boundary) =>
        row.EffectiveDueAt < boundary.EffectiveDueAt || row.EffectiveDueAt == boundary.EffectiveDueAt &&
        string.CompareOrdinal(row.Payload.ActivationId, boundary.ActivationId) <= 0;

    private static bool TryReadActivationReceipt<T>(InMemoryStoreState state, BaseMutationRequestIdentity identity,
        string kind, JsonTypeInfo<T> typeInfo, Func<T, T> duplicate, out OperationResult<T> result)
    {
        string key = ActivationReceiptKey(identity);
        if (!state.ActivationReceipts.TryGetValue(key, out InMemoryActivationReceiptRow? receipt))
        { result = default!; return false; }
        if (receipt.Kind != kind || !CryptographicOperations.FixedTimeEquals(receipt.Fingerprint, identity.Fingerprint.ToArray()))
        {
            result = ActivationFailure<T>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            return true;
        }
        T value = JsonSerializer.Deserialize(receipt.Result, typeInfo) ?? throw new InvalidOperationException("base.activation.receiptCorrupt");
        result = OperationResults.Ok(duplicate(value)); return true;
    }

    private static void WriteActivationReceipt<T>(InMemoryStoreState state, BaseMutationRequestIdentity identity,
        string kind, T result, JsonTypeInfo<T> typeInfo) => state.ActivationReceipts.Add(
            ActivationReceiptKey(identity), new InMemoryActivationReceiptRow(kind, identity.Fingerprint.ToArray(), JsonSerializer.SerializeToUtf8Bytes(result, typeInfo)));

    private static string ActivationReceiptKey(BaseMutationRequestIdentity identity) =>
        $"{identity.Scope}\n{identity.Operation}\n{identity.IdempotencyKey}";

    private static OperationResult<BaseActivationClaimResult> ResolveClaimReplay(
        InMemoryStoreState state, OperationResult<BaseActivationClaimResult> replay, long acceptedNow)
    {
        if (!replay.IsSuccess() || replay.Value is not BaseActivationClaimedResult claimed) return replay;
        if (!state.Activations.TryGetValue(claimed.Claim.ActivationId, out InMemoryActivationRow? row))
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimSupersededResult(claimed.Claim.ActivationId));
        if (row.State == BaseActivationState.Cancelled)
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimCancelledResult(row.Payload.ActivationId));
        if (row.State is BaseActivationState.Succeeded or BaseActivationState.Exhausted or BaseActivationState.OutcomeUnknown or BaseActivationState.Disposed or BaseActivationState.Migrated)
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimTerminalResult(row.Payload.ActivationId, row.State));
        if (!ClaimMatches(row, claimed.Claim))
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimSupersededResult(row.Payload.ActivationId));
        if (row.Lease is null || row.Lease.LeaseExpiresAt <= acceptedNow)
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimExpiredResult(row.Payload.ActivationId));
        return OperationResults.Ok<BaseActivationClaimResult>(claimed with { Lease = row.Lease });
    }

    private static string ActivationTransitionReceiptKind(BaseActivationTransitionRequest request) => request switch
    {
        BaseActivationCompleteRequest => "activation-completed",
        BaseActivationFailRequest failed when failed.Disposition == BaseActivationFailureDisposition.Retry => "activation-retried",
        BaseActivationFailRequest => "activation-failed-terminal",
        BaseActivationCancelRequest => "activation-cancelled",
        BaseActivationBeginEffectRequest => "effect-started",
        BaseActivationEffectHeartbeatRequest => "effect-heartbeat",
        BaseActivationCompleteEffectRequest => "effect-completed",
        BaseActivationRecoverEffectRequest => "effect-outcome-unknown",
        BaseActivationReconcileEffectRequest => "effect-reconciled",
        BaseActivationOperatorRetryRequest => "activation-operator-retried",
        BaseActivationDisposeRequest => "activation-disposed",
        _ => throw new InvalidOperationException("base.activation.invalid"),
    };

    private static string ScheduleKey(string id, int version) => $"{id}\n{version}";

    private static BaseScheduleAuthority ScheduleAuthority(BaseScheduleDefinition definition, long generation, bool enabled,
        long epoch, long? last, long? next)
    {
        byte[] checksum = Hash($"base.activation.schedule.authority.v2\0{definition.Id}\n{definition.Version}\n{Convert.ToHexString(definition.Checksum.AsSpan())}\n{generation}\n{enabled}\n{epoch}\n{last?.ToString() ?? "none"}\n{next?.ToString() ?? "none"}");
        return new BaseScheduleAuthority { Definition = BaseScheduleDefinitionBuilder.Create(definition), DefinitionGeneration = generation,
            Enabled = enabled, ScheduleEpoch = epoch, LastConsideredNominal = last, NextNominal = next, Checksum = checksum.ToImmutableArray() };
    }

    private static bool OccurrenceShapeValid(BaseScheduleOccurrenceProposal proposal) => proposal.Fact.Disposition switch
    {
        BaseOccurrenceMaterialized materialized => proposal.Activation is not null && materialized.ActivationId.Length > 0,
        BaseOccurrenceSkippedMisfire => proposal.Activation is null,
        BaseOccurrenceSkippedOverlap skipped => proposal.Activation is null && skipped.BlockingActivationId.Length > 0,
        BaseOccurrenceCancelled cancelled => proposal.Activation is null && cancelled.CancellationReceiptId.Length > 0,
        BaseOccurrenceSuppressedByReplacement replacement => proposal.Activation is null && replacement.ReplacementGeneration > 0,
        BaseOccurrenceSuppressedByRestoreFloor floor => proposal.Activation is null && floor.FloorChecksum.Length == 32,
        _ => false,
    };

    private static byte[] ScheduleActivationFingerprint(BaseActivationCreateIntent activation, string occurrenceId) =>
        Hash($"base.activation.schedule.create.v2\0{occurrenceId}\n{activation.Definition.Id}\n{activation.Definition.Version}\n{Convert.ToHexString(activation.InputChecksum.AsSpan())}\n{activation.RequestedDueAt}\n{activation.EffectiveDueAt ?? activation.RequestedDueAt}");

    private bool AcceptActivationTime(BaseAcceptedTimeReceipt receipt)
    {
        long native = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (!BaseActivationAcceptedTimeAuthority.Verify(receipt, native)) return false;
        while (true)
        {
            long observed = Volatile.Read(ref _acceptedActivationUtc);
            if (receipt.CapturedUtc < observed) return true;
            if (Interlocked.CompareExchange(ref _acceptedActivationUtc, receipt.CapturedUtc, observed) == observed) return true;
        }
    }

    internal static byte[] OccurrenceChecksum(BaseScheduleOccurrenceFact fact) => Hash(
        $"base.activation.schedule.occurrence.v2\0{fact.OccurrenceId}\n{fact.ScheduleId}\n{fact.ScheduleEpoch}\n{fact.NominalAt}\n{fact.EffectiveAt}\n{fact.OverlapOrdinal}\n{DispositionText(fact.Disposition)}");

    private static string DispositionText(BaseScheduleOccurrenceDisposition disposition) => disposition switch
    {
        BaseOccurrenceMaterialized value => $"materialized:{value.ActivationId}",
        BaseOccurrenceSkippedMisfire => "skipped-misfire",
        BaseOccurrenceSkippedOverlap value => $"skipped-overlap:{value.BlockingActivationId}",
        BaseOccurrenceCancelled value => $"cancelled:{value.CancellationReceiptId}",
        BaseOccurrenceSuppressedByReplacement value => $"replacement:{value.ReplacementGeneration}",
        BaseOccurrenceSuppressedByRestoreFloor value => $"restore:{Convert.ToHexString(value.FloorChecksum.AsSpan())}",
        _ => throw new InvalidOperationException("base.activation.occurrenceInvalid"),
    };

    private static string ExecutorKey(string applicationId, string hostId, string processId) => $"{applicationId}\n{hostId}\n{processId}";

    private static BaseExecutorHeartbeatObservation Heartbeat(BaseExecutorIncarnationAuthority authority, long revision, long expiresAt)
    {
        byte[] checksum = Hash($"base.activation.executor.heartbeat.v2\0{Convert.ToHexString(authority.Checksum.AsSpan())}\n{revision}\n{expiresAt}");
        return new BaseExecutorHeartbeatObservation
        {
            HeartbeatRevision = revision, HeartbeatExpiresAt = expiresAt,
            ExecutorAuthorityChecksum = authority.Checksum.ToArray().ToImmutableArray(), Checksum = checksum.ToImmutableArray(),
        };
    }

    private static bool ExecutorMatches(BaseExecutorIncarnationAuthority left, BaseExecutorIncarnationAuthority right) =>
        left.ApplicationId == right.ApplicationId && left.HostId == right.HostId &&
        left.ProcessIncarnationId == right.ProcessIncarnationId && left.ExecutorGeneration == right.ExecutorGeneration &&
        left.StoreInstanceId == right.StoreInstanceId && left.RestoreEpoch == right.RestoreEpoch &&
        CryptographicOperations.FixedTimeEquals(left.WorkerDefinitionSetChecksum.AsSpan(), right.WorkerDefinitionSetChecksum.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static bool HeartbeatsEqual(BaseExecutorHeartbeatObservation left, BaseExecutorHeartbeatObservation right) =>
        left.HeartbeatRevision == right.HeartbeatRevision && left.HeartbeatExpiresAt == right.HeartbeatExpiresAt &&
        CryptographicOperations.FixedTimeEquals(left.ExecutorAuthorityChecksum.AsSpan(), right.ExecutorAuthorityChecksum.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static bool CurrentExecutorAllows(InMemoryStoreState state, BaseExecutorIncarnationAuthority authority, long now) =>
        state.Executors.TryGetValue(ExecutorKey(authority.ApplicationId, authority.HostId, authority.ProcessIncarnationId), out InMemoryExecutorRow? row) &&
        !row.Retired && ExecutorMatches(row.Authority, authority) && row.Heartbeat.HeartbeatExpiresAt > now;

    private static BaseEffectExecutionAuthority Effect(
        BaseActivationClaimAuthority claim, BaseExecutorIncarnationAuthority executor, long generation, long revision, long expiresAt)
    {
        byte[] checksum = Hash($"base.activation.effect.v2\0{claim.ActivationId}\n{Convert.ToHexString(claim.FencingToken.AsSpan())}\n{Convert.ToHexString(executor.Checksum.AsSpan())}\n{generation}\n{revision}\n{expiresAt}");
        return new BaseEffectExecutionAuthority
        {
            Claim = claim, Executor = executor, EffectStartGeneration = generation, HeartbeatRevision = revision,
            HeartbeatExpiresAt = expiresAt, Checksum = checksum.ToImmutableArray(),
        };
    }

    private static bool EffectMatches(BaseEffectExecutionAuthority left, BaseEffectExecutionAuthority right) =>
        left.EffectStartGeneration == right.EffectStartGeneration && left.HeartbeatRevision == right.HeartbeatRevision &&
        left.HeartbeatExpiresAt == right.HeartbeatExpiresAt && ClaimAuthoritiesEqual(left.Claim, right.Claim) &&
        ExecutorMatches(left.Executor, right.Executor) && CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static bool ClaimAuthoritiesEqual(BaseActivationClaimAuthority left, BaseActivationClaimAuthority right) =>
        left.ActivationId == right.ActivationId && left.AttemptNumber == right.AttemptNumber && left.ClaimEpoch == right.ClaimEpoch &&
        left.WorkerIdentity == right.WorkerIdentity && left.CancellationGeneration == right.CancellationGeneration &&
        left.StoreInstanceId == right.StoreInstanceId && left.RestoreEpoch == right.RestoreEpoch &&
        CryptographicOperations.FixedTimeEquals(left.FencingToken.AsSpan(), right.FencingToken.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(left.DefinitionChecksum.AsSpan(), right.DefinitionChecksum.AsSpan());

    private static List<InMemoryActivationRow> EligibleRows(
        InMemoryStoreState state,
        ImmutableArray<BaseActivationDefinitionKey> definitions,
        BaseOwnedScopeSeekAuthority scope,
        long acceptedNow,
        BaseActivationDueBoundary? after)
    {
        var keys = definitions.ToDictionary(static value => $"{value.Id}\n{value.Version}", StringComparer.Ordinal);
        return state.Activations.Values
            .Where(row => keys.TryGetValue($"{row.Payload.Definition.Id}\n{row.Payload.Definition.Version}", out BaseActivationDefinitionKey? key) &&
                CryptographicOperations.FixedTimeEquals(key.Checksum.AsSpan(), row.Payload.Definition.Checksum.AsSpan()))
            .Where(row => ScopeMatches(row.Payload.Scope, scope))
            .Where(static row => row.Eligible)
            .Where(row => row.OverlapPolicy != BaseScheduleOverlapPolicy.Queue || !HasEarlierActiveOverlap(state, row))
            .Where(row => (row.State is BaseActivationState.Pending or BaseActivationState.RetryPending) ||
                (row.State == BaseActivationState.Claimed && row.Lease is not null && row.Lease.LeaseExpiresAt <= acceptedNow))
            .Where(row => row.EffectiveDueAt <= acceptedNow)
            .OrderBy(row => Boundary(row, acceptedNow), Comparer<BaseActivationDueBoundary>.Create(Compare))
            .Where(row => after is null || Compare(Boundary(row, acceptedNow), after) > 0)
            .ToList();
    }

    private static bool HasEarlierActiveOverlap(InMemoryStoreState state, InMemoryActivationRow row)
    {
        if (row.OverlapKey is null) return false;
        return state.Activations.Values.Any(other => !ReferenceEquals(other, row) && other.OverlapKey is not null &&
            CryptographicOperations.FixedTimeEquals(other.OverlapKey, row.OverlapKey) &&
            (other.State is BaseActivationState.Pending or BaseActivationState.RetryPending or BaseActivationState.Claimed) &&
            (other.EffectiveDueAt < row.EffectiveDueAt || other.EffectiveDueAt == row.EffectiveDueAt &&
                string.Compare(other.Payload.ActivationId, row.Payload.ActivationId, StringComparison.Ordinal) < 0));
    }

    private static bool ScopeMatches(BaseOwnedSubjectScopeEvidence scope, BaseOwnedScopeSeekAuthority authority) =>
        scope.Kind == authority.Kind && CryptographicOperations.FixedTimeEquals(ScopeDigest(scope), authority.ProtectedIndexDigest.AsSpan());

    private static byte[] ScopeDigest(BaseOwnedSubjectScopeEvidence scope) =>
        Hash($"base.activation.scope.v2\0{(int)scope.Kind}\n{scope.Value ?? string.Empty}");

    private static BaseActivationDueBoundary Boundary(InMemoryActivationRow row, long acceptedNow) => new()
    {
        EffectiveAgedPriority = Math.Min(32, row.Priority + checked((int)Math.Min(int.MaxValue,
            Math.Max(0, acceptedNow - row.EffectiveDueAt) / 60_000))),
        EffectiveDueAt = row.EffectiveDueAt,
        OccurrenceId = row.OccurrenceId,
        ActivationId = row.Payload.ActivationId,
    };

    private static int Compare(BaseActivationDueBoundary left, BaseActivationDueBoundary right)
    {
        int priority = right.EffectiveAgedPriority.CompareTo(left.EffectiveAgedPriority);
        if (priority != 0) return priority;
        int due = left.EffectiveDueAt.CompareTo(right.EffectiveDueAt);
        if (due != 0) return due;
        int occurrence = string.Compare(left.OccurrenceId, right.OccurrenceId, StringComparison.Ordinal);
        return occurrence != 0 ? occurrence : string.Compare(left.ActivationId, right.ActivationId, StringComparison.Ordinal);
    }

    private static byte[] CurrentWorkerToken(InMemoryStoreState state, BaseActivationClaimRequest request)
    {
        InMemoryActivationRow? first = EligibleRows(
            state, request.Worker.Definitions, request.Worker.Scope, request.AcceptedTime.CapturedUtc, null).FirstOrDefault();
        return DueToken(state.ActivationIndexGeneration, request.AcceptedTime.CapturedUtc,
            request.Worker.Scope.ProtectedIndexDigest.AsSpan(), request.Worker.Definitions,
            first is null ? null : Boundary(first, request.AcceptedTime.CapturedUtc));
    }

    private static byte[] DueToken(
        long generation,
        long acceptedNow,
        ReadOnlySpan<byte> scopeDigest,
        ImmutableArray<BaseActivationDefinitionKey> definitions,
        BaseActivationDueBoundary? earliest)
    {
        string definitionText = string.Join("\n", definitions.Select(static item =>
            $"{item.Id}:{item.Version}:{Convert.ToHexString(item.Checksum.AsSpan())}"));
        byte[] digest = Hash($"base.activation.due.token.v2\0{generation}\n{acceptedNow}\n{Convert.ToHexString(scopeDigest)}\n{definitionText}\n{earliest?.ActivationId ?? string.Empty}");
        byte[] token = new byte[40];
        BinaryPrimitives.WriteInt64BigEndian(token, generation);
        digest.CopyTo(token, 8);
        return token;
    }

    private static long DecodeDueGeneration(ReadOnlySpan<byte> token) =>
        token.Length == 40 ? BinaryPrimitives.ReadInt64BigEndian(token) : -1;

    private static BaseAtomicReadIntervalEvidence DueInterval(
        BaseOwnedScopeSeekAuthority scope,
        long acceptedNow,
        BaseActivationDueBoundary? after,
        BaseActivationDueBoundary? result) => new()
    {
        LogicalAccessPathId = "base.activation.due.byScopeDefinitionPriorityTime.v1",
        CanonicalLowerBound = Encoding.UTF8.GetBytes(after?.ActivationId ?? string.Empty).ToImmutableArray(),
        LowerInclusive = false,
        CanonicalUpperBound = Encoding.UTF8.GetBytes($"{acceptedNow}\n{result?.ActivationId ?? string.Empty}\n{Convert.ToHexString(scope.ProtectedIndexDigest.AsSpan())}").ToImmutableArray(),
        UpperInclusive = true,
    };

    private static long IntervalBytes(BaseAtomicReadIntervalEvidence interval) =>
        checked(Encoding.UTF8.GetByteCount(interval.LogicalAccessPathId) + interval.CanonicalLowerBound.Length + interval.CanonicalUpperBound.Length + 2);

    private static bool ClaimMatches(InMemoryActivationRow row, BaseActivationClaimAuthority claim) =>
        row.State == BaseActivationState.Claimed && row.Claim is not null && row.Lease is not null &&
        row.Claim.ActivationId == claim.ActivationId &&
        row.Claim.AttemptNumber == claim.AttemptNumber &&
        row.Claim.ClaimEpoch == claim.ClaimEpoch &&
        row.Claim.CancellationGeneration == claim.CancellationGeneration &&
        CryptographicOperations.FixedTimeEquals(row.Claim.FencingToken.AsSpan(), claim.FencingToken.AsSpan());

    private static bool ValidateLimits(BaseActivationExecutionLimits limits) =>
        limits.MaximumCandidates is > 0 and <= 256 &&
        limits.MaximumInputBytes is > 0 and <= 4L * 1024 * 1024 &&
        limits.MaximumResultBytes is > 0 and <= 4L * 1024 * 1024 &&
        limits.MaximumEvidenceBytes is > 0 and <= 16L * 1024 * 1024 &&
        limits.MaximumTransientBytes is > 0 and <= 16L * 1024 * 1024 &&
        limits.MaximumReadIntervals > 0 && limits.MaximumIndexOperations > 0;

    private static byte[] ControlChecksum(string activationId, long generation, BaseActivationState state) =>
        Hash($"base.activation.control.v2\0{activationId}\n{generation}\n{(int)state}");

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static OperationResult<T> ActivationFailure<T>(
        string code,
        OperationStatus status,
        ErrorCategory category) => new()
    {
        Status = status,
        Error = new BaseError
        {
            Code = code,
            Message = "The activation operation could not be completed.",
            Category = category,
        },
    };
}

internal static class BaseActivationPayloadCloneExtensions
{
    internal static BaseActivationPayload DeepClone(this BaseActivationPayload payload) => payload with
    {
        Definition = payload.Definition with { Checksum = payload.Definition.Checksum.ToArray().ToImmutableArray() },
        CanonicalInput = payload.CanonicalInput.ToArray().ToImmutableArray(),
        InputChecksum = payload.InputChecksum.ToArray().ToImmutableArray(),
        Scope = payload.Scope with { },
        Checksum = payload.Checksum.ToArray().ToImmutableArray(),
    };
}
