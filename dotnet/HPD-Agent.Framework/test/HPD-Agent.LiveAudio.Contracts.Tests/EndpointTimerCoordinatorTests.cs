using HPD.Agent.Audio.Runtime.Endpointing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class EndpointTimerCoordinatorTests
{
    [Fact]
    public void Fire_cancel_race_has_exactly_one_sequenced_terminal_winner()
    {
        var f = new Fixture();
        var armed = Assert.IsType<EndpointTimerResult.Armed>(EndpointTimerCoordinator.Arm(
            EndpointTimerCoordinator.Create(), f.Arm(), 4, 8));
        var fired = Assert.IsType<EndpointTimerResult.Terminal>(EndpointTimerCoordinator.Fire(
            armed.State, f.ArmId, f.Authority, f.Candidate, 1, 1, 10, 11, 8));
        var lateCancel = Assert.IsType<EndpointTimerResult.Duplicate>(EndpointTimerCoordinator.Cancel(
            fired.State, f.ArmId, 12, 8));

        Assert.Equal(EndpointTimerTerminalDisposition.Fired, fired.Receipt.Disposition);
        Assert.Equal(fired.Receipt, lateCancel.Receipt);
        Assert.Empty(fired.State.Active);
        Assert.Single(fired.State.Terminal);
    }

    [Fact]
    public void Cancellation_can_win_and_late_wake_is_duplicate_not_authority()
    {
        var f = new Fixture();
        var armed = Assert.IsType<EndpointTimerResult.Armed>(EndpointTimerCoordinator.Arm(
            EndpointTimerCoordinator.Create(), f.Arm(), 4, 8));
        var cancelled = Assert.IsType<EndpointTimerResult.Terminal>(EndpointTimerCoordinator.Cancel(
            armed.State, f.ArmId, 9, 8));
        var lateFire = Assert.IsType<EndpointTimerResult.Duplicate>(EndpointTimerCoordinator.Fire(
            cancelled.State, f.ArmId, f.Authority, f.Candidate, 1, 1, 10, 11, 8));

        Assert.Equal(EndpointTimerTerminalDisposition.Cancelled, cancelled.Receipt.Disposition);
        Assert.Equal(cancelled.Receipt, lateFire.Receipt);
    }

    [Fact]
    public void Stale_authority_or_plan_terminalizes_wake_as_stale()
    {
        var f = new Fixture();
        var armed = Assert.IsType<EndpointTimerResult.Armed>(EndpointTimerCoordinator.Arm(
            EndpointTimerCoordinator.Create(), f.Arm(), 4, 8));
        var stale = Assert.IsType<EndpointTimerResult.Terminal>(EndpointTimerCoordinator.Fire(
            armed.State, f.ArmId, f.Authority, f.Candidate, 2, 1, 10, 11, 8));
        Assert.Equal(EndpointTimerTerminalDisposition.Stale, stale.Receipt.Disposition);
    }

    [Fact]
    public void Rearm_uses_new_identity_preserves_horizon_and_cannot_extend_or_exceed_bound()
    {
        var f = new Fixture();
        var first = f.Arm(maximumRearms: 1);
        var armed = Assert.IsType<EndpointTimerResult.Armed>(EndpointTimerCoordinator.Arm(
            EndpointTimerCoordinator.Create(), first, 4, 8));
        var nextId = TimerArmId.Create();
        var next = f.Arm(nextId, due: 15, horizon: 20, rearm: 1, maximumRearms: 1);
        var rearmed = Assert.IsType<EndpointTimerResult.Rearmed>(EndpointTimerCoordinator.Rearm(
            armed.State, f.ArmId, next, 11, 4, 8));

        Assert.Contains(nextId, rearmed.State.Active.Keys);
        Assert.Equal(EndpointTimerTerminalDisposition.Superseded, rearmed.State.Terminal[f.ArmId].Disposition);
        var third = f.Arm(TimerArmId.Create(), due: 16, horizon: 20, rearm: 1, maximumRearms: 1);
        Assert.Equal("timer-rearm-bound", Assert.IsType<EndpointTimerResult.Rejected>(
            EndpointTimerCoordinator.Rearm(rearmed.State, nextId, third, 12, 4, 8)).SafeCode.ToString());
        Assert.Throws<ArgumentException>(() => f.Arm(TimerArmId.Create(), due: 21, horizon: 20));
    }

    [Fact]
    public void Identity_capacity_early_fire_and_tombstone_bounds_fail_closed()
    {
        var f = new Fixture();
        var initial = EndpointTimerCoordinator.Create();
        var armed = Assert.IsType<EndpointTimerResult.Armed>(EndpointTimerCoordinator.Arm(initial, f.Arm(), 1, 1));
        Assert.IsType<EndpointTimerResult.Duplicate>(EndpointTimerCoordinator.Arm(armed.State, f.Arm(), 1, 1));
        Assert.Equal("timer-arm-capacity-refused", Assert.IsType<EndpointTimerResult.Rejected>(
            EndpointTimerCoordinator.Arm(armed.State, f.Arm(TimerArmId.Create()), 1, 1)).SafeCode.ToString());
        Assert.Equal("timer-fired-early", Assert.IsType<EndpointTimerResult.Rejected>(
            EndpointTimerCoordinator.Fire(armed.State, f.ArmId, f.Authority, f.Candidate, 1, 1, 9, 9, 1)).SafeCode.ToString());
        var terminal = Assert.IsType<EndpointTimerResult.Terminal>(EndpointTimerCoordinator.Cancel(armed.State, f.ArmId, 10, 1));
        var other = f.Arm(TimerArmId.Create());
        var otherArmed = Assert.IsType<EndpointTimerResult.Armed>(EndpointTimerCoordinator.Arm(terminal.State, other, 1, 1));
        Assert.Equal("timer-tombstone-capacity-refused", Assert.IsType<EndpointTimerResult.Rejected>(
            EndpointTimerCoordinator.Cancel(otherArmed.State, other.ArmId, 11, 1)).SafeCode.ToString());
    }

    private sealed class Fixture
    {
        internal Fixture()
        {
            ArmId = TimerArmId.Create();
            Candidate = EndpointCandidateId.Create();
            Family = CandidateFamilyId.Create();
            var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Authority = ExpectedAuthorityVectorV1.Create(session, []);
        }
        internal TimerArmId ArmId { get; }
        internal EndpointCandidateId Candidate { get; }
        internal CandidateFamilyId Family { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }
        internal EndpointTimerArm Arm(TimerArmId? id = null, ulong due = 10, ulong horizon = 20,
            ushort rearm = 0, ushort maximumRearms = 2) => new(id ?? ArmId,
                EndpointTimerKind.SemanticEot, Authority, Family, Candidate, 1, 1,
                due, horizon, rearm, maximumRearms);
    }
}
