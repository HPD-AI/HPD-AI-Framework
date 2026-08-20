using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Tools;

internal sealed class LiveAudioToolGenerationV1
{
    internal LiveAudioToolGenerationV1(ExpectedAuthorityVectorV1 authority)
    {
        Authority=authority??throw new ArgumentNullException(nameof(authority));
        var tools=authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Tool>().ToArray();
        var outputs=authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Output>().ToArray();
        if(tools.Length!=1||outputs.Length!=1)throw new ArgumentException("S7 activation requires exact Tool and Output axes.",nameof(authority));
        ToolGeneration=tools[0].Value;OutputGeneration=outputs[0].Value;
    }
    internal ExpectedAuthorityVectorV1 Authority{get;} internal ToolGenerationId ToolGeneration{get;} internal OutputGenerationId OutputGeneration{get;}
    internal static LiveAudioToolGenerationV1? TryCreate(ExpectedAuthorityVectorV1 authority)
    {ArgumentNullException.ThrowIfNull(authority);var hasTool=authority.Axes.Any(static x=>x.Value is AuthorityAxisValueV1.Tool);var hasOutput=authority.Axes.Any(static x=>x.Value is AuthorityAxisValueV1.Output);return !hasTool&&!hasOutput?null:new(authority);}
    internal OutputInterruptionResultV2 Interrupt(InMemoryOutputControllerV2 output,ToolTransactionStateV1 tool,OperationId operationId,ushort maximumToolReceipts)
    {
        ArgumentNullException.ThrowIfNull(output);ArgumentNullException.ThrowIfNull(tool);
        if(!Same(Authority,output.Plan.Authority)||!Same(Authority,tool.Plan.Authority)||tool.Plan.ToolGeneration!=ToolGeneration||tool.Plan.OutputGeneration!=OutputGeneration)
            return new OutputInterruptionResultV2.Rejected(new(output.State,tool),new BoundedAscii("interruption-generation-stale"));
        return output.Interrupt(tool,operationId,maximumToolReceipts);
    }
    private static bool Same(ExpectedAuthorityVectorV1 left,ExpectedAuthorityVectorV1 right)=>left.Session==right.Session&&left.Axes.SequenceEqual(right.Axes);
}
