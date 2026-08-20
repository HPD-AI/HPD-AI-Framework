using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Output;

internal enum OutputSynthesisFamilyV2:ushort{SegmentedPcm=1,PushPcm=2}
internal sealed record OutputSynthesisRequestV2(OperationId OperationId,OutputSynthesisFamilyV2 Family,string Text,long MaximumUnits);
internal sealed record OutputSynthesisEvidenceV2(OperationId OperationId,OutputSynthesisFamilyV2 Family,long GeneratedUnits,Hash256 PayloadFingerprint);
internal interface IOutputSynthesisProviderV2{OutputSynthesisEvidenceV2 Synthesize(OutputSynthesisRequestV2 request);}

internal sealed class DeterministicPcmSynthesisProviderV2(OutputSynthesisFamilyV2 family):IOutputSynthesisProviderV2
{
    public OutputSynthesisEvidenceV2 Synthesize(OutputSynthesisRequestV2 request)
    {
        ArgumentNullException.ThrowIfNull(request);if(!request.OperationId.IsValid||request.Family!=family||string.IsNullOrWhiteSpace(request.Text)||request.MaximumUnits<=0)throw new ArgumentException("Synthesis request is invalid.");
        var bytes=System.Text.Encoding.UTF8.GetBytes(request.Text);var units=Math.Min(request.MaximumUnits,bytes.LongLength);return new(request.OperationId,family,units,Hash256.Compute(bytes));
    }
}

internal abstract record OutputSinkEffectV2
{
    private protected OutputSinkEffectV2(OperationId operationId,long until){if(!operationId.IsValid||until<=0)throw new ArgumentException("Sink effect invalid.");OperationId=operationId;Until=until;}
    internal OperationId OperationId{get;}internal long Until{get;}
    internal sealed record Send(OperationId O,long U):OutputSinkEffectV2(O,U);internal sealed record Play(OperationId O,long U):OutputSinkEffectV2(O,U);internal sealed record Hear(OperationId O,long U):OutputSinkEffectV2(O,U);
}
internal abstract record OutputSinkEffectResultV2
{
    private OutputSinkEffectResultV2(){}internal sealed record Acknowledged(OutputSinkEffectV2 Effect):OutputSinkEffectResultV2;internal sealed record Refused(BoundedAscii SafeCode):OutputSinkEffectResultV2;internal sealed record OutcomeUnknown(BoundedAscii SafeCode):OutputSinkEffectResultV2;
}
internal interface IOutputSinkEffectPortV2{OutputSinkEffectResultV2 Apply(OutputSinkEffectV2 effect);}
internal sealed class ManualOutputSinkEffectPortV2: IOutputSinkEffectPortV2
{
    private readonly Func<OutputSinkEffectV2,OutputSinkEffectResultV2> _handler;
    internal ManualOutputSinkEffectPortV2(Func<OutputSinkEffectV2,OutputSinkEffectResultV2>? handler=null)=>_handler=handler??(static effect=>new OutputSinkEffectResultV2.Acknowledged(effect));
    public OutputSinkEffectResultV2 Apply(OutputSinkEffectV2 effect)=>_handler(effect??throw new ArgumentNullException(nameof(effect)));
}

internal abstract record OutputPipelineResultV2
{
    private OutputPipelineResultV2(){}internal sealed record Applied(OutputControllerStateV2 State,OutputReceiptV2 Receipt):OutputPipelineResultV2;internal sealed record EffectRefused(OutputControllerStateV2 State,BoundedAscii SafeCode):OutputPipelineResultV2;internal sealed record OutcomeUnknown(OutputControllerStateV2 State,BoundedAscii SafeCode):OutputPipelineResultV2;internal sealed record Rejected(OutputControllerStateV2 State,BoundedAscii SafeCode):OutputPipelineResultV2;
}
internal static class OutputTtsSinkPipelineV2
{
    internal static OutputPipelineResultV2 Generate(OutputControllerStateV2 state,OutputSynthesisRequestV2 request,IOutputSynthesisProviderV2 provider,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(provider);var evidence=provider.Synthesize(request);if(evidence.GeneratedUnits<=state.Status.GeneratedUntil||evidence.GeneratedUnits>state.Plan.MaximumUnits)return Reject(state,"output-synthesis-evidence-invalid");return Reduce(state,new OutputCommandV2.Generate(evidence.OperationId,state.Status.Revision,evidence.GeneratedUnits),maximumReceipts);
    }
    internal static OutputPipelineResultV2 Send(OutputControllerStateV2 state,OutputSinkEffectV2.Send effect,IOutputSinkEffectPortV2 sink,ushort maximumReceipts)=>Effect(state,effect,sink,new OutputCommandV2.Send(effect.OperationId,state.Status.Revision,effect.Until),maximumReceipts);
    internal static OutputPipelineResultV2 Play(OutputControllerStateV2 state,OutputSinkEffectV2.Play effect,IOutputSinkEffectPortV2 sink,ushort maximumReceipts)=>Effect(state,effect,sink,new OutputCommandV2.Play(effect.OperationId,state.Status.Revision,effect.Until),maximumReceipts);
    internal static OutputPipelineResultV2 Hear(OutputControllerStateV2 state,OutputSinkEffectV2.Hear effect,IOutputSinkEffectPortV2 sink,ushort maximumReceipts)=>Effect(state,effect,sink,new OutputCommandV2.Hear(effect.OperationId,state.Status.Revision,effect.Until),maximumReceipts);
    private static OutputPipelineResultV2 Effect(OutputControllerStateV2 state,OutputSinkEffectV2 effect,IOutputSinkEffectPortV2 sink,OutputCommandV2 command,ushort maximumReceipts)
    {ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(sink);return sink.Apply(effect) switch{OutputSinkEffectResultV2.Acknowledged=>Reduce(state,command,maximumReceipts),OutputSinkEffectResultV2.Refused x=>new OutputPipelineResultV2.EffectRefused(state,x.SafeCode),OutputSinkEffectResultV2.OutcomeUnknown x=>new OutputPipelineResultV2.OutcomeUnknown(state,x.SafeCode),_=>throw new InvalidOperationException()};}
    private static OutputPipelineResultV2 Reduce(OutputControllerStateV2 state,OutputCommandV2 command,ushort maximumReceipts)=>OutputReducerV2.Apply(state,command,maximumReceipts) switch{OutputCommandResultV2.Applied x=>new OutputPipelineResultV2.Applied(x.State,x.Receipt),OutputCommandResultV2.Rejected x=>new OutputPipelineResultV2.Rejected(state,x.SafeCode),OutputCommandResultV2.Duplicate x=>new OutputPipelineResultV2.Applied(x.State,x.Receipt),_=>throw new InvalidOperationException()};
    private static OutputPipelineResultV2.Rejected Reject(OutputControllerStateV2 state,string code)=>new(state,new BoundedAscii(code));
}
