using System.Globalization;
using System.Diagnostics;
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

        var resources = new TransactionResources(this, connection, transaction, executionSlot!, generationLease!);
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
                    : processor.ResolveReceiptAsync(receipt.Mutations, processingLifetime.Token).AsTask();
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
        BaseRecordMutationFact[]? mutations = JsonSerializer.Deserialize(result, HPDBaseJsonSerializerContext.Default.BaseRecordMutationFactArray);
        if (fingerprint.Length != 32 || structuralDigest.Length != 32 || mutations is null)
            throw new InvalidOperationException("SQLite receipt state is malformed.");
        return new SqliteMutationReceipt(fingerprint, structuralDigest, mutations);
    }

    private async ValueTask InsertReceiptAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BaseAtomicMutationExecutionRequest request,
        AtomicMutationProcessingResult processing,
        CancellationToken cancellationToken)
    {
        byte[] result = JsonSerializer.SerializeToUtf8Bytes(processing.Mutations, HPDBaseJsonSerializerContext.Default.BaseRecordMutationFactArray);
        if (result.Length > request.MaxReceiptBytes)
            throw new BaseReceiptTooLargeException();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"INSERT INTO {_names.OperationReceipts}(scope,operation,idempotency_key,fingerprint,structural_digest,result_json,result_format_version,schema_generation,store_instance_id,committed_at,expires_at) VALUES($scope,$operation,$key,$fingerprint,$structure,$result,1,$generation,$store,$committed,$expires);";
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

    private sealed record SqliteMutationReceipt(byte[] Fingerprint, byte[] StructuralDigest, BaseRecordMutationFact[] Mutations);
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
            return new RecordMutationExecutionResult(confirmedOutcome, processing);
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
        IAsyncDisposable generationLease)
    {
        private bool _transferred;

        /// <summary>Executes the transfer to operation.</summary>
        public void TransferTo(Task operation)
        {
            _transferred = true;
            owner.TrackQuarantinedMutation(DisposeAfterCompletionAsync(operation), this);
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
                    owner.TrackQuarantinedMutation(cleanup, this);
            }
            catch
            {
                if (!cleanup.IsCompleted)
                    owner.TrackQuarantinedMutation(cleanup, this);
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

    private sealed class SqliteAtomicRecordSession : IAtomicRecordSession
    {
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
                && ex.SqliteErrorCode == 19)
            {
                return SqliteResultFactory.DuplicateId<T>("record");
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

        private async ValueTask<OperationResult<RecordMutationSessionResult>> CreateCoreAsync(
            CollectionDefinition collection,
            RecordCreateRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(collection);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(context);

            if (SqliteValidation.ValidateCollectionId<RecordMutationSessionResult>(collection.Id) is { } collectionError)
                return collectionError;
            if (_owner.ValidateRegisteredCollection<RecordMutationSessionResult>(collection.Id) is { } registrationError)
                return registrationError;
            var id = request.RequestedId ?? new RecordId(NextRecordId());
            if (request.RequestedId is not null && !_owner._options.AllowClientRequestedIds)
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
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

        private async ValueTask<bool> HasRestrictedIncomingReferenceAsync(string collectionId, string targetRecordId, CancellationToken cancellationToken)
        {
            foreach (SqlitePhysicalModel.RelationModel relation in _owner._physical.RelationsTo(collectionId)
                .Where(static relation => relation.Definition.DeleteBehavior == BaseRelationDeleteBehavior.Restrict))
            {
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
            EventReference committedEvent)
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
                Event = committedEvent,
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
