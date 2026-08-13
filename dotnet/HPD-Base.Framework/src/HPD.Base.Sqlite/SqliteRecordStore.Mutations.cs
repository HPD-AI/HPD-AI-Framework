using System.Globalization;
using System.Diagnostics;
using System.Collections.Immutable;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
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
        private Dictionary<int, BaseSubjectIncarnation>? _preparedLifecycleIncarnations;
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
            BaseAtomicMutationIntent intent,
            CancellationToken cancellationToken = default) => ExecuteAsync(BaseOperationKind.Query, cancellationToken, async token =>
        {
            ArgumentNullException.ThrowIfNull(intent);
            if (_capturedMutation is not null || intent.Items.IsDefaultOrEmpty || intent.Items.Length > intent.Limits.MaximumItems)
                return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid);
            (string storeId, long restoreEpoch, long schemaGeneration) = await ReadAuthorityAsync(token).ConfigureAwait(false);
            if (!string.Equals(storeId, intent.Authority.StoreInstanceId, StringComparison.Ordinal) || restoreEpoch != intent.Authority.RestoreEpoch || schemaGeneration != intent.Authority.SchemaGeneration)
                return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);
            var items = ImmutableArray.CreateBuilder<BaseCapturedMutationItem>(intent.Items.Length);
            var intervals = ImmutableArray.CreateBuilder<BaseAtomicReadIntervalEvidence>(intent.Items.Length);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            digest.AppendData(System.Text.Encoding.UTF8.GetBytes(intent.IntentDigest));
            long selectedBytes = 0;
            long retainedBytes = CanonicalStringBytes(intent.IntentDigest)
                + CanonicalStringBytes(intent.Authority.ApplicationId)
                + CanonicalStringBytes(intent.Authority.StoreInstanceId)
                + sizeof(long) * 4L;
            var transactionRecords = new Dictionary<string, RecordEnvelope?>(StringComparer.Ordinal);
            for (int index = 0; index < intent.Items.Length; index++)
            {
                BaseAtomicMutationIntentItem item = intent.Items[index];
                if (item.Ordinal != index) return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid);
                string itemKey = CaptureRecordKey(item.Collection.Id, item.RecordId);
                retainedBytes = checked(retainedBytes + CanonicalStringBytes(item.Collection.Id)
                    + CanonicalStringBytes(item.RecordId.Value) + sizeof(int) * 3L);
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
                    retainedBytes = checked(retainedBytes + CanonicalStringBytes(relation.SourceFieldId)
                        + CanonicalStringBytes(relation.TargetCollection.Id)
                        + CanonicalStringBytes(relation.TargetRecordId.Value));
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
            long evidenceBytes = intervals.Sum(static interval => (long)interval.CanonicalLowerBound.Length + interval.CanonicalUpperBound.Length);
            long transient = checked(retainedBytes + selectedBytes + evidenceBytes);
            if (selectedBytes > intent.Limits.MaximumSelectedBytes || evidenceBytes > intent.Limits.MaximumEvidenceBytes || transient > intent.Limits.MaximumTransientBytes || intervals.Count > intent.Limits.MaximumReadIntervals)
                return SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);
            _capturedMutation = new BaseCapturedAtomicMutationAuthority
            {
                IntentDigest = new string(intent.IntentDigest.AsSpan()), CaptureDigest = Convert.ToHexStringLower(digest.GetHashAndReset()),
                Authority = new BaseAuthoritySnapshotEvidence
                {
                    ApplicationId = intent.Authority.ApplicationId, StoreInstanceId = storeId, RestoreEpoch = restoreEpoch,
                    SchemaGeneration = schemaGeneration, CollectionGeneration = intent.Authority.CollectionGeneration,
                    Isolation = BaseAtomicSelectionIsolationClass.WriteOwningSerializable,
                    TransactionEvidenceToken = BitConverter.GetBytes(_transactionStarted).ToImmutableArray(),
                },
                Items = items.MoveToImmutable(), ReadIntervals = intervals.ToImmutable(),
                Accounting = new BaseCaptureAccounting
                {
                    Records = checked(intent.Items.Length + intent.Items.Sum(static item => item.RelationTargets.Length)),
                    SelectedBytes = selectedBytes, ReadIntervals = intervals.Count,
                    EvidenceBytes = evidenceBytes, TransientBytes = transient,
                },
            };
            return OperationResults.Ok(_capturedMutation);
        });

        public ValueTask<OperationResult<BasePreparedAtomicMutation>> PrepareAtomicMutationAsync(
            BaseCapturedAtomicMutationAuthority captured, BaseAtomicMutationPlan plan,
            CancellationToken cancellationToken = default) => ExecuteAsync(BaseOperationKind.Query, cancellationToken, async token =>
        {
            token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(captured); ArgumentNullException.ThrowIfNull(plan);
            if (!ReferenceEquals(captured, _capturedMutation) || _preparedMutation is not null ||
                !string.Equals(plan.IntentDigest, captured.IntentDigest, StringComparison.Ordinal) ||
                !string.Equals(plan.CaptureDigest, captured.CaptureDigest, StringComparison.Ordinal) || plan.Items.Length != captured.Items.Length)
                return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
            var lifetimes = new Dictionary<string, SqlitePreparedSubjectLifetime?>(StringComparer.Ordinal);
            var overlays = new Dictionary<string, BasePreparedSubjectOverlayEvidence>(StringComparer.Ordinal);
            var lifecycleIncarnations = new Dictionary<int, BaseSubjectIncarnation>();
            var subjectAuthorities = new Dictionary<string, BaseSubjectTransactionAuthorityEvidence>(StringComparer.Ordinal);
            var intervals = captured.ReadIntervals.ToBuilder();
            int authorityReads = captured.Accounting.Records;
            long retainedBytes = checked(captured.Accounting.TransientBytes + EstimatePlanBytes(plan));
            foreach (BaseAtomicMutationPlanItem item in plan.Items)
            {
                if (item.SubjectLifecycle is not { } lifecycle) continue;
                SqliteSubjectContractState? contract = await ReadSubjectContractAsync(lifecycle.ContractId, lifecycle.ContractVersion, token).ConfigureAwait(false);
                authorityReads = checked(authorityReads + 1);
                if (contract is null || !string.Equals(contract.Checksum, lifecycle.ContractChecksum, StringComparison.Ordinal))
                    return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);
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
                    if (lifetime is not null) retainedBytes = checked(retainedBytes + EstimateLifetimeBytes(lifetime));
                }
                switch (lifecycle.Kind)
                {
                    case BaseSubjectLifecycleMutationKind.Create:
                        if (lifetime is not null)
                            return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.TransactionConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
                        lifetime = new SqlitePreparedSubjectLifetime(
                            lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId,
                            BaseSubjectIncarnation.Create(), item.Collection.Id, item.RecordId,
                            item.Ordinal + 1L);
                        lifetimes[key] = lifetime;
                        lifecycleIncarnations[item.Ordinal] = lifetime.Incarnation;
                        retainedBytes = checked(retainedBytes + sizeof(int) + 16);
                        break;
                    case BaseSubjectLifecycleMutationKind.Preserve:
                        if (lifetime is null) return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
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
                BaseExportedSubjectDefinition? definition = _owner._options.ExportedSubjects.FirstOrDefault(subject =>
                    string.Equals(subject.Id, lifecycle.ContractId, StringComparison.Ordinal) && subject.Version == lifecycle.ContractVersion);
                if (definition is null) return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                RecordEnvelope? privateRecord = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Retire ? null : PlanRecord(item);
                ReadLogicalValues(definition, privateRecord, out bool? active, out string? scope, out bool logicalStateValid);
                if (privateRecord is not null && !logicalStateValid)
                    return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
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
                        if (lifetime is not null) retainedBytes = checked(retainedBytes + EstimateLifetimeBytes(lifetime));
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
                        Incarnation = lifetime?.Incarnation, Active = active, Scope = scope,
                    };
                }
            }
            long evidenceBytes = intervals.Sum(static interval => (long)interval.CanonicalLowerBound.Length + interval.CanonicalUpperBound.Length);
            BasePreparedSubjectOverlayEvidence[] ownedOverlays = overlays.Values.ToArray();
            BaseSubjectTransactionAuthorityEvidence[] ownedAuthorities = subjectAuthorities.Values.ToArray();
            BasePreparedSubjectValidationEvidence[] ownedValidations = validationEvidence.ToArray();
            long addedEvidenceBytes = checked(evidenceBytes - captured.Accounting.EvidenceBytes);
            long transient = checked(retainedBytes + addedEvidenceBytes
                + ownedOverlays.Sum(EstimateOverlayBytes)
                + ownedAuthorities.Sum(EstimateAuthorityBytes)
                + ownedValidations.Sum(static value => sizeof(int) * 3L + CanonicalStringBytes(value.SourceFieldId)));
            int intervalCount = intervals.Count;
            if (authorityReads > plan.Limits.MaximumAuthorityReads || intervalCount > plan.Limits.MaximumReadIntervals
                || evidenceBytes > plan.Limits.MaximumEvidenceBytes || transient > plan.Limits.MaximumTransientBytes)
                return SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation);
            _preparedPlan = plan;
            _preparedLifecycleIncarnations = lifecycleIncarnations;
            _preparedMutation = new BasePreparedAtomicMutation
            {
                PlanDigest = new string(plan.PlanDigest.AsSpan()), Authority = captured.Authority with { },
                SubjectAuthorities = subjectAuthorities.Values.OrderBy(static value => value.ContractId, StringComparer.Ordinal)
                    .ThenBy(static value => value.ContractVersion).ToImmutableArray(),
                Dispositions = captured.Items.Select(static item => item.Disposition).ToImmutableArray(),
                SubjectOverlay = overlays.Values.OrderBy(static value => value.ContractId, StringComparer.Ordinal).ThenBy(static value => value.ContractVersion).ThenBy(static value => value.SubjectId.Value, StringComparer.Ordinal).ToImmutableArray(),
                SubjectValidations = validationEvidence.MoveToImmutable(),
                ReadIntervals = intervals.MoveToImmutable(),
                Accounting = new BasePreparedAtomicMutationAccounting
                {
                    AuthorityReads = authorityReads, ReadIntervals = intervalCount,
                    SelectedBytes = captured.Accounting.SelectedBytes, EvidenceBytes = evidenceBytes,
                    TransientBytes = transient,
                },
            };
            return OperationResults.Ok(_preparedMutation);
        });

        private static long CanonicalStringBytes(string? value) =>
            value is null ? sizeof(int) : checked(sizeof(int) + System.Text.Encoding.UTF8.GetByteCount(value));

        private static long EstimateLifetimeBytes(SqlitePreparedSubjectLifetime value) => checked(
            CanonicalStringBytes(value.ContractId)
            + sizeof(int)
            + CanonicalStringBytes(value.SubjectId.Value)
            + 16
            + CanonicalStringBytes(value.CollectionId)
            + CanonicalStringBytes(value.RecordId.Value)
            + sizeof(long));

        private static long EstimateOverlayBytes(BasePreparedSubjectOverlayEvidence value) => checked(
            CanonicalStringBytes(value.ContractId)
            + sizeof(int)
            + CanonicalStringBytes(value.SubjectId.Value)
            + sizeof(byte) * 3L
            + (value.Incarnation is null ? 0 : 16)
            + CanonicalStringBytes(value.Scope));

        private static long EstimateAuthorityBytes(BaseSubjectTransactionAuthorityEvidence value) => checked(
            CanonicalStringBytes(value.ContractId)
            + sizeof(int)
            + CanonicalStringBytes(value.ContractChecksum)
            + CanonicalStringBytes(value.StoreInstanceId)
            + sizeof(long) * 3L
            + 16);

        private static long EstimatePlanBytes(BaseAtomicMutationPlan plan)
        {
            long bytes = CanonicalStringBytes(plan.PlanDigest) + CanonicalStringBytes(plan.IntentDigest)
                + CanonicalStringBytes(plan.CaptureDigest) + sizeof(long) * 8L;
            foreach (BaseAtomicMutationPlanItem item in plan.Items)
            {
                bytes = checked(bytes + sizeof(int) * 3L + CanonicalStringBytes(item.Collection.Id)
                    + CanonicalStringBytes(item.RecordId.Value) + CanonicalStringBytes(item.EventId));
                if (item.ProposedPayload is not null)
                    bytes = checked(bytes + JsonSerializer.SerializeToUtf8Bytes(
                        item.ProposedPayload, HPDBaseJsonSerializerContext.Default.RecordPayload).LongLength);
            }
            foreach (BaseSubjectReferenceValidationPlanItem validation in plan.SubjectValidations)
                bytes = checked(bytes + sizeof(int) * 4L + CanonicalStringBytes(validation.SourceFieldId)
                    + CanonicalStringBytes(validation.ValidationPlanId)
                    + CanonicalStringBytes(validation.Reference.SubjectId.Value)
                    + CanonicalStringBytes(validation.Scope.Value) + 32);
            return bytes;
        }

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

        public ValueTask<OperationResult<BaseAppliedAtomicMutation>> ApplyPreparedAtomicMutationAsync(
            BasePreparedAtomicMutation prepared, CancellationToken cancellationToken = default) => ExecuteAsync(BaseOperationKind.Patch, cancellationToken, async token =>
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(prepared, _preparedMutation) || _preparedPlan is null)
                return SubjectFailure<BaseAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
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
                    return new OperationResult<BaseAppliedAtomicMutation> { Status = mutation.Status, Error = mutation.Error };
                if (item.SubjectLifecycle is { } lifecycle)
                {
                    BasePreparedSubjectOverlayEvidence? overlay = prepared.SubjectOverlay.FirstOrDefault(candidate =>
                        string.Equals(candidate.ContractId, lifecycle.ContractId, StringComparison.Ordinal)
                        && candidate.ContractVersion == lifecycle.ContractVersion
                        && candidate.SubjectId.Equals(lifecycle.SubjectId));
                    if (overlay is null)
                        return SubjectFailure<BaseAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                    OperationResult lifecycleResult = await ApplySubjectLifecycleAsync(
                        item,
                        lifecycle,
                        lifecycleIncarnations.TryGetValue(item.Ordinal, out BaseSubjectIncarnation incarnation)
                            ? incarnation
                            : null,
                        mutation.Value.Mutation.JournalPosition,
                        token).ConfigureAwait(false);
                    if (!lifecycleResult.IsSuccess())
                        return new OperationResult<BaseAppliedAtomicMutation> { Status = lifecycleResult.Status, Error = lifecycleResult.Error };
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
            BaseRecordMutationFact[] materialized = facts.Select(static fact => fact.MaterializeOwned()).ToArray();
            foreach (ISqliteAtomicMutationProjection contributor in _owner._mutationProjectionContributors)
            {
                var projectionContext = new SqliteAtomicProjectionContext(_owner, _connection, _transaction, (ISqliteAtomicMutationProjectionCatalog)contributor);
                OperationResult projected = await contributor.ApplyAsync(
                    projectionContext,
                    BaseAtomicMutationProjectionFactory.Create(materialized),
                    token).ConfigureAwait(false);
                if (!projected.IsSuccess())
                    return new OperationResult<BaseAppliedAtomicMutation> { Status = projected.Status, Error = projected.Error };
            }
            long journalBytes = materialized.Sum(static fact =>
                (long)JsonSerializer.SerializeToUtf8Bytes(fact, HPDBaseJsonSerializerContext.Default.BaseRecordMutationFact).LongLength);
            long transient = checked(prepared.Accounting.TransientBytes + writtenBytes + factBytes + journalBytes + _attributionTransientBytes);
            return OperationResults.Ok(new BaseAppliedAtomicMutation
            {
                PlanDigest = new string(plan.PlanDigest.AsSpan()),
                Authority = prepared.Authority with { },
                Facts = facts.MoveToImmutable(),
                Accounting = new BaseAtomicCommitAccounting
                {
                    WrittenBytes = writtenBytes,
                    FactBytes = factBytes,
                    JournalBytes = journalBytes,
                    ReceiptBytes = 0,
                    TransientBytes = transient,
                },
            });
        });

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

        /// <inheritdoc />
        public ValueTask<OperationResult<BaseAtomicSelectionResult>> SelectAsync(
            BaseAtomicSelectionRequest request,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(BaseOperationKind.Query, cancellationToken, token => SelectCoreAsync(request, token));

        private async ValueTask<OperationResult<BaseAtomicSelectionResult>> SelectCoreAsync(
            BaseAtomicSelectionRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Authority.StoreInstanceId)
                || request.Authority.RestoreEpoch < 0
                || request.Limits.MaximumRecords < 1
                || request.Limits.MaximumSelectedBytes < 1
                || request.Limits.MaximumReadIntervals < 1
                || request.Limits.MaximumTransientBytes < 1
                || request.Limits.MaximumUniqueConstraintChecks < 1
                || request.CanonicalRecordCodecVersion < 1)
            {
                return SelectionFailure(OperationStatus.ValidationFailed,
                    "base.provider.selection.authorityInvalid", ErrorCategory.Validation);
            }
            _selectionUniqueCheckLimit = request.Limits.MaximumUniqueConstraintChecks;
            _selectionTransientLimit = request.Limits.MaximumTransientBytes;
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
                if (!string.Equals(actualStoreInstanceId, request.Authority.StoreInstanceId, StringComparison.Ordinal)
                    || actualRestoreEpoch != request.Authority.RestoreEpoch
                    || actualSchemaGeneration != request.Authority.SchemaGeneration
                    || actualCollectionGeneration != request.Authority.CollectionGeneration)
                    return SelectionFailure(OperationStatus.Conflict, "base.provider.selection.authorityChanged", ErrorCategory.Conflict);
            }

            SqlitePhysicalModel.CollectionModel physical = _owner._physical.Collection(request.Collection.Id);
            SqliteQueryPlan plan = new SqliteQueryPlanner(_owner._options, physical).Plan(request.Query);
            if (!plan.Supported)
                return SelectionFailure(OperationStatus.Unsupported,
                    "base.provider.selection.queryUnsupported", ErrorCategory.Unsupported);
            int requested = request.Query.Page?.Limit ?? request.Limits.MaximumRecords;
            if (requested < 1 || requested > request.Limits.MaximumRecords)
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
                if (bytes > request.Limits.MaximumSelectedBytes || bytes > request.Limits.MaximumTransientBytes)
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
            var selectionAuthority = new BaseAuthoritySnapshotEvidence
            {
                ApplicationId = request.Authority.ApplicationId,
                StoreInstanceId = actualStoreInstanceId,
                RestoreEpoch = actualRestoreEpoch,
                SchemaGeneration = actualSchemaGeneration,
                CollectionGeneration = actualCollectionGeneration,
                Isolation = BaseAtomicSelectionIsolationClass.WriteOwningSerializable,
                TransactionEvidenceToken = BitConverter.GetBytes(_transactionStarted).ToImmutableArray(),
            };
            string selectionIntentDigest = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                $"hpd.base.selection-capture.v1\n{request.Authority.ApplicationId}\n{request.Collection.Id}\n{selectedCount}")));
            string selectionCaptureDigest = Convert.ToHexStringLower(SHA256.HashData(
                selectedRecords.SelectMany(static record => record.CopyCanonicalBytes()).Concat(boundary).ToArray()));
            _capturedMutation = new BaseCapturedAtomicMutationAuthority
            {
                IntentDigest = selectionIntentDigest,
                CaptureDigest = selectionCaptureDigest,
                Authority = selectionAuthority,
                Items = selectedRecords.Select((record, index) => new BaseCapturedMutationItem
                {
                    Ordinal = index,
                    CollectionId = request.Collection.Id,
                    RecordId = new RecordId(record.RecordId),
                    Disposition = BaseCapturedMutationDisposition.Update,
                    Current = record.MaterializeOwned(),
                    RelationTargets = [],
                }).ToImmutableArray(),
                ReadIntervals = [interval],
                Accounting = new BaseCaptureAccounting
                {
                    Records = selectedCount,
                    SelectedBytes = bytes,
                    ReadIntervals = 1,
                    EvidenceBytes = boundary.LongLength,
                    TransientBytes = _selectionRetainedBytes,
                },
            };
            return OperationResults.Ok(new BaseAtomicSelectionResult
            {
                MutationCapture = _capturedMutation,
                Authority = selectionAuthority,
                Records = selectedRecords,
                ReadIntervals = [interval],
                CanonicalOrderBoundary = boundary.ToImmutableArray(),
                Accounting = new BaseAtomicSelectionAccounting
                {
                    SelectedRecords = selectedCount,
                    SelectedBytes = bytes,
                    ReadIntervals = 1,
                    EvidenceBytes = boundary.LongLength,
                },
            });
        }

        private static OperationResult<BaseAtomicSelectionResult> SelectionFailure(
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
