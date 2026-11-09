using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class OutputControllerTests
{
    [Fact]
    public void Four_axes_advance_independently_in_authority_order()
    {
        var controller=Controller();
        Applied(controller,new OutputCommand.Generate(OperationId.Create(),0,100));
        Applied(controller,new OutputCommand.Send(OperationId.Create(),1,80));
        Applied(controller,new OutputCommand.Play(OperationId.Create(),2,60));
        Applied(controller,new OutputCommand.Hear(OperationId.Create(),3,40));
        var status=controller.Read();Assert.Equal(100,status.GeneratedUntil);Assert.Equal(80,status.SentUntil);Assert.Equal(60,status.PlayedUntil);Assert.Equal(40,status.HeardUntil);
    }
    [Fact]
    public void Downstream_axes_cannot_claim_unproven_upstream_ranges()
    {
        var controller=Controller();Applied(controller,new OutputCommand.Generate(OperationId.Create(),0,10));
        Assert.Equal("output-transition-invalid",Assert.IsType<OutputCommandResult.Rejected>(controller.Apply(new OutputCommand.Play(OperationId.Create(),1,1))).SafeCode.ToString());
        Assert.Equal(1UL,controller.Read().Revision);
    }
    [Fact]
    public void Retry_is_exact_and_operation_contradiction_is_closed()
    {
        var controller=Controller();var operation=OperationId.Create();var command=new OutputCommand.Generate(operation,0,10);
        Assert.IsType<OutputCommandResult.Applied>(controller.Apply(command));Assert.IsType<OutputCommandResult.Duplicate>(controller.Apply(command));
        Assert.Equal("output-operation-contradiction",Assert.IsType<OutputCommandResult.Rejected>(controller.Apply(new OutputCommand.Generate(operation,1,20))).SafeCode.ToString());
    }
    [Fact]
    public void Close_requires_all_generated_units_sent_and_is_terminal()
    {
        var controller=Controller();Applied(controller,new OutputCommand.Generate(OperationId.Create(),0,10));
        Assert.IsType<OutputCommandResult.Rejected>(controller.Apply(new OutputCommand.Close(OperationId.Create(),1)));
        Applied(controller,new OutputCommand.Send(OperationId.Create(),1,10));Applied(controller,new OutputCommand.Close(OperationId.Create(),2));
        Assert.Equal("output-closed",Assert.IsType<OutputCommandResult.Rejected>(controller.Apply(new OutputCommand.Play(OperationId.Create(),3,1))).SafeCode.ToString());
    }
    [Fact]
    public void Shadow_projection_is_read_only_and_retains_axis_separation()
    {
        var controller=Controller();Applied(controller,new OutputCommand.Generate(OperationId.Create(),0,8));Applied(controller,new OutputCommand.Send(OperationId.Create(),1,5));
        var projection=OutputShadowProjection.From(controller);Assert.Equal(8,projection.GeneratedUntil);Assert.Equal(5,projection.SentUntil);Assert.Equal(0,projection.PlayedUntil);Assert.Equal(2UL,controller.Read().Revision);
    }
    private static InMemoryOutputController Controller()
    {var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var generation=OutputGenerationId.Create();var authority=ExpectedAuthorityVectorV1.Create(session,[new AuthorityAxisValueV1.Output(generation)]);return new(new OutputPlan(OperationId.Create(),generation,authority,100),16);}
    private static void Applied(InMemoryOutputController controller,OutputCommand command)=>Assert.IsType<OutputCommandResult.Applied>(controller.Apply(command));
}
