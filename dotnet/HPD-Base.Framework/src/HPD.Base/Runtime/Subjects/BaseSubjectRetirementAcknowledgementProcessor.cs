namespace HPD.Base;

internal sealed class BaseSubjectRetirementAcknowledgementProcessor(
    BaseSubjectRetirementProviderAcknowledgementRequest request) : IAtomicMutationProcessor
{
    internal BaseSubjectAcknowledgementResult? Result { get; private set; }

    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(IAtomicRecordSession session, CancellationToken cancellationToken = default)
    {
        if (request.ActivationGuard is not null)
        {
            OperationResult<BaseCapturedActivationGuardEvidence> guarded = await session
                .ValidateActivationGuardAsync(request.ActivationGuard, cancellationToken).ConfigureAwait(false);
            if (!guarded.IsSuccess() || guarded.Value is null)
                return Failed(guarded.Error ?? Error("base.activation.claimLost", ErrorCategory.Conflict));
        }
        OperationResult<BaseSubjectAcknowledgementResult> applied = await session
            .ApplySubjectRetirementAcknowledgementAsync(request, cancellationToken).ConfigureAwait(false);
        if (!applied.IsSuccess() || applied.Value is null)
            return Failed(applied.Error ?? Error(BaseSubjectRetirementErrorCodes.ProviderContractInvalid, ErrorCategory.Store));
        if (!Valid(applied.Value, duplicate: false))
            return Failed(Error(BaseSubjectRetirementErrorCodes.ProviderContractInvalid, ErrorCategory.Capability));
        Result = Clone(applied.Value);
        return Ready(Result);
    }

    public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(BaseAtomicReceiptResult committedResult, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (committedResult.Kind != BaseAtomicReceiptResultKind.SubjectRetirement || committedResult.SubjectRetirement is null
            || committedResult.SubjectRetirement.Operation != BaseSubjectRetirementReceiptOperation.Acknowledgement
            || committedResult.Mutations.Length != 0 || committedResult.SubjectRetirement.Acknowledgement is not { } acknowledgement
            || !Valid(acknowledgement, duplicate: false))
            return ValueTask.FromResult(Failed(Error(BaseMutationRequestErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization)));
        Result = Clone(acknowledgement) with { Outcome = BaseSubjectRetirementMutationOutcome.Duplicate };
        return ValueTask.FromResult(Ready(Result));
    }

    private static AtomicMutationProcessingResult Ready(BaseSubjectAcknowledgementResult result) => new(
        AtomicMutationProcessingOutcome.ReadyToCommit,
        new BaseAtomicReceiptResult { Kind = BaseAtomicReceiptResultKind.SubjectRetirement, Mutations = [], SubjectRetirement = new() { Operation = BaseSubjectRetirementReceiptOperation.Acknowledgement, Acknowledgement = Clone(result) } });
    private static AtomicMutationProcessingResult Failed(BaseError error) => new(AtomicMutationProcessingOutcome.Failed, [], error);
    private static BaseSubjectAcknowledgementResult Clone(BaseSubjectAcknowledgementResult value) => value with { BarrierChecksum = value.BarrierChecksum is null ? null : new string(value.BarrierChecksum.AsSpan()) };
    private static bool Valid(BaseSubjectAcknowledgementResult value, bool duplicate) =>
        value.ThroughSubjectSequence > 0 && Enum.IsDefined(value.Outcome)
        && (value.BarrierState is null && value.BarrierGeneration is null && value.BarrierChecksum is null
            || value.BarrierState is not null && value.BarrierGeneration > 0 && value.BarrierChecksum is { Length: 64 });
    private static BaseError Error(string code, ErrorCategory category) => new() { Code = code, Message = "The subject retirement operation failed.", Category = category };
}
