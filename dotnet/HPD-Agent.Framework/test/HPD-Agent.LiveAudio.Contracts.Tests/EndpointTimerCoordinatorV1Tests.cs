using HPD.Agent.Audio.Runtime.Endpointing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class EndpointTimerCoordinatorV1Tests
{
    [Fact]
    public void Fire_cancel_race_has_exactly_one_sequenced_terminal_winner()
    {
        var f = new Fixture();
        var armed = Assert.IsType<EndpointTimerResultV1.Armed>(EndpointTimerCoordinatorV1.Arm(
            EndpointTimerCoordinatorV1.Create(), f.Arm(), 4, 8));
        var fired = Assert.IsType<EndpointTimerResultV1.Terminal>(EndpointTimerCoordinatorV1.Fire(
            armed.State, f.ArmId, f.Authority, f.Candidate, 1, 1, 10, 11, 8));
        var lateCancel = Assert.IsType<EndpointTimerResultV1.Duplicate>(EndpointTimerCoordinatorV1.Cancel(
            fired.State, f.ArmId, 12, 8));

        Assert.Equal(EndpointTimerTerminalDispositionV1.Fired, fired.Receipt.Disposition);
        Assert.Equal(fired.Receipt, lateCancel.Receipt);
        Assert.Empty(fired.State.Active);
        Assert.Single(fired.State.Terminal);
    }

    [Fact]
    public void Cancellation_can_win_and_late_wake_is_duplicate_not_authority()
    {
        var f = new Fixture();
        var armed = Assert.IsType<EndpointTimerResultV1.Armed>(EndpointTimerCoordinatorV1.Arm(
            EndpointTimerCoordinatorV1.Create(), f.Arm(), 4, 8));
        var cancelled = Assert.IsType<EndpointTimerResultV1.Terminal>(EndpointTimerCoordinatorV1.Cancel(
            armed.State, f.ArmId, 9, 8));
        var lateFire = Assert.IsType<EndpointTimerResultV1.Duplicate>(EndpointTimerCoordinatorV1.Fire(
            cancelled.State, f.ArmId, f.Authority, f.Candidate, 1, 1, 10, 11, 8));

        Assert.Equal(EndpointTimerTerminalDispositionV1.Cancelled, cancelled.Receipt.Disposition);
        Assert.Equal(cancelled.Receipt, lateFire.Receipt);
    }

    [Fact]
    public void Stale_authority_or_plan_terminalizes_wake_as_stale()
    {
        var f = new Fixture();
        var armed = Assert.IsType<EndpointTimerResultV1.Armed>(EndpointTimerCoordinatorV1.Arm(
            EndpointTimerCoordinatorV1.Create(), f.Arm(), 4, 8));
        var stale = Assert.IsType<EndpointTimerResultV1.Terminal>(EndpointTimerCoordinatorV1.Fire(
            armed.State, f.ArmId, f.Authority, f.Candidate, 2, 1, 10, 11, 8));
        Assert.Equal(EndpointTimerTerminalDispositionV1.Stale, stale.Receipt.Disposition);
    }

    [Fact]
    public void Rearm_uses_new_identity_preserves_horizon_and_cannot_extend_or_exceed_bound()
    {
        var f = new Fixture();
        var first = f.Arm(maximumRearms: 1);
        var armed = Assert.IsType<EndpointTimerResultV1.Armed>(EndpointTimerCoordinatorV1.Arm(
            EndpointTimerCoordinatorV1.Create(), first, 4, 8));
        var nextId = TimerArmIdV1.Create();
        var next = f.Arm(nextId, due: 15, horizon: 20, rearm: 1, maximumRearms: 1);
        var rearmed = Assert.IsType<EndpointTimerResultV1.Rearmed>(EndpointTimerCoordinatorV1.Rearm(
            armed.State, f.ArmId, next, 11, 4, 8));

        Assert.Contains(nextId, rearmed.State.Active.Keys);
        Assert.Equal(EndpointTimerTerminalDispositionV1.Superseded, rearmed.State.Terminal[f.ArmId].Disposition);
        var third = f.Arm(TimerArmIdV1.Create(), due: 16, horizon: 20, rearm: 1, maximumRearms: 1);
        Assert.Equal("timer-rearm-bound", Assert.IsType<EndpointTimerResultV1.Rejected>(
            EndpointTimerCoordinatorV1.Rearm(rearmed.State, nextId, third, 12, 4, 8)).SafeCode.ToString());
        Assert.Throws<ArgumentException>(() => f.Arm(TimerArmIdV1.Create(), due: 21, horizon: 20));
    }

    [Fact]
    public void Identity_capacity_early_fire_and_tombstone_bounds_fail_closed()
    {
        var f = new Fixture();
        var initial = EndpointTimerCoordinatorV1.Create();
        var armed = Assert.IsType<EndpointTimerResultV1.Armed>(EndpointTimerCoordinatorV1.Arm(initial, f.Arm(), 1, 1));
        Assert.IsType<EndpointTimerResultV1.Duplicate>(EndpointTimerCoordinatorV1.Arm(armed.State, f.Arm(), 1, 1));
        Assert.Equal("timer-arm-capacity-refused", Assert.IsType<EndpointTimerResultV1.Rejected>(
            EndpointTimerCoordinatorV1.Arm(armed.State, f.Arm(TimerArmIdV1.Create()), 1, 1)).SafeCode.ToString());
        Assert.Equal("timer-fired-early", Assert.IsType<EndpointTimerResultV1.Rejected>(
            EndpointTimerCoordinatorV1.Fire(armed.State, f.ArmId, f.Authority, f.Candidate, 1, 1, 9, 9, 1)).SafeCode.ToString());
        var terminal = Assert.IsType<EndpointTimerResultV1.Terminal>(EndpointTimerCoordinatorV1.Cancel(armed.State, f.ArmId, 10, 1));
        var other = f.Arm(TimerArmIdV1.Create());
        var otherArmed = Assert.IsType<EndpointTimerResultV1.Armed>(EndpointTimerCoordinatorV1.Arm(terminal.State, other, 1, 1));
        Assert.Equal("timer-tombstone-capacity-refused", Assert.IsType<EndpointTimerResultV1.Rejected>(
            EndpointTimerCoordinatorV1.Cancel(otherArmed.State, other.ArmId, 11, 1)).SafeCode.ToString());
    }

    private sealed class Fixture
    {
        internal Fixture()
        {
            ArmId = TimerArmIdV1.Create();
            Candidate = EndpointCandidateIdV1.Create();
            Family = CandidateFamilyIdV1.Create();
            var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Authority = ExpectedAuthorityVectorV1.Create(session, []);
        }
        internal TimerArmIdV1 ArmId { get; }
        internal EndpointCandidateIdV1 Candidate { get; }
        internal CandidateFamilyIdV1 Family { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }
        internal EndpointTimerArmV1 Arm(TimerArmIdV1? id = null, ulong due = 10, ulong horizon = 20,
            ushort rearm = 0, ushort maximumRearms = 2) => new(id ?? ArmId,
                EndpointTimerKindV1.SemanticEot, Authority, Family, Candidate, 1, 1,
                due, horizon, rearm, maximumRearms);
    }
}
