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
    /// Normalized HPD speech recognition events.
    /// </summary>
    public static class Recognition
    {
        /// <summary>Speech recognition started event type.</summary>
        public const string SPEECH_RECOGNITION_STARTED = "SPEECH_RECOGNITION_STARTED";

        /// <summary>Speech recognition interim transcript event type.</summary>
        public const string SPEECH_RECOGNITION_INTERIM = "SPEECH_RECOGNITION_INTERIM";

        /// <summary>Speech recognition preflight transcript event type.</summary>
        public const string SPEECH_RECOGNITION_PREFLIGHT = "SPEECH_RECOGNITION_PREFLIGHT";

        /// <summary>Speech recognition final transcript event type.</summary>
        public const string SPEECH_RECOGNITION_FINAL = "SPEECH_RECOGNITION_FINAL";

        /// <summary>Speech recognition usage event type.</summary>
        public const string SPEECH_RECOGNITION_USAGE = "SPEECH_RECOGNITION_USAGE";

        /// <summary>Speech recognition ended event type.</summary>
        public const string SPEECH_RECOGNITION_ENDED = "SPEECH_RECOGNITION_ENDED";

        /// <summary>Speech recognition error event type.</summary>
        public const string SPEECH_RECOGNITION_ERROR = "SPEECH_RECOGNITION_ERROR";
    }

    /// <summary>
    /// Normalized HPD speech output events.
    /// </summary>
    public static class Output
    {
        /// <summary>Speech output started event type.</summary>
        public const string SPEECH_OUTPUT_STARTED = "SPEECH_OUTPUT_STARTED";

        /// <summary>Speech output text queued event type.</summary>
        public const string SPEECH_OUTPUT_TEXT_QUEUED = "SPEECH_OUTPUT_TEXT_QUEUED";

        /// <summary>Speech output audio queued event type.</summary>
        public const string SPEECH_OUTPUT_AUDIO_QUEUED = "SPEECH_OUTPUT_AUDIO_QUEUED";

        /// <summary>Speech output playback started event type.</summary>
        public const string SPEECH_OUTPUT_PLAYBACK_STARTED = "SPEECH_OUTPUT_PLAYBACK_STARTED";

        /// <summary>Speech output playback progress event type.</summary>
        public const string SPEECH_OUTPUT_PLAYBACK_PROGRESS = "SPEECH_OUTPUT_PLAYBACK_PROGRESS";

        /// <summary>Speech output playback finished event type.</summary>
        public const string SPEECH_OUTPUT_PLAYBACK_FINISHED = "SPEECH_OUTPUT_PLAYBACK_FINISHED";

        /// <summary>Speech output paused event type.</summary>
        public const string SPEECH_OUTPUT_PAUSED = "SPEECH_OUTPUT_PAUSED";

        /// <summary>Speech output resumed event type.</summary>
        public const string SPEECH_OUTPUT_RESUMED = "SPEECH_OUTPUT_RESUMED";

        /// <summary>Speech output interrupted event type.</summary>
        public const string SPEECH_OUTPUT_INTERRUPTED = "SPEECH_OUTPUT_INTERRUPTED";

        /// <summary>Speech output completed event type.</summary>
        public const string SPEECH_OUTPUT_COMPLETED = "SPEECH_OUTPUT_COMPLETED";

        /// <summary>Speech output error event type.</summary>
        public const string SPEECH_OUTPUT_ERROR = "SPEECH_OUTPUT_ERROR";
    }

    /// <summary>
    /// User turn control events.
    /// </summary>
    public static class Turn
    {
        /// <summary>User turn started event type.</summary>
        public const string USER_TURN_STARTED = "USER_TURN_STARTED";

        /// <summary>User turn transcript update event type.</summary>
        public const string USER_TURN_UPDATED = "USER_TURN_UPDATED";

        /// <summary>User turn ready event type.</summary>
        public const string USER_TURN_READY = "USER_TURN_READY";

        /// <summary>User turn committed event type.</summary>
        public const string USER_TURN_COMMITTED = "USER_TURN_COMMITTED";

        /// <summary>User turn cancelled event type.</summary>
        public const string USER_TURN_CANCELLED = "USER_TURN_CANCELLED";
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
        public const string AUDIO_EXPERIENCE_METRIC = "AUDIO_EXPERIENCE_METRIC";
    }

    /// <summary>
    /// End-of-turn detection events.
    /// </summary>
    public static class Eot
    {
        public const string EOT_DETECTED = "EOT_DETECTED";
    }

    /// <summary>
    /// Filler audio events.
    /// </summary>
    public static class Filler
    {
        public const string FILLER_AUDIO_PLAYED = "FILLER_AUDIO_PLAYED";
    }
}
