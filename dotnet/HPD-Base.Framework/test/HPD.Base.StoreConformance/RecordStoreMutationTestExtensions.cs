using System.Globalization;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Stores;

/// <summary>
/// Test-only helpers that exercise provider mutations through the final L30
/// processor/session boundary.
/// </summary>
public static class RecordStoreMutationTestExtensions
{
    private static readonly RecordMutationExecutionRequest ExecutionRequest = new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        TransactionTimeout = TimeSpan.FromSeconds(30),
        CommitCompletionTimeout = TimeSpan.FromSeconds(5)
    };

    /// <summary>Executes a conformance create through the canonical single-mutation boundary.</summary>
    public static async ValueTask<OperationResult<RecordEnvelope>> CreateAsync(
        this IRecordStore store,
        CollectionDefinition collection,
        RecordCreateRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            store,
            (session, mutationContext, token) =>
                session.CreateAsync(collection, request, mutationContext, token),
            BaseRecordMutationKind.Create,
            context,
            cancellationToken).ConfigureAwait(false);

        return Project(result, static value => value.Record);
    }

    /// <summary>Executes a conformance patch through the canonical single-mutation boundary.</summary>
    public static async ValueTask<OperationResult<RecordEnvelope>> PatchAsync(
        this IRecordStore store,
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            store,
            (session, mutationContext, token) =>
                session.PatchAsync(collection, id, request, mutationContext, token),
            BaseRecordMutationKind.Patch,
            context,
            cancellationToken).ConfigureAwait(false);

        return Project(result, static value => value.Record);
    }

    /// <summary>Executes a conformance replacement through the canonical single-mutation boundary.</summary>
    public static async ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(
        this IRecordStore store,
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            store,
            (session, mutationContext, token) =>
                session.ReplaceAsync(collection, id, request, mutationContext, token),
            BaseRecordMutationKind.Replace,
            context,
            cancellationToken).ConfigureAwait(false);

        return Project(result, static value => value.Record);
    }

    /// <summary>Executes a conformance delete through the canonical single-mutation boundary.</summary>
    public static async ValueTask<OperationResult<DeleteResult>> DeleteAsync(
        this IRecordStore store,
        CollectionDefinition collection,
        RecordId id,
        RecordDeleteRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            store,
            (session, mutationContext, token) =>
                session.DeleteAsync(collection, id, request, mutationContext, token),
            BaseRecordMutationKind.Delete,
            context,
            cancellationToken).ConfigureAwait(false);

        return Project(result, static value => value.Delete);
    }

    private static async ValueTask<OperationResult<RecordMutationSessionResult>> ExecuteAsync(
        IRecordStore store,
        Func<IAtomicRecordSession, RecordMutationSessionContext, CancellationToken,
            ValueTask<OperationResult<RecordMutationSessionResult>>> operation,
        BaseRecordMutationKind kind,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        if (store is not IRecordMutationStore mutationStore)
        {
            return new OperationResult<RecordMutationSessionResult>
            {
                Status = OperationStatus.Unsupported,
                Error = new BaseError
                {
                    Code = BaseMutationErrorCodes.UpsertUnsupported,
                    Message = "The record store does not implement the mutation executor.",
                    Category = ErrorCategory.Unsupported
                }
            };
        }

        var processor = new SingleMutationProcessor(operation, kind, context);
        var execution = await mutationStore.ExecuteSingleAsync(
            processor,
            ExecutionRequest,
            cancellationToken).ConfigureAwait(false);

        if (execution.Outcome == RecordMutationExecutionOutcome.Committed)
        {
            return processor.Result ?? Failure(
                OperationStatus.StoreError,
                execution,
                "base.store.mutation.invalidResult",
                "The provider returned a committed execution without a processor result.");
        }

        if (processor.Result is { } processorResult && !processorResult.IsSuccess())
            return processorResult;

        if (execution.Outcome == RecordMutationExecutionOutcome.ConflictRollbackConfirmed)
        {
            return Failure(
                OperationStatus.Conflict,
                execution,
                BaseMutationErrorCodes.TransactionConflict,
                "The provider confirmed rollback after a transaction conflict.");
        }

        if (execution.Outcome == RecordMutationExecutionOutcome.Indeterminate)
        {
            var error = execution.Error ?? new BaseError
            {
                Code = BaseMutationErrorCodes.BatchIndeterminate,
                Message = "The provider could not determine whether the mutation committed.",
                Category = ErrorCategory.Store,
                Store = new StoreErrorInfo { Retryable = false }
            };
            return new OperationResult<RecordMutationSessionResult>
            {
                Status = OperationStatus.StoreError,
                Error = error with
                {
                    Store = (error.Store ?? new StoreErrorInfo()) with { Retryable = false }
                }
            };
        }

        return Failure(
            OperationStatus.StoreError,
            execution,
            execution.Outcome == RecordMutationExecutionOutcome.CancelledRollbackConfirmed
                ? BaseMutationErrorCodes.TransactionTimeout
                : "base.store.mutation.failed",
            execution.Outcome == RecordMutationExecutionOutcome.CancelledRollbackConfirmed
                ? "The provider confirmed rollback after cancellation."
                : "The provider confirmed rollback after a mutation failure.");
    }

    private static OperationResult<RecordMutationSessionResult> Failure(
        OperationStatus status,
        RecordMutationExecutionResult execution,
        string fallbackCode,
        string fallbackMessage) => new()
    {
        Status = status,
        Error = execution.Error ?? execution.Processing?.Error ?? new BaseError
        {
            Code = fallbackCode,
            Message = fallbackMessage,
            Category = status == OperationStatus.Conflict
                ? ErrorCategory.Conflict
                : ErrorCategory.Store
        }
    };

    private static OperationResult<T> Project<T>(
        OperationResult<RecordMutationSessionResult> source,
        Func<RecordMutationSessionResult, T?> value)
        where T : class =>
        new()
        {
            Status = source.Status,
            Value = source.Value is null ? null : value(source.Value),
            Error = source.Error,
            Warnings = source.Warnings,
            Diagnostics = source.Diagnostics,
            Revision = source.Revision,
            Events = source.Events
        };

    private sealed class SingleMutationProcessor(
        Func<IAtomicRecordSession, RecordMutationSessionContext, CancellationToken,
            ValueTask<OperationResult<RecordMutationSessionResult>>> operation,
        BaseRecordMutationKind kind,
        OperationContext context) : IAtomicMutationProcessor
    {
        public OperationResult<RecordMutationSessionResult>? Result { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            Result = await operation(
                session,
                new RecordMutationSessionContext
                {
                    RequestedOperation = kind,
                    EventId = "evt_conformance_"
                        + Interlocked.Increment(ref _eventSequence).ToString(CultureInfo.InvariantCulture),
                    Operation = context
                },
                cancellationToken).ConfigureAwait(false);

            if (!Result.IsSuccess() || Result.Value is null)
            {
                return new AtomicMutationProcessingResult(
                    AtomicMutationProcessingOutcome.Failed,
                    [],
                    Result.Error ?? new BaseError
                    {
                        Code = "base.store.mutation.failed",
                        Message = "The provider mutation failed.",
                        Category = ErrorCategory.Store
                    });
            }

            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.ReadyToCommit,
                [Result.Value.Mutation]);
        }

        private static long _eventSequence;
    }
}
