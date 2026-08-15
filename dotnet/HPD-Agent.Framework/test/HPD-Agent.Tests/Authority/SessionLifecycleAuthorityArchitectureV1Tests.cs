using HPD.Agent.Audio;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SessionLifecycleAuthorityArchitectureV1Tests
{
    [Fact]
    public void AudioProjectionEnums_ExactlyMatchCoreWireEnums()
    {
        Equal<SessionLifecycleStateWireV1, LiveAudioSessionStateV1>();
        Equal<SessionAdmissionWireV1, LiveAudioAdmissionStateV1>();
        Equal<SessionAvailabilityWireV1, LiveAudioAvailabilityV1>();
        Equal<SessionReadinessWireV1, LiveAudioReadinessV1>();
        Equal<SessionTerminalIntentWireV1, LiveAudioTerminalIntentV1>();
        Equal<SessionTerminalCauseWireV1, LiveAudioTerminalCauseV1>();
        Equal<SessionTerminalSeverityWireV1, LiveAudioTerminalSeverityV1>();
        Equal<SessionConvergencePhaseWireV1, LiveAudioConvergencePhaseV1>();
        Equal<SessionMutationFenceWireV1, LiveAudioMutationFenceV1>();
    }

    [Fact]
    public void AudioAssembly_DoesNotContainACompetingLifecycleReducerOrCommandUnion()
    {
        var assembly = typeof(LiveAudioLifecycleSnapshotV1).Assembly;

        Assert.Null(assembly.GetType("HPD.Agent.Audio.LiveAudioSessionStateMachineV1"));
        Assert.Null(assembly.GetType("HPD.Agent.Audio.LiveAudioLifecycleCommandV1"));
        Assert.Null(assembly.GetType("HPD.Agent.Audio.LiveAudioLifecycleTransitionV1"));
    }

    private static void Equal<TCore, TAudio>()
        where TCore : struct, Enum
        where TAudio : struct, Enum
    {
        var core = Enum.GetValues<TCore>()
            .Select(static value => (Name: value.ToString(), Value: Convert.ToUInt16(value)))
            .ToArray();
        var audio = Enum.GetValues<TAudio>()
            .Select(static value => (Name: value.ToString(), Value: Convert.ToUInt16(value)))
            .ToArray();

        Assert.Equal(core, audio);
    }
}
