using System.Collections.ObjectModel;
using HPD.Agent.Audio.Transports;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Transports;

internal enum TransportProfile:ushort{FiniteContent=1,Manual=2,LocalDevice=3}
internal enum TransportLifecycle:ushort{Proposed=1,Bound=2,Active=3,Stopped=4,Quarantined=5}
internal sealed record TransportPlan
{
    internal TransportPlan(OperationId planId,TransportProfile profile,TransportGenerationId generation,ExpectedAuthorityVectorV1 authority)
    {if(!planId.IsValid||!generation.IsValid||authority is null||authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Transport>().SingleOrDefault()?.Value!=generation)throw new ArgumentException("Transport plan authority is invalid.");PlanId=planId;Profile=profile;Generation=generation;Authority=authority;}
    internal OperationId PlanId{get;}internal TransportProfile Profile{get;}internal TransportGenerationId Generation{get;}internal ExpectedAuthorityVectorV1 Authority{get;}
}
internal abstract record TransportLifecycleCommand
{
    private protected TransportLifecycleCommand(OperationId operationId,ulong expectedRevision){if(!operationId.IsValid)throw new ArgumentException();OperationId=operationId;ExpectedRevision=expectedRevision;}
    internal OperationId OperationId{get;}internal ulong ExpectedRevision{get;}internal sealed record Bind(OperationId O,ulong R):TransportLifecycleCommand(O,R);internal sealed record Start(OperationId O,ulong R):TransportLifecycleCommand(O,R);internal sealed record Stop(OperationId O,ulong R):TransportLifecycleCommand(O,R);
}
internal sealed record TransportLifecycleSnapshot(ulong Revision,TransportLifecycle Lifecycle);
internal sealed record TransportLifecycleReceipt(TransportLifecycleCommand Command,TransportLifecycleSnapshot Snapshot);
internal sealed class TransportCoordinatorState
{
    private readonly ReadOnlyDictionary<OperationId,TransportLifecycleReceipt> _receipts;
    internal TransportCoordinatorState(TransportPlan plan,TransportLifecycleSnapshot snapshot,IDictionary<OperationId,TransportLifecycleReceipt>? receipts=null){Plan=plan??throw new ArgumentNullException(nameof(plan));Snapshot=snapshot??throw new ArgumentNullException(nameof(snapshot));_receipts=new(receipts is null?new Dictionary<OperationId,TransportLifecycleReceipt>():new Dictionary<OperationId,TransportLifecycleReceipt>(receipts));}
    internal TransportPlan Plan{get;}internal TransportLifecycleSnapshot Snapshot{get;}internal IReadOnlyDictionary<OperationId,TransportLifecycleReceipt> Receipts=>_receipts;
}
internal abstract record TransportAdapterEffectResult{private TransportAdapterEffectResult(){}internal sealed record Completed:TransportAdapterEffectResult;internal sealed record Refused(BoundedAscii SafeCode):TransportAdapterEffectResult;internal sealed record OutcomeUnknown(BoundedAscii SafeCode):TransportAdapterEffectResult;}
internal interface ITransportLifecycleEffectPort{ValueTask<TransportAdapterEffectResult> ApplyAsync(TransportLifecycleCommand command,CancellationToken cancellationToken);}
internal sealed class FiniteManualTransportAdapterPort(ContentInputTransportAdapter adapter):ITransportLifecycleEffectPort
{
    public async ValueTask<TransportAdapterEffectResult> ApplyAsync(TransportLifecycleCommand command,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);try{switch(command){case TransportLifecycleCommand.Bind:return new TransportAdapterEffectResult.Completed();case TransportLifecycleCommand.Start:await adapter.StartAsync(cancellationToken).ConfigureAwait(false);break;case TransportLifecycleCommand.Stop:await adapter.StopAsync(cancellationToken).ConfigureAwait(false);break;}return new TransportAdapterEffectResult.Completed();}
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}catch(Exception){return new TransportAdapterEffectResult.OutcomeUnknown(new BoundedAscii("transport-adapter-outcome-unknown"));}
    }
}
internal abstract record TransportCoordinatorResult{private TransportCoordinatorResult(){}internal sealed record Applied(TransportCoordinatorState State,TransportLifecycleReceipt Receipt):TransportCoordinatorResult;internal sealed record Duplicate(TransportCoordinatorState State,TransportLifecycleReceipt Receipt):TransportCoordinatorResult;internal sealed record EffectRefused(TransportCoordinatorState State,BoundedAscii SafeCode):TransportCoordinatorResult;internal sealed record OutcomeUnknown(TransportCoordinatorState State,BoundedAscii SafeCode):TransportCoordinatorResult;internal sealed record Rejected(TransportCoordinatorState State,BoundedAscii SafeCode):TransportCoordinatorResult;}
internal static class TransportCoordinator
{
    internal static TransportCoordinatorState Create(TransportPlan plan)=>new(plan,new(0,TransportLifecycle.Proposed));
    internal static async ValueTask<TransportCoordinatorResult> ApplyAsync(TransportCoordinatorState state,TransportLifecycleCommand command,ITransportLifecycleEffectPort effects,ushort maximumReceipts,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(command);ArgumentNullException.ThrowIfNull(effects);if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));cancellationToken.ThrowIfCancellationRequested();
        if(state.Receipts.TryGetValue(command.OperationId,out var prior))return prior.Command==command?new TransportCoordinatorResult.Duplicate(state,prior):Reject(state,"transport-operation-contradiction");if(state.Receipts.Count>=maximumReceipts)return Reject(state,"transport-receipt-capacity-refused");if(command.ExpectedRevision!=state.Snapshot.Revision)return Reject(state,"transport-revision-conflict");
        var target=command switch{TransportLifecycleCommand.Bind when state.Snapshot.Lifecycle==TransportLifecycle.Proposed=>TransportLifecycle.Bound,TransportLifecycleCommand.Start when state.Snapshot.Lifecycle==TransportLifecycle.Bound=>TransportLifecycle.Active,TransportLifecycleCommand.Stop when state.Snapshot.Lifecycle==TransportLifecycle.Active=>TransportLifecycle.Stopped,_=>(TransportLifecycle?)null};if(target is null)return Reject(state,"transport-transition-invalid");
        var effect=await effects.ApplyAsync(command,cancellationToken).ConfigureAwait(false);if(effect is TransportAdapterEffectResult.Refused refused)return new TransportCoordinatorResult.EffectRefused(state,refused.SafeCode);if(effect is TransportAdapterEffectResult.OutcomeUnknown unknown)return new TransportCoordinatorResult.OutcomeUnknown(state,unknown.SafeCode);
        var snapshot=new TransportLifecycleSnapshot(state.Snapshot.Revision+1,target.Value);var receipt=new TransportLifecycleReceipt(command,snapshot);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(command.OperationId,receipt);return new TransportCoordinatorResult.Applied(new(state.Plan,snapshot,receipts),receipt);
    }
    private static TransportCoordinatorResult.Rejected Reject(TransportCoordinatorState state,string code)=>new(state,new BoundedAscii(code));
}
