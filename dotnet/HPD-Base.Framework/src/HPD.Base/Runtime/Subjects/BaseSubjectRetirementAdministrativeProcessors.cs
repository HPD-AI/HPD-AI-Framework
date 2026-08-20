namespace HPD.Base;

internal sealed class BaseSubjectRetirementTimeoutProcessor(BaseSubjectRetirementProviderTimeoutRequest request) : IAtomicMutationProcessor
{
    internal BaseSubjectRetirementTimeoutResult? Result { get; private set; }
    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(IAtomicRecordSession session, CancellationToken cancellationToken = default)
    {
        OperationResult<BaseSubjectRetirementTimeoutResult> result = await session.ApplySubjectRetirementTimeoutAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null) return Failed(result.Error);
        Result = result.Value with { BarrierChecksum = new string(result.Value.BarrierChecksum.AsSpan()) };
        return Ready(Result);
    }
    public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(BaseAtomicReceiptResult receipt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); BaseSubjectRetirementTimeoutResult? result = receipt.SubjectRetirement?.Operation == BaseSubjectRetirementReceiptOperation.Timeout ? receipt.SubjectRetirement.Timeout : null;
        if (receipt.Kind != BaseAtomicReceiptResultKind.SubjectRetirement || result is null) return ValueTask.FromResult(Failed(null));
        Result = result with { Outcome = BaseSubjectRetirementMutationOutcome.Duplicate, BarrierChecksum = new string(result.BarrierChecksum.AsSpan()) }; return ValueTask.FromResult(Ready(Result));
    }
    private static AtomicMutationProcessingResult Ready(BaseSubjectRetirementTimeoutResult result) => new(AtomicMutationProcessingOutcome.ReadyToCommit, new BaseAtomicReceiptResult { Kind = BaseAtomicReceiptResultKind.SubjectRetirement, Mutations = [], SubjectRetirement = new() { Operation = BaseSubjectRetirementReceiptOperation.Timeout, Timeout = result } });
    private static AtomicMutationProcessingResult Failed(BaseError? error) => new(AtomicMutationProcessingOutcome.Failed, [], error ?? Error());
    private static BaseError Error() => new() { Code = BaseSubjectRetirementErrorCodes.ProviderContractInvalid, Message = "The retirement timeout operation failed.", Category = ErrorCategory.Store };
}

internal sealed class BaseSubjectRetirementOverrideProcessor(BaseSubjectRetirementProviderOverrideRequest request) : IAtomicMutationProcessor
{
    internal BaseSubjectRetirementOverrideResult? Result { get; private set; }
    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(IAtomicRecordSession session, CancellationToken cancellationToken = default)
    {
        OperationResult<BaseSubjectRetirementOverrideResult> result = await session.ApplySubjectRetirementOverrideAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null) return Failed(result.Error);
        Result = result.Value with { BarrierChecksum = new string(result.Value.BarrierChecksum.AsSpan()) }; return Ready(Result);
    }
    public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(BaseAtomicReceiptResult receipt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); BaseSubjectRetirementOverrideResult? result = receipt.SubjectRetirement?.Operation == BaseSubjectRetirementReceiptOperation.Override ? receipt.SubjectRetirement.Override : null;
        if (receipt.Kind != BaseAtomicReceiptResultKind.SubjectRetirement || result is null) return ValueTask.FromResult(Failed(null));
        Result = result with { Outcome = BaseSubjectRetirementMutationOutcome.Duplicate, BarrierChecksum = new string(result.BarrierChecksum.AsSpan()) }; return ValueTask.FromResult(Ready(Result));
    }
    private static AtomicMutationProcessingResult Ready(BaseSubjectRetirementOverrideResult result) => new(AtomicMutationProcessingOutcome.ReadyToCommit, new BaseAtomicReceiptResult { Kind = BaseAtomicReceiptResultKind.SubjectRetirement, Mutations = [], SubjectRetirement = new() { Operation = BaseSubjectRetirementReceiptOperation.Override, Override = result } });
    private static AtomicMutationProcessingResult Failed(BaseError? error) => new(AtomicMutationProcessingOutcome.Failed, [], error ?? Error());
    private static BaseError Error() => new() { Code = BaseSubjectRetirementErrorCodes.ProviderContractInvalid, Message = "The retirement override operation failed.", Category = ErrorCategory.Store };
}

internal sealed class BaseSubjectRetirementPurgeProcessor(BaseSubjectRetirementProviderPurgeRequest request) : IAtomicMutationProcessor
{
    internal BaseSubjectFinalPurgeResult? Result { get; private set; }
    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(IAtomicRecordSession session,CancellationToken cancellationToken=default)
    {
        OperationResult<BaseSubjectRetirementPurgeApplied> applied=await session.ApplySubjectRetirementPurgeAsync(request,cancellationToken).ConfigureAwait(false);if(!applied.IsSuccess()||applied.Value is null)return Failed(applied.Error);BaseSubjectRetirementPurgeApplied value=applied.Value;if(value.Result.RetiredPosition!=value.Mutation.JournalPosition||value.Result.RetiredSubjectSequence!=value.Mutation.SubjectLifecycle?.SubjectSequence||value.Terminal.ReceiptChecksum!=value.Result.TerminalReceiptChecksum||BaseSubjectRetirementRegistry.TerminalChecksum(value.Terminal)!=value.Terminal.ReceiptChecksum)return Failed(null);Result=value.Result with{TerminalReceiptChecksum=new string(value.Result.TerminalReceiptChecksum.AsSpan())};return Ready(Result,value.Mutation);
    }
    public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(BaseAtomicReceiptResult receipt,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();BaseSubjectFinalPurgeResult? result=receipt.SubjectRetirement?.Operation==BaseSubjectRetirementReceiptOperation.FinalPurge?receipt.SubjectRetirement.Purge:null;if(receipt.Kind!=BaseAtomicReceiptResultKind.SubjectRetirement||result is null||receipt.Mutations.Length!=1)return ValueTask.FromResult(Failed(null));Result=result with{Outcome=BaseSubjectRetirementMutationOutcome.Duplicate,TerminalReceiptChecksum=new string(result.TerminalReceiptChecksum.AsSpan())};return ValueTask.FromResult(Ready(Result,receipt.Mutations[0].MaterializeOwned()));
    }
    private static AtomicMutationProcessingResult Ready(BaseSubjectFinalPurgeResult result,BaseRecordMutationFact mutation)=>new(AtomicMutationProcessingOutcome.ReadyToCommit,new BaseAtomicReceiptResult{Kind=BaseAtomicReceiptResultKind.SubjectRetirement,Mutations=[BaseOwnedMutationFact.Freeze(mutation,1)],SubjectRetirement=new(){Operation=BaseSubjectRetirementReceiptOperation.FinalPurge,Purge=result}});
    private static AtomicMutationProcessingResult Failed(BaseError? error)=>new(AtomicMutationProcessingOutcome.Failed,[],error??new BaseError{Code=BaseSubjectRetirementErrorCodes.ProviderContractInvalid,Message="The final subject purge failed.",Category=ErrorCategory.Store});
}
