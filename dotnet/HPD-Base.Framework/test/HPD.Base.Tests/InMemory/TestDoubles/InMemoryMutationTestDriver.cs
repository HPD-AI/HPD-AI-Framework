namespace HPD.Base.Tests.InMemory.TestDoubles;

internal static class InMemoryMutationTestDriver
{
    private static readonly RecordMutationExecutionRequest ExecutionRequest = new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitCompletionTimeout = TimeSpan.FromSeconds(5)
    };

    private static long _nextEventId;

    public static ValueTask<OperationResult<RecordEnvelope>> CreateAsync(
        IRecordMutationStore store,
        CollectionDefinition collection,
        RecordCreateRequest request,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            store,
            BaseRecordMutationKind.Create,
            (session, context, token) =>
                session.CreateAsync(collection, request, context, token),
            static value => value.Record!,
            operation,
            cancellationToken);

    public static ValueTask<OperationResult<RecordEnvelope>> PatchAsync(
        IRecordMutationStore store,
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            store,
            BaseRecordMutationKind.Patch,
            (session, context, token) =>
                session.PatchAsync(collection, id, request, context, token),
            static value => value.Record!,
            operation,
            cancellationToken);

    public static ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(
        IRecordMutationStore store,
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            store,
            BaseRecordMutationKind.Replace,
            (session, context, token) =>
                session.ReplaceAsync(collection, id, request, context, token),
            static value => value.Record!,
            operation,
            cancellationToken);

    public static ValueTask<OperationResult<DeleteResult>> DeleteAsync(
        IRecordMutationStore store,
        CollectionDefinition collection,
        RecordId id,
        RecordDeleteRequest request,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            store,
            BaseRecordMutationKind.Delete,
            (session, context, token) =>
                session.DeleteAsync(collection, id, request, context, token),
            static value => value.Delete!,
            operation,
            cancellationToken);

    private static async ValueTask<OperationResult<T>> ExecuteAsync<T>(
        IRecordMutationStore store,
        BaseRecordMutationKind mutation,
        Func<
            IAtomicRecordSession,
            RecordMutationSessionContext,
            CancellationToken,
            ValueTask<OperationResult<RecordMutationSessionResult>>> invoke,
        Func<RecordMutationSessionResult, T> select,
        OperationContext operation,
        CancellationToken cancellationToken)
    {
        var processor = new CapturingProcessor(
            invoke,
            new RecordMutationSessionContext
            {
                RequestedOperation = mutation,
                EventId = $"test:{Interlocked.Increment(ref _nextEventId)}",
                Operation = operation
            });
        var execution = await store.ExecuteSingleAsync(
            processor,
            ExecutionRequest,
            cancellationToken);
        var sessionResult = processor.Result;
        if (sessionResult is not null)
        {
            return new OperationResult<T>
            {
                Status = sessionResult.Status,
                Value = sessionResult.Value is null ? default : select(sessionResult.Value),
                Error = sessionResult.Error,
                Warnings = sessionResult.Warnings,
                Diagnostics = sessionResult.Diagnostics,
                Revision = sessionResult.Revision,
                Events = sessionResult.Events
            };
        }

        return new OperationResult<T>
        {
            Status = execution.Outcome == RecordMutationExecutionOutcome.ConflictRollbackConfirmed
                ? OperationStatus.Conflict
                : OperationStatus.StoreError,
            Error = execution.Error
        };
    }

    private sealed class CapturingProcessor(
        Func<
            IAtomicRecordSession,
            RecordMutationSessionContext,
            CancellationToken,
            ValueTask<OperationResult<RecordMutationSessionResult>>> invoke,
        RecordMutationSessionContext context) : IAtomicMutationProcessor
    {
        public OperationResult<RecordMutationSessionResult>? Result { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            Result = await invoke(session, context, cancellationToken);
            return Result.Value is not null
                ? new AtomicMutationProcessingResult(
                    AtomicMutationProcessingOutcome.ReadyToCommit,
                    [Result.Value.Mutation])
                : new AtomicMutationProcessingResult(
                    AtomicMutationProcessingOutcome.Failed,
                    [],
                    Result.Error ?? new BaseError
                    {
                        Code = "test.inmemory.mutation.failed",
                        Message = "The test mutation failed without a bounded error.",
                        Category = ErrorCategory.Unexpected
                    });
        }
    }
}
