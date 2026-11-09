using HPD.Agent.Audio;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SessionLifecycleAuthorityArchitectureV1Tests
{
    [Fact]
    public void AudioProjectionEnums_ExactlyMatchCoreWireEnums()
    {
        Equal<SessionLifecycleStateWireV1, LiveAudioSessionState>();
        Equal<SessionAdmissionWireV1, LiveAudioAdmissionState>();
        Equal<SessionAvailabilityWireV1, LiveAudioAvailability>();
        Equal<SessionReadinessWireV1, LiveAudioReadiness>();
        Equal<SessionTerminalIntentWireV1, LiveAudioTerminalIntent>();
        Equal<SessionTerminalCauseWireV1, LiveAudioTerminalCause>();
        Equal<SessionTerminalSeverityWireV1, LiveAudioTerminalSeverity>();
        Equal<SessionConvergencePhaseWireV1, LiveAudioConvergencePhase>();
        Equal<SessionMutationFenceWireV1, LiveAudioMutationFence>();
    }

    [Fact]
    public void AudioAssembly_DoesNotContainACompetingLifecycleReducerOrCommandUnion()
    {
        var assembly = typeof(LiveAudioLifecycleSnapshot).Assembly;

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
