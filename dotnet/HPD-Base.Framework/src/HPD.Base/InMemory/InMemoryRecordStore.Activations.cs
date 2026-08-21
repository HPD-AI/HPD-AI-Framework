using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

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
            GuardedChildrenSupported = true,
            RestoreFencingSupported = true,
            DueInvalidation = BaseDueInvalidationClass.Native,
            MaximumActivationsPerTransaction = 256,
            MaximumDueCandidates = 256,
            MaximumInputBytes = 4L * 1024 * 1024,
            MaximumResultBytes = 4L * 1024 * 1024,
            MaximumEvidenceBytes = 16L * 1024 * 1024,
            MaximumTransientBytes = 16L * 1024 * 1024,
            CanonicalChecksum = ImmutableArray.CreateRange(SHA256.HashData("hpd.base.inMemory.activations.v2"u8)),
        },
    };

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationDueObservation>> ObserveDueAsync(
        BaseActivationDueObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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
            BaseActivationDueBoundary? boundary = first is null ? null : Boundary(first);
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
        if (!ValidateLimits(request.Limits) || request.LeaseMilliseconds <= 0)
            return ActivationFailure<BaseActivationClaimResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
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
                Volatile.Write(ref _publishedState, next);
                return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationRecoveredClaimResult(
                    row.Payload.ActivationId, recoveredGeneration));
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
            Volatile.Write(ref _publishedState, next);
            var attempt = new BaseActivationAttemptEvidence
            {
                AttemptId = $"{mutable.Payload.ActivationId}:{attemptNumber}",
                AttemptNumber = attemptNumber,
                StartedAt = request.AcceptedTime.CapturedUtc,
                Checksum = Hash($"base.activation.attempt.v2\0{mutable.Payload.ActivationId}\n{attemptNumber}").ToImmutableArray(),
            };
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimedResult(
                mutable.Payload.DeepClone(),
                claim,
                lease,
                attempt,
                [DueInterval(request.Worker.Scope, request.AcceptedTime.CapturedUtc, null, Boundary(mutable))],
                EmptyActivationAccounting with { Candidates = 1, Comparisons = 1 }));
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
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
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
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(new BaseActivationRenewResult
            {
                Claim = request.Claim,
                Lease = lease,
                Accounting = EmptyActivationAccounting with { ReadIntervals = 0, IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            });
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
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (!current.Activations.TryGetValue(request.ActivationId, out InMemoryActivationRow? row))
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.notFound", OperationStatus.NotFound, ErrorCategory.NotFound);

            BaseActivationState resultingState;
            byte[]? result = null;
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
                    resultingState = BaseActivationState.Cancelled;
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
                EffectiveDueAt = resultingState == BaseActivationState.RetryPending
                    ? ((BaseActivationFailRequest)request).RetryDueAt!.Value
                    : row.EffectiveDueAt,
                ControlChecksum = checksum,
            };
            next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(new BaseActivationTransitionResult
            {
                State = resultingState,
                Generation = generation,
                ControlChecksum = checksum.ToImmutableArray(),
                Accounting = EmptyActivationAccounting with { ReadIntervals = 0, IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            });
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
        if (string.IsNullOrWhiteSpace(request.ApplicationId) || string.IsNullOrWhiteSpace(request.HostId) ||
            string.IsNullOrWhiteSpace(request.ProcessIncarnationId) || request.WorkerDefinitionSetChecksum.Length != 32 ||
            request.RequestedHeartbeatMilliseconds <= 0 || request.AcceptedTime.ApplicationId != request.ApplicationId)
            return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
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
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(new BaseExecutorRegistrationResult
            {
                Executor = authority, Heartbeat = heartbeat, Accounting = EmptyActivationAccounting with { IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            });
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorHeartbeatResult>> HeartbeatExecutorAsync(
        BaseExecutorHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedHeartbeatRevision <= 0 || request.ExtensionMilliseconds <= 0)
            return ActivationFailure<BaseExecutorHeartbeatResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
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
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(new BaseExecutorHeartbeatResult
            {
                Executor = row.Authority, Heartbeat = heartbeat, Accounting = EmptyActivationAccounting with { IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            });
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorRetirementResult>> RetireExecutorAsync(
        BaseExecutorRetirementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            string key = ExecutorKey(request.Executor.ApplicationId, request.Executor.HostId, request.Executor.ProcessIncarnationId);
            if (!current.Executors.TryGetValue(key, out InMemoryExecutorRow? row) || row.Retired ||
                !ExecutorMatches(row.Authority, request.Executor) || row.Heartbeat.HeartbeatRevision != request.ExpectedHeartbeatRevision)
                return ActivationFailure<BaseExecutorRetirementResult>("base.activation.executorLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            var next = current.Clone();
            next.Executors[key] = next.Executors[key] with { Retired = true };
            Volatile.Write(ref _publishedState, next);
            byte[] checksum = Hash($"base.activation.executor.retired.v2\0{Convert.ToHexString(row.Authority.Checksum.AsSpan())}\n{row.Heartbeat.HeartbeatRevision}");
            return OperationResults.Ok(new BaseExecutorRetirementResult
            {
                Executor = row.Authority, HeartbeatRevision = row.Heartbeat.HeartbeatRevision,
                RetirementChecksum = checksum.ToImmutableArray(), Accounting = EmptyActivationAccounting with { IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            });
        }
        finally { _stateGate.Release(); }
    }

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
            .Where(row => (row.State is BaseActivationState.Pending or BaseActivationState.RetryPending) ||
                (row.State == BaseActivationState.Claimed && row.Lease is not null && row.Lease.LeaseExpiresAt <= acceptedNow))
            .Where(row => row.EffectiveDueAt <= acceptedNow)
            .OrderBy(row => row.EffectiveDueAt)
            .ThenBy(row => row.Payload.ActivationId, StringComparer.Ordinal)
            .Where(row => after is null || Compare(Boundary(row), after) > 0)
            .ToList();
    }

    private static bool ScopeMatches(BaseOwnedSubjectScopeEvidence scope, BaseOwnedScopeSeekAuthority authority) =>
        scope.Kind == authority.Kind && CryptographicOperations.FixedTimeEquals(ScopeDigest(scope), authority.ProtectedIndexDigest.AsSpan());

    private static byte[] ScopeDigest(BaseOwnedSubjectScopeEvidence scope) =>
        Hash($"base.activation.scope.v2\0{(int)scope.Kind}\n{scope.Value ?? string.Empty}");

    private static BaseActivationDueBoundary Boundary(InMemoryActivationRow row) => new()
    {
        EffectiveAgedPriority = 0,
        EffectiveDueAt = row.EffectiveDueAt,
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
            request.Worker.Scope.ProtectedIndexDigest.AsSpan(), request.Worker.Definitions, first is null ? null : Boundary(first));
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
