using HPD.Agent.Audio.Runtime.Replay;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Transports;

internal abstract record TransportRecordingEffectResult{private TransportRecordingEffectResult(){}internal sealed record Materialized(Hash256 ContentHash):TransportRecordingEffectResult;internal sealed record Refused(BoundedAscii SafeCode):TransportRecordingEffectResult;internal sealed record OutcomeUnknown(BoundedAscii SafeCode):TransportRecordingEffectResult;}
internal interface ITransportRecordingEffectPort{ValueTask<TransportRecordingEffectResult> RecordAsync(CopyReservationV1 reservation,CancellationToken cancellationToken);}
internal abstract record TransportRecordingResult
{
    private TransportRecordingResult(){}internal sealed record Materialized(PrivacyCopyStateV1 Privacy,PrivacyCopyReceiptV1 ReservationReceipt,Hash256 ContentHash):TransportRecordingResult;internal sealed record Duplicate(PrivacyCopyStateV1 Privacy,PrivacyCopyReceiptV1 ReservationReceipt):TransportRecordingResult;internal sealed record EffectRefused(PrivacyCopyStateV1 Privacy,BoundedAscii SafeCode):TransportRecordingResult;internal sealed record OutcomeUnknown(PrivacyCopyStateV1 Privacy,BoundedAscii SafeCode):TransportRecordingResult;internal sealed record Rejected(PrivacyCopyStateV1 Privacy,BoundedAscii SafeCode):TransportRecordingResult;
}
internal static class TransportRecordingCoordinator
{
    internal static async ValueTask<TransportRecordingResult> RecordAsync(PrivacyCopyStateV1 privacy,CopyReservationV1 reservation,ulong expectedPrivacyRevision,ITransportRecordingEffectPort effect,ushort maximumCopies,ushort maximumReceipts,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(privacy);ArgumentNullException.ThrowIfNull(reservation);ArgumentNullException.ThrowIfNull(effect);cancellationToken.ThrowIfCancellationRequested();
        var reserved=PrivacyCopyLifecycleV1.Apply(privacy,new PrivacyCopyCommandV1.Reserve(reservation.OperationId,expectedPrivacyRevision,reservation),maximumCopies,maximumReceipts);
        if(reserved is PrivacyCopyResultV1.Rejected rejected)return new TransportRecordingResult.Rejected(privacy,rejected.SafeCode);
        if(reserved is PrivacyCopyResultV1.Duplicate duplicate)return new TransportRecordingResult.Duplicate(duplicate.State,duplicate.Receipt);
        var applied=(PrivacyCopyResultV1.Applied)reserved;
        var result=await effect.RecordAsync(reservation,cancellationToken).ConfigureAwait(false);return result switch{TransportRecordingEffectResult.Materialized x=>new TransportRecordingResult.Materialized(applied!.State,applied.Receipt,x.ContentHash),TransportRecordingEffectResult.Refused x=>new TransportRecordingResult.EffectRefused(applied!.State,x.SafeCode),TransportRecordingEffectResult.OutcomeUnknown x=>new TransportRecordingResult.OutcomeUnknown(applied!.State,x.SafeCode),_=>throw new InvalidOperationException()};
    }
}

internal abstract record TransportReplayResult
{
    private TransportReplayResult(){}internal sealed record Compiled(ReplayCompilation Compilation):TransportReplayResult;internal sealed record Unauthorized(BoundedAscii SafeCode):TransportReplayResult;internal sealed record Invalid(BoundedAscii SafeCode):TransportReplayResult;internal sealed record Unavailable(BoundedAscii SafeCode):TransportReplayResult;
}
internal static class TransportReplayCoordinator
{
    internal static async ValueTask<TransportReplayResult> CompileAsync(PrivacyCopyStateV1 privacy,CopyId copyId,IAuthorityReplaySource source,SessionAuthorityStampV1 session,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(privacy);ArgumentNullException.ThrowIfNull(source);if(!copyId.IsValid)throw new ArgumentException("Copy required.");
        if(!privacy.Copies.TryGetValue(copyId,out var copy)||copy.Held||copy.Evidence is CopyTerminalEvidenceV1.Deleted or CopyTerminalEvidenceV1.CryptoErased or CopyTerminalEvidenceV1.NeverStored)return new TransportReplayResult.Unauthorized(new BoundedAscii("replay-copy-unavailable"));
        if(copy.Reservation.Authority.Session!=session)return new TransportReplayResult.Unauthorized(new BoundedAscii("replay-copy-session-mismatch"));
        var compiled=await ReplayCompiler.CompileAsync(source,session,cancellationToken).ConfigureAwait(false);
        return compiled switch{ReplayCompileResult.Compiled x=>new TransportReplayResult.Compiled(x.Compilation),ReplayCompileResult.InvalidHistory x=>new TransportReplayResult.Invalid(x.SafeCode),ReplayCompileResult.Unavailable x=>new TransportReplayResult.Unavailable(x.SafeCode),_=>throw new InvalidOperationException()};
    }
}
