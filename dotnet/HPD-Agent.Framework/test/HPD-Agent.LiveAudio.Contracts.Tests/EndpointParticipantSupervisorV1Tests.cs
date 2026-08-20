using HPD.Agent.Audio.Runtime.Endpointing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class EndpointParticipantSupervisorV1Tests
{
    [Fact]
    public void Reserve_prepare_effective_stop_releases_all_capacity()
    {
        var f = new Fixture();
        var state = f.State;
        state = Applied(state, new EndpointParticipantCommandV1.Reserve(OperationId.Create(), 0, f.Plan1));
        Assert.Equal(4, state.Snapshot.Capacity.Used[f.Cpu]);
        state = Applied(state, new EndpointParticipantCommandV1.Prepared(OperationId.Create(), 1));
        state = Applied(state, new EndpointParticipantCommandV1.Effective(OperationId.Create(), 2));
        state = Applied(state, new EndpointParticipantCommandV1.Stop(OperationId.Create(), 3));
        state = Applied(state, new EndpointParticipantCommandV1.Stopped(OperationId.Create(), 4));

        Assert.Equal(EndpointParticipantStateV1.Stopped, state.Snapshot.State);
        Assert.Equal(0, state.Snapshot.Capacity.Used[f.Cpu]);
        Assert.Null(state.Snapshot.CurrentPlan);
    }

    [Fact]
    public void Replacement_reserves_overlap_then_releases_predecessor_without_leak()
    {
        var f = new Fixture();
        var state = f.Effective();
        state = Applied(state, new EndpointParticipantCommandV1.PrepareReplacement(OperationId.Create(), 3, f.Plan2));
        Assert.Equal(10, state.Snapshot.Capacity.Used[f.Cpu]);
        state = Applied(state, new EndpointParticipantCommandV1.CommitReplacement(OperationId.Create(), 4));

        Assert.Equal(EndpointParticipantStateV1.Effective, state.Snapshot.State);
        Assert.Equal(f.Plan2.Generation, state.Snapshot.CurrentPlan!.Generation);
        Assert.Equal(6, state.Snapshot.Capacity.Used[f.Cpu]);
        Assert.Null(state.Snapshot.PendingPlan);
    }

    [Fact]
    public void Exact_retry_is_duplicate_and_same_operation_difference_is_contradiction()
    {
        var f = new Fixture();
        var operation = OperationId.Create();
        var command = new EndpointParticipantCommandV1.Reserve(operation, 0, f.Plan1);
        var applied = Assert.IsType<EndpointParticipantResultV1.Applied>(EndpointParticipantSupervisorV1.Apply(f.State, command, 16));
        Assert.IsType<EndpointParticipantResultV1.Duplicate>(EndpointParticipantSupervisorV1.Apply(applied.State, command, 16));
        Assert.Equal("participant-operation-contradiction", Assert.IsType<EndpointParticipantResultV1.Rejected>(
            EndpointParticipantSupervisorV1.Apply(applied.State,
                new EndpointParticipantCommandV1.Reserve(operation, 1, f.Plan1), 16)).SafeCode.ToString());
    }

    [Fact]
    public void Capacity_revision_generation_and_authority_fail_closed()
    {
        var f = new Fixture(limit: 5);
        Assert.Equal("participant-capacity-refused", Assert.IsType<EndpointParticipantResultV1.Rejected>(
            EndpointParticipantSupervisorV1.Apply(f.State,
                new EndpointParticipantCommandV1.Reserve(OperationId.Create(), 0, f.Plan2), 16)).SafeCode.ToString());
        Assert.Equal("participant-revision-conflict", Assert.IsType<EndpointParticipantResultV1.Rejected>(
            EndpointParticipantSupervisorV1.Apply(f.State,
                new EndpointParticipantCommandV1.Reserve(OperationId.Create(), 1, f.Plan1), 16)).SafeCode.ToString());

        var effective = f.Effective();
        var otherSession = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var wrongAuthority = ExpectedAuthorityVectorV1.Create(otherSession, []);
        var wrong = new EndpointParticipantPlanV1(f.Participant, TurnGenerationId.Create(), wrongAuthority,
            [new EndpointCapacityChargeV1(f.Cpu, 1)]);
        Assert.Equal("participant-transition-invalid", Assert.IsType<EndpointParticipantResultV1.Rejected>(
            EndpointParticipantSupervisorV1.Apply(effective,
                new EndpointParticipantCommandV1.PrepareReplacement(OperationId.Create(), 3, wrong), 16)).SafeCode.ToString());
        var sameGeneration = new EndpointParticipantPlanV1(f.Participant, f.Plan1.Generation, f.Authority,
            [new EndpointCapacityChargeV1(f.Cpu, 1)]);
        Assert.Equal("participant-transition-invalid", Assert.IsType<EndpointParticipantResultV1.Rejected>(
            EndpointParticipantSupervisorV1.Apply(effective,
                new EndpointParticipantCommandV1.PrepareReplacement(OperationId.Create(), 3, sameGeneration), 16)).SafeCode.ToString());
    }

    [Fact]
    public void Quarantine_is_terminal_for_effects_and_receipts_are_bounded()
    {
        var f = new Fixture();
        var state = f.Effective();
        var quarantined = Applied(state, new EndpointParticipantCommandV1.Quarantine(
            OperationId.Create(), 3, new BoundedAscii("provider-outcome-unknown")), maximumReceipts: 4);
        Assert.Equal(EndpointParticipantStateV1.Quarantined, quarantined.Snapshot.State);
        Assert.Equal("participant-transition-invalid", Assert.IsType<EndpointParticipantResultV1.Rejected>(
            EndpointParticipantSupervisorV1.Apply(quarantined,
                new EndpointParticipantCommandV1.Stop(OperationId.Create(), 4), 5)).SafeCode.ToString());
        Assert.Equal("participant-receipt-capacity-refused", Assert.IsType<EndpointParticipantResultV1.Rejected>(
            EndpointParticipantSupervisorV1.Apply(quarantined,
                new EndpointParticipantCommandV1.Quarantine(OperationId.Create(), 4, new BoundedAscii("again")), 4)).SafeCode.ToString());
    }

    private static EndpointParticipantSupervisorStateV1 Applied(EndpointParticipantSupervisorStateV1 state,
        EndpointParticipantCommandV1 command, ushort maximumReceipts = 16) =>
        Assert.IsType<EndpointParticipantResultV1.Applied>(EndpointParticipantSupervisorV1.Apply(state, command, maximumReceipts)).State;

    private sealed class Fixture
    {
        internal Fixture(long limit = 12)
        {
            Cpu = new BoundedAscii("endpoint-cpu");
            Participant = ParticipantId.Create();
            var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Authority = ExpectedAuthorityVectorV1.Create(session, []);
            Plan1 = new EndpointParticipantPlanV1(Participant, TurnGenerationId.Create(), Authority,
                [new EndpointCapacityChargeV1(Cpu, 4)]);
            Plan2 = new EndpointParticipantPlanV1(Participant, TurnGenerationId.Create(), Authority,
                [new EndpointCapacityChargeV1(Cpu, 6)]);
            State = EndpointParticipantSupervisorV1.Create(new EndpointCapacityLedgerV1(
                new Dictionary<BoundedAscii, long> { [Cpu] = limit }));
        }
        internal BoundedAscii Cpu { get; }
        internal ParticipantId Participant { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }
        internal EndpointParticipantPlanV1 Plan1 { get; }
        internal EndpointParticipantPlanV1 Plan2 { get; }
        internal EndpointParticipantSupervisorStateV1 State { get; }
        internal EndpointParticipantSupervisorStateV1 Effective()
        {
            var state = Applied(State, new EndpointParticipantCommandV1.Reserve(OperationId.Create(), 0, Plan1));
            state = Applied(state, new EndpointParticipantCommandV1.Prepared(OperationId.Create(), 1));
            return Applied(state, new EndpointParticipantCommandV1.Effective(OperationId.Create(), 2));
        }
    }
}
