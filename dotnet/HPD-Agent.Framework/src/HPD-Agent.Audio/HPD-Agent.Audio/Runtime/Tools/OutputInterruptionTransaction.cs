using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Tools;

internal sealed record OutputInterruptionState(OutputControllerState Output,ToolTransactionState Tool);
internal sealed record OutputInterruptionReceipt(OperationId OperationId,OutputReceipt OutputReceipt,ToolTransactionReceipt ToolReceipt);

internal abstract record OutputInterruptionResult
{
    private OutputInterruptionResult(){}
    internal sealed record Applied(OutputInterruptionState State,OutputInterruptionReceipt Receipt):OutputInterruptionResult;
    internal sealed record Duplicate(OutputInterruptionState State,OutputInterruptionReceipt Receipt):OutputInterruptionResult;
    internal sealed record Rejected(OutputInterruptionState State,BoundedAscii SafeCode):OutputInterruptionResult;
}

internal static class OutputInterruptionCoordinator
{
    internal static OutputInterruptionResult Interrupt(OutputInterruptionState state,OperationId operationId,ushort maximumOutputReceipts,ushort maximumToolReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);if(!operationId.IsValid)throw new ArgumentException("An interruption operation is required.");
        if(state.Output.Plan.Generation!=state.Tool.Plan.OutputGeneration||!SameAuthority(state.Output.Plan.Authority,state.Tool.Plan.Authority))return Reject(state,"interruption-output-authority-mismatch");
        if(state.Tool.Receipts.TryGetValue(operationId,out var priorTool))
        {
            if(priorTool.Command is not ToolTransactionCommand.Advance {Target:ToolTransactionPhase.ToolControlRecorded}||!state.Output.Receipts.TryGetValue(operationId,out var priorOutput))return Reject(state,"interruption-operation-contradiction");
            return new OutputInterruptionResult.Duplicate(state,new(operationId,priorOutput,priorTool));
        }
        var close=new OutputCommand.Close(operationId,state.Output.Status.Revision);var output=OutputReducer.Apply(state.Output,close,maximumOutputReceipts);
        if(output is OutputCommandResult.Rejected outputRejected)return Reject(state,outputRejected.SafeCode.ToString());
        var outputApplied=AssertApplied(output);var control=new ToolTransactionCommand.Advance(operationId,state.Tool.Snapshot.Revision,ToolTransactionPhase.ToolControlRecorded,1);
        var tool=ToolTransactionSupervisor.Apply(state.Tool,control,maximumToolReceipts);
        if(tool is ToolTransactionResult.Rejected toolRejected)return Reject(state,toolRejected.SafeCode.ToString());
        var toolApplied=tool as ToolTransactionResult.Applied??throw new InvalidOperationException("A new interruption cannot be duplicate.");var next=new OutputInterruptionState(outputApplied.State,toolApplied.State);
        return new OutputInterruptionResult.Applied(next,new(operationId,outputApplied.Receipt,toolApplied.Receipt));
    }
    private static OutputCommandResult.Applied AssertApplied(OutputCommandResult result)=>result as OutputCommandResult.Applied??throw new InvalidOperationException("A new close cannot be duplicate.");
    private static bool SameAuthority(ExpectedAuthorityVectorV1 left,ExpectedAuthorityVectorV1 right)=>left.Session==right.Session&&left.Axes.SequenceEqual(right.Axes);
    private static OutputInterruptionResult.Rejected Reject(OutputInterruptionState state,string code)=>new(state,new BoundedAscii(code));
}
