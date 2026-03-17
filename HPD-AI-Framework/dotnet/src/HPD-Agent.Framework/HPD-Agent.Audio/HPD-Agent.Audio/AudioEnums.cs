// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio;

/// <summary>
/// How audio is processed internally.
/// </summary>
public enum AudioProcessingMode
{
    /// <summary>
    /// Orchestrated pipeline: STT → LLM → TTS as separate components.
    /// Allows mixing providers (e.g., Whisper STT + Claude + ElevenLabs TTS).
    /// </summary>
    Pipeline,

    /// <summary>
    /// Single model handles audio I/O directly.
    /// Used with GPT-4o Realtime, Gemini Live, etc.
    /// </summary>
    Native
}

/// <summary>
/// What input/output modalities to use.
/// </summary>
public enum AudioIOMode
{
    /// <summary>Audio in → Text out. Voice input, text response.</summary>
    AudioToText,

    /// <summary>Text in → Audio out. Text input, voice response.</summary>
    TextToAudio,

    /// <summary>Audio in → Audio out. Full voice conversation.</summary>
    AudioToAudio,

    /// <summary>Audio in → Audio + Text out. Voice with captions/transcripts.</summary>
    AudioToAudioAndText,

    /// <summary>Text in → Audio + Text out. Text input, voice + text response.</summary>
    TextToAudioAndText
}

/// <summary>
/// Turn detection strategy (like PIIMiddleware per-type strategies).
/// </summary>
public enum TurnDetectionStrategy
{
    /// <summary>Don't use this detection method.</summary>
    Disabled,

    /// <summary>Use as fast-path rejection (silence threshold).</summary>
    FastPath,

    /// <summary>Use only when other signals are ambiguous.</summary>
    OnAmbiguous,

    /// <summary>Always run this detection method.</summary>
    Always,

    /// <summary>User controls turn boundaries via CommitUserTurn(). ( )</summary>
    Manual
}

/// <summary>
/// How to handle backchannel utterances during bot speech.
/// </summary>
public enum BackchannelStrategy
{
    /// <summary>Any speech interrupts immediately.</summary>
    InterruptImmediately,

    /// <summary>Wait for MinWordsForInterruption before interrupting.</summary>
    IgnoreShortUtterances,

    /// <summary>Ignore known backchannels ("uh-huh", "yeah", etc.).</summary>
    IgnoreKnownBackchannels
}

/// <summary>
/// Strategy for selecting filler audio phrases.
/// </summary>
public enum FillerStrategy
{
    /// <summary>Random selection from available phrases.</summary>
    Random,

    /// <summary>Round-robin through phrases in order.</summary>
    RoundRobin,

    /// <summary>Select based on duration (shortest first for quick responses).</summary>
    ShortestFirst
}
