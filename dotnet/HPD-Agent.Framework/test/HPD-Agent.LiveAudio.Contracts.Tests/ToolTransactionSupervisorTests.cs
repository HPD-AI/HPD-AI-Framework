using HPD.Agent.Audio.Runtime.Tools;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ToolTransactionSupervisorTests
{
    [Fact]
    public void T0_through_T10_require_exact_order_and_entry_intent_precedes_external_effect()
    {
        var state=Create();
        foreach(var phase in new[]{ToolTransactionPhase.ToolControlRecorded,ToolTransactionPhase.ToolArgumentsFinalized,ToolTransactionPhase.ToolApprovalDecided,
            ToolTransactionPhase.ToolDispositionChosen,ToolTransactionPhase.ToolOwnerClaimed,ToolTransactionPhase.ToolDispatchAuthorized,ToolTransactionPhase.ToolEntryIntentRecorded,
            ToolTransactionPhase.ToolExternalBoundaryEntered})state=Advance(state,phase);
        state=Applied(state,new ToolTransactionCommand.AdmitEffectEvidence(OperationId.Create(),state.Snapshot.Revision,true));
        state=Advance(state,ToolTransactionPhase.ToolResultFinalized);state=Advance(state,ToolTransactionPhase.ToolResultProjected);
        Assert.True(state.Snapshot.InterruptionRequested);Assert.True(state.Snapshot.ExternalBoundaryEntered);Assert.True(state.Snapshot.EffectOutcomeKnown);
    }
    [Fact]
    public void External_boundary_cannot_be_entered_without_durable_entry_intent()
    {
        var state=Create();state=Advance(state,ToolTransactionPhase.ToolControlRecorded);
        Assert.Equal("tool-transition-invalid",Assert.IsType<ToolTransactionResult.Rejected>(ToolTransactionSupervisor.Apply(state,
            new ToolTransactionCommand.Advance(OperationId.Create(),state.Snapshot.Revision,ToolTransactionPhase.ToolExternalBoundaryEntered,1),32)).SafeCode.ToString());
    }
    [Fact]
    public void Unknown_effect_blocks_result_until_explicit_reconciliation()
    {
        var state=ToExternal();state=Applied(state,new ToolTransactionCommand.AdmitEffectEvidence(OperationId.Create(),8,false));
        Assert.Equal("tool-transition-invalid",Assert.IsType<ToolTransactionResult.Rejected>(ToolTransactionSupervisor.Apply(state,
            new ToolTransactionCommand.Advance(OperationId.Create(),9,ToolTransactionPhase.ToolResultFinalized,1),32)).SafeCode.ToString());
        state=Applied(state,new ToolTransactionCommand.ReconcileEffect(OperationId.Create(),9,true));state=Advance(state,ToolTransactionPhase.ToolResultFinalized);
        Assert.Equal(ToolTransactionPhase.ToolResultFinalized,state.Snapshot.Phase);
    }
    [Fact]
    public void Missing_route_returns_replacement_required_without_fabricated_resume()
    {
        var state=ToProjected();var result=Assert.IsType<ToolTransactionResult.ReplacementRequired>(ToolTransactionSupervisor.Apply(state,
            new ToolTransactionCommand.AuthorizeContinuation(OperationId.Create(),state.Snapshot.Revision,null),32));
        Assert.Equal("replacement-required",result.SafeCode.ToString());Assert.Equal(ToolTransactionPhase.ToolResultProjected,result.State.Snapshot.Phase);
        Assert.IsType<ToolTransactionResult.RouteUnavailable>(ToolTransactionSupervisor.Apply(state,
            new ToolTransactionCommand.AuthorizeContinuation(OperationId.Create(),state.Snapshot.Revision,new JournalPositionV1(state.Plan.Authority.Session,1)),32));
    }
    [Fact]
    public void Terminalization_retry_is_exact_and_receipts_are_bounded()
    {
        var state=ToProjected();var operation=OperationId.Create();var command=new ToolTransactionCommand.Terminalize(operation,state.Snapshot.Revision,new BoundedAscii("replacement-required"));
        var applied=Assert.IsType<ToolTransactionResult.Applied>(ToolTransactionSupervisor.Apply(state,command,32));Assert.IsType<ToolTransactionResult.Duplicate>(ToolTransactionSupervisor.Apply(applied.State,command,32));
        Assert.Equal("tool-terminal",Assert.IsType<ToolTransactionResult.Rejected>(ToolTransactionSupervisor.Apply(applied.State,new ToolTransactionCommand.Terminalize(OperationId.Create(),applied.State.Snapshot.Revision,new BoundedAscii("again")),32)).SafeCode.ToString());
    }
    private static ToolTransactionState Create()
    {var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var tool=ToolGenerationId.Create();var output=OutputGenerationId.Create();var authority=ExpectedAuthorityVectorV1.Create(session,[new AuthorityAxisValueV1.Output(output),new AuthorityAxisValueV1.Tool(tool)]);return ToolTransactionSupervisor.Create(new(OperationId.Create(),tool,output,authority,new MonotonicStampV1(ClockDomainId.Create(),BootId.Create(),100),true));}
    private static ToolTransactionState ToExternal(){var state=Create();foreach(var phase in new[]{ToolTransactionPhase.ToolControlRecorded,ToolTransactionPhase.ToolArgumentsFinalized,ToolTransactionPhase.ToolApprovalDecided,ToolTransactionPhase.ToolDispositionChosen,ToolTransactionPhase.ToolOwnerClaimed,ToolTransactionPhase.ToolDispatchAuthorized,ToolTransactionPhase.ToolEntryIntentRecorded,ToolTransactionPhase.ToolExternalBoundaryEntered})state=Advance(state,phase);return state;}
    private static ToolTransactionState ToProjected(){var state=ToExternal();state=Applied(state,new ToolTransactionCommand.AdmitEffectEvidence(OperationId.Create(),8,true));state=Advance(state,ToolTransactionPhase.ToolResultFinalized);return Advance(state,ToolTransactionPhase.ToolResultProjected);}
    private static ToolTransactionState Advance(ToolTransactionState state,ToolTransactionPhase phase)=>Applied(state,new ToolTransactionCommand.Advance(OperationId.Create(),state.Snapshot.Revision,phase,1));
    private static ToolTransactionState Applied(ToolTransactionState state,ToolTransactionCommand command)=>Assert.IsType<ToolTransactionResult.Applied>(ToolTransactionSupervisor.Apply(state,command,32)).State;
}
