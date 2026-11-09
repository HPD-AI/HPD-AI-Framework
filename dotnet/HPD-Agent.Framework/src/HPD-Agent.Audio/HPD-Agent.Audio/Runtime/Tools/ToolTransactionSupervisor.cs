using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Tools;

internal enum ToolTransactionPhase : ushort
{
    None=1,ToolControlRecorded=2,ToolArgumentsFinalized=3,ToolApprovalDecided=4,
    ToolDispositionChosen=5,ToolOwnerClaimed=6,ToolDispatchAuthorized=7,
    ToolEntryIntentRecorded=8,ToolExternalBoundaryEntered=9,ToolEffectEvidenceAdmitted=10,
    ToolResultFinalized=11,ToolResultProjected=12,ToolContinuationAuthorized=13,
    ToolOrchestrationTerminalized=14,
}

internal sealed record ToolTransactionPlan
{
    internal ToolTransactionPlan(OperationId transactionId,ToolGenerationId toolGeneration,
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

internal abstract record ToolTransactionCommand
{
    private protected ToolTransactionCommand(OperationId operationId,ulong expectedRevision)
    {if(!operationId.IsValid)throw new ArgumentException("An operation is required.");OperationId=operationId;ExpectedRevision=expectedRevision;}
    internal OperationId OperationId{get;} internal ulong ExpectedRevision{get;}
    internal sealed record Advance(OperationId O,ulong R,ToolTransactionPhase Target,ushort Disposition):ToolTransactionCommand(O,R);
    internal sealed record AdmitEffectEvidence(OperationId O,ulong R,bool OutcomeKnown):ToolTransactionCommand(O,R);
    internal sealed record ReconcileEffect(OperationId O,ulong R,bool OutcomeKnown):ToolTransactionCommand(O,R);
    internal sealed record AuthorizeContinuation(OperationId O,ulong R,JournalPositionV1? RouteReceipt):ToolTransactionCommand(O,R);
    internal sealed record Terminalize(OperationId O,ulong R,BoundedAscii SafeCode):ToolTransactionCommand(O,R);
}

internal sealed record ToolTransactionSnapshot(ulong Revision,ToolTransactionPhase Phase,
    bool InterruptionRequested,bool ExternalBoundaryEntered,bool EffectOutcomeKnown,
    BoundedAscii? TerminalSafeCode);
internal sealed record ToolTransactionReceipt(ToolTransactionCommand Command,ToolTransactionSnapshot Snapshot);

internal sealed class ToolTransactionState
{
    private readonly ReadOnlyDictionary<OperationId,ToolTransactionReceipt> _receipts;
    internal ToolTransactionState(ToolTransactionPlan plan,ToolTransactionSnapshot snapshot,
        IDictionary<OperationId,ToolTransactionReceipt>? receipts=null)
    {Plan=plan??throw new ArgumentNullException(nameof(plan));Snapshot=snapshot??throw new ArgumentNullException(nameof(snapshot));_receipts=new(receipts is null?new Dictionary<OperationId,ToolTransactionReceipt>():new Dictionary<OperationId,ToolTransactionReceipt>(receipts));}
    internal ToolTransactionPlan Plan{get;} internal ToolTransactionSnapshot Snapshot{get;}
    internal IReadOnlyDictionary<OperationId,ToolTransactionReceipt> Receipts=>_receipts;
}

internal abstract record ToolTransactionResult
{
    private ToolTransactionResult(){}
    internal sealed record Applied(ToolTransactionState State,ToolTransactionReceipt Receipt):ToolTransactionResult;
    internal sealed record Duplicate(ToolTransactionState State,ToolTransactionReceipt Receipt):ToolTransactionResult;
    internal sealed record ReplacementRequired(ToolTransactionState State,BoundedAscii SafeCode):ToolTransactionResult;
    internal sealed record RouteUnavailable(ToolTransactionState State,BoundedAscii SafeCode):ToolTransactionResult;
    internal sealed record Rejected(ToolTransactionState State,BoundedAscii SafeCode):ToolTransactionResult;
}

internal static class ToolTransactionSupervisor
{
    internal static ToolTransactionState Create(ToolTransactionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var tool=plan.Authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Tool>().SingleOrDefault();
        var output=plan.Authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Output>().SingleOrDefault();
        if(tool?.Value!=plan.ToolGeneration||output?.Value!=plan.OutputGeneration)throw new ArgumentException("Tool/output authority axes must exactly match the plan.");
        return new(plan,new(0,ToolTransactionPhase.None,false,false,false,null));
    }

    internal static ToolTransactionResult Apply(ToolTransactionState state,ToolTransactionCommand command,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(command);if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));
        if(state.Receipts.TryGetValue(command.OperationId,out var prior))return prior.Command==command?new ToolTransactionResult.Duplicate(state,prior):Reject(state,"tool-operation-contradiction");
        if(state.Receipts.Count>=maximumReceipts)return Reject(state,"tool-receipt-capacity-refused");
        if(command.ExpectedRevision!=state.Snapshot.Revision)return Reject(state,"tool-revision-conflict");
        if(state.Snapshot.Phase==ToolTransactionPhase.ToolOrchestrationTerminalized)return Reject(state,"tool-terminal");
        if(command is ToolTransactionCommand.AuthorizeContinuation continuation)
            return continuation.RouteReceipt is null?new ToolTransactionResult.ReplacementRequired(state,new BoundedAscii("replacement-required")):
                new ToolTransactionResult.RouteUnavailable(state,new BoundedAscii("route-unavailable"));
        ToolTransactionSnapshot? next=command switch
        {
            ToolTransactionCommand.Advance advance when advance.Disposition>0&&CanAdvance(state.Snapshot,advance.Target)=>
                state.Snapshot with{Revision=state.Snapshot.Revision+1,Phase=advance.Target,InterruptionRequested=advance.Target==ToolTransactionPhase.ToolControlRecorded||state.Snapshot.InterruptionRequested,
                    ExternalBoundaryEntered=advance.Target==ToolTransactionPhase.ToolExternalBoundaryEntered||state.Snapshot.ExternalBoundaryEntered},
            ToolTransactionCommand.AdmitEffectEvidence evidence when state.Snapshot.Phase==ToolTransactionPhase.ToolExternalBoundaryEntered=>
                state.Snapshot with{Revision=state.Snapshot.Revision+1,Phase=ToolTransactionPhase.ToolEffectEvidenceAdmitted,EffectOutcomeKnown=evidence.OutcomeKnown},
            ToolTransactionCommand.ReconcileEffect reconcile when state.Snapshot.Phase==ToolTransactionPhase.ToolEffectEvidenceAdmitted&&!state.Snapshot.EffectOutcomeKnown&&reconcile.OutcomeKnown=>
                state.Snapshot with{Revision=state.Snapshot.Revision+1,EffectOutcomeKnown=true},
            ToolTransactionCommand.Terminalize terminal when terminal.SafeCode.IsValid&&state.Snapshot.Phase is ToolTransactionPhase.ToolResultProjected or ToolTransactionPhase.ToolContinuationAuthorized=>
                state.Snapshot with{Revision=state.Snapshot.Revision+1,Phase=ToolTransactionPhase.ToolOrchestrationTerminalized,TerminalSafeCode=terminal.SafeCode},
            _=>null,
        };
        if(next is null)return Reject(state,"tool-transition-invalid");
        if(next.Phase==ToolTransactionPhase.ToolResultFinalized&&!state.Snapshot.EffectOutcomeKnown)return Reject(state,"tool-effect-outcome-unknown");
        var receipt=new ToolTransactionReceipt(command,next);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(command.OperationId,receipt);
        return new ToolTransactionResult.Applied(new ToolTransactionState(state.Plan,next,receipts),receipt);
    }

    private static bool CanAdvance(ToolTransactionSnapshot snapshot,ToolTransactionPhase target)
    {
        if(target is ToolTransactionPhase.ToolEffectEvidenceAdmitted or ToolTransactionPhase.ToolContinuationAuthorized or ToolTransactionPhase.ToolOrchestrationTerminalized)return false;
        if(target==ToolTransactionPhase.ToolExternalBoundaryEntered&&snapshot.Phase!=ToolTransactionPhase.ToolEntryIntentRecorded)return false;
        if(target==ToolTransactionPhase.ToolResultFinalized&&(!snapshot.EffectOutcomeKnown||snapshot.Phase!=ToolTransactionPhase.ToolEffectEvidenceAdmitted))return false;
        return (ushort)target==(ushort)snapshot.Phase+1;
    }
    private static ToolTransactionResult.Rejected Reject(ToolTransactionState state,string code)=>new(state,new BoundedAscii(code));
}
