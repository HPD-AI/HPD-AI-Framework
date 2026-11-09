using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Output;

internal enum OutputSynthesisFamily:ushort{SegmentedPcm=1,PushPcm=2}
internal sealed record OutputSynthesisRequest(OperationId OperationId,OutputSynthesisFamily Family,string Text,long MaximumUnits);
internal sealed record OutputSynthesisEvidence(OperationId OperationId,OutputSynthesisFamily Family,long GeneratedUnits,Hash256 PayloadFingerprint);
internal interface IOutputSynthesisProvider{OutputSynthesisEvidence Synthesize(OutputSynthesisRequest request);}

internal sealed class DeterministicPcmSynthesisProvider(OutputSynthesisFamily family):IOutputSynthesisProvider
{
    public OutputSynthesisEvidence Synthesize(OutputSynthesisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);if(!request.OperationId.IsValid||request.Family!=family||string.IsNullOrWhiteSpace(request.Text)||request.MaximumUnits<=0)throw new ArgumentException("Synthesis request is invalid.");
        var bytes=System.Text.Encoding.UTF8.GetBytes(request.Text);var units=Math.Min(request.MaximumUnits,bytes.LongLength);return new(request.OperationId,family,units,Hash256.Compute(bytes));
    }
}

internal abstract record OutputSinkEffect
{
    private protected OutputSinkEffect(OperationId operationId,long until){if(!operationId.IsValid||until<=0)throw new ArgumentException("Sink effect invalid.");OperationId=operationId;Until=until;}
    internal OperationId OperationId{get;}internal long Until{get;}
    internal sealed record Send(OperationId O,long U):OutputSinkEffect(O,U);internal sealed record Play(OperationId O,long U):OutputSinkEffect(O,U);internal sealed record Hear(OperationId O,long U):OutputSinkEffect(O,U);
}
internal abstract record OutputSinkEffectResult
{
    private OutputSinkEffectResult(){}internal sealed record Acknowledged(OutputSinkEffect Effect):OutputSinkEffectResult;internal sealed record Refused(BoundedAscii SafeCode):OutputSinkEffectResult;internal sealed record OutcomeUnknown(BoundedAscii SafeCode):OutputSinkEffectResult;
}
internal interface IOutputSinkEffectPort{ValueTask<OutputSinkEffectResult> ApplyAsync(OutputSinkEffect effect,CancellationToken cancellationToken=default);}
internal sealed class ManualOutputSinkEffectPort: IOutputSinkEffectPort
{
    private readonly Func<OutputSinkEffect,OutputSinkEffectResult> _handler;
    internal ManualOutputSinkEffectPort(Func<OutputSinkEffect,OutputSinkEffectResult>? handler=null)=>_handler=handler??(static effect=>new OutputSinkEffectResult.Acknowledged(effect));
    public ValueTask<OutputSinkEffectResult> ApplyAsync(OutputSinkEffect effect,CancellationToken cancellationToken=default)
    {cancellationToken.ThrowIfCancellationRequested();return ValueTask.FromResult(_handler(effect??throw new ArgumentNullException(nameof(effect))));}
}

internal abstract record OutputPipelineResult
{
    private OutputPipelineResult(){}internal sealed record Applied(OutputControllerState State,OutputReceipt Receipt):OutputPipelineResult;internal sealed record EffectRefused(OutputControllerState State,BoundedAscii SafeCode):OutputPipelineResult;internal sealed record OutcomeUnknown(OutputControllerState State,BoundedAscii SafeCode):OutputPipelineResult;internal sealed record Rejected(OutputControllerState State,BoundedAscii SafeCode):OutputPipelineResult;
}
internal static class OutputTtsSinkPipeline
{
    internal static OutputPipelineResult Generate(OutputControllerState state,OutputSynthesisRequest request,IOutputSynthesisProvider provider,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(provider);return Generate(state,provider.Synthesize(request),maximumReceipts);
    }
    internal static OutputPipelineResult Generate(OutputControllerState state,OutputSynthesisEvidence evidence,ushort maximumReceipts)
    {ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(evidence);if(evidence.GeneratedUnits<=state.Status.GeneratedUntil||evidence.GeneratedUnits>state.Plan.MaximumUnits)return Reject(state,"output-synthesis-evidence-invalid");return Reduce(state,new OutputCommand.Generate(evidence.OperationId,state.Status.Revision,evidence.GeneratedUnits),maximumReceipts);}
    internal static ValueTask<OutputPipelineResult> SendAsync(OutputControllerState state,OutputSinkEffect.Send effect,IOutputSinkEffectPort sink,ushort maximumReceipts,CancellationToken cancellationToken=default)=>EffectAsync(state,effect,sink,new OutputCommand.Send(effect.OperationId,state.Status.Revision,effect.Until),maximumReceipts,cancellationToken);
    internal static ValueTask<OutputPipelineResult> PlayAsync(OutputControllerState state,OutputSinkEffect.Play effect,IOutputSinkEffectPort sink,ushort maximumReceipts,CancellationToken cancellationToken=default)=>EffectAsync(state,effect,sink,new OutputCommand.Play(effect.OperationId,state.Status.Revision,effect.Until),maximumReceipts,cancellationToken);
    internal static ValueTask<OutputPipelineResult> HearAsync(OutputControllerState state,OutputSinkEffect.Hear effect,IOutputSinkEffectPort sink,ushort maximumReceipts,CancellationToken cancellationToken=default)=>EffectAsync(state,effect,sink,new OutputCommand.Hear(effect.OperationId,state.Status.Revision,effect.Until),maximumReceipts,cancellationToken);
    private static async ValueTask<OutputPipelineResult> EffectAsync(OutputControllerState state,OutputSinkEffect effect,IOutputSinkEffectPort sink,OutputCommand command,ushort maximumReceipts,CancellationToken cancellationToken)
    {ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(sink);var result=await sink.ApplyAsync(effect,cancellationToken).ConfigureAwait(false);return result switch{OutputSinkEffectResult.Acknowledged x when x.Effect==effect=>Reduce(state,command,maximumReceipts),OutputSinkEffectResult.Acknowledged=>Reject(state,"output-sink-receipt-invalid"),OutputSinkEffectResult.Refused x=>new OutputPipelineResult.EffectRefused(state,x.SafeCode),OutputSinkEffectResult.OutcomeUnknown x=>new OutputPipelineResult.OutcomeUnknown(state,x.SafeCode),_=>throw new InvalidOperationException()};}
    private static OutputPipelineResult Reduce(OutputControllerState state,OutputCommand command,ushort maximumReceipts)=>OutputReducer.Apply(state,command,maximumReceipts) switch{OutputCommandResult.Applied x=>new OutputPipelineResult.Applied(x.State,x.Receipt),OutputCommandResult.Rejected x=>new OutputPipelineResult.Rejected(state,x.SafeCode),OutputCommandResult.Duplicate x=>new OutputPipelineResult.Applied(x.State,x.Receipt),_=>throw new InvalidOperationException()};
    private static OutputPipelineResult.Rejected Reject(OutputControllerState state,string code)=>new(state,new BoundedAscii(code));
}
