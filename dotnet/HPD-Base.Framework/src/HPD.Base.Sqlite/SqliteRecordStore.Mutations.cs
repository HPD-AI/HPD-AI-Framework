using System.Globalization;
using System.Diagnostics;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Results;
using HPD.Base.Schema;
using HPD.Base.Sqlite.Internal;
using HPD.Base.Stores;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

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
        SqliteConnection? connection = null;
        SqliteTransaction? transaction = null;
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return FailedBeforeCommit(ProviderError(
                    SqliteErrorCodes.DatabaseUnavailable,
                    "SQLite mutation execution is unavailable."));

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

            return CancelledBeforeCommit();
        }
        catch (SqliteException ex)
        {
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
            executionSlot?.Dispose();

            if (IsTransactionConflict(ex))
                return ConflictBeforeCommit();

            return FailedBeforeCommit(MapSqlite<object>(BaseOperationKind.Batch, ex).Error!);
        }
        catch (ObjectDisposedException)
        {
            executionSlot?.Dispose();
            return FailedBeforeCommit(ProviderError(
                SqliteErrorCodes.DatabaseUnavailable,
                "SQLite mutation execution is unavailable."));
        }
        catch (InvalidOperationException ex)
        {
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
            executionSlot?.Dispose();

            return FailedBeforeCommit(MapSchemaFailure<object>(ex).Error!);
        }

        var resources = new TransactionResources(this, connection, transaction, executionSlot!);
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
            try
            {
                var processingTask =
                    processor.ProcessAsync(session, processingLifetime.Token).AsTask();
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
                    processing);
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
    }

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
        MutationExecutionSlot executionSlot)
    {
        private bool _transferred;

        public void TransferTo(Task operation)
        {
            _transferred = true;
            owner.TrackQuarantinedMutation(DisposeAfterCompletionAsync(operation), this);
        }

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

        public ValueTask<OperationResult<RecordMutationSessionResult>> CreateAsync(
            CollectionDefinition collection,
            RecordCreateRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                BaseOperationKind.Create,
                cancellationToken,
                token => CreateCoreAsync(collection, request, context, token));

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
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
                return SqliteResultFactory.Unsupported<RecordMutationSessionResult>(
                    SqliteErrorCodes.IdempotencyUnsupported,
                    "Idempotency keys are not supported by HPD.BASE SQLite.");

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
            var payloadJson = SqliteRecordSerializer.Serialize(request.Payload);
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = $"INSERT INTO {_owner._names.Records}(collection_id, record_id, revision, created_at, updated_at, payload_json) VALUES ($collection, $id, 1, $created, $updated, $payload);";
            command.CommandTimeout = CommandTimeoutSeconds();
            command.Parameters.AddWithValue("$collection", collection.Id);
            command.Parameters.AddWithValue("$id", id.Value);
            command.Parameters.AddWithValue("$created", now.ToString("O"));
            command.Parameters.AddWithValue("$updated", now.ToString("O"));
            command.Parameters.AddWithValue("$payload", payloadJson);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            var metadata = SqliteRecordMapper.Metadata(1, now, now, _owner._options.StoreId);
            var after = new RecordEnvelope
            {
                CollectionId = collection.Id,
                Id = id,
                Payload = SqliteRecordSerializer.Deserialize(payloadJson),
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
            var payloadJson = SqliteRecordSerializer.Serialize(nextPayload);
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = $"UPDATE {_owner._names.Records} SET revision = $revision, updated_at = $updated, payload_json = $payload WHERE collection_id = $collection AND record_id = $id;";
            command.CommandTimeout = CommandTimeoutSeconds();
            command.Parameters.AddWithValue("$revision", nextRevision);
            command.Parameters.AddWithValue("$updated", now.ToString("O"));
            command.Parameters.AddWithValue("$payload", payloadJson);
            command.Parameters.AddWithValue("$collection", collection.Id);
            command.Parameters.AddWithValue("$id", id.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            var metadata = SqliteRecordMapper.Metadata(
                nextRevision,
                before.Metadata.CreatedAt!.Value,
                now,
                _owner._options.StoreId);
            var after = before with
            {
                Payload = SqliteRecordSerializer.Deserialize(payloadJson),
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

            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = $"DELETE FROM {_owner._names.Records} WHERE collection_id = $collection AND record_id = $id;";
            command.CommandTimeout = CommandTimeoutSeconds();
            command.Parameters.AddWithValue("$collection", collection.Id);
            command.Parameters.AddWithValue("$id", id.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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
