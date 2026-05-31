// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Stt;
using HPD.Agent.Audio.Tts;
using HPD.Agent.Audio.Vad;
using HPD.Agent.Audio.Eot;
using HPD.Agent.Audio.Realtime;

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

    /// <summary>
    /// EOT (End-of-Turn) detection configuration.
    /// Null if provider-backed endpointing is not needed for this agent.
    /// </summary>
    public EotConfig? Eot { get; set; } = new();

    /// <summary>
    /// Realtime session configuration. Required when <see cref="ProcessingMode"/> is Realtime.
    /// </summary>
    public RealtimeAudioConfig? Realtime { get; set; }

    //
    // PROCESSING MODE & I/O
    //

    /// <summary>
    /// Processing mode: Pipeline (HPD STT/chat/TTS) or Realtime (MEAI realtime session).
    /// Default: Pipeline.
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

    /// <summary>
    /// Optional Microsoft.Extensions.AI diagnostics wrappers for provider clients.
    /// </summary>
    public AudioDiagnosticsConfig? Diagnostics { get; set; }

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
            Eot = overrides.Eot ?? Eot,
            Realtime = overrides.Realtime ?? Realtime,

            // Processing
            ProcessingMode = overrides.ProcessingMode,
            IOMode = overrides.IOMode,
            Language = overrides.Language ?? Language,
            Disabled = overrides.Disabled ?? Disabled,
            Diagnostics = overrides.Diagnostics ?? Diagnostics,

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
    /// Creates a copy of the configuration.
    /// </summary>
    public AudioConfig Clone() => new()
    {
        Tts = CloneTts(Tts),
        Stt = CloneStt(Stt),
        Vad = CloneVad(Vad),
        Eot = CloneEot(Eot),
        Realtime = Realtime?.Clone(),
        ProcessingMode = ProcessingMode,
        IOMode = IOMode,
        Language = Language,
        Disabled = Disabled,
        Diagnostics = Diagnostics?.Clone(),
        EnableQuickAnswer = EnableQuickAnswer,
        EnableSpeedAdaptation = EnableSpeedAdaptation,
        EnablePreemptiveGeneration = EnablePreemptiveGeneration,
        PreemptiveGenerationThreshold = PreemptiveGenerationThreshold,
        BackchannelStrategy = BackchannelStrategy,
        MinWordsForInterruption = MinWordsForInterruption,
        EnableFalseInterruptionRecovery = EnableFalseInterruptionRecovery,
        FalseInterruptionTimeout = FalseInterruptionTimeout,
        ResumeFalseInterruption = ResumeFalseInterruption,
        MaxBufferedChunksDuringPause = MaxBufferedChunksDuringPause,
        EnableFillerAudio = EnableFillerAudio,
        FillerSilenceThreshold = FillerSilenceThreshold,
        FillerPhrases = FillerPhrases is null ? null : [.. FillerPhrases],
        FillerSelectionStrategy = FillerSelectionStrategy,
        MaxFillerPlaysPerTurn = MaxFillerPlaysPerTurn,
        FillerVoice = FillerVoice,
        FillerSpeed = FillerSpeed,
        EnableTextFiltering = EnableTextFiltering,
        FilterCodeBlocks = FilterCodeBlocks,
        FilterTables = FilterTables,
        FilterUrls = FilterUrls,
        FilterMarkdownFormatting = FilterMarkdownFormatting,
        FilterEmoji = FilterEmoji
    };

    internal static TtsConfig? CloneTts(TtsConfig? config) => config is null ? null : new TtsConfig
    {
        Voice = config.Voice,
        Speed = config.Speed,
        Pitch = config.Pitch,
        Volume = config.Volume,
        OutputFormat = config.OutputFormat,
        SampleRate = config.SampleRate,
        ModelId = config.ModelId,
        Language = config.Language,
        Provider = config.Provider,
        ProviderOptionsJson = config.ProviderOptionsJson,
        OverrideClient = config.OverrideClient,
        AdditionalProperties = config.AdditionalProperties is null
            ? null
            : new Dictionary<string, object>(config.AdditionalProperties)
    };

    internal static SttConfig? CloneStt(SttConfig? config) => config is null ? null : new SttConfig
    {
        Language = config.Language,
        SpeechSampleRate = config.SpeechSampleRate,
        TextLanguage = config.TextLanguage,
        ModelId = config.ModelId,
        Temperature = config.Temperature,
        ResponseFormat = config.ResponseFormat,
        AdditionalProperties = config.AdditionalProperties is null
            ? null
            : new Dictionary<string, object>(config.AdditionalProperties),
        Provider = config.Provider,
        ProviderOptionsJson = config.ProviderOptionsJson,
        OverrideClient = config.OverrideClient
    };

    internal static VadConfig? CloneVad(VadConfig? config) => config is null ? null : new VadConfig
    {
        MinSpeechDuration = config.MinSpeechDuration,
        MinSilenceDuration = config.MinSilenceDuration,
        PrefixPaddingDuration = config.PrefixPaddingDuration,
        ActivationThreshold = config.ActivationThreshold,
        Provider = config.Provider,
        ProviderOptionsJson = config.ProviderOptionsJson
    };

    internal static EotConfig? CloneEot(EotConfig? config) => config is null ? null : new EotConfig
    {
        Provider = config.Provider,
        SilenceStrategy = config.SilenceStrategy,
        DetectorStrategy = config.DetectorStrategy,
        SilenceFastPathThreshold = config.SilenceFastPathThreshold,
        MinEndpointingDelay = config.MinEndpointingDelay,
        MaxEndpointingDelay = config.MaxEndpointingDelay,
        SilenceBoostMultiplier = config.SilenceBoostMultiplier,
        UseCombinedProbability = config.UseCombinedProbability,
        CustomTrailingWords = config.CustomTrailingWords is null
            ? null
            : new HashSet<string>(config.CustomTrailingWords),
        TrailingWordPenalty = config.TrailingWordPenalty,
        ProviderOptionsJson = config.ProviderOptionsJson
    };

    /// <summary>
    /// Validates configuration values.
    /// </summary>
    public void Validate()
    {
        // Realtime mode is hosted by IRealtimeClientSession; HPD pipeline STT/TTS/VAD are not used.
        // Setting them alongside Realtime mode is a configuration error.
        if (ProcessingMode == AudioProcessingMode.Realtime)
        {
            if (Realtime == null)
                throw new InvalidOperationException(
                    "AudioConfig.Realtime must be set when ProcessingMode is Realtime.");

            if (Stt != null)
                throw new InvalidOperationException(
                    "AudioConfig.Stt cannot be set when ProcessingMode is Realtime. " +
                    "In Realtime mode audio input is sent to the realtime session; " +
                    "remove the Stt configuration or switch to ProcessingMode.Pipeline.");

            if (Tts != null)
                throw new InvalidOperationException(
                    "AudioConfig.Tts cannot be set when ProcessingMode is Realtime. " +
                    "In Realtime mode audio output is received from the realtime session; " +
                    "remove the Tts configuration or switch to ProcessingMode.Pipeline.");

            if (Vad != null)
                throw new InvalidOperationException(
                    "AudioConfig.Vad cannot be set when ProcessingMode is Realtime. " +
                    "In Realtime mode turn handling is configured on the realtime session; " +
                    "remove the Vad configuration or switch to ProcessingMode.Pipeline.");

            return;
        }

        if (Realtime != null)
            throw new InvalidOperationException(
                "AudioConfig.Realtime cannot be set when ProcessingMode is Pipeline. " +
                "Remove the Realtime configuration or switch to ProcessingMode.Realtime.");

        // Validate role configs. Provider selection is only required when a provider
        // factory must be used; injected MEAI clients can use option-only configs.
        Tts?.Validate(requireProvider: false);
        Stt?.Validate(requireProvider: false);
        Vad?.Validate();
        Eot?.Validate();

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

/// <summary>
/// Controls optional Microsoft.Extensions.AI diagnostics wrappers for audio provider clients.
/// </summary>
public class AudioDiagnosticsConfig
{
    /// <summary>Wrap provider clients with Microsoft.Extensions.AI logging decorators.</summary>
    public bool EnableLogging { get; set; }

    /// <summary>Wrap provider clients with Microsoft.Extensions.AI OpenTelemetry decorators.</summary>
    public bool EnableOpenTelemetry { get; set; }

    /// <summary>Include potentially sensitive request/response data in OpenTelemetry events.</summary>
    public bool CaptureSensitiveTelemetry { get; set; }

    /// <summary>Optional OpenTelemetry source name.</summary>
    public string? SourceName { get; set; }

    /// <summary>Creates a copy of this diagnostics config.</summary>
    public AudioDiagnosticsConfig Clone() => new()
    {
        EnableLogging = EnableLogging,
        EnableOpenTelemetry = EnableOpenTelemetry,
        CaptureSensitiveTelemetry = CaptureSensitiveTelemetry,
        SourceName = SourceName
    };
}
