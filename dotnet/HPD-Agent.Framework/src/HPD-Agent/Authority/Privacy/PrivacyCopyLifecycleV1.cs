using System.Collections.ObjectModel;

namespace HPD.Agent.Authority;

internal enum CopyTerminalEvidenceV1:ushort{Deleted=1,CryptoErased=2,NeverStored=3,Held=4,Failed=5,Unsupported=6,Contradicted=7,OutcomeUnknown=8,Transferred=9}
internal enum PrivacyDeletionOutcomeV1:ushort{Open=1,Completed=2,CompletedWithMinimalTombstone=3,Partial=4,Held=5,Contradicted=6,Failed=7,Unknown=8}

internal sealed record PrivacyCopyEntryV1(CopyReservationV1 Reservation,Hash256 ReservationHash,bool Held,CopyTerminalEvidenceV1? Evidence);
internal sealed record PrivacyDeletionSnapshotV1(DeletionId DeletionId,ulong FencedFrontier,bool RegistrationClosed,
    PrivacyDeletionOutcomeV1 Outcome,bool TombstoneCommitted,ulong RepairRevision);

internal abstract record PrivacyCopyCommandV1
{
    private protected PrivacyCopyCommandV1(OperationId operationId,ulong expectedRevision){if(!operationId.IsValid)throw new ArgumentException();OperationId=operationId;ExpectedRevision=expectedRevision;}
    internal OperationId OperationId{get;}internal ulong ExpectedRevision{get;}
    internal sealed record Reserve(OperationId O,ulong R,CopyReservationV1 Reservation):PrivacyCopyCommandV1(O,R);
    internal sealed record SetHold(OperationId O,ulong R,CopyId CopyId,bool Held):PrivacyCopyCommandV1(O,R);
    internal sealed record Fence(OperationId O,ulong R,DeletionId DeletionId):PrivacyCopyCommandV1(O,R);
    internal sealed record CloseRegistration(OperationId O,ulong R):PrivacyCopyCommandV1(O,R);
    internal sealed record AdmitReceipt(OperationId O,ulong R,CopyId CopyId,CopyTerminalEvidenceV1 Evidence):PrivacyCopyCommandV1(O,R);
    internal sealed record FoldTerminal(OperationId O,ulong R):PrivacyCopyCommandV1(O,R);
    internal sealed record CommitTombstone(OperationId O,ulong R):PrivacyCopyCommandV1(O,R);
}

internal sealed record PrivacyCopyReceiptV1(PrivacyCopyCommandV1 Command,ulong Revision);
internal sealed class PrivacyCopyStateV1
{
    private readonly ReadOnlyDictionary<CopyId,PrivacyCopyEntryV1> _copies;
    private readonly ReadOnlyDictionary<OperationId,PrivacyCopyReceiptV1> _receipts;
    internal PrivacyCopyStateV1(ulong revision,IDictionary<CopyId,PrivacyCopyEntryV1>? copies=null,
        PrivacyDeletionSnapshotV1? deletion=null,IDictionary<OperationId,PrivacyCopyReceiptV1>? receipts=null)
    {Revision=revision;_copies=new(copies is null?new Dictionary<CopyId,PrivacyCopyEntryV1>():new Dictionary<CopyId,PrivacyCopyEntryV1>(copies));Deletion=deletion;_receipts=new(receipts is null?new Dictionary<OperationId,PrivacyCopyReceiptV1>():new Dictionary<OperationId,PrivacyCopyReceiptV1>(receipts));}
    internal ulong Revision{get;}internal IReadOnlyDictionary<CopyId,PrivacyCopyEntryV1> Copies=>_copies;internal PrivacyDeletionSnapshotV1? Deletion{get;}internal IReadOnlyDictionary<OperationId,PrivacyCopyReceiptV1> Receipts=>_receipts;
}
internal abstract record PrivacyCopyResultV1
{
    private PrivacyCopyResultV1(){}internal sealed record Applied(PrivacyCopyStateV1 State,PrivacyCopyReceiptV1 Receipt):PrivacyCopyResultV1;internal sealed record Duplicate(PrivacyCopyStateV1 State,PrivacyCopyReceiptV1 Receipt):PrivacyCopyResultV1;internal sealed record Rejected(PrivacyCopyStateV1 State,BoundedAscii SafeCode):PrivacyCopyResultV1;
}

internal static class PrivacyCopyLifecycleV1
{
    internal static PrivacyCopyStateV1 Create()=>new(0);
    internal static PrivacyCopyResultV1 Apply(PrivacyCopyStateV1 state,PrivacyCopyCommandV1 command,ushort maximumCopies,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(command);if(maximumCopies==0||maximumReceipts==0)throw new ArgumentOutOfRangeException();
        if(state.Receipts.TryGetValue(command.OperationId,out var prior))return prior.Command==command?new PrivacyCopyResultV1.Duplicate(state,prior):Reject(state,"privacy-operation-contradiction");
        if(state.Receipts.Count>=maximumReceipts)return Reject(state,"privacy-receipt-capacity-refused");if(command.ExpectedRevision!=state.Revision)return Reject(state,"privacy-revision-conflict");
        var copies=state.Copies.ToDictionary(static x=>x.Key,static x=>x.Value);var deletion=state.Deletion;
        switch(command)
        {
            case PrivacyCopyCommandV1.Reserve reserve:
                if(reserve.Reservation.ExpectedInventoryFrontier!=(ulong)copies.Count)return Reject(state,"copy-frontier-conflict");
                var hash=CopyReservationCodecsV1.ComputeHash(reserve.Reservation);
                if(copies.TryGetValue(reserve.Reservation.CopyId,out var existing))return existing.ReservationHash==hash?Reject(state,"copy-id-already-reserved"):Reject(state,"copy-id-contradiction");
                if(copies.Count>=maximumCopies)return Reject(state,"copy-capacity-refused");
                copies.Add(reserve.Reservation.CopyId,new(reserve.Reservation,hash,false,null));
                if(deletion is not null)deletion=deletion with{Outcome=PrivacyDeletionOutcomeV1.Open,RegistrationClosed=false,RepairRevision=deletion.RepairRevision+1,TombstoneCommitted=false};
                break;
            case PrivacyCopyCommandV1.SetHold hold when copies.TryGetValue(hold.CopyId,out var entry):copies[hold.CopyId]=entry with{Held=hold.Held};break;
            case PrivacyCopyCommandV1.Fence fence when deletion is null:deletion=new(fence.DeletionId,(ulong)copies.Count,false,PrivacyDeletionOutcomeV1.Open,false,0);break;
            case PrivacyCopyCommandV1.CloseRegistration when deletion is not null&&!deletion.RegistrationClosed:deletion=deletion with{RegistrationClosed=true};break;
            case PrivacyCopyCommandV1.AdmitReceipt receipt when deletion is not null&&deletion.RegistrationClosed&&copies.TryGetValue(receipt.CopyId,out var target):
                if(target.Evidence is not null)return Reject(state,"copy-receipt-duplicate");
                if(receipt.Evidence==CopyTerminalEvidenceV1.Transferred)return Reject(state,"copy-transfer-nonterminal");
                if(target.Held&&receipt.Evidence is CopyTerminalEvidenceV1.Deleted or CopyTerminalEvidenceV1.CryptoErased)return Reject(state,"copy-held");
                copies[receipt.CopyId]=target with{Evidence=receipt.Evidence};break;
            case PrivacyCopyCommandV1.FoldTerminal when deletion is not null&&deletion.RegistrationClosed:
                deletion=deletion with{Outcome=Fold(copies.Values)};break;
            case PrivacyCopyCommandV1.CommitTombstone when deletion is not null&&deletion.Outcome==PrivacyDeletionOutcomeV1.Completed:
                deletion=deletion with{Outcome=PrivacyDeletionOutcomeV1.CompletedWithMinimalTombstone,TombstoneCommitted=true};break;
            default:return Reject(state,"privacy-transition-invalid");
        }
        var revision=state.Revision+1;var accepted=new PrivacyCopyReceiptV1(command,revision);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(command.OperationId,accepted);return new PrivacyCopyResultV1.Applied(new(revision,copies,deletion,receipts),accepted);
    }
    private static PrivacyDeletionOutcomeV1 Fold(IEnumerable<PrivacyCopyEntryV1> entries)
    {var values=entries.ToArray();if(values.Any(static x=>x.Held||x.Evidence==CopyTerminalEvidenceV1.Held))return PrivacyDeletionOutcomeV1.Held;if(values.Any(static x=>x.Evidence is null or CopyTerminalEvidenceV1.OutcomeUnknown))return PrivacyDeletionOutcomeV1.Unknown;if(values.Any(static x=>x.Evidence==CopyTerminalEvidenceV1.Contradicted))return PrivacyDeletionOutcomeV1.Contradicted;if(values.Any(static x=>x.Evidence is CopyTerminalEvidenceV1.Failed or CopyTerminalEvidenceV1.Unsupported))return PrivacyDeletionOutcomeV1.Partial;return PrivacyDeletionOutcomeV1.Completed;}
    private static PrivacyCopyResultV1.Rejected Reject(PrivacyCopyStateV1 state,string code)=>new(state,new BoundedAscii(code));
}
