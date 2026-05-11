// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Turn;

/// <summary>
/// Stable endpointing reason constants used by tests, metrics, and replay.
/// </summary>
public static class EndpointingReason
{
    /// <summary>VAD ended and the minimum endpointing delay applies.</summary>
    public const string VadEndMinDelay = "vad_end_min_delay";

    /// <summary>EOT detector returned high confidence.</summary>
    public const string EotHighConfidence = "eot_high_confidence";

    /// <summary>EOT detector indicated the turn is unlikely to be complete.</summary>
    public const string EotUnlikelyMaxDelay = "eot_unlikely_max_delay";

    /// <summary>STT final transcript drove endpointing.</summary>
    public const string SttFinalMinDelay = "stt_final_min_delay";

    /// <summary>Final transcript arrived while speech was not active.</summary>
    public const string FinalTranscriptNoSpeech = "final_transcript_no_speech";

    /// <summary>Native realtime model committed the turn.</summary>
    public const string RealtimeModelCommit = "realtime_model_commit";

    /// <summary>User manually committed the turn.</summary>
    public const string ManualCommit = "manual_commit";

    /// <summary>User manually cancelled the turn.</summary>
    public const string ManualCancel = "manual_cancel";

    /// <summary>Recognizer reported an error.</summary>
    public const string RecognitionError = "recognition_error";
}
