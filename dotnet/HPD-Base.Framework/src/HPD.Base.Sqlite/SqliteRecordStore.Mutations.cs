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
        private BaseCapturedAtomicMutationAuthority? _capturedMutation;
        private BasePreparedAtomicMutation? _preparedMutation;
        private BaseAtomicMutationPlan? _preparedPlan;
        private BaseProvisionalAppliedAtomicMutation? _appliedProvisional;
        private Dictionary<int, BaseSubjectIncarnation>? _preparedLifecycleIncarnations;
        private Dictionary<int, SqliteModuleGenerationKey>? _capturedModuleGenerationKeys;
        private BaseModuleMutationCaptureExtension? _capturedModuleExtension;
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

        public ValueTask<OperationResult<BaseCapturedAtomicMutationAuthority>> CaptureAtomicMutationAuthorityAsync(
            BaseAtomicMutationCaptureRequest request,
            CancellationToken cancellationToken = default) => ExecuteAsync(BaseOperationKind.Query, cancellationToken, async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            BaseAtomicMutationIntent intent = request.Intent;
            BaseAtomicMutationExecutionLimits limits = request.Limits;
            if (request.Kind == BaseAtomicMutationExecutionKind.SelectionMutation)
            {
                if (request.Selection is null || request.Module is not null || !intent.Items.IsDefaultOrEmpty)
                    return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid);
                OperationResult<BaseCapturedAtomicMutationAuthority> selected = await SelectCoreAsync(
                    request.Selection.Selection, intent, limits, token).ConfigureAwait(false);
                return selected;
            }
            if (request.Kind == BaseAtomicMutationExecutionKind.ModuleMutation)
                return await CaptureModuleAuthorityAsync(request, token).ConfigureAwait(false);
            if (_capturedMutation is not null || request.Kind != BaseAtomicMutationExecutionKind.RecordMutations
                || request.Selection is not null || request.Module is not null
                || intent.Items.IsDefaultOrEmpty || intent.Items.Length > limits.MaximumItems)
                return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid);
            (string storeId, long restoreEpoch, long schemaGeneration) = await ReadAuthorityAsync(token).ConfigureAwait(false);
            if (!string.Equals(storeId, intent.Authority.StoreInstanceId, StringComparison.Ordinal) || restoreEpoch != intent.Authority.RestoreEpoch || schemaGeneration != intent.Authority.SchemaGeneration)
                return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);
            foreach (BaseCollectionGenerationRequirement requirement in intent.Authority.Collections)
            {
                await using SqliteCommand generation = _connection.CreateCommand();
                generation.Transaction = _transaction;
                generation.CommandText = $"SELECT purge_generation FROM {_owner._names.Collections} WHERE collection_id=$collection;";
                generation.Parameters.AddWithValue("$collection", requirement.CollectionId);
                object? actual = await generation.ExecuteScalarAsync(token).ConfigureAwait(false);
                if (actual is null or DBNull || Convert.ToInt64(actual, CultureInfo.InvariantCulture) != requirement.CollectionGeneration)
                    return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);
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
                if (item.Ordinal != index) return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid);
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
            ImmutableArray<BaseCapturedMutationItem> ownedItems = items.ToImmutable();
            OperationResult<ImmutableArray<BaseCapturedSubjectLifecycleConsumerProjection>> lifecycleProjectionResult =
                await CaptureLifecycleConsumerProjectionsAsync(request.LifecycleConsumerProjections, digest, intervals, token).ConfigureAwait(false);
            if (!lifecycleProjectionResult.IsSuccess() || lifecycleProjectionResult.Value.IsDefault)
                return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid);
            OperationResult<ImmutableArray<BaseCapturedSubjectRetirementProjection>> retirementResult=await CaptureRetirementAsync(request.SubjectRetirement,intent,request.Module,digest,intervals,token).ConfigureAwait(false);
            if(!retirementResult.IsSuccess()||retirementResult.Value.IsDefault)return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
            ImmutableArray<BaseAtomicReadIntervalEvidence> ownedIntervals = intervals.ToImmutable();
            long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(ownedIntervals);
            long transient = BaseSubjectCanonicalRetainedWork.MeasureCapture(intent, ownedItems, ownedIntervals, lifecycleProjectionResult.Value);
            if (selectedBytes > limits.MaximumSelectedBytes || evidenceBytes > limits.MaximumEvidenceBytes || transient > limits.MaximumTransientBytes || intervals.Count > limits.MaximumReadIntervals)
                return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);
            _capturedMutation = new BaseCapturedAtomicMutationAuthority
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
                LifecycleConsumerProjections = lifecycleProjectionResult.Value,
                SubjectRetirement=retirementResult.Value,
                ReadIntervals = ownedIntervals,
                Accounting = new BaseAtomicCaptureAccounting
                {
                    Records = checked(intent.Items.Length + intent.Items.Sum(static item => item.RelationTargets.Length)),
                    RelationTargetReads = intent.Items.Sum(static item => item.RelationTargets.Length), GenerationReads = 0,
                    SelectedBytes = selectedBytes, ReadIntervals = intervals.Count,
                    RelationTargetBytes = 0, GenerationBytes = 0,
                    EvidenceBytes = evidenceBytes, TransientBytes = transient,
                    RetirementBarrierReads=0,RetirementAcknowledgementReads=0,RetirementProjections=request.SubjectRetirement?.Projections.Length??0,RetirementPublications=0,RetirementEvidenceBytes=0,RetirementPublicationBytes=0,
                },
            };
            return OperationResults.Ok(_capturedMutation);
        });

        private async ValueTask<OperationResult<ImmutableArray<BaseCapturedSubjectRetirementProjection>>> CaptureRetirementAsync(BaseSubjectRetirementCaptureExtension? extension,BaseAtomicMutationIntent intent,BaseModuleMutationCaptureExtension? module,IncrementalHash digest,ImmutableArray<BaseAtomicReadIntervalEvidence>.Builder intervals,CancellationToken cancellationToken)
        {
            if(extension is null)return OperationResults.Ok(ImmutableArray<BaseCapturedSubjectRetirementProjection>.Empty);
            var result=ImmutableArray.CreateBuilder<BaseCapturedSubjectRetirementProjection>(extension.Projections.Length);
            foreach(BaseSubjectRetirementProjectionCaptureRequest request in extension.Projections)
            {
                BaseAtomicMutationIntentItem? item=intent.Items.SingleOrDefault(value=>value.Ordinal==request.SourceMutationOrdinal);BaseModuleRecordCaptureRequest? moduleRecord=module?.Records.SingleOrDefault(value=>value.Ordinal==request.SourceMutationOrdinal);CollectionDefinition? sourceCollection=item?.Collection??moduleRecord?.Collection;RecordId? sourceRecord=item?.RecordId??moduleRecord?.RecordId;if(sourceCollection is null||sourceRecord is null)return SubjectFailure<ImmutableArray<BaseCapturedSubjectRetirementProjection>>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                await using SqliteCommand command=_connection.CreateCommand();command.Transaction=_transaction;command.CommandTimeout=CommandTimeoutSeconds();command.CommandText=$"SELECT c.contract_checksum,c.authority_epoch,l.subject_id,l.incarnation,l.lifecycle_state,l.subject_sequence,l.scope_kind,l.scope_index_digest,b.tombstone_sequence,b.required_consumer_set_checksum,b.created_at,b.deadline_at,b.state,b.generation,b.barrier_checksum,l.protected_scope_value FROM {_owner._names.SubjectLifetimes} l JOIN {_owner._names.SubjectContracts} c ON c.contract_id=l.contract_id AND c.contract_version=l.contract_version LEFT JOIN {_owner._names.SubjectRetirementBarriers} b ON b.scope_kind=l.scope_kind AND b.scope_index_digest=l.scope_index_digest AND b.contract_id=l.contract_id AND b.contract_version=l.contract_version AND b.subject_id=l.subject_id AND b.authority_epoch=c.authority_epoch AND b.incarnation=l.incarnation WHERE l.contract_id=$contract AND l.contract_version=$version AND l.private_collection_id=$collection AND l.private_record_id=$record;";command.Parameters.AddWithValue("$contract",request.ContractId);command.Parameters.AddWithValue("$version",request.ContractVersion);command.Parameters.AddWithValue("$collection",sourceCollection.Id);command.Parameters.AddWithValue("$record",sourceRecord.Value.Value);
                await using SqliteDataReader reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);if(!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)||reader.GetString(0)!=request.ContractChecksum)return SubjectFailure<ImmutableArray<BaseCapturedSubjectRetirementProjection>>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                BaseExportedSubjectDefinition? definition=_owner._options.ExportedSubjects.SingleOrDefault(value=>value.Id==request.ContractId&&value.Version==request.ContractVersion);if(definition is null)return SubjectFailure<ImmutableArray<BaseCapturedSubjectRetirementProjection>>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);var subjectId=BaseSubjectId.Create(reader.GetString(2),definition.SubjectIdKind,definition.MaximumSubjectIdUtf8Bytes);var epoch=new BaseSubjectAuthorityEpoch((byte[])reader.GetValue(1));var incarnation=new BaseSubjectIncarnation((byte[])reader.GetValue(3));BaseSubjectRetirementBarrier? barrier=reader.IsDBNull(8)?null:new(){ContractId=request.ContractId,ContractVersion=request.ContractVersion,SubjectId=subjectId,AuthorityEpoch=epoch,Incarnation=incarnation,TombstoneSequence=reader.GetInt64(8),RequiredConsumerSetChecksum=reader.GetString(9),CreatedAtUtc=DateTimeOffset.Parse(reader.GetString(10),CultureInfo.InvariantCulture),DeadlineUtc=DateTimeOffset.Parse(reader.GetString(11),CultureInfo.InvariantCulture),State=(BaseSubjectRetirementBarrierState)reader.GetInt32(12),Generation=reader.GetInt64(13),BarrierChecksum=reader.GetString(14)};
                var protectedScope=new BaseProtectedSubjectScope{Kind=(BaseSubjectScopeKind)reader.GetInt32(6),IndexDigest=(byte[])reader.GetValue(7),ProtectedCanonicalValue=(byte[])reader.GetValue(15)};var currentState=(BaseSubjectLifecycleState)reader.GetInt32(4);long currentSequence=reader.GetInt64(5);
                result.Add(new(){SourceMutationOrdinal=request.SourceMutationOrdinal,ContractId=request.ContractId,ContractVersion=request.ContractVersion,ContractChecksum=request.ContractChecksum,RetirementPolicyChecksum=request.RetirementPolicyChecksum,AcceptedConsumerSetChecksum=request.AcceptedConsumerSetChecksum,SubjectId=subjectId,ProtectedScope=protectedScope,AuthorityEpoch=epoch,Incarnation=incarnation,CurrentState=currentState,CurrentSubjectSequence=currentSequence,CurrentBarrier=barrier});byte[] key=Encoding.UTF8.GetBytes($"{(int)protectedScope.Kind}\n{Convert.ToHexString(protectedScope.IndexDigest)}\n{request.ContractId}\n{request.ContractVersion}\n{subjectId.Value}\n{Convert.ToHexString(epoch.ToArray())}\n{Convert.ToHexString(incarnation.ToArray())}");digest.AppendData(key);intervals.Add(ExactInterval($"subjectRetirement:{request.ContractId}:barrier",key));
            }
            return OperationResults.Ok(result.MoveToImmutable());
        }

        private async ValueTask<OperationResult<ImmutableArray<BaseCapturedSubjectLifecycleConsumerProjection>>> CaptureLifecycleConsumerProjectionsAsync(
            ImmutableArray<BaseSubjectLifecycleConsumerProjectionCaptureRequest> requests,
            IncrementalHash digest,
            ImmutableArray<BaseAtomicReadIntervalEvidence>.Builder intervals,
            CancellationToken cancellationToken)
        {
            var captured = ImmutableArray.CreateBuilder<BaseCapturedSubjectLifecycleConsumerProjection>(requests.Length);
            for (int index = 0; index < requests.Length; index++)
            {
                BaseSubjectLifecycleConsumerProjectionCaptureRequest request = requests[index];
                if (index > 0 && CompareLifecycleProjectionRequest(requests[index - 1], request) >= 0)
                    return SubjectFailure<ImmutableArray<BaseCapturedSubjectLifecycleConsumerProjection>>(BaseSubjectErrorCodes.ProviderContractInvalid);
                await using SqliteCommand command = _connection.CreateCommand();
                command.Transaction = _transaction;
                command.CommandText = $"SELECT consumer_checksum,contract_id,contract_version,projection_generation,published_graph_generation,state FROM {_owner._names.SubjectLifecycleConsumers} WHERE consumer_id=$id AND consumer_version=$version;";
                command.Parameters.AddWithValue("$id", request.ConsumerId);
                command.Parameters.AddWithValue("$version", request.ConsumerVersion);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    || reader.GetString(0) != request.ConsumerChecksum || reader.GetString(1) != request.ContractId
                    || reader.GetInt32(2) != request.ContractVersion || reader.GetInt64(3) < 1
                    || reader.GetInt64(4) < 1 || reader.GetInt32(5) != 0)
                    return SubjectFailure<ImmutableArray<BaseCapturedSubjectLifecycleConsumerProjection>>(BaseSubjectErrorCodes.ProviderContractInvalid);
                var value = new BaseCapturedSubjectLifecycleConsumerProjection
                {
                    ConsumerId = request.ConsumerId, ConsumerVersion = request.ConsumerVersion,
                    ConsumerChecksum = request.ConsumerChecksum, ContractId = request.ContractId,
                    ContractVersion = request.ContractVersion, ProjectionGeneration = reader.GetInt64(3),
                    PublishedGraphGeneration = reader.GetInt64(4),
                };
                captured.Add(value);
                digest.AppendData(System.Text.Encoding.UTF8.GetBytes($"\0lifecycle-consumer\0{value.ConsumerId}\0{value.ConsumerVersion}\0{value.ConsumerChecksum}\0{value.ContractId}\0{value.ContractVersion}\0{value.ProjectionGeneration}\0{value.PublishedGraphGeneration}\0"));
                byte[] key = System.Text.Encoding.UTF8.GetBytes($"{value.ConsumerId}\0{value.ConsumerVersion}");
                intervals.Add(new BaseAtomicReadIntervalEvidence
                {
                    LogicalAccessPathId = "subject-lifecycle:consumer-projection",
                    CanonicalLowerBound = key.ToImmutableArray(), LowerInclusive = true,
                    CanonicalUpperBound = key.ToImmutableArray(), UpperInclusive = true,
                });
            }
            return OperationResults.Ok(captured.MoveToImmutable());
        }

        private static int CompareLifecycleProjectionRequest(
            BaseSubjectLifecycleConsumerProjectionCaptureRequest left,
            BaseSubjectLifecycleConsumerProjectionCaptureRequest right)
        {
            int byId = string.CompareOrdinal(left.ConsumerId, right.ConsumerId);
            return byId != 0 ? byId : left.ConsumerVersion.CompareTo(right.ConsumerVersion);
        }

        private async ValueTask<OperationResult<BaseCapturedAtomicMutationAuthority>> CaptureModuleAuthorityAsync(
            BaseAtomicMutationCaptureRequest request,
            CancellationToken cancellationToken)
        {
            BaseAtomicMutationIntent intent = request.Intent;
            BaseModuleMutationCaptureExtension? module = request.Module;
            BaseAtomicMutationExecutionLimits limits = request.Limits;
            if (_capturedMutation is not null || module is null || request.Selection is not null || !intent.Items.IsDefaultOrEmpty
                || module.Records.Length > limits.MaximumRecordCaptures
                || module.RelationTargets.Length > limits.MaximumRelationTargetCaptures
                || module.Generations.Length > limits.MaximumGenerationReads)
                return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid);
            _capturedModuleExtension = module;

            (string storeId, long restoreEpoch, long schemaGeneration) = await ReadAuthorityAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(storeId, intent.Authority.StoreInstanceId, StringComparison.Ordinal)
                || restoreEpoch != intent.Authority.RestoreEpoch || schemaGeneration != intent.Authority.SchemaGeneration
                || !await CollectionAuthorityMatchesAsync(intent.Authority.Collections, module, cancellationToken).ConfigureAwait(false))
                return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);

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
                    return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid);
                RecordEnvelope? current = await _owner.ReadAsync(
                    _connection, capture.Collection.Id, capture.RecordId.Value, cancellationToken, _transaction, CommandTimeoutSeconds()).ConfigureAwait(false);
                if ((capture.Presence == BaseModuleCapturePresence.RequirePresent && current is null)
                    || (capture.Presence == BaseModuleCapturePresence.RequireMissing && current is not null))
                    return SubjectFailure<BaseCapturedAtomicMutationAuthority>("base.moduleMutation.captureConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
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
                    return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid);
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
                    return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid);
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
                    return SubjectFailure<BaseCapturedAtomicMutationAuthority>("base.moduleMutation.generationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
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
                return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);

            int readIntervalCount = intervals.Count;
            _capturedMutation = new BaseCapturedAtomicMutationAuthority
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
                Generations = generations.MoveToImmutable(), ReadIntervals = intervals.MoveToImmutable(),
                Accounting = new BaseAtomicCaptureAccounting
                {
                    Records = module.Records.Length, RelationTargetReads = module.RelationTargets.Length,
                    GenerationReads = module.Generations.Length, ReadIntervals = readIntervalCount,
                    SelectedBytes = selectedBytes, RelationTargetBytes = relationBytes, GenerationBytes = generationBytes,
                    EvidenceBytes = evidenceBytes, TransientBytes = transient,
                    RetirementBarrierReads=0,RetirementAcknowledgementReads=0,RetirementProjections=request.SubjectRetirement?.Projections.Length??0,RetirementPublications=0,RetirementEvidenceBytes=0,RetirementPublicationBytes=0,
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

        private static bool ModuleBindingsValid(BaseAtomicMutationPlan plan, BaseCapturedAtomicMutationAuthority captured)
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

        public ValueTask<OperationResult<BasePreparedAtomicMutation>> PrepareAtomicMutationAsync(
            BaseCapturedAtomicMutationAuthority captured, BaseAtomicMutationPlan plan,
            CancellationToken cancellationToken = default) => ExecuteAsync(BaseOperationKind.Query, cancellationToken, async token =>
        {
            token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(captured); ArgumentNullException.ThrowIfNull(plan);
            if (!ReferenceEquals(captured, _capturedMutation) || _preparedMutation is not null || plan.Kind != captured.Kind ||
                !string.Equals(plan.IntentDigest, captured.IntentDigest, StringComparison.Ordinal) ||
                !string.Equals(plan.CaptureDigest, captured.CaptureDigest, StringComparison.Ordinal) ||
                !LifecycleProjectionBindingsValid(plan, captured) ||
                (plan.Kind == BaseAtomicMutationExecutionKind.ModuleMutation
                    ? plan.Module is null || captured.Items.Length != 0
                    : plan.Items.Length != captured.Items.Length))
                return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
            var preparedGenerations = ImmutableArray.CreateBuilder<BasePreparedModuleGenerationEvidence>(captured.Generations.Length);
            if (plan.Kind == BaseAtomicMutationExecutionKind.ModuleMutation)
            {
                if (captured.Accounting.GenerationReads > plan.Limits.MaximumGenerationReads
                    || captured.Accounting.GenerationBytes > plan.Limits.MaximumGenerationBytes
                    || plan.Module!.Comparisons.Length > plan.Limits.MaximumGenerationComparisons
                    || plan.Module.Increments.Length > plan.Limits.MaximumGenerationIncrements)
                    return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);
                if (_capturedModuleGenerationKeys is null
                    || !ModuleBindingsValid(plan, captured)
                    || plan.Module!.Comparisons.Select(static value => value.CaptureOrdinal).Distinct().Count() != plan.Module.Comparisons.Length
                    || plan.Module.Increments.Select(static value => value.CaptureOrdinal).Distinct().Count() != plan.Module.Increments.Length)
                    return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
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
                        return SubjectFailure<BasePreparedAtomicMutation>("base.moduleMutation.generationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
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
                    return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);
                subjectAuthorities[$"{lifecycle.ContractId}\n{lifecycle.ContractVersion}"] = SubjectAuthority(lifecycle.ContractId, lifecycle.ContractVersion, contract);
                BaseExportedSubjectDefinition? definition = _owner._options.ExportedSubjects.FirstOrDefault(subject =>
                    string.Equals(subject.Id, lifecycle.ContractId, StringComparison.Ordinal) && subject.Version == lifecycle.ContractVersion);
                if (definition is null) return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                BaseOwnedSubjectScopeEvidence ownedScope = ScopeForItem(item, definition);
                string key = SubjectKey(ownedScope, lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId);
                intervals.Add(ExactInterval($"subject:{lifecycle.ContractId}:contract", System.Text.Encoding.UTF8.GetBytes($"{lifecycle.ContractId}\n{lifecycle.ContractVersion}")));
                intervals.Add(ExactInterval($"subject:{lifecycle.ContractId}:lifetime", System.Text.Encoding.UTF8.GetBytes(key)));
                intervals.Add(ExactInterval($"subject:{lifecycle.ContractId}:record", System.Text.Encoding.UTF8.GetBytes(lifecycle.SubjectId.Value)));
                if (!lifetimes.TryGetValue(key, out SqlitePreparedSubjectLifetime? lifetime))
                {
                    lifetime = await ReadSubjectLifetimeAsync(ownedScope, lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId, token).ConfigureAwait(false);
                    if (lifetime is null && lifecycle.Kind != BaseSubjectLifecycleMutationKind.Create)
                    {
                        BaseOwnedSubjectScopeEvidence originalScope = ScopeForCurrentItem(item, definition);
                        lifetime = await ReadSubjectLifetimeAsync(originalScope, lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId, token).ConfigureAwait(false);
                    }
                    authorityReads = checked(authorityReads + 1);
                    lifetimes[key] = lifetime;
                }
                switch (lifecycle.Kind)
                {
                    case BaseSubjectLifecycleMutationKind.Create:
                        if (lifetime is not null)
                            return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.TransactionConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
                        long priorGeneration = await ReadTerminalGenerationAsync(ownedScope, lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId, token).ConfigureAwait(false);
                        long generation;
                        try { generation = checked(priorGeneration + 1); }
                        catch (OverflowException) { return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.LifetimeGenerationExhausted, OperationStatus.Conflict, ErrorCategory.Conflict); }
                        lifetime = new SqlitePreparedSubjectLifetime(
                            lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId,
                            BaseSubjectIncarnation.Create(generation), generation, lifecycle.ResultingState, 1,
                            ownedScope, item.Collection.Id, item.RecordId,
                            item.Ordinal + 1L, item.Ordinal + 1L);
                        lifetimes[key] = lifetime;
                        lifecycleIncarnations[item.Ordinal] = lifetime.Incarnation;
                        break;
                    case BaseSubjectLifecycleMutationKind.Preserve:
                        if (lifetime is null) return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                        if (lifecycle.PublishFact)
                        {
                            long sequence;
                            try { sequence = checked(lifetime.SubjectSequence + 1); }
                            catch (OverflowException) { return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.SequenceExhausted, OperationStatus.Conflict, ErrorCategory.Conflict); }
                            lifetime = lifetime with { LifecycleState = lifecycle.ResultingState, SubjectSequence = sequence };
                            lifetimes[key] = lifetime;
                        }
                        lifecycleIncarnations[item.Ordinal] = lifetime.Incarnation;
                        break;
                    case BaseSubjectLifecycleMutationKind.Retire:
                        if (lifetime is null) return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                        lifetimes[key] = null;
                        lifetime = null;
                        break;
                    default:
                        return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                }
                RecordEnvelope? privateRecord = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Retire ? null : PlanRecord(item);
                ReadLogicalValues(definition, privateRecord, out bool? active, out string? scope, out bool logicalStateValid);
                if (privateRecord is not null && !logicalStateValid)
                    return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                if (lifetime is not null)
                {
                    ownedScope = new BaseOwnedSubjectScopeEvidence { Kind = definition.Scope, Value = scope };
                    lifetime = lifetime with { Scope = ownedScope };
                    lifetimes[key] = lifetime;
                }
                overlays[key] = new BasePreparedSubjectOverlayEvidence
                {
                    ContractId = lifecycle.ContractId, ContractVersion = lifecycle.ContractVersion,
                    SubjectId = lifecycle.SubjectId, Exists = lifetime is not null && privateRecord is not null,
                    Incarnation = lifetime?.Incarnation, Active = active,
                    Scope = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Retire ? ownedScope.Value : scope,
                    ProtectedScope = ProtectScope(ownedScope),
                    LifecycleState = lifetime?.LifecycleState,
                    SubjectSequence = lifetime?.SubjectSequence,
                };
                if (lifecycle.Kind == BaseSubjectLifecycleMutationKind.Preserve)
                {
                    BaseOwnedSubjectScopeEvidence originalScope = ScopeForCurrentItem(item, definition);
                    string originalKey = SubjectKey(originalScope, lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId);
                    if (!string.Equals(originalKey, key, StringComparison.Ordinal) && plan.SubjectValidations.Any(validation =>
                        validation.ValidationPlanId == definition.ValidationPlan.Id && validation.ValidationPlanVersion == definition.ValidationPlan.Version &&
                        validation.Reference.SubjectId.Equals(lifecycle.SubjectId) && validation.Scope.Kind == originalScope.Kind &&
                        string.Equals(validation.Scope.Value, originalScope.Value, StringComparison.Ordinal)))
                    {
                        lifetimes[originalKey] = null;
                        intervals.Add(ExactInterval($"subject:{lifecycle.ContractId}:lifetime", System.Text.Encoding.UTF8.GetBytes(originalKey)));
                        overlays[originalKey] = new BasePreparedSubjectOverlayEvidence
                        {
                            ContractId = lifecycle.ContractId, ContractVersion = lifecycle.ContractVersion,
                            SubjectId = lifecycle.SubjectId, Exists = false, Incarnation = null, Active = null,
                            Scope = originalScope.Value, ProtectedScope = ProtectScope(originalScope),
                            LifecycleState = null, SubjectSequence = null,
                        };
                    }
                }
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
                string key = definition is null ? string.Empty : SubjectKey(validation.Scope, definition.Id, definition.Version, validation.Reference.SubjectId);
                if (valid)
                {
                    contract = await ReadSubjectContractAsync(definition!.Id, definition.Version, token).ConfigureAwait(false);
                    authorityReads = checked(authorityReads + 1);
                    if (!lifetimes.TryGetValue(key, out lifetime))
                    {
                        lifetime = await ReadSubjectLifetimeAsync(validation.Scope, definition.Id, definition.Version, validation.Reference.SubjectId, token).ConfigureAwait(false);
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
                        return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
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
                        Incarnation = lifetime?.Incarnation, Active = active, Scope = definition.Scope == BaseSubjectScopeKind.Global ? null : validation.Scope.Value,
                        ProtectedScope = ProtectScope(validation.Scope),
                        LifecycleState = lifetime?.LifecycleState,
                        SubjectSequence = lifetime?.SubjectSequence,
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
                + BaseSubjectCanonicalRetainedWork.MeasureIntegerDictionary(lifecycleIncarnations, static _ => 24L));
            int intervalCount = intervals.Count;
            if (authorityReads > plan.Limits.MaximumAuthorityReads || intervalCount > plan.Limits.MaximumReadIntervals
                || evidenceBytes > plan.Limits.MaximumEvidenceBytes || transient > plan.Limits.MaximumTransientBytes)
                return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);
            BaseSubjectRetirementPreparedEvidence? preparedRetirement = null;
            if (plan.SubjectRetirement is { } retirementPlan)
            {
                long publicationPosition;
                await using (SqliteCommand position = _connection.CreateCommand())
                {
                    position.Transaction = _transaction;
                    position.CommandTimeout = CommandTimeoutSeconds();
                    position.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_owner._names.ProviderState} WHERE key='subject_retirement_position';";
                    object? raw = await position.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (raw is null or DBNull)
                        return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                    publicationPosition = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                }
                var retirementItems = ImmutableArray.CreateBuilder<BaseSubjectRetirementPreparedEvidenceItem>(retirementPlan.Items.Length);
                foreach (BaseSubjectRetirementProjectionPlanItem projection in retirementPlan.Items)
                {
                    BaseProtectedSubjectScope protectedScope = ProtectScope(projection.Scope);
                    await using SqliteCommand exists = _connection.CreateCommand();
                    exists.Transaction = _transaction;
                    exists.CommandTimeout = CommandTimeoutSeconds();
                    exists.CommandText = $"SELECT 1 FROM {_owner._names.SubjectRetirementBarriers} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation;";
                    AddProtectedScope(exists, protectedScope);
                    exists.Parameters.AddWithValue("$contract", projection.ContractId);
                    exists.Parameters.AddWithValue("$version", projection.ContractVersion);
                    exists.Parameters.AddWithValue("$subject", projection.SubjectId.Value);
                    exists.Parameters.Add("$epoch", SqliteType.Blob).Value = projection.AuthorityEpoch.ToArray();
                    exists.Parameters.Add("$incarnation", SqliteType.Blob).Value = projection.Incarnation.ToArray();
                    if (await exists.ExecuteScalarAsync(token).ConfigureAwait(false) is not null)
                        return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                    var barrier = new BaseSubjectRetirementBarrier
                    {
                        ContractId = projection.ContractId, ContractVersion = projection.ContractVersion,
                        SubjectId = projection.SubjectId, AuthorityEpoch = projection.AuthorityEpoch,
                        Incarnation = projection.Incarnation, TombstoneSequence = projection.TombstoneSequence,
                        RequiredConsumerSetChecksum = projection.AcceptedConsumerSetChecksum,
                        CreatedAtUtc = projection.TombstonedAtUtc, DeadlineUtc = projection.DeadlineUtc,
                        State = BaseSubjectRetirementBarrierState.Pending, Generation = 1, BarrierChecksum = string.Empty,
                    };
                    barrier = barrier with { BarrierChecksum = BaseSubjectRetirementRegistry.BarrierChecksum(barrier, []) };
                    retirementItems.Add(new BaseSubjectRetirementPreparedEvidenceItem
                    {
                        ProjectionOrdinal = projection.ProjectionOrdinal, Previous = null, Resulting = barrier,
                        ProtectedScope = protectedScope, PublicationPosition = checked(publicationPosition + projection.ProjectionOrdinal + 1),
                    });
                }
                preparedRetirement = new BaseSubjectRetirementPreparedEvidence
                {
                    Items = retirementItems.MoveToImmutable(), PlanChecksum = retirementPlan.PlanChecksum,
                };
            }
            long retirementEvidenceBytes=BaseSubjectCanonicalRetainedWork.MeasureRetirementPreparedEvidence(preparedRetirement);
            evidenceBytes=checked(evidenceBytes+retirementEvidenceBytes);transient=checked(transient+retirementEvidenceBytes);
            var textGenerations = new Dictionary<(string CollectionId, string IndexId), long>();
            if (plan.Text is not null)
                foreach ((string collectionId, string indexId) in plan.Text.Facts.Select(static fact => (fact.CollectionId, fact.TextIndexId)).Distinct())
                {
                    await using SqliteCommand textGeneration = _connection.CreateCommand(); textGeneration.Transaction = _transaction;
                    textGeneration.CommandText = $"SELECT generation FROM {SqliteTextModel.StateTable} WHERE collection_id=$collection AND index_id=$index;";
                    textGeneration.Parameters.AddWithValue("$collection", collectionId); textGeneration.Parameters.AddWithValue("$index", indexId);
                    object? observed = await textGeneration.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (observed is not long generation || generation <= 0) return SubjectFailure<BasePreparedAtomicMutation>(BaseTextErrorCodes.IndexUnavailable, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
                    textGenerations.Add((collectionId, indexId), generation);
                }
            ImmutableArray<BasePreparedTextIndexEvidence> preparedTextIndexes = BaseTextAtomicMutationContract.Indexes(plan.Text, (collectionId, indexId) => textGenerations[(collectionId, indexId)]);
            BasePreparedTextMutationEvidence? preparedText = BaseTextAtomicMutationContract.Prepare(plan.Text, preparedTextIndexes);
            long textEvidenceBytes = preparedText?.EvidenceBytes ?? 0;
            evidenceBytes = checked(evidenceBytes + textEvidenceBytes); transient = checked(transient + textEvidenceBytes);
            int retirementReads=preparedRetirement?.Items.Length??0;
            if(retirementReads>plan.Limits.MaximumRetirementBarrierReads||retirementReads>plan.Limits.MaximumRetirementProjections
                ||retirementEvidenceBytes>plan.Limits.MaximumRetirementEvidenceBytes||evidenceBytes>plan.Limits.MaximumEvidenceBytes||transient>plan.Limits.MaximumTransientBytes)
                return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.BudgetExceeded,OperationStatus.ValidationFailed,ErrorCategory.Validation);
            _preparedPlan = plan;
            _preparedLifecycleIncarnations = lifecycleIncarnations;
            _preparedMutation = new BasePreparedAtomicMutation
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
                SubjectOverlay = overlays.Values.OrderBy(static value => value.ContractId, StringComparer.Ordinal).ThenBy(static value => value.ContractVersion).ThenBy(static value => value.SubjectId.Value, StringComparer.Ordinal).ToImmutableArray(),
                SubjectValidations = validationEvidence.MoveToImmutable(),
                ReadIntervals = intervals.ToImmutable(),
                SubjectRetirement = preparedRetirement,
                Text = preparedText,
                Accounting = new BasePreparedAtomicMutationAccounting
                {
                    AuthorityReads = authorityReads, GenerationReads = captured.Generations.Length,
                    GenerationComparisons = plan.Module?.Comparisons.Length ?? 0,
                    GenerationIncrements = plan.Module?.Increments.Length ?? 0,
                    ReadIntervals = intervalCount,
                    SelectedBytes = captured.Accounting.SelectedBytes, GenerationBytes = captured.Accounting.GenerationBytes, EvidenceBytes = evidenceBytes,
                    TransientBytes = transient,
                    RetirementBarrierReads=retirementReads,RetirementAcknowledgementReads=0,RetirementProjections=plan.SubjectRetirement?.Items.Length??0,RetirementPublications=0,RetirementEvidenceBytes=retirementEvidenceBytes,RetirementPublicationBytes=0,
                },
            };
            return OperationResults.Ok(_preparedMutation);
        });

        private static bool LifecycleProjectionBindingsValid(BaseAtomicMutationPlan plan, BaseCapturedAtomicMutationAuthority captured)
        {
            var expected = captured.LifecycleConsumerProjections.ToDictionary(
                static value => (value.ConsumerId, value.ConsumerVersion));
            foreach (BaseSubjectLifecycleMembershipPlanItem membership in plan.Items
                .SelectMany(static item => item.SubjectLifecycle?.Memberships ?? []))
            {
                if (!expected.TryGetValue((membership.ConsumerId, membership.ConsumerVersion), out BaseCapturedSubjectLifecycleConsumerProjection? projection)
                    || projection.ConsumerChecksum != membership.ConsumerChecksum
                    || projection.ProjectionGeneration != membership.ProjectionGeneration)
                    return false;
            }
            return true;
        }

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

        private static long CanonicalPlanRetainedBytes(BaseAtomicMutationPlan plan) =>
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

        public ValueTask<OperationResult<BaseProvisionalAppliedAtomicMutation>> ApplyPreparedAtomicMutationAsync(
            BasePreparedAtomicMutation prepared, CancellationToken cancellationToken = default) => ExecuteAsync(BaseOperationKind.Patch, cancellationToken, async token =>
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(prepared, _preparedMutation) || _preparedPlan is null)
                return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
            BaseAtomicMutationPlan plan = _preparedPlan;
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
                    return new OperationResult<BaseProvisionalAppliedAtomicMutation> { Status = mutation.Status, Error = mutation.Error };
                BaseSubjectLifecycleCommitEvidence? committedLifecycle = null;
                if (item.SubjectLifecycle is { } lifecycle)
                {
                    BasePreparedSubjectOverlayEvidence? overlay = prepared.SubjectOverlay.FirstOrDefault(candidate =>
                        string.Equals(candidate.ContractId, lifecycle.ContractId, StringComparison.Ordinal)
                        && candidate.ContractVersion == lifecycle.ContractVersion
                        && candidate.SubjectId.Equals(lifecycle.SubjectId));
                    if (overlay is null)
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                    OperationResult<BaseSubjectLifecycleCommitEvidence> lifecycleResult = await ApplySubjectLifecycleAsync(
                        item,
                        lifecycle,
                        lifecycleIncarnations.TryGetValue(item.Ordinal, out BaseSubjectIncarnation incarnation)
                            ? incarnation
                            : null,
                        mutation.Value.Mutation.JournalPosition,
                        token).ConfigureAwait(false);
                    if (!lifecycleResult.IsSuccess() || lifecycleResult.Value is null)
                        return new OperationResult<BaseProvisionalAppliedAtomicMutation> { Status = lifecycleResult.Status, Error = lifecycleResult.Error };
                    committedLifecycle = lifecycleResult.Value;
                }
                BaseRecordMutationFact committedFact = mutation.Value.Mutation with
                {
                    SubjectLifecycle = committedLifecycle,
                };
                BaseOwnedMutationFact owned = BaseOwnedMutationFact.Freeze(committedFact, 1);
                facts.Add(owned);
                factBytes = checked(factBytes + owned.EncodedLength);
                writtenBytes = checked(writtenBytes + (mutation.Value.Record is null
                    ? System.Text.Encoding.UTF8.GetByteCount(item.RecordId.Value) + sizeof(long)
                    : JsonSerializer.SerializeToUtf8Bytes(mutation.Value.Record, HPDBaseJsonSerializerContext.Default.RecordEnvelope).LongLength));
            }
            BaseSubjectRetirementProvisionalEvidence? appliedRetirement = null;
            long retirementPublicationBytes = 0;
            if (prepared.SubjectRetirement is { } retirement)
            {
                if (plan.SubjectRetirement is not { } retirementPlan
                    || !string.Equals(retirement.PlanChecksum, retirementPlan.PlanChecksum, StringComparison.Ordinal)
                    || retirement.Items.Length != retirementPlan.Items.Length)
                    return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                long expectedPosition;
                await using (SqliteCommand readPosition = _connection.CreateCommand())
                {
                    readPosition.Transaction = _transaction;
                    readPosition.CommandTimeout = CommandTimeoutSeconds();
                    readPosition.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_owner._names.ProviderState} WHERE key='subject_retirement_position';";
                    object? raw = await readPosition.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (raw is null or DBNull)
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                    expectedPosition = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                }
                foreach (BaseSubjectRetirementPreparedEvidenceItem evidence in retirement.Items)
                {
                    BaseSubjectRetirementProjectionPlanItem? retirementProjection = retirementPlan.Items.SingleOrDefault(
                        value => value.ProjectionOrdinal == evidence.ProjectionOrdinal);
                    if (retirementProjection is null || evidence.Previous is not null
                        || evidence.PublicationPosition != checked(expectedPosition + 1)
                        || !RetirementProjectionMatches(retirementProjection, evidence))
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                    await using SqliteCommand insert = _connection.CreateCommand();
                    insert.Transaction = _transaction; insert.CommandTimeout = CommandTimeoutSeconds();
                    insert.CommandText = $"INSERT INTO {_owner._names.SubjectRetirementBarriers}(scope_kind,scope_index_digest,protected_scope_value,contract_id,contract_version,subject_id,authority_epoch,incarnation,tombstone_sequence,required_consumer_set_checksum,created_at,deadline_at,state,generation,barrier_checksum,policy_checksum) VALUES($scopeKind,$scopeDigest,$scopeValue,$contract,$version,$subject,$epoch,$incarnation,$sequence,$set,$created,$deadline,$state,$generation,$checksum,$policy);";
                    AddProtectedScope(insert, evidence.ProtectedScope);
                    insert.Parameters.AddWithValue("$contract", retirementProjection.ContractId); insert.Parameters.AddWithValue("$version", retirementProjection.ContractVersion);
                    insert.Parameters.AddWithValue("$subject", retirementProjection.SubjectId.Value); insert.Parameters.Add("$epoch", SqliteType.Blob).Value = retirementProjection.AuthorityEpoch.ToArray();
                    insert.Parameters.Add("$incarnation", SqliteType.Blob).Value = retirementProjection.Incarnation.ToArray(); insert.Parameters.AddWithValue("$sequence", retirementProjection.TombstoneSequence);
                    insert.Parameters.AddWithValue("$set", retirementProjection.AcceptedConsumerSetChecksum); insert.Parameters.AddWithValue("$created", retirementProjection.TombstonedAtUtc.ToString("O", CultureInfo.InvariantCulture));
                    insert.Parameters.AddWithValue("$deadline", retirementProjection.DeadlineUtc.ToString("O", CultureInfo.InvariantCulture)); insert.Parameters.AddWithValue("$state", (int)evidence.Resulting.State);
                    insert.Parameters.AddWithValue("$generation", evidence.Resulting.Generation); insert.Parameters.AddWithValue("$checksum", evidence.Resulting.BarrierChecksum);
                    insert.Parameters.AddWithValue("$policy", retirementProjection.RetirementPolicyChecksum);
                    if (await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                    await using SqliteCommand advance = _connection.CreateCommand();
                    advance.Transaction = _transaction; advance.CommandTimeout = CommandTimeoutSeconds();
                    advance.CommandText = $"UPDATE {_owner._names.ProviderState} SET value=$next WHERE key='subject_retirement_position' AND CAST(value AS INTEGER)=$expected;";
                    advance.Parameters.AddWithValue("$next", evidence.PublicationPosition); advance.Parameters.AddWithValue("$expected", expectedPosition);
                    if (await advance.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                    var retirementPublication = new BaseSubjectRetirementPublicationFact
                    {
                        Position = new(evidence.PublicationPosition), Kind = BaseSubjectRetirementPublicationKind.BarrierCreated,
                        Barrier = BarrierPublication(evidence.Resulting, 0, null),
                    };
                    await WriteRetirementPublicationAsync(evidence.ProtectedScope, retirementPublication, token).ConfigureAwait(false);
                    retirementPublicationBytes=checked(retirementPublicationBytes+BaseSubjectCanonicalRetainedWork.MeasureRetirementPublication(retirementPublication));
                    expectedPosition = evidence.PublicationPosition;
                }
                appliedRetirement = new BaseSubjectRetirementProvisionalEvidence { Items = retirement.Items, PlanChecksum = retirement.PlanChecksum };
                if(retirement.Items.Length>plan.Limits.MaximumRetirementPublications||retirementPublicationBytes>plan.Limits.MaximumRetirementPublicationBytes)
                    return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.BudgetExceeded,OperationStatus.ValidationFailed,ErrorCategory.Validation);
            }
            if (plan.Kind == BaseAtomicMutationExecutionKind.ModuleMutation)
            {
                if (_capturedModuleGenerationKeys is null || prepared.Generations.Length != _capturedModuleGenerationKeys.Count)
                    return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                foreach (BasePreparedModuleGenerationEvidence generation in prepared.Generations)
                {
                    if (!_capturedModuleGenerationKeys.TryGetValue(generation.CaptureOrdinal, out SqliteModuleGenerationKey? key))
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
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
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>("base.moduleMutation.generationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
                    writtenBytes = checked(writtenBytes + 8);
                }
                _capturedModuleGenerationKeys = null;
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
                    return new OperationResult<BaseProvisionalAppliedAtomicMutation> { Status = projected.Status, Error = projected.Error };
            }
            long journalBytes = materialized.Sum(static fact =>
                (long)JsonSerializer.SerializeToUtf8Bytes(fact, HPDBaseJsonSerializerContext.Default.BaseRecordMutationFact).LongLength);
            long transient = checked(prepared.Accounting.TransientBytes + writtenBytes + factBytes + journalBytes + _attributionTransientBytes);
            ImmutableArray<BaseModuleCommittedGeneration> generations = CommittedGenerations(prepared);
            var applied = new BaseProvisionalAppliedAtomicMutation
            {
                Kind = plan.Kind,
                PlanDigest = new string(plan.PlanDigest.AsSpan()),
                Authority = prepared.Authority with { },
                Facts = facts.MoveToImmutable(),
                Generations = generations,
                SubjectRetirement = appliedRetirement,
                Text = BaseTextAtomicMutationContract.Apply(plan.Text, materialized, prepared.Text?.Indexes ?? []),
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
                    RetirementBarrierReads=prepared.Accounting.RetirementBarrierReads,RetirementAcknowledgementReads=prepared.Accounting.RetirementAcknowledgementReads,RetirementProjections=prepared.Accounting.RetirementProjections,RetirementPublications=prepared.SubjectRetirement?.Items.Length??0,RetirementEvidenceBytes=prepared.Accounting.RetirementEvidenceBytes,RetirementPublicationBytes=retirementPublicationBytes,
                },
            };
            _appliedProvisional = applied;
            return OperationResults.Ok(applied);
        });

        private static bool RetirementProjectionMatches(
            BaseSubjectRetirementProjectionPlanItem projection,
            BaseSubjectRetirementPreparedEvidenceItem evidence)
        {
            BaseSubjectRetirementBarrier barrier = evidence.Resulting;
            return projection.ContractId == barrier.ContractId
                && projection.ContractVersion == barrier.ContractVersion
                && projection.SubjectId.Equals(barrier.SubjectId)
                && projection.AuthorityEpoch.Equals(barrier.AuthorityEpoch)
                && projection.Incarnation.Equals(barrier.Incarnation)
                && projection.TombstoneSequence == barrier.TombstoneSequence
                && projection.AcceptedConsumerSetChecksum == barrier.RequiredConsumerSetChecksum
                && projection.TombstonedAtUtc == barrier.CreatedAtUtc
                && projection.DeadlineUtc == barrier.DeadlineUtc
                && barrier.State == BaseSubjectRetirementBarrierState.Pending
                && barrier.Generation == 1
                && barrier.BarrierChecksum == BaseSubjectRetirementRegistry.BarrierChecksum(barrier, []);
        }

        internal bool ValidateCommitFinalization(AtomicMutationProcessingResult processing)
        {
            if (processing.Receipt.Kind != BaseAtomicReceiptResultKind.ModuleMutation)
                return processing.Finalization is null;
            BaseAtomicMutationCommitFinalization? finalization = processing.Finalization;
            BaseProvisionalAppliedAtomicMutation? applied = _appliedProvisional;
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

        private ImmutableArray<BaseModuleCommittedGeneration> CommittedGenerations(BasePreparedAtomicMutation prepared)
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

        private async ValueTask<OperationResult<BaseSubjectLifecycleCommitEvidence>> ApplySubjectLifecycleAsync(
            BaseAtomicMutationPlanItem item,
            BaseSubjectLifecyclePlanItem lifecycle,
            BaseSubjectIncarnation? preparedIncarnation,
            BaseMutationJournalPosition journalPosition,
            CancellationToken cancellationToken)
        {
            BaseExportedSubjectDefinition? definition = _owner._options.ExportedSubjects.FirstOrDefault(value => value.Id == lifecycle.ContractId && value.Version == lifecycle.ContractVersion);
            if (definition is null) return SubjectFailure<BaseSubjectLifecycleCommitEvidence>(BaseSubjectErrorCodes.ProviderContractInvalid);
            SqliteSubjectContractState? contract = await ReadSubjectContractAsync(lifecycle.ContractId, lifecycle.ContractVersion, cancellationToken).ConfigureAwait(false);
            if (contract is null) return SubjectFailure<BaseSubjectLifecycleCommitEvidence>(BaseSubjectErrorCodes.ProviderContractInvalid);
            RecordEnvelope? planned = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Retire ? null : PlanRecord(item);
            ReadLogicalValues(definition, planned, out _, out string? plannedScope, out bool scopeValid);
            if (planned is not null && !scopeValid) return SubjectFailure<BaseSubjectLifecycleCommitEvidence>(BaseSubjectErrorCodes.ProviderContractInvalid);
            BaseOwnedSubjectScopeEvidence requestedScope = ScopeForItem(item, definition);
            SqlitePreparedSubjectLifetime? previous = await ReadSubjectLifetimeAsync(requestedScope, lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId, cancellationToken).ConfigureAwait(false);
            if (previous is null && lifecycle.Kind != BaseSubjectLifecycleMutationKind.Create)
                previous = await ReadSubjectLifetimeAsync(ScopeForCurrentItem(item, definition), lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId, cancellationToken).ConfigureAwait(false);
            BaseOwnedSubjectScopeEvidence scope = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Preserve
                ? requestedScope
                : previous?.Scope ?? new BaseOwnedSubjectScopeEvidence { Kind = definition.Scope, Value = plannedScope };
            BaseProtectedSubjectScope protectedScope = ProtectScope(scope);
            long generation; long sequence; BaseSubjectIncarnation factIncarnation;
            await using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = _transaction; command.CommandTimeout = CommandTimeoutSeconds();
            command.Parameters.AddWithValue("$contract", lifecycle.ContractId); command.Parameters.AddWithValue("$version", lifecycle.ContractVersion); command.Parameters.AddWithValue("$subject", lifecycle.SubjectId.Value);
            switch (lifecycle.Kind)
            {
                case BaseSubjectLifecycleMutationKind.Create:
                    if (preparedIncarnation is not { } incarnation || journalPosition.Value <= 0)
                        return SubjectFailure<BaseSubjectLifecycleCommitEvidence>(BaseSubjectErrorCodes.ProviderContractInvalid);
                    generation = incarnation.LifetimeGeneration; sequence = 1; factIncarnation = incarnation;
                    command.CommandText = $"INSERT INTO {_owner._names.SubjectLifetimes}(contract_id,contract_version,subject_id,incarnation,lifetime_generation,lifecycle_state,subject_sequence,scope_kind,scope_index_digest,protected_scope_value,private_collection_id,private_record_id,created_journal_position,last_lifecycle_position) VALUES($contract,$version,$subject,$incarnation,$generation,$state,$sequence,$scopeKind,$scopeDigest,$scopeCiphertext,$collection,$record,$position,$position);";
                    command.Parameters.Add("$incarnation", SqliteType.Blob).Value = incarnation.ToArray();
                    command.Parameters.AddWithValue("$generation", generation); command.Parameters.AddWithValue("$state", (int)lifecycle.ResultingState); command.Parameters.AddWithValue("$sequence", sequence);
                    AddProtectedScope(command, protectedScope);
                    command.Parameters.AddWithValue("$collection", item.Collection.Id);
                    command.Parameters.AddWithValue("$record", item.RecordId.Value);
                    command.Parameters.AddWithValue("$position", journalPosition.Value);
                    break;
                case BaseSubjectLifecycleMutationKind.Preserve:
                    if (preparedIncarnation is not { } preservedIncarnation || previous is null)
                        return SubjectFailure<BaseSubjectLifecycleCommitEvidence>(BaseSubjectErrorCodes.ProviderContractInvalid);
                    generation = previous.LifetimeGeneration; sequence = lifecycle.PublishFact ? checked(previous.SubjectSequence + 1) : previous.SubjectSequence; factIncarnation = preservedIncarnation;
                    BaseProtectedSubjectScope previousProtectedScope = ProtectScope(previous.Scope);
                    command.CommandText = lifecycle.PublishFact
                        ? $"UPDATE {_owner._names.SubjectLifetimes} SET lifecycle_state=$state,subject_sequence=$sequence,last_lifecycle_position=$position,scope_kind=$scopeKind,scope_index_digest=$scopeDigest,protected_scope_value=$scopeCiphertext WHERE scope_kind=$oldScopeKind AND scope_index_digest=$oldScopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND incarnation=$incarnation AND subject_sequence=$previousSequence;"
                        : $"UPDATE {_owner._names.SubjectLifetimes} SET scope_kind=$scopeKind,scope_index_digest=$scopeDigest,protected_scope_value=$scopeCiphertext WHERE scope_kind=$oldScopeKind AND scope_index_digest=$oldScopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND incarnation=$incarnation AND subject_sequence=$previousSequence;";
                    command.Parameters.AddWithValue("$state", (int)lifecycle.ResultingState); command.Parameters.AddWithValue("$sequence", sequence); command.Parameters.AddWithValue("$position", journalPosition.Value);
                    AddProtectedScope(command, protectedScope);
                    command.Parameters.AddWithValue("$oldScopeKind", (int)previousProtectedScope.Kind);
                    command.Parameters.Add("$oldScopeDigest", SqliteType.Blob).Value = previousProtectedScope.IndexDigest;
                    command.Parameters.Add("$incarnation", SqliteType.Blob).Value = preservedIncarnation.ToArray(); command.Parameters.AddWithValue("$previousSequence", previous.SubjectSequence);
                    break;
                case BaseSubjectLifecycleMutationKind.Retire:
                    if (preparedIncarnation is not null || previous is null || previous.LifecycleState != BaseSubjectLifecycleState.Tombstoned)
                        return SubjectFailure<BaseSubjectLifecycleCommitEvidence>(BaseSubjectErrorCodes.ProviderContractInvalid);
                    generation = previous.LifetimeGeneration; sequence = checked(previous.SubjectSequence + 1); factIncarnation = previous.Incarnation;
                    string terminalChecksum = BaseSubjectTerminalIntegrity.Compute(
                        lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId, scope,
                        contract.Epoch, factIncarnation, generation, sequence, journalPosition,
                        contract.StateGeneration, contract.RestoreEpoch);
                    await using (SqliteCommand terminal = _connection.CreateCommand())
                    {
                        terminal.Transaction = _transaction; terminal.CommandTimeout = CommandTimeoutSeconds();
                        terminal.CommandText = $"INSERT INTO {_owner._names.SubjectTerminalLifetimes}(contract_id,contract_version,subject_id,scope_kind,scope_index_digest,protected_scope_value,retired_authority_epoch,retired_incarnation,retired_lifetime_generation,retired_subject_sequence,retired_position,contract_state_generation,restore_epoch,receipt_checksum) VALUES($contract,$version,$subject,$scopeKind,$scopeDigest,$scopeCiphertext,$epoch,$incarnation,$generation,$sequence,$position,$stateGeneration,$restore,$checksum) ON CONFLICT(scope_kind,scope_index_digest,contract_id,contract_version,subject_id) DO UPDATE SET protected_scope_value=excluded.protected_scope_value,retired_authority_epoch=excluded.retired_authority_epoch,retired_incarnation=excluded.retired_incarnation,retired_lifetime_generation=excluded.retired_lifetime_generation,retired_subject_sequence=excluded.retired_subject_sequence,retired_position=excluded.retired_position,contract_state_generation=excluded.contract_state_generation,restore_epoch=excluded.restore_epoch,receipt_checksum=excluded.receipt_checksum;";
                        terminal.Parameters.AddWithValue("$contract", lifecycle.ContractId); terminal.Parameters.AddWithValue("$version", lifecycle.ContractVersion); terminal.Parameters.AddWithValue("$subject", lifecycle.SubjectId.Value);
                        AddProtectedScope(terminal, protectedScope); terminal.Parameters.Add("$epoch", SqliteType.Blob).Value = contract.Epoch.ToArray(); terminal.Parameters.Add("$incarnation", SqliteType.Blob).Value = factIncarnation.ToArray();
                        terminal.Parameters.AddWithValue("$generation", generation); terminal.Parameters.AddWithValue("$sequence", sequence); terminal.Parameters.AddWithValue("$position", journalPosition.Value); terminal.Parameters.AddWithValue("$stateGeneration", contract.StateGeneration); terminal.Parameters.AddWithValue("$restore", contract.RestoreEpoch); terminal.Parameters.AddWithValue("$checksum", terminalChecksum);
                        await terminal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    command.CommandText = $"DELETE FROM {_owner._names.SubjectLifetimes} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND private_collection_id=$collection AND private_record_id=$record;";
                    AddProtectedScope(command, protectedScope);
                    command.Parameters.AddWithValue("$collection", item.Collection.Id);
                    command.Parameters.AddWithValue("$record", item.RecordId.Value);
                    break;
                default:
                    return SubjectFailure<BaseSubjectLifecycleCommitEvidence>(BaseSubjectErrorCodes.ProviderContractInvalid);
            }

            int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected != 1) return SubjectFailure<BaseSubjectLifecycleCommitEvidence>(BaseSubjectErrorCodes.ProviderContractInvalid);
            long deliveryEpoch;
            await using (SqliteCommand readDeliveryEpoch = _connection.CreateCommand())
            {
                readDeliveryEpoch.Transaction = _transaction;
                readDeliveryEpoch.CommandTimeout = CommandTimeoutSeconds();
                readDeliveryEpoch.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_owner._names.ProviderState} WHERE key='subject_lifecycle_delivery_epoch';";
                object? rawDeliveryEpoch = await readDeliveryEpoch.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (rawDeliveryEpoch is null || rawDeliveryEpoch is DBNull)
                    return SubjectFailure<BaseSubjectLifecycleCommitEvidence>(BaseSubjectErrorCodes.ProviderContractInvalid);
                deliveryEpoch = Convert.ToInt64(rawDeliveryEpoch, CultureInfo.InvariantCulture);
            }
            var committed = new BaseSubjectLifecycleCommitEvidence
            {
                ContractId = lifecycle.ContractId,
                ContractVersion = lifecycle.ContractVersion,
                SubjectId = lifecycle.SubjectId.Value,
                Kind = lifecycle.Kind,
                AuthorityEpoch = contract.Epoch,
                Incarnation = factIncarnation,
                SubjectSequence = sequence,
                ContractStateGeneration = contract.StateGeneration,
                DeliveryEpoch = deliveryEpoch,
                Scope = scope with { Value = scope.Value is null ? null : new string(scope.Value.AsSpan()) },
                PreviousState = lifecycle.PreviousState,
                ResultingState = lifecycle.ResultingState,
                CommitPosition = journalPosition,
            };
            if (!lifecycle.PublishFact) return new OperationResult<BaseSubjectLifecycleCommitEvidence> { Status = OperationStatus.Ok, Value = committed };

            await using (SqliteCommand capacity = _connection.CreateCommand())
            {
                capacity.Transaction = _transaction;
                capacity.CommandTimeout = CommandTimeoutSeconds();
                capacity.CommandText = $"SELECT COUNT(*) FROM {_owner._names.SubjectLifecycleFacts};";
                long retained = Convert.ToInt64(await capacity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (retained >= BaseSubjectLifecycleProviderCapabilities.BuiltIn.MaximumRetainedFacts)
                    return SubjectFailure<BaseSubjectLifecycleCommitEvidence>(BaseSubjectErrorCodes.LifecycleCapacityExceeded);
            }

            int factKind = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Create ? (int)BaseSubjectLifecycleFactKind.Created : lifecycle.Kind == BaseSubjectLifecycleMutationKind.Retire ? (int)BaseSubjectLifecycleFactKind.Retired : (int)BaseSubjectLifecycleFactKind.Transitioned;
            await using (SqliteCommand fact = _connection.CreateCommand())
            {
                fact.Transaction = _transaction; fact.CommandTimeout = CommandTimeoutSeconds();
                fact.CommandText = $"INSERT INTO {_owner._names.SubjectLifecycleFacts}(commit_position,contract_id,contract_version,subject_id,authority_epoch,incarnation,subject_sequence,contract_state_generation,delivery_epoch,fact_kind,previous_state,current_state,scope_kind,scope_index_digest,protected_scope_value) VALUES($position,$contract,$version,$subject,$epoch,$incarnation,$sequence,$stateGeneration,$deliveryEpoch,$kind,$previous,$current,$scopeKind,$scopeDigest,$scopeCiphertext);";
                fact.Parameters.AddWithValue("$position", journalPosition.Value); fact.Parameters.AddWithValue("$contract", lifecycle.ContractId); fact.Parameters.AddWithValue("$version", lifecycle.ContractVersion); fact.Parameters.AddWithValue("$subject", lifecycle.SubjectId.Value); fact.Parameters.AddWithValue("$deliveryEpoch", deliveryEpoch);
                fact.Parameters.Add("$epoch", SqliteType.Blob).Value = contract.Epoch.ToArray(); fact.Parameters.Add("$incarnation", SqliteType.Blob).Value = factIncarnation.ToArray(); fact.Parameters.AddWithValue("$sequence", sequence); fact.Parameters.AddWithValue("$stateGeneration", contract.StateGeneration); fact.Parameters.AddWithValue("$kind", factKind);
                fact.Parameters.AddWithValue("$previous", lifecycle.PreviousState is null ? DBNull.Value : (object)(int)lifecycle.PreviousState.Value); fact.Parameters.AddWithValue("$current", lifecycle.ResultingState == BaseSubjectLifecycleState.Retired ? DBNull.Value : (object)(int)lifecycle.ResultingState);
                AddProtectedScope(fact, protectedScope);
                await fact.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            foreach (BaseSubjectLifecycleMembershipPlanItem membership in lifecycle.Memberships)
            {
                await using SqliteCommand insertMembership = _connection.CreateCommand(); insertMembership.Transaction = _transaction; insertMembership.CommandTimeout = CommandTimeoutSeconds();
                insertMembership.CommandText = $"INSERT INTO {_owner._names.SubjectLifecycleMemberships}(consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,matched_state,scope_kind,scope_index_digest,protected_scope_value,commit_position,subject_id,authority_epoch,incarnation,subject_sequence) VALUES($consumer,$consumerVersion,$consumerChecksum,$contract,$version,$projection,$matched,$scopeKind,$scopeDigest,$scopeCiphertext,$position,$subject,$epoch,$incarnation,$sequence);";
                insertMembership.Parameters.AddWithValue("$consumer", membership.ConsumerId); insertMembership.Parameters.AddWithValue("$consumerVersion", membership.ConsumerVersion); insertMembership.Parameters.AddWithValue("$consumerChecksum", membership.ConsumerChecksum); insertMembership.Parameters.AddWithValue("$contract", lifecycle.ContractId); insertMembership.Parameters.AddWithValue("$version", lifecycle.ContractVersion); insertMembership.Parameters.AddWithValue("$projection", membership.ProjectionGeneration); insertMembership.Parameters.AddWithValue("$matched", (int)membership.MatchedObservedState); AddProtectedScope(insertMembership, protectedScope); insertMembership.Parameters.AddWithValue("$position", journalPosition.Value); insertMembership.Parameters.AddWithValue("$subject", lifecycle.SubjectId.Value); insertMembership.Parameters.Add("$epoch", SqliteType.Blob).Value = contract.Epoch.ToArray(); insertMembership.Parameters.Add("$incarnation", SqliteType.Blob).Value = factIncarnation.ToArray(); insertMembership.Parameters.AddWithValue("$sequence", sequence);
                await insertMembership.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            return new OperationResult<BaseSubjectLifecycleCommitEvidence> { Status = OperationStatus.Ok, Value = committed };
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
            long LifetimeGeneration,
            BaseSubjectLifecycleState LifecycleState,
            long SubjectSequence,
            BaseOwnedSubjectScopeEvidence Scope,
            string CollectionId,
            RecordId RecordId,
            long CreatedJournalPosition,
            long LastLifecyclePosition);

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
            BaseOwnedSubjectScopeEvidence scope,
            string contractId,
            int contractVersion,
            BaseSubjectId subjectId,
            CancellationToken cancellationToken)
        {
            BaseProtectedSubjectScope protectedScope = ProtectScope(scope);
            await using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandTimeout = CommandTimeoutSeconds();
            command.CommandText = $"SELECT incarnation,lifetime_generation,lifecycle_state,subject_sequence,scope_kind,scope_index_digest,protected_scope_value,private_collection_id,private_record_id,created_journal_position,last_lifecycle_position FROM {_owner._names.SubjectLifetimes} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject;";
            AddProtectedScope(command, protectedScope);
            command.Parameters.AddWithValue("$contract", contractId);
            command.Parameters.AddWithValue("$version", contractVersion);
            command.Parameters.AddWithValue("$subject", subjectId.Value);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            var storedScope = new BaseProtectedSubjectScope
            {
                Kind = (BaseSubjectScopeKind)reader.GetInt32(4),
                IndexDigest = (byte[])reader.GetValue(5),
                ProtectedCanonicalValue = (byte[])reader.GetValue(6),
            };
            if (_owner._subjectScopes is null || !_owner._subjectScopes.Matches(storedScope, scope))
                throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            return new SqlitePreparedSubjectLifetime(
                contractId,
                contractVersion,
                subjectId,
                new BaseSubjectIncarnation((byte[])reader.GetValue(0)),
                reader.GetInt64(1), (BaseSubjectLifecycleState)reader.GetInt32(2), reader.GetInt64(3),
                scope with { },
                reader.GetString(7), new RecordId(reader.GetString(8)), reader.GetInt64(9), reader.GetInt64(10));
        }

        private async ValueTask<long> ReadTerminalGenerationAsync(BaseOwnedSubjectScopeEvidence scope, string contractId, int contractVersion, BaseSubjectId subjectId, CancellationToken cancellationToken)
        {
            BaseProtectedSubjectScope protectedScope = ProtectScope(scope);
            await using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = _transaction; command.CommandTimeout = CommandTimeoutSeconds();
            command.CommandText = $"SELECT retired_lifetime_generation FROM {_owner._names.SubjectTerminalLifetimes} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject ORDER BY retired_lifetime_generation DESC LIMIT 1;";
            AddProtectedScope(command, protectedScope);
            command.Parameters.AddWithValue("$contract", contractId); command.Parameters.AddWithValue("$version", contractVersion); command.Parameters.AddWithValue("$subject", subjectId.Value);
            object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private string SubjectKey(BaseOwnedSubjectScopeEvidence scope, string contractId, int version, BaseSubjectId subjectId) =>
            $"{(int)scope.Kind}\n{Convert.ToHexString(ProtectScope(scope).IndexDigest)}\n{contractId}\n{version}\n{subjectId.Value}";

        private BaseProtectedSubjectScope ProtectScope(BaseOwnedSubjectScopeEvidence scope) =>
            _owner._subjectScopes?.Protect(scope, _owner._subjectScopeProtectionKey!.Value)
            ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);

        private static void AddProtectedScope(SqliteCommand command, BaseProtectedSubjectScope scope)
        {
            command.Parameters.AddWithValue("$scopeKind", (int)scope.Kind);
            command.Parameters.Add("$scopeDigest", SqliteType.Blob).Value = scope.IndexDigest;
            if (command.CommandText.Contains("$scopeCiphertext", StringComparison.Ordinal))
                command.Parameters.Add("$scopeCiphertext", SqliteType.Blob).Value = scope.ProtectedCanonicalValue;
        }

        private static BaseOwnedSubjectScopeEvidence ScopeForItem(BaseAtomicMutationPlanItem item, BaseExportedSubjectDefinition definition)
        {
            string? value = null;
            if (definition.Scope != BaseSubjectScopeKind.Global)
            {
                string fieldId = definition.ValidationPlan.Scope.FieldId
                    ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                FieldDefinition field = (item.Collection.Fields ?? []).Single(candidate => candidate.Id == fieldId);
                RecordPayload? payload = item.Kind == BaseCommittedRecordMutationKind.Delete ? item.Current?.Payload : item.ProposedPayload;
                if (payload?.Fields is not null && payload.Fields.TryGetValue(field.WireName, out JsonElement element) && element.ValueKind == JsonValueKind.String)
                    value = element.GetString();
            }
            return new BaseOwnedSubjectScopeEvidence { Kind = definition.Scope, Value = value };
        }

        private static BaseOwnedSubjectScopeEvidence ScopeForCurrentItem(BaseAtomicMutationPlanItem item, BaseExportedSubjectDefinition definition)
        {
            string? value = null;
            if (definition.Scope != BaseSubjectScopeKind.Global)
            {
                string fieldId = definition.ValidationPlan.Scope.FieldId
                    ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                FieldDefinition field = (item.Collection.Fields ?? []).Single(candidate => candidate.Id == fieldId);
                if (item.Current?.Payload.Fields is { } fields && fields.TryGetValue(field.WireName, out JsonElement element) && element.ValueKind == JsonValueKind.String)
                    value = element.GetString();
            }
            return new BaseOwnedSubjectScopeEvidence { Kind = definition.Scope, Value = value };
        }

        private static string CaptureRecordKey(string collectionId, RecordId recordId) =>
            collectionId + "\n" + recordId.Value;

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

        private async ValueTask<OperationResult<BaseCapturedAtomicMutationAuthority>> SelectCoreAsync(
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
            _capturedMutation = new BaseCapturedAtomicMutationAuthority
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
                    RetirementBarrierReads=0,RetirementAcknowledgementReads=0,RetirementProjections=0,RetirementPublications=0,RetirementEvidenceBytes=0,RetirementPublicationBytes=0,
                },
            };
            return OperationResults.Ok(_capturedMutation);
        }

        private static OperationResult<BaseCapturedAtomicMutationAuthority> SelectionFailure(
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

        public ValueTask<OperationResult<BaseSubjectAcknowledgementResult>> ApplySubjectRetirementAcknowledgementAsync(
            BaseSubjectRetirementProviderAcknowledgementRequest request,
            CancellationToken cancellationToken = default) => ExecuteAsync(BaseOperationKind.SubjectLifecycleCheckpoint, cancellationToken, async token =>
        {
            BaseSubjectLifecycleAcknowledgement acknowledgement=request.Acknowledgement;
            BaseSubjectRetirementConsumerDefinition? consumer=_owner._options.SubjectRetirementConsumers.SingleOrDefault(value=>value.ConsumerId==acknowledgement.ConsumerId&&value.ConsumerVersion==acknowledgement.ConsumerVersion);
            if(consumer is null||BaseSubjectRetirementRegistry.ConsumerChecksum(consumer)!=request.RetirementConsumerChecksum||consumer.Participation!=acknowledgement.Participation||consumer.LifecycleConsumerChecksum!=acknowledgement.ConsumerChecksum)
                return RetirementFailure(BaseSubjectRetirementErrorCodes.ProviderContractInvalid,OperationStatus.CapabilityUnavailable,ErrorCategory.Capability);
            BaseProtectedSubjectScope scope=ProtectScope(request.Scope);
            if(acknowledgement.Participation==BaseSubjectRetirementParticipation.AdvisoryAcknowledgement)
            {
                await using SqliteCommand delivered=_connection.CreateCommand();delivered.Transaction=_transaction;delivered.CommandTimeout=CommandTimeoutSeconds();delivered.CommandText=$"SELECT 1 FROM {_owner._names.SubjectLifecycleMemberships} WHERE consumer_id=$consumer AND consumer_version=$consumerVersion AND contract_id=$contract AND contract_version=$version AND scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation AND subject_sequence=$sequence LIMIT 1;";
                AddRetirementIdentity(delivered,scope,acknowledgement);object? exists=await delivered.ExecuteScalarAsync(token).ConfigureAwait(false);if(exists is null)return RetirementFailure(BaseSubjectRetirementErrorCodes.ProviderContractInvalid,OperationStatus.ValidationFailed,ErrorCategory.Validation);
                long advisoryPosition=await NextRetirementPositionAsync(token).ConfigureAwait(false);await WriteRetirementPublicationAsync(scope,new(){Position=new(advisoryPosition),Kind=BaseSubjectRetirementPublicationKind.AdvisoryAcknowledgementAccepted,AdvisoryAcknowledgement=new(){ContractId=acknowledgement.ContractId,ContractVersion=acknowledgement.ContractVersion,SubjectId=acknowledgement.SubjectId,AuthorityEpoch=acknowledgement.AuthorityEpoch,Incarnation=acknowledgement.Incarnation,ThroughSubjectSequence=acknowledgement.ThroughSubjectSequence,ConsumerId=acknowledgement.ConsumerId,ConsumerVersion=acknowledgement.ConsumerVersion,Disposition=acknowledgement.Disposition}},token).ConfigureAwait(false);return OperationResults.Ok(new BaseSubjectAcknowledgementResult{Outcome=BaseSubjectRetirementMutationOutcome.Applied,ThroughSubjectSequence=acknowledgement.ThroughSubjectSequence});
            }
            if(acknowledgement.Participation!=BaseSubjectRetirementParticipation.RequiredBeforePurge||acknowledgement.RequiredBarrier is null)return RetirementFailure(BaseSubjectRetirementErrorCodes.ContractInvalid,OperationStatus.ValidationFailed,ErrorCategory.Validation);
            BaseSubjectRetirementPolicy? policy=_owner._options.SubjectRetirementPolicies.SingleOrDefault(value=>value.ContractId==acknowledgement.ContractId&&value.ContractVersion==acknowledgement.ContractVersion);
            if(policy is null||policy.PolicyChecksum!=request.RetirementPolicyChecksum||!policy.AcceptedConsumers.Any(value=>value.ConsumerId==acknowledgement.ConsumerId&&value.ConsumerVersion==acknowledgement.ConsumerVersion&&value.RetirementConsumerChecksum==request.RetirementConsumerChecksum))return RetirementFailure(BaseSubjectRetirementErrorCodes.ProviderContractInvalid,OperationStatus.CapabilityUnavailable,ErrorCategory.Capability);
            BaseSubjectRetirementBarrier? barrier=null;
            await using(SqliteCommand read=_connection.CreateCommand()){read.Transaction=_transaction;read.CommandTimeout=CommandTimeoutSeconds();read.CommandText=$"SELECT tombstone_sequence,required_consumer_set_checksum,created_at,deadline_at,state,generation,barrier_checksum FROM {_owner._names.SubjectRetirementBarriers} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation;";AddRetirementIdentity(read,scope,acknowledgement);await using SqliteDataReader reader=await read.ExecuteReaderAsync(token).ConfigureAwait(false);if(await reader.ReadAsync(token).ConfigureAwait(false))barrier=new BaseSubjectRetirementBarrier{ContractId=acknowledgement.ContractId,ContractVersion=acknowledgement.ContractVersion,SubjectId=acknowledgement.SubjectId,AuthorityEpoch=acknowledgement.AuthorityEpoch,Incarnation=acknowledgement.Incarnation,TombstoneSequence=reader.GetInt64(0),RequiredConsumerSetChecksum=reader.GetString(1),CreatedAtUtc=DateTimeOffset.Parse(reader.GetString(2),CultureInfo.InvariantCulture),DeadlineUtc=DateTimeOffset.Parse(reader.GetString(3),CultureInfo.InvariantCulture),State=(BaseSubjectRetirementBarrierState)reader.GetInt32(4),Generation=reader.GetInt64(5),BarrierChecksum=reader.GetString(6)};}
            if(barrier is null||barrier.Generation!=acknowledgement.RequiredBarrier.Generation||barrier.BarrierChecksum!=acknowledgement.RequiredBarrier.Checksum||barrier.TombstoneSequence!=acknowledgement.ThroughSubjectSequence)return RetirementFailure("base.subjectRetirement.acknowledgementConflict",OperationStatus.Conflict,ErrorCategory.Conflict);
            if(request.ObservedAtUtc>barrier.DeadlineUtc||barrier.State!=BaseSubjectRetirementBarrierState.Pending)return RetirementFailure("base.subjectRetirement.barrierTimedOut",OperationStatus.Conflict,ErrorCategory.Conflict);
            var inputs=new List<string>();bool existing=false;
            await using(SqliteCommand rows=_connection.CreateCommand()){rows.Transaction=_transaction;rows.CommandTimeout=CommandTimeoutSeconds();rows.CommandText=$"SELECT consumer_id,consumer_version,consumer_checksum,through_sequence,disposition,retirement_position FROM {_owner._names.SubjectRetirementAcknowledgements} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation ORDER BY consumer_id,consumer_version;";AddRetirementIdentity(rows,scope,acknowledgement);await using SqliteDataReader reader=await rows.ExecuteReaderAsync(token).ConfigureAwait(false);while(await reader.ReadAsync(token).ConfigureAwait(false)){string id=reader.GetString(0);int version=reader.GetInt32(1);if(id==acknowledgement.ConsumerId&&version==acknowledgement.ConsumerVersion)existing=true;inputs.Add(BaseSubjectRetirementRegistry.AcknowledgementChecksumInput(id,version,reader.GetString(2),reader.GetInt64(3),(BaseSubjectAcknowledgementDisposition)reader.GetInt32(4),reader.GetInt64(5)));}}
            if(existing)return OperationResults.Ok(new BaseSubjectAcknowledgementResult{Outcome=BaseSubjectRetirementMutationOutcome.Obsolete,BarrierState=barrier.State,BarrierGeneration=barrier.Generation,BarrierChecksum=barrier.BarrierChecksum,ThroughSubjectSequence=acknowledgement.ThroughSubjectSequence});
            long position=await NextRetirementPositionAsync(token).ConfigureAwait(false);inputs.Add(BaseSubjectRetirementRegistry.AcknowledgementChecksumInput(acknowledgement.ConsumerId,acknowledgement.ConsumerVersion,acknowledgement.ConsumerChecksum,acknowledgement.ThroughSubjectSequence,acknowledgement.Disposition,position));
            await using(SqliteCommand insert=_connection.CreateCommand()){insert.Transaction=_transaction;insert.CommandTimeout=CommandTimeoutSeconds();insert.CommandText=$"INSERT INTO {_owner._names.SubjectRetirementAcknowledgements}(scope_kind,scope_index_digest,protected_scope_value,contract_id,contract_version,subject_id,authority_epoch,incarnation,consumer_id,consumer_version,consumer_checksum,through_sequence,disposition,retirement_position) VALUES($scopeKind,$scopeDigest,$scopeValue,$contract,$version,$subject,$epoch,$incarnation,$consumer,$consumerVersion,$consumerChecksum,$sequence,$disposition,$position);";AddRetirementIdentity(insert,scope,acknowledgement);insert.Parameters.Add("$scopeValue",SqliteType.Blob).Value=scope.ProtectedCanonicalValue;insert.Parameters.AddWithValue("$consumerChecksum",acknowledgement.ConsumerChecksum);insert.Parameters.AddWithValue("$disposition",(int)acknowledgement.Disposition);insert.Parameters.AddWithValue("$position",position);await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);}
            bool satisfied=policy.AcceptedConsumers.All(value=>inputs.Any(input=>input.StartsWith(value.ConsumerId+"\0"+value.ConsumerVersion+"\0",StringComparison.Ordinal)));BaseSubjectRetirementBarrier resulting=barrier with{State=satisfied?BaseSubjectRetirementBarrierState.Satisfied:BaseSubjectRetirementBarrierState.Pending,Generation=checked(barrier.Generation+1),BarrierChecksum=string.Empty};resulting=resulting with{BarrierChecksum=BaseSubjectRetirementRegistry.BarrierChecksum(resulting,inputs)};
            await using(SqliteCommand update=_connection.CreateCommand()){update.Transaction=_transaction;update.CommandTimeout=CommandTimeoutSeconds();update.CommandText=$"UPDATE {_owner._names.SubjectRetirementBarriers} SET state=$state,generation=$generation,barrier_checksum=$result WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation AND generation=$expected AND barrier_checksum=$checksum;";AddRetirementIdentity(update,scope,acknowledgement);update.Parameters.AddWithValue("$state",(int)resulting.State);update.Parameters.AddWithValue("$generation",resulting.Generation);update.Parameters.AddWithValue("$result",resulting.BarrierChecksum);update.Parameters.AddWithValue("$expected",barrier.Generation);update.Parameters.AddWithValue("$checksum",barrier.BarrierChecksum);if(await update.ExecuteNonQueryAsync(token).ConfigureAwait(false)!=1)return RetirementFailure("base.subjectRetirement.acknowledgementConflict",OperationStatus.Conflict,ErrorCategory.Conflict);}
            await WriteRetirementPublicationAsync(scope,new(){Position=new(position),Kind=satisfied?BaseSubjectRetirementPublicationKind.BarrierSatisfied:BaseSubjectRetirementPublicationKind.RequiredAcknowledgementAccepted,Barrier=BarrierPublication(resulting,barrier.Generation,acknowledgement.ConsumerId)},token).ConfigureAwait(false);
            return OperationResults.Ok(new BaseSubjectAcknowledgementResult{Outcome=BaseSubjectRetirementMutationOutcome.Applied,BarrierState=resulting.State,BarrierGeneration=resulting.Generation,BarrierChecksum=resulting.BarrierChecksum,ThroughSubjectSequence=acknowledgement.ThroughSubjectSequence});
        });

        public ValueTask<OperationResult<BaseSubjectRetirementTimeoutResult>> ApplySubjectRetirementTimeoutAsync(BaseSubjectRetirementProviderTimeoutRequest request,CancellationToken cancellationToken=default)=>ExecuteAsync(BaseOperationKind.SubjectRetirementTimeout,cancellationToken,async token=>
        {
            ArgumentNullException.ThrowIfNull(request);BaseSubjectRetirementTimeoutRequest command=request.Request;if(!RetirementPolicyMatches(command.ContractId,command.ContractVersion,request.RetirementPolicyChecksum)||request.ObservedAtUtc.Offset!=TimeSpan.Zero)return RetirementFailure<BaseSubjectRetirementTimeoutResult>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid,OperationStatus.CapabilityUnavailable,ErrorCategory.Capability);BaseProtectedSubjectScope scope=ProtectScope(request.Scope);
            BaseSubjectRetirementBarrier? barrier=await ReadRetirementBarrierAsync(scope,command.ContractId,command.ContractVersion,command.SubjectId,command.AuthorityEpoch,command.Incarnation,token).ConfigureAwait(false);if(barrier is null||barrier.Generation!=command.ExpectedBarrierGeneration||barrier.BarrierChecksum!=command.ExpectedBarrierChecksum)return RetirementFailure<BaseSubjectRetirementTimeoutResult>(BaseSubjectRetirementErrorCodes.SequenceInvalid,OperationStatus.Conflict,ErrorCategory.Conflict);if(barrier.State!=BaseSubjectRetirementBarrierState.Pending)return OperationResults.Ok(new BaseSubjectRetirementTimeoutResult{Outcome=BaseSubjectRetirementMutationOutcome.Obsolete,State=barrier.State,Generation=barrier.Generation,BarrierChecksum=barrier.BarrierChecksum});if(request.ObservedAtUtc<=barrier.DeadlineUtc)return RetirementFailure<BaseSubjectRetirementTimeoutResult>(BaseSubjectRetirementErrorCodes.BarrierPending,OperationStatus.Conflict,ErrorCategory.Conflict);BaseSubjectRetirementPolicy policy=_owner._options.SubjectRetirementPolicies.Single(value=>value.ContractId==command.ContractId&&value.ContractVersion==command.ContractVersion);BaseSubjectRetirementBarrierState timeoutState=policy.TimeoutBehavior==BaseSubjectRetirementTimeoutBehavior.Quarantine?BaseSubjectRetirementBarrierState.Quarantined:BaseSubjectRetirementBarrierState.TimedOut;
            BaseSubjectRetirementBarrier resulting=await TransitionRetirementBarrierAsync(scope,barrier,timeoutState,token).ConfigureAwait(false);return OperationResults.Ok(new BaseSubjectRetirementTimeoutResult{Outcome=BaseSubjectRetirementMutationOutcome.Applied,State=resulting.State,Generation=resulting.Generation,BarrierChecksum=resulting.BarrierChecksum});
        });

        public ValueTask<OperationResult<BaseSubjectRetirementOverrideResult>> ApplySubjectRetirementOverrideAsync(BaseSubjectRetirementProviderOverrideRequest request,CancellationToken cancellationToken=default)=>ExecuteAsync(BaseOperationKind.SubjectRetirementOverride,cancellationToken,async token=>
        {
            ArgumentNullException.ThrowIfNull(request);BaseSubjectRetirementOverrideRequest command=request.Request;if(command.Intent!="override-subject-retirement-barrier"||command.ChangeReference.Length is<1 or>256||!RetirementPolicyMatches(command.ContractId,command.ContractVersion,request.RetirementPolicyChecksum))return RetirementFailure<BaseSubjectRetirementOverrideResult>(BaseSubjectRetirementErrorCodes.ContractInvalid,OperationStatus.ValidationFailed,ErrorCategory.Validation);BaseProtectedSubjectScope scope=ProtectScope(request.Scope);
            BaseSubjectRetirementBarrier? barrier=await ReadRetirementBarrierAsync(scope,command.ContractId,command.ContractVersion,command.SubjectId,command.AuthorityEpoch,command.Incarnation,token).ConfigureAwait(false);if(barrier is null||barrier.TombstoneSequence!=command.ExpectedTombstoneSequence||barrier.Generation!=command.ExpectedBarrierGeneration||barrier.BarrierChecksum!=command.ExpectedBarrierChecksum)return RetirementFailure<BaseSubjectRetirementOverrideResult>("base.subjectRetirement.overrideConflict",OperationStatus.Conflict,ErrorCategory.Conflict);if(barrier.State==BaseSubjectRetirementBarrierState.Overridden)return OperationResults.Ok(new BaseSubjectRetirementOverrideResult{Outcome=BaseSubjectRetirementMutationOutcome.Obsolete,Generation=barrier.Generation,BarrierChecksum=barrier.BarrierChecksum});if(barrier.State is not(BaseSubjectRetirementBarrierState.TimedOut or BaseSubjectRetirementBarrierState.Quarantined))return RetirementFailure<BaseSubjectRetirementOverrideResult>("base.subjectRetirement.overrideConflict",OperationStatus.Conflict,ErrorCategory.Conflict);
            BaseSubjectRetirementBarrier resulting=await TransitionRetirementBarrierAsync(scope,barrier,BaseSubjectRetirementBarrierState.Overridden,token).ConfigureAwait(false);return OperationResults.Ok(new BaseSubjectRetirementOverrideResult{Outcome=BaseSubjectRetirementMutationOutcome.Applied,Generation=resulting.Generation,BarrierChecksum=resulting.BarrierChecksum});
        });

        public ValueTask<OperationResult<BaseSubjectRetirementPurgeApplied>> ApplySubjectRetirementPurgeAsync(BaseSubjectRetirementProviderPurgeRequest request,CancellationToken cancellationToken=default)=>ExecuteAsync(BaseOperationKind.SubjectRetirementPurge,cancellationToken,async token=>
        {
            ArgumentNullException.ThrowIfNull(request);BaseSubjectFinalPurgeRequest command=request.Request;BaseSubjectRetirementPolicy? policy=_owner._options.SubjectRetirementPolicies.SingleOrDefault(value=>value.ContractId==command.ContractId&&value.ContractVersion==command.ContractVersion);BaseExportedSubjectDefinition? definition=_owner._options.ExportedSubjects.SingleOrDefault(value=>value.Id==command.ContractId&&value.Version==command.ContractVersion);if(policy is null||definition is null||policy.PolicyChecksum!=request.RetirementPolicyChecksum||BaseSubjectContractGraph.Checksum(definition)!=request.ContractChecksum||policy.PurgeRetention.MinimumTombstoneAge!=request.MinimumTombstoneAge)return RetirementFailure<BaseSubjectRetirementPurgeApplied>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid,OperationStatus.CapabilityUnavailable,ErrorCategory.Capability);BaseProtectedSubjectScope scope=ProtectScope(request.Scope);BaseSubjectRetirementBarrier? barrier=await ReadRetirementBarrierAsync(scope,command.ContractId,command.ContractVersion,command.SubjectId,command.AuthorityEpoch,command.Incarnation,token).ConfigureAwait(false);if(barrier is null||barrier.TombstoneSequence!=command.ExpectedTombstoneSequence||barrier.Generation!=command.ExpectedBarrierGeneration||barrier.BarrierChecksum!=command.ExpectedBarrierChecksum||barrier.State is not(BaseSubjectRetirementBarrierState.Satisfied or BaseSubjectRetirementBarrierState.Overridden)||barrier.RequiredConsumerSetChecksum!=BaseSubjectRetirementRegistry.AcceptedSetChecksum(policy.AcceptedConsumers)||request.ObservedAtUtc<checked(barrier.CreatedAtUtc+request.MinimumTombstoneAge))return RetirementFailure<BaseSubjectRetirementPurgeApplied>("base.subjectRetirement.purgeConflict",OperationStatus.Conflict,ErrorCategory.Conflict);
            string? collectionId=null;RecordId? recordId=null;long sequence=0;await using(SqliteCommand lifetime=_connection.CreateCommand()){lifetime.Transaction=_transaction;lifetime.CommandTimeout=CommandTimeoutSeconds();lifetime.CommandText=$"SELECT private_collection_id,private_record_id,subject_sequence FROM {_owner._names.SubjectLifetimes} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND incarnation=$incarnation AND lifecycle_state=$state;";AddRetirementKey(lifetime,scope,command.ContractId,command.ContractVersion,command.SubjectId,command.AuthorityEpoch,command.Incarnation);lifetime.Parameters.AddWithValue("$state",(int)BaseSubjectLifecycleState.Tombstoned);await using SqliteDataReader reader=await lifetime.ExecuteReaderAsync(token).ConfigureAwait(false);if(await reader.ReadAsync(token).ConfigureAwait(false)){collectionId=reader.GetString(0);recordId=new(reader.GetString(1));sequence=reader.GetInt64(2);}}if(collectionId is null||recordId is null)return RetirementFailure<BaseSubjectRetirementPurgeApplied>("base.subjectRetirement.purgeConflict",OperationStatus.Conflict,ErrorCategory.Conflict);CollectionDefinition? collection=(_owner._options.Collections??[]).SingleOrDefault(value=>value.Id==collectionId);if(collection is null)return RetirementFailure<BaseSubjectRetirementPurgeApplied>(BaseSubjectRetirementErrorCodes.ProviderContractInvalid,OperationStatus.CapabilityUnavailable,ErrorCategory.Capability);
            var memberships=ImmutableArray.CreateBuilder<BaseSubjectLifecycleMembershipPlanItem>();foreach(BaseSubjectLifecycleConsumerDefinition consumer in _owner._options.SubjectLifecycleConsumers.Where(value=>value.ContractId==command.ContractId&&value.ContractVersion==command.ContractVersion&&value.ObservedStates.Contains(BaseSubjectLifecycleState.Retired))){await using SqliteCommand projection=_connection.CreateCommand();projection.Transaction=_transaction;projection.CommandTimeout=CommandTimeoutSeconds();projection.CommandText=$"SELECT consumer_checksum,projection_generation FROM {_owner._names.SubjectLifecycleConsumers} WHERE consumer_id=$consumer AND consumer_version=$version AND state=0;";projection.Parameters.AddWithValue("$consumer",consumer.Id);projection.Parameters.AddWithValue("$version",consumer.Version);await using SqliteDataReader reader=await projection.ExecuteReaderAsync(token).ConfigureAwait(false);if(await reader.ReadAsync(token).ConfigureAwait(false))memberships.Add(new(){ConsumerId=consumer.Id,ConsumerVersion=consumer.Version,ConsumerChecksum=reader.GetString(0),ProjectionGeneration=reader.GetInt64(1),MatchedObservedState=BaseSubjectLifecycleState.Retired});}
            var context=new RecordMutationSessionContext{ItemId="subject-retirement-purge",RequestedOperation=BaseRecordMutationKind.Delete,EventId=Guid.NewGuid().ToString("N"),Operation=request.Operation,ChangedFields=[]};OperationResult<RecordMutationSessionResult> deleted=await DeleteCoreAsync(collection,recordId.Value,new(){ExpectedRevision=command.ExpectedPrivateRevision,ReturnPrevious=true},context,token).ConfigureAwait(false);if(!deleted.IsSuccess()||deleted.Value is null)return new(){Status=deleted.Status,Error=deleted.Error};var lifecyclePlan=new BaseSubjectLifecyclePlanItem{ContractId=command.ContractId,ContractVersion=command.ContractVersion,ContractChecksum=request.ContractChecksum,Kind=BaseSubjectLifecycleMutationKind.Retire,SubjectId=command.SubjectId,PreviousState=BaseSubjectLifecycleState.Tombstoned,ResultingState=BaseSubjectLifecycleState.Retired,PublishFact=true,Memberships=memberships.MoveToImmutable()};var item=new BaseAtomicMutationPlanItem{Ordinal=0,ItemId="subject-retirement-purge",EventId=context.EventId,Collection=collection,Kind=BaseCommittedRecordMutationKind.Delete,RequestedKind=BaseRecordMutationKind.Delete,RecordId=recordId.Value,Delete=new(){ExpectedRevision=command.ExpectedPrivateRevision,ReturnPrevious=true},Current=deleted.Value.Mutation.Before,ChangedFields=[],SubjectLifecycle=lifecyclePlan,Operation=request.Operation};OperationResult<BaseSubjectLifecycleCommitEvidence> lifecycle=await ApplySubjectLifecycleAsync(item,lifecyclePlan,null,deleted.Value.Mutation.JournalPosition,token).ConfigureAwait(false);if(!lifecycle.IsSuccess()||lifecycle.Value is null)return new(){Status=lifecycle.Status,Error=lifecycle.Error};BaseRecordMutationFact fact=deleted.Value.Mutation with{SubjectLifecycle=lifecycle.Value};
            var acknowledgements=ImmutableArray.CreateBuilder<BaseSubjectTerminalAcknowledgement>();await using(SqliteCommand rows=_connection.CreateCommand()){rows.Transaction=_transaction;rows.CommandTimeout=CommandTimeoutSeconds();rows.CommandText=$"SELECT consumer_id,consumer_version,consumer_checksum,through_sequence,disposition,retirement_position FROM {_owner._names.SubjectRetirementAcknowledgements} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation ORDER BY consumer_id,consumer_version;";AddRetirementKey(rows,scope,command.ContractId,command.ContractVersion,command.SubjectId,command.AuthorityEpoch,command.Incarnation);await using SqliteDataReader reader=await rows.ExecuteReaderAsync(token).ConfigureAwait(false);while(await reader.ReadAsync(token).ConfigureAwait(false))acknowledgements.Add(new(){ConsumerId=reader.GetString(0),ConsumerVersion=reader.GetInt32(1),ConsumerChecksum=reader.GetString(2),ThroughSubjectSequence=reader.GetInt64(3),Disposition=(BaseSubjectAcknowledgementDisposition)reader.GetInt32(4),AcknowledgedPosition=new(reader.GetInt64(5))});}
            var terminal=new BaseSubjectRetirementTerminalReceipt{ContractId=command.ContractId,ContractVersion=command.ContractVersion,SubjectId=command.SubjectId,Scope=scope,AuthorityEpoch=command.AuthorityEpoch,Incarnation=command.Incarnation,TombstoneSequence=barrier.TombstoneSequence,AuthorizingState=barrier.State,FinalBarrierGeneration=barrier.Generation,FinalBarrierChecksum=barrier.BarrierChecksum,RequiredConsumerSetChecksum=barrier.RequiredConsumerSetChecksum,Acknowledgements=acknowledgements.MoveToImmutable(),RetiredPosition=deleted.Value.Mutation.JournalPosition,PurgedAtUtc=request.ObservedAtUtc,ReceiptChecksum=string.Empty};terminal=terminal with{ReceiptChecksum=BaseSubjectRetirementRegistry.TerminalChecksum(terminal)};byte[] acknowledgementBytes=Encoding.UTF8.GetBytes(string.Join('\n',terminal.Acknowledgements.Select(static value=>$"{value.ConsumerId}\0{value.ConsumerVersion}\0{value.ConsumerChecksum}\0{value.ThroughSubjectSequence}\0{(int)value.Disposition}\0{value.AcknowledgedPosition.Value}")));
            await using(SqliteCommand insert=_connection.CreateCommand()){insert.Transaction=_transaction;insert.CommandTimeout=CommandTimeoutSeconds();insert.CommandText=$"INSERT INTO {_owner._names.SubjectRetirementTerminals}(scope_kind,scope_index_digest,protected_scope_value,contract_id,contract_version,subject_id,authority_epoch,incarnation,tombstone_sequence,authorizing_state,final_barrier_generation,final_barrier_checksum,required_consumer_set_checksum,acknowledgements_blob,retired_position,purged_at,receipt_checksum) VALUES($scopeKind,$scopeDigest,$scopeValue,$contract,$version,$subject,$epoch,$incarnation,$sequence,$state,$generation,$barrierChecksum,$set,$acknowledgements,$position,$purged,$receipt) ON CONFLICT(scope_kind,scope_index_digest,contract_id,contract_version,subject_id) DO UPDATE SET protected_scope_value=excluded.protected_scope_value,authority_epoch=excluded.authority_epoch,incarnation=excluded.incarnation,tombstone_sequence=excluded.tombstone_sequence,authorizing_state=excluded.authorizing_state,final_barrier_generation=excluded.final_barrier_generation,final_barrier_checksum=excluded.final_barrier_checksum,required_consumer_set_checksum=excluded.required_consumer_set_checksum,acknowledgements_blob=excluded.acknowledgements_blob,retired_position=excluded.retired_position,purged_at=excluded.purged_at,receipt_checksum=excluded.receipt_checksum;";insert.Parameters.AddWithValue("$scopeKind",(int)scope.Kind);insert.Parameters.Add("$scopeDigest",SqliteType.Blob).Value=scope.IndexDigest;insert.Parameters.Add("$scopeValue",SqliteType.Blob).Value=scope.ProtectedCanonicalValue;insert.Parameters.AddWithValue("$contract",command.ContractId);insert.Parameters.AddWithValue("$version",command.ContractVersion);insert.Parameters.AddWithValue("$subject",command.SubjectId.Value);insert.Parameters.Add("$epoch",SqliteType.Blob).Value=command.AuthorityEpoch.ToArray();insert.Parameters.Add("$incarnation",SqliteType.Blob).Value=command.Incarnation.ToArray();insert.Parameters.AddWithValue("$sequence",barrier.TombstoneSequence);insert.Parameters.AddWithValue("$state",(int)barrier.State);insert.Parameters.AddWithValue("$generation",barrier.Generation);insert.Parameters.AddWithValue("$barrierChecksum",barrier.BarrierChecksum);insert.Parameters.AddWithValue("$set",barrier.RequiredConsumerSetChecksum);insert.Parameters.Add("$acknowledgements",SqliteType.Blob).Value=acknowledgementBytes;insert.Parameters.AddWithValue("$position",deleted.Value.Mutation.JournalPosition.Value);insert.Parameters.AddWithValue("$purged",request.ObservedAtUtc.ToString("O",CultureInfo.InvariantCulture));insert.Parameters.AddWithValue("$receipt",terminal.ReceiptChecksum);await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);}
            foreach(string table in new[]{_owner._names.SubjectRetirementAcknowledgements,_owner._names.SubjectRetirementBarriers}){await using SqliteCommand remove=_connection.CreateCommand();remove.Transaction=_transaction;remove.CommandTimeout=CommandTimeoutSeconds();remove.CommandText=$"DELETE FROM {table} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation;";AddRetirementKey(remove,scope,command.ContractId,command.ContractVersion,command.SubjectId,command.AuthorityEpoch,command.Incarnation);await remove.ExecuteNonQueryAsync(token).ConfigureAwait(false);}long publicationPosition=await NextRetirementPositionAsync(token).ConfigureAwait(false);await WriteRetirementPublicationAsync(scope,new(){Position=new(publicationPosition),Kind=BaseSubjectRetirementPublicationKind.SubjectPurged,Purged=new(){ContractId=command.ContractId,ContractVersion=command.ContractVersion,SubjectId=command.SubjectId,AuthorityEpoch=command.AuthorityEpoch,Incarnation=command.Incarnation,TombstoneSequence=barrier.TombstoneSequence,FinalBarrierGeneration=barrier.Generation,FinalBarrierChecksum=barrier.BarrierChecksum,TerminalReceiptChecksum=terminal.ReceiptChecksum,RetiredLifecyclePosition=deleted.Value.Mutation.JournalPosition}},token).ConfigureAwait(false);long retiredSequence=checked(sequence+1);return OperationResults.Ok(new BaseSubjectRetirementPurgeApplied{Result=new(){Outcome=BaseSubjectRetirementMutationOutcome.Applied,RetiredSubjectSequence=retiredSequence,RetiredPosition=deleted.Value.Mutation.JournalPosition,TerminalReceiptChecksum=terminal.ReceiptChecksum},Mutation=fact,Terminal=terminal});
        });

        private bool RetirementPolicyMatches(string contractId,int contractVersion,string checksum)=>_owner._options.SubjectRetirementPolicies.SingleOrDefault(value=>value.ContractId==contractId&&value.ContractVersion==contractVersion)?.PolicyChecksum==checksum;
        private async ValueTask<BaseSubjectRetirementBarrier?> ReadRetirementBarrierAsync(BaseProtectedSubjectScope scope,string contractId,int contractVersion,BaseSubjectId subjectId,BaseSubjectAuthorityEpoch epoch,BaseSubjectIncarnation incarnation,CancellationToken token)
        {
            await using SqliteCommand command=_connection.CreateCommand();command.Transaction=_transaction;command.CommandTimeout=CommandTimeoutSeconds();command.CommandText=$"SELECT tombstone_sequence,required_consumer_set_checksum,created_at,deadline_at,state,generation,barrier_checksum FROM {_owner._names.SubjectRetirementBarriers} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation;";AddRetirementKey(command,scope,contractId,contractVersion,subjectId,epoch,incarnation);await using SqliteDataReader reader=await command.ExecuteReaderAsync(token).ConfigureAwait(false);return await reader.ReadAsync(token).ConfigureAwait(false)?new(){ContractId=contractId,ContractVersion=contractVersion,SubjectId=subjectId,AuthorityEpoch=epoch,Incarnation=incarnation,TombstoneSequence=reader.GetInt64(0),RequiredConsumerSetChecksum=reader.GetString(1),CreatedAtUtc=DateTimeOffset.Parse(reader.GetString(2),CultureInfo.InvariantCulture),DeadlineUtc=DateTimeOffset.Parse(reader.GetString(3),CultureInfo.InvariantCulture),State=(BaseSubjectRetirementBarrierState)reader.GetInt32(4),Generation=reader.GetInt64(5),BarrierChecksum=reader.GetString(6)}:null;
        }
        private async ValueTask<BaseSubjectRetirementBarrier> TransitionRetirementBarrierAsync(BaseProtectedSubjectScope scope,BaseSubjectRetirementBarrier barrier,BaseSubjectRetirementBarrierState state,CancellationToken token)
        {
            var inputs=new List<string>();await using(SqliteCommand rows=_connection.CreateCommand()){rows.Transaction=_transaction;rows.CommandTimeout=CommandTimeoutSeconds();rows.CommandText=$"SELECT consumer_id,consumer_version,consumer_checksum,through_sequence,disposition,retirement_position FROM {_owner._names.SubjectRetirementAcknowledgements} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation ORDER BY consumer_id,consumer_version;";AddRetirementKey(rows,scope,barrier.ContractId,barrier.ContractVersion,barrier.SubjectId,barrier.AuthorityEpoch,barrier.Incarnation);await using SqliteDataReader reader=await rows.ExecuteReaderAsync(token).ConfigureAwait(false);while(await reader.ReadAsync(token).ConfigureAwait(false))inputs.Add(BaseSubjectRetirementRegistry.AcknowledgementChecksumInput(reader.GetString(0),reader.GetInt32(1),reader.GetString(2),reader.GetInt64(3),(BaseSubjectAcknowledgementDisposition)reader.GetInt32(4),reader.GetInt64(5)));}
            BaseSubjectRetirementBarrier resulting=barrier with{State=state,Generation=checked(barrier.Generation+1),BarrierChecksum=string.Empty};resulting=resulting with{BarrierChecksum=BaseSubjectRetirementRegistry.BarrierChecksum(resulting,inputs)};await using SqliteCommand update=_connection.CreateCommand();update.Transaction=_transaction;update.CommandTimeout=CommandTimeoutSeconds();update.CommandText=$"UPDATE {_owner._names.SubjectRetirementBarriers} SET state=$state,generation=$generation,barrier_checksum=$result WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation AND generation=$expected AND barrier_checksum=$checksum;";AddRetirementKey(update,scope,barrier.ContractId,barrier.ContractVersion,barrier.SubjectId,barrier.AuthorityEpoch,barrier.Incarnation);update.Parameters.AddWithValue("$state",(int)resulting.State);update.Parameters.AddWithValue("$generation",resulting.Generation);update.Parameters.AddWithValue("$result",resulting.BarrierChecksum);update.Parameters.AddWithValue("$expected",barrier.Generation);update.Parameters.AddWithValue("$checksum",barrier.BarrierChecksum);if(await update.ExecuteNonQueryAsync(token).ConfigureAwait(false)!=1)throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);long position=await NextRetirementPositionAsync(token).ConfigureAwait(false);BaseSubjectRetirementPublicationKind kind=state switch{BaseSubjectRetirementBarrierState.TimedOut=>BaseSubjectRetirementPublicationKind.BarrierTimedOut,BaseSubjectRetirementBarrierState.Quarantined=>BaseSubjectRetirementPublicationKind.BarrierQuarantined,BaseSubjectRetirementBarrierState.Overridden=>BaseSubjectRetirementPublicationKind.BarrierOverridden,_=>throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid)};await WriteRetirementPublicationAsync(scope,new(){Position=new(position),Kind=kind,Barrier=BarrierPublication(resulting,barrier.Generation,null)},token).ConfigureAwait(false);return resulting;
        }
        private static void AddRetirementKey(SqliteCommand command,BaseProtectedSubjectScope scope,string contractId,int contractVersion,BaseSubjectId subjectId,BaseSubjectAuthorityEpoch epoch,BaseSubjectIncarnation incarnation){command.Parameters.AddWithValue("$scopeKind",(int)scope.Kind);command.Parameters.Add("$scopeDigest",SqliteType.Blob).Value=scope.IndexDigest;command.Parameters.AddWithValue("$contract",contractId);command.Parameters.AddWithValue("$version",contractVersion);command.Parameters.AddWithValue("$subject",subjectId.Value);command.Parameters.Add("$epoch",SqliteType.Blob).Value=epoch.ToArray();command.Parameters.Add("$incarnation",SqliteType.Blob).Value=incarnation.ToArray();}

        private void AddRetirementIdentity(SqliteCommand command,BaseProtectedSubjectScope scope,BaseSubjectLifecycleAcknowledgement acknowledgement){command.Parameters.AddWithValue("$scopeKind",(int)scope.Kind);command.Parameters.Add("$scopeDigest",SqliteType.Blob).Value=scope.IndexDigest;command.Parameters.AddWithValue("$contract",acknowledgement.ContractId);command.Parameters.AddWithValue("$version",acknowledgement.ContractVersion);command.Parameters.AddWithValue("$subject",acknowledgement.SubjectId.Value);command.Parameters.Add("$epoch",SqliteType.Blob).Value=acknowledgement.AuthorityEpoch.ToArray();command.Parameters.Add("$incarnation",SqliteType.Blob).Value=acknowledgement.Incarnation.ToArray();command.Parameters.AddWithValue("$consumer",acknowledgement.ConsumerId);command.Parameters.AddWithValue("$consumerVersion",acknowledgement.ConsumerVersion);command.Parameters.AddWithValue("$sequence",acknowledgement.ThroughSubjectSequence);}
        private async ValueTask<long> NextRetirementPositionAsync(CancellationToken cancellationToken){await using SqliteCommand command=_connection.CreateCommand();command.Transaction=_transaction;command.CommandTimeout=CommandTimeoutSeconds();command.CommandText=$"UPDATE {_owner._names.ProviderState} SET value=CAST(value AS INTEGER)+1 WHERE key='subject_retirement_position' RETURNING CAST(value AS INTEGER);";object? value=await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);if(value is null)throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);return Convert.ToInt64(value,CultureInfo.InvariantCulture);}
        private static BaseSubjectBarrierPublication BarrierPublication(BaseSubjectRetirementBarrier barrier,long previous,string? consumer)=>new(){ContractId=barrier.ContractId,ContractVersion=barrier.ContractVersion,SubjectId=barrier.SubjectId,AuthorityEpoch=barrier.AuthorityEpoch,Incarnation=barrier.Incarnation,TombstoneSequence=barrier.TombstoneSequence,PreviousGeneration=previous,PublishedGeneration=barrier.Generation,ConsumerId=consumer};
        private async ValueTask WriteRetirementPublicationAsync(BaseProtectedSubjectScope? scope,BaseSubjectRetirementPublicationFact fact,CancellationToken token){fact=BaseSubjectRetirementRegistry.SealPublication(fact,scope);BaseSubjectRetirementRegistry.ValidatePublication(new(){Scope=scope,Fact=fact});byte[] payload=JsonSerializer.SerializeToUtf8Bytes(fact,HPDBaseJsonSerializerContext.Default.BaseSubjectRetirementPublicationFact);await using SqliteCommand command=_connection.CreateCommand();command.Transaction=_transaction;command.CommandTimeout=CommandTimeoutSeconds();command.CommandText=$"INSERT INTO {_owner._names.SubjectRetirementPublications}(position,kind,scope_kind,scope_index_digest,protected_scope_value,payload) VALUES($position,$kind,$scopeKind,$scopeDigest,$scopeValue,$payload);";command.Parameters.AddWithValue("$position",fact.Position.Value);command.Parameters.AddWithValue("$kind",(int)fact.Kind);command.Parameters.AddWithValue("$scopeKind",scope is null?DBNull.Value:(int)scope.Kind);command.Parameters.Add("$scopeDigest",SqliteType.Blob).Value=scope is null?DBNull.Value:scope.IndexDigest;command.Parameters.Add("$scopeValue",SqliteType.Blob).Value=scope is null?DBNull.Value:scope.ProtectedCanonicalValue;command.Parameters.Add("$payload",SqliteType.Blob).Value=payload;if(await command.ExecuteNonQueryAsync(token).ConfigureAwait(false)!=1)throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);}
        private static OperationResult<BaseSubjectAcknowledgementResult> RetirementFailure(string code,OperationStatus status,ErrorCategory category)=>new(){Status=status,Error=new BaseError{Code=code,Message="The subject retirement operation failed.",Category=category}};
        private static OperationResult<T> RetirementFailure<T>(string code,OperationStatus status,ErrorCategory category)=>new(){Status=status,Error=new BaseError{Code=code,Message="The subject retirement operation failed.",Category=category}};

        public ValueTask<OperationResult<BaseSubjectLifecycleCheckpointResult>> AdvanceSubjectLifecycleCheckpointAsync(
            BaseSubjectLifecycleProviderCheckpointRequest request,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(BaseOperationKind.SubjectLifecycleCheckpoint, cancellationToken, async token =>
            {
                ArgumentNullException.ThrowIfNull(request);
                if (_owner._subjectScopes is null || request.DeadlineUtc <= _owner._timeProvider.GetUtcNow()
                    || request.ExpectedCheckpointGeneration < 0 || request.ProjectionGeneration < 1)
                    return OperationResults.ValidationFailed<BaseSubjectLifecycleCheckpointResult>(new BaseError
                    {
                        Code = BaseSubjectErrorCodes.ContractInvalid,
                        Message = "The subject lifecycle checkpoint operation is invalid.",
                        Category = ErrorCategory.Validation,
                    });

                BaseProtectedSubjectScope protectedScope = _owner._subjectScopes.Protect(request.Scope, _owner._subjectScopeProtectionKey!.Value);
                await using (SqliteCommand installed = _connection.CreateCommand())
                {
                    installed.Transaction = _transaction;
                    installed.CommandTimeout = _owner.TimeoutSeconds();
                    installed.CommandText = $"SELECT consumer_checksum,contract_id,contract_version,projection_generation,state FROM {_owner._names.SubjectLifecycleConsumers} WHERE consumer_id=$consumer AND consumer_version=$version;";
                    installed.Parameters.AddWithValue("$consumer", request.ConsumerId);
                    installed.Parameters.AddWithValue("$version", request.ConsumerVersion);
                    await using SqliteDataReader reader = await installed.ExecuteReaderAsync(token).ConfigureAwait(false);
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)
                        || reader.GetString(0) != request.ConsumerChecksum || reader.GetString(1) != request.ContractId
                        || reader.GetInt32(2) != request.ContractVersion || reader.GetInt64(3) != request.ProjectionGeneration
                        || reader.GetInt32(4) != 0)
                        return OperationResults.StoreError<BaseSubjectLifecycleCheckpointResult>(new BaseError
                        {
                            Code = BaseSubjectErrorCodes.ProviderContractInvalid,
                            Message = "The subject lifecycle checkpoint authority is invalid.",
                            Category = ErrorCategory.Store,
                        });
                }

                BaseSubjectLifecycleOrderingBoundary? prior = null;
                long priorGeneration = 0;
                await using (SqliteCommand read = _connection.CreateCommand())
                {
                    read.Transaction = _transaction;
                    read.CommandTimeout = _owner.TimeoutSeconds();
                    read.CommandText = $"SELECT through_position,through_subject_id,through_authority_epoch,through_incarnation,through_sequence,checkpoint_generation,state,protected_scope_value FROM {_owner._names.SubjectLifecycleCheckpoints} WHERE consumer_id=$consumer AND consumer_version=$version AND scope_kind=$scopeKind AND scope_index_digest=$scopeDigest;";
                    read.Parameters.AddWithValue("$consumer", request.ConsumerId);
                    read.Parameters.AddWithValue("$version", request.ConsumerVersion);
                    SqliteRecordStore.AddScopeQuery(read, protectedScope);
                    await using SqliteDataReader reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
                    if (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (!_owner._subjectScopes.Matches(new BaseProtectedSubjectScope { Kind = request.Scope.Kind, IndexDigest = protectedScope.IndexDigest, ProtectedCanonicalValue = (byte[])reader.GetValue(7) }, request.Scope)
                            || reader.GetInt32(6) != 0)
                            return OperationResults.Conflict<BaseSubjectLifecycleCheckpointResult>(new BaseError { Code = BaseSubjectErrorCodes.CursorOvertaken, Message = "The subject lifecycle checkpoint is no longer current.", Category = ErrorCategory.Conflict });
                        priorGeneration = reader.GetInt64(5);
                        if (!reader.IsDBNull(0)) prior = new BaseSubjectLifecycleOrderingBoundary
                        {
                            CommitPosition = new(reader.GetInt64(0)),
                            SubjectId = BaseSubjectId.Create(reader.GetString(1), BaseSubjectIdKind.OrdinalString),
                            AuthorityEpoch = new((byte[])reader.GetValue(2)),
                            Incarnation = new((byte[])reader.GetValue(3)),
                            SubjectSequence = reader.GetInt64(4),
                        };
                    }
                }
                if (priorGeneration != request.ExpectedCheckpointGeneration
                    || prior is not null && request.Through is not null && CompareLifecycleBoundary(request.Through, prior) < 0)
                    return OperationResults.Conflict<BaseSubjectLifecycleCheckpointResult>(new BaseError { Code = BaseSubjectErrorCodes.CursorOvertaken, Message = "The subject lifecycle checkpoint is no longer current.", Category = ErrorCategory.Conflict });

                if (request.Through is { } through)
                {
                    await using SqliteCommand membership = _connection.CreateCommand();
                    membership.Transaction = _transaction; membership.CommandTimeout = _owner.TimeoutSeconds();
                    membership.CommandText = $"SELECT 1 FROM {_owner._names.SubjectLifecycleMemberships} WHERE consumer_id=$consumer AND consumer_version=$version AND consumer_checksum=$checksum AND contract_id=$contract AND contract_version=$contractVersion AND projection_generation=$projection AND scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND commit_position=$position AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation AND subject_sequence=$sequence LIMIT 1;";
                    membership.Parameters.AddWithValue("$consumer", request.ConsumerId); membership.Parameters.AddWithValue("$version", request.ConsumerVersion); membership.Parameters.AddWithValue("$checksum", request.ConsumerChecksum); membership.Parameters.AddWithValue("$contract", request.ContractId); membership.Parameters.AddWithValue("$contractVersion", request.ContractVersion); membership.Parameters.AddWithValue("$projection", request.ProjectionGeneration); SqliteRecordStore.AddScopeQuery(membership, protectedScope); membership.Parameters.AddWithValue("$position", through.CommitPosition.Value); membership.Parameters.AddWithValue("$subject", through.SubjectId.Value); membership.Parameters.Add("$epoch", SqliteType.Blob).Value = through.AuthorityEpoch.ToArray(); membership.Parameters.Add("$incarnation", SqliteType.Blob).Value = through.Incarnation.ToArray(); membership.Parameters.AddWithValue("$sequence", through.SubjectSequence);
                    if (await membership.ExecuteScalarAsync(token).ConfigureAwait(false) is null)
                        return OperationResults.ValidationFailed<BaseSubjectLifecycleCheckpointResult>(new BaseError { Code = BaseSubjectErrorCodes.CursorInvalid, Message = "The subject lifecycle checkpoint is invalid.", Category = ErrorCategory.Validation });
                }

                long generation = checked(priorGeneration + 1);
                DateTimeOffset now = _owner._timeProvider.GetUtcNow();
                BaseSubjectLifecycleOrderingBoundary? resulting = request.Through ?? prior;
                await using (SqliteCommand upsert = _connection.CreateCommand())
                {
                    upsert.Transaction = _transaction; upsert.CommandTimeout = _owner.TimeoutSeconds();
                    upsert.CommandText = $"INSERT INTO {_owner._names.SubjectLifecycleCheckpoints}(consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,scope_kind,scope_index_digest,protected_scope_value,through_position,through_subject_id,through_authority_epoch,through_incarnation,through_sequence,checkpoint_generation,advanced_at,state) VALUES($consumer,$version,$checksum,$contract,$contractVersion,$projection,$scopeKind,$scopeDigest,$scopeCiphertext,$position,$subject,$epoch,$incarnation,$sequence,$generation,$advanced,0) ON CONFLICT(consumer_id,consumer_version,scope_kind,scope_index_digest) DO UPDATE SET protected_scope_value=excluded.protected_scope_value,through_position=excluded.through_position,through_subject_id=excluded.through_subject_id,through_authority_epoch=excluded.through_authority_epoch,through_incarnation=excluded.through_incarnation,through_sequence=excluded.through_sequence,checkpoint_generation=excluded.checkpoint_generation,advanced_at=excluded.advanced_at WHERE checkpoint_generation=$expected;";
                    upsert.Parameters.AddWithValue("$consumer", request.ConsumerId); upsert.Parameters.AddWithValue("$version", request.ConsumerVersion); upsert.Parameters.AddWithValue("$checksum", request.ConsumerChecksum); upsert.Parameters.AddWithValue("$contract", request.ContractId); upsert.Parameters.AddWithValue("$contractVersion", request.ContractVersion); upsert.Parameters.AddWithValue("$projection", request.ProjectionGeneration); SqliteRecordStore.AddScopeWrite(upsert, protectedScope); upsert.Parameters.AddWithValue("$position", resulting is null ? DBNull.Value : resulting.CommitPosition.Value); upsert.Parameters.AddWithValue("$subject", resulting is null ? DBNull.Value : resulting.SubjectId.Value); upsert.Parameters.Add("$epoch", SqliteType.Blob).Value = resulting is null ? DBNull.Value : resulting.AuthorityEpoch.ToArray(); upsert.Parameters.Add("$incarnation", SqliteType.Blob).Value = resulting is null ? DBNull.Value : resulting.Incarnation.ToArray(); upsert.Parameters.AddWithValue("$sequence", resulting is null ? DBNull.Value : resulting.SubjectSequence); upsert.Parameters.AddWithValue("$generation", generation); upsert.Parameters.AddWithValue("$advanced", now.ToString("O", System.Globalization.CultureInfo.InvariantCulture)); upsert.Parameters.AddWithValue("$expected", priorGeneration);
                    if (await upsert.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        return OperationResults.Conflict<BaseSubjectLifecycleCheckpointResult>(new BaseError { Code = BaseSubjectErrorCodes.CursorOvertaken, Message = "The subject lifecycle checkpoint is no longer current.", Category = ErrorCategory.Conflict });
                }
                return OperationResults.Ok(new BaseSubjectLifecycleCheckpointResult { Through = resulting, CheckpointGeneration = generation, ProjectionGeneration = request.ProjectionGeneration, AdvancedAtUtc = now, Duplicate = false });
            });

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
                BaseOperationKind.Delete or BaseOperationKind.SubjectLifecycleFinalizeRetirement or BaseOperationKind.SubjectRetirementPurge =>
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
                        : operation is BaseOperationKind.Delete or BaseOperationKind.SubjectLifecycleFinalizeRetirement or BaseOperationKind.SubjectRetirementPurge
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
