// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Stt;
using HPD.Agent.Audio.Tts;
using HPD.Agent.Audio.Vad;

namespace HPD.Agent.Audio;

/// <summary>
/// Complete audio configuration for HPD-Agent.
/// Organizes settings by role (TTS, STT, VAD) to prevent dimensional explosion.
/// </summary>
public class AudioConfig
{
    //
    // ROLE-BASED CONFIGURATION
    //

    /// <summary>
    /// TTS (Text-to-Speech) configuration.
    /// Null if TTS is not needed for this agent.
    /// </summary>
    public TtsConfig? Tts { get; set; }

    /// <summary>
    /// STT (Speech-to-Text) configuration.
    /// Null if STT is not needed for this agent.
    /// </summary>
    public SttConfig? Stt { get; set; }

    /// <summary>
    /// VAD (Voice Activity Detection) configuration.
    /// Null if VAD is not needed for this agent.
    /// </summary>
    public VadConfig? Vad { get; set; }

    //
    // PROCESSING MODE & I/O
    //

    /// <summary>
    /// Audio processing mode: Pipeline (STT→LLM→TTS) or Native (GPT-4o Realtime, Gemini Live).
    /// Default: Pipeline
    /// </summary>
    public AudioProcessingMode ProcessingMode { get; set; } = AudioProcessingMode.Pipeline;

    /// <summary>
    /// I/O modality: AudioToText, TextToAudio, AudioToAudio, AudioToAudioAndText, TextToAudioAndText.
    /// Default: AudioToAudioAndText (full voice with captions)
    /// </summary>
    public AudioIOMode IOMode { get; set; } = AudioIOMode.AudioToAudioAndText;

    /// <summary>
    /// Global language override (ISO 639-1).
    /// If set, overrides language settings in Tts.Language and Stt.Language.
    /// Example: "en", "es", "fr"
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Disable all audio processing for this request. Default: false.
    /// </summary>
    public bool? Disabled { get; set; }

    //
    // TURN DETECTION (Pipeline-Level)
    //

    /// <summary>Turn detection strategy for silence. Default: FastPath.</summary>
    public TurnDetectionStrategy? SilenceStrategy { get; set; } = TurnDetectionStrategy.FastPath;

    /// <summary>Turn detection strategy for ML. Default: OnAmbiguous.</summary>
    public TurnDetectionStrategy? MlStrategy { get; set; } = TurnDetectionStrategy.OnAmbiguous;

    /// <summary>Silence threshold for fast-path rejection. Default: 1.5s.</summary>
    public float? SilenceFastPathThreshold { get; set; } = 1.5f;

    /// <summary>Min endpointing delay when ML is confident. Default: 0.3s.</summary>
    public float? MinEndpointingDelay { get; set; } = 0.3f;

    /// <summary>Max endpointing delay when ML is uncertain. Default: 1.5s.</summary>
    public float? MaxEndpointingDelay { get; set; } = 1.5f;

    /// <summary>Multiplier for silence-based probability boost. Default: 0.7.</summary>
    public float? SilenceBoostMultiplier { get; set; } = 0.7f;

    /// <summary>Whether to combine ML and silence probabilities. Default: true.</summary>
    public bool? UseCombinedProbability { get; set; } = true;

    /// <summary>Custom trailing words that indicate incomplete thoughts. Default: null (uses built-in list).</summary>
    public HashSet<string>? CustomTrailingWords { get; set; }

    /// <summary>Probability penalty for trailing incomplete words. Default: 0.6.</summary>
    public float? TrailingWordPenalty { get; set; } = 0.6f;

    //
    // PIPELINE FEATURES
    //

    /// <summary>Enable TTS on first complete sentence. Default: true.</summary>
    public bool? EnableQuickAnswer { get; set; } = true;

    /// <summary>Enable adaptive endpointing based on user speaking speed. Default: true.</summary>
    public bool? EnableSpeedAdaptation { get; set; } = true;

    /// <summary>Enable preemptive generation. Default: false.</summary>
    public bool? EnablePreemptiveGeneration { get; set; } = false;

    /// <summary>Confidence threshold for preemptive generation. Default: 0.7.</summary>
    public float? PreemptiveGenerationThreshold { get; set; } = 0.7f;

    //
    // INTERRUPTION HANDLING
    //

    /// <summary>How to handle short utterances during bot speech. Default: IgnoreShortUtterances.</summary>
    public BackchannelStrategy? BackchannelStrategy { get; set; } = Audio.BackchannelStrategy.IgnoreShortUtterances;

    /// <summary>Minimum words required to trigger interruption. Default: 2.</summary>
    public int? MinWordsForInterruption { get; set; } = 2;

    /// <summary>Enable false interruption recovery (pause before full interrupt). Default: true.</summary>
    public bool? EnableFalseInterruptionRecovery { get; set; } = true;

    /// <summary>Timeout before resuming paused speech (seconds). Default: 2.0.</summary>
    public float? FalseInterruptionTimeout { get; set; } = 2.0f;

    /// <summary>Whether to resume synthesis after timeout. Default: true.</summary>
    public bool? ResumeFalseInterruption { get; set; } = true;

    /// <summary>Max audio chunks to buffer during pause. Default: 100.</summary>
    public int? MaxBufferedChunksDuringPause { get; set; } = 100;

    //
    // FILLER AUDIO
    //

    /// <summary>Enable filler audio during LLM thinking. Default: false.</summary>
    public bool? EnableFillerAudio { get; set; } = false;

    /// <summary>Silence duration before playing filler. Default: 1.5s.</summary>
    public float? FillerSilenceThreshold { get; set; } = 1.5f;

    /// <summary>Filler phrases to pre-cache. Default: ["Um...", "Let me see...", "One moment..."].</summary>
    public string[]? FillerPhrases { get; set; } = ["Um...", "Let me see...", "One moment..."];

    /// <summary>Filler selection strategy. Default: Random.</summary>
    public FillerStrategy? FillerSelectionStrategy { get; set; } = FillerStrategy.Random;

    /// <summary>Max filler plays per turn. Default: 1.</summary>
    public int? MaxFillerPlaysPerTurn { get; set; } = 1;

    /// <summary>Voice to use for filler audio. Default: null (uses default TTS voice).</summary>
    public string? FillerVoice { get; set; }

    /// <summary>Speed multiplier for filler audio. Default: 0.95.</summary>
    public float? FillerSpeed { get; set; } = 0.95f;

    //
    // TEXT FILTERING
    //

    /// <summary>Enable text filtering for TTS. Default: true.</summary>
    public bool? EnableTextFiltering { get; set; } = true;

    /// <summary>Filter code blocks. Default: true.</summary>
    public bool? FilterCodeBlocks { get; set; } = true;

    /// <summary>Filter tables. Default: true.</summary>
    public bool? FilterTables { get; set; } = true;

    /// <summary>Filter URLs. Default: true.</summary>
    public bool? FilterUrls { get; set; } = true;

    /// <summary>Filter markdown formatting. Default: true.</summary>
    public bool? FilterMarkdownFormatting { get; set; } = true;

    /// <summary>Filter emoji. Default: true.</summary>
    public bool? FilterEmoji { get; set; } = true;

    //
    // HELPER METHODS
    //

    /// <summary>
    /// Merges per-request overrides with middleware defaults.
    /// Per-request values take precedence.
    /// </summary>
    public AudioConfig MergeWith(AudioConfig? overrides)
    {
        if (overrides == null) return this;

        return new AudioConfig
        {
            // Role configs (merge deeply)
            Tts = overrides.Tts ?? Tts,
            Stt = overrides.Stt ?? Stt,
            Vad = overrides.Vad ?? Vad,

            // Processing
            ProcessingMode = overrides.ProcessingMode,
            IOMode = overrides.IOMode,
            Language = overrides.Language ?? Language,
            Disabled = overrides.Disabled ?? Disabled,

            // Turn Detection
            SilenceStrategy = overrides.SilenceStrategy ?? SilenceStrategy,
            MlStrategy = overrides.MlStrategy ?? MlStrategy,
            SilenceFastPathThreshold = overrides.SilenceFastPathThreshold ?? SilenceFastPathThreshold,
            MinEndpointingDelay = overrides.MinEndpointingDelay ?? MinEndpointingDelay,
            MaxEndpointingDelay = overrides.MaxEndpointingDelay ?? MaxEndpointingDelay,
            SilenceBoostMultiplier = overrides.SilenceBoostMultiplier ?? SilenceBoostMultiplier,
            UseCombinedProbability = overrides.UseCombinedProbability ?? UseCombinedProbability,
            CustomTrailingWords = overrides.CustomTrailingWords ?? CustomTrailingWords,
            TrailingWordPenalty = overrides.TrailingWordPenalty ?? TrailingWordPenalty,

            // Features
            EnableQuickAnswer = overrides.EnableQuickAnswer ?? EnableQuickAnswer,
            EnableSpeedAdaptation = overrides.EnableSpeedAdaptation ?? EnableSpeedAdaptation,
            EnablePreemptiveGeneration = overrides.EnablePreemptiveGeneration ?? EnablePreemptiveGeneration,
            PreemptiveGenerationThreshold = overrides.PreemptiveGenerationThreshold ?? PreemptiveGenerationThreshold,

            // Interruption
            BackchannelStrategy = overrides.BackchannelStrategy ?? BackchannelStrategy,
            MinWordsForInterruption = overrides.MinWordsForInterruption ?? MinWordsForInterruption,
            EnableFalseInterruptionRecovery = overrides.EnableFalseInterruptionRecovery ?? EnableFalseInterruptionRecovery,
            FalseInterruptionTimeout = overrides.FalseInterruptionTimeout ?? FalseInterruptionTimeout,
            ResumeFalseInterruption = overrides.ResumeFalseInterruption ?? ResumeFalseInterruption,
            MaxBufferedChunksDuringPause = overrides.MaxBufferedChunksDuringPause ?? MaxBufferedChunksDuringPause,

            // Filler
            EnableFillerAudio = overrides.EnableFillerAudio ?? EnableFillerAudio,
            FillerSilenceThreshold = overrides.FillerSilenceThreshold ?? FillerSilenceThreshold,
            FillerPhrases = overrides.FillerPhrases ?? FillerPhrases,
            FillerSelectionStrategy = overrides.FillerSelectionStrategy ?? FillerSelectionStrategy,
            MaxFillerPlaysPerTurn = overrides.MaxFillerPlaysPerTurn ?? MaxFillerPlaysPerTurn,
            FillerVoice = overrides.FillerVoice ?? FillerVoice,
            FillerSpeed = overrides.FillerSpeed ?? FillerSpeed,

            // Text Filtering
            EnableTextFiltering = overrides.EnableTextFiltering ?? EnableTextFiltering,
            FilterCodeBlocks = overrides.FilterCodeBlocks ?? FilterCodeBlocks,
            FilterTables = overrides.FilterTables ?? FilterTables,
            FilterUrls = overrides.FilterUrls ?? FilterUrls,
            FilterMarkdownFormatting = overrides.FilterMarkdownFormatting ?? FilterMarkdownFormatting,
            FilterEmoji = overrides.FilterEmoji ?? FilterEmoji
        };
    }

    /// <summary>
    /// Validates configuration values.
    /// </summary>
    public void Validate()
    {
        // Native mode: the model handles audio I/O directly — STT/TTS/VAD are not used.
        // Setting them alongside Native mode is a configuration error.
        if (ProcessingMode == AudioProcessingMode.Native)
        {
            if (Stt != null)
                throw new InvalidOperationException(
                    "AudioConfig.Stt cannot be set when ProcessingMode is Native. " +
                    "In Native mode the model handles speech-to-text directly; " +
                    "remove the Stt configuration or switch to ProcessingMode.Pipeline.");

            if (Tts != null)
                throw new InvalidOperationException(
                    "AudioConfig.Tts cannot be set when ProcessingMode is Native. " +
                    "In Native mode the model produces audio output directly; " +
                    "remove the Tts configuration or switch to ProcessingMode.Pipeline.");

            if (Vad != null)
                throw new InvalidOperationException(
                    "AudioConfig.Vad cannot be set when ProcessingMode is Native. " +
                    "In Native mode turn detection is handled by the model itself; " +
                    "remove the Vad configuration or switch to ProcessingMode.Pipeline.");

            return;
        }

        // Validate role configs
        Tts?.Validate();
        Stt?.Validate();
        Vad?.Validate();

        // Validate turn detection
        if (SilenceFastPathThreshold is < 0)
            throw new ArgumentException("SilenceFastPathThreshold must be non-negative");

        if (MinEndpointingDelay is < 0)
            throw new ArgumentException("MinEndpointingDelay must be non-negative");

        if (MaxEndpointingDelay is < 0)
            throw new ArgumentException("MaxEndpointingDelay must be non-negative");

        // Validate interruption
        if (MinWordsForInterruption is < 0)
            throw new ArgumentException("MinWordsForInterruption must be non-negative");

        if (FalseInterruptionTimeout is < 0)
            throw new ArgumentException("FalseInterruptionTimeout must be non-negative");

        // Validate filler
        if (FillerSilenceThreshold is < 0)
            throw new ArgumentException("FillerSilenceThreshold must be non-negative");

        if (MaxFillerPlaysPerTurn is < 0)
            throw new ArgumentException("MaxFillerPlaysPerTurn must be non-negative");

        if (FillerSpeed is < 0.25f or > 4.0f)
            throw new ArgumentException("FillerSpeed must be between 0.25 and 4.0");

        // Validate preemptive generation
        if (PreemptiveGenerationThreshold is < 0 or > 1.0f)
            throw new ArgumentException("PreemptiveGenerationThreshold must be between 0 and 1.0");
    }
}
