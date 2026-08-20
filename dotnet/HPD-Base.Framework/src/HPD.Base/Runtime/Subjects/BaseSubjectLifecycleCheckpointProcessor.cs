namespace HPD.Base;

internal sealed class BaseSubjectLifecycleCheckpointProcessor(
    BaseSubjectLifecycleProviderCheckpointRequest request) : IAtomicMutationProcessor
{
    internal BaseSubjectLifecycleCheckpointResult? Result { get; private set; }

    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
        IAtomicRecordSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        OperationResult<BaseSubjectLifecycleCheckpointResult> advanced =
            await session.AdvanceSubjectLifecycleCheckpointAsync(request, cancellationToken).ConfigureAwait(false);
        if (!advanced.IsSuccess() || advanced.Value is null)
            return Failed(advanced.Error ?? Error(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Store));

        if (!ValidFreshResult(advanced.Value))
            return Failed(Error(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, ErrorCategory.Capability));

        Result = BaseSubjectLifecycleReceiptOwnership.Clone(advanced.Value);
        return new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.ReadyToCommit,
            new BaseAtomicReceiptResult
            {
                Kind = BaseAtomicReceiptResultKind.SubjectLifecycleCheckpoint,
                Mutations = [],
                SubjectLifecycleCheckpoint = BaseSubjectLifecycleReceiptOwnership.Clone(Result),
            });
    }

    public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseAtomicReceiptResult committedResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (committedResult.Kind != BaseAtomicReceiptResultKind.SubjectLifecycleCheckpoint
            || committedResult.SubjectLifecycleCheckpoint is null
            || committedResult.Mutations.Length != 0
            || !ValidStoredResult(committedResult.SubjectLifecycleCheckpoint))
            return ValueTask.FromResult(Failed(Error(BaseMutationRequestErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization)));

        Result = BaseSubjectLifecycleReceiptOwnership.Clone(committedResult.SubjectLifecycleCheckpoint) with { Duplicate = true };
        return ValueTask.FromResult(new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.ReadyToCommit,
            new BaseAtomicReceiptResult
            {
                Kind = BaseAtomicReceiptResultKind.SubjectLifecycleCheckpoint,
                Mutations = [],
                SubjectLifecycleCheckpoint = BaseSubjectLifecycleReceiptOwnership.Clone(Result),
            }));
    }

    private static AtomicMutationProcessingResult Failed(BaseError error) =>
        new(AtomicMutationProcessingOutcome.Failed, [], error);

    private bool ValidFreshResult(BaseSubjectLifecycleCheckpointResult value)
    {
        long expectedGeneration;
        try { expectedGeneration = checked(request.ExpectedCheckpointGeneration + 1); }
        catch (OverflowException) { return false; }
        return !value.Duplicate
            && value.CheckpointGeneration == expectedGeneration
            && value.ProjectionGeneration == request.ProjectionGeneration
            && value.AdvancedAtUtc != default
            && value.AdvancedAtUtc.Offset == TimeSpan.Zero
            && (request.Through is null || SameBoundary(value.Through, request.Through));
    }

    private static bool ValidStoredResult(BaseSubjectLifecycleCheckpointResult value) =>
        value.CheckpointGeneration > 0
        && value.ProjectionGeneration > 0
        && value.AdvancedAtUtc != default
        && value.AdvancedAtUtc.Offset == TimeSpan.Zero
        && !value.Duplicate
        && ValidBoundary(value.Through);

    private static bool SameBoundary(BaseSubjectLifecycleOrderingBoundary? left, BaseSubjectLifecycleOrderingBoundary right) =>
        left is not null
        && left.CommitPosition.Equals(right.CommitPosition)
        && left.SubjectId.Equals(right.SubjectId)
        && left.AuthorityEpoch.Equals(right.AuthorityEpoch)
        && left.Incarnation.Equals(right.Incarnation)
        && left.SubjectSequence == right.SubjectSequence;

    private static bool ValidBoundary(BaseSubjectLifecycleOrderingBoundary? value) => value is null
        || value.CommitPosition.Value > 0 && value.SubjectSequence > 0;

    private static BaseError Error(string code, ErrorCategory category) => code.StartsWith("base.subjectLifecycle.", StringComparison.Ordinal)
        ? BaseSubjectFailureContract.Error(code)
        : new BaseError { Code = code, Message = "The stored mutation receipt cannot be resolved.", Category = category };
}
