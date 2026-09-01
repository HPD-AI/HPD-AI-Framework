using System.Collections.ObjectModel;
using HPD.Agent.Authority;
using HPD.Agent.Audio.Runtime.Tools;

namespace HPD.Agent.Audio.Runtime.Output;

internal sealed record OutputPlanV2
{
    internal OutputPlanV2(OperationId outputOperation,OutputGenerationId generation,ExpectedAuthorityVectorV1 authority,long maximumUnits)
    {if(!outputOperation.IsValid||!generation.IsValid||authority is null||maximumUnits<=0)throw new ArgumentException("Output plan is invalid.");OutputOperation=outputOperation;Generation=generation;Authority=authority;MaximumUnits=maximumUnits;}
    internal OperationId OutputOperation{get;} internal OutputGenerationId Generation{get;} internal ExpectedAuthorityVectorV1 Authority{get;} internal long MaximumUnits{get;}
}

internal sealed record OutputStatusV2
{
    internal OutputStatusV2(ulong revision,long generatedUntil,long sentUntil,long playedUntil,long heardUntil,bool closed)
    {if(generatedUntil<0||sentUntil<0||playedUntil<0||heardUntil<0||heardUntil>playedUntil||playedUntil>sentUntil||sentUntil>generatedUntil)throw new ArgumentException("Output axes are inconsistent.");Revision=revision;GeneratedUntil=generatedUntil;SentUntil=sentUntil;PlayedUntil=playedUntil;HeardUntil=heardUntil;Closed=closed;}
    internal ulong Revision{get;init;} internal long GeneratedUntil{get;init;} internal long SentUntil{get;init;} internal long PlayedUntil{get;init;} internal long HeardUntil{get;init;} internal bool Closed{get;init;}
}

internal abstract record OutputCommandV2
{
    private protected OutputCommandV2(OperationId operationId,ulong expectedRevision,long until)
    {if(!operationId.IsValid||until<0)throw new ArgumentException("Output command is invalid.");OperationId=operationId;ExpectedRevision=expectedRevision;Until=until;}
    internal OperationId OperationId{get;} internal ulong ExpectedRevision{get;} internal long Until{get;}
    internal sealed record Generate(OperationId O,ulong R,long U):OutputCommandV2(O,R,U);
    internal sealed record Send(OperationId O,ulong R,long U):OutputCommandV2(O,R,U);
    internal sealed record Play(OperationId O,ulong R,long U):OutputCommandV2(O,R,U);
    internal sealed record Hear(OperationId O,ulong R,long U):OutputCommandV2(O,R,U);
    internal sealed record Close(OperationId O,ulong R):OutputCommandV2(O,R,0);
}

internal sealed record OutputReceiptV2(OutputCommandV2 Command,OutputStatusV2 Status);
internal sealed class OutputControllerStateV2
{
    private readonly ReadOnlyDictionary<OperationId,OutputReceiptV2> _receipts;
    internal OutputControllerStateV2(OutputPlanV2 plan,OutputStatusV2 status,IDictionary<OperationId,OutputReceiptV2>? receipts=null)
    {Plan=plan??throw new ArgumentNullException(nameof(plan));Status=status??throw new ArgumentNullException(nameof(status));_receipts=new(receipts is null?new Dictionary<OperationId,OutputReceiptV2>():new Dictionary<OperationId,OutputReceiptV2>(receipts));}
    internal OutputPlanV2 Plan{get;} internal OutputStatusV2 Status{get;} internal IReadOnlyDictionary<OperationId,OutputReceiptV2> Receipts=>_receipts;
}
internal abstract record OutputCommandResultV2
{
    private OutputCommandResultV2(){}
    internal sealed record Applied(OutputControllerStateV2 State,OutputReceiptV2 Receipt):OutputCommandResultV2;
    internal sealed record Duplicate(OutputControllerStateV2 State,OutputReceiptV2 Receipt):OutputCommandResultV2;
    internal sealed record Rejected(OutputControllerStateV2 State,BoundedAscii SafeCode):OutputCommandResultV2;
}

internal interface IOutputControllerV2{OutputCommandResultV2 Apply(OutputCommandV2 command);}
internal interface IOutputStatusReaderV2{OutputStatusV2 Read();}

internal sealed class InMemoryOutputControllerV2 : IOutputControllerV2,IOutputStatusReaderV2
{
    private OutputControllerStateV2 _state;
    private readonly ushort _maximumReceipts;
    internal InMemoryOutputControllerV2(OutputPlanV2 plan,ushort maximumReceipts)
    {if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));_maximumReceipts=maximumReceipts;_state=new(plan,new(0,0,0,0,0,false));}
    public OutputCommandResultV2 Apply(OutputCommandV2 command)
    {var result=OutputReducerV2.Apply(_state,command,_maximumReceipts);if(result is OutputCommandResultV2.Applied applied)_state=applied.State;return result;}
    public OutputStatusV2 Read()=>_state.Status with{};
    internal OutputPipelineResultV2 Generate(OutputSynthesisRequestV2 request,IOutputSynthesisProviderV2 provider)
        =>ApplyPipeline(OutputTtsSinkPipelineV2.Generate(_state,request,provider,_maximumReceipts));
    internal OutputPipelineResultV2 Generate(OutputSynthesisEvidenceV2 evidence)
        =>ApplyPipeline(OutputTtsSinkPipelineV2.Generate(_state,evidence,_maximumReceipts));
    internal async ValueTask<OutputPipelineResultV2> SendAsync(OutputSinkEffectV2.Send effect,IOutputSinkEffectPortV2 sink,CancellationToken cancellationToken=default)
        =>ApplyPipeline(await OutputTtsSinkPipelineV2.SendAsync(_state,effect,sink,_maximumReceipts,cancellationToken).ConfigureAwait(false));
    internal async ValueTask<OutputPipelineResultV2> PlayAsync(OutputSinkEffectV2.Play effect,IOutputSinkEffectPortV2 sink,CancellationToken cancellationToken=default)
        =>ApplyPipeline(await OutputTtsSinkPipelineV2.PlayAsync(_state,effect,sink,_maximumReceipts,cancellationToken).ConfigureAwait(false));
    internal async ValueTask<OutputPipelineResultV2> HearAsync(OutputSinkEffectV2.Hear effect,IOutputSinkEffectPortV2 sink,CancellationToken cancellationToken=default)
        =>ApplyPipeline(await OutputTtsSinkPipelineV2.HearAsync(_state,effect,sink,_maximumReceipts,cancellationToken).ConfigureAwait(false));
    internal OutputPlanV2 Plan=>_state.Plan;
    internal OutputControllerStateV2 State=>_state;
    internal OutputInterruptionResultV2 Interrupt(ToolTransactionStateV1 tool,OperationId operationId,ushort maximumToolReceipts)
    {var result=OutputInterruptionCoordinatorV2.Interrupt(new(_state,tool),operationId,_maximumReceipts,maximumToolReceipts);if(result is OutputInterruptionResultV2.Applied applied)_state=applied.State.Output;return result;}
    private OutputPipelineResultV2 ApplyPipeline(OutputPipelineResultV2 result)
    {if(result is OutputPipelineResultV2.Applied applied)_state=applied.State;return result;}
}

internal static class OutputReducerV2
{
    internal static OutputCommandResultV2 Apply(OutputControllerStateV2 state,OutputCommandV2 command,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(command);if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));
        if(state.Receipts.TryGetValue(command.OperationId,out var prior))return prior.Command==command?new OutputCommandResultV2.Duplicate(state,prior):Reject(state,"output-operation-contradiction");
        if(state.Receipts.Count>=maximumReceipts)return Reject(state,"output-receipt-capacity-refused");
        if(command.ExpectedRevision!=state.Status.Revision)return Reject(state,"output-revision-conflict");
        if(state.Status.Closed)return Reject(state,"output-closed");
        var s=state.Status;OutputStatusV2? next=command switch
        {
            OutputCommandV2.Generate g when g.Until>s.GeneratedUntil&&g.Until<=state.Plan.MaximumUnits=>s with{Revision=s.Revision+1,GeneratedUntil=g.Until},
            OutputCommandV2.Send x when x.Until>s.SentUntil&&x.Until<=s.GeneratedUntil=>s with{Revision=s.Revision+1,SentUntil=x.Until},
            OutputCommandV2.Play x when x.Until>s.PlayedUntil&&x.Until<=s.SentUntil=>s with{Revision=s.Revision+1,PlayedUntil=x.Until},
            OutputCommandV2.Hear x when x.Until>s.HeardUntil&&x.Until<=s.PlayedUntil=>s with{Revision=s.Revision+1,HeardUntil=x.Until},
            OutputCommandV2.Close when s.SentUntil==s.GeneratedUntil=>s with{Revision=s.Revision+1,Closed=true},
            _=>null,
        };
        if(next is null)return Reject(state,"output-transition-invalid");var receipt=new OutputReceiptV2(command,next);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(command.OperationId,receipt);
        return new OutputCommandResultV2.Applied(new OutputControllerStateV2(state.Plan,next,receipts),receipt);
    }
    private static OutputCommandResultV2.Rejected Reject(OutputControllerStateV2 state,string code)=>new(state,new BoundedAscii(code));
}

internal sealed record OutputShadowProjectionV2(long GeneratedUntil,long SentUntil,long PlayedUntil,long HeardUntil,bool Closed)
{
    internal static OutputShadowProjectionV2 From(IOutputStatusReaderV2 reader)
    {ArgumentNullException.ThrowIfNull(reader);var status=reader.Read();return new(status.GeneratedUntil,status.SentUntil,status.PlayedUntil,status.HeardUntil,status.Closed);}
}
