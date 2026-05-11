// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Interruption;

/// <summary>
/// Stable interruption reason constants used by tests, metrics, and replay.
/// </summary>
public static class InterruptionReason
{
    /// <summary>User speech started while agent output was playing.</summary>
    public const string UserSpeechDuringPlayback = "user_speech_during_playback";

    /// <summary>Transcript contains enough meaningful speech to interrupt.</summary>
    public const string MeaningfulSpeech = "meaningful_speech";

    /// <summary>Transcript was too short to count as an interruption.</summary>
    public const string ShortBackchannel = "short_backchannel";

    /// <summary>Transcript matched a known backchannel phrase.</summary>
    public const string KnownBackchannel = "known_backchannel";

    /// <summary>No transcript arrived before the recovery timeout.</summary>
    public const string FalseInterruptionTimeout = "false_interruption_timeout";

    /// <summary>There is no active speech output to interrupt.</summary>
    public const string NoActiveSpeech = "no_active_speech";

    /// <summary>Speech output completed before interruption could be confirmed.</summary>
    public const string OutputCompleted = "output_completed";
}
