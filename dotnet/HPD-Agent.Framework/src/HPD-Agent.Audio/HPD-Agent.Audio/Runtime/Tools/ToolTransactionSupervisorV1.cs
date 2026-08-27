using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Tools;

internal enum ToolTransactionPhaseV1 : ushort
{
    None=1,ToolControlRecorded=2,ToolArgumentsFinalized=3,ToolApprovalDecided=4,
    ToolDispositionChosen=5,ToolOwnerClaimed=6,ToolDispatchAuthorized=7,
    ToolEntryIntentRecorded=8,ToolExternalBoundaryEntered=9,ToolEffectEvidenceAdmitted=10,
    ToolResultFinalized=11,ToolResultProjected=12,ToolContinuationAuthorized=13,
    ToolOrchestrationTerminalized=14,
}

internal sealed record ToolTransactionPlanV1
{
    internal ToolTransactionPlanV1(OperationId transactionId,ToolGenerationId toolGeneration,
        OutputGenerationId outputGeneration,ExpectedAuthorityVectorV1 authority,
        MonotonicStampV1 deadline,bool capacityGrantAuthenticated)
    {
        if(!transactionId.IsValid||!toolGeneration.IsValid||!outputGeneration.IsValid||authority is null||
            !deadline.IsValid||!capacityGrantAuthenticated)throw new ArgumentException("Tool transaction authority is incomplete.");
        TransactionId=transactionId;ToolGeneration=toolGeneration;OutputGeneration=outputGeneration;
        Authority=authority;Deadline=deadline;CapacityGrantAuthenticated=capacityGrantAuthenticated;
    }
    internal OperationId TransactionId{get;} internal ToolGenerationId ToolGeneration{get;}
    internal OutputGenerationId OutputGeneration{get;} internal ExpectedAuthorityVectorV1 Authority{get;}
    internal MonotonicStampV1 Deadline{get;} internal bool CapacityGrantAuthenticated{get;}
}

internal abstract record ToolTransactionCommandV1
{
    private protected ToolTransactionCommandV1(OperationId operationId,ulong expectedRevision)
    {if(!operationId.IsValid)throw new ArgumentException("An operation is required.");OperationId=operationId;ExpectedRevision=expectedRevision;}
    internal OperationId OperationId{get;} internal ulong ExpectedRevision{get;}
    internal sealed record Advance(OperationId O,ulong R,ToolTransactionPhaseV1 Target,ushort Disposition):ToolTransactionCommandV1(O,R);
    internal sealed record AdmitEffectEvidence(OperationId O,ulong R,bool OutcomeKnown):ToolTransactionCommandV1(O,R);
    internal sealed record ReconcileEffect(OperationId O,ulong R,bool OutcomeKnown):ToolTransactionCommandV1(O,R);
    internal sealed record AuthorizeContinuation(OperationId O,ulong R,JournalPositionV1? RouteReceipt):ToolTransactionCommandV1(O,R);
    internal sealed record Terminalize(OperationId O,ulong R,BoundedAscii SafeCode):ToolTransactionCommandV1(O,R);
}

internal sealed record ToolTransactionSnapshotV1(ulong Revision,ToolTransactionPhaseV1 Phase,
    bool InterruptionRequested,bool ExternalBoundaryEntered,bool EffectOutcomeKnown,
    BoundedAscii? TerminalSafeCode);
internal sealed record ToolTransactionReceiptV1(ToolTransactionCommandV1 Command,ToolTransactionSnapshotV1 Snapshot);

internal sealed class ToolTransactionStateV1
{
    private readonly ReadOnlyDictionary<OperationId,ToolTransactionReceiptV1> _receipts;
    internal ToolTransactionStateV1(ToolTransactionPlanV1 plan,ToolTransactionSnapshotV1 snapshot,
        IDictionary<OperationId,ToolTransactionReceiptV1>? receipts=null)
    {Plan=plan??throw new ArgumentNullException(nameof(plan));Snapshot=snapshot??throw new ArgumentNullException(nameof(snapshot));_receipts=new(receipts is null?new Dictionary<OperationId,ToolTransactionReceiptV1>():new Dictionary<OperationId,ToolTransactionReceiptV1>(receipts));}
    internal ToolTransactionPlanV1 Plan{get;} internal ToolTransactionSnapshotV1 Snapshot{get;}
    internal IReadOnlyDictionary<OperationId,ToolTransactionReceiptV1> Receipts=>_receipts;
}

internal abstract record ToolTransactionResultV1
{
    private ToolTransactionResultV1(){}
    internal sealed record Applied(ToolTransactionStateV1 State,ToolTransactionReceiptV1 Receipt):ToolTransactionResultV1;
    internal sealed record Duplicate(ToolTransactionStateV1 State,ToolTransactionReceiptV1 Receipt):ToolTransactionResultV1;
    internal sealed record ReplacementRequired(ToolTransactionStateV1 State,BoundedAscii SafeCode):ToolTransactionResultV1;
    internal sealed record RouteUnavailable(ToolTransactionStateV1 State,BoundedAscii SafeCode):ToolTransactionResultV1;
    internal sealed record Rejected(ToolTransactionStateV1 State,BoundedAscii SafeCode):ToolTransactionResultV1;
}

internal static class ToolTransactionSupervisorV1
{
    internal static ToolTransactionStateV1 Create(ToolTransactionPlanV1 plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var tool=plan.Authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Tool>().SingleOrDefault();
        var output=plan.Authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Output>().SingleOrDefault();
        if(tool?.Value!=plan.ToolGeneration||output?.Value!=plan.OutputGeneration)throw new ArgumentException("Tool/output authority axes must exactly match the plan.");
        return new(plan,new(0,ToolTransactionPhaseV1.None,false,false,false,null));
    }

    internal static ToolTransactionResultV1 Apply(ToolTransactionStateV1 state,ToolTransactionCommandV1 command,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(command);if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));
        if(state.Receipts.TryGetValue(command.OperationId,out var prior))return prior.Command==command?new ToolTransactionResultV1.Duplicate(state,prior):Reject(state,"tool-operation-contradiction");
        if(state.Receipts.Count>=maximumReceipts)return Reject(state,"tool-receipt-capacity-refused");
        if(command.ExpectedRevision!=state.Snapshot.Revision)return Reject(state,"tool-revision-conflict");
        if(state.Snapshot.Phase==ToolTransactionPhaseV1.ToolOrchestrationTerminalized)return Reject(state,"tool-terminal");
        if(command is ToolTransactionCommandV1.AuthorizeContinuation continuation)
            return continuation.RouteReceipt is null?new ToolTransactionResultV1.ReplacementRequired(state,new BoundedAscii("replacement-required")):
                new ToolTransactionResultV1.RouteUnavailable(state,new BoundedAscii("route-unavailable"));
        ToolTransactionSnapshotV1? next=command switch
        {
            ToolTransactionCommandV1.Advance advance when advance.Disposition>0&&CanAdvance(state.Snapshot,advance.Target)=>
                state.Snapshot with{Revision=state.Snapshot.Revision+1,Phase=advance.Target,InterruptionRequested=advance.Target==ToolTransactionPhaseV1.ToolControlRecorded||state.Snapshot.InterruptionRequested,
                    ExternalBoundaryEntered=advance.Target==ToolTransactionPhaseV1.ToolExternalBoundaryEntered||state.Snapshot.ExternalBoundaryEntered},
            ToolTransactionCommandV1.AdmitEffectEvidence evidence when state.Snapshot.Phase==ToolTransactionPhaseV1.ToolExternalBoundaryEntered=>
                state.Snapshot with{Revision=state.Snapshot.Revision+1,Phase=ToolTransactionPhaseV1.ToolEffectEvidenceAdmitted,EffectOutcomeKnown=evidence.OutcomeKnown},
            ToolTransactionCommandV1.ReconcileEffect reconcile when state.Snapshot.Phase==ToolTransactionPhaseV1.ToolEffectEvidenceAdmitted&&!state.Snapshot.EffectOutcomeKnown&&reconcile.OutcomeKnown=>
                state.Snapshot with{Revision=state.Snapshot.Revision+1,EffectOutcomeKnown=true},
            ToolTransactionCommandV1.Terminalize terminal when terminal.SafeCode.IsValid&&state.Snapshot.Phase is ToolTransactionPhaseV1.ToolResultProjected or ToolTransactionPhaseV1.ToolContinuationAuthorized=>
                state.Snapshot with{Revision=state.Snapshot.Revision+1,Phase=ToolTransactionPhaseV1.ToolOrchestrationTerminalized,TerminalSafeCode=terminal.SafeCode},
            _=>null,
        };
        if(next is null)return Reject(state,"tool-transition-invalid");
        if(next.Phase==ToolTransactionPhaseV1.ToolResultFinalized&&!state.Snapshot.EffectOutcomeKnown)return Reject(state,"tool-effect-outcome-unknown");
        var receipt=new ToolTransactionReceiptV1(command,next);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(command.OperationId,receipt);
        return new ToolTransactionResultV1.Applied(new ToolTransactionStateV1(state.Plan,next,receipts),receipt);
    }

    private static bool CanAdvance(ToolTransactionSnapshotV1 snapshot,ToolTransactionPhaseV1 target)
    {
        if(target is ToolTransactionPhaseV1.ToolEffectEvidenceAdmitted or ToolTransactionPhaseV1.ToolContinuationAuthorized or ToolTransactionPhaseV1.ToolOrchestrationTerminalized)return false;
        if(target==ToolTransactionPhaseV1.ToolExternalBoundaryEntered&&snapshot.Phase!=ToolTransactionPhaseV1.ToolEntryIntentRecorded)return false;
        if(target==ToolTransactionPhaseV1.ToolResultFinalized&&(!snapshot.EffectOutcomeKnown||snapshot.Phase!=ToolTransactionPhaseV1.ToolEffectEvidenceAdmitted))return false;
        return (ushort)target==(ushort)snapshot.Phase+1;
    }
    private static ToolTransactionResultV1.Rejected Reject(ToolTransactionStateV1 state,string code)=>new(state,new BoundedAscii(code));
}
