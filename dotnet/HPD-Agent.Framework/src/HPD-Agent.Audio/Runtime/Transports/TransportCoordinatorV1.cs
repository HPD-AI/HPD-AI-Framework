using System.Collections.ObjectModel;
using HPD.Agent.Audio.Transports;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Transports;

internal enum TransportProfileV1:ushort{FiniteContent=1,Manual=2}
internal enum TransportLifecycleV1:ushort{Proposed=1,Bound=2,Active=3,Stopped=4,Quarantined=5}
internal sealed record TransportPlanV1
{
    internal TransportPlanV1(OperationId planId,TransportProfileV1 profile,TransportGenerationId generation,ExpectedAuthorityVectorV1 authority)
    {if(!planId.IsValid||!generation.IsValid||authority is null||authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Transport>().SingleOrDefault()?.Value!=generation)throw new ArgumentException("Transport plan authority is invalid.");PlanId=planId;Profile=profile;Generation=generation;Authority=authority;}
    internal OperationId PlanId{get;}internal TransportProfileV1 Profile{get;}internal TransportGenerationId Generation{get;}internal ExpectedAuthorityVectorV1 Authority{get;}
}
internal abstract record TransportLifecycleCommandV1
{
    private protected TransportLifecycleCommandV1(OperationId operationId,ulong expectedRevision){if(!operationId.IsValid)throw new ArgumentException();OperationId=operationId;ExpectedRevision=expectedRevision;}
    internal OperationId OperationId{get;}internal ulong ExpectedRevision{get;}internal sealed record Bind(OperationId O,ulong R):TransportLifecycleCommandV1(O,R);internal sealed record Start(OperationId O,ulong R):TransportLifecycleCommandV1(O,R);internal sealed record Stop(OperationId O,ulong R):TransportLifecycleCommandV1(O,R);
}
internal sealed record TransportLifecycleSnapshotV1(ulong Revision,TransportLifecycleV1 Lifecycle);
internal sealed record TransportLifecycleReceiptV1(TransportLifecycleCommandV1 Command,TransportLifecycleSnapshotV1 Snapshot);
internal sealed class TransportCoordinatorStateV1
{
    private readonly ReadOnlyDictionary<OperationId,TransportLifecycleReceiptV1> _receipts;
    internal TransportCoordinatorStateV1(TransportPlanV1 plan,TransportLifecycleSnapshotV1 snapshot,IDictionary<OperationId,TransportLifecycleReceiptV1>? receipts=null){Plan=plan??throw new ArgumentNullException(nameof(plan));Snapshot=snapshot??throw new ArgumentNullException(nameof(snapshot));_receipts=new(receipts is null?new Dictionary<OperationId,TransportLifecycleReceiptV1>():new Dictionary<OperationId,TransportLifecycleReceiptV1>(receipts));}
    internal TransportPlanV1 Plan{get;}internal TransportLifecycleSnapshotV1 Snapshot{get;}internal IReadOnlyDictionary<OperationId,TransportLifecycleReceiptV1> Receipts=>_receipts;
}
internal abstract record TransportAdapterEffectResultV1{private TransportAdapterEffectResultV1(){}internal sealed record Completed:TransportAdapterEffectResultV1;internal sealed record Refused(BoundedAscii SafeCode):TransportAdapterEffectResultV1;internal sealed record OutcomeUnknown(BoundedAscii SafeCode):TransportAdapterEffectResultV1;}
internal interface ITransportLifecycleEffectPortV1{ValueTask<TransportAdapterEffectResultV1> ApplyAsync(TransportLifecycleCommandV1 command,CancellationToken cancellationToken);}
internal sealed class FiniteManualTransportAdapterPortV1(ContentInputTransportAdapter adapter):ITransportLifecycleEffectPortV1
{
    public async ValueTask<TransportAdapterEffectResultV1> ApplyAsync(TransportLifecycleCommandV1 command,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);try{switch(command){case TransportLifecycleCommandV1.Bind:return new TransportAdapterEffectResultV1.Completed();case TransportLifecycleCommandV1.Start:await adapter.StartAsync(cancellationToken).ConfigureAwait(false);break;case TransportLifecycleCommandV1.Stop:await adapter.StopAsync(cancellationToken).ConfigureAwait(false);break;}return new TransportAdapterEffectResultV1.Completed();}
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}catch(Exception){return new TransportAdapterEffectResultV1.OutcomeUnknown(new BoundedAscii("transport-adapter-outcome-unknown"));}
    }
}
internal abstract record TransportCoordinatorResultV1{private TransportCoordinatorResultV1(){}internal sealed record Applied(TransportCoordinatorStateV1 State,TransportLifecycleReceiptV1 Receipt):TransportCoordinatorResultV1;internal sealed record Duplicate(TransportCoordinatorStateV1 State,TransportLifecycleReceiptV1 Receipt):TransportCoordinatorResultV1;internal sealed record EffectRefused(TransportCoordinatorStateV1 State,BoundedAscii SafeCode):TransportCoordinatorResultV1;internal sealed record OutcomeUnknown(TransportCoordinatorStateV1 State,BoundedAscii SafeCode):TransportCoordinatorResultV1;internal sealed record Rejected(TransportCoordinatorStateV1 State,BoundedAscii SafeCode):TransportCoordinatorResultV1;}
internal static class TransportCoordinatorV1
{
    internal static TransportCoordinatorStateV1 Create(TransportPlanV1 plan)=>new(plan,new(0,TransportLifecycleV1.Proposed));
    internal static async ValueTask<TransportCoordinatorResultV1> ApplyAsync(TransportCoordinatorStateV1 state,TransportLifecycleCommandV1 command,ITransportLifecycleEffectPortV1 effects,ushort maximumReceipts,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(command);ArgumentNullException.ThrowIfNull(effects);if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));cancellationToken.ThrowIfCancellationRequested();
        if(state.Receipts.TryGetValue(command.OperationId,out var prior))return prior.Command==command?new TransportCoordinatorResultV1.Duplicate(state,prior):Reject(state,"transport-operation-contradiction");if(state.Receipts.Count>=maximumReceipts)return Reject(state,"transport-receipt-capacity-refused");if(command.ExpectedRevision!=state.Snapshot.Revision)return Reject(state,"transport-revision-conflict");
        var target=command switch{TransportLifecycleCommandV1.Bind when state.Snapshot.Lifecycle==TransportLifecycleV1.Proposed=>TransportLifecycleV1.Bound,TransportLifecycleCommandV1.Start when state.Snapshot.Lifecycle==TransportLifecycleV1.Bound=>TransportLifecycleV1.Active,TransportLifecycleCommandV1.Stop when state.Snapshot.Lifecycle==TransportLifecycleV1.Active=>TransportLifecycleV1.Stopped,_=>(TransportLifecycleV1?)null};if(target is null)return Reject(state,"transport-transition-invalid");
        var effect=await effects.ApplyAsync(command,cancellationToken).ConfigureAwait(false);if(effect is TransportAdapterEffectResultV1.Refused refused)return new TransportCoordinatorResultV1.EffectRefused(state,refused.SafeCode);if(effect is TransportAdapterEffectResultV1.OutcomeUnknown unknown)return new TransportCoordinatorResultV1.OutcomeUnknown(state,unknown.SafeCode);
        var snapshot=new TransportLifecycleSnapshotV1(state.Snapshot.Revision+1,target.Value);var receipt=new TransportLifecycleReceiptV1(command,snapshot);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(command.OperationId,receipt);return new TransportCoordinatorResultV1.Applied(new(state.Plan,snapshot,receipts),receipt);
    }
    private static TransportCoordinatorResultV1.Rejected Reject(TransportCoordinatorStateV1 state,string code)=>new(state,new BoundedAscii(code));
}
