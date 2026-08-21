using System.Globalization;
using System.Diagnostics;
using System.Collections.Immutable;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base.Sqlite;

/// <summary>Represents a sqlite record store.</summary>
public sealed partial class SqliteRecordStore
{
    private static readonly TimeSpan MinimumExecutionTimeout = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    public ValueTask<RecordMutationExecutionResult> ExecuteSingleAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(processor, request, cancellationToken);

    /// <inheritdoc />
    public ValueTask<RecordMutationExecutionResult> ExecuteAtomicAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(processor, request, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<RecordMutationExecutionResult> ResolveAtomicReceiptAsync(
        IAtomicMutationProcessor processor,
        BaseMutationRequestIdentity identity,
        TimeSpan resolutionTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(identity);
        if (resolutionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(resolutionTimeout));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(resolutionTimeout);
        try
        {
            await using SqliteConnection connection = await OpenInitializedAsync(lifetime.Token).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(lifetime.Token).ConfigureAwait(false);
            var request = new BaseAtomicMutationExecutionRequest
            {
                Identity = identity,
                StructuralDigest = new byte[32],
                ExpiresAt = _timeProvider.GetUtcNow().Add(resolutionTimeout),
                MaxReceiptBytes = 4096,
            };
            SqliteMutationReceipt? receipt = await ReadReceiptAsync(connection, transaction, request, lifetime.Token).ConfigureAwait(false);
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            if (receipt is null || !CryptographicOperations.FixedTimeEquals(identity.Fingerprint.ToArray(), receipt.Fingerprint))
                return FailedBeforeCommit(new BaseError
                {
                    Code = BaseMutationRequestErrorCodes.ReceiptUnavailable,
                    Message = "The stored mutation receipt cannot be resolved.",
                    Category = ErrorCategory.Authorization,
                });
            AtomicMutationProcessingResult resolved = await processor.ResolveReceiptAsync(receipt.Result, lifetime.Token).ConfigureAwait(false);
            return resolved.Outcome == AtomicMutationProcessingOutcome.ReadyToCommit
                ? new RecordMutationExecutionResult(RecordMutationExecutionOutcome.Committed, resolved)
                  { RequestDisposition = BaseMutationRequestDisposition.Duplicate }
                : new RecordMutationExecutionResult(RecordMutationExecutionOutcome.RollbackConfirmed, resolved, resolved.Error);
        }
        catch (OperationCanceledException)
        {
            return FailedBeforeCommit(new BaseError
            {
                Code = BaseMutationRequestErrorCodes.ReceiptUnavailable,
                Message = "The stored mutation receipt cannot be resolved.",
                Category = ErrorCategory.Authorization,
            });
        }
        catch
        {
            return FailedBeforeCommit(ProviderError(SqliteErrorCodes.DatabaseUnavailable, "SQLite receipt resolution is unavailable."));
        }
    }

    private async ValueTask<RecordMutationExecutionResult> ExecuteMutationAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(request);
        ValidateExecutionRequest(request);
        string? quarantinedRequestIdentity = QuarantineRequestIdentity(request.AtomicRequest?.Identity);
        if (quarantinedRequestIdentity is not null
            && _quarantinedMutations.Values.Any(value =>
                string.Equals(value.RequestIdentity, quarantinedRequestIdentity, StringComparison.Ordinal)))
            return Indeterminate();

        var acquisitionStarted = Stopwatch.GetTimestamp();
        using var acquisitionLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acquisitionLifetime.CancelAfter(request.AcquisitionTimeout);

        MutationExecutionSlot? executionSlot = null;
        IAsyncDisposable? generationLease = null;
        SqliteConnection? connection = null;
        SqliteTransaction? transaction = null;
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return FailedBeforeCommit(ProviderError(
                    SqliteErrorCodes.DatabaseUnavailable,
                    "SQLite mutation execution is unavailable."));

            generationLease = await _schemaGenerationGate.AcquireSharedAsync(acquisitionLifetime.Token).ConfigureAwait(false);
            await _mutationExecutionSlots.WaitAsync(acquisitionLifetime.Token).ConfigureAwait(false);
            executionSlot = new MutationExecutionSlot(_mutationExecutionSlots);
            if (Volatile.Read(ref _disposed) != 0)
            {
                executionSlot.Dispose();
                return FailedBeforeCommit(ProviderError(
                    SqliteErrorCodes.DatabaseUnavailable,
                    "SQLite mutation execution is unavailable."));
            }
            connection = await OpenInitializedAsync(acquisitionLifetime.Token).ConfigureAwait(false);
            var remaining = request.AcquisitionTimeout - Stopwatch.GetElapsedTime(acquisitionStarted);
            if (remaining <= TimeSpan.Zero)
                throw new OperationCanceledException(acquisitionLifetime.Token);
            transaction = await BeginImmediateAsync(
                connection,
                remaining,
                acquisitionLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
            executionSlot?.Dispose();
            if (generationLease is not null)
                await generationLease.DisposeAsync().ConfigureAwait(false);

            return CancelledBeforeCommit();
        }
        catch (SqliteException ex)
        {
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
            executionSlot?.Dispose();
            if (generationLease is not null)
                await generationLease.DisposeAsync().ConfigureAwait(false);

            if (IsTransactionConflict(ex))
                return ConflictBeforeCommit();

            return FailedBeforeCommit(MapSqlite<object>(BaseOperationKind.Batch, ex).Error!);
        }
        catch (ObjectDisposedException)
        {
            executionSlot?.Dispose();
            if (generationLease is not null)
                await generationLease.DisposeAsync().ConfigureAwait(false);
            return FailedBeforeCommit(ProviderError(
                SqliteErrorCodes.DatabaseUnavailable,
                "SQLite mutation execution is unavailable."));
        }
        catch (InvalidOperationException ex)
        {
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
            executionSlot?.Dispose();
            if (generationLease is not null)
                await generationLease.DisposeAsync().ConfigureAwait(false);

            return FailedBeforeCommit(MapSchemaFailure<object>(ex).Error!);
        }

        var resources = new TransactionResources(
            this,
            connection,
            transaction,
            executionSlot!,
            generationLease!,
            quarantinedRequestIdentity);
        try
        {
            using var processingLifetime =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            processingLifetime.CancelAfter(request.TransactionTimeout);

            var transactionStarted = Stopwatch.GetTimestamp();
            var session = new SqliteAtomicRecordSession(
                this,
                connection,
                transaction,
                transactionStarted,
                request.TransactionTimeout);
            AtomicMutationProcessingResult processing;
            bool duplicate = false;
            try
            {
                SqliteMutationReceipt? receipt = request.AtomicRequest is null
                    ? null
                    : await ReadReceiptAsync(connection, transaction, request.AtomicRequest, processingLifetime.Token).ConfigureAwait(false);
                var processingTask = receipt is null
                    ? processor.ProcessAsync(session, processingLifetime.Token).AsTask()
                    : processor.ResolveReceiptAsync(receipt.Result, processingLifetime.Token).AsTask();
                try
                {
                    processing = await processingTask
                        .WaitAsync(processingLifetime.Token)
                        .ConfigureAwait(false);
                }
                catch
                {
                    ObserveCompletion(processingTask);
                    throw;
                }
                if (receipt is not null && processing.Outcome == AtomicMutationProcessingOutcome.ReadyToCommit)
                {
                    bool fingerprintMatch = CryptographicOperations.FixedTimeEquals(request.AtomicRequest!.Identity.Fingerprint.ToArray(), receipt.Fingerprint);
                    bool structureMatch = CryptographicOperations.FixedTimeEquals(request.AtomicRequest.StructuralDigest, receipt.StructuralDigest);
                    if (!fingerprintMatch || !structureMatch)
                        processing = FailedProcessing(BaseMutationRequestErrorCodes.FingerprintConflict, "The mutation request identity conflicts with an existing receipt.");
                    else
                        duplicate = true;
                }
            }
            catch (OperationCanceledException)
            {
                if (!await CloseSessionAsync(
                        session,
                        resources,
                        request.CommitCompletionTimeout).ConfigureAwait(false))
                    return Indeterminate();
                return await RollbackAsync(
                    resources,
                    transaction,
                    RecordMutationExecutionOutcome.CancelledRollbackConfirmed,
                    FailedProcessing(
                        BaseMutationErrorCodes.TransactionTimeout,
                        "SQLite mutation processing was cancelled or exceeded its bounded lifetime."),
                    request.CommitCompletionTimeout)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (processingLifetime.IsCancellationRequested)
            {
                if (!await CloseSessionAsync(
                        session,
                        resources,
                        request.CommitCompletionTimeout).ConfigureAwait(false))
                    return Indeterminate();
                return await RollbackAsync(
                    resources,
                    transaction,
                    RecordMutationExecutionOutcome.CancelledRollbackConfirmed,
                    FailedProcessing(
                        BaseMutationErrorCodes.TransactionTimeout,
                        "SQLite mutation processing was cancelled or exceeded its bounded lifetime."),
                    request.CommitCompletionTimeout)
                    .ConfigureAwait(false);
            }
            catch
            {
                if (!await CloseSessionAsync(
                        session,
                        resources,
                        request.CommitCompletionTimeout).ConfigureAwait(false))
                    return Indeterminate();
                return await RollbackAsync(
                    resources,
                    transaction,
                    RecordMutationExecutionOutcome.RollbackConfirmed,
                    FailedProcessing(
                        SqliteErrorCodes.DatabaseUnavailable,
                        "SQLite mutation processing failed."),
                    request.CommitCompletionTimeout)
                    .ConfigureAwait(false);
            }

            if (!await CloseSessionAsync(
                    session,
                    resources,
                    request.CommitCompletionTimeout).ConfigureAwait(false))
                return Indeterminate();

            if (processingLifetime.IsCancellationRequested)
            {
                return await RollbackAsync(
                    resources,
                    transaction,
                    RecordMutationExecutionOutcome.CancelledRollbackConfirmed,
                    FailedProcessing(
                        BaseMutationErrorCodes.TransactionTimeout,
                        "SQLite mutation processing was cancelled or exceeded its bounded lifetime.",
                        processing.Mutations),
                    request.CommitCompletionTimeout).ConfigureAwait(false);
            }

            if (processing.Outcome != AtomicMutationProcessingOutcome.ReadyToCommit)
            {
                var outcome = processing.Error?.Code switch
                {
                    BaseMutationErrorCodes.TransactionConflict =>
                        RecordMutationExecutionOutcome.ConflictRollbackConfirmed,
                    BaseMutationErrorCodes.TransactionTimeout =>
                        RecordMutationExecutionOutcome.CancelledRollbackConfirmed,
                    _ => RecordMutationExecutionOutcome.RollbackConfirmed
                };
                return await RollbackAsync(
                    resources,
                    transaction,
                    outcome,
                    processing,
                    request.CommitCompletionTimeout).ConfigureAwait(false);
            }

            // Duplicate resolution returns the already committed receipt and deliberately performs
            // no provisional apply, so it has no fresh Runtime-owned commit finalization to validate.
            if (!duplicate && !session.ValidateCommitFinalization(processing))
            {
                return await RollbackAsync(
                    resources,
                    transaction,
                    RecordMutationExecutionOutcome.RollbackConfirmed,
                    FailedProcessing(BaseSubjectErrorCodes.ProviderContractInvalid, "The mutation commit finalization was invalid.", processing.Mutations),
                    request.CommitCompletionTimeout).ConfigureAwait(false);
            }

            try
            {
                processingLifetime.Token.ThrowIfCancellationRequested();
                if (request.AtomicRequest is { } identified && !duplicate)
                    await InsertReceiptAsync(connection, transaction, identified, processing, processingLifetime.Token).ConfigureAwait(false);
                await PruneMutationJournalAsync(
                    connection,
                    transaction,
                    _timeProvider.GetUtcNow(),
                    processingLifetime.Token,
                    TransactionCommandTimeoutSeconds(
                        transactionStarted,
                        request.TransactionTimeout)).ConfigureAwait(false);
                processingLifetime.Token.ThrowIfCancellationRequested();
                await SetBusyTimeoutAsync(
                    connection,
                    request.CommitCompletionTimeout,
                    processingLifetime.Token).ConfigureAwait(false);
                processingLifetime.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                return await RollbackAsync(
                    resources,
                    transaction,
                    RecordMutationExecutionOutcome.CancelledRollbackConfirmed,
                    FailedProcessing(
                        BaseMutationErrorCodes.TransactionTimeout,
                        "SQLite mutation processing was cancelled or exceeded its bounded lifetime.",
                        processing.Mutations),
                    request.CommitCompletionTimeout)
                    .ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                var conflict = IsTransactionConflict(ex);
                return await RollbackAsync(
                    resources,
                    transaction,
                    conflict
                        ? RecordMutationExecutionOutcome.ConflictRollbackConfirmed
                        : RecordMutationExecutionOutcome.RollbackConfirmed,
                    conflict
                        ? FailedProcessing(TransactionConflict(), processing.Mutations)
                        : FailedProcessing(
                            MapSqlite<object>(BaseOperationKind.Batch, ex).Error!,
                            processing.Mutations),
                    request.CommitCompletionTimeout)
                    .ConfigureAwait(false);
            }
            catch (BaseReceiptTooLargeException)
            {
                return await RollbackAsync(
                    resources,
                    transaction,
                    RecordMutationExecutionOutcome.RollbackConfirmed,
                    FailedProcessing(BaseMutationRequestErrorCodes.ReceiptTooLarge, "The mutation receipt exceeds its configured bound.", processing.Mutations),
                    request.CommitCompletionTimeout).ConfigureAwait(false);
            }

            // Request cancellation stops controlling the result at this point. The controller call
            // runs outside this method's continuation so even a synchronous, non-cooperative native
            // implementation cannot defeat the advertised completion bound.
            using var commitLifetime = new CancellationTokenSource(request.CommitCompletionTimeout);
            var commitTask = InvokeTransactionOperationAsync(
                () => _transactions.CommitAsync(transaction, commitLifetime.Token));
            try
            {
                await commitTask.WaitAsync(commitLifetime.Token).ConfigureAwait(false);
                return new RecordMutationExecutionResult(
                    RecordMutationExecutionOutcome.Committed,
                    processing)
                {
                    RequestDisposition = duplicate ? BaseMutationRequestDisposition.Duplicate : BaseMutationRequestDisposition.Committed,
                };
            }
            catch (OperationCanceledException) when (!commitTask.IsCompleted)
            {
                resources.TransferTo(commitTask);
                return Indeterminate();
            }
            catch (Exception ex)
            {
                using var rollbackLifetime =
                    new CancellationTokenSource(request.CommitCompletionTimeout);
                var rollbackTask = InvokeTransactionOperationAsync(
                    () => _transactions.RollbackAsync(transaction, rollbackLifetime.Token));
                try
                {
                    await rollbackTask.WaitAsync(rollbackLifetime.Token).ConfigureAwait(false);
                    var error = ex switch
                    {
                        SqliteException sqlite when IsTransactionConflict(sqlite) =>
                            TransactionConflict(),
                        OperationCanceledException =>
                            ProviderError(
                                BaseMutationErrorCodes.TransactionTimeout,
                                "SQLite did not complete commit within its bounded lifetime."),
                        SqliteException sqlite =>
                            MapSqlite<object>(BaseOperationKind.Batch, sqlite).Error!,
                        _ =>
                            ProviderError(
                                SqliteErrorCodes.DatabaseUnavailable,
                                "SQLite mutation commit failed.")
                    };
                    return new RecordMutationExecutionResult(
                        error.Code == BaseMutationErrorCodes.TransactionConflict
                            ? RecordMutationExecutionOutcome.ConflictRollbackConfirmed
                            : RecordMutationExecutionOutcome.RollbackConfirmed,
                        processing,
                        error);
                }
                catch (OperationCanceledException) when (!rollbackTask.IsCompleted)
                {
                    resources.TransferTo(rollbackTask);
                    return Indeterminate();
                }
                catch
                {
                    return Indeterminate();
                }
            }
        }
        finally
        {
            await resources.DisposeIfOwnedAsync(request.CommitCompletionTimeout).ConfigureAwait(false);
        }
    }

    private static void ValidateExecutionRequest(RecordMutationExecutionRequest request)
    {
        ValidateExecutionTimeout(request.AcquisitionTimeout, "Acquisition timeout");
        ValidateExecutionTimeout(request.TransactionTimeout, "Transaction timeout");
        ValidateExecutionTimeout(request.CommitCompletionTimeout, "Commit completion timeout");
        if (request.AtomicRequest is { StructuralDigest.Length: not 32 } or { MaxReceiptBytes: < 4096 })
            throw new ArgumentOutOfRangeException(nameof(request), "The identified mutation request bounds are invalid.");
    }

    private async ValueTask<SqliteMutationReceipt?> ReadReceiptAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BaseAtomicMutationExecutionRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT fingerprint, structural_digest, result_json, expires_at FROM {_names.OperationReceipts} WHERE scope=$scope AND operation=$operation AND idempotency_key=$key;";
        command.Parameters.AddWithValue("$scope", request.Identity.Scope);
        command.Parameters.AddWithValue("$operation", request.Identity.Operation);
        command.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        byte[] fingerprint = (byte[])reader[0];
        byte[] structuralDigest = (byte[])reader[1];
        byte[] result = (byte[])reader[2];
        DateTimeOffset expiresAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        await reader.DisposeAsync().ConfigureAwait(false);
        if (expiresAt <= _timeProvider.GetUtcNow())
        {
            await using var remove = connection.CreateCommand();
            remove.Transaction = transaction;
            remove.CommandTimeout = TimeoutSeconds();
            remove.CommandText = $"DELETE FROM {_names.OperationReceipts} WHERE scope=$scope AND operation=$operation AND idempotency_key=$key;";
            remove.Parameters.AddWithValue("$scope", request.Identity.Scope);
            remove.Parameters.AddWithValue("$operation", request.Identity.Operation);
            remove.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey);
            await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        BaseAtomicReceiptWire? receiptWire = JsonSerializer.Deserialize(result, HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        if (fingerprint.Length != 32 || structuralDigest.Length != 32 || receiptWire is null)
            throw new InvalidOperationException("SQLite receipt state is malformed.");
        return new SqliteMutationReceipt(fingerprint, structuralDigest, receiptWire.Materialize());
    }

    private async ValueTask InsertReceiptAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BaseAtomicMutationExecutionRequest request,
        AtomicMutationProcessingResult processing,
        CancellationToken cancellationToken)
    {
        byte[] result = JsonSerializer.SerializeToUtf8Bytes(BaseAtomicReceiptWire.From(processing.Receipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        if (result.Length > request.MaxReceiptBytes)
            throw new BaseReceiptTooLargeException();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"INSERT INTO {_names.OperationReceipts}(scope,operation,idempotency_key,fingerprint,structural_digest,result_json,result_format_version,schema_generation,store_instance_id,committed_at,expires_at) VALUES($scope,$operation,$key,$fingerprint,$structure,$result,2,$generation,$store,$committed,$expires);";
        command.Parameters.AddWithValue("$scope", request.Identity.Scope);
        command.Parameters.AddWithValue("$operation", request.Identity.Operation);
        command.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey);
        command.Parameters.AddWithValue("$fingerprint", request.Identity.Fingerprint.ToArray());
        command.Parameters.AddWithValue("$structure", request.StructuralDigest);
        command.Parameters.AddWithValue("$result", result);
        command.Parameters.AddWithValue("$generation", Volatile.Read(ref _schemaGeneration));
        command.Parameters.AddWithValue("$store", _options.StoreId);
        command.Parameters.AddWithValue("$committed", _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$expires", request.ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record SqliteMutationReceipt(byte[] Fingerprint, byte[] StructuralDigest, BaseAtomicReceiptResult Result);
    private sealed class BaseReceiptTooLargeException : Exception;

    private static void ValidateExecutionTimeout(TimeSpan timeout, string name)
    {
        if (timeout < MinimumExecutionTimeout
            || timeout.Ticks % MinimumExecutionTimeout.Ticks != 0)
        {
            throw new ArgumentOutOfRangeException(
                "request",
                $"{name} must be at least one second and use whole-second granularity for SQLite.");
        }
    }

    private async ValueTask<RecordMutationExecutionResult> RollbackAsync(
        TransactionResources resources,
        SqliteTransaction transaction,
        RecordMutationExecutionOutcome confirmedOutcome,
        AtomicMutationProcessingResult processing,
        TimeSpan completionTimeout)
    {
        using var rollbackLifetime = new CancellationTokenSource(completionTimeout);
        var rollbackTask = InvokeTransactionOperationAsync(
            () => _transactions.RollbackAsync(transaction, rollbackLifetime.Token));
        try
        {
            await rollbackTask.WaitAsync(rollbackLifetime.Token).ConfigureAwait(false);
            return new RecordMutationExecutionResult(confirmedOutcome, processing, processing.Error);
        }
        catch (OperationCanceledException) when (!rollbackTask.IsCompleted)
        {
            resources.TransferTo(rollbackTask);
            return Indeterminate();
        }
        catch
        {
            return Indeterminate();
        }
    }

    private static Task InvokeTransactionOperationAsync(Func<ValueTask> operation) =>
        Task.Run(
            async () => await operation().ConfigureAwait(false),
            CancellationToken.None);

    private static async ValueTask<bool> CloseSessionAsync(
        SqliteAtomicRecordSession session,
        TransactionResources resources,
        TimeSpan completionTimeout)
    {
        var closeTask = Task.Run(
            async () => await session.CloseAsync().ConfigureAwait(false),
            CancellationToken.None);
        try
        {
            await closeTask.WaitAsync(completionTimeout).ConfigureAwait(false);
            return true;
        }
        catch
        {
            resources.TransferTo(closeTask);
            return false;
        }
    }

    private sealed class TransactionResources(
        SqliteRecordStore owner,
        SqliteConnection connection,
        SqliteTransaction transaction,
        MutationExecutionSlot executionSlot,
        IAsyncDisposable generationLease,
        string? requestIdentity)
    {
        private bool _transferred;

        /// <summary>Executes the transfer to operation.</summary>
        public void TransferTo(Task operation)
        {
            _transferred = true;
            owner.TrackQuarantinedMutation(DisposeAfterCompletionAsync(operation), this, requestIdentity);
        }

        /// <summary>Executes the dispose if owned async operation.</summary>
        public async ValueTask DisposeIfOwnedAsync(TimeSpan completionTimeout)
        {
            if (_transferred)
                return;

            var cleanup = Task.Run(DisposeCoreAsync, CancellationToken.None);
            try
            {
                var disposed = await cleanup
                    .WaitAsync(completionTimeout)
                    .ConfigureAwait(false);
                if (!disposed)
                    owner.TrackQuarantinedMutation(cleanup, this, requestIdentity);
            }
            catch
            {
                if (!cleanup.IsCompleted)
                    owner.TrackQuarantinedMutation(cleanup, this, requestIdentity);
                else
                    ObserveCompletion(cleanup);
            }
        }

        private static async Task IgnoreFailureAsync(Task operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch
            {
                // The public result is already indeterminate. Never expose provider details.
            }
        }

        private async Task<bool> DisposeAfterCompletionAsync(Task operation)
        {
            await IgnoreFailureAsync(operation).ConfigureAwait(false);
            return await DisposeCoreAsync().ConfigureAwait(false);
        }

        private async Task<bool> DisposeCoreAsync()
        {
            try
            {
                await owner._transactionResourceDisposer
                    .DisposeAsync(transaction, connection)
                    .ConfigureAwait(false);
                executionSlot.Dispose();
                await generationLease.DisposeAsync().ConfigureAwait(false);
                return true;
            }
            catch
            {
                // Unconfirmed cleanup remains quarantined and retains its capacity slot.
                return false;
            }
        }
    }

    private sealed class MutationExecutionSlot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        /// <summary>Executes the dispose operation.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                slots.Release();
        }
    }

    private static RecordMutationExecutionResult CancelledBeforeCommit()
    {
        var error = ProviderError(
            BaseMutationErrorCodes.TransactionTimeout,
            "SQLite mutation execution was cancelled before commit.");
        return new RecordMutationExecutionResult(
            RecordMutationExecutionOutcome.CancelledRollbackConfirmed,
            FailedProcessing(error),
            error);
    }

    private static RecordMutationExecutionResult FailedBeforeCommit(BaseError error) =>
        new(
            RecordMutationExecutionOutcome.RollbackConfirmed,
            FailedProcessing(error),
            error);

    private static RecordMutationExecutionResult ConflictBeforeCommit()
    {
        var error = TransactionConflict();
        return new RecordMutationExecutionResult(
            RecordMutationExecutionOutcome.ConflictRollbackConfirmed,
            FailedProcessing(error),
            error);
    }

    private static RecordMutationExecutionResult Indeterminate() =>
        new(
            RecordMutationExecutionOutcome.Indeterminate,
            processing: null,
            ProviderError(
                BaseMutationErrorCodes.BatchIndeterminate,
                "SQLite could not determine the mutation transaction outcome."));

    private static AtomicMutationProcessingResult FailedProcessing(
        string code,
        string message,
        BaseRecordMutationFact[]? mutations = null) =>
        FailedProcessing(ProviderError(code, message), mutations);

    private static AtomicMutationProcessingResult FailedProcessing(
        BaseError error,
        BaseRecordMutationFact[]? mutations = null) =>
        new(AtomicMutationProcessingOutcome.Failed, mutations ?? [], error);

    private static bool IsTransactionConflict(SqliteException exception) =>
        exception.SqliteErrorCode is 5 or 6;

    private int TransactionCommandTimeoutSeconds(
        long transactionStarted,
        TimeSpan transactionTimeout)
    {
        var remaining = transactionTimeout - Stopwatch.GetElapsedTime(transactionStarted);
        if (remaining <= TimeSpan.Zero)
            throw new OperationCanceledException();

        return TimeoutSeconds(remaining);
    }

    private static BaseError TransactionConflict() => new()
    {
        Code = BaseMutationErrorCodes.TransactionConflict,
        Message = "SQLite could not acquire or retain the transaction write boundary.",
        Category = ErrorCategory.Conflict,
        Conflict = new ConflictInfo { Kind = ConflictKind.Transaction }
    };

    private static BaseError ProviderError(string code, string message) => new()
    {
        Code = code,
        Message = message,
        Category = ErrorCategory.Store,
        Store = new StoreErrorInfo { Retryable = false }
    };

    private static void ObserveCompletion(Task task) =>
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static string? QuarantineRequestIdentity(BaseMutationRequestIdentity? identity)
    {
        if (identity is null) return null;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(string.Join(
            '\0', identity.Scope, identity.Operation, identity.IdempotencyKey));
        try { return Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private sealed class SqliteAtomicRecordSession : IAtomicRecordSession
    {
        private int _relationChecks;
        private int _uniqueChecks;
        private SqlitePhysicalModel.CollectionModel? _constraintCollection;
        private RecordPayload? _constraintPayload;
        private string? _constraintRecordId;
        private int _selectionUniqueCheckLimit;
        private long _selectionTransientLimit;
        private long _selectionRetainedBytes;
        private long _attributionTransientBytes;
        private BaseCapturedAtomicExecution? _capturedMutation;
        private BasePreparedAtomicExecution? _preparedMutation;
        private BaseFinalizedAtomicExecutionPlan? _preparedPlan;
        private BaseProvisionalAtomicExecution? _appliedProvisional;
        private Dictionary<int, BaseSubjectIncarnation>? _preparedLifecycleIncarnations;
        private Dictionary<int, SqliteModuleGenerationKey>? _capturedModuleGenerationKeys;
        private BaseModuleMutationCaptureExtension? _capturedModuleExtension;
        private BaseActivationCreationExtension? _capturedActivationExtension;
        private BaseCapturedActivationGuardEvidence? _capturedActivationGuard;
        private sealed record SqliteModuleGenerationKey(
            string CellId,
            int CellVersion,
            int ScopeKind,
            string Tenant,
            string Project,
            byte[] KeyBytes);
        public async ValueTask<OperationResult<BaseSelectionMutationCommitAccounting>> MeasureSelectionMutationAsync(
            BaseAtomicReceiptResult receipt, BaseSelectionMutationResult result, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long written = 0, facts = 0, journal = 0;
            foreach (BaseOwnedMutationFact owned in receipt.Mutations)
            {
                BaseRecordMutationFact fact = owned.MaterializeOwned();
                facts = checked(facts + owned.EncodedLength);
                if (fact.After is { } after)
                {
                    SqlitePhysicalModel.CollectionModel physical = _owner._physical.Collection(fact.Collection.Id);
                    await using SqliteCommand stored = _connection.CreateCommand();
                    stored.Transaction = _transaction;
                    string payloadBytes = string.Join(string.Empty, physical.PayloadColumns.Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(static column => $"+COALESCE(length(CAST({column} AS BLOB)),0)"));
                    stored.CommandText = $"SELECT length(CAST(record_id AS BLOB))+8+length(CAST(created_at AS BLOB))+length(CAST(updated_at AS BLOB)){payloadBytes} FROM {physical.Table} WHERE record_id=$id;";
                    stored.Parameters.AddWithValue("$id", after.Id.Value);
                    written = checked(written + Convert.ToInt64(await stored.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture));
                }
                else written = checked(written + System.Text.Encoding.UTF8.GetByteCount(fact.Before!.Id.Value) + sizeof(long));

                await using SqliteCommand persistedJournal = _connection.CreateCommand();
                persistedJournal.Transaction = _transaction;
                persistedJournal.CommandText = $"SELECT length(CAST(event_id AS BLOB))+length(CAST(event_type AS BLOB))+length(CAST(schema_version AS BLOB))+length(CAST(occurred_at AS BLOB))+COALESCE(length(CAST(tenant_id AS BLOB)),0)+8+8+length(CAST(collection_id AS BLOB))+length(CAST(record_id AS BLOB))+COALESCE(length(CAST(before_json AS BLOB)),0)+COALESCE(length(CAST(after_json AS BLOB)),0) FROM {_owner._names.MutationJournal} WHERE position=$position;";
                persistedJournal.Parameters.AddWithValue("$position", fact.JournalPosition.Value);
                journal = checked(journal + Convert.ToInt64(await persistedJournal.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture));
            }
            long receiptBytes = JsonSerializer.SerializeToUtf8Bytes(BaseAtomicReceiptWire.From(receipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire).LongLength;
            long resultBytes = JsonSerializer.SerializeToUtf8Bytes(result, HPDBaseJsonSerializerContext.Default.BaseSelectionMutationResult).LongLength;
            return OperationResults.Ok(new BaseSelectionMutationCommitAccounting
            {
                WrittenBytes = written, FactBytes = facts, JournalBytes = journal, ReceiptBytes = receiptBytes,
                RelationChecks = _relationChecks, UniqueConstraintChecks = _uniqueChecks, ResultBytes = resultBytes,
                TransientBytes = checked(_selectionRetainedBytes + _attributionTransientBytes + written + facts + journal + receiptBytes + resultBytes),
            });
        }

        private const int Active = 0;
        private const int Closing = 1;
        private const int Closed = 2;

        private readonly SqliteRecordStore _owner;
        private readonly SqliteConnection _connection;
        private readonly SqliteTransaction _transaction;
        private readonly SemaphoreSlim _operationGate = new(1, 1);
        private readonly long _transactionStarted;
        private readonly TimeSpan _transactionTimeout;
        private int _lifetimeState;

        /// <summary>Initializes a new instance.</summary>
        public SqliteAtomicRecordSession(
            SqliteRecordStore owner,
            SqliteConnection connection,
            SqliteTransaction transaction,
            long transactionStarted,
            TimeSpan transactionTimeout)
        {
            _owner = owner;
            _connection = connection;
            _transaction = transaction;
            _transactionStarted = transactionStarted;
            _transactionTimeout = transactionTimeout;
        }

        public ValueTask<OperationResult<BaseCapturedAtomicExecution>> CaptureAtomicExecutionAsync(
            BaseAtomicExecutionRequest request,
            CancellationToken cancellationToken = default) => ExecuteAsync(BaseOperationKind.Query, cancellationToken, async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            BaseAtomicMutationIntent intent = request.Intent;
            BaseAtomicMutationExecutionLimits limits = request.Limits;
            if (request.Kind == BaseAtomicMutationExecutionKind.ActivationCreation)
                return await CaptureActivationAuthorityAsync(request, token).ConfigureAwait(false);
            if (request.ActivationGuard is not null)
            {
                OperationResult<BaseCapturedActivationGuardEvidence> guarded = await CaptureActivationGuardAsync(request.ActivationGuard, token).ConfigureAwait(false);
                if (!guarded.IsSuccess() || guarded.Value is null)
                    return new OperationResult<BaseCapturedAtomicExecution> { Status = guarded.Status, Error = guarded.Error };
                _capturedActivationGuard = guarded.Value;
            }
            if (request.Kind == BaseAtomicMutationExecutionKind.SelectionMutation)
            {
                if (request.Selection is null || request.Module is not null || !intent.Items.IsDefaultOrEmpty)
                    return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                OperationResult<BaseCapturedAtomicExecution> selected = await SelectCoreAsync(
                    request.Selection.Selection, intent, limits, token).ConfigureAwait(false);
                return selected;
            }
            if (request.Kind == BaseAtomicMutationExecutionKind.ModuleMutation)
                return await CaptureModuleAuthorityAsync(request, token).ConfigureAwait(false);
            if (_capturedMutation is not null || request.Kind != BaseAtomicMutationExecutionKind.RecordMutations
                || request.Selection is not null || request.Module is not null
                || intent.Items.IsDefaultOrEmpty || intent.Items.Length > limits.MaximumItems)
                return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
            (string storeId, long restoreEpoch, long schemaGeneration) = await ReadAuthorityAsync(token).ConfigureAwait(false);
            if (!string.Equals(storeId, intent.Authority.StoreInstanceId, StringComparison.Ordinal) || restoreEpoch != intent.Authority.RestoreEpoch || schemaGeneration != intent.Authority.SchemaGeneration)
                return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);
            foreach (BaseCollectionGenerationRequirement requirement in intent.Authority.Collections)
            {
                await using SqliteCommand generation = _connection.CreateCommand();
                generation.Transaction = _transaction;
                generation.CommandText = $"SELECT purge_generation FROM {_owner._names.Collections} WHERE collection_id=$collection;";
                generation.Parameters.AddWithValue("$collection", requirement.CollectionId);
                object? actual = await generation.ExecuteScalarAsync(token).ConfigureAwait(false);
                if (actual is null or DBNull || Convert.ToInt64(actual, CultureInfo.InvariantCulture) != requirement.CollectionGeneration)
                    return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);
            }
            var items = ImmutableArray.CreateBuilder<BaseCapturedMutationItem>(intent.Items.Length);
            var intervals = ImmutableArray.CreateBuilder<BaseAtomicReadIntervalEvidence>(intent.Items.Length);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            digest.AppendData(System.Text.Encoding.UTF8.GetBytes(intent.IntentDigest));
            long selectedBytes = 0;
            var transactionRecords = new Dictionary<string, RecordEnvelope?>(StringComparer.Ordinal);
            for (int index = 0; index < intent.Items.Length; index++)
            {
                BaseAtomicMutationIntentItem item = intent.Items[index];
                if (item.Ordinal != index) return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                string itemKey = CaptureRecordKey(item.Collection.Id, item.RecordId);
                if (!transactionRecords.TryGetValue(itemKey, out RecordEnvelope? current))
                {
                    current = await _owner.ReadAsync(_connection, item.Collection.Id, item.RecordId.Value, token, _transaction, CommandTimeoutSeconds()).ConfigureAwait(false);
                    transactionRecords[itemKey] = current;
                }
                if (current is not null)
                {
                    byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(current, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                    selectedBytes = checked(selectedBytes + encoded.LongLength); digest.AppendData(encoded);
                }
                byte[] key = System.Text.Encoding.UTF8.GetBytes(item.RecordId.Value); digest.AppendData(key);
                var relationTargets = ImmutableArray.CreateBuilder<BaseCapturedRelationTarget>(item.RelationTargets.Length);
                foreach (BaseAtomicRelationTargetIntent relation in item.RelationTargets)
                {
                    string relationRecordKey = CaptureRecordKey(relation.TargetCollection.Id, relation.TargetRecordId);
                    if (!transactionRecords.TryGetValue(relationRecordKey, out RecordEnvelope? target))
                    {
                        target = await _owner.ReadAsync(_connection, relation.TargetCollection.Id,
                            relation.TargetRecordId.Value, token, _transaction, CommandTimeoutSeconds()).ConfigureAwait(false);
                        transactionRecords[relationRecordKey] = target;
                    }
                    if (target is not null)
                    {
                        byte[] targetBytes = JsonSerializer.SerializeToUtf8Bytes(target, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                        selectedBytes = checked(selectedBytes + targetBytes.LongLength);
                        digest.AppendData(targetBytes);
                    }
                    byte[] relationKey = System.Text.Encoding.UTF8.GetBytes(relation.TargetRecordId.Value);
                    intervals.Add(new BaseAtomicReadIntervalEvidence
                    {
                        LogicalAccessPathId = $"collection:{relation.TargetCollection.Id}:record",
                        CanonicalLowerBound = relationKey.ToImmutableArray(), LowerInclusive = true,
                        CanonicalUpperBound = relationKey.ToImmutableArray(), UpperInclusive = true,
                    });
                    relationTargets.Add(new BaseCapturedRelationTarget
                    {
                        SourceFieldId = new string(relation.SourceFieldId.AsSpan()),
                        TargetCollectionId = new string(relation.TargetCollection.Id.AsSpan()),
                        TargetRecordId = relation.TargetRecordId,
                        Current = target,
                    });
                }
                items.Add(new BaseCapturedMutationItem
                {
                    Ordinal = index, CollectionId = item.Collection.Id, RecordId = item.RecordId,
                    RuntimeAssignedRecordId = item.RuntimeAssignedRecordId,
                    Disposition = item.RequestedKind switch
                    {
                        BaseRecordMutationKind.Create => current is null
                            ? BaseCapturedMutationDisposition.Create
                            : BaseCapturedMutationDisposition.Update,
                        BaseRecordMutationKind.Upsert => current is null ? BaseCapturedMutationDisposition.Create : BaseCapturedMutationDisposition.Update,
                        BaseRecordMutationKind.Delete => BaseCapturedMutationDisposition.Delete,
                        _ => BaseCapturedMutationDisposition.Update,
                    },
                    Current = current,
                    RelationTargets = relationTargets.MoveToImmutable(),
                });
                intervals.Add(new BaseAtomicReadIntervalEvidence
                {
                    LogicalAccessPathId = $"collection:{item.Collection.Id}:record", CanonicalLowerBound = key.ToImmutableArray(),
                    LowerInclusive = true, CanonicalUpperBound = key.ToImmutableArray(), UpperInclusive = true,
                });
                transactionRecords[itemKey] = SimulateIntentRecord(item, current);
            }
            long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals.ToImmutable());
            ImmutableArray<BaseCapturedMutationItem> ownedItems = items.ToImmutable();
            ImmutableArray<BaseAtomicReadIntervalEvidence> ownedIntervals = intervals.ToImmutable();
            long transient = BaseSubjectCanonicalRetainedWork.MeasureCapture(intent, ownedItems, ownedIntervals);
            if (selectedBytes > limits.MaximumSelectedBytes || evidenceBytes > limits.MaximumEvidenceBytes || transient > limits.MaximumTransientBytes || intervals.Count > limits.MaximumReadIntervals)
                return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);
            _capturedMutation = new BaseCapturedAtomicExecution
            {
                Kind = request.Kind,
                IntentDigest = new string(intent.IntentDigest.AsSpan()), CaptureDigest = Convert.ToHexStringLower(digest.GetHashAndReset()),
                Authority = new BaseAtomicMutationAuthorityEvidence
                {
                    ApplicationId = intent.Authority.ApplicationId, StoreInstanceId = storeId, RestoreEpoch = restoreEpoch,
                    SchemaGeneration = schemaGeneration,
                    Collections = intent.Authority.Collections.Select(static value => value with { }).ToImmutableArray(),
                    Isolation = BaseAtomicSelectionIsolationClass.WriteOwningSerializable,
                    TransactionEvidenceToken = BitConverter.GetBytes(_transactionStarted).ToImmutableArray(),
                },
                Items = ownedItems, ModuleRecords = [], ModuleRelationTargets = [], Generations = [],
                ActivationGuard = _capturedActivationGuard, ReadIntervals = ownedIntervals,
                Accounting = new BaseAtomicCaptureAccounting
                {
                    Records = checked(intent.Items.Length + intent.Items.Sum(static item => item.RelationTargets.Length)),
                    RelationTargetReads = intent.Items.Sum(static item => item.RelationTargets.Length), GenerationReads = 0,
                    SelectedBytes = selectedBytes, ReadIntervals = intervals.Count,
                    RelationTargetBytes = 0, GenerationBytes = 0,
                    EvidenceBytes = evidenceBytes, TransientBytes = transient,
                },
            };
            return OperationResults.Ok(_capturedMutation);
        });

        private async ValueTask<OperationResult<BaseCapturedActivationGuardEvidence>> CaptureActivationGuardAsync(
            BaseActivationGuard guard,
            CancellationToken cancellationToken)
        {
            BaseActivationClaimAuthority claim = guard.Claim;
            (string storeId, long restoreEpoch, _) = await ReadAuthorityAsync(cancellationToken).ConfigureAwait(false);
            if (guard.ChildOrdinal <= 0 || guard.ChildRequestFingerprint.Length != 32 ||
                !string.Equals(storeId, claim.StoreInstanceId, StringComparison.Ordinal) || restoreEpoch != claim.RestoreEpoch)
                return SubjectFailure<BaseCapturedActivationGuardEvidence>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            await using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = $"SELECT generation,definition_checksum,attempt_number,claim_epoch,claim_fence,claim_worker,lease_revision,lease_expires_at FROM {_owner._names.Activations} WHERE activation_id=$id AND state=$state;";
            command.Parameters.AddWithValue("$id", claim.ActivationId);
            command.Parameters.AddWithValue("$state", (int)BaseActivationState.Claimed);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(4) || reader.IsDBNull(5) || reader.IsDBNull(6) || reader.IsDBNull(7) ||
                reader.GetInt32(2) != claim.AttemptNumber || reader.GetInt64(3) != claim.ClaimEpoch ||
                !string.Equals(reader.GetString(5), claim.WorkerIdentity, StringComparison.Ordinal) ||
                reader.GetInt64(7) <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ||
                !CryptographicOperations.FixedTimeEquals((byte[])reader[4], claim.FencingToken.AsSpan()) ||
                !CryptographicOperations.FixedTimeEquals((byte[])reader[1], claim.DefinitionChecksum.AsSpan()))
                return SubjectFailure<BaseCapturedActivationGuardEvidence>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            long generation = reader.GetInt64(0), leaseRevision = reader.GetInt64(6), leaseExpiresAt = reader.GetInt64(7);
            byte[] checksum = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"base.activation.guard.v2\0{claim.ActivationId}\n{generation}\n{leaseRevision}\n{guard.StepId}\n{guard.ChildOrdinal}\n{Convert.ToHexString(guard.ChildRequestFingerprint.AsSpan())}"));
            return OperationResults.Ok(new BaseCapturedActivationGuardEvidence
            {
                ActivationId = new string(claim.ActivationId.AsSpan()), Generation = generation,
                LeaseRevision = leaseRevision, LeaseExpiresAt = leaseExpiresAt, Checksum = checksum.ToImmutableArray(),
            });
        }

        private static bool ActivationGuardMatches(BaseActivationGuard? guard, BaseCapturedActivationGuardEvidence? evidence) =>
            guard is null ? evidence is null : evidence is not null &&
            string.Equals(guard.Claim.ActivationId, evidence.ActivationId, StringComparison.Ordinal) &&
            guard.Claim.FencingToken.Length == 32 && guard.ChildRequestFingerprint.Length == 32;

        private async ValueTask<OperationResult<BaseCapturedAtomicExecution>> CaptureActivationAuthorityAsync(
            BaseAtomicExecutionRequest request,
            CancellationToken cancellationToken)
        {
            BaseAtomicMutationIntent intent = request.Intent;
            BaseActivationCreationExtension? extension = request.Activations;
            if (_capturedMutation is not null || extension is null || request.Selection is not null
                || request.Module is not null || request.ActivationGuard is not null
                || !intent.Items.IsDefaultOrEmpty || extension.Items.IsDefaultOrEmpty
                || extension.Items.Length > request.Limits.MaximumItems || extension.StructuralDigest.Length != 32
                || !intent.Authority.Collections.IsDefaultOrEmpty)
                return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);

            (string storeId, long restoreEpoch, long schemaGeneration) = await ReadAuthorityAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(storeId, intent.Authority.StoreInstanceId, StringComparison.Ordinal)
                || restoreEpoch != intent.Authority.RestoreEpoch || schemaGeneration != intent.Authority.SchemaGeneration)
                return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);

            var items = ImmutableArray.CreateBuilder<BaseCapturedActivationItem>(extension.Items.Length);
            var intervals = ImmutableArray.CreateBuilder<BaseAtomicReadIntervalEvidence>(extension.Items.Length);
            using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            aggregate.AppendData(extension.StructuralDigest.AsSpan());
            long selectedBytes = 0;
            for (int ordinal = 0; ordinal < extension.Items.Length; ordinal++)
            {
                BaseActivationCreateIntent item = extension.Items[ordinal];
                if (item.Ordinal != ordinal || item.Definition.Version < 1 || item.Definition.Checksum.Length != 32
                    || item.InputChecksum.Length != 32 || item.CanonicalInput.IsDefault
                    || item.RequestedDueAt < 0 || item.EffectiveDueAt is < 0)
                    return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                byte[] idBytes = SHA256.HashData(extension.StructuralDigest
                    .Concat(BitConverter.GetBytes(ordinal).Reverse()).ToArray());
                string activationId = Convert.ToHexStringLower(idBytes);
                byte[] fingerprint = ActivationFingerprint(item);
                byte[]? existingFingerprint;
                await using (SqliteCommand command = _connection.CreateCommand())
                {
                    command.Transaction = _transaction;
                    command.CommandText = $"SELECT fingerprint FROM {_owner._names.Activations} WHERE activation_id=$id;";
                    command.Parameters.AddWithValue("$id", activationId);
                    object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    existingFingerprint = value is byte[] bytes ? bytes : null;
                }
                if (existingFingerprint is not null
                    && !CryptographicOperations.FixedTimeEquals(existingFingerprint, fingerprint))
                    return SubjectFailure<BaseCapturedAtomicExecution>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
                byte[] key = Encoding.UTF8.GetBytes(activationId);
                intervals.Add(ExactInterval("base.activation.byId", key));
                aggregate.AppendData(key); aggregate.AppendData(fingerprint);
                selectedBytes = checked(selectedBytes + item.CanonicalInput.Length + fingerprint.Length);
                items.Add(new BaseCapturedActivationItem
                {
                    Ordinal = ordinal, ActivationId = activationId, Exists = existingFingerprint is not null,
                    ExistingFingerprint = existingFingerprint?.ToImmutableArray() ?? [],
                });
            }
            ImmutableArray<BaseAtomicReadIntervalEvidence> ownedIntervals = intervals.MoveToImmutable();
            long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(ownedIntervals);
            long transient = checked(selectedBytes + evidenceBytes);
            if (selectedBytes > request.Limits.MaximumSelectedBytes || evidenceBytes > request.Limits.MaximumEvidenceBytes
                || transient > request.Limits.MaximumTransientBytes || ownedIntervals.Length > request.Limits.MaximumReadIntervals)
                return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);
            BaseCapturedActivationExtension capturedActivations = new()
            {
                Items = items.MoveToImmutable(), ReadIntervals = ownedIntervals,
                Checksum = aggregate.GetHashAndReset().ToImmutableArray(),
            };
            _capturedActivationExtension = FreezeActivationExtension(extension);
            _capturedMutation = new BaseCapturedAtomicExecution
            {
                Kind = request.Kind, IntentDigest = new string(intent.IntentDigest.AsSpan()),
                CaptureDigest = Convert.ToHexStringLower(capturedActivations.Checksum.AsSpan()),
                Authority = new BaseAtomicMutationAuthorityEvidence
                {
                    ApplicationId = intent.Authority.ApplicationId, StoreInstanceId = storeId,
                    RestoreEpoch = restoreEpoch, SchemaGeneration = schemaGeneration, Collections = [],
                    Isolation = BaseAtomicSelectionIsolationClass.WriteOwningSerializable,
                    TransactionEvidenceToken = BitConverter.GetBytes(_transactionStarted).ToImmutableArray(),
                },
                Items = [], ModuleRecords = [], ModuleRelationTargets = [], Generations = [],
                Activations = capturedActivations, ActivationGuard = null, ReadIntervals = ownedIntervals,
                Accounting = new BaseAtomicCaptureAccounting
                {
                    Records = 0, RelationTargetReads = 0, GenerationReads = 0,
                    SelectedBytes = selectedBytes, RelationTargetBytes = 0, GenerationBytes = 0,
                    ReadIntervals = ownedIntervals.Length, EvidenceBytes = evidenceBytes, TransientBytes = transient,
                },
            };
            return OperationResults.Ok(_capturedMutation);
        }

        private async ValueTask<OperationResult<BaseCapturedAtomicExecution>> CaptureModuleAuthorityAsync(
            BaseAtomicExecutionRequest request,
            CancellationToken cancellationToken)
        {
            BaseAtomicMutationIntent intent = request.Intent;
            BaseModuleMutationCaptureExtension? module = request.Module;
            BaseAtomicMutationExecutionLimits limits = request.Limits;
            if (_capturedMutation is not null || module is null || request.Selection is not null || !intent.Items.IsDefaultOrEmpty
                || module.Records.Length > limits.MaximumRecordCaptures
                || module.RelationTargets.Length > limits.MaximumRelationTargetCaptures
                || module.Generations.Length > limits.MaximumGenerationReads)
                return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
            _capturedModuleExtension = module;

            (string storeId, long restoreEpoch, long schemaGeneration) = await ReadAuthorityAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(storeId, intent.Authority.StoreInstanceId, StringComparison.Ordinal)
                || restoreEpoch != intent.Authority.RestoreEpoch || schemaGeneration != intent.Authority.SchemaGeneration
                || !await CollectionAuthorityMatchesAsync(intent.Authority.Collections, module, cancellationToken).ConfigureAwait(false))
                return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);

            var records = ImmutableArray.CreateBuilder<BaseCapturedModuleRecord>(module.Records.Length);
            var relations = ImmutableArray.CreateBuilder<BaseCapturedModuleRelationTarget>(module.RelationTargets.Length);
            var generations = ImmutableArray.CreateBuilder<BaseCapturedModuleGeneration>(module.Generations.Length);
            var intervals = ImmutableArray.CreateBuilder<BaseAtomicReadIntervalEvidence>(
                checked(module.Records.Length + module.RelationTargets.Length + module.Generations.Length));
            var keys = new Dictionary<int, SqliteModuleGenerationKey>();
            long selectedBytes = 0, relationBytes = 0, generationBytes = 0;
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            digest.AppendData(System.Text.Encoding.UTF8.GetBytes(intent.IntentDigest));

            for (int index = 0; index < module.Records.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BaseModuleRecordCaptureRequest capture = module.Records[index];
                if (capture.Ordinal != index)
                    return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                RecordEnvelope? current = await _owner.ReadAsync(
                    _connection, capture.Collection.Id, capture.RecordId.Value, cancellationToken, _transaction, CommandTimeoutSeconds()).ConfigureAwait(false);
                if ((capture.Presence == BaseModuleCapturePresence.RequirePresent && current is null)
                    || (capture.Presence == BaseModuleCapturePresence.RequireMissing && current is not null))
                    return SubjectFailure<BaseCapturedAtomicExecution>("base.moduleMutation.captureConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
                byte[] key = System.Text.Encoding.UTF8.GetBytes(capture.RecordId.Value);
                intervals.Add(ExactInterval($"collection:{capture.Collection.Id}:record", key));
                if (current is not null)
                {
                    byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(current, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                    selectedBytes = checked(selectedBytes + encoded.LongLength); digest.AppendData(encoded);
                }
                records.Add(new BaseCapturedModuleRecord
                {
                    Ordinal = index, CaptureId = new string(capture.CaptureId.AsSpan()), CollectionId = new string(capture.Collection.Id.AsSpan()),
                    RecordId = capture.RecordId, Exists = current is not null,
                    Current = current is null ? null : RecordCloneHelpers.CloneEnvelope(current),
                });
            }

            for (int index = 0; index < module.RelationTargets.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BaseModuleRelationTargetCaptureRequest capture = module.RelationTargets[index];
                if (capture.Ordinal != index)
                    return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                RecordEnvelope? current = await _owner.ReadAsync(
                    _connection, capture.TargetCollection.Id, capture.TargetRecordId.Value, cancellationToken, _transaction, CommandTimeoutSeconds()).ConfigureAwait(false);
                byte[] key = System.Text.Encoding.UTF8.GetBytes(capture.TargetRecordId.Value);
                intervals.Add(ExactInterval($"collection:{capture.TargetCollection.Id}:record", key));
                if (current is not null)
                {
                    byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(current, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                    relationBytes = checked(relationBytes + encoded.LongLength); digest.AppendData(encoded);
                }
                relations.Add(new BaseCapturedModuleRelationTarget
                {
                    Ordinal = index, SourceStatementId = new string(capture.SourceStatementId.AsSpan()),
                    SourceFieldId = new string(capture.SourceFieldId.AsSpan()), TargetCollectionId = new string(capture.TargetCollection.Id.AsSpan()),
                    TargetRecordId = capture.TargetRecordId,
                    Current = current is null ? null : RecordCloneHelpers.CloneEnvelope(current),
                });
            }

            for (int index = 0; index < module.Generations.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BaseModuleGenerationCaptureRequest capture = module.Generations[index];
                if (capture.Ordinal != index || !ValidGenerationScope(capture))
                    return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                SqliteModuleGenerationKey key = GenerationKey(capture);
                keys.Add(index, key);
                await using SqliteCommand command = _connection.CreateCommand();
                command.Transaction = _transaction;
                command.CommandText = $"SELECT generation FROM {_owner._names.ModuleGenerations} WHERE cell_id=$id AND cell_version=$version AND scope_kind=$scope AND tenant=$tenant AND project=$project AND key_bytes=$key;";
                AddGenerationKeyParameters(command, key);
                object? raw = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                bool exists = raw is not null and not DBNull;
                long value = exists ? Convert.ToInt64(raw, CultureInfo.InvariantCulture) : 0;
                if ((capture.Absence == BaseModuleGenerationAbsenceBehavior.RequireExisting && !exists)
                    || (capture.Absence == BaseModuleGenerationAbsenceBehavior.RequireMissing && exists))
                    return SubjectFailure<BaseCapturedAtomicExecution>("base.moduleMutation.generationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
                byte[] canonicalKey = CanonicalGenerationKeyBytes(key);
                string keyDigest = Convert.ToHexStringLower(SHA256.HashData(canonicalKey));
                intervals.Add(ExactInterval("module-generation", canonicalKey));
                generationBytes = checked(generationBytes + canonicalKey.LongLength + 1 + (exists ? 8 : 0));
                digest.AppendData(canonicalKey); if (exists) digest.AppendData(BitConverter.GetBytes(value));
                generations.Add(new BaseCapturedModuleGeneration
                {
                    Ordinal = index, CaptureId = new string(capture.CaptureId.AsSpan()), CellId = new string(capture.Cell.Id.AsSpan()),
                    CellVersion = capture.Cell.Version, CanonicalKeyDigest = keyDigest, Exists = exists,
                    Generation = exists ? BaseModuleGeneration.Create(value) : null,
                });
            }

            long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals);
            long transient = checked(selectedBytes + relationBytes + generationBytes + evidenceBytes);
            if (selectedBytes > limits.MaximumSelectedBytes || generationBytes > limits.MaximumGenerationBytes
                || evidenceBytes > limits.MaximumEvidenceBytes || transient > limits.MaximumTransientBytes
                || intervals.Count > limits.MaximumReadIntervals)
                return SubjectFailure<BaseCapturedAtomicExecution>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);

            int readIntervalCount = intervals.Count;
            _capturedMutation = new BaseCapturedAtomicExecution
            {
                Kind = request.Kind, IntentDigest = new string(intent.IntentDigest.AsSpan()),
                CaptureDigest = Convert.ToHexStringLower(digest.GetHashAndReset()),
                Authority = new BaseAtomicMutationAuthorityEvidence
                {
                    ApplicationId = intent.Authority.ApplicationId, StoreInstanceId = storeId, RestoreEpoch = restoreEpoch,
                    SchemaGeneration = schemaGeneration,
                    Collections = intent.Authority.Collections.Select(static value => value with { }).ToImmutableArray(),
                    Isolation = BaseAtomicSelectionIsolationClass.WriteOwningSerializable,
                    TransactionEvidenceToken = BitConverter.GetBytes(_transactionStarted).ToImmutableArray(),
                },
                Items = [], ModuleRecords = records.MoveToImmutable(), ModuleRelationTargets = relations.MoveToImmutable(),
                Generations = generations.MoveToImmutable(), ActivationGuard = _capturedActivationGuard,
                ReadIntervals = intervals.MoveToImmutable(),
                Accounting = new BaseAtomicCaptureAccounting
                {
                    Records = module.Records.Length, RelationTargetReads = module.RelationTargets.Length,
                    GenerationReads = module.Generations.Length, ReadIntervals = readIntervalCount,
                    SelectedBytes = selectedBytes, RelationTargetBytes = relationBytes, GenerationBytes = generationBytes,
                    EvidenceBytes = evidenceBytes, TransientBytes = transient,
                },
            };
            _capturedModuleGenerationKeys = keys;
            return OperationResults.Ok(_capturedMutation);
        }

        private async ValueTask<bool> CollectionAuthorityMatchesAsync(
            ImmutableArray<BaseCollectionGenerationRequirement> requirements,
            BaseModuleMutationCaptureExtension module,
            CancellationToken cancellationToken)
        {
            string[] expected = module.Records.Select(static value => value.Collection.Id)
                .Concat(module.RelationTargets.Select(static value => value.TargetCollection.Id))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (requirements.Length != expected.Length
                || !requirements.Select(static value => value.CollectionId).SequenceEqual(expected, StringComparer.Ordinal)) return false;
            foreach (BaseCollectionGenerationRequirement requirement in requirements)
            {
                await using SqliteCommand command = _connection.CreateCommand();
                command.Transaction = _transaction;
                command.CommandText = $"SELECT purge_generation FROM {_owner._names.Collections} WHERE collection_id=$collection;";
                command.Parameters.AddWithValue("$collection", requirement.CollectionId);
                object? raw = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (raw is null or DBNull || Convert.ToInt64(raw, CultureInfo.InvariantCulture) != requirement.CollectionGeneration) return false;
            }
            return true;
        }

        private static bool ValidGenerationScope(BaseModuleGenerationCaptureRequest capture)
        {
            bool keyed = capture.Cell.Scope is BaseModuleGenerationScope.TenantAndKey or BaseModuleGenerationScope.ProjectAndKey;
            if (capture.Cell.Scope != capture.Scope.Kind || capture.KeyUtf8.IsDefault
                || (keyed ? capture.KeyUtf8.IsDefaultOrEmpty || capture.KeyUtf8.Length > capture.Cell.MaximumKeyUtf8Bytes : !capture.KeyUtf8.IsEmpty)) return false;
            return capture.Scope.Kind switch
            {
                BaseModuleGenerationScope.Application => capture.Scope.Tenant is null && capture.Scope.Project is null,
                BaseModuleGenerationScope.Tenant or BaseModuleGenerationScope.TenantAndKey => !string.IsNullOrEmpty(capture.Scope.Tenant) && capture.Scope.Project is null,
                BaseModuleGenerationScope.Project or BaseModuleGenerationScope.ProjectAndKey => capture.Scope.Tenant is null && !string.IsNullOrEmpty(capture.Scope.Project),
                _ => false,
            };
        }

        private static bool ModuleBindingsValid(BaseFinalizedAtomicExecutionPlan plan, BaseCapturedAtomicExecution captured)
        {
            if (plan.Module is null || plan.Module.ItemBindings.Length != plan.Items.Length) return false;
            if (plan.Module.RelationTargets.Select(static value => value.CaptureOrdinal).Distinct().Count()
                != plan.Module.RelationTargets.Length) return false;
            foreach (BaseAuthorizedModuleRelationTarget authorized in plan.Module.RelationTargets)
            {
                if (authorized.CaptureOrdinal < 0 || authorized.CaptureOrdinal >= captured.ModuleRelationTargets.Length) return false;
                BaseCapturedModuleRelationTarget actual = captured.ModuleRelationTargets[authorized.CaptureOrdinal];
                if (actual.Ordinal != authorized.CaptureOrdinal
                    || !string.Equals(actual.SourceStatementId, authorized.SourceStatementId, StringComparison.Ordinal)
                    || !string.Equals(actual.SourceFieldId, authorized.SourceFieldId, StringComparison.Ordinal)
                    || !string.Equals(actual.TargetCollectionId, authorized.TargetCollectionId, StringComparison.Ordinal)
                    || actual.TargetRecordId != authorized.TargetRecordId) return false;
            }
            var overlay = new Dictionary<(string CollectionId, RecordId RecordId), RecordEnvelope?>();
            for (int ordinal = 0; ordinal < plan.Items.Length; ordinal++)
            {
                BaseModuleMutationItemCaptureBinding binding = plan.Module.ItemBindings[ordinal];
                if (binding.MutationOrdinal != ordinal || binding.RecordCaptureOrdinal < 0
                    || binding.RecordCaptureOrdinal >= captured.ModuleRecords.Length) return false;
                BaseAtomicMutationPlanItem item = plan.Items[ordinal];
                BaseCapturedModuleRecord capture = captured.ModuleRecords[binding.RecordCaptureOrdinal];
                if (!string.Equals(item.Collection.Id, capture.CollectionId, StringComparison.Ordinal) || item.RecordId != capture.RecordId) return false;
                var key = (item.Collection.Id, item.RecordId);
                RecordEnvelope? expected = overlay.TryGetValue(key, out RecordEnvelope? prior) ? prior : capture.Current;
                if (!SameEnvelope(item.Current, expected)) return false;
                overlay[key] = item.Kind == BaseCommittedRecordMutationKind.Delete ? null : new RecordEnvelope
                {
                    CollectionId = item.Collection.Id, Id = item.RecordId,
                    Payload = RecordCloneHelpers.ClonePayload(item.ProposedPayload!),
                    Metadata = item.Current?.Metadata ?? new RecordMetadata(),
                };
            }
            return true;
        }

        private static bool SameEnvelope(RecordEnvelope? left, RecordEnvelope? right) => left is null && right is null
            || left is not null && right is not null
            && JsonSerializer.SerializeToUtf8Bytes(left, HPDBaseJsonSerializerContext.Default.RecordEnvelope).AsSpan()
                .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, HPDBaseJsonSerializerContext.Default.RecordEnvelope));

        private static SqliteModuleGenerationKey GenerationKey(BaseModuleGenerationCaptureRequest capture) => new(
            new string(capture.Cell.Id.AsSpan()), capture.Cell.Version, (int)capture.Scope.Kind,
            capture.Scope.Tenant is null ? string.Empty : new string(capture.Scope.Tenant.AsSpan()),
            capture.Scope.Project is null ? string.Empty : new string(capture.Scope.Project.AsSpan()), capture.KeyUtf8.ToArray());

        private static void AddGenerationKeyParameters(SqliteCommand command, SqliteModuleGenerationKey key)
        {
            command.Parameters.AddWithValue("$id", key.CellId); command.Parameters.AddWithValue("$version", key.CellVersion);
            command.Parameters.AddWithValue("$scope", key.ScopeKind); command.Parameters.AddWithValue("$tenant", key.Tenant);
            command.Parameters.AddWithValue("$project", key.Project); command.Parameters.Add("$key", SqliteType.Blob).Value = key.KeyBytes;
        }

        private static byte[] CanonicalGenerationKeyBytes(SqliteModuleGenerationKey key) => System.Text.Encoding.UTF8.GetBytes(string.Join('\n',
            key.CellId, key.CellVersion.ToString(CultureInfo.InvariantCulture), key.ScopeKind.ToString(CultureInfo.InvariantCulture),
            key.Tenant, key.Project, Convert.ToHexStringLower(key.KeyBytes)));

        public ValueTask<OperationResult<BasePreparedAtomicExecution>> PrepareAtomicExecutionAsync(
            BaseCapturedAtomicExecution captured, BaseFinalizedAtomicExecutionPlan plan,
            CancellationToken cancellationToken = default) => ExecuteAsync(BaseOperationKind.Query, cancellationToken, async token =>
        {
            token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(captured); ArgumentNullException.ThrowIfNull(plan);
            if (!ReferenceEquals(captured, _capturedMutation) || _preparedMutation is not null || plan.Kind != captured.Kind ||
                !string.Equals(plan.IntentDigest, captured.IntentDigest, StringComparison.Ordinal) ||
                !string.Equals(plan.CaptureDigest, captured.CaptureDigest, StringComparison.Ordinal) ||
                (plan.Kind == BaseAtomicMutationExecutionKind.ModuleMutation
                    ? plan.Module is null || captured.Items.Length != 0
                    : plan.Items.Length != captured.Items.Length)
                || (plan.Kind == BaseAtomicMutationExecutionKind.ActivationCreation
                    && (plan.Activations is null || captured.Activations is null
                        || _capturedActivationExtension is null || !plan.Items.IsDefaultOrEmpty)))
                return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
            if (!ActivationGuardMatches(plan.ActivationGuard, captured.ActivationGuard))
                return SubjectFailure<BasePreparedAtomicExecution>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            var preparedGenerations = ImmutableArray.CreateBuilder<BasePreparedModuleGenerationEvidence>(captured.Generations.Length);
            BasePreparedActivationExtension? preparedActivations = null;
            if (plan.Kind == BaseAtomicMutationExecutionKind.ActivationCreation)
            {
                if (!ActivationExtensionsMatch(plan.Activations!, _capturedActivationExtension!, captured.Activations!))
                    return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                var activationItems = ImmutableArray.CreateBuilder<BasePreparedActivationItem>(captured.Activations!.Items.Length);
                using var activationDigest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                for (int ordinal = 0; ordinal < captured.Activations.Items.Length; ordinal++)
                {
                    BaseCapturedActivationItem capturedItem = captured.Activations.Items[ordinal];
                    BaseActivationCreateIntent intentItem = plan.Activations!.Items[ordinal];
                    byte[] payloadChecksum = SHA256.HashData(intentItem.CanonicalInput.AsSpan());
                    byte[] controlChecksum = SHA256.HashData(Encoding.UTF8.GetBytes(
                        $"{capturedItem.ActivationId}\n1\n{intentItem.EffectiveDueAt ?? intentItem.RequestedDueAt}"));
                    activationDigest.AppendData(payloadChecksum); activationDigest.AppendData(controlChecksum);
                    activationItems.Add(new BasePreparedActivationItem
                    {
                        Ordinal = ordinal, ActivationId = capturedItem.ActivationId, ResultingGeneration = 1,
                        PayloadChecksum = payloadChecksum.ToImmutableArray(), ControlChecksum = controlChecksum.ToImmutableArray(),
                    });
                }
                preparedActivations = new BasePreparedActivationExtension
                {
                    Items = activationItems.MoveToImmutable(),
                    Checksum = activationDigest.GetHashAndReset().ToImmutableArray(),
                };
            }
            if (plan.Kind == BaseAtomicMutationExecutionKind.ModuleMutation)
            {
                if (captured.Accounting.GenerationReads > plan.Limits.MaximumGenerationReads
                    || captured.Accounting.GenerationBytes > plan.Limits.MaximumGenerationBytes
                    || plan.Module!.Comparisons.Length > plan.Limits.MaximumGenerationComparisons
                    || plan.Module.Increments.Length > plan.Limits.MaximumGenerationIncrements)
                    return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);
                if (_capturedModuleGenerationKeys is null
                    || !ModuleBindingsValid(plan, captured)
                    || plan.Module!.Comparisons.Select(static value => value.CaptureOrdinal).Distinct().Count() != plan.Module.Comparisons.Length
                    || plan.Module.Increments.Select(static value => value.CaptureOrdinal).Distinct().Count() != plan.Module.Increments.Length)
                    return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                for (int ordinal = 0; ordinal < captured.Generations.Length; ordinal++)
                {
                    BaseCapturedModuleGeneration capture = captured.Generations[ordinal];
                    BaseModuleGenerationComparison? comparison = plan.Module.Comparisons.SingleOrDefault(value => value.CaptureOrdinal == ordinal);
                    BaseModuleGenerationIncrement? increment = plan.Module.Increments.SingleOrDefault(value => value.CaptureOrdinal == ordinal);
                    bool comparisonSatisfied = comparison is null || comparison.Kind switch
                    {
                        BaseModuleGenerationComparisonKind.MustExist => capture.Exists,
                        BaseModuleGenerationComparisonKind.MustBeMissing => !capture.Exists,
                        BaseModuleGenerationComparisonKind.MustEqual => capture.Exists && comparison.Expected is not null && capture.Generation!.Equals(comparison.Expected),
                        _ => false,
                    };
                    if (!comparisonSatisfied || (increment is not null && !capture.Exists && !increment.CreateIfAbsent))
                        return SubjectFailure<BasePreparedAtomicExecution>("base.moduleMutation.generationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
                    BaseModuleGeneration? resulting = increment is null ? capture.Generation
                        : capture.Generation is null ? BaseModuleGeneration.Create(1) : capture.Generation.Increment();
                    preparedGenerations.Add(new BasePreparedModuleGenerationEvidence
                    {
                        CaptureOrdinal = ordinal, CanonicalKeyDigest = new string(capture.CanonicalKeyDigest.AsSpan()),
                        Previous = capture.Generation, Resulting = resulting,
                        Disposition = increment is null
                            ? capture.Exists ? BaseModuleGenerationPreparationDisposition.Preserved : BaseModuleGenerationPreparationDisposition.RemainedAbsent
                            : capture.Exists ? BaseModuleGenerationPreparationDisposition.Incremented : BaseModuleGenerationPreparationDisposition.Created,
                    });
                }
            }
            var lifetimes = new Dictionary<string, SqlitePreparedSubjectLifetime?>(StringComparer.Ordinal);
            var overlays = new Dictionary<string, BasePreparedSubjectOverlayEvidence>(StringComparer.Ordinal);
            var lifecycleIncarnations = new Dictionary<int, BaseSubjectIncarnation>();
            var subjectAuthorities = new Dictionary<string, BaseSubjectTransactionAuthorityEvidence>(StringComparer.Ordinal);
            var intervals = captured.ReadIntervals.ToBuilder();
            int authorityReads = captured.Accounting.Records;
            long retainedBytes = checked(captured.Accounting.TransientBytes + CanonicalPlanRetainedBytes(plan));
            foreach (BaseAtomicMutationPlanItem item in plan.Items)
            {
                if (item.SubjectLifecycle is not { } lifecycle) continue;
                SqliteSubjectContractState? contract = await ReadSubjectContractAsync(lifecycle.ContractId, lifecycle.ContractVersion, token).ConfigureAwait(false);
                authorityReads = checked(authorityReads + 1);
                if (contract is null || !string.Equals(contract.Checksum, lifecycle.ContractChecksum, StringComparison.Ordinal))
                    return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);
                subjectAuthorities[$"{lifecycle.ContractId}\n{lifecycle.ContractVersion}"] = SubjectAuthority(lifecycle.ContractId, lifecycle.ContractVersion, contract);
                string key = SubjectKey(lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId);
                intervals.Add(ExactInterval($"subject:{lifecycle.ContractId}:contract", System.Text.Encoding.UTF8.GetBytes($"{lifecycle.ContractId}\n{lifecycle.ContractVersion}")));
                intervals.Add(ExactInterval($"subject:{lifecycle.ContractId}:lifetime", System.Text.Encoding.UTF8.GetBytes(key)));
                intervals.Add(ExactInterval($"subject:{lifecycle.ContractId}:record", System.Text.Encoding.UTF8.GetBytes(lifecycle.SubjectId.Value)));
                if (!lifetimes.TryGetValue(key, out SqlitePreparedSubjectLifetime? lifetime))
                {
                    lifetime = await ReadSubjectLifetimeAsync(lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId, token).ConfigureAwait(false);
                    authorityReads = checked(authorityReads + 1);
                    lifetimes[key] = lifetime;
                }
                switch (lifecycle.Kind)
                {
                    case BaseSubjectLifecycleMutationKind.Create:
                        if (lifetime is not null)
                            return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.TransactionConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
                        lifetime = new SqlitePreparedSubjectLifetime(
                            lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId,
                            BaseSubjectIncarnation.Create(), item.Collection.Id, item.RecordId,
                            item.Ordinal + 1L);
                        lifetimes[key] = lifetime;
                        lifecycleIncarnations[item.Ordinal] = lifetime.Incarnation;
                        break;
                    case BaseSubjectLifecycleMutationKind.Preserve:
                        if (lifetime is null) return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                        lifecycleIncarnations[item.Ordinal] = lifetime.Incarnation;
                        break;
                    case BaseSubjectLifecycleMutationKind.Retire:
                        if (lifetime is null) return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                        lifetimes[key] = null;
                        lifetime = null;
                        break;
                    default:
                        return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                }
                BaseExportedSubjectDefinition? definition = _owner._options.ExportedSubjects.FirstOrDefault(subject =>
                    string.Equals(subject.Id, lifecycle.ContractId, StringComparison.Ordinal) && subject.Version == lifecycle.ContractVersion);
                if (definition is null) return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                RecordEnvelope? privateRecord = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Retire ? null : PlanRecord(item);
                ReadLogicalValues(definition, privateRecord, out bool? active, out string? scope, out bool logicalStateValid);
                if (privateRecord is not null && !logicalStateValid)
                    return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                overlays[key] = new BasePreparedSubjectOverlayEvidence
                {
                    ContractId = lifecycle.ContractId, ContractVersion = lifecycle.ContractVersion,
                    SubjectId = lifecycle.SubjectId, Exists = lifetime is not null && privateRecord is not null,
                    Incarnation = lifetime?.Incarnation, Active = active, Scope = scope,
                };
            }

            var validationEvidence = ImmutableArray.CreateBuilder<BasePreparedSubjectValidationEvidence>(plan.SubjectValidations.Length);
            for (int ordinal = 0; ordinal < plan.SubjectValidations.Length; ordinal++)
            {
                BaseSubjectReferenceValidationPlanItem validation = plan.SubjectValidations[ordinal];
                BaseExportedSubjectDefinition? definition = _owner._options.ExportedSubjects.FirstOrDefault(subject =>
                    string.Equals(subject.ValidationPlan.Id, validation.ValidationPlanId, StringComparison.Ordinal)
                    && subject.ValidationPlan.Version == validation.ValidationPlanVersion);
                SqliteSubjectContractState? contract = null;
                SqlitePreparedSubjectLifetime? lifetime = null;
                RecordEnvelope? privateRecord = null;
                bool valid = definition is not null;
                string key = definition is null ? string.Empty : SubjectKey(definition.Id, definition.Version, validation.Reference.SubjectId);
                if (valid)
                {
                    contract = await ReadSubjectContractAsync(definition!.Id, definition.Version, token).ConfigureAwait(false);
                    authorityReads = checked(authorityReads + 1);
                    if (!lifetimes.TryGetValue(key, out lifetime))
                    {
                        lifetime = await ReadSubjectLifetimeAsync(definition.Id, definition.Version, validation.Reference.SubjectId, token).ConfigureAwait(false);
                        authorityReads = checked(authorityReads + 1);
                        lifetimes[key] = lifetime;
                    }
                    if (contract is not null)
                        subjectAuthorities[$"{definition.Id}\n{definition.Version}"] = SubjectAuthority(definition.Id, definition.Version, contract);
                    if (lifetime is not null)
                    {
                        if (!TryResolveFinalRecord(plan.Items, definition.ValidationPlan.PrivateCollectionId, lifetime.RecordId, out privateRecord))
                            privateRecord = await _owner.ReadAsync(
                                _connection,
                                definition.ValidationPlan.PrivateCollectionId,
                                lifetime.RecordId.Value,
                                token,
                                _transaction,
                                CommandTimeoutSeconds()).ConfigureAwait(false);
                        authorityReads = checked(authorityReads + 1);
                    }
                    valid = contract is not null
                        && lifetime is not null
                        && privateRecord is not null
                        && contract.Epoch.Equals(validation.Reference.AuthorityEpoch)
                        && lifetime.Incarnation.Equals(validation.Reference.Incarnation);
                }
                bool? active = null;
                string? scope = null;
                if (privateRecord is not null)
                {
                    BaseExportedSubjectDefinition resolvedDefinition = definition!;
                    ReadLogicalValues(resolvedDefinition, privateRecord, out active, out scope, out bool logicalStateValid);
                    if (!logicalStateValid)
                        return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                    valid = valid && logicalStateValid
                        && (validation.Requirement != BaseSubjectReferenceRequirement.Active || active == resolvedDefinition.ValidationPlan.Active.ActiveValue)
                        && (resolvedDefinition.Scope == BaseSubjectScopeKind.Global || string.Equals(scope, validation.Scope.Value, StringComparison.Ordinal));
                }
                validationEvidence.Add(new BasePreparedSubjectValidationEvidence
                {
                    Ordinal = ordinal, MutationOrdinal = validation.MutationOrdinal,
                    SourceFieldId = validation.SourceFieldId,
                    State = valid ? BaseSubjectValidationState.Valid : BaseSubjectValidationState.Invalid,
                });
                if (definition is not null)
                {
                    byte[] contractKey = System.Text.Encoding.UTF8.GetBytes($"{definition.Id}\n{definition.Version}");
                    byte[] subjectKey = System.Text.Encoding.UTF8.GetBytes(key);
                    byte[] recordKey = System.Text.Encoding.UTF8.GetBytes(validation.Reference.SubjectId.Value);
                    intervals.Add(ExactInterval($"subject:{definition.Id}:contract", contractKey));
                    intervals.Add(ExactInterval($"subject:{definition.Id}:lifetime", subjectKey));
                    intervals.Add(ExactInterval($"subject:{definition.Id}:record", recordKey));
                    overlays[key] = new BasePreparedSubjectOverlayEvidence
                    {
                        ContractId = definition.Id, ContractVersion = definition.Version,
                        SubjectId = validation.Reference.SubjectId, Exists = lifetime is not null && privateRecord is not null,
                        Incarnation = lifetime?.Incarnation, Active = active, Scope = scope,
                    };
                }
            }
            long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals.ToImmutable());
            BasePreparedSubjectOverlayEvidence[] ownedOverlays = overlays.Values.ToArray();
            BaseSubjectTransactionAuthorityEvidence[] ownedAuthorities = subjectAuthorities.Values.ToArray();
            BasePreparedSubjectValidationEvidence[] ownedValidations = validationEvidence.ToArray();
            long addedIntervalBytes = checked(
                BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals.ToImmutable())
                - BaseSubjectCanonicalRetainedWork.MeasureIntervals(captured.ReadIntervals));
            long transient = checked(retainedBytes + addedIntervalBytes
                + BaseSubjectCanonicalRetainedWork.MeasurePreparedEvidence(ownedOverlays, ownedAuthorities, ownedValidations)
                + BaseSubjectCanonicalRetainedWork.MeasureStringDictionary(lifetimes,
                    value => value is null ? 1L : checked(1L + CanonicalLifetimeRetainedBytes(value)))
                + BaseSubjectCanonicalRetainedWork.MeasureStringDictionary(overlays)
                + BaseSubjectCanonicalRetainedWork.MeasureStringDictionary(subjectAuthorities)
                + BaseSubjectCanonicalRetainedWork.MeasureIntegerDictionary(lifecycleIncarnations, static _ => 16L));
            int intervalCount = intervals.Count;
            if (authorityReads > plan.Limits.MaximumAuthorityReads || intervalCount > plan.Limits.MaximumReadIntervals
                || evidenceBytes > plan.Limits.MaximumEvidenceBytes || transient > plan.Limits.MaximumTransientBytes)
                return SubjectFailure<BasePreparedAtomicExecution>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);
            _preparedPlan = plan;
            _preparedLifecycleIncarnations = lifecycleIncarnations;
            _preparedMutation = new BasePreparedAtomicExecution
            {
                Kind = plan.Kind,
                PlanDigest = new string(plan.PlanDigest.AsSpan()), Authority = captured.Authority with { },
                SubjectAuthorities = subjectAuthorities.Values.OrderBy(static value => value.ContractId, StringComparer.Ordinal)
                    .ThenBy(static value => value.ContractVersion).ToImmutableArray(),
                Dispositions = plan.Kind == BaseAtomicMutationExecutionKind.ModuleMutation
                    ? plan.Items.Select(static item => item.Kind switch
                    {
                        BaseCommittedRecordMutationKind.Create => BaseCapturedMutationDisposition.Create,
                        BaseCommittedRecordMutationKind.Delete => BaseCapturedMutationDisposition.Delete,
                        _ => BaseCapturedMutationDisposition.Update,
                    }).ToImmutableArray()
                    : captured.Items.Select(static item => item.Disposition).ToImmutableArray(),
                Generations = preparedGenerations.MoveToImmutable(),
                Activations = preparedActivations,
                ActivationGuard = captured.ActivationGuard,
                SubjectOverlay = overlays.Values.OrderBy(static value => value.ContractId, StringComparer.Ordinal).ThenBy(static value => value.ContractVersion).ThenBy(static value => value.SubjectId.Value, StringComparer.Ordinal).ToImmutableArray(),
                SubjectValidations = validationEvidence.MoveToImmutable(),
                ReadIntervals = intervals.MoveToImmutable(),
                Accounting = new BasePreparedAtomicMutationAccounting
                {
                    AuthorityReads = authorityReads, GenerationReads = captured.Generations.Length,
                    GenerationComparisons = plan.Module?.Comparisons.Length ?? 0,
                    GenerationIncrements = plan.Module?.Increments.Length ?? 0,
                    ReadIntervals = intervalCount,
                    SelectedBytes = captured.Accounting.SelectedBytes, GenerationBytes = captured.Accounting.GenerationBytes, EvidenceBytes = evidenceBytes,
                    TransientBytes = transient,
                },
            };
            return OperationResults.Ok(_preparedMutation);
        });

        private static long CanonicalLifetimeRetainedBytes(SqlitePreparedSubjectLifetime value)
        {
            var counter = new BaseSubjectCanonicalRetainedWork();
            counter.AddContainer(); counter.AddString(value.ContractId); counter.AddInteger();
            counter.AddString(value.SubjectId.Value); counter.AddFixed16();
            counter.AddString(value.CollectionId); counter.AddString(value.RecordId.Value); counter.AddInteger();
            return counter.Bytes;
        }

        private static long CanonicalOverlayRetainedBytes(BasePreparedSubjectOverlayEvidence value) =>
            BaseSubjectCanonicalRetainedWork.MeasureOverlay(value);

        private static long CanonicalAuthorityRetainedBytes(BaseSubjectTransactionAuthorityEvidence value) =>
            BaseSubjectCanonicalRetainedWork.MeasureAuthority(value);

        private static long CanonicalPlanRetainedBytes(BaseFinalizedAtomicExecutionPlan plan) =>
            BaseSubjectCanonicalRetainedWork.MeasurePlan(plan);

        private BaseSubjectTransactionAuthorityEvidence SubjectAuthority(
            string contractId,
            int contractVersion,
            SqliteSubjectContractState contract) => new()
        {
            ContractId = new string(contractId.AsSpan()),
            ContractVersion = contractVersion,
            ContractChecksum = new string(contract.Checksum.AsSpan()),
            StoreInstanceId = new string(_owner._options.StoreId.AsSpan()),
            RestoreEpoch = contract.RestoreEpoch,
            SchemaGeneration = Volatile.Read(ref _owner._schemaGeneration),
            StateGeneration = contract.StateGeneration,
            AuthorityEpoch = new BaseSubjectAuthorityEpoch(contract.Epoch.ToArray()),
        };

        public ValueTask<OperationResult<BaseProvisionalAtomicExecution>> ApplyPreparedAtomicExecutionAsync(
            BasePreparedAtomicExecution prepared, CancellationToken cancellationToken = default) => ExecuteAsync(BaseOperationKind.Patch, cancellationToken, async token =>
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(prepared, _preparedMutation) || _preparedPlan is null)
                return SubjectFailure<BaseProvisionalAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
            BaseFinalizedAtomicExecutionPlan plan = _preparedPlan;
            _preparedMutation = null;
            Dictionary<int, BaseSubjectIncarnation> lifecycleIncarnations = _preparedLifecycleIncarnations
                ?? new Dictionary<int, BaseSubjectIncarnation>();
            _preparedLifecycleIncarnations = null;
            var facts = ImmutableArray.CreateBuilder<BaseOwnedMutationFact>(plan.Items.Length);
            long writtenBytes = 0;
            long factBytes = 0;
            foreach (BaseAtomicMutationPlanItem item in plan.Items)
            {
                token.ThrowIfCancellationRequested();
                var context = new RecordMutationSessionContext
                {
                    ItemId = item.ItemId,
                    RequestedOperation = item.RequestedKind,
                    EventId = item.EventId,
                    Operation = item.Operation,
                    ChangedFields = item.ChangedFields.ToArray(),
                };
                OperationResult<RecordMutationSessionResult> mutation = item.Kind switch
                {
                    BaseCommittedRecordMutationKind.Create => await CreateCoreAsync(
                        item.Collection,
                        new RecordCreateRequest { RequestedId = item.RecordId, Payload = item.ProposedPayload! },
                        context,
                        token,
                        item.RuntimeAssignedRecordId).ConfigureAwait(false),
                    BaseCommittedRecordMutationKind.Patch => await MutateCoreAsync(
                        item.Collection,
                        item.RecordId,
                        item.Current?.Metadata.Revision,
                        PatchDelta(item),
                        replace: false,
                        BaseCommittedRecordMutationKind.Patch,
                        context,
                        token).ConfigureAwait(false),
                    BaseCommittedRecordMutationKind.Replace => await MutateCoreAsync(
                        item.Collection,
                        item.RecordId,
                        item.Current?.Metadata.Revision,
                        item.ProposedPayload!,
                        replace: true,
                        BaseCommittedRecordMutationKind.Replace,
                        context,
                        token).ConfigureAwait(false),
                    BaseCommittedRecordMutationKind.Delete => await DeleteCoreAsync(
                        item.Collection,
                        item.RecordId,
                        item.Delete! with { ExpectedRevision = item.Current?.Metadata.Revision },
                        context,
                        token).ConfigureAwait(false),
                    _ => SubjectFailure<RecordMutationSessionResult>(BaseSubjectErrorCodes.ProviderContractInvalid),
                };
                if (!mutation.IsSuccess() || mutation.Value is null)
                    return new OperationResult<BaseProvisionalAtomicExecution> { Status = mutation.Status, Error = mutation.Error };
                if (item.SubjectLifecycle is { } lifecycle)
                {
                    BasePreparedSubjectOverlayEvidence? overlay = prepared.SubjectOverlay.FirstOrDefault(candidate =>
                        string.Equals(candidate.ContractId, lifecycle.ContractId, StringComparison.Ordinal)
                        && candidate.ContractVersion == lifecycle.ContractVersion
                        && candidate.SubjectId.Equals(lifecycle.SubjectId));
                    if (overlay is null)
                        return SubjectFailure<BaseProvisionalAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                    OperationResult lifecycleResult = await ApplySubjectLifecycleAsync(
                        item,
                        lifecycle,
                        lifecycleIncarnations.TryGetValue(item.Ordinal, out BaseSubjectIncarnation incarnation)
                            ? incarnation
                            : null,
                        mutation.Value.Mutation.JournalPosition,
                        token).ConfigureAwait(false);
                    if (!lifecycleResult.IsSuccess())
                        return new OperationResult<BaseProvisionalAtomicExecution> { Status = lifecycleResult.Status, Error = lifecycleResult.Error };
                }
                BaseRecordMutationFact committedFact = mutation.Value.Mutation with
                {
                    SubjectLifecycle = item.SubjectLifecycle is null ? null : new BaseSubjectLifecycleCommitEvidence
                    {
                        ContractId = item.SubjectLifecycle.ContractId,
                        ContractVersion = item.SubjectLifecycle.ContractVersion,
                        SubjectId = item.SubjectLifecycle.SubjectId.Value,
                        Kind = item.SubjectLifecycle.Kind,
                        Incarnation = item.SubjectLifecycle.Kind == BaseSubjectLifecycleMutationKind.Retire
                            ? null
                            : lifecycleIncarnations.GetValueOrDefault(item.Ordinal).ToBase64Url(),
                    },
                };
                BaseOwnedMutationFact owned = BaseOwnedMutationFact.Freeze(committedFact, 1);
                facts.Add(owned);
                factBytes = checked(factBytes + owned.EncodedLength);
                writtenBytes = checked(writtenBytes + (mutation.Value.Record is null
                    ? System.Text.Encoding.UTF8.GetByteCount(item.RecordId.Value) + sizeof(long)
                    : JsonSerializer.SerializeToUtf8Bytes(mutation.Value.Record, HPDBaseJsonSerializerContext.Default.RecordEnvelope).LongLength));
            }
            if (plan.Kind == BaseAtomicMutationExecutionKind.ModuleMutation)
            {
                if (_capturedModuleGenerationKeys is null || prepared.Generations.Length != _capturedModuleGenerationKeys.Count)
                    return SubjectFailure<BaseProvisionalAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                foreach (BasePreparedModuleGenerationEvidence generation in prepared.Generations)
                {
                    if (!_capturedModuleGenerationKeys.TryGetValue(generation.CaptureOrdinal, out SqliteModuleGenerationKey? key))
                        return SubjectFailure<BaseProvisionalAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                    if (generation.Disposition is BaseModuleGenerationPreparationDisposition.RemainedAbsent
                        or BaseModuleGenerationPreparationDisposition.Preserved) continue;
                    await using SqliteCommand command = _connection.CreateCommand();
                    command.Transaction = _transaction;
                    AddGenerationKeyParameters(command, key);
                    command.Parameters.AddWithValue("$result", generation.Resulting!.Value);
                    if (generation.Disposition == BaseModuleGenerationPreparationDisposition.Created)
                    {
                        command.CommandText = $"INSERT INTO {_owner._names.ModuleGenerations}(cell_id,cell_version,scope_kind,tenant,project,key_bytes,generation) VALUES($id,$version,$scope,$tenant,$project,$key,$result);";
                    }
                    else
                    {
                        command.CommandText = $"UPDATE {_owner._names.ModuleGenerations} SET generation=$result WHERE cell_id=$id AND cell_version=$version AND scope_kind=$scope AND tenant=$tenant AND project=$project AND key_bytes=$key AND generation=$previous;";
                        command.Parameters.AddWithValue("$previous", generation.Previous!.Value);
                    }
                    int changed = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    if (changed != 1)
                        return SubjectFailure<BaseProvisionalAtomicExecution>("base.moduleMutation.generationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
                    writtenBytes = checked(writtenBytes + 8);
                }
                _capturedModuleGenerationKeys = null;
            }
            BaseProvisionalActivationExtension? provisionalActivations = null;
            if (plan.Kind == BaseAtomicMutationExecutionKind.ActivationCreation)
            {
                if (prepared.Activations is null || plan.Activations is null || _capturedMutation?.Activations is null
                    || prepared.Activations.Items.Length != plan.Activations.Items.Length)
                    return SubjectFailure<BaseProvisionalAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                var appliedActivations = ImmutableArray.CreateBuilder<BaseProvisionalActivationItem>(prepared.Activations.Items.Length);
                using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                for (int ordinal = 0; ordinal < prepared.Activations.Items.Length; ordinal++)
                {
                    BasePreparedActivationItem preparedItem = prepared.Activations.Items[ordinal];
                    BaseActivationCreateIntent intentItem = plan.Activations.Items[ordinal];
                    BaseCapturedActivationItem capturedItem = _capturedMutation.Activations.Items[ordinal];
                    byte[] fingerprint = ActivationFingerprint(intentItem);
                    if (!capturedItem.Exists)
                    {
                        await using SqliteCommand command = _connection.CreateCommand();
                        command.Transaction = _transaction;
                        command.CommandText = $"INSERT INTO {_owner._names.Activations}(activation_id,definition_id,definition_version,definition_checksum,canonical_input,input_checksum,scope_kind,scope_value,scope_digest,payload_checksum,fingerprint,state,generation,requested_due_at,effective_due_at,control_checksum) VALUES($id,$definition,$version,$definition_checksum,$input,$input_checksum,$scope_kind,$scope_value,$scope_digest,$payload_checksum,$fingerprint,$state,1,$requested,$effective,$control_checksum);";
                        command.Parameters.AddWithValue("$id", preparedItem.ActivationId);
                        command.Parameters.AddWithValue("$definition", intentItem.Definition.Id);
                        command.Parameters.AddWithValue("$version", intentItem.Definition.Version);
                        command.Parameters.Add("$definition_checksum", SqliteType.Blob).Value = intentItem.Definition.Checksum.ToArray();
                        command.Parameters.Add("$input", SqliteType.Blob).Value = intentItem.CanonicalInput.ToArray();
                        command.Parameters.Add("$input_checksum", SqliteType.Blob).Value = intentItem.InputChecksum.ToArray();
                        command.Parameters.AddWithValue("$scope_kind", (int)intentItem.Scope.Kind);
                        command.Parameters.AddWithValue("$scope_value", intentItem.Scope.Value ?? string.Empty);
                        command.Parameters.Add("$scope_digest", SqliteType.Blob).Value = SHA256.HashData(Encoding.UTF8.GetBytes(
                            $"base.activation.scope.v2\0{(int)intentItem.Scope.Kind}\n{intentItem.Scope.Value ?? string.Empty}"));
                        command.Parameters.Add("$payload_checksum", SqliteType.Blob).Value = preparedItem.PayloadChecksum.ToArray();
                        command.Parameters.Add("$fingerprint", SqliteType.Blob).Value = fingerprint;
                        command.Parameters.AddWithValue("$state", (int)BaseActivationState.Pending);
                        command.Parameters.AddWithValue("$requested", intentItem.RequestedDueAt);
                        command.Parameters.AddWithValue("$effective", intentItem.EffectiveDueAt ?? intentItem.RequestedDueAt);
                        command.Parameters.Add("$control_checksum", SqliteType.Blob).Value = preparedItem.ControlChecksum.ToArray();
                        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                            return SubjectFailure<BaseProvisionalAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                        await using SqliteCommand generation = _connection.CreateCommand();
                        generation.Transaction = _transaction;
                        generation.CommandText = $"UPDATE {_owner._names.ProviderState} SET value=CAST(CAST(value AS INTEGER)+1 AS TEXT) WHERE key='activation_generation';";
                        if (await generation.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                            return SubjectFailure<BaseProvisionalAtomicExecution>(BaseSubjectErrorCodes.ProviderContractInvalid);
                        writtenBytes = checked(writtenBytes + intentItem.CanonicalInput.Length + 192L);
                    }
                    byte[] itemChecksum = SHA256.HashData(Encoding.UTF8.GetBytes(
                        $"{preparedItem.ActivationId}\n{preparedItem.ResultingGeneration}"));
                    aggregate.AppendData(itemChecksum);
                    appliedActivations.Add(new BaseProvisionalActivationItem
                    {
                        Ordinal = ordinal, ActivationId = preparedItem.ActivationId,
                        Generation = preparedItem.ResultingGeneration, Checksum = itemChecksum.ToImmutableArray(),
                    });
                }
                provisionalActivations = new BaseProvisionalActivationExtension
                {
                    Items = appliedActivations.MoveToImmutable(),
                    Checksum = aggregate.GetHashAndReset().ToImmutableArray(),
                };
                _capturedActivationExtension = null;
            }
            BaseRecordMutationFact[] materialized = facts.Select(static fact => fact.MaterializeOwned()).ToArray();
            foreach (ISqliteAtomicMutationProjection contributor in _owner._mutationProjectionContributors)
            {
                var projectionContext = new SqliteAtomicProjectionContext(_owner, _connection, _transaction, (ISqliteAtomicMutationProjectionCatalog)contributor);
                OperationResult projected = await contributor.ApplyAsync(
                    projectionContext,
                    BaseAtomicMutationProjectionFactory.Create(materialized),
                    token).ConfigureAwait(false);
                if (!projected.IsSuccess())
                    return new OperationResult<BaseProvisionalAtomicExecution> { Status = projected.Status, Error = projected.Error };
            }
            long journalBytes = materialized.Sum(static fact =>
                (long)JsonSerializer.SerializeToUtf8Bytes(fact, HPDBaseJsonSerializerContext.Default.BaseRecordMutationFact).LongLength);
            long transient = checked(prepared.Accounting.TransientBytes + writtenBytes + factBytes + journalBytes + _attributionTransientBytes);
            ImmutableArray<BaseModuleCommittedGeneration> generations = CommittedGenerations(prepared);
            var applied = new BaseProvisionalAtomicExecution
            {
                Kind = plan.Kind,
                PlanDigest = new string(plan.PlanDigest.AsSpan()),
                Authority = prepared.Authority with { },
                Facts = facts.MoveToImmutable(),
                Generations = generations,
                Activations = provisionalActivations,
                ActivationGuard = prepared.ActivationGuard,
                Accounting = new BaseProvisionalAtomicMutationAccounting
                {
                    WrittenBytes = writtenBytes,
                    GenerationBytes = prepared.Accounting.GenerationBytes,
                    FactBytes = factBytes,
                    JournalBytes = journalBytes,
                    RelationChecks = _relationChecks,
                    UniqueConstraintChecks = _uniqueChecks,
                    AuthorityReads = prepared.Accounting.AuthorityReads,
                    ReadIntervals = prepared.ReadIntervals.Length,
                    SelectedBytes = prepared.Accounting.SelectedBytes,
                    EvidenceBytes = prepared.Accounting.EvidenceBytes,
                    TransientBytes = transient,
                },
            };
            _appliedProvisional = applied;
            return OperationResults.Ok(applied);
        });

        internal bool ValidateCommitFinalization(AtomicMutationProcessingResult processing)
        {
            if (processing.Receipt.Kind != BaseAtomicReceiptResultKind.ModuleMutation)
                return processing.Finalization is null;
            BaseAtomicMutationCommitFinalization? finalization = processing.Finalization;
            BaseProvisionalAtomicExecution? applied = _appliedProvisional;
            BaseModuleMutationReceiptResult? module = processing.Receipt.ModuleMutation;
            if (finalization is null || applied is null || module is null
                || !ReferenceEquals(finalization.Receipt, processing.Receipt)
                || !string.Equals(finalization.PlanDigest, applied.PlanDigest, StringComparison.Ordinal)
                || !finalization.CanonicalResultBytes.AsSpan().SequenceEqual(module.CanonicalResultBytes.AsSpan())
                || !applied.Facts.Select(static value => value.CopyCanonicalBytes()).SequenceEqual(
                    processing.Receipt.Mutations.Select(static value => value.CopyCanonicalBytes()), ByteArrayComparer.Instance))
                return false;
            long receiptBytes;
            try
            {
                receiptBytes = JsonSerializer.SerializeToUtf8Bytes(
                    BaseAtomicReceiptWire.From(processing.Receipt),
                    HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire).LongLength;
            }
            catch { return false; }
            BaseAtomicCommitAccounting actual = finalization.Accounting;
            BaseProvisionalAtomicMutationAccounting prior = applied.Accounting;
            long resultBytes = finalization.CanonicalResultBytes.Length;
            return actual.WrittenBytes == prior.WrittenBytes
                && actual.GenerationBytes == prior.GenerationBytes
                && actual.FactBytes == prior.FactBytes
                && actual.JournalBytes == prior.JournalBytes
                && actual.ReceiptBytes == receiptBytes
                && actual.ResultBytes == resultBytes
                && actual.RelationChecks == prior.RelationChecks
                && actual.UniqueConstraintChecks == prior.UniqueConstraintChecks
                && actual.AuthorityReads == prior.AuthorityReads
                && actual.ReadIntervals == prior.ReadIntervals
                && actual.SelectedBytes == prior.SelectedBytes
                && actual.EvidenceBytes == prior.EvidenceBytes
                && actual.TransientBytes == checked(prior.TransientBytes + receiptBytes + resultBytes);
        }

        private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            internal static ByteArrayComparer Instance { get; } = new();
            public bool Equals(byte[]? left, byte[]? right) => left is not null && right is not null && left.AsSpan().SequenceEqual(right);
            public int GetHashCode(byte[] value) => 0;
        }

        private ImmutableArray<BaseModuleCommittedGeneration> CommittedGenerations(BasePreparedAtomicExecution prepared)
        {
            if (prepared.Kind != BaseAtomicMutationExecutionKind.ModuleMutation) return [];
            BaseModuleMutationCaptureExtension module = _capturedModuleExtension
                ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            return prepared.Generations
                .Where(static generation => generation.Disposition is BaseModuleGenerationPreparationDisposition.Created
                    or BaseModuleGenerationPreparationDisposition.Incremented)
                .Select(generation =>
                {
                    BaseModuleGenerationCaptureRequest capture = module.Generations.Single(item => item.Ordinal == generation.CaptureOrdinal);
                    return new BaseModuleCommittedGeneration
                    {
                        CaptureId = capture.CaptureId,
                        CellId = capture.Cell.Id,
                        CellVersion = capture.Cell.Version,
                        Previous = generation.Previous,
                        Resulting = generation.Resulting!,
                    };
                }).ToImmutableArray();
        }

        private async ValueTask<OperationResult> ApplySubjectLifecycleAsync(
            BaseAtomicMutationPlanItem item,
            BaseSubjectLifecyclePlanItem lifecycle,
            BaseSubjectIncarnation? preparedIncarnation,
            BaseMutationJournalPosition journalPosition,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandTimeout = CommandTimeoutSeconds();
            command.Parameters.AddWithValue("$contract", lifecycle.ContractId);
            command.Parameters.AddWithValue("$version", lifecycle.ContractVersion);
            command.Parameters.AddWithValue("$subject", lifecycle.SubjectId.Value);
            switch (lifecycle.Kind)
            {
                case BaseSubjectLifecycleMutationKind.Create:
                    if (preparedIncarnation is not { } incarnation || journalPosition.Value <= 0)
                        return SubjectFailure(BaseSubjectErrorCodes.ProviderContractInvalid);
                    command.CommandText = $"INSERT INTO {_owner._names.SubjectLifetimes}(contract_id, contract_version, subject_id, incarnation, private_collection_id, private_record_id, created_journal_position) VALUES($contract,$version,$subject,$incarnation,$collection,$record,$position);";
                    command.Parameters.Add("$incarnation", SqliteType.Blob).Value = incarnation.ToArray();
                    command.Parameters.AddWithValue("$collection", item.Collection.Id);
                    command.Parameters.AddWithValue("$record", item.RecordId.Value);
                    command.Parameters.AddWithValue("$position", journalPosition.Value);
                    break;
                case BaseSubjectLifecycleMutationKind.Preserve:
                    if (preparedIncarnation is not { } preservedIncarnation)
                        return SubjectFailure(BaseSubjectErrorCodes.ProviderContractInvalid);
                    command.CommandText = $"SELECT COUNT(*) FROM {_owner._names.SubjectLifetimes} WHERE contract_id=$contract AND contract_version=$version AND subject_id=$subject AND incarnation=$incarnation AND private_collection_id=$collection AND private_record_id=$record;";
                    command.Parameters.Add("$incarnation", SqliteType.Blob).Value = preservedIncarnation.ToArray();
                    command.Parameters.AddWithValue("$collection", item.Collection.Id);
                    command.Parameters.AddWithValue("$record", item.RecordId.Value);
                    object? count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    return Convert.ToInt64(count, CultureInfo.InvariantCulture) == 1
                        ? SubjectSuccess()
                        : SubjectFailure(BaseSubjectErrorCodes.ProviderContractInvalid);
                case BaseSubjectLifecycleMutationKind.Retire:
                    if (preparedIncarnation is not null)
                        return SubjectFailure(BaseSubjectErrorCodes.ProviderContractInvalid);
                    command.CommandText = $"DELETE FROM {_owner._names.SubjectLifetimes} WHERE contract_id=$contract AND contract_version=$version AND subject_id=$subject AND private_collection_id=$collection AND private_record_id=$record;";
                    command.Parameters.AddWithValue("$collection", item.Collection.Id);
                    command.Parameters.AddWithValue("$record", item.RecordId.Value);
                    break;
                default:
                    return SubjectFailure(BaseSubjectErrorCodes.ProviderContractInvalid);
            }

            int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return affected == 1
                ? SubjectSuccess()
                : SubjectFailure(BaseSubjectErrorCodes.ProviderContractInvalid);
        }

        private sealed record SqliteSubjectContractState(
            string Checksum,
            BaseSubjectAuthorityEpoch Epoch,
            long RestoreEpoch,
            long StateGeneration);

        private sealed record SqlitePreparedSubjectLifetime(
            string ContractId,
            int ContractVersion,
            BaseSubjectId SubjectId,
            BaseSubjectIncarnation Incarnation,
            string CollectionId,
            RecordId RecordId,
            long CreatedJournalPosition);

        private async ValueTask<SqliteSubjectContractState?> ReadSubjectContractAsync(
            string contractId,
            int contractVersion,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandTimeout = CommandTimeoutSeconds();
            command.CommandText = $"SELECT contract_checksum, authority_epoch, restore_epoch, state_generation FROM {_owner._names.SubjectContracts} WHERE contract_id=$contract AND contract_version=$version;";
            command.Parameters.AddWithValue("$contract", contractId);
            command.Parameters.AddWithValue("$version", contractVersion);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            return new SqliteSubjectContractState(
                reader.GetString(0),
                new BaseSubjectAuthorityEpoch((byte[])reader.GetValue(1)),
                reader.GetInt64(2),
                reader.GetInt64(3));
        }

        private async ValueTask<SqlitePreparedSubjectLifetime?> ReadSubjectLifetimeAsync(
            string contractId,
            int contractVersion,
            BaseSubjectId subjectId,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandTimeout = CommandTimeoutSeconds();
            command.CommandText = $"SELECT incarnation, private_collection_id, private_record_id, created_journal_position FROM {_owner._names.SubjectLifetimes} WHERE contract_id=$contract AND contract_version=$version AND subject_id=$subject;";
            command.Parameters.AddWithValue("$contract", contractId);
            command.Parameters.AddWithValue("$version", contractVersion);
            command.Parameters.AddWithValue("$subject", subjectId.Value);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            return new SqlitePreparedSubjectLifetime(
                contractId,
                contractVersion,
                subjectId,
                new BaseSubjectIncarnation((byte[])reader.GetValue(0)),
                reader.GetString(1),
                new RecordId(reader.GetString(2)),
                reader.GetInt64(3));
        }

        private static string SubjectKey(string contractId, int version, BaseSubjectId subjectId) =>
            $"{contractId}\n{version}\n{subjectId.Value}";

        private static string CaptureRecordKey(string collectionId, RecordId recordId) =>
            collectionId + "\n" + recordId.Value;

        private static byte[] ActivationFingerprint(BaseActivationCreateIntent item)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes("base.activation.create.v1\0"));
            hash.AppendData(Encoding.UTF8.GetBytes(item.Definition.Id));
            hash.AppendData(BitConverter.GetBytes(item.Definition.Version).Reverse().ToArray());
            hash.AppendData(item.Definition.Checksum.AsSpan());
            hash.AppendData(item.InputChecksum.AsSpan());
            hash.AppendData(BitConverter.GetBytes(item.RequestedDueAt).Reverse().ToArray());
            hash.AppendData(BitConverter.GetBytes(item.EffectiveDueAt ?? item.RequestedDueAt).Reverse().ToArray());
            hash.AppendData(Encoding.UTF8.GetBytes(item.Identity.IdempotencyKey));
            return hash.GetHashAndReset();
        }

        private static BaseActivationCreationExtension FreezeActivationExtension(BaseActivationCreationExtension source) => new()
        {
            StructuralDigest = source.StructuralDigest.ToArray().ToImmutableArray(),
            Items = source.Items.Select(static item => item with
            {
                Definition = item.Definition with { Checksum = item.Definition.Checksum.ToArray().ToImmutableArray() },
                CanonicalInput = item.CanonicalInput.ToArray().ToImmutableArray(),
                InputChecksum = item.InputChecksum.ToArray().ToImmutableArray(),
                Scope = item.Scope with { },
            }).ToImmutableArray(),
        };

        private static bool ActivationExtensionsMatch(
            BaseActivationCreationExtension plan,
            BaseActivationCreationExtension capturedRequest,
            BaseCapturedActivationExtension captured) =>
            plan.StructuralDigest.AsSpan().SequenceEqual(capturedRequest.StructuralDigest.AsSpan())
            && plan.Items.Length == capturedRequest.Items.Length && plan.Items.Length == captured.Items.Length
            && plan.Items.Select((item, ordinal) => (item, ordinal)).All(pair =>
            {
                BaseActivationCreateIntent right = capturedRequest.Items[pair.ordinal];
                return pair.item.Ordinal == pair.ordinal && right.Ordinal == pair.ordinal
                    && string.Equals(pair.item.Definition.Id, right.Definition.Id, StringComparison.Ordinal)
                    && pair.item.Definition.Version == right.Definition.Version
                    && pair.item.Definition.Checksum.AsSpan().SequenceEqual(right.Definition.Checksum.AsSpan())
                    && pair.item.CanonicalInput.AsSpan().SequenceEqual(right.CanonicalInput.AsSpan())
                    && pair.item.InputChecksum.AsSpan().SequenceEqual(right.InputChecksum.AsSpan())
                    && pair.item.RequestedDueAt == right.RequestedDueAt && pair.item.EffectiveDueAt == right.EffectiveDueAt
                    && Equals(pair.item.Scope, right.Scope) && Equals(pair.item.Identity, right.Identity)
                    && captured.Items[pair.ordinal].Ordinal == pair.ordinal;
            });

        private static RecordEnvelope? SimulateIntentRecord(
            BaseAtomicMutationIntentItem item,
            RecordEnvelope? current)
        {
            if (item.RequestedKind == BaseRecordMutationKind.Delete) return null;
            RecordPayload? payload = item.RequestedKind switch
            {
                BaseRecordMutationKind.Create => item.Create?.Payload,
                BaseRecordMutationKind.Replace => item.Replace?.Payload,
                BaseRecordMutationKind.Patch => MergeCapturePatch(current?.Payload, item.Patch?.Patch),
                BaseRecordMutationKind.Upsert when current is null => item.Upsert?.CreatePayload,
                BaseRecordMutationKind.Upsert when item.Upsert?.UpdateMode == RecordUpsertUpdateMode.Replace => item.Upsert.UpdatePayload,
                BaseRecordMutationKind.Upsert => MergeCapturePatch(current?.Payload, item.Upsert?.UpdatePayload),
                _ => null,
            };
            if (payload is null) return current;
            return new RecordEnvelope
            {
                CollectionId = item.Collection.Id,
                Id = item.RecordId,
                Payload = ClonePayload(payload),
                Metadata = current?.Metadata is { } metadata ? metadata with { } : new RecordMetadata(),
            };
        }

        private static RecordPayload? MergeCapturePatch(RecordPayload? current, RecordPayload? patch)
        {
            if (current?.Kind != RecordPayloadKind.FieldMap || current.Fields is null
                || patch?.Kind != RecordPayloadKind.FieldMap || patch.Fields is null)
                return patch;
            var fields = current.Fields.ToDictionary(
                static value => value.Key,
                static value => value.Value.Clone(),
                StringComparer.Ordinal);
            foreach ((string key, JsonElement value) in patch.Fields)
                fields[key] = value.Clone();
            return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
        }

        private static RecordEnvelope? PlanRecord(BaseAtomicMutationPlanItem item) =>
            item.Kind == BaseCommittedRecordMutationKind.Delete || item.ProposedPayload is null
                ? null
                : new RecordEnvelope
                {
                    CollectionId = item.Collection.Id,
                    Id = item.RecordId,
                    Payload = ClonePayload(item.ProposedPayload),
                    Metadata = item.Current?.Metadata ?? new RecordMetadata(),
                };

        private static RecordPayload ClonePayload(RecordPayload payload) => new()
        {
            Kind = payload.Kind,
            Fields = payload.Fields?.ToDictionary(
                static pair => new string(pair.Key.AsSpan()),
                static pair => pair.Value.Clone(),
                StringComparer.Ordinal),
        };

        private static bool TryResolveFinalRecord(
            ImmutableArray<BaseAtomicMutationPlanItem> items,
            string collectionId,
            RecordId recordId,
            out RecordEnvelope? record)
        {
            for (int index = items.Length - 1; index >= 0; index--)
            {
                BaseAtomicMutationPlanItem item = items[index];
                if (!string.Equals(item.Collection.Id, collectionId, StringComparison.Ordinal) || item.RecordId != recordId)
                    continue;
                record = PlanRecord(item);
                return true;
            }
            record = null;
            return false;
        }

        private bool TryReadLogicalField(
            BaseExportedSubjectDefinition definition,
            RecordEnvelope record,
            string fieldId,
            out JsonElement value)
        {
            value = default;
            CollectionDefinition? collection = (_owner._options.Collections ?? []).FirstOrDefault(candidate =>
                string.Equals(candidate.Id, definition.ValidationPlan.PrivateCollectionId, StringComparison.Ordinal));
            string? wireName = collection?.Fields?.FirstOrDefault(field =>
                string.Equals(field.Id, fieldId, StringComparison.Ordinal))?.WireName;
            return wireName is not null && record.Payload.Fields?.TryGetValue(wireName, out value) == true;
        }

        private void ReadLogicalValues(
            BaseExportedSubjectDefinition definition,
            RecordEnvelope? record,
            out bool? active,
            out string? scope,
            out bool valid)
        {
            active = null;
            scope = null;
            valid = record is not null && string.Equals(record.CollectionId, definition.ValidationPlan.PrivateCollectionId, StringComparison.Ordinal);
            if (!valid) return;
            if (definition.ValidationPlan.Active.Kind == BaseSubjectActiveBindingKind.RequiredBooleanField)
            {
                valid = TryReadLogicalField(definition, record!, definition.ValidationPlan.Active.FieldId!, out JsonElement activeValue)
                    && activeValue.ValueKind is JsonValueKind.True or JsonValueKind.False;
                if (!valid) return;
                active = activeValue.GetBoolean();
            }
            if (definition.ValidationPlan.Scope.Kind != BaseSubjectScopeBindingKind.Global)
            {
                valid = TryReadLogicalField(definition, record!, definition.ValidationPlan.Scope.FieldId!, out JsonElement scopeValue)
                    && scopeValue.ValueKind == JsonValueKind.String;
                if (!valid) return;
                scope = scopeValue.GetString();
                try { _ = BaseSubjectId.Create(scope!, BaseSubjectIdKind.OrdinalString, 256); }
                catch { valid = false; scope = null; }
            }
        }

        private static BaseAtomicReadIntervalEvidence ExactInterval(string path, byte[] key) => new()
        {
            LogicalAccessPathId = path,
            CanonicalLowerBound = key.ToImmutableArray(),
            LowerInclusive = true,
            CanonicalUpperBound = key.ToImmutableArray(),
            UpperInclusive = true,
        };

        private static RecordPayload PatchDelta(BaseAtomicMutationPlanItem item)
        {
            Dictionary<string, JsonElement> proposed = item.ProposedPayload?.Fields
                ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (string name in item.ChangedFields)
                if (proposed.TryGetValue(name, out JsonElement value)) fields[name] = value.Clone();
            return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
        }

        private async ValueTask<(string StoreId, long RestoreEpoch, long SchemaGeneration)> ReadAuthorityAsync(CancellationToken cancellationToken)
        {
            await using SqliteCommand command = _connection.CreateCommand(); command.Transaction = _transaction;
            command.CommandTimeout = CommandTimeoutSeconds();
            command.CommandText = $"SELECT COALESCE((SELECT CAST(value AS INTEGER) FROM {_owner._names.ProviderState} WHERE key='restore_epoch'),0);";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            return (_owner._options.StoreId, reader.GetInt64(0), Volatile.Read(ref _owner._schemaGeneration));
        }

        private static OperationResult<T> SubjectFailure<T>(string code, OperationStatus status = OperationStatus.StoreError, ErrorCategory category = ErrorCategory.Store) => new()
        { Status = status, Error = new BaseError { Code = code, Message = "The subject mutation provider operation failed.", Category = category } };

        private static OperationResult SubjectFailure(string code, OperationStatus status = OperationStatus.StoreError, ErrorCategory category = ErrorCategory.Store) => new()
        { Status = status, Error = new BaseError { Code = code, Message = "The subject mutation provider operation failed.", Category = category } };

        private static OperationResult SubjectSuccess() => new() { Status = OperationStatus.Ok };

        private async ValueTask<OperationResult<BaseCapturedAtomicExecution>> SelectCoreAsync(
            BaseAtomicSelectionRequest request,
            BaseAtomicMutationIntent intent,
            BaseAtomicMutationExecutionLimits limits,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            BaseAtomicMutationAuthorityRequirement requiredAuthority = intent.Authority;
            BaseCollectionGenerationRequirement? collectionAuthority = requiredAuthority.Collections.SingleOrDefault(
                value => string.Equals(value.CollectionId, request.Collection.Id, StringComparison.Ordinal));
            if (collectionAuthority is null || string.IsNullOrWhiteSpace(requiredAuthority.StoreInstanceId)
                || requiredAuthority.RestoreEpoch < 0
                || limits.MaximumSelectedRecords < 1
                || limits.MaximumSelectedBytes < 1
                || limits.MaximumReadIntervals < 1
                || limits.MaximumTransientBytes < 1
                || limits.MaximumUniqueConstraintChecks < 1
                || request.CanonicalRecordCodecVersion < 1)
            {
                return SelectionFailure(OperationStatus.ValidationFailed,
                    "base.provider.selection.authorityInvalid", ErrorCategory.Validation);
            }
            _selectionUniqueCheckLimit = limits.MaximumUniqueConstraintChecks;
            _selectionTransientLimit = limits.MaximumTransientBytes;
            _attributionTransientBytes = 0;
            _selectionRetainedBytes = 0;

            string actualStoreInstanceId;
            long actualRestoreEpoch;
            long actualSchemaGeneration;
            long actualCollectionGeneration;
            await using (SqliteCommand authority = _connection.CreateCommand())
            {
                authority.Transaction = _transaction;
                authority.CommandText = $"SELECT COALESCE((SELECT CAST(value AS INTEGER) FROM {_owner._names.ProviderState} WHERE key='restore_epoch'),0), COALESCE((SELECT purge_generation FROM {_owner._names.Collections} WHERE collection_id=$collection),0);";
                authority.Parameters.AddWithValue("$collection", request.Collection.Id);
                await using SqliteDataReader authorityReader = await authority.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await authorityReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    return SelectionFailure(OperationStatus.Conflict, "base.provider.selection.authorityChanged", ErrorCategory.Conflict);
                actualStoreInstanceId = _owner._options.StoreId;
                actualRestoreEpoch = authorityReader.GetInt64(0);
                actualSchemaGeneration = Volatile.Read(ref _owner._schemaGeneration);
                actualCollectionGeneration = authorityReader.GetInt64(1);
                if (!string.Equals(actualStoreInstanceId, requiredAuthority.StoreInstanceId, StringComparison.Ordinal)
                    || actualRestoreEpoch != requiredAuthority.RestoreEpoch
                    || actualSchemaGeneration != requiredAuthority.SchemaGeneration
                    || actualCollectionGeneration != collectionAuthority.CollectionGeneration)
                    return SelectionFailure(OperationStatus.Conflict, "base.provider.selection.authorityChanged", ErrorCategory.Conflict);
            }

            SqlitePhysicalModel.CollectionModel physical = _owner._physical.Collection(request.Collection.Id);
            SqliteQueryPlan plan = new SqliteQueryPlanner(_owner._options, physical).Plan(request.Query);
            if (!plan.Supported)
                return SelectionFailure(OperationStatus.Unsupported,
                    "base.provider.selection.queryUnsupported", ErrorCategory.Unsupported);
            int requested = request.Query.Page?.Limit ?? limits.MaximumSelectedRecords;
            if (requested < 1 || requested > limits.MaximumSelectedRecords)
                return SelectionFailure(OperationStatus.ValidationFailed,
                    "base.provider.selection.limitExceeded", ErrorCategory.Validation);

            await using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = plan.SelectSql;
            command.CommandTimeout = CommandTimeoutSeconds();
            plan.Bind(command);
            var records = System.Collections.Immutable.ImmutableArray.CreateBuilder<BaseOwnedSelectedRecord>(requested);
            long bytes = 0;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (records.Count < requested && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                RecordEnvelope envelope = physical.ReadEnvelope(reader, _owner._options.StoreId, out _);
                BaseOwnedSelectedRecord owned = BaseOwnedSelectedRecord.Freeze(
                    envelope, records.Count, request.CanonicalRecordCodecVersion);
                bytes = checked(bytes + owned.CanonicalBytes);
                if (bytes > limits.MaximumSelectedBytes || bytes > limits.MaximumTransientBytes)
                    return SelectionFailure(OperationStatus.ValidationFailed,
                        "base.provider.selection.limitExceeded", ErrorCategory.Validation);
                records.Add(owned);
            }
            byte[] boundary = records.Count == 0 ? [] : BaseSelectionOrderTuple.Encode(records[^1].MaterializeOwned(), request.Query.Sort!);
            _selectionRetainedBytes = checked(bytes + boundary.LongLength);
            if (_selectionRetainedBytes > _selectionTransientLimit)
                return SelectionFailure(OperationStatus.ValidationFailed,
                    "base.provider.selection.limitExceeded", ErrorCategory.Validation);
            int selectedCount = records.Count;
            ImmutableArray<BaseOwnedSelectedRecord> selectedRecords = records.MoveToImmutable();
            var interval = new BaseAtomicReadIntervalEvidence
            {
                LogicalAccessPathId = $"collection:{request.Collection.Id}",
                CanonicalLowerBound = ImmutableArray<byte>.Empty,
                LowerInclusive = true,
                CanonicalUpperBound = boundary.ToImmutableArray(),
                UpperInclusive = true,
            };
            ImmutableArray<byte> transactionEvidence = BitConverter.GetBytes(_transactionStarted).ToImmutableArray();
            string selectionCaptureDigest = Convert.ToHexStringLower(SHA256.HashData(
                selectedRecords.SelectMany(static record => record.CopyCanonicalBytes()).Concat(boundary).ToArray()));
            _capturedMutation = new BaseCapturedAtomicExecution
            {
                Kind = BaseAtomicMutationExecutionKind.SelectionMutation,
                IntentDigest = new string(intent.IntentDigest.AsSpan()),
                CaptureDigest = selectionCaptureDigest,
                Authority = new BaseAtomicMutationAuthorityEvidence
                {
                    ApplicationId = requiredAuthority.ApplicationId,
                    StoreInstanceId = actualStoreInstanceId,
                    RestoreEpoch = actualRestoreEpoch,
                    SchemaGeneration = actualSchemaGeneration,
                    Collections = [new BaseCollectionGenerationRequirement
                    {
                        CollectionId = request.Collection.Id,
                        CollectionGeneration = collectionAuthority.CollectionGeneration,
                    }],
                    Isolation = BaseAtomicSelectionIsolationClass.WriteOwningSerializable,
                    TransactionEvidenceToken = transactionEvidence,
                },
                Selection = new BaseCapturedSelectionEvidence
                {
                    Records = selectedRecords,
                    CanonicalOrderBoundary = boundary.ToImmutableArray(),
                    Accounting = new BaseAtomicSelectionAccounting
                    {
                        SelectedRecords = selectedCount, SelectedBytes = bytes,
                        ReadIntervals = 1, EvidenceBytes = boundary.LongLength,
                    },
                },
                Items = selectedRecords.Select((record, index) => new BaseCapturedMutationItem
                {
                    Ordinal = index,
                    CollectionId = request.Collection.Id,
                    RecordId = new RecordId(record.RecordId),
                    Disposition = BaseCapturedMutationDisposition.Update,
                    Current = record.MaterializeOwned(),
                    RelationTargets = [],
                }).ToImmutableArray(),
                ModuleRecords = [], ModuleRelationTargets = [], Generations = [],
                ActivationGuard = _capturedActivationGuard,
                ReadIntervals = [interval],
                Accounting = new BaseAtomicCaptureAccounting
                {
                    Records = selectedCount,
                    RelationTargetReads = 0, GenerationReads = 0,
                    SelectedBytes = bytes,
                    RelationTargetBytes = 0, GenerationBytes = 0,
                    ReadIntervals = 1,
                    EvidenceBytes = boundary.LongLength,
                    TransientBytes = _selectionRetainedBytes,
                },
            };
            return OperationResults.Ok(_capturedMutation);
        }

        private static OperationResult<BaseCapturedAtomicExecution> SelectionFailure(
            OperationStatus status, string code, ErrorCategory category) => new()
        {
            Status = status,
            Error = new BaseError { Code = code, Message = "The provider selection failed.", Category = category },
        };

        /// <summary>Executes the get async operation.</summary>
        public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
            CollectionDefinition collection,
            RecordId id,
            OperationContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                BaseOperationKind.Get,
                cancellationToken,
                async token =>
                {
                    if (SqliteValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
                        return collectionError;
                    if (_owner.ValidateRegisteredCollection<RecordEnvelope>(collection.Id) is { } registrationError)
                        return registrationError;
                    if (SqliteValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
                        return idError;

                    var record = await _owner.ReadAsync(
                        _connection,
                        collection.Id,
                        id.Value,
                        token,
                        _transaction,
                        CommandTimeoutSeconds()).ConfigureAwait(false);
                    return record is null
                        ? SqliteResultFactory.NotFound<RecordEnvelope>(id.Value)
                        : SqliteResultFactory.WithRevision(OperationResults.Ok(record), record.Metadata);
                });

        /// <summary>Executes the create async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> CreateAsync(
            CollectionDefinition collection,
            RecordCreateRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                BaseOperationKind.Create,
                cancellationToken,
                token => CreateCoreAsync(collection, request, context, token));

        /// <summary>Executes the patch async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> PatchAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordPatchRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                BaseOperationKind.Patch,
                cancellationToken,
                token => MutateCoreAsync(
                    collection,
                    id,
                    request.ExpectedRevision,
                    request.Patch,
                    replace: false,
                    BaseCommittedRecordMutationKind.Patch,
                    context,
                    token));

        /// <summary>Executes the replace async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> ReplaceAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordReplaceRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                BaseOperationKind.Replace,
                cancellationToken,
                token => MutateCoreAsync(
                    collection,
                    id,
                    request.ExpectedRevision,
                    request.Payload,
                    replace: true,
                    BaseCommittedRecordMutationKind.Replace,
                    context,
                    token));

        /// <summary>Executes the delete async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> DeleteAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordDeleteRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                BaseOperationKind.Delete,
                cancellationToken,
                token => DeleteCoreAsync(collection, id, request, context, token));

        public ValueTask<OperationResult<long>> AdvancePurgeGenerationAsync(
            CollectionDefinition collection,
            long? expectedGeneration,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                BaseOperationKind.Purge,
                cancellationToken,
                token => AdvancePurgeGenerationCoreAsync(collection, expectedGeneration, token));

        /// <inheritdoc />
        public async ValueTask<OperationResult> ApplyMutationProjectionsAsync(
            BaseAtomicMutationProjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (ISqliteAtomicMutationProjection contributor in _owner._mutationProjectionContributors)
            {
                var context = new SqliteAtomicProjectionContext(_owner, _connection, _transaction, (ISqliteAtomicMutationProjectionCatalog)contributor);
                BaseAtomicMutationProjectionRequest isolated = BaseAtomicMutationProjectionFactory.Clone(request);
                OperationResult result = await contributor.ApplyAsync(context, isolated, cancellationToken).ConfigureAwait(false);
                if (!result.Status.IsSuccess()) return result;
            }
            return OperationResults.NoContent();
        }

        private sealed class SqliteAtomicProjectionContext(SqliteRecordStore owner, SqliteConnection connection, SqliteTransaction transaction, ISqliteAtomicMutationProjectionCatalog catalog) : ISqliteAtomicProjectionContext
        {
            public long SchemaGeneration => owner.VectorSchemaGeneration;

            public async ValueTask<OperationResult<int>> ExecuteAsync(string statementId, System.Collections.Immutable.ImmutableArray<SqliteProjectionValue> parameters, CancellationToken cancellationToken = default)
            {
                SqliteProjectionStatement? statement = catalog.Statements.SingleOrDefault(item => string.Equals(item.Id, statementId, StringComparison.Ordinal));
                if (statement is null || parameters.IsDefault || parameters.Select(static item => item.Name).Distinct(StringComparer.Ordinal).Count() != parameters.Length || !statement.ParameterNames.SequenceEqual(parameters.Select(static item => item.Name), StringComparer.Ordinal))
                    return OperationResults.ValidationFailed<int>(new BaseError { Code = "base.sqlite.projection.invalid", Message = "The SQLite projection statement is invalid.", Category = ErrorCategory.Validation });
                await using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = statement.Sql;
                command.CommandTimeout = owner.VectorCommandTimeoutSeconds;
                foreach (SqliteProjectionValue parameter in parameters) command.Parameters.AddWithValue("$" + parameter.Name, parameter.Value);
                int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return affected <= statement.MaximumAffectedRows ? OperationResults.Ok(affected) : OperationResults.StoreError<int>(new BaseError { Code = "base.sqlite.projection.affectedRowsExceeded", Message = "The SQLite projection exceeded its affected-row bound.", Category = ErrorCategory.Store });
            }
        }

        /// <summary>Executes the close async operation.</summary>
        public async ValueTask CloseAsync()
        {
            if (Interlocked.CompareExchange(
                    ref _lifetimeState,
                    Closing,
                    Active) != Active)
            {
                return;
            }

            await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            Volatile.Write(ref _lifetimeState, Closed);
            _operationGate.Release();
        }

        private async ValueTask<OperationResult<T>> ExecuteAsync<T>(
            BaseOperationKind operation,
            CancellationToken cancellationToken,
            Func<CancellationToken, ValueTask<OperationResult<T>>> action)
        {
            if (Volatile.Read(ref _lifetimeState) != Active)
                return SessionClosed<T>();

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _lifetimeState) != Active)
                    return SessionClosed<T>();

                await _owner._sessionOperations
                    .BeforeExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return SqliteResultFactory.StoreError<T>(
                    BaseMutationErrorCodes.TransactionTimeout,
                    "SQLite mutation processing was cancelled or exceeded its bounded lifetime.");
            }
            catch (SqliteException ex) when (
                operation == BaseOperationKind.Create
                && ex.SqliteExtendedErrorCode == 1555)
            {
                return SqliteResultFactory.DuplicateId<T>("record");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return await ConstraintFailureAsync<T>(ex, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex) when (IsTransactionConflict(ex))
            {
                return OperationResults.Conflict<T>(TransactionConflict());
            }
            catch (SqliteException ex)
            {
                return _owner.MapSqlite<T>(operation, ex);
            }
            catch (InvalidOperationException ex)
            {
                return _owner.MapSchemaFailure<T>(ex);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async ValueTask<OperationResult<T>> ConstraintFailureAsync<T>(SqliteException exception, CancellationToken cancellationToken)
        {
            string code = exception.SqliteExtendedErrorCode switch
            {
                1555 => "base.constraint.recordIdentity",
                2067 => await AttributeUniqueAsync(cancellationToken).ConfigureAwait(false),
                787 => "base.constraint.attributionUnavailable",
                275 => "base.constraint.attributionUnavailable",
                1299 => "base.constraint.attributionUnavailable",
                _ => "base.constraint.attributionUnavailable",
            };
            return OperationResults.Conflict<T>(new BaseError
            {
                Code = code,
                Message = "The mutation violated an authoritative logical constraint.",
                Category = ErrorCategory.Conflict,
            });
        }

        private async ValueTask<string> AttributeUniqueAsync(CancellationToken cancellationToken)
        {
            if (_constraintCollection is null || _constraintPayload is null || _constraintRecordId is null)
                return "base.constraint.attributionUnavailable";
            Dictionary<string, System.Text.Json.JsonElement> values = SqliteRecordSerializer.NormalizeObjectPayload(_constraintPayload).Fields ?? [];
            int matches = 0;
            foreach (SqlitePhysicalModel.IndexModel index in _constraintCollection.Indexes
                .Where(static candidate => candidate.Definition.Unique || candidate.Definition.Kind == IndexKind.Unique))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_selectionUniqueCheckLimit <= 0 || _uniqueChecks >= _selectionUniqueCheckLimit)
                    return "base.constraint.attributionUnavailable";
                _uniqueChecks = checked(_uniqueChecks + 1);
                if (index.Definition.Predicate is not null || index.Definition.NativePredicate is not null
                    || index.Parts.Length == 0 || index.Definition.Parts!.Any(static part => part.Kind != IndexPartKind.Field || part.Collation is not null || part.Expression is not null))
                    continue;
                await using SqliteCommand probe = _connection.CreateCommand();
                probe.Transaction = _transaction;
                probe.CommandTimeout = CommandTimeoutSeconds();
                var predicates = new List<string>(index.Parts.Length);
                bool complete = true;
                for (int part = 0; part < index.Parts.Length; part++)
                {
                    SqlitePhysicalModel.FieldModel field = index.Parts[part];
                    if (!values.TryGetValue(field.Definition.WireName, out System.Text.Json.JsonElement value)) { complete = false; break; }
                    string parameter = "$u" + part.ToString(CultureInfo.InvariantCulture);
                    predicates.Add(field.Column + " IS " + parameter);
                    object encoded = field.Encode(value);
                    _attributionTransientBytes = checked(_attributionTransientBytes + System.Text.Encoding.UTF8.GetByteCount(parameter) + EncodedSize(encoded));
                    if (checked(_selectionRetainedBytes + _attributionTransientBytes) > _selectionTransientLimit) return "base.constraint.attributionUnavailable";
                    probe.Parameters.AddWithValue(parameter, encoded);
                }
                if (!complete) continue;
                probe.CommandText = $"SELECT 1 FROM {_constraintCollection.Table} WHERE {string.Join(" AND ", predicates)} AND record_id <> $record LIMIT 1;";
                probe.Parameters.AddWithValue("$record", _constraintRecordId);
                _attributionTransientBytes = checked(_attributionTransientBytes + System.Text.Encoding.UTF8.GetByteCount(probe.CommandText) + System.Text.Encoding.UTF8.GetByteCount(_constraintRecordId));
                if (checked(_selectionRetainedBytes + _attributionTransientBytes) > _selectionTransientLimit) return "base.constraint.attributionUnavailable";
                if (await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null) matches++;
            }
            return matches == 1 ? "base.constraint.unique" : "base.constraint.attributionUnavailable";
        }

        private static long EncodedSize(object value) => value switch
        {
            byte[] bytes => bytes.LongLength,
            string text => System.Text.Encoding.UTF8.GetByteCount(text),
            _ => sizeof(long),
        };

        private async ValueTask<OperationResult<RecordMutationSessionResult>> CreateCoreAsync(
            CollectionDefinition collection,
            RecordCreateRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken,
            bool runtimeAssignedId = false)
        {
            ArgumentNullException.ThrowIfNull(collection);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(context);

            if (MutationModeFailure(collection, BaseOperationKind.Create) is { } modeError)
                return modeError;

            if (SqliteValidation.ValidateCollectionId<RecordMutationSessionResult>(collection.Id) is { } collectionError)
                return collectionError;
            if (_owner.ValidateRegisteredCollection<RecordMutationSessionResult>(collection.Id) is { } registrationError)
                return registrationError;
            var id = request.RequestedId ?? new RecordId(NextRecordId());
            if (request.RequestedId is not null && !runtimeAssignedId && !_owner._options.AllowClientRequestedIds)
                return SqliteResultFactory.Unsupported<RecordMutationSessionResult>(
                    SqliteErrorCodes.RequestedIdUnsupported,
                    "Client-requested ids are disabled for this SQLite store.");
            if (SqliteValidation.ValidateRecordId<RecordMutationSessionResult>(id.Value) is { } idError)
                return idError;
            if (_owner.ValidatePayload<RecordMutationSessionResult>(request.Payload) is { } payloadError)
                return payloadError;

            var now = Now(context.Operation);
            RecordPayload normalizedPayload = SqliteRecordSerializer.NormalizeObjectPayload(request.Payload);
            SqlitePhysicalModel.CollectionModel physical = _owner._physical.Collection(collection.Id);
            long appendPosition = await AllocateAppendPositionAsync(collection.Id, cancellationToken).ConfigureAwait(false);
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = $"INSERT INTO {physical.Table}(record_id, revision, created_at, updated_at, append_position{physical.PayloadColumnClause}) VALUES ($id, 1, $created, $updated, $appendPosition{physical.PayloadParameterClause});";
            command.CommandTimeout = CommandTimeoutSeconds();
            command.Parameters.AddWithValue("$id", id.Value);
            command.Parameters.AddWithValue("$created", now.ToString("O"));
            command.Parameters.AddWithValue("$updated", now.ToString("O"));
            command.Parameters.AddWithValue("$appendPosition", appendPosition);
            physical.AddPayloadParameters(command, normalizedPayload, includeExtensions: true);
            SetConstraintProbe(physical, normalizedPayload, id.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            ClearConstraintProbe();
            _uniqueChecks = checked(_uniqueChecks + physical.Indexes.Count(index => index.Definition.Unique));
            await SyncRelationsAsync(collection.Id, id.Value, normalizedPayload, cancellationToken).ConfigureAwait(false);

            var metadata = SqliteRecordMapper.Metadata(1, now, now, _owner._options.StoreId);
            var after = new RecordEnvelope
            {
                CollectionId = collection.Id,
                Id = id,
                Payload = normalizedPayload,
                Metadata = metadata
            };
            var journal = await _owner.AppendMutationJournalAsync(
                _connection,
                _transaction,
                context.EventId,
                BaseOperationKind.Create,
                context.Operation,
                collection.Id,
                id,
                collection.Visibility?.Visibility ?? VisibilityLevel.Public,
                null,
                after,
                cancellationToken,
                CommandTimeoutSeconds()).ConfigureAwait(false);
            await SetLatestMutationPositionAsync(physical, id, journal.Position, cancellationToken).ConfigureAwait(false);
            var value = SessionResult(
                collection,
                context,
                BaseCommittedRecordMutationKind.Create,
                before: null,
                after,
                delete: null,
                journal);
            return SqliteResultFactory.WithRevision(
                OperationResults.Created(value),
                metadata);
        }

        private async ValueTask<long> AllocateAppendPositionAsync(
            string collectionId,
            CancellationToken cancellationToken)
        {
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = $"UPDATE {_owner._names.Collections} SET next_append_position = next_append_position + 1 WHERE collection_id = $collection RETURNING next_append_position;";
            command.CommandTimeout = CommandTimeoutSeconds();
            command.Parameters.AddWithValue("$collection", collectionId);
            object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is long position && position > 0
                ? position
                : throw new InvalidOperationException("SQLite collection append-position state is unavailable.");
        }

        private async ValueTask<OperationResult<long>> AdvancePurgeGenerationCoreAsync(
            CollectionDefinition collection,
            long? expectedGeneration,
            CancellationToken cancellationToken)
        {
            if (collection.MutationMode != BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)
                return SqliteResultFactory.Unsupported<long>(
                    BaseCollectionErrorCodes.PurgeUnsupported,
                    "The collection does not support administrative purge.");
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = $"UPDATE {_owner._names.Collections} SET purge_generation = purge_generation + 1 WHERE collection_id = $collection AND purge_generation < 9223372036854775807 AND ($expected IS NULL OR purge_generation = $expected) RETURNING purge_generation;";
            command.CommandTimeout = CommandTimeoutSeconds();
            command.Parameters.AddWithValue("$collection", collection.Id);
            command.Parameters.AddWithValue("$expected", expectedGeneration is { } expected ? expected : DBNull.Value);
            object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is long generation)
                return OperationResults.Ok(generation);

            await using var current = _connection.CreateCommand();
            current.Transaction = _transaction;
            current.CommandText = $"SELECT purge_generation FROM {_owner._names.Collections} WHERE collection_id = $collection;";
            current.CommandTimeout = CommandTimeoutSeconds();
            current.Parameters.AddWithValue("$collection", collection.Id);
            object? observed = await current.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (observed is long actual && expectedGeneration is { } expectedValue && actual != expectedValue)
                return OperationResults.Conflict<long>(new BaseError
                {
                    Code = BaseCollectionErrorCodes.PurgeGenerationConflict,
                    Message = "The purge generation did not match.",
                    Category = ErrorCategory.Conflict
                });
            return SqliteResultFactory.StoreError<long>(
                BaseCollectionErrorCodes.PurgeFailed,
                "The purge generation could not be advanced.");
        }

        private static OperationResult<RecordMutationSessionResult>? MutationModeFailure(
            CollectionDefinition collection,
            BaseOperationKind operation)
        {
            bool allowed = operation switch
            {
                BaseOperationKind.Create => collection.MutationMode is
                    BaseCollectionMutationMode.Mutable or
                    BaseCollectionMutationMode.AppendOnly or
                    BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge,
                BaseOperationKind.Patch or BaseOperationKind.Replace =>
                    collection.MutationMode == BaseCollectionMutationMode.Mutable,
                BaseOperationKind.Delete =>
                    collection.MutationMode == BaseCollectionMutationMode.Mutable,
                BaseOperationKind.Purge =>
                    collection.MutationMode == BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge,
                _ => false
            };
            if (allowed) return null;
            string code = !Enum.IsDefined(collection.MutationMode)
                ? BaseCollectionErrorCodes.MutationModeInvalid
                : collection.MutationMode == BaseCollectionMutationMode.ReadOnly
                    ? BaseCollectionErrorCodes.ReadOnlyMutationForbidden
                    : operation is BaseOperationKind.Patch or BaseOperationKind.Replace
                        ? BaseCollectionErrorCodes.AppendOnlyUpdateForbidden
                        : operation == BaseOperationKind.Delete
                            ? BaseCollectionErrorCodes.AppendOnlyDeleteForbidden
                            : BaseCollectionErrorCodes.PurgeUnsupported;
            return SqliteResultFactory.Unsupported<RecordMutationSessionResult>(
                code,
                "The collection mutation mode does not permit this operation.");
        }

        private async ValueTask<OperationResult<RecordMutationSessionResult>> MutateCoreAsync(
            CollectionDefinition collection,
            RecordId id,
            RevisionToken? expectedRevision,
            RecordPayload payload,
            bool replace,
            BaseCommittedRecordMutationKind committedOperation,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken)
        {
            if (MutationModeFailure(collection, committedOperation == BaseCommittedRecordMutationKind.Patch ? BaseOperationKind.Patch : BaseOperationKind.Replace) is { } modeError)
                return modeError;
            if (SqliteValidation.ValidateCollectionId<RecordMutationSessionResult>(collection.Id) is { } collectionError)
                return collectionError;
            if (_owner.ValidateRegisteredCollection<RecordMutationSessionResult>(collection.Id) is { } registrationError)
                return registrationError;
            if (SqliteValidation.ValidateRecordId<RecordMutationSessionResult>(id.Value) is { } idError)
                return idError;
            if (_owner.ValidatePayload<RecordMutationSessionResult>(payload) is { } payloadError)
                return payloadError;
            if (!SqliteRecordMapper.TryParseRevision(expectedRevision, out var expected))
                return SqliteResultFactory.Validation<RecordMutationSessionResult>(
                    SqliteErrorCodes.InvalidRevisionToken,
                    "Expected revision must use the sqlite:{integer} format.",
                    "expectedRevision");

            var before = await _owner.ReadAsync(
                _connection,
                collection.Id,
                id.Value,
                cancellationToken,
                _transaction,
                CommandTimeoutSeconds()).ConfigureAwait(false);
            if (before is null)
                return SqliteResultFactory.NotFound<RecordMutationSessionResult>(id.Value);

            var currentRevision = long.Parse(
                before.Metadata.Revision!.Value.Value["sqlite:".Length..],
                CultureInfo.InvariantCulture);
            if (expectedRevision is not null && expected != currentRevision)
                return SqliteResultFactory.RevisionConflict<RecordMutationSessionResult>(
                    expectedRevision.Value,
                    before.Metadata.Revision,
                    id.Value);

            var nextRevision = currentRevision + 1;
            var now = Now(context.Operation);
            var nextPayload = replace
                ? SqliteRecordSerializer.Clone(payload)
                : SqliteRecordSerializer.Merge(before.Payload, payload);
            SqlitePhysicalModel.CollectionModel physical = _owner._physical.Collection(collection.Id);
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = $"UPDATE {physical.Table} SET revision = $revision, updated_at = $updated{physical.PayloadAssignmentClause} WHERE record_id = $id;";
            command.CommandTimeout = CommandTimeoutSeconds();
            command.Parameters.AddWithValue("$revision", nextRevision);
            command.Parameters.AddWithValue("$updated", now.ToString("O"));
            physical.AddPayloadParameters(command, nextPayload, includeExtensions: true);
            command.Parameters.AddWithValue("$id", id.Value);
            SetConstraintProbe(physical, nextPayload, id.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            ClearConstraintProbe();
            _uniqueChecks = checked(_uniqueChecks + physical.Indexes.Count(index => index.Definition.Unique));
            await SyncRelationsAsync(collection.Id, id.Value, nextPayload, cancellationToken).ConfigureAwait(false);

            var metadata = SqliteRecordMapper.Metadata(
                nextRevision,
                before.Metadata.CreatedAt!.Value,
                now,
                _owner._options.StoreId);
            var after = before with
            {
                Payload = SqliteRecordSerializer.Clone(nextPayload),
                Metadata = metadata
            };
            var physicalOperation = committedOperation == BaseCommittedRecordMutationKind.Patch
                ? BaseOperationKind.Patch
                : BaseOperationKind.Replace;
            var journal = await _owner.AppendMutationJournalAsync(
                _connection,
                _transaction,
                context.EventId,
                physicalOperation,
                context.Operation,
                collection.Id,
                id,
                collection.Visibility?.Visibility ?? VisibilityLevel.Public,
                before,
                after,
                cancellationToken,
                CommandTimeoutSeconds()).ConfigureAwait(false);
            await SetLatestMutationPositionAsync(physical, id, journal.Position, cancellationToken).ConfigureAwait(false);
            var value = SessionResult(
                collection,
                context,
                committedOperation,
                before,
                after,
                delete: null,
                journal);
            return SqliteResultFactory.WithRevision(
                OperationResults.Updated(value),
                metadata);
        }

        private async ValueTask<OperationResult<RecordMutationSessionResult>> DeleteCoreAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordDeleteRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken)
        {
            if (MutationModeFailure(collection, context.Operation.Operation) is { } modeError)
                return modeError;
            if (SqliteValidation.ValidateCollectionId<RecordMutationSessionResult>(collection.Id) is { } collectionError)
                return collectionError;
            if (_owner.ValidateRegisteredCollection<RecordMutationSessionResult>(collection.Id) is { } registrationError)
                return registrationError;
            if (SqliteValidation.ValidateRecordId<RecordMutationSessionResult>(id.Value) is { } idError)
                return idError;
            if (!SqliteRecordMapper.TryParseRevision(request.ExpectedRevision, out var expected))
                return SqliteResultFactory.Validation<RecordMutationSessionResult>(
                    SqliteErrorCodes.InvalidRevisionToken,
                    "Expected revision must use the sqlite:{integer} format.",
                    "expectedRevision");

            var before = await _owner.ReadAsync(
                _connection,
                collection.Id,
                id.Value,
                cancellationToken,
                _transaction,
                CommandTimeoutSeconds()).ConfigureAwait(false);
            if (before is null)
                return SqliteResultFactory.NotFound<RecordMutationSessionResult>(id.Value);
            if (request.ExpectedRevision is not null
                && expected.ToString(CultureInfo.InvariantCulture)
                    != before.Metadata.Revision?.Value["sqlite:".Length..])
            {
                return SqliteResultFactory.RevisionConflict<RecordMutationSessionResult>(
                    request.ExpectedRevision.Value,
                    before.Metadata.Revision,
                    id.Value);
            }

            if (await HasRestrictedIncomingReferenceAsync(collection.Id, id.Value, cancellationToken).ConfigureAwait(false))
                return OperationResults.Conflict<RecordMutationSessionResult>(new BaseError
                {
                    Code = "base.relation.deleteRestricted",
                    Message = "The record is referenced by a restricted relation.",
                    Category = ErrorCategory.Conflict
                });

            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            SqlitePhysicalModel.CollectionModel physical = _owner._physical.Collection(collection.Id);
            command.CommandText = $"DELETE FROM {physical.Table} WHERE record_id = $id;";
            command.CommandTimeout = CommandTimeoutSeconds();
            command.Parameters.AddWithValue("$id", id.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await DeleteOutgoingRelationsAsync(collection.Id, id.Value, cancellationToken).ConfigureAwait(false);

            var delete = new DeleteResult
            {
                Id = id,
                Deleted = true,
                Previous = request.ReturnPrevious ? before : null
            };
            var journal = await _owner.AppendMutationJournalAsync(
                _connection,
                _transaction,
                context.EventId,
                BaseOperationKind.Delete,
                context.Operation,
                collection.Id,
                id,
                collection.Visibility?.Visibility ?? VisibilityLevel.Public,
                before,
                null,
                cancellationToken,
                CommandTimeoutSeconds()).ConfigureAwait(false);
            var value = SessionResult(
                collection,
                context,
                BaseCommittedRecordMutationKind.Delete,
                before,
                after: null,
                delete,
                journal);
            return OperationResults.Deleted(value);
        }

        private async ValueTask SyncRelationsAsync(string collectionId, string sourceRecordId, RecordPayload payload, CancellationToken cancellationToken)
        {
            Dictionary<string, System.Text.Json.JsonElement> fields = SqliteRecordSerializer.NormalizeObjectPayload(payload).Fields ?? [];
            foreach (SqlitePhysicalModel.RelationModel relation in _owner._physical.RelationsFrom(collectionId))
            {
                await using (var remove = _connection.CreateCommand())
                {
                    remove.Transaction = _transaction;
                    remove.CommandTimeout = CommandTimeoutSeconds();
                    remove.CommandText = $"DELETE FROM {relation.Table} WHERE source_record_id = $source;";
                    remove.Parameters.AddWithValue("$source", sourceRecordId);
                    await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!fields.TryGetValue(relation.SourceFieldName, out var value) || value.ValueKind is System.Text.Json.JsonValueKind.Null)
                    continue;
                if (value.ValueKind != System.Text.Json.JsonValueKind.Array)
                    throw new InvalidOperationException("SQLite relation payload shape is invalid.");

                var ordinal = 0;
                foreach (var target in value.EnumerateArray())
                {
                    _relationChecks = checked(_relationChecks + 1);
                    if (target.ValueKind != System.Text.Json.JsonValueKind.String || string.IsNullOrWhiteSpace(target.GetString()))
                        throw new InvalidOperationException("SQLite relation payload shape is invalid.");
                    await using var insert = _connection.CreateCommand();
                    insert.Transaction = _transaction;
                    insert.CommandTimeout = CommandTimeoutSeconds();
                    insert.CommandText = $"INSERT INTO {relation.Table}(source_record_id, target_record_id, ordinal) VALUES ($source, $target, $ordinal);";
                    insert.Parameters.AddWithValue("$source", sourceRecordId);
                    insert.Parameters.AddWithValue("$target", target.GetString()!);
                    insert.Parameters.AddWithValue("$ordinal", ordinal++);
                    await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private void SetConstraintProbe(SqlitePhysicalModel.CollectionModel collection, RecordPayload payload, string recordId)
        {
            _constraintCollection = collection;
            _constraintPayload = SqliteRecordSerializer.Clone(payload);
            _constraintRecordId = recordId;
        }

        private void ClearConstraintProbe()
        {
            _constraintCollection = null;
            _constraintPayload = null;
            _constraintRecordId = null;
        }

        private async ValueTask SetLatestMutationPositionAsync(
            SqlitePhysicalModel.CollectionModel physical,
            RecordId recordId,
            BaseMutationJournalPosition position,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = $"UPDATE {physical.Table} SET latest_mutation_position=$position WHERE record_id=$record;";
            command.CommandTimeout = CommandTimeoutSeconds();
            command.Parameters.AddWithValue("$position", position.Value);
            command.Parameters.AddWithValue("$record", recordId.Value);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The authoritative mutation position could not be recorded.");
        }

        private async ValueTask<bool> HasRestrictedIncomingReferenceAsync(string collectionId, string targetRecordId, CancellationToken cancellationToken)
        {
            foreach (SqlitePhysicalModel.RelationModel relation in _owner._physical.RelationsTo(collectionId)
                .Where(static relation => relation.Definition.DeleteBehavior == BaseRelationDeleteBehavior.Restrict))
            {
                _relationChecks = checked(_relationChecks + 1);
                await using var command = _connection.CreateCommand();
                command.Transaction = _transaction;
                command.CommandTimeout = CommandTimeoutSeconds();
                command.CommandText = $"SELECT 1 FROM {relation.Table} WHERE target_record_id = $target LIMIT 1;";
                command.Parameters.AddWithValue("$target", targetRecordId);
                if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null) return true;
            }
            return false;
        }

        private async ValueTask DeleteOutgoingRelationsAsync(string collectionId, string sourceRecordId, CancellationToken cancellationToken)
        {
            foreach (SqlitePhysicalModel.RelationModel relation in _owner._physical.RelationsFrom(collectionId))
            {
                await using var command = _connection.CreateCommand();
                command.Transaction = _transaction;
                command.CommandTimeout = CommandTimeoutSeconds();
                command.CommandText = $"DELETE FROM {relation.Table} WHERE source_record_id = $source;";
                command.Parameters.AddWithValue("$source", sourceRecordId);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static RecordMutationSessionResult SessionResult(
            CollectionDefinition collection,
            RecordMutationSessionContext context,
            BaseCommittedRecordMutationKind committedOperation,
            RecordEnvelope? before,
            RecordEnvelope? after,
            DeleteResult? delete,
            MutationJournalAppendResult committedEvent)
        {
            RecordUpsertOutcome? upsertOutcome =
                context.RequestedOperation == BaseRecordMutationKind.Upsert
                ? committedOperation == BaseCommittedRecordMutationKind.Create
                    ? RecordUpsertOutcome.Created
                    : RecordUpsertOutcome.Updated
                : null;
            var mutation = new BaseRecordMutationFact
            {
                ItemId = context.ItemId,
                RequestedOperation = context.RequestedOperation,
                CommittedOperation = committedOperation,
                UpsertOutcome = upsertOutcome,
                Collection = collection,
                Event = committedEvent.Event,
                JournalPosition = committedEvent.Position,
                Before = before,
                After = after,
                Delete = delete,
                ChangedFields = context.ChangedFields
            };
            return new RecordMutationSessionResult
            {
                Mutation = mutation,
                Record = after,
                Delete = delete
            };
        }

        private static OperationResult<T> SessionClosed<T>() =>
            SqliteResultFactory.StoreError<T>(
                SqliteErrorCodes.SessionClosed,
                "SQLite mutation session is no longer active.");

        private int CommandTimeoutSeconds() =>
            _owner.TransactionCommandTimeoutSeconds(
                _transactionStarted,
                _transactionTimeout);
    }
}
