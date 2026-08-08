using System.Diagnostics;

namespace HPD.Base;

internal sealed class DefaultBasePurgeProcessor(
    BaseMutationCommand[] commands,
    PrincipalContext principal,
    IBasePolicyOrchestrator policy,
    IBaseResultNormalizer normalizer,
    long? expectedGeneration,
    TimeSpan transactionTimeout) : IAtomicMutationProcessor
{
    private readonly List<BaseMutationAttempt> _attempts = [];
    private long _deadline;

    internal IReadOnlyList<BaseMutationAttempt> Attempts => _attempts;
    internal long PurgeGeneration { get; private set; }

    public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseRecordMutationFact[] committedMutations,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Failed(BaseCollectionErrorCodes.PurgeFailed, "Administrative purge receipts are not replayable."));
    }

    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
        IAtomicRecordSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_attempts.Count != 0 || commands.Length == 0)
            return Failed(BaseCollectionErrorCodes.PurgeInvalid, "A purge processor can only be invoked once.");

        _deadline = Stopwatch.GetTimestamp() + (long)(transactionTimeout.TotalSeconds * Stopwatch.Frequency);
        foreach (BaseMutationCommand command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetTimestamp() >= _deadline)
                return Failed(BaseCollectionErrorCodes.PurgeFailed, "The administrative purge exceeded its transaction lifetime.");

            OperationResult<RecordEnvelope> read = normalizer.NormalizeStoreResult(
                await session.GetAsync(command.Collection, command.RecordId!.Value, command.Context, cancellationToken).ConfigureAwait(false),
                command.Context);
            if (read.Status == OperationStatus.NotFound)
                continue;
            if (!read.IsSuccess() || read.Value is null)
                return Failed(read.Error ?? Error(BaseCollectionErrorCodes.PurgeFailed, "The administrative purge could not resolve a record.", ErrorCategory.Store));

            OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateWriteAsync(new BasePolicyRequest
            {
                Principal = principal,
                Operation = command.Context,
                Collection = command.Collection,
                ResourceKind = PolicyResourceKind.DeleteCandidate,
                RecordId = command.RecordId,
                ExistingRecord = read.Value,
            }, cancellationToken).ConfigureAwait(false);
            if (!authorization.IsSuccess() || authorization.Value is null
                || !BaseRecordFilterMatcher.Matches(read.Value, authorization.Value.EffectiveRecordFilter)
                || WriteCheckDenied(read.Value.Payload, authorization.Value))
            {
                return Failed(Error(BaseCollectionErrorCodes.PurgeForbidden, "The administrative purge is not authorized.", ErrorCategory.Authorization));
            }

            OperationResult<RecordMutationSessionResult> deleted = normalizer.NormalizeStoreResult(
                await session.DeleteAsync(
                    command.Collection,
                    command.RecordId.Value,
                    new RecordDeleteRequest { ReturnPrevious = true },
                    new RecordMutationSessionContext
                    {
                        ItemId = command.ItemId,
                        RequestedOperation = BaseRecordMutationKind.Purge,
                        EventId = command.EventId,
                        Operation = command.Context,
                    },
                    cancellationToken).ConfigureAwait(false),
                command.Context);
            if (!deleted.IsSuccess() || deleted.Value is null)
            {
                BaseError failure = deleted.Error?.Code is "base.relation.deleteRestricted"
                    ? Error(BaseCollectionErrorCodes.PurgeRestricted, "The administrative purge is restricted by a relation.", ErrorCategory.Conflict)
                    : deleted.Error ?? Error(BaseCollectionErrorCodes.PurgeFailed, "The administrative purge failed.", ErrorCategory.Store);
                return Failed(failure);
            }

            BaseRecordMutationFact mutation = deleted.Value.Mutation;
            if (!Valid(command, mutation))
                return Failed(BaseCollectionErrorCodes.PurgeFailed, "The provider returned an inconsistent purge mutation fact.");

            _attempts.Add(new BaseMutationAttempt
            {
                Command = command,
                Status = OperationStatus.Deleted,
                Mutation = mutation,
                Policy = authorization.Value,
                Revision = deleted.Revision,
            });
        }

        OperationResult<long> generation = await session.AdvancePurgeGenerationAsync(
            commands[0].Collection,
            expectedGeneration,
            cancellationToken).ConfigureAwait(false);
        if (!generation.IsSuccess() || generation.Value <= 0)
            return Failed(generation.Error ?? Error(BaseCollectionErrorCodes.PurgeFailed, "The purge generation could not be advanced.", ErrorCategory.Store));

        PurgeGeneration = generation.Value;
        BaseRecordMutationFact[] mutations = _attempts.Select(static attempt => attempt.Mutation!).ToArray();
        OperationResult projections = await session.ApplyMutationProjectionsAsync(
            BaseAtomicMutationProjectionFactory.Create(
                mutations,
                BaseAtomicMutationProjectionFactory.Purge(
                    commands[0].Collection.Id,
                    generation.Value - 1,
                    generation.Value)),
            cancellationToken).ConfigureAwait(false);
        if (!projections.IsSuccess())
            return Failed(projections.Error ?? Error(
                "base.runtime.mutationProjectionFailed",
                "A transactional mutation projection failed.",
                ErrorCategory.Store));

        return new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.ReadyToCommit,
            mutations);
    }

    private static bool WriteCheckDenied(RecordPayload payload, BasePolicyEvaluation evaluation) =>
        evaluation.Decision.Constraints?.WriteCheck is { } check
        && BasePolicyWriteConstraintEvaluator.Evaluate(payload, check) != BasePolicyWriteCheckEvaluation.Allowed;

    private static bool Valid(BaseMutationCommand command, BaseRecordMutationFact mutation) =>
        mutation.RequestedOperation == BaseRecordMutationKind.Purge
        && mutation.CommittedOperation == BaseCommittedRecordMutationKind.Delete
        && mutation.Delete?.Deleted == true
        && mutation.Before is not null
        && mutation.After is null
        && string.Equals(mutation.Collection.Id, command.Collection.Id, StringComparison.Ordinal)
        && string.Equals(mutation.ItemId, command.ItemId, StringComparison.Ordinal)
        && string.Equals(mutation.Event.EventId, command.EventId, StringComparison.Ordinal);

    private static AtomicMutationProcessingResult Failed(string code, string message) =>
        Failed(Error(code, message, ErrorCategory.Store));

    private static AtomicMutationProcessingResult Failed(BaseError error) =>
        new(AtomicMutationProcessingOutcome.Failed, [], error);

    private static BaseError Error(string code, string message, ErrorCategory category) => new()
    {
        Code = code,
        Message = message,
        Category = category,
        Store = category == ErrorCategory.Store ? new StoreErrorInfo { Retryable = false } : null,
    };
}
