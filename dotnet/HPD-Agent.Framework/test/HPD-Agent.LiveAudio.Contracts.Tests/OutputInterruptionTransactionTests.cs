using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Tools;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class OutputInterruptionTransactionTests
{
    [Fact]
    public void S7_control_atomically_closes_eligible_Output()
    {var f=Fixture(true);var applied=Assert.IsType<OutputInterruptionResult.Applied>(OutputInterruptionCoordinator.Interrupt(f.State,f.Operation,16,32));Assert.True(applied.State.Output.Status.Closed);Assert.True(applied.State.Tool.Snapshot.InterruptionRequested);}
    [Fact]
    public void Unsent_generated_output_refuses_interruption_without_tool_advance()
    {var f=Fixture(false);var rejected=Assert.IsType<OutputInterruptionResult.Rejected>(OutputInterruptionCoordinator.Interrupt(f.State,f.Operation,16,32));Assert.Equal("output-transition-invalid",rejected.SafeCode.ToString());Assert.Equal(ToolTransactionPhase.None,rejected.State.Tool.Snapshot.Phase);Assert.False(rejected.State.Output.Status.Closed);}
    [Fact]
    public void Output_and_tool_authority_must_be_identical()
    {var f=Fixture(true);var other=ToolGenerationId.Create();var authority=ExpectedAuthorityVectorV1.Create(f.State.Output.Plan.Authority.Session,[new AuthorityAxisValueV1.Output(f.State.Output.Plan.Generation),new AuthorityAxisValueV1.Tool(other)]);var tool=ToolTransactionSupervisor.Create(new ToolTransactionPlan(OperationId.Create(),other,f.State.Output.Plan.Generation,authority,new MonotonicStampV1(ClockDomainId.Create(),BootId.Create(),10),true));var rejected=Assert.IsType<OutputInterruptionResult.Rejected>(OutputInterruptionCoordinator.Interrupt(new(f.State.Output,tool),f.Operation,16,32));Assert.Equal("interruption-output-authority-mismatch",rejected.SafeCode.ToString());}
    [Fact]
    public void Exact_retry_returns_original_composite_receipt()
    {var f=Fixture(true);var applied=Assert.IsType<OutputInterruptionResult.Applied>(OutputInterruptionCoordinator.Interrupt(f.State,f.Operation,16,32));var duplicate=Assert.IsType<OutputInterruptionResult.Duplicate>(OutputInterruptionCoordinator.Interrupt(applied.State,f.Operation,16,32));Assert.Equal(applied.Receipt,duplicate.Receipt);}
    [Fact]
    public void Closed_output_is_terminal_for_new_interruption_operation()
    {var f=Fixture(true);var applied=Assert.IsType<OutputInterruptionResult.Applied>(OutputInterruptionCoordinator.Interrupt(f.State,f.Operation,16,32));var rejected=Assert.IsType<OutputInterruptionResult.Rejected>(OutputInterruptionCoordinator.Interrupt(applied.State,OperationId.Create(),16,32));Assert.Equal("output-closed",rejected.SafeCode.ToString());}

    private static (OutputInterruptionState State,OperationId Operation) Fixture(bool sent)
    {
        var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var output=OutputGenerationId.Create();var tool=ToolGenerationId.Create();var authority=ExpectedAuthorityVectorV1.Create(session,[new AuthorityAxisValueV1.Output(output),new AuthorityAxisValueV1.Tool(tool)]);
        var plan=new OutputPlan(OperationId.Create(),output,authority,10);var state=new OutputControllerState(plan,new OutputStatus(sent?2UL:1UL,5,sent?5:0,0,0,false));var toolState=ToolTransactionSupervisor.Create(new ToolTransactionPlan(OperationId.Create(),tool,output,authority,new MonotonicStampV1(ClockDomainId.Create(),BootId.Create(),100),true));return(new(state,toolState),OperationId.Create());
    }
}
