using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Tools;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class LiveAudioToolGenerationV1Tests
{
    [Fact] public void S7_owner_closes_its_exact_OutputV2_controller()
    {var f=Fixture();var result=Assert.IsType<OutputInterruptionResultV2.Applied>(f.Owner.Interrupt(f.Output,f.Tool,f.Operation,16));Assert.True(f.Output.Read().Closed);Assert.True(result.State.Tool.Snapshot.InterruptionRequested);}
    [Fact] public void Legacy_generation_has_no_S7_effect_owner()
    {var s=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());Assert.Null(LiveAudioToolGenerationV1.TryCreate(ExpectedAuthorityVectorV1.Create(s,[])));}
    [Fact] public void Foreign_controller_cannot_be_interrupted()
    {var f=Fixture();var other=Fixture();var rejected=Assert.IsType<OutputInterruptionResultV2.Rejected>(f.Owner.Interrupt(other.Output,f.Tool,f.Operation,16));Assert.Equal("interruption-generation-stale",rejected.SafeCode.ToString());Assert.False(other.Output.Read().Closed);}
    [Fact] public void Exact_retry_returns_the_original_composite_receipt()
    {var f=Fixture();var first=Assert.IsType<OutputInterruptionResultV2.Applied>(f.Owner.Interrupt(f.Output,f.Tool,f.Operation,16));var retry=Assert.IsType<OutputInterruptionResultV2.Duplicate>(f.Owner.Interrupt(f.Output,first.State.Tool,f.Operation,16));Assert.Equal(first.Receipt,retry.Receipt);}
    private static (LiveAudioToolGenerationV1 Owner,InMemoryOutputControllerV2 Output,ToolTransactionStateV1 Tool,OperationId Operation) Fixture()
    {var s=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var o=OutputGenerationId.Create();var t=ToolGenerationId.Create();var a=ExpectedAuthorityVectorV1.Create(s,[new AuthorityAxisValueV1.Output(o),new AuthorityAxisValueV1.Tool(t)]);var output=new InMemoryOutputControllerV2(new OutputPlanV2(OperationId.Create(),o,a,10),16);Assert.IsType<OutputCommandResultV2.Applied>(output.Apply(new OutputCommandV2.Generate(OperationId.Create(),0,5)));Assert.IsType<OutputCommandResultV2.Applied>(output.Apply(new OutputCommandV2.Send(OperationId.Create(),1,5)));var tool=ToolTransactionSupervisorV1.Create(new(OperationId.Create(),t,o,a,new MonotonicStampV1(ClockDomainId.Create(),BootId.Create(),100),true));return(new(a),output,tool,OperationId.Create());}
}
