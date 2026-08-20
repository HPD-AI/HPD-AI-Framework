using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Tools;

internal sealed record OutputInterruptionStateV2(OutputControllerStateV2 Output,ToolTransactionStateV1 Tool);
internal sealed record OutputInterruptionReceiptV2(OperationId OperationId,OutputReceiptV2 OutputReceipt,ToolTransactionReceiptV1 ToolReceipt);

internal abstract record OutputInterruptionResultV2
{
    private OutputInterruptionResultV2(){}
    internal sealed record Applied(OutputInterruptionStateV2 State,OutputInterruptionReceiptV2 Receipt):OutputInterruptionResultV2;
    internal sealed record Duplicate(OutputInterruptionStateV2 State,OutputInterruptionReceiptV2 Receipt):OutputInterruptionResultV2;
    internal sealed record Rejected(OutputInterruptionStateV2 State,BoundedAscii SafeCode):OutputInterruptionResultV2;
}

internal static class OutputInterruptionCoordinatorV2
{
    internal static OutputInterruptionResultV2 Interrupt(OutputInterruptionStateV2 state,OperationId operationId,ushort maximumOutputReceipts,ushort maximumToolReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);if(!operationId.IsValid)throw new ArgumentException("An interruption operation is required.");
        if(state.Output.Plan.Generation!=state.Tool.Plan.OutputGeneration||!SameAuthority(state.Output.Plan.Authority,state.Tool.Plan.Authority))return Reject(state,"interruption-output-authority-mismatch");
        if(state.Tool.Receipts.TryGetValue(operationId,out var priorTool))
        {
            if(priorTool.Command is not ToolTransactionCommandV1.Advance {Target:ToolTransactionPhaseV1.ToolControlRecorded}||!state.Output.Receipts.TryGetValue(operationId,out var priorOutput))return Reject(state,"interruption-operation-contradiction");
            return new OutputInterruptionResultV2.Duplicate(state,new(operationId,priorOutput,priorTool));
        }
        var close=new OutputCommandV2.Close(operationId,state.Output.Status.Revision);var output=OutputReducerV2.Apply(state.Output,close,maximumOutputReceipts);
        if(output is OutputCommandResultV2.Rejected outputRejected)return Reject(state,outputRejected.SafeCode.ToString());
        var outputApplied=AssertApplied(output);var control=new ToolTransactionCommandV1.Advance(operationId,state.Tool.Snapshot.Revision,ToolTransactionPhaseV1.ToolControlRecorded,1);
        var tool=ToolTransactionSupervisorV1.Apply(state.Tool,control,maximumToolReceipts);
        if(tool is ToolTransactionResultV1.Rejected toolRejected)return Reject(state,toolRejected.SafeCode.ToString());
        var toolApplied=tool as ToolTransactionResultV1.Applied??throw new InvalidOperationException("A new interruption cannot be duplicate.");var next=new OutputInterruptionStateV2(outputApplied.State,toolApplied.State);
        return new OutputInterruptionResultV2.Applied(next,new(operationId,outputApplied.Receipt,toolApplied.Receipt));
    }
    private static OutputCommandResultV2.Applied AssertApplied(OutputCommandResultV2 result)=>result as OutputCommandResultV2.Applied??throw new InvalidOperationException("A new close cannot be duplicate.");
    private static bool SameAuthority(ExpectedAuthorityVectorV1 left,ExpectedAuthorityVectorV1 right)=>left.Session==right.Session&&left.Axes.SequenceEqual(right.Axes);
    private static OutputInterruptionResultV2.Rejected Reject(OutputInterruptionStateV2 state,string code)=>new(state,new BoundedAscii(code));
}
