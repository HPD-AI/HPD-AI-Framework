using HPD.Agent.Audio.Runtime.Tools;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ToolTransactionSupervisorV1Tests
{
    [Fact]
    public void T0_through_T10_require_exact_order_and_entry_intent_precedes_external_effect()
    {
        var state=Create();
        foreach(var phase in new[]{ToolTransactionPhaseV1.ToolControlRecorded,ToolTransactionPhaseV1.ToolArgumentsFinalized,ToolTransactionPhaseV1.ToolApprovalDecided,
            ToolTransactionPhaseV1.ToolDispositionChosen,ToolTransactionPhaseV1.ToolOwnerClaimed,ToolTransactionPhaseV1.ToolDispatchAuthorized,ToolTransactionPhaseV1.ToolEntryIntentRecorded,
            ToolTransactionPhaseV1.ToolExternalBoundaryEntered})state=Advance(state,phase);
        state=Applied(state,new ToolTransactionCommandV1.AdmitEffectEvidence(OperationId.Create(),state.Snapshot.Revision,true));
        state=Advance(state,ToolTransactionPhaseV1.ToolResultFinalized);state=Advance(state,ToolTransactionPhaseV1.ToolResultProjected);
        Assert.True(state.Snapshot.InterruptionRequested);Assert.True(state.Snapshot.ExternalBoundaryEntered);Assert.True(state.Snapshot.EffectOutcomeKnown);
    }
    [Fact]
    public void External_boundary_cannot_be_entered_without_durable_entry_intent()
    {
        var state=Create();state=Advance(state,ToolTransactionPhaseV1.ToolControlRecorded);
        Assert.Equal("tool-transition-invalid",Assert.IsType<ToolTransactionResultV1.Rejected>(ToolTransactionSupervisorV1.Apply(state,
            new ToolTransactionCommandV1.Advance(OperationId.Create(),state.Snapshot.Revision,ToolTransactionPhaseV1.ToolExternalBoundaryEntered,1),32)).SafeCode.ToString());
    }
    [Fact]
    public void Unknown_effect_blocks_result_until_explicit_reconciliation()
    {
        var state=ToExternal();state=Applied(state,new ToolTransactionCommandV1.AdmitEffectEvidence(OperationId.Create(),8,false));
        Assert.Equal("tool-transition-invalid",Assert.IsType<ToolTransactionResultV1.Rejected>(ToolTransactionSupervisorV1.Apply(state,
            new ToolTransactionCommandV1.Advance(OperationId.Create(),9,ToolTransactionPhaseV1.ToolResultFinalized,1),32)).SafeCode.ToString());
        state=Applied(state,new ToolTransactionCommandV1.ReconcileEffect(OperationId.Create(),9,true));state=Advance(state,ToolTransactionPhaseV1.ToolResultFinalized);
        Assert.Equal(ToolTransactionPhaseV1.ToolResultFinalized,state.Snapshot.Phase);
    }
    [Fact]
    public void Missing_route_returns_replacement_required_without_fabricated_resume()
    {
        var state=ToProjected();var result=Assert.IsType<ToolTransactionResultV1.ReplacementRequired>(ToolTransactionSupervisorV1.Apply(state,
            new ToolTransactionCommandV1.AuthorizeContinuation(OperationId.Create(),state.Snapshot.Revision,null),32));
        Assert.Equal("replacement-required",result.SafeCode.ToString());Assert.Equal(ToolTransactionPhaseV1.ToolResultProjected,result.State.Snapshot.Phase);
        Assert.IsType<ToolTransactionResultV1.RouteUnavailable>(ToolTransactionSupervisorV1.Apply(state,
            new ToolTransactionCommandV1.AuthorizeContinuation(OperationId.Create(),state.Snapshot.Revision,new JournalPositionV1(state.Plan.Authority.Session,1)),32));
    }
    [Fact]
    public void Terminalization_retry_is_exact_and_receipts_are_bounded()
    {
        var state=ToProjected();var operation=OperationId.Create();var command=new ToolTransactionCommandV1.Terminalize(operation,state.Snapshot.Revision,new BoundedAscii("replacement-required"));
        var applied=Assert.IsType<ToolTransactionResultV1.Applied>(ToolTransactionSupervisorV1.Apply(state,command,32));Assert.IsType<ToolTransactionResultV1.Duplicate>(ToolTransactionSupervisorV1.Apply(applied.State,command,32));
        Assert.Equal("tool-terminal",Assert.IsType<ToolTransactionResultV1.Rejected>(ToolTransactionSupervisorV1.Apply(applied.State,new ToolTransactionCommandV1.Terminalize(OperationId.Create(),applied.State.Snapshot.Revision,new BoundedAscii("again")),32)).SafeCode.ToString());
    }
    private static ToolTransactionStateV1 Create()
    {var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var tool=ToolGenerationId.Create();var output=OutputGenerationId.Create();var authority=ExpectedAuthorityVectorV1.Create(session,[new AuthorityAxisValueV1.Output(output),new AuthorityAxisValueV1.Tool(tool)]);return ToolTransactionSupervisorV1.Create(new(OperationId.Create(),tool,output,authority,new MonotonicStampV1(ClockDomainId.Create(),BootId.Create(),100),true));}
    private static ToolTransactionStateV1 ToExternal(){var state=Create();foreach(var phase in new[]{ToolTransactionPhaseV1.ToolControlRecorded,ToolTransactionPhaseV1.ToolArgumentsFinalized,ToolTransactionPhaseV1.ToolApprovalDecided,ToolTransactionPhaseV1.ToolDispositionChosen,ToolTransactionPhaseV1.ToolOwnerClaimed,ToolTransactionPhaseV1.ToolDispatchAuthorized,ToolTransactionPhaseV1.ToolEntryIntentRecorded,ToolTransactionPhaseV1.ToolExternalBoundaryEntered})state=Advance(state,phase);return state;}
    private static ToolTransactionStateV1 ToProjected(){var state=ToExternal();state=Applied(state,new ToolTransactionCommandV1.AdmitEffectEvidence(OperationId.Create(),8,true));state=Advance(state,ToolTransactionPhaseV1.ToolResultFinalized);return Advance(state,ToolTransactionPhaseV1.ToolResultProjected);}
    private static ToolTransactionStateV1 Advance(ToolTransactionStateV1 state,ToolTransactionPhaseV1 phase)=>Applied(state,new ToolTransactionCommandV1.Advance(OperationId.Create(),state.Snapshot.Revision,phase,1));
    private static ToolTransactionStateV1 Applied(ToolTransactionStateV1 state,ToolTransactionCommandV1 command)=>Assert.IsType<ToolTransactionResultV1.Applied>(ToolTransactionSupervisorV1.Apply(state,command,32)).State;
}
