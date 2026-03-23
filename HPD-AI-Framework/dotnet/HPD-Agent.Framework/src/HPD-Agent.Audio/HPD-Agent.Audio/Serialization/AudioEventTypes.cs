// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Serialization;

/// <summary>
/// Constants for audio event type discriminators.
/// Uses SCREAMING_SNAKE_CASE convention for JSON API compatibility.
/// </summary>
public static class AudioEventTypes
{
    /// <summary>
    /// TTS synthesis events.
    /// </summary>
    public static class Synthesis
    {
        public const string SYNTHESIS_STARTED = "SYNTHESIS_STARTED";
        public const string AUDIO_CHUNK = "AUDIO_CHUNK";
        public const string SYNTHESIS_COMPLETED = "SYNTHESIS_COMPLETED";
    }

    /// <summary>
    /// STT transcription events.
    /// </summary>
    public static class Transcription
    {
        public const string TRANSCRIPTION_DELTA = "TRANSCRIPTION_DELTA";
        public const string TRANSCRIPTION_COMPLETED = "TRANSCRIPTION_COMPLETED";
    }

    /// <summary>
    /// User interruption events.
    /// </summary>
    public static class Interruption
    {
        public const string USER_INTERRUPTED = "USER_INTERRUPTED";
        public const string SPEECH_PAUSED = "SPEECH_PAUSED";
        public const string SPEECH_RESUMED = "SPEECH_RESUMED";
    }

    /// <summary>
    /// Preemptive generation events ( ).
    /// </summary>
    public static class PreemptiveGeneration
    {
        public const string PREEMPTIVE_GENERATION_STARTED = "PREEMPTIVE_GENERATION_STARTED";
        public const string PREEMPTIVE_GENERATION_DISCARDED = "PREEMPTIVE_GENERATION_DISCARDED";
    }

    /// <summary>
    /// Voice activity detection events.
    /// </summary>
    public static class Vad
    {
        public const string VAD_START_OF_SPEECH = "VAD_START_OF_SPEECH";
        public const string VAD_END_OF_SPEECH = "VAD_END_OF_SPEECH";
    }

    /// <summary>
    /// Audio pipeline metrics events.
    /// </summary>
    public static class Metrics
    {
        public const string AUDIO_PIPELINE_METRICS = "AUDIO_PIPELINE_METRICS";
    }

    /// <summary>
    /// Turn detection events.
    /// </summary>
    public static class TurnDetection
    {
        public const string TURN_DETECTED = "TURN_DETECTED";
    }

    /// <summary>
    /// Filler audio events.
    /// </summary>
    public static class Filler
    {
        public const string FILLER_AUDIO_PLAYED = "FILLER_AUDIO_PLAYED";
    }
}
