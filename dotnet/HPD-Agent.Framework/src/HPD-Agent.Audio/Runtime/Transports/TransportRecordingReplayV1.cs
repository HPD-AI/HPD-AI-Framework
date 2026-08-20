using HPD.Agent.Audio.Runtime.Replay;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Transports;

internal abstract record TransportRecordingEffectResultV1{private TransportRecordingEffectResultV1(){}internal sealed record Materialized(Hash256 ContentHash):TransportRecordingEffectResultV1;internal sealed record Refused(BoundedAscii SafeCode):TransportRecordingEffectResultV1;internal sealed record OutcomeUnknown(BoundedAscii SafeCode):TransportRecordingEffectResultV1;}
internal interface ITransportRecordingEffectPortV1{ValueTask<TransportRecordingEffectResultV1> RecordAsync(CopyReservationV1 reservation,CancellationToken cancellationToken);}
internal abstract record TransportRecordingResultV1
{
    private TransportRecordingResultV1(){}internal sealed record Materialized(PrivacyCopyStateV1 Privacy,PrivacyCopyReceiptV1 ReservationReceipt,Hash256 ContentHash):TransportRecordingResultV1;internal sealed record Duplicate(PrivacyCopyStateV1 Privacy,PrivacyCopyReceiptV1 ReservationReceipt):TransportRecordingResultV1;internal sealed record EffectRefused(PrivacyCopyStateV1 Privacy,BoundedAscii SafeCode):TransportRecordingResultV1;internal sealed record OutcomeUnknown(PrivacyCopyStateV1 Privacy,BoundedAscii SafeCode):TransportRecordingResultV1;internal sealed record Rejected(PrivacyCopyStateV1 Privacy,BoundedAscii SafeCode):TransportRecordingResultV1;
}
internal static class TransportRecordingCoordinatorV1
{
    internal static async ValueTask<TransportRecordingResultV1> RecordAsync(PrivacyCopyStateV1 privacy,CopyReservationV1 reservation,ulong expectedPrivacyRevision,ITransportRecordingEffectPortV1 effect,ushort maximumCopies,ushort maximumReceipts,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(privacy);ArgumentNullException.ThrowIfNull(reservation);ArgumentNullException.ThrowIfNull(effect);cancellationToken.ThrowIfCancellationRequested();
        var reserved=PrivacyCopyLifecycleV1.Apply(privacy,new PrivacyCopyCommandV1.Reserve(reservation.OperationId,expectedPrivacyRevision,reservation),maximumCopies,maximumReceipts);
        if(reserved is PrivacyCopyResultV1.Rejected rejected)return new TransportRecordingResultV1.Rejected(privacy,rejected.SafeCode);
        if(reserved is PrivacyCopyResultV1.Duplicate duplicate)return new TransportRecordingResultV1.Duplicate(duplicate.State,duplicate.Receipt);
        var applied=(PrivacyCopyResultV1.Applied)reserved;
        var result=await effect.RecordAsync(reservation,cancellationToken).ConfigureAwait(false);return result switch{TransportRecordingEffectResultV1.Materialized x=>new TransportRecordingResultV1.Materialized(applied!.State,applied.Receipt,x.ContentHash),TransportRecordingEffectResultV1.Refused x=>new TransportRecordingResultV1.EffectRefused(applied!.State,x.SafeCode),TransportRecordingEffectResultV1.OutcomeUnknown x=>new TransportRecordingResultV1.OutcomeUnknown(applied!.State,x.SafeCode),_=>throw new InvalidOperationException()};
    }
}

internal abstract record TransportReplayResultV1
{
    private TransportReplayResultV1(){}internal sealed record Compiled(ReplayCompilationV1 Compilation):TransportReplayResultV1;internal sealed record Unauthorized(BoundedAscii SafeCode):TransportReplayResultV1;internal sealed record Invalid(BoundedAscii SafeCode):TransportReplayResultV1;internal sealed record Unavailable(BoundedAscii SafeCode):TransportReplayResultV1;
}
internal static class TransportReplayCoordinatorV1
{
    internal static async ValueTask<TransportReplayResultV1> CompileAsync(PrivacyCopyStateV1 privacy,CopyId copyId,IAuthorityReplaySourceV1 source,SessionAuthorityStampV1 session,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(privacy);ArgumentNullException.ThrowIfNull(source);if(!copyId.IsValid)throw new ArgumentException("Copy required.");
        if(!privacy.Copies.TryGetValue(copyId,out var copy)||copy.Held||copy.Evidence is CopyTerminalEvidenceV1.Deleted or CopyTerminalEvidenceV1.CryptoErased or CopyTerminalEvidenceV1.NeverStored)return new TransportReplayResultV1.Unauthorized(new BoundedAscii("replay-copy-unavailable"));
        if(copy.Reservation.Authority.Session!=session)return new TransportReplayResultV1.Unauthorized(new BoundedAscii("replay-copy-session-mismatch"));
        var compiled=await ReplayCompilerV1.CompileAsync(source,session,cancellationToken).ConfigureAwait(false);
        return compiled switch{ReplayCompileResultV1.Compiled x=>new TransportReplayResultV1.Compiled(x.Compilation),ReplayCompileResultV1.InvalidHistory x=>new TransportReplayResultV1.Invalid(x.SafeCode),ReplayCompileResultV1.Unavailable x=>new TransportReplayResultV1.Unavailable(x.SafeCode),_=>throw new InvalidOperationException()};
    }
}
