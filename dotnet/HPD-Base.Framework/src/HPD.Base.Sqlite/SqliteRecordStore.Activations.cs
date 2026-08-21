using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore
{
    private static readonly BaseActivationProviderDescriptor ActivationDescriptor = new()
    {
        ProviderId = "hpd.base.sqlite.activations",
        ProviderVersion = "1",
        ProtocolVersion = 2,
        Capability = new BaseActivationProviderCapability
        {
            AtomicCreationSupported = true,
            SelectionTargetSupported = true,
            ModuleTargetSupported = true,
            GuardedChildrenSupported = true,
            RestoreFencingSupported = true,
            DueInvalidation = BaseDueInvalidationClass.BoundedPolling,
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
            CanonicalChecksum = ImmutableArray.CreateRange(SHA256.HashData("hpd.base.sqlite.activations.v2"u8)),
        },
    };

    BaseActivationProviderDescriptor IBaseActivationProvider.Descriptor => ActivationDescriptor;

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationDueObservation>> ObserveDueAsync(
        BaseActivationDueObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationDueObservation>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (!ActivationLimitsValid(request.Limits) || request.MaximumCandidates is < 1 or > 256)
            return ActivationFailure<BaseActivationDueObservation>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        (long generation, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, null, cancellationToken).ConfigureAwait(false);
        List<SqliteActivationRow> rows = await ReadDueRowsAsync(connection, null, request.Definitions, request.Scope,
            request.AcceptedTime.CapturedUtc, request.After, request.MaximumCandidates, cancellationToken).ConfigureAwait(false);
        SqliteActivationRow? first = rows.FirstOrDefault();
        BaseActivationDueBoundary? boundary = first is null ? null : ActivationBoundary(first, request.AcceptedTime.CapturedUtc);
        byte[] token = ActivationDueToken(generation, restoreEpoch, request.AcceptedTime.CapturedUtc,
            request.Scope.ProtectedIndexDigest.AsSpan(), request.Definitions, boundary);
        BaseAtomicReadIntervalEvidence interval = ActivationDueInterval(request.Scope, request.AcceptedTime.CapturedUtc, request.After, boundary);
        long evidenceBytes = checked(token.Length + ActivationIntervalBytes(interval));
        if (evidenceBytes > request.Limits.MaximumEvidenceBytes)
            return ActivationFailure<BaseActivationDueObservation>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        return OperationResults.Ok(new BaseActivationDueObservation
        {
            Earliest = boundary,
            Token = new BaseDueObservationToken { Value = token.ToImmutableArray() },
            Intervals = [interval],
            Accounting = ActivationAccounting(rows.Count, evidenceBytes),
        });
    }

    /// <inheritdoc />
    public async ValueTask<BaseDueWaitResult> WaitForDueChangeAsync(
        BaseDueObservationToken token,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        (long expectedGeneration, long expectedRestore) = DecodeActivationTokenAuthority(token.Value.AsSpan());
        if (expectedGeneration < 0)
            return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.TokenInvalid };
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            (long generation, long restore) = await ReadActivationAuthorityAsync(connection, null, cancellationToken).ConfigureAwait(false);
            if (restore != expectedRestore)
                return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.TokenInvalid };
            if (generation != expectedGeneration)
                return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.Changed };
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50), cancellationToken)
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
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationClaimResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (!ActivationLimitsValid(request.Limits) || request.LeaseMilliseconds <= 0)
            return ActivationFailure<BaseActivationClaimResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (long generation, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        (long expectedGeneration, long expectedRestore) = DecodeActivationTokenAuthority(request.Observation.Value.AsSpan());
        if (generation != expectedGeneration || restoreEpoch != expectedRestore)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationObservationChangedResult(
                new BaseDueObservationToken { Value = ActivationDueToken(generation, restoreEpoch, request.AcceptedTime.CapturedUtc,
                    request.Worker.Scope.ProtectedIndexDigest.AsSpan(), request.Worker.Definitions, null).ToImmutableArray() }));
        }
        List<SqliteActivationRow> rows = await ReadDueRowsAsync(connection, transaction, request.Worker.Definitions,
            request.Worker.Scope, request.AcceptedTime.CapturedUtc, null, 1, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimEmptyResult(request.Observation));
        }
        SqliteActivationRow row = rows[0];
        if (row.State == BaseActivationState.Claimed)
        {
            long recovered = checked(row.Generation + 1);
            await UpdateRecoveredAsync(connection, transaction, row.ActivationId, recovered,
                request.AcceptedTime.CapturedUtc, cancellationToken).ConfigureAwait(false);
            await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationRecoveredClaimResult(row.ActivationId, recovered));
        }

        int attempt = checked(row.AttemptNumber + 1);
        long claimEpoch = checked(row.ClaimEpoch + 1);
        long resultingGeneration = checked(row.Generation + 1);
        byte[] fence = ActivationHash($"base.activation.claim.v2\0{row.ActivationId}\n{attempt}\n{claimEpoch}\n{request.Worker.WorkerIdentity}");
        long leaseExpires = checked(request.AcceptedTime.CapturedUtc + request.LeaseMilliseconds);
        byte[] leaseChecksum = ActivationHash($"base.activation.lease.v2\0{row.ActivationId}\n1\n{leaseExpires}");
        byte[] controlChecksum = ActivationControlChecksum(row.ActivationId, resultingGeneration, BaseActivationState.Claimed);
        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {_names.Activations} SET state=$state,generation=$generation,attempt_number=$attempt,claim_epoch=$epoch,claim_fence=$fence,claim_worker=$worker,lease_revision=1,lease_expires_at=$expires,control_checksum=$checksum WHERE activation_id=$id AND generation=$expected AND state IN ($pending,$retry);";
            update.Parameters.AddWithValue("$state", (int)BaseActivationState.Claimed);
            update.Parameters.AddWithValue("$generation", resultingGeneration);
            update.Parameters.AddWithValue("$attempt", attempt);
            update.Parameters.AddWithValue("$epoch", claimEpoch);
            update.Parameters.Add("$fence", SqliteType.Blob).Value = fence;
            update.Parameters.AddWithValue("$worker", request.Worker.WorkerIdentity);
            update.Parameters.AddWithValue("$expires", leaseExpires);
            update.Parameters.Add("$checksum", SqliteType.Blob).Value = controlChecksum;
            update.Parameters.AddWithValue("$id", row.ActivationId);
            update.Parameters.AddWithValue("$expected", row.Generation);
            update.Parameters.AddWithValue("$pending", (int)BaseActivationState.Pending);
            update.Parameters.AddWithValue("$retry", (int)BaseActivationState.RetryPending);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return ActivationFailure<BaseActivationClaimResult>("base.activation.claimConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            }
        }
        await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        var claim = new BaseActivationClaimAuthority
        {
            ActivationId = row.ActivationId, AttemptNumber = attempt, ClaimEpoch = claimEpoch,
            FencingToken = fence.ToImmutableArray(), WorkerIdentity = request.Worker.WorkerIdentity,
            CancellationGeneration = 0, StoreInstanceId = _options.StoreId, RestoreEpoch = restoreEpoch,
            DefinitionChecksum = row.DefinitionChecksum.ToImmutableArray(),
        };
        var lease = new BaseActivationLeaseObservation
        {
            LeaseRevision = 1, LeaseExpiresAt = leaseExpires, Checksum = leaseChecksum.ToImmutableArray(),
        };
        var attemptEvidence = new BaseActivationAttemptEvidence
        {
            AttemptId = $"{row.ActivationId}:{attempt}", AttemptNumber = attempt,
            StartedAt = request.AcceptedTime.CapturedUtc,
            Checksum = ActivationHash($"base.activation.attempt.v2\0{row.ActivationId}\n{attempt}").ToImmutableArray(),
        };
        return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimedResult(
            row.Payload(), claim, lease, attemptEvidence,
            [ActivationDueInterval(request.Worker.Scope, request.AcceptedTime.CapturedUtc, null,
                ActivationBoundary(row, request.AcceptedTime.CapturedUtc))],
            ActivationAccounting(1, 128)));
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(
        BaseActivationRenewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationRenewResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        (long _, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (restoreEpoch != request.Claim.RestoreEpoch)
            return ActivationFailure<BaseActivationRenewResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        long revision = checked(request.ExpectedLeaseRevision + 1);
        long expires = checked(request.AcceptedTime.CapturedUtc + request.ExtensionMilliseconds);
        byte[] checksum = ActivationHash($"base.activation.lease.v2\0{request.Claim.ActivationId}\n{revision}\n{expires}");
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.Activations} SET lease_revision=$next,lease_expires_at=$expires WHERE activation_id=$id AND state=$state AND attempt_number=$attempt AND claim_epoch=$epoch AND claim_fence=$fence AND claim_worker=$worker AND lease_revision=$expected AND lease_expires_at>$now;";
        command.Parameters.AddWithValue("$next", revision); command.Parameters.AddWithValue("$expires", expires);
        command.Parameters.AddWithValue("$id", request.Claim.ActivationId); command.Parameters.AddWithValue("$state", (int)BaseActivationState.Claimed);
        command.Parameters.AddWithValue("$attempt", request.Claim.AttemptNumber); command.Parameters.AddWithValue("$epoch", request.Claim.ClaimEpoch);
        command.Parameters.Add("$fence", SqliteType.Blob).Value = request.Claim.FencingToken.ToArray(); command.Parameters.AddWithValue("$worker", request.Claim.WorkerIdentity);
        command.Parameters.AddWithValue("$expected", request.ExpectedLeaseRevision); command.Parameters.AddWithValue("$now", request.AcceptedTime.CapturedUtc);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return ActivationFailure<BaseActivationRenewResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(new BaseActivationRenewResult
        {
            Claim = request.Claim,
            Lease = new BaseActivationLeaseObservation { LeaseRevision = revision, LeaseExpiresAt = expires, Checksum = checksum.ToImmutableArray() },
            Accounting = ActivationAccounting(1, 64), Disposition = BaseMutationRequestDisposition.Committed,
        });
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationTransitionResult>> TransitionAsync(
        BaseActivationTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        SqliteActivationRow? row = await ReadActivationAsync(connection, transaction, request.ActivationId, cancellationToken).ConfigureAwait(false);
        if (row is null)
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.notFound", OperationStatus.NotFound, ErrorCategory.NotFound);
        BaseEffectExecutionAuthority? storedEffect = await ReadEffectAsync(connection, transaction, row.ActivationId, cancellationToken).ConfigureAwait(false);
        if (request is BaseActivationEffectHeartbeatRequest effectHeartbeat)
        {
            SqliteExecutorRow? executor = storedEffect is null ? null : await ReadExecutorAsync(connection, transaction,
                storedEffect.Executor.ApplicationId, storedEffect.Executor.HostId, storedEffect.Executor.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
            if (row.State != BaseActivationState.EffectStarted || storedEffect is null || !SqliteEffectMatches(storedEffect, effectHeartbeat.Effect) ||
                storedEffect.HeartbeatRevision != effectHeartbeat.ExpectedHeartbeatRevision || effectHeartbeat.ExtensionMilliseconds <= 0 ||
                executor is null || executor.Retired || !SqliteExecutorMatches(executor.Authority, storedEffect.Executor) || executor.Heartbeat.HeartbeatExpiresAt <= request.AcceptedTime.CapturedUtc)
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.effectLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            BaseEffectExecutionAuthority replacement = SqliteEffect(storedEffect.Claim, storedEffect.Executor, storedEffect.EffectStartGeneration,
                checked(storedEffect.HeartbeatRevision + 1), checked(request.AcceptedTime.CapturedUtc + effectHeartbeat.ExtensionMilliseconds));
            await WriteEffectAsync(connection, transaction, replacement, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok(new BaseActivationTransitionResult
            {
                State = row.State, Generation = row.Generation, ControlChecksum = row.ControlChecksum.ToImmutableArray(),
                Accounting = ActivationAccounting(1, 128), Disposition = BaseMutationRequestDisposition.Committed, Effect = replacement,
            });
        }
        BaseActivationState state;
        byte[]? result = null;
        BaseActivationClaimAuthority? claim = null;
        BaseEffectExecutionAuthority? resultingEffect = null;
        if (request is BaseActivationCompleteRequest complete)
        {
            claim = complete.Claim;
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(complete.CanonicalResult.AsSpan()), complete.ResultChecksum.AsSpan()))
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            state = BaseActivationState.Succeeded; result = complete.CanonicalResult.ToArray();
        }
        else if (request is BaseActivationFailRequest failed)
        {
            if ((failed.Disposition == BaseActivationFailureDisposition.Retry) != failed.RetryDueAt.HasValue || failed.RetryDueAt is < 0)
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            claim = failed.Claim;
            state = failed.Disposition == BaseActivationFailureDisposition.Retry ? BaseActivationState.RetryPending : BaseActivationState.Exhausted;
        }
        else if (request is BaseActivationCancelRequest cancel && cancel.ExpectedGeneration == row.Generation)
            state = BaseActivationState.Cancelled;
        else if (request is BaseActivationBeginEffectRequest begin)
        {
            SqliteExecutorRow? executor = await ReadExecutorAsync(connection, transaction, begin.Executor.ApplicationId,
                begin.Executor.HostId, begin.Executor.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
            if (!SqliteClaimMatches(row, begin.Claim) || begin.HeartbeatMilliseconds <= 0 || executor is null || executor.Retired ||
                !SqliteExecutorMatches(executor.Authority, begin.Executor) || !SqliteHeartbeatsEqual(executor.Heartbeat, begin.ExecutorHeartbeat) ||
                executor.Heartbeat.HeartbeatExpiresAt <= request.AcceptedTime.CapturedUtc)
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.executorLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            state = BaseActivationState.EffectStarted;
            resultingEffect = SqliteEffect(begin.Claim, begin.Executor, checked(row.Generation + 1), 1,
                checked(request.AcceptedTime.CapturedUtc + begin.HeartbeatMilliseconds));
        }
        else if (request is BaseActivationCompleteEffectRequest completeEffect && row.State == BaseActivationState.EffectStarted &&
            storedEffect is not null && SqliteEffectMatches(storedEffect, completeEffect.Effect))
        {
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(completeEffect.CanonicalResult.AsSpan()), completeEffect.ResultChecksum.AsSpan()))
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            state = BaseActivationState.Succeeded; result = completeEffect.CanonicalResult.ToArray();
        }
        else if (request is BaseActivationRecoverEffectRequest recover && row.State == BaseActivationState.EffectStarted &&
            storedEffect is not null && SqliteEffectMatches(storedEffect, recover.Effect) && storedEffect.HeartbeatExpiresAt <= request.AcceptedTime.CapturedUtc)
        {
            SqliteExecutorRow? executor = await ReadExecutorAsync(connection, transaction, storedEffect.Executor.ApplicationId,
                storedEffect.Executor.HostId, storedEffect.Executor.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
            if (executor is not null && !executor.Retired && SqliteExecutorMatches(executor.Authority, storedEffect.Executor) &&
                executor.Heartbeat.HeartbeatExpiresAt > request.AcceptedTime.CapturedUtc)
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.effectOwned", OperationStatus.Conflict, ErrorCategory.Conflict);
            state = BaseActivationState.OutcomeUnknown;
        }
        else
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        if (claim is not null && !SqliteClaimMatches(row, claim))
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        long generation = checked(row.Generation + 1);
        byte[] control = ActivationControlChecksum(row.ActivationId, generation, state);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.Activations} SET state=$state,generation=$generation,claim_fence=NULL,claim_worker=NULL,lease_revision=NULL,lease_expires_at=NULL,canonical_result=$result,effective_due_at=CASE WHEN $state=$retry THEN $now ELSE effective_due_at END,control_checksum=$checksum WHERE activation_id=$id AND generation=$expected;";
        command.Parameters.AddWithValue("$state", (int)state); command.Parameters.AddWithValue("$generation", generation);
        command.Parameters.Add("$result", SqliteType.Blob).Value = (object?)result ?? DBNull.Value;
        command.Parameters.AddWithValue("$retry", (int)BaseActivationState.RetryPending);
        command.Parameters.AddWithValue("$now", request is BaseActivationFailRequest retry ? (object?)retry.RetryDueAt ?? request.AcceptedTime.CapturedUtc : request.AcceptedTime.CapturedUtc);
        command.Parameters.Add("$checksum", SqliteType.Blob).Value = control; command.Parameters.AddWithValue("$id", row.ActivationId); command.Parameters.AddWithValue("$expected", row.Generation);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        if (resultingEffect is not null) await WriteEffectAsync(connection, transaction, resultingEffect, cancellationToken).ConfigureAwait(false);
        await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(new BaseActivationTransitionResult
        {
            State = state, Generation = generation, ControlChecksum = control.ToImmutableArray(),
            Accounting = ActivationAccounting(1, 64), Disposition = BaseMutationRequestDisposition.Committed, Effect = resultingEffect,
        });
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorRegistrationResult>> RegisterExecutorAsync(
        BaseExecutorRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (string.IsNullOrWhiteSpace(request.ApplicationId) || string.IsNullOrWhiteSpace(request.HostId) ||
            string.IsNullOrWhiteSpace(request.ProcessIncarnationId) || request.WorkerDefinitionSetChecksum.Length != 32 ||
            request.RequestedHeartbeatMilliseconds <= 0 || request.AcceptedTime.ApplicationId != request.ApplicationId)
            return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        SqliteExecutorRow? existing = await ReadExecutorAsync(connection, transaction, request.ApplicationId, request.HostId, request.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
        if (existing is { Retired: false })
            return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.executorConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        long generation;
        await using (SqliteCommand maximum = connection.CreateCommand())
        {
            maximum.Transaction = transaction;
            maximum.CommandText = $"SELECT COALESCE(MAX(executor_generation),0)+1 FROM {_names.Executors};";
            generation = Convert.ToInt64(await maximum.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }
        (_, long restoreEpoch) = await ReadActivationAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        byte[] authorityChecksum = ActivationHash($"base.activation.executor.v2\0{request.ApplicationId}\n{request.HostId}\n{request.ProcessIncarnationId}\n{generation}\n{_options.StoreId}\n{restoreEpoch}\n{Convert.ToHexString(request.WorkerDefinitionSetChecksum.AsSpan())}");
        var authority = new BaseExecutorIncarnationAuthority
        {
            ApplicationId = request.ApplicationId, HostId = request.HostId, ProcessIncarnationId = request.ProcessIncarnationId,
            ExecutorGeneration = generation, StoreInstanceId = _options.StoreId, RestoreEpoch = restoreEpoch,
            WorkerDefinitionSetChecksum = request.WorkerDefinitionSetChecksum.ToArray().ToImmutableArray(), Checksum = authorityChecksum.ToImmutableArray(),
        };
        BaseExecutorHeartbeatObservation heartbeat = ExecutorHeartbeat(authority, 1, checked(request.AcceptedTime.CapturedUtc + request.RequestedHeartbeatMilliseconds));
        await WriteExecutorAsync(connection, transaction, authority, heartbeat, false, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(new BaseExecutorRegistrationResult
        { Executor = authority, Heartbeat = heartbeat, Accounting = ActivationAccounting(1, 128), Disposition = BaseMutationRequestDisposition.Committed });
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorHeartbeatResult>> HeartbeatExecutorAsync(
        BaseExecutorHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseExecutorHeartbeatResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        SqliteExecutorRow? row = await ReadExecutorAsync(connection, transaction, request.Executor.ApplicationId, request.Executor.HostId, request.Executor.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
        if (row is null || row.Retired || !SqliteExecutorMatches(row.Authority, request.Executor) ||
            row.Heartbeat.HeartbeatRevision != request.ExpectedHeartbeatRevision || row.Heartbeat.HeartbeatExpiresAt < request.AcceptedTime.CapturedUtc)
            return ActivationFailure<BaseExecutorHeartbeatResult>("base.activation.executorLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        BaseExecutorHeartbeatObservation heartbeat = ExecutorHeartbeat(row.Authority, checked(row.Heartbeat.HeartbeatRevision + 1),
            checked(request.AcceptedTime.CapturedUtc + request.ExtensionMilliseconds));
        await WriteExecutorAsync(connection, transaction, row.Authority, heartbeat, false, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(new BaseExecutorHeartbeatResult
        { Executor = row.Authority, Heartbeat = heartbeat, Accounting = ActivationAccounting(1, 128), Disposition = BaseMutationRequestDisposition.Committed });
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorRetirementResult>> RetireExecutorAsync(
        BaseExecutorRetirementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseExecutorRetirementResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        SqliteExecutorRow? row = await ReadExecutorAsync(connection, transaction, request.Executor.ApplicationId, request.Executor.HostId, request.Executor.ProcessIncarnationId, cancellationToken).ConfigureAwait(false);
        if (row is null || row.Retired || !SqliteExecutorMatches(row.Authority, request.Executor) || row.Heartbeat.HeartbeatRevision != request.ExpectedHeartbeatRevision)
            return ActivationFailure<BaseExecutorRetirementResult>("base.activation.executorLost", OperationStatus.Conflict, ErrorCategory.Conflict);
        await WriteExecutorAsync(connection, transaction, row.Authority, row.Heartbeat, true, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        byte[] checksum = ActivationHash($"base.activation.executor.retired.v2\0{Convert.ToHexString(row.Authority.Checksum.AsSpan())}\n{row.Heartbeat.HeartbeatRevision}");
        return OperationResults.Ok(new BaseExecutorRetirementResult
        {
            Executor = row.Authority, HeartbeatRevision = row.Heartbeat.HeartbeatRevision, RetirementChecksum = checksum.ToImmutableArray(),
            Accounting = ActivationAccounting(1, 128), Disposition = BaseMutationRequestDisposition.Committed,
        });
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleAuthority>> ReadScheduleAsync(
        string scheduleId, int scheduleVersion, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        BaseScheduleAuthority? authority = await ReadScheduleCoreAsync(connection, null, scheduleId, scheduleVersion, cancellationToken).ConfigureAwait(false);
        return authority is null
            ? ActivationFailure<BaseScheduleAuthority>("base.activation.scheduleNotFound", OperationStatus.NotFound, ErrorCategory.NotFound)
            : OperationResults.Ok(authority);
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleMutationResult>> MutateScheduleAsync(
        BaseScheduleMutationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseScheduleMutationResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        BaseScheduleDefinition definition;
        try { definition = BaseScheduleDefinitionBuilder.Create(request.Definition); }
        catch { return ActivationFailure<BaseScheduleMutationResult>("base.activation.scheduleInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation); }
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        BaseScheduleAuthority? existing = await ReadScheduleCoreAsync(connection, transaction, definition.Id, definition.Version, cancellationToken).ConfigureAwait(false);
        if (request.Kind == BaseScheduleMutationKind.Create && existing is not null ||
            request.Kind != BaseScheduleMutationKind.Create && (existing is null || existing.DefinitionGeneration != request.ExpectedDefinitionGeneration))
            return ActivationFailure<BaseScheduleMutationResult>("base.activation.scheduleConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        if (request.Kind == BaseScheduleMutationKind.Remove)
        {
            await using SqliteCommand remove = connection.CreateCommand(); remove.Transaction = transaction;
            remove.CommandText = $"DELETE FROM {_names.ActivationSchedules} WHERE schedule_id=$id AND schedule_version=$version;";
            remove.Parameters.AddWithValue("$id", definition.Id); remove.Parameters.AddWithValue("$version", definition.Version);
            await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok(new BaseScheduleMutationResult { Authority = null, Accounting = ActivationAccounting(1, 64), Disposition = BaseMutationRequestDisposition.Committed });
        }
        long generation = existing is null ? 1 : checked(existing.DefinitionGeneration + 1);
        long epoch = existing is null ? 1 : request.Kind == BaseScheduleMutationKind.Update ? checked(existing.ScheduleEpoch + 1) : existing.ScheduleEpoch;
        bool enabled = request.Kind switch { BaseScheduleMutationKind.Disable => false, BaseScheduleMutationKind.Enable => true, _ => existing?.Enabled ?? true };
        long? last = request.Kind == BaseScheduleMutationKind.Update ? null : existing?.LastConsideredNominal;
        long? following = request.Kind == BaseScheduleMutationKind.Update || existing is null ? request.InitialNextNominal : existing.NextNominal;
        BaseScheduleAuthority authority = SqliteScheduleAuthority(definition, generation, enabled, epoch, last, following);
        await WriteScheduleAsync(connection, transaction, authority, cancellationToken).ConfigureAwait(false); await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(new BaseScheduleMutationResult { Authority = authority, Accounting = ActivationAccounting(1, 128), Disposition = BaseMutationRequestDisposition.Committed });
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleMaintenancePage>> AdvanceSchedulesAsync(
        BaseScheduleMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AcceptActivationTimeAsync(request.AcceptedTime, cancellationToken).ConfigureAwait(false))
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (request.Occurrences.Length is < 1 or > 256)
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        BaseScheduleAuthority? authority = await ReadScheduleCoreAsync(connection, transaction, request.ScheduleId, request.ScheduleVersion, cancellationToken).ConfigureAwait(false);
        if (authority is null || !authority.Enabled || !CryptographicOperations.FixedTimeEquals(authority.Checksum.AsSpan(), request.ExpectedAuthorityChecksum.AsSpan()))
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.scheduleConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
        long previous = authority.LastConsideredNominal ?? -1;
        foreach (BaseScheduleOccurrenceProposal proposal in request.Occurrences)
        {
            BaseScheduleOccurrenceFact fact = proposal.Fact;
            if (fact.ScheduleId != authority.Definition.Id || fact.ScheduleEpoch != authority.ScheduleEpoch || fact.NominalAt <= previous ||
                !SqliteOccurrenceShapeValid(proposal) || !CryptographicOperations.FixedTimeEquals(fact.Checksum.AsSpan(), SqliteOccurrenceChecksum(fact)))
                return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.occurrenceInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            previous = fact.NominalAt;
            await using (SqliteCommand occurrence = connection.CreateCommand())
            {
                occurrence.Transaction = transaction;
                occurrence.CommandText = $"INSERT INTO {_names.ActivationOccurrences}(occurrence_id,schedule_id,schedule_version,schedule_epoch,nominal_at,effective_at,overlap_ordinal,fact_json,fact_checksum) VALUES($occurrence,$schedule,$version,$epoch,$nominal,$effective,$ordinal,$json,$checksum);";
                occurrence.Parameters.AddWithValue("$occurrence", fact.OccurrenceId); occurrence.Parameters.AddWithValue("$schedule", fact.ScheduleId); occurrence.Parameters.AddWithValue("$version", request.ScheduleVersion);
                occurrence.Parameters.AddWithValue("$epoch", fact.ScheduleEpoch); occurrence.Parameters.AddWithValue("$nominal", fact.NominalAt); occurrence.Parameters.AddWithValue("$effective", fact.EffectiveAt); occurrence.Parameters.AddWithValue("$ordinal", fact.OverlapOrdinal);
                occurrence.Parameters.Add("$json", SqliteType.Blob).Value = JsonSerializer.SerializeToUtf8Bytes(fact, HPDBaseJsonSerializerContext.Default.BaseScheduleOccurrenceFact);
                occurrence.Parameters.Add("$checksum", SqliteType.Blob).Value = fact.Checksum.ToArray();
                try { await occurrence.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                { return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.occurrenceConflict", OperationStatus.Conflict, ErrorCategory.Conflict); }
            }
            if (proposal.Activation is { } activation)
            {
                string activationId = ((BaseOccurrenceMaterialized)fact.Disposition).ActivationId;
                byte[] fingerprint = SqliteScheduleActivationFingerprint(activation, fact.OccurrenceId);
                await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction = transaction;
                insert.CommandText = $"INSERT INTO {_names.Activations}(activation_id,definition_id,definition_version,definition_checksum,canonical_input,input_checksum,scope_kind,scope_value,scope_digest,payload_checksum,fingerprint,state,generation,requested_due_at,effective_due_at,occurrence_id,priority,overlap_key,overlap_policy,eligible,control_checksum) VALUES($id,$definition,$version,$definition_checksum,$input,$input_checksum,$scope_kind,$scope_value,$scope_digest,$payload_checksum,$fingerprint,$state,1,$requested,$effective,$occurrence,$priority,$overlap_key,$overlap_policy,$eligible,$control);";
                insert.Parameters.AddWithValue("$id", activationId); insert.Parameters.AddWithValue("$definition", activation.Definition.Id); insert.Parameters.AddWithValue("$version", activation.Definition.Version);
                insert.Parameters.Add("$definition_checksum", SqliteType.Blob).Value = activation.Definition.Checksum.ToArray(); insert.Parameters.Add("$input", SqliteType.Blob).Value = activation.CanonicalInput.ToArray(); insert.Parameters.Add("$input_checksum", SqliteType.Blob).Value = activation.InputChecksum.ToArray();
                insert.Parameters.AddWithValue("$scope_kind", (int)activation.Scope.Kind); insert.Parameters.AddWithValue("$scope_value", activation.Scope.Value ?? string.Empty); insert.Parameters.Add("$scope_digest", SqliteType.Blob).Value = ActivationHash($"base.activation.scope.v2\0{(int)activation.Scope.Kind}\n{activation.Scope.Value ?? string.Empty}");
                insert.Parameters.Add("$payload_checksum", SqliteType.Blob).Value = SHA256.HashData(activation.CanonicalInput.AsSpan()); insert.Parameters.Add("$fingerprint", SqliteType.Blob).Value = fingerprint; insert.Parameters.AddWithValue("$state", (int)BaseActivationState.Pending);
                insert.Parameters.AddWithValue("$requested", activation.RequestedDueAt); insert.Parameters.AddWithValue("$effective", activation.EffectiveDueAt ?? activation.RequestedDueAt);
                insert.Parameters.AddWithValue("$occurrence", (object?)activation.OccurrenceId ?? DBNull.Value); insert.Parameters.AddWithValue("$priority", activation.Priority);
                insert.Parameters.Add("$overlap_key", SqliteType.Blob).Value = activation.OverlapKey.IsDefaultOrEmpty ? DBNull.Value : activation.OverlapKey.ToArray();
                insert.Parameters.AddWithValue("$overlap_policy", (int)activation.OverlapPolicy); insert.Parameters.AddWithValue("$eligible", activation.InitiallyEligible ? 1 : 0);
                insert.Parameters.Add("$control", SqliteType.Blob).Value = ActivationControlChecksum(activationId, 1, BaseActivationState.Pending);
                try { await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                { return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict); }
            }
        }
        if (previous != request.ResultingLastConsideredNominal)
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.occurrenceInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        BaseScheduleAuthority replacement = SqliteScheduleAuthority(authority.Definition, authority.DefinitionGeneration, true, authority.ScheduleEpoch,
            request.ResultingLastConsideredNominal, request.ResultingNextNominal);
        await WriteScheduleAsync(connection, transaction, replacement, cancellationToken).ConfigureAwait(false);
        await IncrementActivationGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResults.Ok(new BaseScheduleMaintenancePage { Authority = replacement,
            Occurrences = request.Occurrences.Select(static value => value.Fact).ToImmutableArray(), Accounting = ActivationAccounting(request.Occurrences.Length, request.Occurrences.Length * 128L),
            Disposition = BaseMutationRequestDisposition.Committed });
    }

    private async ValueTask<SqliteExecutorRow?> ReadExecutorAsync(
        SqliteConnection connection, SqliteTransaction transaction, string applicationId, string hostId, string processId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT executor_generation,store_instance_id,restore_epoch,worker_set_checksum,authority_checksum,heartbeat_revision,heartbeat_expires_at,heartbeat_checksum,retired FROM {_names.Executors} WHERE application_id=$application AND host_id=$host AND process_incarnation_id=$process;";
        command.Parameters.AddWithValue("$application", applicationId); command.Parameters.AddWithValue("$host", hostId); command.Parameters.AddWithValue("$process", processId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var authority = new BaseExecutorIncarnationAuthority
        {
            ApplicationId = applicationId, HostId = hostId, ProcessIncarnationId = processId, ExecutorGeneration = reader.GetInt64(0),
            StoreInstanceId = reader.GetString(1), RestoreEpoch = reader.GetInt64(2), WorkerDefinitionSetChecksum = ((byte[])reader[3]).ToImmutableArray(),
            Checksum = ((byte[])reader[4]).ToImmutableArray(),
        };
        return new SqliteExecutorRow(authority, new BaseExecutorHeartbeatObservation
        {
            HeartbeatRevision = reader.GetInt64(5), HeartbeatExpiresAt = reader.GetInt64(6),
            ExecutorAuthorityChecksum = authority.Checksum, Checksum = ((byte[])reader[7]).ToImmutableArray(),
        }, reader.GetInt64(8) != 0);
    }

    private async ValueTask<BaseScheduleAuthority?> ReadScheduleCoreAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string id, int version, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT definition_json,definition_generation,enabled,schedule_epoch,last_nominal,next_nominal,authority_checksum FROM {_names.ActivationSchedules} WHERE schedule_id=$id AND schedule_version=$version;";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$version", version);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        BaseScheduleDefinition definition = JsonSerializer.Deserialize((byte[])reader[0], HPDBaseJsonSerializerContext.Default.BaseScheduleDefinition)
            ?? throw new InvalidOperationException("base.activation.scheduleInvalid");
        definition = BaseScheduleDefinitionBuilder.Create(definition);
        var authority = new BaseScheduleAuthority { Definition = definition, DefinitionGeneration = reader.GetInt64(1), Enabled = reader.GetInt64(2) != 0,
            ScheduleEpoch = reader.GetInt64(3), LastConsideredNominal = reader.IsDBNull(4) ? null : reader.GetInt64(4), NextNominal = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            Checksum = ((byte[])reader[6]).ToImmutableArray() };
        BaseScheduleAuthority expected = SqliteScheduleAuthority(definition, authority.DefinitionGeneration, authority.Enabled, authority.ScheduleEpoch, authority.LastConsideredNominal, authority.NextNominal);
        if (!CryptographicOperations.FixedTimeEquals(authority.Checksum.AsSpan(), expected.Checksum.AsSpan())) throw new InvalidOperationException("base.activation.scheduleInvalid");
        return authority;
    }

    private async ValueTask WriteScheduleAsync(SqliteConnection connection, SqliteTransaction transaction, BaseScheduleAuthority authority, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT OR REPLACE INTO {_names.ActivationSchedules}(schedule_id,schedule_version,definition_json,definition_generation,enabled,schedule_epoch,last_nominal,next_nominal,authority_checksum) VALUES($id,$version,$definition,$generation,$enabled,$epoch,$last,$next,$checksum);";
        command.Parameters.AddWithValue("$id", authority.Definition.Id); command.Parameters.AddWithValue("$version", authority.Definition.Version);
        command.Parameters.Add("$definition", SqliteType.Blob).Value = JsonSerializer.SerializeToUtf8Bytes(authority.Definition, HPDBaseJsonSerializerContext.Default.BaseScheduleDefinition);
        command.Parameters.AddWithValue("$generation", authority.DefinitionGeneration); command.Parameters.AddWithValue("$enabled", authority.Enabled ? 1 : 0); command.Parameters.AddWithValue("$epoch", authority.ScheduleEpoch);
        command.Parameters.AddWithValue("$last", (object?)authority.LastConsideredNominal ?? DBNull.Value); command.Parameters.AddWithValue("$next", (object?)authority.NextNominal ?? DBNull.Value);
        command.Parameters.Add("$checksum", SqliteType.Blob).Value = authority.Checksum.ToArray(); await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BaseScheduleAuthority SqliteScheduleAuthority(BaseScheduleDefinition definition, long generation, bool enabled, long epoch, long? last, long? next)
    {
        byte[] checksum = ActivationHash($"base.activation.schedule.authority.v2\0{definition.Id}\n{definition.Version}\n{Convert.ToHexString(definition.Checksum.AsSpan())}\n{generation}\n{enabled}\n{epoch}\n{last?.ToString() ?? "none"}\n{next?.ToString() ?? "none"}");
        return new BaseScheduleAuthority { Definition = BaseScheduleDefinitionBuilder.Create(definition), DefinitionGeneration = generation, Enabled = enabled,
            ScheduleEpoch = epoch, LastConsideredNominal = last, NextNominal = next, Checksum = checksum.ToImmutableArray() };
    }

    private static bool SqliteOccurrenceShapeValid(BaseScheduleOccurrenceProposal proposal) => proposal.Fact.Disposition switch
    {
        BaseOccurrenceMaterialized value => proposal.Activation is not null && value.ActivationId.Length > 0,
        BaseOccurrenceSkippedMisfire => proposal.Activation is null,
        BaseOccurrenceSkippedOverlap value => proposal.Activation is null && value.BlockingActivationId.Length > 0,
        BaseOccurrenceCancelled value => proposal.Activation is null && value.CancellationReceiptId.Length > 0,
        BaseOccurrenceSuppressedByReplacement value => proposal.Activation is null && value.ReplacementGeneration > 0,
        BaseOccurrenceSuppressedByRestoreFloor value => proposal.Activation is null && value.FloorChecksum.Length == 32,
        _ => false,
    };

    private static byte[] SqliteOccurrenceChecksum(BaseScheduleOccurrenceFact fact) => ActivationHash(
        $"base.activation.schedule.occurrence.v2\0{fact.OccurrenceId}\n{fact.ScheduleId}\n{fact.ScheduleEpoch}\n{fact.NominalAt}\n{fact.EffectiveAt}\n{fact.OverlapOrdinal}\n{SqliteDispositionText(fact.Disposition)}");
    private static string SqliteDispositionText(BaseScheduleOccurrenceDisposition disposition) => disposition switch
    {
        BaseOccurrenceMaterialized value => $"materialized:{value.ActivationId}", BaseOccurrenceSkippedMisfire => "skipped-misfire",
        BaseOccurrenceSkippedOverlap value => $"skipped-overlap:{value.BlockingActivationId}", BaseOccurrenceCancelled value => $"cancelled:{value.CancellationReceiptId}",
        BaseOccurrenceSuppressedByReplacement value => $"replacement:{value.ReplacementGeneration}", BaseOccurrenceSuppressedByRestoreFloor value => $"restore:{Convert.ToHexString(value.FloorChecksum.AsSpan())}",
        _ => throw new InvalidOperationException("base.activation.occurrenceInvalid"),
    };
    private static byte[] SqliteScheduleActivationFingerprint(BaseActivationCreateIntent activation, string occurrenceId) =>
        ActivationHash($"base.activation.schedule.create.v2\0{occurrenceId}\n{activation.Definition.Id}\n{activation.Definition.Version}\n{Convert.ToHexString(activation.InputChecksum.AsSpan())}\n{activation.RequestedDueAt}\n{activation.EffectiveDueAt ?? activation.RequestedDueAt}");

    private async ValueTask<bool> AcceptActivationTimeAsync(BaseAcceptedTimeReceipt receipt, CancellationToken cancellationToken)
    {
        long native = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (!BaseActivationAcceptedTimeAuthority.Verify(receipt, native)) return false;
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        long persisted;
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='activation_accepted_utc';";
            persisted = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        if (receipt.CapturedUtc < persisted) return false;
        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {_names.ProviderState} SET value=$value WHERE key='activation_accepted_utc';";
            update.Parameters.AddWithValue("$value", receipt.CapturedUtc.ToString(CultureInfo.InvariantCulture));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) return false;
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<BaseEffectExecutionAuthority?> ReadEffectAsync(
        SqliteConnection connection, SqliteTransaction transaction, string activationId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT claim_attempt,claim_epoch,claim_fence,claim_worker,cancellation_generation,claim_store_id,claim_restore_epoch,definition_checksum,executor_application,executor_host,executor_process,executor_generation,executor_store_id,executor_restore_epoch,worker_set_checksum,executor_checksum,effect_start_generation,heartbeat_revision,heartbeat_expires_at,effect_checksum FROM {_names.ActivationEffects} WHERE activation_id=$id;";
        command.Parameters.AddWithValue("$id", activationId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var claim = new BaseActivationClaimAuthority
        {
            ActivationId = activationId, AttemptNumber = reader.GetInt32(0), ClaimEpoch = reader.GetInt64(1), FencingToken = ((byte[])reader[2]).ToImmutableArray(),
            WorkerIdentity = reader.GetString(3), CancellationGeneration = reader.GetInt64(4), StoreInstanceId = reader.GetString(5),
            RestoreEpoch = reader.GetInt64(6), DefinitionChecksum = ((byte[])reader[7]).ToImmutableArray(),
        };
        var executor = new BaseExecutorIncarnationAuthority
        {
            ApplicationId = reader.GetString(8), HostId = reader.GetString(9), ProcessIncarnationId = reader.GetString(10),
            ExecutorGeneration = reader.GetInt64(11), StoreInstanceId = reader.GetString(12), RestoreEpoch = reader.GetInt64(13),
            WorkerDefinitionSetChecksum = ((byte[])reader[14]).ToImmutableArray(), Checksum = ((byte[])reader[15]).ToImmutableArray(),
        };
        return new BaseEffectExecutionAuthority
        {
            Claim = claim, Executor = executor, EffectStartGeneration = reader.GetInt64(16), HeartbeatRevision = reader.GetInt64(17),
            HeartbeatExpiresAt = reader.GetInt64(18), Checksum = ((byte[])reader[19]).ToImmutableArray(),
        };
    }

    private async ValueTask WriteEffectAsync(SqliteConnection connection, SqliteTransaction transaction,
        BaseEffectExecutionAuthority effect, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT OR REPLACE INTO {_names.ActivationEffects}(activation_id,claim_attempt,claim_epoch,claim_fence,claim_worker,cancellation_generation,claim_store_id,claim_restore_epoch,definition_checksum,executor_application,executor_host,executor_process,executor_generation,executor_store_id,executor_restore_epoch,worker_set_checksum,executor_checksum,effect_start_generation,heartbeat_revision,heartbeat_expires_at,effect_checksum) VALUES($id,$attempt,$epoch,$fence,$worker,$cancel,$claim_store,$claim_restore,$definition,$application,$host,$process,$generation,$executor_store,$executor_restore,$worker_set,$executor_checksum,$start,$revision,$expires,$effect_checksum);";
        command.Parameters.AddWithValue("$id", effect.Claim.ActivationId); command.Parameters.AddWithValue("$attempt", effect.Claim.AttemptNumber); command.Parameters.AddWithValue("$epoch", effect.Claim.ClaimEpoch);
        command.Parameters.Add("$fence", SqliteType.Blob).Value = effect.Claim.FencingToken.ToArray(); command.Parameters.AddWithValue("$worker", effect.Claim.WorkerIdentity);
        command.Parameters.AddWithValue("$cancel", effect.Claim.CancellationGeneration); command.Parameters.AddWithValue("$claim_store", effect.Claim.StoreInstanceId); command.Parameters.AddWithValue("$claim_restore", effect.Claim.RestoreEpoch);
        command.Parameters.Add("$definition", SqliteType.Blob).Value = effect.Claim.DefinitionChecksum.ToArray(); command.Parameters.AddWithValue("$application", effect.Executor.ApplicationId);
        command.Parameters.AddWithValue("$host", effect.Executor.HostId); command.Parameters.AddWithValue("$process", effect.Executor.ProcessIncarnationId); command.Parameters.AddWithValue("$generation", effect.Executor.ExecutorGeneration);
        command.Parameters.AddWithValue("$executor_store", effect.Executor.StoreInstanceId); command.Parameters.AddWithValue("$executor_restore", effect.Executor.RestoreEpoch);
        command.Parameters.Add("$worker_set", SqliteType.Blob).Value = effect.Executor.WorkerDefinitionSetChecksum.ToArray(); command.Parameters.Add("$executor_checksum", SqliteType.Blob).Value = effect.Executor.Checksum.ToArray();
        command.Parameters.AddWithValue("$start", effect.EffectStartGeneration); command.Parameters.AddWithValue("$revision", effect.HeartbeatRevision); command.Parameters.AddWithValue("$expires", effect.HeartbeatExpiresAt);
        command.Parameters.Add("$effect_checksum", SqliteType.Blob).Value = effect.Checksum.ToArray(); await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BaseEffectExecutionAuthority SqliteEffect(BaseActivationClaimAuthority claim, BaseExecutorIncarnationAuthority executor,
        long generation, long revision, long expiresAt)
    {
        byte[] checksum = ActivationHash($"base.activation.effect.v2\0{claim.ActivationId}\n{Convert.ToHexString(claim.FencingToken.AsSpan())}\n{Convert.ToHexString(executor.Checksum.AsSpan())}\n{generation}\n{revision}\n{expiresAt}");
        return new BaseEffectExecutionAuthority { Claim = claim, Executor = executor, EffectStartGeneration = generation,
            HeartbeatRevision = revision, HeartbeatExpiresAt = expiresAt, Checksum = checksum.ToImmutableArray() };
    }

    private static bool SqliteEffectMatches(BaseEffectExecutionAuthority left, BaseEffectExecutionAuthority right) =>
        left.EffectStartGeneration == right.EffectStartGeneration && left.HeartbeatRevision == right.HeartbeatRevision && left.HeartbeatExpiresAt == right.HeartbeatExpiresAt &&
        left.Claim.ActivationId == right.Claim.ActivationId && left.Claim.AttemptNumber == right.Claim.AttemptNumber && left.Claim.ClaimEpoch == right.Claim.ClaimEpoch &&
        CryptographicOperations.FixedTimeEquals(left.Claim.FencingToken.AsSpan(), right.Claim.FencingToken.AsSpan()) &&
        SqliteExecutorMatches(left.Executor, right.Executor) && CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private async ValueTask WriteExecutorAsync(SqliteConnection connection, SqliteTransaction transaction,
        BaseExecutorIncarnationAuthority authority, BaseExecutorHeartbeatObservation heartbeat, bool retired, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT OR REPLACE INTO {_names.Executors}(application_id,host_id,process_incarnation_id,executor_generation,store_instance_id,restore_epoch,worker_set_checksum,authority_checksum,heartbeat_revision,heartbeat_expires_at,heartbeat_checksum,retired) VALUES($application,$host,$process,$generation,$store,$restore,$workers,$authority,$revision,$expires,$heartbeat,$retired);";
        command.Parameters.AddWithValue("$application", authority.ApplicationId); command.Parameters.AddWithValue("$host", authority.HostId); command.Parameters.AddWithValue("$process", authority.ProcessIncarnationId);
        command.Parameters.AddWithValue("$generation", authority.ExecutorGeneration); command.Parameters.AddWithValue("$store", authority.StoreInstanceId); command.Parameters.AddWithValue("$restore", authority.RestoreEpoch);
        command.Parameters.Add("$workers", SqliteType.Blob).Value = authority.WorkerDefinitionSetChecksum.ToArray(); command.Parameters.Add("$authority", SqliteType.Blob).Value = authority.Checksum.ToArray();
        command.Parameters.AddWithValue("$revision", heartbeat.HeartbeatRevision); command.Parameters.AddWithValue("$expires", heartbeat.HeartbeatExpiresAt); command.Parameters.Add("$heartbeat", SqliteType.Blob).Value = heartbeat.Checksum.ToArray();
        command.Parameters.AddWithValue("$retired", retired ? 1 : 0); await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BaseExecutorHeartbeatObservation ExecutorHeartbeat(BaseExecutorIncarnationAuthority authority, long revision, long expiresAt)
    {
        byte[] checksum = ActivationHash($"base.activation.executor.heartbeat.v2\0{Convert.ToHexString(authority.Checksum.AsSpan())}\n{revision}\n{expiresAt}");
        return new BaseExecutorHeartbeatObservation { HeartbeatRevision = revision, HeartbeatExpiresAt = expiresAt,
            ExecutorAuthorityChecksum = authority.Checksum.ToArray().ToImmutableArray(), Checksum = checksum.ToImmutableArray() };
    }

    private static bool SqliteExecutorMatches(BaseExecutorIncarnationAuthority left, BaseExecutorIncarnationAuthority right) =>
        left.ApplicationId == right.ApplicationId && left.HostId == right.HostId && left.ProcessIncarnationId == right.ProcessIncarnationId &&
        left.ExecutorGeneration == right.ExecutorGeneration && left.StoreInstanceId == right.StoreInstanceId && left.RestoreEpoch == right.RestoreEpoch &&
        CryptographicOperations.FixedTimeEquals(left.WorkerDefinitionSetChecksum.AsSpan(), right.WorkerDefinitionSetChecksum.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static bool SqliteHeartbeatsEqual(BaseExecutorHeartbeatObservation left, BaseExecutorHeartbeatObservation right) =>
        left.HeartbeatRevision == right.HeartbeatRevision && left.HeartbeatExpiresAt == right.HeartbeatExpiresAt &&
        CryptographicOperations.FixedTimeEquals(left.ExecutorAuthorityChecksum.AsSpan(), right.ExecutorAuthorityChecksum.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private sealed record SqliteExecutorRow(BaseExecutorIncarnationAuthority Authority, BaseExecutorHeartbeatObservation Heartbeat, bool Retired);

    private async ValueTask<List<SqliteActivationRow>> ReadDueRowsAsync(
        SqliteConnection connection, SqliteTransaction? transaction, ImmutableArray<BaseActivationDefinitionKey> definitions,
        BaseOwnedScopeSeekAuthority scope, long now, BaseActivationDueBoundary? after, int take, CancellationToken cancellationToken)
    {
        if (definitions.IsDefaultOrEmpty) return [];
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        var predicate = new StringBuilder();
        for (int i = 0; i < definitions.Length; i++)
        {
            if (i > 0) predicate.Append(" OR ");
            predicate.Append($"(definition_id=$definition{i} AND definition_version=$version{i} AND definition_checksum=$checksum{i})");
            command.Parameters.AddWithValue($"$definition{i}", definitions[i].Id);
            command.Parameters.AddWithValue($"$version{i}", definitions[i].Version);
            command.Parameters.Add($"$checksum{i}", SqliteType.Blob).Value = definitions[i].Checksum.ToArray();
        }
        command.CommandText = $"SELECT activation_id,definition_id,definition_version,definition_checksum,canonical_input,input_checksum,scope_kind,scope_value,payload_checksum,state,generation,requested_due_at,effective_due_at,control_checksum,attempt_number,claim_epoch,claim_fence,claim_worker,lease_revision,lease_expires_at,occurrence_id,priority,overlap_key,overlap_policy,eligible FROM {_names.Activations} INDEXED BY {_names.Prefix}activation_due_idx WHERE scope_kind=$scope_kind AND scope_digest=$scope_digest AND eligible=1 AND ((state IN ($pending,$retry) AND effective_due_at<=$now) OR (state=$claimed AND lease_expires_at<=$now)) AND (overlap_policy<>$queue OR overlap_key IS NULL OR NOT EXISTS(SELECT 1 FROM {_names.Activations} b WHERE b.overlap_key={_names.Activations}.overlap_key AND b.activation_id<>{_names.Activations}.activation_id AND b.state IN ($pending,$retry,$claimed) AND (b.effective_due_at<{_names.Activations}.effective_due_at OR (b.effective_due_at={_names.Activations}.effective_due_at AND b.activation_id<{_names.Activations}.activation_id)))) AND ({predicate}) AND ($after_priority IS NULL OR MIN(32,priority+CAST(MAX(0,$now-effective_due_at)/60000 AS INTEGER))<$after_priority OR (MIN(32,priority+CAST(MAX(0,$now-effective_due_at)/60000 AS INTEGER))=$after_priority AND (effective_due_at>$after_due OR (effective_due_at=$after_due AND (COALESCE(occurrence_id,'')>$after_occurrence OR (COALESCE(occurrence_id,'')=$after_occurrence AND activation_id>$after_id)))))) ORDER BY MIN(32,priority+CAST(MAX(0,$now-effective_due_at)/60000 AS INTEGER)) DESC,effective_due_at,COALESCE(occurrence_id,''),activation_id LIMIT $take;";
        command.Parameters.AddWithValue("$scope_kind", (int)scope.Kind); command.Parameters.Add("$scope_digest", SqliteType.Blob).Value = scope.ProtectedIndexDigest.ToArray();
        command.Parameters.AddWithValue("$pending", (int)BaseActivationState.Pending); command.Parameters.AddWithValue("$retry", (int)BaseActivationState.RetryPending);
        command.Parameters.AddWithValue("$claimed", (int)BaseActivationState.Claimed); command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$queue", (int)BaseScheduleOverlapPolicy.Queue);
        command.Parameters.AddWithValue("$after_priority", (object?)after?.EffectiveAgedPriority ?? DBNull.Value);
        command.Parameters.AddWithValue("$after_due", (object?)after?.EffectiveDueAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$after_occurrence", after?.OccurrenceId ?? string.Empty); command.Parameters.AddWithValue("$after_id", after?.ActivationId ?? string.Empty);
        command.Parameters.AddWithValue("$take", take);
        var result = new List<SqliteActivationRow>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadActivationRow(reader));
        return result;
    }

    private async ValueTask<SqliteActivationRow?> ReadActivationAsync(SqliteConnection connection, SqliteTransaction transaction, string id, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT activation_id,definition_id,definition_version,definition_checksum,canonical_input,input_checksum,scope_kind,scope_value,payload_checksum,state,generation,requested_due_at,effective_due_at,control_checksum,attempt_number,claim_epoch,claim_fence,claim_worker,lease_revision,lease_expires_at,occurrence_id,priority,overlap_key,overlap_policy,eligible FROM {_names.Activations} WHERE activation_id=$id;";
        command.Parameters.AddWithValue("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadActivationRow(reader) : null;
    }

    private static SqliteActivationRow ReadActivationRow(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetInt32(2), (byte[])reader[3], (byte[])reader[4], (byte[])reader[5],
        (BaseSubjectScopeKind)reader.GetInt32(6), reader.GetString(7), (byte[])reader[8], (BaseActivationState)reader.GetInt32(9),
        reader.GetInt64(10), reader.GetInt64(11), reader.GetInt64(12), (byte[])reader[13], reader.GetInt32(14), reader.GetInt64(15),
        reader.IsDBNull(16) ? null : (byte[])reader[16], reader.IsDBNull(17) ? null : reader.GetString(17),
        reader.IsDBNull(18) ? null : reader.GetInt64(18), reader.IsDBNull(19) ? null : reader.GetInt64(19),
        reader.IsDBNull(20) ? null : reader.GetString(20), reader.GetInt32(21), reader.IsDBNull(22) ? null : (byte[])reader[22],
        (BaseScheduleOverlapPolicy)reader.GetInt32(23), reader.GetInt32(24) == 1);

    private async ValueTask<(long Generation, long RestoreEpoch)> ReadActivationAuthorityAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT (SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='activation_generation'),(SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch');";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("Activation authority is unavailable.");
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private async ValueTask IncrementActivationGenerationAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.ProviderState} SET value=CAST(CAST(value AS INTEGER)+1 AS TEXT) WHERE key='activation_generation';";
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidOperationException("Activation generation update failed.");
    }

    private async ValueTask UpdateRecoveredAsync(SqliteConnection connection, SqliteTransaction transaction, string id, long generation, long now, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"UPDATE {_names.Activations} SET state=$state,generation=$generation,claim_fence=NULL,claim_worker=NULL,lease_revision=NULL,lease_expires_at=NULL,effective_due_at=$now,control_checksum=$checksum WHERE activation_id=$id AND state=$claimed AND lease_expires_at<=$now;";
        command.Parameters.AddWithValue("$state", (int)BaseActivationState.RetryPending); command.Parameters.AddWithValue("$generation", generation);
        command.Parameters.AddWithValue("$now", now); command.Parameters.Add("$checksum", SqliteType.Blob).Value = ActivationControlChecksum(id, generation, BaseActivationState.RetryPending);
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$claimed", (int)BaseActivationState.Claimed);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidOperationException("Expired activation recovery conflicted.");
    }

    private static bool SqliteClaimMatches(SqliteActivationRow row, BaseActivationClaimAuthority claim) =>
        row.State == BaseActivationState.Claimed && row.AttemptNumber == claim.AttemptNumber && row.ClaimEpoch == claim.ClaimEpoch &&
        row.ClaimFence is not null && row.ClaimWorker == claim.WorkerIdentity &&
        CryptographicOperations.FixedTimeEquals(row.ClaimFence, claim.FencingToken.AsSpan());

    private static BaseActivationDueBoundary ActivationBoundary(SqliteActivationRow row, long now) => new()
    {
        EffectiveAgedPriority = Math.Min(32, row.Priority + checked((int)Math.Min(int.MaxValue, Math.Max(0, now - row.EffectiveDueAt) / 60_000))),
        EffectiveDueAt = row.EffectiveDueAt, OccurrenceId = row.OccurrenceId, ActivationId = row.ActivationId,
    };

    private static byte[] ActivationDueToken(long generation, long restoreEpoch, long now, ReadOnlySpan<byte> scope,
        ImmutableArray<BaseActivationDefinitionKey> definitions, BaseActivationDueBoundary? first)
    {
        string definitionText = string.Join("\n", definitions.Select(static item => $"{item.Id}:{item.Version}:{Convert.ToHexString(item.Checksum.AsSpan())}"));
        byte[] digest = ActivationHash($"base.activation.due.token.v2\0{generation}\n{restoreEpoch}\n{now}\n{Convert.ToHexString(scope)}\n{definitionText}\n{first?.ActivationId ?? string.Empty}");
        byte[] token = new byte[48]; BinaryPrimitives.WriteInt64BigEndian(token, generation); BinaryPrimitives.WriteInt64BigEndian(token.AsSpan(8), restoreEpoch); digest.CopyTo(token, 16); return token;
    }

    private static (long Generation, long RestoreEpoch) DecodeActivationTokenAuthority(ReadOnlySpan<byte> token) =>
        token.Length == 48 ? (BinaryPrimitives.ReadInt64BigEndian(token), BinaryPrimitives.ReadInt64BigEndian(token[8..])) : (-1, -1);

    private static BaseAtomicReadIntervalEvidence ActivationDueInterval(BaseOwnedScopeSeekAuthority scope, long now,
        BaseActivationDueBoundary? after, BaseActivationDueBoundary? result) => new()
    {
        LogicalAccessPathId = "base.activation.due.byScopeDefinitionPriorityTime.v1",
        CanonicalLowerBound = Encoding.UTF8.GetBytes(after?.ActivationId ?? string.Empty).ToImmutableArray(), LowerInclusive = false,
        CanonicalUpperBound = Encoding.UTF8.GetBytes($"{now}\n{result?.ActivationId ?? string.Empty}\n{Convert.ToHexString(scope.ProtectedIndexDigest.AsSpan())}").ToImmutableArray(), UpperInclusive = true,
    };

    private static long ActivationIntervalBytes(BaseAtomicReadIntervalEvidence interval) =>
        checked(Encoding.UTF8.GetByteCount(interval.LogicalAccessPathId) + interval.CanonicalLowerBound.Length + interval.CanonicalUpperBound.Length + 2);

    private static BaseActivationAccounting ActivationAccounting(int candidates, long evidence) => new()
    { Candidates = candidates, Comparisons = candidates, IndexOperations = 1, ReadIntervals = 1, EvidenceBytes = evidence, TransientBytes = evidence };

    private static bool ActivationLimitsValid(BaseActivationExecutionLimits limits) => limits.MaximumCandidates is > 0 and <= 256 &&
        limits.MaximumInputBytes is > 0 and <= 4L * 1024 * 1024 && limits.MaximumResultBytes is > 0 and <= 4L * 1024 * 1024 &&
        limits.MaximumEvidenceBytes is > 0 and <= 16L * 1024 * 1024 && limits.MaximumTransientBytes is > 0 and <= 16L * 1024 * 1024 &&
        limits.MaximumReadIntervals > 0 && limits.MaximumIndexOperations > 0;

    private static byte[] ActivationControlChecksum(string id, long generation, BaseActivationState state) => ActivationHash($"base.activation.control.v2\0{id}\n{generation}\n{(int)state}");
    private static byte[] ActivationHash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static OperationResult<T> ActivationFailure<T>(string code, OperationStatus status, ErrorCategory category) => new()
    { Status = status, Error = new BaseError { Code = code, Message = "The activation operation could not be completed.", Category = category } };

    private sealed record SqliteActivationRow(
        string ActivationId, string DefinitionId, int DefinitionVersion, byte[] DefinitionChecksum, byte[] CanonicalInput,
        byte[] InputChecksum, BaseSubjectScopeKind ScopeKind, string ScopeValue, byte[] PayloadChecksum,
        BaseActivationState State, long Generation, long RequestedDueAt, long EffectiveDueAt, byte[] ControlChecksum,
        int AttemptNumber, long ClaimEpoch, byte[]? ClaimFence, string? ClaimWorker, long? LeaseRevision, long? LeaseExpiresAt,
        string? OccurrenceId, int Priority, byte[]? OverlapKey, BaseScheduleOverlapPolicy OverlapPolicy, bool Eligible)
    {
        internal BaseActivationPayload Payload() => new()
        {
            ActivationId = ActivationId,
            Definition = new BaseActivationDefinitionKey { Id = DefinitionId, Version = DefinitionVersion, Checksum = DefinitionChecksum.ToImmutableArray() },
            CanonicalInput = CanonicalInput.ToImmutableArray(), InputChecksum = InputChecksum.ToImmutableArray(),
            Scope = new BaseOwnedSubjectScopeEvidence { Kind = ScopeKind, Value = ScopeValue.Length == 0 ? null : ScopeValue },
            Checksum = PayloadChecksum.ToImmutableArray(),
        };
    }
}
