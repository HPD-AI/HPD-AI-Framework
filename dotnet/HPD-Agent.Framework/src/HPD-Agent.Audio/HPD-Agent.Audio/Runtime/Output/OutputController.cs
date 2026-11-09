using System.Collections.ObjectModel;
using HPD.Agent.Authority;
using HPD.Agent.Audio.Runtime.Tools;

namespace HPD.Agent.Audio.Runtime.Output;

internal sealed record OutputPlan
{
    internal OutputPlan(OperationId outputOperation,OutputGenerationId generation,ExpectedAuthorityVectorV1 authority,long maximumUnits)
    {if(!outputOperation.IsValid||!generation.IsValid||authority is null||maximumUnits<=0)throw new ArgumentException("Output plan is invalid.");OutputOperation=outputOperation;Generation=generation;Authority=authority;MaximumUnits=maximumUnits;}
    internal OperationId OutputOperation{get;} internal OutputGenerationId Generation{get;} internal ExpectedAuthorityVectorV1 Authority{get;} internal long MaximumUnits{get;}
}

internal sealed record OutputStatus
{
    internal OutputStatus(ulong revision,long generatedUntil,long sentUntil,long playedUntil,long heardUntil,bool closed)
    {if(generatedUntil<0||sentUntil<0||playedUntil<0||heardUntil<0||heardUntil>playedUntil||playedUntil>sentUntil||sentUntil>generatedUntil)throw new ArgumentException("Output axes are inconsistent.");Revision=revision;GeneratedUntil=generatedUntil;SentUntil=sentUntil;PlayedUntil=playedUntil;HeardUntil=heardUntil;Closed=closed;}
    internal ulong Revision{get;init;} internal long GeneratedUntil{get;init;} internal long SentUntil{get;init;} internal long PlayedUntil{get;init;} internal long HeardUntil{get;init;} internal bool Closed{get;init;}
}

internal abstract record OutputCommand
{
    private protected OutputCommand(OperationId operationId,ulong expectedRevision,long until)
    {if(!operationId.IsValid||until<0)throw new ArgumentException("Output command is invalid.");OperationId=operationId;ExpectedRevision=expectedRevision;Until=until;}
    internal OperationId OperationId{get;} internal ulong ExpectedRevision{get;} internal long Until{get;}
    internal sealed record Generate(OperationId O,ulong R,long U):OutputCommand(O,R,U);
    internal sealed record Send(OperationId O,ulong R,long U):OutputCommand(O,R,U);
    internal sealed record Play(OperationId O,ulong R,long U):OutputCommand(O,R,U);
    internal sealed record Hear(OperationId O,ulong R,long U):OutputCommand(O,R,U);
    internal sealed record Close(OperationId O,ulong R):OutputCommand(O,R,0);
}

internal sealed record OutputReceipt(OutputCommand Command,OutputStatus Status);
internal sealed class OutputControllerState
{
    private readonly ReadOnlyDictionary<OperationId,OutputReceipt> _receipts;
    internal OutputControllerState(OutputPlan plan,OutputStatus status,IDictionary<OperationId,OutputReceipt>? receipts=null)
    {Plan=plan??throw new ArgumentNullException(nameof(plan));Status=status??throw new ArgumentNullException(nameof(status));_receipts=new(receipts is null?new Dictionary<OperationId,OutputReceipt>():new Dictionary<OperationId,OutputReceipt>(receipts));}
    internal OutputPlan Plan{get;} internal OutputStatus Status{get;} internal IReadOnlyDictionary<OperationId,OutputReceipt> Receipts=>_receipts;
}
internal abstract record OutputCommandResult
{
    private OutputCommandResult(){}
    internal sealed record Applied(OutputControllerState State,OutputReceipt Receipt):OutputCommandResult;
    internal sealed record Duplicate(OutputControllerState State,OutputReceipt Receipt):OutputCommandResult;
    internal sealed record Rejected(OutputControllerState State,BoundedAscii SafeCode):OutputCommandResult;
}

internal interface IOutputController{OutputCommandResult Apply(OutputCommand command);}
internal interface IOutputStatusReader{OutputStatus Read();}

internal sealed class InMemoryOutputController : IOutputController,IOutputStatusReader
{
    private OutputControllerState _state;
    private readonly ushort _maximumReceipts;
    internal InMemoryOutputController(OutputPlan plan,ushort maximumReceipts)
    {if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));_maximumReceipts=maximumReceipts;_state=new(plan,new(0,0,0,0,0,false));}
    public OutputCommandResult Apply(OutputCommand command)
    {var result=OutputReducer.Apply(_state,command,_maximumReceipts);if(result is OutputCommandResult.Applied applied)_state=applied.State;return result;}
    public OutputStatus Read()=>_state.Status with{};
    internal OutputPipelineResult Generate(OutputSynthesisRequest request,IOutputSynthesisProvider provider)
        =>ApplyPipeline(OutputTtsSinkPipeline.Generate(_state,request,provider,_maximumReceipts));
    internal OutputPipelineResult Generate(OutputSynthesisEvidence evidence)
        =>ApplyPipeline(OutputTtsSinkPipeline.Generate(_state,evidence,_maximumReceipts));
    internal async ValueTask<OutputPipelineResult> SendAsync(OutputSinkEffect.Send effect,IOutputSinkEffectPort sink,CancellationToken cancellationToken=default)
        =>ApplyPipeline(await OutputTtsSinkPipeline.SendAsync(_state,effect,sink,_maximumReceipts,cancellationToken).ConfigureAwait(false));
    internal async ValueTask<OutputPipelineResult> PlayAsync(OutputSinkEffect.Play effect,IOutputSinkEffectPort sink,CancellationToken cancellationToken=default)
        =>ApplyPipeline(await OutputTtsSinkPipeline.PlayAsync(_state,effect,sink,_maximumReceipts,cancellationToken).ConfigureAwait(false));
    internal async ValueTask<OutputPipelineResult> HearAsync(OutputSinkEffect.Hear effect,IOutputSinkEffectPort sink,CancellationToken cancellationToken=default)
        =>ApplyPipeline(await OutputTtsSinkPipeline.HearAsync(_state,effect,sink,_maximumReceipts,cancellationToken).ConfigureAwait(false));
    internal OutputPlan Plan=>_state.Plan;
    internal OutputControllerState State=>_state;
    internal OutputInterruptionResult Interrupt(ToolTransactionState tool,OperationId operationId,ushort maximumToolReceipts)
    {var result=OutputInterruptionCoordinator.Interrupt(new(_state,tool),operationId,_maximumReceipts,maximumToolReceipts);if(result is OutputInterruptionResult.Applied applied)_state=applied.State.Output;return result;}
    private OutputPipelineResult ApplyPipeline(OutputPipelineResult result)
    {if(result is OutputPipelineResult.Applied applied)_state=applied.State;return result;}
}

internal static class OutputReducer
{
    internal static OutputCommandResult Apply(OutputControllerState state,OutputCommand command,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(command);if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));
        if(state.Receipts.TryGetValue(command.OperationId,out var prior))return prior.Command==command?new OutputCommandResult.Duplicate(state,prior):Reject(state,"output-operation-contradiction");
        if(state.Receipts.Count>=maximumReceipts)return Reject(state,"output-receipt-capacity-refused");
        if(command.ExpectedRevision!=state.Status.Revision)return Reject(state,"output-revision-conflict");
        if(state.Status.Closed)return Reject(state,"output-closed");
        var s=state.Status;OutputStatus? next=command switch
        {
            OutputCommand.Generate g when g.Until>s.GeneratedUntil&&g.Until<=state.Plan.MaximumUnits=>s with{Revision=s.Revision+1,GeneratedUntil=g.Until},
            OutputCommand.Send x when x.Until>s.SentUntil&&x.Until<=s.GeneratedUntil=>s with{Revision=s.Revision+1,SentUntil=x.Until},
            OutputCommand.Play x when x.Until>s.PlayedUntil&&x.Until<=s.SentUntil=>s with{Revision=s.Revision+1,PlayedUntil=x.Until},
            OutputCommand.Hear x when x.Until>s.HeardUntil&&x.Until<=s.PlayedUntil=>s with{Revision=s.Revision+1,HeardUntil=x.Until},
            OutputCommand.Close when s.SentUntil==s.GeneratedUntil=>s with{Revision=s.Revision+1,Closed=true},
            _=>null,
        };
        if(next is null)return Reject(state,"output-transition-invalid");var receipt=new OutputReceipt(command,next);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(command.OperationId,receipt);
        return new OutputCommandResult.Applied(new OutputControllerState(state.Plan,next,receipts),receipt);
    }
    private static OutputCommandResult.Rejected Reject(OutputControllerState state,string code)=>new(state,new BoundedAscii(code));
}

internal sealed record OutputShadowProjection(long GeneratedUntil,long SentUntil,long PlayedUntil,long HeardUntil,bool Closed)
{
    internal static OutputShadowProjection From(IOutputStatusReader reader)
    {ArgumentNullException.ThrowIfNull(reader);var status=reader.Read();return new(status.GeneratedUntil,status.SentUntil,status.PlayedUntil,status.HeardUntil,status.Closed);}
}
